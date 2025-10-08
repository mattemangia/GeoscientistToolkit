// GeoscientistToolkit/UI/AcousticVolume/DamageAnalysisTool.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI.AcousticVolume;

/// <summary>
///     Provides analysis tools specifically for the DamageField in an AcousticVolumeDataset.
/// </summary>
public class DamageAnalysisTool : IDatasetTools
{
    // --- Fracture Analysis State ---
    private int _damageThreshold = 128;
    private string _fractureAnalysisStatus = "Ready for analysis.";
    private List<FracturePlane> _fracturePlanes;
    private bool _isCalculating;
    private int _minClusterSize = 50;

    // --- Profile Tool State ---
    private List<byte> _profileData;
    private string _profileStats = "No profile selected.";


    public void Draw(Dataset dataset)
    {
        if (dataset is not AcousticVolumeDataset ad)
        {
            ImGui.TextDisabled("This tool requires an Acoustic Volume Dataset.");
            return;
        }

        if (ad.DamageField == null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Warning: No Damage Field is present in this dataset.");
            return;
        }

        // Check for new line data from the viewer on every frame for the profile tool
        if (AcousticInteractionManager.HasNewLine)
        {
            AcousticInteractionManager.HasNewLine = false; // Consume the event
            if (!_isCalculating)
            {
                _isCalculating = true;
                Task.Run(() => CalculateDamageProfile(ad));
            }
        }

        if (ImGui.BeginTabBar("DamageAnalysisTabs"))
        {
            if (ImGui.BeginTabItem("Damage Profile"))
            {
                DrawProfileTab(ad);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Fracture Orientation"))
            {
                DrawFractureTab(ad);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawProfileTab(AcousticVolumeDataset dataset)
    {
        ImGui.Text("Analyze damage values along a user-defined line.");
        ImGui.Separator();

        if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Drawing mode active in viewer window...");
            if (ImGui.Button("Cancel Drawing")) AcousticInteractionManager.CancelLineDrawing();
        }
        else
        {
            if (ImGui.Button("Select Profile in Viewer...")) AcousticInteractionManager.StartLineDrawing();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Results:");

        if (_isCalculating)
        {
            ImGui.Text("Calculating...");
        }
        else
        {
            ImGui.TextWrapped(_profileStats);
            if (_profileData != null && _profileData.Count > 0)
            {
                var floatData = _profileData.Select(b => (float)b).ToArray();
                ImGui.PlotLines("Damage Profile", ref floatData[0], floatData.Length, 0, "Distance ->", 0, 255,
                    new Vector2(0, 150));
            }
        }
    }

    private void DrawFractureTab(AcousticVolumeDataset dataset)
    {
        ImGui.Text("Identify and analyze the orientation of major fracture clusters.");
        ImGui.TextWrapped(
            "This tool performs a 3D scan to find connected regions of high damage and calculates their primary orientation.");
        ImGui.Separator();

        ImGui.SliderInt("Damage Threshold", ref _damageThreshold, 1, 255);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Voxels with damage values above this will be considered part of a fracture.");

        ImGui.InputInt("Minimum Cluster Size", ref _minClusterSize);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Ignore small, isolated clusters of damage.");

        if (ImGui.Button("Analyze Full Volume for Fractures", new Vector2(-1, 0)))
            if (!_isCalculating)
            {
                _isCalculating = true;
                _fracturePlanes = null;
                _fractureAnalysisStatus = "Starting analysis...";
                Task.Run(() => AnalyzeFractureOrientations(dataset));
            }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Results:");

        if (_isCalculating)
        {
            ImGui.TextWrapped($"Status: {_fractureAnalysisStatus}");
        }
        else if (_fracturePlanes != null)
        {
            ImGui.Text($"Found {_fracturePlanes.Count} potential fracture planes.");
            if (ImGui.BeginTable("FractureTable", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Size (voxels)");
                ImGui.TableSetupColumn("Centroid (X,Y,Z)");
                ImGui.TableSetupColumn("Orientation (dX,dY,dZ)");
                ImGui.TableSetupColumn("Dip/Azimuth");
                ImGui.TableHeadersRow();

                foreach (var plane in _fracturePlanes)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text($"{plane.Size}");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text($"{plane.Centroid.X:F0}, {plane.Centroid.Y:F0}, {plane.Centroid.Z:F0}");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.Text($"{plane.Orientation.X:F2}, {plane.Orientation.Y:F2}, {plane.Orientation.Z:F2}");
                    ImGui.TableSetColumnIndex(3);
                    ImGui.Text($"{plane.Dip:F1}° / {plane.Azimuth:F1}°");
                }

                ImGui.EndTable();
            }
        }
        else
        {
            ImGui.TextWrapped($"Status: {_fractureAnalysisStatus}");
        }
    }


    private void CalculateDamageProfile(AcousticVolumeDataset dataset)
    {
        var volume = dataset.DamageField;
        if (volume == null)
        {
            _profileStats = "Damage field is not available.";
            _isCalculating = false;
            return;
        }

        // Get coordinates from the interaction manager
        var x1 = (int)AcousticInteractionManager.LineStartPoint.X;
        var y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
        var x2 = (int)AcousticInteractionManager.LineEndPoint.X;
        var y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
        var slice_coord = AcousticInteractionManager.LineSliceIndex;
        var viewIndex = AcousticInteractionManager.LineViewIndex;

        _profileData = new List<byte>();

        // Bresenham's line algorithm
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;

        while (true)
        {
            int volX, volY, volZ;
            var inBounds = false;
            switch (viewIndex)
            {
                case 0: // XY View
                    volX = x1;
                    volY = y1;
                    volZ = slice_coord;
                    if (volX >= 0 && volX < volume.Width && volY >= 0 && volY < volume.Height && volZ >= 0 &&
                        volZ < volume.Depth) inBounds = true;
                    break;
                case 1: // XZ View
                    volX = x1;
                    volY = slice_coord;
                    volZ = y1;
                    if (volX >= 0 && volX < volume.Width && volY >= 0 && volY < volume.Height && volZ >= 0 &&
                        volZ < volume.Depth) inBounds = true;
                    break;
                case 2: // YZ View
                    volX = slice_coord;
                    volY = x1;
                    volZ = y1;
                    if (volX >= 0 && volX < volume.Width && volY >= 0 && volY < volume.Height && volZ >= 0 &&
                        volZ < volume.Depth) inBounds = true;
                    break;
                default: volX = volY = volZ = 0; break;
            }

            if (inBounds) _profileData.Add(volume[volX, volY, volZ]);

            if (x1 == x2 && y1 == y2) break;
            e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }

        if (_profileData.Count > 0)
            _profileStats = $"Points Sampled: {_profileData.Count}\n" +
                            $"Min Damage: {_profileData.Min()}\n" +
                            $"Max Damage: {_profileData.Max()}\n" +
                            $"Average Damage: {_profileData.Average(b => b):F2}";
        else
            _profileStats = "No data points found along the selected line.";
        _isCalculating = false;
    }

    private void AnalyzeFractureOrientations(AcousticVolumeDataset dataset)
    {
        _fracturePlanes = AnalyzeFractureOrientations_Internal(dataset.DamageField, _damageThreshold, _minClusterSize,
            status => _fractureAnalysisStatus = status);
        _fractureAnalysisStatus = $"Analysis complete. Found {_fracturePlanes.Count} clusters.";
        _isCalculating = false;
    }

    /// <summary>
    ///     Public, reusable logic for fracture orientation analysis.
    /// </summary>
    public List<FracturePlane> AnalyzeFractureOrientations_Internal(ChunkedVolume volume, int damageThreshold,
        int minClusterSize, Action<string> statusUpdate)
    {
        var planes = new List<FracturePlane>();
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        var visited = new bool[w, h, d];
        var threshold = (byte)damageThreshold;

        for (var z = 0; z < d; z++)
        {
            statusUpdate?.Invoke($"Scanning slice {z + 1}/{d}...");
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (!visited[x, y, z] && volume[x, y, z] >= threshold)
                {
                    var cluster = new List<Vector3>();
                    var q = new Queue<Tuple<int, int, int>>();

                    q.Enqueue(new Tuple<int, int, int>(x, y, z));
                    visited[x, y, z] = true;

                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        cluster.Add(new Vector3(p.Item1, p.Item2, p.Item3));

                        for (var dz = -1; dz <= 1; dz++)
                        for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            int nx = p.Item1 + dx, ny = p.Item2 + dy, nz = p.Item3 + dz;

                            if (nx >= 0 && nx < w && ny >= 0 && ny < h && nz >= 0 && nz < d &&
                                !visited[nx, ny, nz] && volume[nx, ny, nz] >= threshold)
                            {
                                visited[nx, ny, nz] = true;
                                q.Enqueue(new Tuple<int, int, int>(nx, ny, nz));
                            }
                        }
                    }

                    if (cluster.Count >= minClusterSize) planes.Add(CalculatePlaneFromCluster(cluster));
                }
        }

        return planes.OrderByDescending(p => p.Size).ToList();
    }

    /// <summary>
    ///     Calculates the best-fit plane for a cluster of points using Principal Component Analysis (PCA).
    ///     This is a robust, production-ready replacement for the simplified power iteration method.
    /// </summary>
    /// <param name="cluster">A list of 3D points representing the damage cluster.</param>
    /// <returns>A FracturePlane object describing the cluster's properties and orientation.</returns>
    private FracturePlane CalculatePlaneFromCluster(List<Vector3> cluster)
    {
        // 1. Calculate the centroid (mean position) of the cluster.
        var centroid = Vector3.Zero;
        foreach (var p in cluster) centroid += p;
        centroid /= cluster.Count;

        // 2. Construct the 3x3 covariance matrix.
        // This matrix describes the shape and orientation of the point cloud.
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in cluster)
        {
            var r = p - centroid;
            xx += r.X * r.X;
            xy += r.X * r.Y;
            xz += r.X * r.Z;
            yy += r.Y * r.Y;
            yz += r.Y * r.Z;
            zz += r.Z * r.Z;
        }

        var cov = new Matrix4x4(
            xx, xy, xz, 0,
            xy, yy, yz, 0,
            xz, yz, zz, 0,
            0, 0, 0, 1);

        // It's good practice to normalize the covariance matrix.
        if (cluster.Count > 1) cov = cov * (1.0f / (cluster.Count - 1));

        // 3. Find the eigenvectors and eigenvalues of the covariance matrix.
        // The eigenvector corresponding to the SMALLEST eigenvalue is the normal
        // to the plane that best fits the data. The other two eigenvectors
        // represent the principal axes of the plane itself.
        var (eigenvalues, eigenvectors) = Eigen.Solve(cov);

        // Find the index of the smallest eigenvalue.
        var minIndex = 0;
        if (eigenvalues.Y < eigenvalues.X) minIndex = 1;
        if (eigenvalues.Z < eigenvalues[minIndex]) minIndex = 2;

        // The orientation is the normal vector to the best-fit plane.
        var orientation = eigenvectors[minIndex];

        // Ensure consistent direction (e.g., points upwards in the Z-axis) for geological convention.
        if (orientation.Z < 0) orientation = -orientation;

        return new FracturePlane(cluster.Count, centroid, orientation);
    }

    public class FracturePlane
    {
        public FracturePlane(int size, Vector3 centroid, Vector3 orientation)
        {
            Size = size;
            Centroid = centroid;
            Orientation = Vector3.Normalize(orientation);

            // Geological dip and azimuth calculation from the plane's normal vector.
            // Dip is the angle from the horizontal plane (90 degrees - angle with vertical Z-axis).
            Dip = 90.0f - (float)(Math.Acos(Orientation.Z) * 180.0 / Math.PI);

            // Azimuth is the compass direction of the horizontal projection of the normal vector.
            Azimuth = (float)(Math.Atan2(Orientation.X, Orientation.Y) * 180.0 / Math.PI);
            if (Azimuth < 0) Azimuth += 360;
        }

        public int Size { get; }
        public Vector3 Centroid { get; }
        public Vector3 Orientation { get; }
        public float Dip { get; }
        public float Azimuth { get; }
    }
}

/// <summary>
///     A helper class to compute the eigensystem of a 3x3 symmetric matrix.
///     Uses the robust Jacobi eigenvalue algorithm, which is suitable for production use.
/// </summary>
internal static class Eigen
{
    private const int MaxSweeps = 15;
    private const float Epsilon = 1e-10f;

