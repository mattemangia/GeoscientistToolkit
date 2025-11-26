// GeoscientistToolkit/Data/PhysicoChem/PhysicoChemMesh.cs

using System.Collections.Generic;
using System.Linq;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using Newtonsoft.Json;
using SharpVoronoiLib;
using SharpVoronoiLib.Exceptions;

namespace GeoscientistToolkit.Data.PhysicoChem
{
    /// <summary>
    ///     Represents a single cell in a PhysicoChem mesh.
    /// </summary>
    public class Cell
    {
        /// <summary>
        ///     Unique identifier for the cell.
        /// </summary>
        [JsonProperty]
        public string ID { get; set; }

        /// <summary>
        ///     ID of the material assigned to this cell.
        /// </summary>
        [JsonProperty]
        public string MaterialID { get; set; }

        /// <summary>
        ///     Whether the cell is active in the simulation.
        /// </summary>
        [JsonProperty]
        public bool IsActive { get; set; } = true;

        /// <summary>
        ///     Initial conditions for this cell.
        /// </summary>
        [JsonProperty]
        public InitialConditions InitialConditions { get; set; }

        /// <summary>
        ///     The spatial coordinates of the cell's center.
        /// </summary>
        [JsonProperty]
        public (double X, double Y, double Z) Center { get; set; }

        /// <summary>
        ///     The volume of the cell.
        /// </summary>
        [JsonProperty]
        public double Volume { get; set; }

        /// <summary>
        ///     The vertices of the Voronoi cell.
        /// </summary>
        [JsonIgnore]
        public List<System.Numerics.Vector3> Vertices { get; set; } = new List<System.Numerics.Vector3>();
    }

    /// <summary>
    ///     Represents an object inside the reactor (e.g., Heat Exchanger, Baffle).
    /// </summary>
    public class ReactorObject
    {
        public string Name { get; set; }
        public string MaterialID { get; set; }
        public string Type { get; set; } // "HeatExchanger", "Baffle", "Obstacle"

        // Simplified Geometry (Box or Cylinder for now)
        public (double X, double Y, double Z) Center { get; set; }
        public (double X, double Y, double Z) Size { get; set; } // For Box
        public double Radius { get; set; } // For Cylinder
        public double Height { get; set; } // For Cylinder
        public bool IsCylinder { get; set; }

        public bool IsPointInside(double x, double y, double z)
        {
            if (IsCylinder)
            {
                var dx = x - Center.X;
                var dy = y - Center.Y;
                var distSq = dx * dx + dy * dy;
                return distSq <= Radius * Radius && z >= Center.Z - Height / 2 && z <= Center.Z + Height / 2;
            }
            else
            {
                return x >= Center.X - Size.X / 2 && x <= Center.X + Size.X / 2 &&
                       y >= Center.Y - Size.Y / 2 && y <= Center.Y + Size.Y / 2 &&
                       z >= Center.Z - Size.Z / 2 && z <= Center.Z + Size.Z / 2;
            }
        }
    }

    /// <summary>
    ///     Represents a nucleation point for crystallization or phase change.
    /// </summary>
    public class NucleationPoint
    {
        public string ID { get; set; }
        public (double X, double Y, double Z) Position { get; set; }
        public bool Active { get; set; } = true;
    }

    /// <summary>
    ///     Represents the simulation mesh for a PhysicoChem dataset.
    /// </summary>
    public class PhysicoChemMesh
    {
        /// <summary>
        ///     A collection of all cells in the mesh.
        /// </summary>
        [JsonProperty]
        public Dictionary<string, Cell> Cells { get; set; } = new Dictionary<string, Cell>();

        /// <summary>
        ///     A list of connections between cells.
        /// </summary>
        [JsonProperty]
        public List<(string Cell1_ID, string Cell2_ID)> Connections { get; set; } = new List<(string, string)>();

        /// <summary>
        ///     List of objects embedded in the mesh.
        /// </summary>
        [JsonProperty]
        public List<ReactorObject> ReactorObjects { get; set; } = new List<ReactorObject>();

        /// <summary>
        ///     List of nucleation points.
        /// </summary>
        [JsonProperty]
        public List<NucleationPoint> NucleationPoints { get; set; } = new List<NucleationPoint>();

