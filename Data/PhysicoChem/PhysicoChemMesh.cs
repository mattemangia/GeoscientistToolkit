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
    ///     Represents an object inside the reactor (e.g., Heat Exchanger, Baffle, ORC components).
    /// </summary>
    public class ReactorObject
    {
        public string Name { get; set; }
        public string MaterialID { get; set; }
        public string Type { get; set; } // "HeatExchanger", "Baffle", "Obstacle", "Condenser", "Evaporator", "Turbine", "Pump"

        // Simplified Geometry (Box or Cylinder for now)
        public (double X, double Y, double Z) Center { get; set; }
        public (double X, double Y, double Z) Size { get; set; } // For Box
        public double Radius { get; set; } // For Cylinder
        public double Height { get; set; } // For Cylinder
        public bool IsCylinder { get; set; }

        // ORC-specific parameters
        public ORCComponentParameters? ORCParams { get; set; }

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

        /// <summary>
        /// Gets the top Z coordinate of the object
        /// </summary>
        public double GetTopZ()
        {
            if (IsCylinder)
                return Center.Z + Height / 2;
            else
                return Center.Z + Size.Z / 2;
        }

        /// <summary>
        /// Gets the bottom Z coordinate of the object
        /// </summary>
        public double GetBottomZ()
        {
            if (IsCylinder)
                return Center.Z - Height / 2;
            else
                return Center.Z - Size.Z / 2;
        }

        /// <summary>
        /// Checks if the object contacts the surface (top of mesh) given mesh bounds.
        /// Heat exchangers must contact surface for fluid inlet/outlet.
        /// </summary>
        /// <param name="meshTopZ">The Z coordinate of the mesh top surface</param>
        /// <param name="tolerance">Tolerance for contact check</param>
        public bool ContactsSurface(double meshTopZ, double tolerance = 0.01)
        {
            double objectTopZ = GetTopZ();
            return Math.Abs(objectTopZ - meshTopZ) <= tolerance || objectTopZ >= meshTopZ;
        }

        /// <summary>
        /// Validates heat exchanger placement: must contact surface for inlet/outlet.
        /// </summary>
        /// <param name="meshTopZ">The Z coordinate of the mesh top surface</param>
        /// <param name="errorMessage">Error message if validation fails</param>
        public bool ValidateHeatExchangerPlacement(double meshTopZ, out string errorMessage)
        {
            errorMessage = null;

            if (Type != "HeatExchanger")
            {
                // Non-heat exchanger objects don't require surface contact
                return true;
            }

            if (!ContactsSurface(meshTopZ))
            {
                errorMessage = $"Heat exchanger '{Name}' must contact the surface (top of mesh at Z={meshTopZ:F2}). " +
                              $"Current top is at Z={GetTopZ():F2}. Adjust position or height.";
                return false;
            }

            return true;
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
        ///     Gets the top Z coordinate of the mesh (surface level).
        /// </summary>
        public double GetMeshTopZ()
        {
            if (Cells.Count == 0) return 0;
            return Cells.Values.Max(c => c.Center.Z + Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);
        }

        /// <summary>
        ///     Gets the bottom Z coordinate of the mesh.
        /// </summary>
        public double GetMeshBottomZ()
        {
            if (Cells.Count == 0) return 0;
            return Cells.Values.Min(c => c.Center.Z - Math.Pow(c.Volume, 1.0 / 3.0) / 2.0);
        }

        /// <summary>
        ///     Embeds a reactor object into the mesh, updating cell materials.
        ///     Validates that heat exchangers contact the surface.
        /// </summary>
        /// <param name="obj">The reactor object to embed</param>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if embedding succeeded, false if validation failed</returns>
        public bool TryEmbedObject(ReactorObject obj, out string errorMessage)
        {
            errorMessage = null;

            // Validate heat exchanger placement
            double meshTopZ = GetMeshTopZ();
            if (!obj.ValidateHeatExchangerPlacement(meshTopZ, out errorMessage))
            {
                return false;
            }

            EmbedObject(obj);
            return true;
        }

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

    /// <summary>
    /// ORC (Organic Rankine Cycle) component parameters for reactor objects.
    /// Used when Type is "Condenser", "Evaporator", "Turbine", or "Pump".
    /// </summary>
    public class ORCComponentParameters
    {
        /// <summary>Component type for ORC circuit</summary>
        public string ComponentType { get; set; } = "Generic"; // Evaporator, Turbine, Condenser, Pump, Recuperator

        // === Thermal Parameters ===
        /// <summary>Operating temperature (°C)</summary>
        public double Temperature { get; set; } = 30.0;

        /// <summary>Operating pressure (bar)</summary>
        public double Pressure { get; set; } = 2.0;

        /// <summary>Heat transfer effectiveness (0-1)</summary>
        public double Effectiveness { get; set; } = 0.85;

        /// <summary>UA value - overall heat transfer coefficient × area (kW/K)</summary>
        public double UAValue { get; set; } = 50.0;

        // === Flow Parameters ===
        /// <summary>Working fluid mass flow rate (kg/s)</summary>
        public double MassFlowRate { get; set; } = 1.0;

        /// <summary>Cooling/heating fluid flow rate (kg/s)</summary>
        public double SecondaryFlowRate { get; set; } = 5.0;

        /// <summary>Pressure drop across component (kPa)</summary>
        public double PressureDrop { get; set; } = 10.0;

        // === Efficiency Parameters ===
        /// <summary>Isentropic efficiency (for turbine/pump) (0-1)</summary>
        public double IsentropicEfficiency { get; set; } = 0.80;

        /// <summary>Mechanical efficiency (0-1)</summary>
        public double MechanicalEfficiency { get; set; } = 0.95;

        // === Condenser-Specific ===
        /// <summary>Condenser type: WaterCooled, AirCooled, Evaporative, Hybrid</summary>
        public string CondenserType { get; set; } = "WaterCooled";

        /// <summary>Cooling water inlet temperature (°C)</summary>
        public double CoolingInletTemp { get; set; } = 15.0;

        /// <summary>Cooling water outlet temperature (°C)</summary>
        public double CoolingOutletTemp { get; set; } = 25.0;

        /// <summary>Subcooling degrees (°C)</summary>
        public double Subcooling { get; set; } = 2.0;

        // === Evaporator-Specific ===
        /// <summary>Pinch point temperature difference (°C)</summary>
        public double PinchPoint { get; set; } = 5.0;

        /// <summary>Superheat degrees (°C)</summary>
        public double Superheat { get; set; } = 5.0;

        // === Turbine-Specific ===
        /// <summary>Turbine type: Radial, Axial, Screw</summary>
        public string TurbineType { get; set; } = "Radial";

        /// <summary>Rotational speed (RPM)</summary>
        public double RotationalSpeed { get; set; } = 3000.0;

        /// <summary>Power output (kW)</summary>
        public double PowerOutput { get; set; } = 0.0;

        // === Pump-Specific ===
        /// <summary>Pump type: Centrifugal, PositiveDisplacement</summary>
        public string PumpType { get; set; } = "Centrifugal";

        /// <summary>Pump head (m)</summary>
        public double PumpHead { get; set; } = 100.0;

        /// <summary>Power consumption (kW)</summary>
        public double PowerConsumption { get; set; } = 0.0;

        // === Geometry ===
        /// <summary>Number of tubes (for shell-tube HX)</summary>
        public int TubeCount { get; set; } = 100;

        /// <summary>Tube length (m)</summary>
        public double TubeLength { get; set; } = 3.0;

        /// <summary>Tube diameter (m)</summary>
        public double TubeDiameter { get; set; } = 0.019;

        /// <summary>Shell diameter (m)</summary>
        public double ShellDiameter { get; set; } = 0.5;

        /// <summary>Material of construction</summary>
        public string Material { get; set; } = "Stainless Steel 316";

        /// <summary>Fouling factor (m²K/kW)</summary>
        public double FoulingFactor { get; set; } = 0.05;

        // === Connection Info ===
        /// <summary>ID of upstream component</summary>
        public string UpstreamComponentId { get; set; } = "";

        /// <summary>ID of downstream component</summary>
        public string DownstreamComponentId { get; set; } = "";

        /// <summary>
        /// Calculate heat transfer rate for heat exchangers (kW)
        /// </summary>
        public double CalculateHeatTransfer(double hotInletTemp, double coldInletTemp)
        {
            double LMTD = (hotInletTemp - CoolingOutletTemp - (Temperature - CoolingInletTemp)) /
                          Math.Log((hotInletTemp - CoolingOutletTemp) / (Temperature - CoolingInletTemp + 1e-10) + 1e-10);
            return UAValue * LMTD;
        }

        /// <summary>
        /// Calculate turbine power output (kW) given inlet/outlet conditions
        /// </summary>
        public double CalculateTurbinePower(double enthalpyIn, double enthalpyOutIsentropic)
        {
            double deltaH_isentropic = enthalpyIn - enthalpyOutIsentropic;
            double deltaH_actual = deltaH_isentropic * IsentropicEfficiency;
            PowerOutput = MassFlowRate * deltaH_actual * MechanicalEfficiency;
            return PowerOutput;
        }

        /// <summary>
        /// Calculate pump power consumption (kW)
        /// </summary>
        public double CalculatePumpPower(double densityFluid)
        {
            // P = (ṁ × g × H) / (η_pump × η_mech)
            double g = 9.81;
            PowerConsumption = (MassFlowRate * g * PumpHead) / (IsentropicEfficiency * MechanicalEfficiency * 1000.0);
            return PowerConsumption;
        }
    }
}