    /// <summary>
    ///     Solves the eigensystem for a 3x3 symmetric matrix.
    /// </summary>
    /// <param name="matrix">The symmetric 3x3 matrix (passed as the upper-left of a Matrix4x4).</param>
    /// <returns>A tuple containing eigenvalues (Vector3) and an array of corresponding eigenvectors (Vector3[]).</returns>
    public static (Vector3 eigenvalues, Vector3[] eigenvectors) Solve(Matrix4x4 matrix)
    {
        // Extract the 3x3 part into a mutable array.
        var a = new float[3, 3]
        {
            { matrix.M11, matrix.M12, matrix.M13 },
            { matrix.M21, matrix.M22, matrix.M23 },
            { matrix.M31, matrix.M32, matrix.M33 }
        };

        // Initialize eigenvectors as the identity matrix.
        var v = new float[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        // Initial eigenvalues are the diagonal elements.
        var d = new float[3] { a[0, 0], a[1, 1], a[2, 2] };

        for (var sweep = 0; sweep < MaxSweeps; sweep++)
        {
            var sumOffDiagonal = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (sumOffDiagonal < Epsilon) // Converged if off-diagonal elements are near zero.
                break;

            // Perform a Jacobi rotation for each off-diagonal element.
            Rotate(a, v, 0, 1, d);
            Rotate(a, v, 0, 2, d);
            Rotate(a, v, 1, 2, d);
        }

        var eigenvalues = new Vector3(d[0], d[1], d[2]);
        var eigenvectors = new[]
        {
            Vector3.Normalize(new Vector3(v[0, 0], v[1, 0], v[2, 0])),
            Vector3.Normalize(new Vector3(v[0, 1], v[1, 1], v[2, 1])),
            Vector3.Normalize(new Vector3(v[0, 2], v[1, 2], v[2, 2]))
        };

        return (eigenvalues, eigenvectors);
    }

    /// <summary>
    ///     Performs a single Jacobi rotation to zero out the a[i, j] element.
    /// </summary>
    private static void Rotate(float[,] a, float[,] v, int i, int j, float[] d)
    {
        var g = 100.0f * Math.Abs(a[i, j]);

        // Avoid division by zero and unnecessary rotations.
        if (g < Epsilon)
            return;

        var h = d[j] - d[i];
        float t;

        if (Math.Abs(h) + g == Math.Abs(h))
        {
            t = a[i, j] / h;
        }
        else
        {
            var theta = 0.5f * h / a[i, j];
            t = 1.0f / (Math.Abs(theta) + (float)Math.Sqrt(1.0f + theta * theta));
            if (theta < 0.0f) t = -t;
        }

        var c = 1.0f / (float)Math.Sqrt(1 + t * t);
        var s = t * c;
        var tau = s / (1.0f + c);
        h = t * a[i, j];

        // Update eigenvalues.
        d[i] -= h;
        d[j] += h;
        a[i, j] = 0.0f;

        // Update the matrix 'a' for the next rotations.
        for (var k = 0; k < 3; k++)
            if (k != i && k != j)
            {
                var g_ik = a[k, i];
                var g_jk = a[k, j];
                a[k, i] = g_ik - s * (g_jk + tau * g_ik);
                a[k, j] = g_jk + s * (g_ik - tau * g_jk);
            }

        // Update the eigenvector matrix 'v'.
        for (var k = 0; k < 3; k++)
        {
            var v_ik = v[k, i];
            var v_jk = v[k, j];
            v[k, i] = v_ik - s * (v_jk + tau * v_ik);
            v[k, j] = v_jk + s * (v_ik - tau * v_jk);
        }
    }
}