        /// <summary>
        ///     Embeds a reactor object into the mesh, updating cell materials.
        /// </summary>
        public void EmbedObject(ReactorObject obj)
        {
            ReactorObjects.Add(obj);
            foreach (var cell in Cells.Values)
            {
                if (obj.IsPointInside(cell.Center.X, cell.Center.Y, cell.Center.Z))
                {
                    cell.MaterialID = obj.MaterialID;
                }
            }
        }

        /// <summary>
        ///     Updates the mesh based on all reactor objects (re-applies materials).
        /// </summary>
        public void RefreshObjects()
        {
            // Reset to default if needed? Or just overwrite.
            // Assuming base material is set, we overwrite with object materials.
            foreach (var obj in ReactorObjects)
            {
                foreach (var cell in Cells.Values)
                {
                    if (obj.IsPointInside(cell.Center.X, cell.Center.Y, cell.Center.Z))
                    {
                        cell.MaterialID = obj.MaterialID;
                    }
                }
            }
        }

        /// <summary>
        ///     Splits the mesh into a structured grid of cells.
        /// </summary>
        /// <param name="x_divisions">The number of divisions in the x-dimension.</param>
        /// <param name="y_divisions">The number of divisions in the y-dimension.</param>
        /// <param name="z_divisions">The number of divisions in the z-dimension.</param>
        public void SplitIntoGrid(int x_divisions, int y_divisions, int z_divisions)
        {
            if (Cells.Count == 0) return;

            // Calculate the bounding box of the existing cells
            var minX = Cells.Values.Min(c => c.Center.X - Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);
            var maxX = Cells.Values.Max(c => c.Center.X + Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);
            var minY = Cells.Values.Min(c => c.Center.Y - Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);
            var maxY = Cells.Values.Max(c => c.Center.Y + Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);
            var minZ = Cells.Values.Min(c => c.Center.Z - Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);
            var maxZ = Cells.Values.Max(c => c.Center.Z + Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);

            var newCells = new Dictionary<string, Cell>();
            var cellWidth = (maxX - minX) / x_divisions;
            var cellHeight = (maxY - minY) / y_divisions;
            var cellDepth = (maxZ - minZ) / z_divisions;

            for (int i = 0; i < x_divisions; i++)
            for (int j = 0; j < y_divisions; j++)
            for (int k = 0; k < z_divisions; k++)
            {
                var id = $"C_{i}_{j}_{k}";
                var center = (minX + (i + 0.5) * cellWidth, minY + (j + 0.5) * cellHeight, minZ + (k + 0.5) * cellDepth);
                var volume = cellWidth * cellHeight * cellDepth;

                // Find the original cell that contains the new cell's center
                var originalCell = Cells.Values.FirstOrDefault(c =>
                    c.Center.X - Math.Pow(c.Volume, 1.0 / 3.0) / 2.0 <= center.Item1 && center.Item1 < c.Center.X + Math.Pow(c.Volume, 1.0 / 3.0) / 2.0 &&
                    c.Center.Y - Math.Pow(c.Volume, 1.0 / 3.0) / 2.0 <= center.Item2 && center.Item2 < c.Center.Y + Math.Pow(c.Volume, 1.0 / 3.0) / 2.0 &&
                    c.Center.Z - Math.Pow(c.Volume, 1.0 / 3.0) / 2.0 <= center.Item3 && center.Item3 < c.Center.Z + Math.Pow(c.Volume, 1.0 / 3.0) / 2.0
                );

                newCells[id] = new Cell
                {
                    ID = id,
                    MaterialID = originalCell?.MaterialID ?? "Default",
                    IsActive = originalCell?.IsActive ?? true,
                    InitialConditions = originalCell?.InitialConditions,
                    Center = center,
                    Volume = volume
                };
            }

            Cells = newCells;
            Connections.Clear(); // Connections are invalidated by the split
        }

