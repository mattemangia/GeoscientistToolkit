// GeoscientistToolkit/Business/Photogrammetry/GeoreferencingService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
// Add using statements for MathNet
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Handles georeferencing and coordinate transformations
    /// </summary>
    internal class GeoreferencingService
    {
        private readonly PhotogrammetryProcessingService _service;
        private const double WGS84_A = 6378137.0;
        private const double WGS84_E2 = 0.00669437999014;

        // Define a concrete type instead of using anonymous types
        private class ImageWithObservations
        {
            public PhotogrammetryImage Image { get; set; }
            public List<(Vector3 world, Vector2 pixel)> Observations { get; set; }
        }

        public GeoreferencingService(PhotogrammetryProcessingService service)
        {
            _service = service;
        }

        public async Task<bool> ApplyGeoreferencingAsync(
            PhotogrammetryPointCloud cloud,
            List<PhotogrammetryImage> images)
        {
            return await Task.Run(() => ApplyGeoreferencing(cloud, images));
        }

        private bool ApplyGeoreferencing(
            PhotogrammetryPointCloud cloud,
            List<PhotogrammetryImage> images)
        {
            var georefImages = images.Where(img => img.IsGeoreferenced).ToList();
            var gcpImages = images.Where(img => img.GroundControlPoints.Any(g => g.IsConfirmed)).ToList();

            var uniqueGcpCount = gcpImages
                .SelectMany(img => img.GroundControlPoints)
                .Where(g => g.IsConfirmed)
                .Select(g => g.Id)
                .Distinct()
                .Count();

            Matrix4x4 initialTransform = Matrix4x4.Identity;
            bool hasInitialAlignment = false;

            // Try initial GPS alignment
            if (georefImages.Count >= 3)
            {
                hasInitialAlignment = TryGPSAlignment(georefImages, images, cloud, out initialTransform);
            }
            else if (uniqueGcpCount < 3)
            {
                _service.Log("Georeferencing requires at least 3 GPS-tagged images or 3 confirmed GCPs. " +
                           "Model will remain in relative coordinates.");
                return false;
            }

            // Apply initial transformation if available
            if (hasInitialAlignment)
            {
                ApplyTransformToCloud(cloud, initialTransform);
                ApplyTransformToImages(images, initialTransform);
            }

            // Refine with GCPs if available
            if (uniqueGcpCount >= 3)
            {
                _service.Log($"Refining the model using {uniqueGcpCount} unique Ground Control Points...");

                if (RefineWithGCPs(cloud, gcpImages))
                {
                    _service.Log("Model successfully refined with GCPs.");
                }
                else
                {
                    _service.Log("Warning: GCP refinement failed.");
                }
            }
            else if (hasInitialAlignment)
            {
                _service.Log("No GCPs found. The model is georeferenced but not refined.");
            }

            ComputePointCloudBounds(cloud);
            return true;
        }

        private bool TryGPSAlignment(
            List<PhotogrammetryImage> georefImages,
            List<PhotogrammetryImage> allImages,
            PhotogrammetryPointCloud cloud,
            out Matrix4x4 transform)
        {
            _service.Log($"Applying initial georeferencing using {georefImages.Count} GPS-tagged images...");

            var worldPositions = georefImages
                .Select(img => LatLonAltToECEF(
                    img.Latitude.Value,
                    img.Longitude.Value,
                    img.Altitude ?? 0.0))
                .ToList();

            var reconPositions = georefImages
                .Select(img => img.GlobalPose.Translation)
                .ToList();

            if (ComputeSimilarityTransform(reconPositions, worldPositions, out transform))
            {
                return true;
            }

            _service.Log("Warning: Could not compute a valid initial transformation from GPS data.");
            return false;
        }

        private bool RefineWithGCPs(
            PhotogrammetryPointCloud cloud,
            List<PhotogrammetryImage> gcpImages)
        {
            // Use global similarity transformation for refinement
            return RefineWithGlobalSimilarity(cloud, gcpImages);
        }

        private bool RefineWithGlobalSimilarity(
            PhotogrammetryPointCloud cloud,
            List<PhotogrammetryImage> gcpImages)
        {
            var imagesWithObs = gcpImages
                .Select(img => new ImageWithObservations
                {
                    Image = img,
                    Observations = img.GroundControlPoints
                        .Where(g => g.IsConfirmed && g.WorldPosition.HasValue)
                        .Select(g => (world: g.WorldPosition.Value, pixel: g.ImagePosition))
                        .ToList()
                })
                .Where(x => x.Observations.Count >= 3)
                .ToList();

            if (imagesWithObs.Count == 0)
            {
                _service.Log("Global similarity skipped: no images with ≥3 confirmed GCPs.");
                return false;
            }

            _service.Log($"Running global similarity optimization on {imagesWithObs.Count} images...");

            // Initialize parameters: identity (scale=1, rotation=I, translation=0)
            float[] parameters = new float[7]; // [log(s), rx, ry, rz, tx, ty, tz]

            // Run Levenberg-Marquardt optimization
            OptimizeSimilarityTransform(parameters, imagesWithObs);

            // Apply the optimized transformation
            var finalTransform = BuildSimilarityMatrix(parameters);
            ApplyTransformToImages(gcpImages.Distinct().ToList(), finalTransform);
            ApplyTransformToCloud(cloud, finalTransform);

            _service.Log("Global similarity refinement complete.");
            return true;
        }

        private void OptimizeSimilarityTransform(
            float[] parameters,
            List<ImageWithObservations> imagesWithObs)
        {
            const int maxIterations = 30;
            double lambda = 0.01;
            const float stepSize = 1e-4f;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                var (H, g, currentCost) = ComputeJacobianAndResiduals(
                    parameters, imagesWithObs, stepSize);

                // Apply Levenberg-Marquardt damping
                for (int d = 0; d < 7; d++)
                    H[d, d] += lambda;

                // Solve for parameter update
                var deltaParams = SolveLinearSystem(H, g);
                if (deltaParams == null)
                {
                    lambda *= 10.0;
                    continue;
                }

                // Try new parameters
                float[] trialParams = new float[7];
                for (int i = 0; i < 7; i++)
                    trialParams[i] = parameters[i] + (float)deltaParams[i];

                double newCost = ComputeCost(trialParams, imagesWithObs);

                if (newCost < currentCost)
                {
                    // Accept the step
                    for (int i = 0; i < 7; i++)
                        parameters[i] = trialParams[i];

                    lambda = System.Math.Max(1e-6, lambda * 0.5);

                    // Check for convergence
                    double stepNorm = 0;
                    for (int i = 0; i < 7; i++)
                        stepNorm += deltaParams[i] * deltaParams[i];

                    if (stepNorm < 1e-12 || System.Math.Abs(currentCost - newCost) < 1e-6)
                        break;
                }
                else
                {
                    lambda *= 5.0;
                }
            }
        }

        private (double[,] H, double[] g, double cost) ComputeJacobianAndResiduals(
            float[] parameters,
            List<ImageWithObservations> imagesWithObs,
            float stepSize)
        {
            double[,] H = new double[7, 7];
            double[] g = new double[7];
            double cost = 0.0;

            foreach (var pack in imagesWithObs)
            {
                foreach (var obs in pack.Observations)
                {
                    var projection = ProjectWithSimilarity(obs.world, pack.Image, parameters);
                    float residualU = projection.X - obs.pixel.X;
                    float residualV = projection.Y - obs.pixel.Y;
                    cost += residualU * residualU + residualV * residualV;

                    // Compute numerical Jacobian
                    float[] jacobianU = new float[7];
                    float[] jacobianV = new float[7];

                    for (int k = 0; k < 7; k++)
                    {
                        float[] perturbedParams = new float[7];
                        Array.Copy(parameters, perturbedParams, 7);
                        perturbedParams[k] += stepSize;

                        var perturbedProj = ProjectWithSimilarity(obs.world, pack.Image, perturbedParams);
                        jacobianU[k] = (perturbedProj.X - projection.X) / stepSize;
                        jacobianV[k] = (perturbedProj.Y - projection.Y) / stepSize;
                    }

                    // Accumulate J^T * J and J^T * r
                    for (int row = 0; row < 7; row++)
                    {
                        g[row] += jacobianU[row] * residualU + jacobianV[row] * residualV;

                        for (int col = 0; col < 7; col++)
                        {
                            H[row, col] += jacobianU[row] * jacobianU[col] +
                                          jacobianV[row] * jacobianV[col];
                        }
                    }
                }
            }

            return (H, g, cost);
        }

        private double ComputeCost(float[] parameters, List<ImageWithObservations> imagesWithObs)
        {
            double cost = 0.0;

            foreach (var pack in imagesWithObs)
            {
                foreach (var obs in pack.Observations)
                {
                    var proj = ProjectWithSimilarity(obs.world, pack.Image, parameters);
                    float du = proj.X - obs.pixel.X;
                    float dv = proj.Y - obs.pixel.Y;
                    cost += du * du + dv * dv;
                }
            }

            return cost;
        }

        private Vector2 ProjectWithSimilarity(
            Vector3 worldPoint,
            PhotogrammetryImage image,
            float[] parameters)
        {
            var similarity = BuildSimilarityMatrix(parameters);
            var newPose = similarity * image.GlobalPose;

            Matrix4x4.Invert(newPose, out var view);
            var cameraPoint = Vector4.Transform(new Vector4(worldPoint, 1f), view);
            var projected = Vector4.Transform(cameraPoint, image.IntrinsicMatrix);

            if (System.Math.Abs(projected.W) < 1e-8f)
                return new Vector2(-1, -1);

            return new Vector2(projected.X / projected.W, projected.Y / projected.W);
        }

        private Matrix4x4 BuildSimilarityMatrix(float[] parameters)
        {
            float sigma = parameters[0];
            float scale = MathF.Exp(sigma);
            var rotationVector = new Vector3(parameters[1], parameters[2], parameters[3]);
            var translation = new Vector3(parameters[4], parameters[5], parameters[6]);

            var rotation = Matrix3x3.Rodrigues(rotationVector);

            var similarity = new Matrix4x4(
                scale * rotation.M11, scale * rotation.M12, scale * rotation.M13, 0,
                scale * rotation.M21, scale * rotation.M22, scale * rotation.M23, 0,
                scale * rotation.M31, scale * rotation.M32, scale * rotation.M33, 0,
                0, 0, 0, 1
            );

            similarity.Translation = translation;
            return similarity;
        }

        private double[] SolveLinearSystem(double[,] A, double[] b)
        {
            // Solve A * x = -b using Cholesky decomposition
            int n = 7;
            double[,] L = new double[n, n];
            double[] x = new double[n];
            double[] rhs = new double[n];

            for (int i = 0; i < n; i++)
                rhs[i] = -b[i];

            // Cholesky decomposition
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double sum = A[i, j];
                    for (int k = 0; k < j; k++)
                        sum -= L[i, k] * L[j, k];

                    if (i == j)
                    {
                        if (sum <= 1e-12)
                            return null;
                        L[i, j] = System.Math.Sqrt(sum);
                    }
                    else
                    {
                        L[i, j] = sum / L[j, j];
                    }
                }
            }

            // Forward substitution
            double[] y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = rhs[i];
                for (int k = 0; k < i; k++)
                    sum -= L[i, k] * y[k];
                y[i] = sum / L[i, i];
            }

            // Backward substitution
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = y[i];
                for (int k = i + 1; k < n; k++)
                    sum -= L[k, i] * x[k];
                x[i] = sum / L[i, i];
            }

            return x;
        }

        private static Matrix3x3 ToMatrix3x3(Matrix<double> m)
        {
            return new Matrix3x3(
                (float)m[0, 0], (float)m[0, 1], (float)m[0, 2],
                (float)m[1, 0], (float)m[1, 1], (float)m[1, 2],
                (float)m[2, 0], (float)m[2, 1], (float)m[2, 2]
            );
        }

        private bool ComputeSimilarityTransform(
            List<Vector3> source,
            List<Vector3> target,
            out Matrix4x4 transform)
        {
            transform = Matrix4x4.Identity;

            if (source.Count != target.Count || source.Count < 3)
                return false;

            int n = source.Count;

            // Compute centroids
            var sourceCentroid = new Vector3(
                source.Average(p => p.X),
                source.Average(p => p.Y),
                source.Average(p => p.Z));

            var targetCentroid = new Vector3(
                target.Average(p => p.X),
                target.Average(p => p.Y),
                target.Average(p => p.Z));

            // --- START OF CORRECTION ---
            // Build cross-covariance matrix using MathNet
            var H = DenseMatrix.Create(3, 3, 0.0);
            for (int i = 0; i < n; i++)
            {
                var s = source[i] - sourceCentroid;
                var t = target[i] - targetCentroid;
                var s_vec = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new double[] { s.X, s.Y, s.Z });
                var t_vec = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(new double[] { t.X, t.Y, t.Z });
                H.Add(s_vec.OuterProduct(t_vec), H);
            }

            // Compute rotation using SVD
            var svd = H.Svd(true);
            var U = svd.U;
            var V = svd.VT.Transpose();
            var R_mathnet = V * U.Transpose();

            // Ensure proper rotation (no reflection)
            if (R_mathnet.Determinant() < 0)
            {
                var V_mutable = DenseMatrix.OfMatrix(V);
                V_mutable.SetColumn(2, V.Column(2) * -1);
                R_mathnet = V_mutable * U.Transpose();
            }

            var R = ToMatrix3x3(R_mathnet);
            // --- END OF CORRECTION ---

            // Compute optimal scale
            var sourceCentered = source.Select(p => p - sourceCentroid).ToList();
            var targetCentered = target.Select(p => p - targetCentroid).ToList();
            float numerator = 0.0f;
            float denominator = 0.0f;

            for (int i = 0; i < n; i++)
            {
                numerator += Vector3.Dot(targetCentered[i], R * sourceCentered[i]);
                denominator += sourceCentered[i].LengthSquared();
            }
            float scale = (denominator > 1e-8) ? numerator / denominator : 1.0f;

            // Compute translation
            var T = targetCentroid - scale * (R * sourceCentroid);

            // Build final transformation matrix
            transform = new Matrix4x4(
                scale * R.M11, scale * R.M12, scale * R.M13, 0,
                scale * R.M21, scale * R.M22, scale * R.M23, 0,
                scale * R.M31, scale * R.M32, scale * R.M33, 0,
                T.X, T.Y, T.Z, 1
            );

            return true;
        }

        private Vector3 LatLonAltToECEF(double latitude, double longitude, double altitude)
        {
            double lat = latitude * System.Math.PI / 180.0;
            double lon = longitude * System.Math.PI / 180.0;

            double sinLat = System.Math.Sin(lat);
            double cosLat = System.Math.Cos(lat);
            double N = WGS84_A / System.Math.Sqrt(1 - WGS84_E2 * sinLat * sinLat);

            double x = (N + altitude) * cosLat * System.Math.Cos(lon);
            double y = (N + altitude) * cosLat * System.Math.Sin(lon);
            double z = (N * (1 - WGS84_E2) + altitude) * sinLat;

            return new Vector3((float)x, (float)y, (float)z);
        }

        private void ApplyTransformToCloud(PhotogrammetryPointCloud cloud, Matrix4x4 transform)
        {
            for (int i = 0; i < cloud.Points.Count; i++)
            {
                var pt = cloud.Points[i];
                pt.Position = Vector3.Transform(pt.Position, transform);
                cloud.Points[i] = pt;
            }
        }

        private void ApplyTransformToImages(List<PhotogrammetryImage> images, Matrix4x4 transform)
        {
            foreach (var image in images)
            {
                image.GlobalPose = image.GlobalPose * transform;
            }
        }

        private void ComputePointCloudBounds(PhotogrammetryPointCloud cloud)
        {
            if (cloud.Points.Count == 0)
            {
                cloud.BoundingBoxMin = cloud.BoundingBoxMax = Vector3.Zero;
                return;
            }

            cloud.BoundingBoxMin = new Vector3(float.MaxValue);
            cloud.BoundingBoxMax = new Vector3(float.MinValue);

            foreach (var point in cloud.Points)
            {
                cloud.BoundingBoxMin = Vector3.Min(cloud.BoundingBoxMin, point.Position);
                cloud.BoundingBoxMax = Vector3.Max(cloud.BoundingBoxMax, point.Position);
            }
        }
    }
}