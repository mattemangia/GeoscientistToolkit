// GeoscientistToolkit/UI/AcousticVolume/DamageAnalysisTool.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    /// <summary>
    /// Provides analysis tools specifically for the DamageField in an AcousticVolumeDataset.
    /// </summary>
    public class DamageAnalysisTool : IDatasetTools
    {
        private bool _isCalculating = false;

        // --- Profile Tool State ---
        private List<byte> _profileData;
        private string _profileStats = "No profile selected.";

        // --- Fracture Analysis State ---
        private int _damageThreshold = 128;
        private int _minClusterSize = 50;
        private List<FracturePlane> _fracturePlanes;
        private string _fractureAnalysisStatus = "Ready for analysis.";


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
                if (ImGui.Button("Cancel Drawing"))
                {
                    AcousticInteractionManager.CancelLineDrawing();
                }
            }
            else
            {
                if (ImGui.Button("Select Profile in Viewer..."))
                {
                    AcousticInteractionManager.StartLineDrawing();
                }
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
                    ImGui.PlotLines("Damage Profile", ref floatData[0], floatData.Length, 0, "Distance ->", 0, 255, new Vector2(0, 150));
                }
            }
        }

        private void DrawFractureTab(AcousticVolumeDataset dataset)
        {
            ImGui.Text("Identify and analyze the orientation of major fracture clusters.");
            ImGui.TextWrapped("This tool performs a 3D scan to find connected regions of high damage and calculates their primary orientation.");
            ImGui.Separator();

            ImGui.SliderInt("Damage Threshold", ref _damageThreshold, 1, 255);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Voxels with damage values above this will be considered part of a fracture.");

            ImGui.InputInt("Minimum Cluster Size", ref _minClusterSize);
             if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Ignore small, isolated clusters of damage.");

            if (ImGui.Button("Analyze Full Volume for Fractures", new Vector2(-1, 0)))
            {
                if (!_isCalculating)
                {
                    _isCalculating = true;
                    _fracturePlanes = null;
                    _fractureAnalysisStatus = "Starting analysis...";
                    Task.Run(() => AnalyzeFractureOrientations(dataset));
                }
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
                if (ImGui.BeginTable("FractureTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
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
            int x1 = (int)AcousticInteractionManager.LineStartPoint.X;
            int y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
            int x2 = (int)AcousticInteractionManager.LineEndPoint.X;
            int y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
            int slice_coord = AcousticInteractionManager.LineSliceIndex;
            int viewIndex = AcousticInteractionManager.LineViewIndex;

            _profileData = new List<byte>();

            // Bresenham's line algorithm
            int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
            int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                int volX, volY, volZ;
                bool inBounds = false;
                switch (viewIndex)
                {
                    case 0: // XY View
                        volX = x1; volY = y1; volZ = slice_coord;
                        if (volX >= 0 && volX < volume.Width && volY >= 0 && volY < volume.Height && volZ >= 0 && volZ < volume.Depth) inBounds = true;
                        break;
                    case 1: // XZ View
                        volX = x1; volY = slice_coord; volZ = y1;
                        if (volX >= 0 && volX < volume.Width && volY >= 0 && volY < volume.Height && volZ >= 0 && volZ < volume.Depth) inBounds = true;
                        break;
                    case 2: // YZ View
                        volX = slice_coord; volY = x1; volZ = y1;
                        if (volX >= 0 && volX < volume.Width && volY >= 0 && volY < volume.Height && volZ >= 0 && volZ < volume.Depth) inBounds = true;
                        break;
                    default: volX = volY = volZ = 0; break;
                }

                if (inBounds)
                {
                    _profileData.Add(volume[volX, volY, volZ]);
                }

                if (x1 == x2 && y1 == y2) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x1 += sx; }
                if (e2 <= dx) { err += dx; y1 += sy; }
            }

            if (_profileData.Count > 0)
            {
                _profileStats = $"Points Sampled: {_profileData.Count}\n" +
                                $"Min Damage: {_profileData.Min()}\n" +
                                $"Max Damage: {_profileData.Max()}\n" +
                                $"Average Damage: {_profileData.Average(b => b):F2}";
            }
            else
            {
                _profileStats = "No data points found along the selected line.";
            }
            _isCalculating = false;
        }

        private void AnalyzeFractureOrientations(AcousticVolumeDataset dataset)
        {
            _fracturePlanes = AnalyzeFractureOrientations_Internal(dataset.DamageField, _damageThreshold, _minClusterSize,
                (status) => _fractureAnalysisStatus = status);
            _fractureAnalysisStatus = $"Analysis complete. Found {_fracturePlanes.Count} clusters.";
            _isCalculating = false;
        }
        
        /// <summary>
        /// Public, reusable logic for fracture orientation analysis.
        /// </summary>
        public List<FracturePlane> AnalyzeFractureOrientations_Internal(ChunkedVolume volume, int damageThreshold, int minClusterSize, Action<string> statusUpdate)
        {
            var planes = new List<FracturePlane>();
            int w = volume.Width, h = volume.Height, d = volume.Depth;
            var visited = new bool[w, h, d];
            byte threshold = (byte)damageThreshold;

            for (int z = 0; z < d; z++)
            {
                 statusUpdate?.Invoke($"Scanning slice {z+1}/{d}...");
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
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

                                for (int dz = -1; dz <= 1; dz++)
                                for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
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

                            if (cluster.Count >= minClusterSize)
                            {
                                planes.Add(CalculatePlaneFromCluster(cluster));
                            }
                        }
                    }
                }
            }
            return planes.OrderByDescending(p => p.Size).ToList();
        }
        
        private FracturePlane CalculatePlaneFromCluster(List<Vector3> cluster)
        {
            // 1. Calculate Centroid
            Vector3 centroid = Vector3.Zero;
            foreach (var p in cluster) centroid += p;
            centroid /= cluster.Count;

            // 2. Calculate Covariance Matrix
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in cluster)
            {
                Vector3 r = p - centroid;
                xx += r.X * r.X; xy += r.X * r.Y; xz += r.X * r.Z;
                yy += r.Y * r.Y; yz += r.Y * r.Z; zz += r.Z * r.Z;
            }
            var cov = new Matrix4x4(xx, xy, xz, 0,
                                    xy, yy, yz, 0,
                                    xz, yz, zz, 0,
                                    0,  0,  0,  1);

            // 3. Find eigenvector for the largest eigenvalue (PCA)
            // For simplicity, we use a power iteration method to find the principal eigenvector
            Vector3 orientation = Vector3.Normalize(new Vector3(1, 1, 1));
            for(int i = 0; i < 10; i++)
            {
                orientation = Vector3.Transform(orientation, cov);
                orientation = Vector3.Normalize(orientation);
            }

            // Ensure consistent direction (e.g., points upwards)
            if (orientation.Z < 0) orientation = -orientation;

            return new FracturePlane(cluster.Count, centroid, orientation);
        }

        public class FracturePlane
        {
            public int Size { get; }
            public Vector3 Centroid { get; }
            public Vector3 Orientation { get; }
            public float Dip { get; }
            public float Azimuth { get; }

            public FracturePlane(int size, Vector3 centroid, Vector3 orientation)
            {
                Size = size;
                Centroid = centroid;
                Orientation = orientation;

                // Geological dip and azimuth calculation
                // Dip is the angle from the horizontal plane (90 - angle with Z-axis)
                Dip = 90.0f - (float)(Math.Acos(orientation.Z) * 180.0 / Math.PI);

                // Azimuth is the direction of the horizontal projection of the vector
                Azimuth = (float)(Math.Atan2(orientation.X, orientation.Y) * 180.0 / Math.PI);
                if (Azimuth < 0) Azimuth += 360;
            }
        }
    }
}