        public void GenerateVoronoiMesh(BoreholeDataset borehole, int layers, double radius, double height)
        {
            var sites = new List<VoronoiSite>();

            // Add the well location as the center point
            sites.Add(new VoronoiSite((double)borehole.SurfaceCoordinates.X, (double)borehole.SurfaceCoordinates.Y));

            // Generate points in concentric circles around the well
            for (int i = 1; i <= layers; i++)
            {
                var layerRadius = (radius / layers) * i;
                var numPointsInLayer = i * 6; // Increase the number of points in each layer
                for (int j = 0; j < numPointsInLayer; j++)
                {
                    var angle = 2 * Math.PI / numPointsInLayer * j;
                    var x = (double)borehole.SurfaceCoordinates.X + layerRadius * Math.Cos(angle);
                    var y = (double)borehole.SurfaceCoordinates.Y + layerRadius * Math.Sin(angle);
                    sites.Add(new VoronoiSite(x, y));
                }
            }

            // Generate the Voronoi diagram
            var edges = VoronoiPlane.TessellateOnce(sites, 0, 0, 1, 1);

            // Create cells from the Voronoi diagram
            var newCells = new Dictionary<string, Cell>();
            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                var id = $"V_{i}";
                var center = (site.X, site.Y, (double)borehole.Elevation); // Use the borehole elevation for the z-coordinate

                // Estimate cell area and volume
                var area = CalculatePolygonArea(site.ClockwisePoints.ToList());
                var volume = area * height;

                newCells[id] = new Cell
                {
                    ID = id,
                    MaterialID = "Default",
                    IsActive = true,
                    InitialConditions = new InitialConditions(),
                    Center = center,
                    Volume = volume
                };
            }

            Cells = newCells;
            Connections.Clear(); // Connections will be calculated later
        }

        public void FromMesh3DDataset(Mesh3DDataset mesh3D, double height)
        {
            Cells.Clear();
            Connections.Clear();

            if (mesh3D.Faces.Count == 0) return;

            for (int i = 0; i < mesh3D.Faces.Count; i++)
            {
                var face = mesh3D.Faces[i];
                var id = $"F_{i}";

                var v1 = mesh3D.Vertices[face[0]];
                var v2 = mesh3D.Vertices[face[1]];
                var v3 = mesh3D.Vertices[face[2]];

                var center = ((double)(v1.X + v2.X + v3.X) / 3.0,
                              (double)(v1.Y + v2.Y + v3.Y) / 3.0,
                              (double)(v1.Z + v2.Z + v3.Z) / 3.0);

                var area = 0.5 * System.Numerics.Vector3.Cross(v2 - v1, v3 - v1).Length();
                var volume = area * height;

                Cells[id] = new Cell
                {
                    ID = id,
                    MaterialID = "Default",
                    IsActive = true,
                    InitialConditions = new InitialConditions(),
                    Center = center,
                    Volume = volume
                };
            }
        }

        private double CalculateTetrahedronVolume(System.Numerics.Vector3 v1, System.Numerics.Vector3 v2, System.Numerics.Vector3 v3, System.Numerics.Vector3 v4)
        {
            var a = v2 - v1;
            var b = v3 - v1;
            var c = v4 - v1;

            return Math.Abs(System.Numerics.Vector3.Dot(a, System.Numerics.Vector3.Cross(b, c))) / 6.0;
        }

        private double CalculatePolygonArea(List<VoronoiPoint> points)
        {
            if (points == null || points.Count < 3)
                return 0;

            var area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % points.Count];
                area += (p1.X * p2.Y - p2.X * p1.Y);
            }

            return Math.Abs(area) / 2.0;
        }

        public void Generate2DExtrudedVoronoiMesh(int numSites, double width, double depth, double height)
        {
            var sites = new List<VoronoiSite>();
            var random = new System.Random();

            for (int i = 0; i < numSites; i++)
            {
                sites.Add(new VoronoiSite(
                    random.NextDouble() * width - width / 2.0,
                    random.NextDouble() * depth - depth / 2.0
                ));
            }

            VoronoiPlane.TessellateOnce(sites, -width / 2.0, -depth / 2.0, width / 2.0, depth / 2.0);

            Cells.Clear();
            Connections.Clear();

            for (int i = 0; i < sites.Count; i++)
            {
                var site = sites[i];
                var id = $"V_{i}";
                var center = (site.X, site.Y, 0.0);

                var newCell = new Cell
                {
                    ID = id,
                    MaterialID = "Default",
                    IsActive = true,
                    InitialConditions = new InitialConditions(),
                    Center = center,
                };

                var points2D = site.ClockwisePoints;

                // Bottom face
                foreach (var p in points2D)
                {
                    newCell.Vertices.Add(new System.Numerics.Vector3((float)p.X, (float)p.Y, (float)-height / 2.0f));
                }

                // Top face
                foreach (var p in points2D)
                {
                    newCell.Vertices.Add(new System.Numerics.Vector3((float)p.X, (float)p.Y, (float)height / 2.0f));
                }

                newCell.Volume = CalculatePolygonArea(points2D.ToList()) * height;

                Cells[id] = newCell;
            }
        }
    }
}
