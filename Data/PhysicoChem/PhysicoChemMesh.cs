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
        public string Type { get; set; } // "HeatExchanger", "Baffle", "Obstacle", "Condenser", "Evaporator", "Turbine", "Pump", "NuclearCore", "FuelAssembly", "ControlRod", "Moderator"

        // Simplified Geometry (Box or Cylinder for now)
        public (double X, double Y, double Z) Center { get; set; }
        public (double X, double Y, double Z) Size { get; set; } // For Box
        public double Radius { get; set; } // For Cylinder
        public double Height { get; set; } // For Cylinder
        public bool IsCylinder { get; set; }

        // ORC-specific parameters
        public ORCComponentParameters? ORCParams { get; set; }

        // Nuclear reactor parameters (for NuclearCore, FuelAssembly, ControlRod, Moderator types)
        public NuclearReactorParameters? NuclearParams { get; set; }

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
        ///     Probe manager for simulation measurement and visualization.
        /// </summary>
        [JsonProperty]
        public ProbeManager Probes { get; set; } = new ProbeManager();

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
        ///     Embeds a nuclear reactor core into the mesh using thermodynamic reactor builder pattern.
        /// </summary>
        /// <param name="nuclearParams">Nuclear reactor configuration</param>
        /// <param name="center">Center position of the reactor core</param>
        /// <returns>The created ReactorObject representing the nuclear core</returns>
        public ReactorObject EmbedNuclearReactor(NuclearReactorParameters nuclearParams, (double X, double Y, double Z) center)
        {
            var coreObject = new ReactorObject
            {
                Name = $"NuclearCore_{nuclearParams.ReactorType}",
                MaterialID = "NuclearFuel",
                Type = "NuclearCore",
                Center = center,
                IsCylinder = true,
                Radius = nuclearParams.CoreDiameter / 2,
                Height = nuclearParams.CoreHeight,
                NuclearParams = nuclearParams
            };

            EmbedObject(coreObject);
            return coreObject;
        }

        /// <summary>
        ///     Gets all nuclear reactor objects from the mesh.
        /// </summary>
        public IEnumerable<ReactorObject> GetNuclearReactorObjects()
        {
            return ReactorObjects.Where(obj => obj.NuclearParams != null ||
                obj.Type == "NuclearCore" || obj.Type == "FuelAssembly" ||
                obj.Type == "ControlRod" || obj.Type == "Moderator");
        }

        /// <summary>
        ///     Gets all ORC (Organic Rankine Cycle) objects from the mesh.
        /// </summary>
        public IEnumerable<ReactorObject> GetORCObjects()
        {
            return ReactorObjects.Where(obj => obj.ORCParams != null ||
                obj.Type == "Condenser" || obj.Type == "Evaporator" ||
                obj.Type == "Turbine" || obj.Type == "Pump");
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

    // ============================================================================
    // NUCLEAR REACTOR SIMULATION PARAMETERS
    // ============================================================================

    /// <summary>
    /// Nuclear reactor types supported by the simulation
    /// </summary>
    public enum NuclearReactorType
    {
        PWR,    // Pressurized Water Reactor
        BWR,    // Boiling Water Reactor
        PHWR,   // Pressurized Heavy Water Reactor (CANDU)
        HTGR,   // High Temperature Gas Reactor
        LMFBR,  // Liquid Metal Fast Breeder Reactor
        Research // Research/Test Reactor
    }

    /// <summary>
    /// Moderator types for neutron thermalization
    /// </summary>
    public enum ModeratorType
    {
        LightWater,  // H2O - most common
        HeavyWater,  // D2O - CANDU reactors
        Graphite,    // Gas-cooled reactors
        Beryllium    // Research reactors
    }

    /// <summary>
    /// Coolant types for heat removal
    /// </summary>
    public enum NuclearCoolantType
    {
        LightWater,
        HeavyWater,
        Helium,
        CO2,
        SodiumLiquid,
        LeadBismuth
    }

    /// <summary>
    /// Control rod types
    /// </summary>
    public enum ControlRodType
    {
        Control,    // Normal control rods
        Shutdown,   // Safety shutdown rods
        PartLength, // Axial power shaping
        Gray        // Load following
    }

    /// <summary>
    /// Parameters for nuclear reactor simulation within PhysicoChem framework
    /// </summary>
    public class NuclearReactorParameters
    {
        // === Reactor Type and Power ===
        public NuclearReactorType ReactorType { get; set; } = NuclearReactorType.PWR;
        public double ThermalPowerMW { get; set; } = 3000.0;
        public double ElectricalPowerMW { get; set; } = 1000.0;
        public double ThermalEfficiency => ElectricalPowerMW / ThermalPowerMW;

        // === Core Geometry ===
        public double CoreHeight { get; set; } = 3.66; // m
        public double CoreDiameter { get; set; } = 3.37; // m
        public int NumberOfAssemblies { get; set; } = 193;
        public double AssemblyPitch { get; set; } = 0.214; // m
        public int AxialNodes { get; set; } = 24;
        public int RadialRings { get; set; } = 10;
        public double CoreVolume => Math.PI * Math.Pow(CoreDiameter / 2, 2) * CoreHeight;

        // === Fuel Assemblies ===
        public List<FuelAssemblyParameters> FuelAssemblies { get; set; } = new();

        // === Control Systems ===
        public List<ControlRodBankParameters> ControlRodBanks { get; set; } = new();
        public double BoronConcentrationPPM { get; set; } = 1000; // Soluble boron

        // === Moderator ===
        public ModeratorParameters Moderator { get; set; } = new();

        // === Coolant ===
        public NuclearCoolantParameters Coolant { get; set; } = new();

        // === Neutronics ===
        public NeutronicsParameters Neutronics { get; set; } = new();

        // === Thermal-Hydraulics ===
        public NuclearThermalHydraulics ThermalHydraulics { get; set; } = new();

        // === Safety ===
        public NuclearSafetyParameters Safety { get; set; } = new();

        /// <summary>
        /// Initialize default PWR configuration (Westinghouse 4-loop type)
        /// </summary>
        public void InitializePWR()
        {
            ReactorType = NuclearReactorType.PWR;
            ThermalPowerMW = 3411;
            ElectricalPowerMW = 1150;
            CoreHeight = 3.66;
            CoreDiameter = 3.37;
            NumberOfAssemblies = 193;
            AssemblyPitch = 0.214;

            Moderator = new ModeratorParameters
            {
                Type = ModeratorType.LightWater,
                Density = 700,
                Temperature = 300
            };

            Coolant = new NuclearCoolantParameters
            {
                Type = NuclearCoolantType.LightWater,
                InletTemperature = 292,
                OutletTemperature = 326,
                Pressure = 15.5,
                MassFlowRate = 17400
            };

            InitializeFuelAssemblies(17, 17, 264, 3.5);
            InitializeControlRods();
        }

        /// <summary>
        /// Initialize CANDU/PHWR configuration with heavy water
        /// </summary>
        public void InitializeCANDU()
        {
            ReactorType = NuclearReactorType.PHWR;
            ThermalPowerMW = 2064;
            ElectricalPowerMW = 700;
            CoreHeight = 5.94;
            CoreDiameter = 7.6;
            NumberOfAssemblies = 380;
            AssemblyPitch = 0.286;

            Moderator = new ModeratorParameters
            {
                Type = ModeratorType.HeavyWater,
                Density = 1085,
                Temperature = 70,
                D2OPurity = 99.75
            };

            Coolant = new NuclearCoolantParameters
            {
                Type = NuclearCoolantType.HeavyWater,
                InletTemperature = 266,
                OutletTemperature = 310,
                Pressure = 10.0,
                MassFlowRate = 7600
            };

            InitializeFuelAssemblies(1, 380, 37, 0.71); // Natural uranium
            BoronConcentrationPPM = 0; // CANDU doesn't use boron
        }

        private void InitializeFuelAssemblies(int rows, int cols, int rodsPerAssembly, double enrichment)
        {
            FuelAssemblies.Clear();
            int id = 0;
            double startX = -AssemblyPitch * (rows - 1) / 2.0;
            double startY = -AssemblyPitch * (cols - 1) / 2.0;

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (ReactorType == NuclearReactorType.PWR && IsCornerPosition(i, j, rows, cols))
                        continue;

                    FuelAssemblies.Add(new FuelAssemblyParameters
                    {
                        Id = id++,
                        PositionX = startX + i * AssemblyPitch,
                        PositionY = startY + j * AssemblyPitch,
                        NumberOfRods = rodsPerAssembly,
                        EnrichmentPercent = enrichment
                    });
                }
            }
        }

        private void InitializeControlRods()
        {
            ControlRodBanks.Clear();
            // Typical PWR has 4 control rod banks + shutdown banks
            string[] bankNames = { "Bank A", "Bank B", "Bank C", "Bank D", "Shutdown A", "Shutdown B" };
            for (int i = 0; i < bankNames.Length; i++)
            {
                ControlRodBanks.Add(new ControlRodBankParameters
                {
                    BankId = i,
                    Name = bankNames[i],
                    RodType = i < 4 ? ControlRodType.Control : ControlRodType.Shutdown,
                    InsertionFraction = 0,
                    Worth = i < 4 ? 500 : 2000 // pcm
                });
            }
        }

        private static bool IsCornerPosition(int i, int j, int rows, int cols)
        {
            int corner = 2;
            return (i < corner && j < corner) ||
                   (i < corner && j >= cols - corner) ||
                   (i >= rows - corner && j < corner) ||
                   (i >= rows - corner && j >= cols - corner);
        }
    }

    /// <summary>
    /// Fuel assembly parameters
    /// </summary>
    public class FuelAssemblyParameters
    {
        public int Id { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public int NumberOfRods { get; set; } = 264;
        public double EnrichmentPercent { get; set; } = 3.5;
        public double BurnupMWdPerTU { get; set; } = 0;
        public double AveragePowerDensity { get; set; } = 0; // kW/L
        public double PeakPowerFactor { get; set; } = 1.0;

        // Fuel rod geometry
        public double FuelPelletDiameter { get; set; } = 0.0082; // m
        public double CladOuterDiameter { get; set; } = 0.0095; // m
        public double CladThickness { get; set; } = 0.00057; // m
        public double ActiveFuelLength { get; set; } = 3.66; // m
        public string CladMaterial { get; set; } = "Zircaloy-4";
        public string FuelMaterial { get; set; } = "UO2";

        // Thermal state
        public double FuelCenterlineTemp { get; set; } = 400;
        public double FuelSurfaceTemp { get; set; } = 400;
        public double CladOuterTemp { get; set; } = 340;

        /// <summary>
        /// Calculate total fuel mass in assembly (kg)
        /// </summary>
        public double CalculateFuelMass()
        {
            double pelletVolume = Math.PI * Math.Pow(FuelPelletDiameter / 2, 2) * ActiveFuelLength;
            double uo2Density = 10970 * 0.95; // 95% theoretical density
            return pelletVolume * uo2Density * NumberOfRods;
        }

        /// <summary>
        /// Calculate U-235 content (kg)
        /// </summary>
        public double CalculateU235Mass()
        {
            double fuelMass = CalculateFuelMass();
            double uraniumFraction = 238.0 / 270.0; // U in UO2
            return fuelMass * uraniumFraction * EnrichmentPercent / 100.0;
        }
    }

    /// <summary>
    /// Control rod bank parameters
    /// </summary>
    public class ControlRodBankParameters
    {
        public int BankId { get; set; }
        public string Name { get; set; } = "";
        public ControlRodType RodType { get; set; } = ControlRodType.Control;
        public double InsertionFraction { get; set; } = 0; // 0=out, 1=fully in
        public double Worth { get; set; } = 500; // pcm
        public string AbsorberMaterial { get; set; } = "Ag-In-Cd";
        public int NumberOfRods { get; set; } = 24;
        public double TotalLength { get; set; } = 3.66;
        public List<int> AssemblyIds { get; set; } = new();

        /// <summary>
        /// Calculate current reactivity contribution (pcm)
        /// </summary>
        public double GetReactivityContribution()
        {
            // Integral worth follows S-curve typically
            double x = InsertionFraction;
            double integralFraction = 3 * x * x - 2 * x * x * x; // S-curve
            return -Worth * integralFraction;
        }
    }

    /// <summary>
    /// Moderator properties for neutron thermalization
    /// </summary>
    public class ModeratorParameters
    {
        public ModeratorType Type { get; set; } = ModeratorType.LightWater;
        public double Density { get; set; } = 700; // kg/m³
        public double Temperature { get; set; } = 300; // °C
        public double D2OPurity { get; set; } = 99.75; // % for heavy water

        /// <summary>
        /// Scattering cross section (barn)
        /// </summary>
        public double ScatteringCrossSection => Type switch
        {
            ModeratorType.HeavyWater => 10.6,
            ModeratorType.LightWater => 49.2,
            ModeratorType.Graphite => 4.7,
            ModeratorType.Beryllium => 6.0,
            _ => 49.2
        };

        /// <summary>
        /// Absorption cross section (barn) - critical for reactor design
        /// </summary>
        public double AbsorptionCrossSection => Type switch
        {
            ModeratorType.HeavyWater => 0.0013, // Very low - allows natural U
            ModeratorType.LightWater => 0.664,
            ModeratorType.Graphite => 0.0034,
            ModeratorType.Beryllium => 0.0076,
            _ => 0.664
        };

        /// <summary>
        /// Moderation ratio - higher is better
        /// </summary>
        public double ModerationRatio => ScatteringCrossSection / AbsorptionCrossSection;

        /// <summary>
        /// Average logarithmic energy decrement per collision
        /// </summary>
        public double Xi => Type switch
        {
            ModeratorType.HeavyWater => 0.509,
            ModeratorType.LightWater => 0.920,
            ModeratorType.Graphite => 0.158,
            ModeratorType.Beryllium => 0.209,
            _ => 0.920
        };

        /// <summary>
        /// Slowing down power (xi * Sigma_s)
        /// </summary>
        public double SlowingDownPower => Xi * ScatteringCrossSection;

        /// <summary>
        /// Number of collisions to thermalize a fission neutron
        /// </summary>
        public int CollisionsToThermalize => (int)(Math.Log(2e6 / 0.025) / Xi);
    }

    /// <summary>
    /// Nuclear coolant parameters
    /// </summary>
    public class NuclearCoolantParameters
    {
        public NuclearCoolantType Type { get; set; } = NuclearCoolantType.LightWater;
        public double InletTemperature { get; set; } = 290; // °C
        public double OutletTemperature { get; set; } = 325; // °C
        public double Pressure { get; set; } = 15.5; // MPa
        public double MassFlowRate { get; set; } = 17000; // kg/s

        public double AverageTemperature => (InletTemperature + OutletTemperature) / 2;
        public double TemperatureRise => OutletTemperature - InletTemperature;

        /// <summary>
        /// Specific heat capacity (J/kg·K)
        /// </summary>
        public double GetSpecificHeat() => Type switch
        {
            NuclearCoolantType.LightWater => 5500,
            NuclearCoolantType.HeavyWater => 5200,
            NuclearCoolantType.Helium => 5190,
            NuclearCoolantType.SodiumLiquid => 1260,
            NuclearCoolantType.CO2 => 1100,
            NuclearCoolantType.LeadBismuth => 147,
            _ => 5500
        };

        /// <summary>
        /// Thermal conductivity (W/m·K)
        /// </summary>
        public double GetThermalConductivity() => Type switch
        {
            NuclearCoolantType.LightWater => 0.55,
            NuclearCoolantType.HeavyWater => 0.52,
            NuclearCoolantType.Helium => 0.30,
            NuclearCoolantType.SodiumLiquid => 70.0,
            _ => 0.55
        };

        /// <summary>
        /// Calculate heat removal capacity (MW)
        /// </summary>
        public double CalculateHeatRemoval()
        {
            return MassFlowRate * GetSpecificHeat() * TemperatureRise / 1e6;
        }
    }

    /// <summary>
    /// Neutronics parameters for reactor physics calculations
    /// </summary>
    public class NeutronicsParameters
    {
        // Criticality
        public double Keff { get; set; } = 1.0;
        public double Kinf { get; set; } = 1.3;
        public double Reactivity => (Keff - 1) / Keff * 1e5; // pcm

        // Neutron flux levels
        public double NeutronFluxThermal { get; set; } = 3e13; // n/cm²·s
        public double NeutronFluxFast { get; set; } = 1e14; // n/cm²·s
        public double AverageNeutronsPerFission { get; set; } = 2.43;

        // Delayed neutrons - critical for control
        public double DelayedNeutronFraction { get; set; } = 0.0065; // β
        public double PromptNeutronLifetime { get; set; } = 2e-5; // s
        public double GenerationTime { get; set; } = 1e-4; // s

        // Six-group delayed neutron data (U-235)
        public double[] DelayedFractions { get; set; } = { 0.000215, 0.001424, 0.001274, 0.002568, 0.000748, 0.000273 };
        public double[] DecayConstants { get; set; } = { 0.0124, 0.0305, 0.111, 0.301, 1.14, 3.01 }; // 1/s

        // Power distribution
        public double RadialPeakingFactor { get; set; } = 1.45;
        public double AxialPeakingFactor { get; set; } = 1.55;
        public double TotalPeakingFactor => RadialPeakingFactor * AxialPeakingFactor;

        // Two-group cross sections (homogenized)
        public double SigmaAbsorption1 { get; set; } = 0.010; // Fast (1/cm)
        public double SigmaAbsorption2 { get; set; } = 0.100; // Thermal
        public double SigmaFission1 { get; set; } = 0.003;
        public double SigmaFission2 { get; set; } = 0.150;
        public double SigmaScatter12 { get; set; } = 0.020;
        public double DiffusionCoeff1 { get; set; } = 1.5; // cm
        public double DiffusionCoeff2 { get; set; } = 0.4; // cm

        // Fission product poisons
        public double XenonConcentration { get; set; } = 0;
        public double SamariumConcentration { get; set; } = 0;
        public double XenonEquilibriumWorth { get; set; } = -2500; // pcm

        /// <summary>
        /// Calculate reactor period from reactivity (seconds)
        /// </summary>
        public double CalculatePeriod(double reactivityPcm)
        {
            double rho = reactivityPcm / 1e5;
            if (Math.Abs(rho) < 1e-10) return double.PositiveInfinity;

            // Inhour equation approximation
            if (rho > 0 && rho < DelayedNeutronFraction)
            {
                // Delayed critical - long period
                return GenerationTime / (rho - DelayedNeutronFraction) +
                       DelayedFractions[0] / (DecayConstants[0] * (DelayedNeutronFraction - rho));
            }
            else if (rho >= DelayedNeutronFraction)
            {
                // Prompt critical - very short period (dangerous!)
                return PromptNeutronLifetime / (rho - DelayedNeutronFraction);
            }
            else
            {
                // Subcritical
                return GenerationTime / rho;
            }
        }
    }

    /// <summary>
    /// Thermal-hydraulics parameters
    /// </summary>
    public class NuclearThermalHydraulics
    {
        // Thermal limits
        public double MaxFuelCenterlineTemp { get; set; } = 2800; // °C
        public double MaxCladTemp { get; set; } = 1200; // °C
        public double MinDNBRatio { get; set; } = 1.3;

        // Heat rates
        public double AveragePowerDensity { get; set; } = 100; // kW/L
        public double AverageLinearHeatRate { get; set; } = 17.8; // kW/m
        public double MaxLinearHeatRate { get; set; } = 44; // kW/m

        // Flow parameters
        public double CorePressureDrop { get; set; } = 0.15; // MPa
        public double CoreFlowArea { get; set; } = 4.75; // m²
        public double AverageFlowVelocity { get; set; } = 5.0; // m/s
        public double ReynoldsNumber { get; set; } = 500000;

        // Heat transfer
        public double HeatTransferCoeff { get; set; } = 35000; // W/m²·K
        public double GapConductance { get; set; } = 5700; // W/m²·K
        public double FuelThermalConductivity { get; set; } = 3.0; // W/m·K

        /// <summary>
        /// Calculate fuel centerline temperature
        /// </summary>
        public double CalculateFuelCenterline(double linearHeatRate, double surfaceTemp)
        {
            // T_center = T_surface + q'/(4*pi*k)
            return surfaceTemp + linearHeatRate * 1000 / (4 * Math.PI * FuelThermalConductivity);
        }

        /// <summary>
        /// Calculate DNBR using W-3 correlation (simplified)
        /// </summary>
        public double CalculateDNBR(double heatFlux, double pressure, double massFlux, double quality)
        {
            // Simplified W-3 critical heat flux (MW/m²)
            double p = pressure; // MPa
            double G = massFlux / 1000; // Mg/m²·s
            double qCrit = (2.022 - 0.0004302 * p + 0.1722 * G) * (1 - quality);
            return qCrit / (heatFlux / 1e6);
        }
    }

    /// <summary>
    /// Nuclear safety system parameters
    /// </summary>
    public class NuclearSafetyParameters
    {
        // SCRAM setpoints
        public double ScramPowerPercent { get; set; } = 118;
        public double ScramPeriodSeconds { get; set; } = 10;
        public double ScramTempCelsius { get; set; } = 343;
        public double ScramPressureMPa { get; set; } = 17.2;

        // ECCS
        public bool HasECCS { get; set; } = true;
        public double ECCSFlowRate { get; set; } = 500; // kg/s
        public double AccumulatorPressure { get; set; } = 4.0; // MPa
        public double AccumulatorVolume { get; set; } = 40; // m³

        // Containment
        public double ContainmentPressure { get; set; } = 0.5; // MPa design
        public double ContainmentVolume { get; set; } = 50000; // m³

        // Status
        public bool IsScramActive { get; set; } = false;
        public bool IsECCSActive { get; set; } = false;
        public string ScramReason { get; set; } = "";

        /// <summary>
        /// Check if any safety limit is exceeded
        /// </summary>
        public bool CheckSafetyLimits(double power, double period, double temp, double pressure)
        {
            if (power > ScramPowerPercent) { ScramReason = "High Power"; return true; }
            if (period < ScramPeriodSeconds && period > 0) { ScramReason = "Short Period"; return true; }
            if (temp > ScramTempCelsius) { ScramReason = "High Temperature"; return true; }
            if (pressure > ScramPressureMPa) { ScramReason = "High Pressure"; return true; }
            return false;
        }
    }

    /// <summary>
    /// Real-time state of the nuclear reactor
    /// </summary>
    public class NuclearReactorState
    {
        public double Time { get; set; } = 0;
        public double ThermalPowerMW { get; set; } = 0;
        public double RelativePower { get; set; } = 0;
        public double Keff { get; set; } = 1.0;
        public double ReactivityPcm { get; set; } = 0;
        public double PeriodSeconds { get; set; } = double.PositiveInfinity;

        // 3D field distributions (for visualization)
        public double[,,]? NeutronFlux { get; set; }
        public double[,,]? PowerDensity { get; set; }
        public double[,,]? FuelTemperature { get; set; }
        public double[,,]? CoolantTemperature { get; set; }
        public double[,,]? CladTemperature { get; set; }

        // Delayed neutron precursors (6 groups)
        public double[] PrecursorConcentrations { get; set; } = new double[6];

        // Fission products
        public double XenonConcentration { get; set; } = 0;
        public double IodineConcentration { get; set; } = 0;
        public double SamariumConcentration { get; set; } = 0;
        public double PromethiumConcentration { get; set; } = 0;

        // Peak values
        public double PeakFuelTemp { get; set; } = 0;
        public double PeakCladTemp { get; set; } = 0;
        public double MinDNBR { get; set; } = 10;
        public double MaxLinearHeatRate { get; set; } = 0;

        // Control state
        public double[] ControlRodPositions { get; set; } = Array.Empty<double>();
        public double BoronConcentrationPPM { get; set; } = 0;

        public NuclearReactorState Clone()
        {
            return new NuclearReactorState
            {
                Time = Time,
                ThermalPowerMW = ThermalPowerMW,
                RelativePower = RelativePower,
                Keff = Keff,
                ReactivityPcm = ReactivityPcm,
                PeriodSeconds = PeriodSeconds,
                PrecursorConcentrations = (double[])PrecursorConcentrations.Clone(),
                XenonConcentration = XenonConcentration,
                IodineConcentration = IodineConcentration,
                SamariumConcentration = SamariumConcentration,
                PeakFuelTemp = PeakFuelTemp,
                PeakCladTemp = PeakCladTemp,
                MinDNBR = MinDNBR,
                ControlRodPositions = (double[])ControlRodPositions.Clone(),
                BoronConcentrationPPM = BoronConcentrationPPM
            };
        }
    }

    // ============================================================================
    // SIMULATION PROBE SYSTEM
    // ============================================================================

    /// <summary>
    /// Available variables that can be tracked by probes
    /// </summary>
    public enum ProbeVariable
    {
        // Thermal
        Temperature,
        Pressure,
        HeatFlux,

        // Flow
        Velocity,
        MassFlowRate,
        Density,
        Viscosity,

        // Chemical
        Concentration,
        pH,
        Salinity,
        DissolvedOxygen,

        // Geothermal
        ThermalConductivity,
        HeatCapacity,
        Enthalpy,

        // Nuclear
        NeutronFlux,
        PowerDensity,
        FuelTemperature,
        CoolantTemperature,
        CladTemperature,
        XenonConcentration,
        Reactivity,
        DNBR,

        // ORC
        ORCEfficiency,
        TurbinePower,
        CondenserDuty,

        // Custom
        Custom
    }

    /// <summary>
    /// Plane orientation for planar probes
    /// </summary>
    public enum ProbePlaneOrientation
    {
        XY,
        XZ,
        YZ
    }

    /// <summary>
    /// Data point recorded by a probe at a specific time
    /// </summary>
    public class ProbeDataPoint
    {
        public double Time { get; set; }
        public double Value { get; set; }
        public double? Min { get; set; }  // For line/plane probes
        public double? Max { get; set; }  // For line/plane probes
        public double? StdDev { get; set; }  // Standard deviation for averaging
    }

    /// <summary>
    /// 2D field data for planar probe cross-section visualization
    /// </summary>
    public class ProbeFieldData
    {
        public double Time { get; set; }
        public double[,] Values { get; set; } = new double[0, 0];
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public int ResolutionX { get; set; }
        public int ResolutionY { get; set; }
    }

    /// <summary>
    /// Base class for simulation probes
    /// </summary>
    public abstract class SimulationProbe
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Probe";
        public bool IsActive { get; set; } = true;
        public ProbeVariable Variable { get; set; } = ProbeVariable.Temperature;
        public string CustomVariableName { get; set; } = "";
        public uint Color { get; set; } = 0xFF00FF00; // Green (ABGR)
        public List<ProbeDataPoint> History { get; set; } = new();
        public int MaxHistoryPoints { get; set; } = 10000;

        /// <summary>
        /// Record a new data point
        /// </summary>
        public void RecordValue(double time, double value, double? min = null, double? max = null, double? stdDev = null)
        {
            History.Add(new ProbeDataPoint
            {
                Time = time,
                Value = value,
                Min = min,
                Max = max,
                StdDev = stdDev
            });

            // Trim history if needed
            while (History.Count > MaxHistoryPoints)
                History.RemoveAt(0);
        }

        /// <summary>
        /// Clear all recorded data
        /// </summary>
        public void ClearHistory() => History.Clear();

        /// <summary>
        /// Get the display name for the variable
        /// </summary>
        public string GetVariableDisplayName()
        {
            if (Variable == ProbeVariable.Custom)
                return CustomVariableName;
            return Variable.ToString();
        }

        /// <summary>
        /// Get units for the variable
        /// </summary>
        public string GetVariableUnits() => Variable switch
        {
            ProbeVariable.Temperature => "°C",
            ProbeVariable.Pressure => "MPa",
            ProbeVariable.HeatFlux => "W/m²",
            ProbeVariable.Velocity => "m/s",
            ProbeVariable.MassFlowRate => "kg/s",
            ProbeVariable.Density => "kg/m³",
            ProbeVariable.Viscosity => "Pa·s",
            ProbeVariable.Concentration => "mol/L",
            ProbeVariable.pH => "",
            ProbeVariable.Salinity => "ppt",
            ProbeVariable.DissolvedOxygen => "mg/L",
            ProbeVariable.ThermalConductivity => "W/(m·K)",
            ProbeVariable.HeatCapacity => "J/(kg·K)",
            ProbeVariable.Enthalpy => "kJ/kg",
            ProbeVariable.NeutronFlux => "n/(cm²·s)",
            ProbeVariable.PowerDensity => "kW/L",
            ProbeVariable.FuelTemperature => "°C",
            ProbeVariable.CoolantTemperature => "°C",
            ProbeVariable.CladTemperature => "°C",
            ProbeVariable.XenonConcentration => "atoms/cm³",
            ProbeVariable.Reactivity => "pcm",
            ProbeVariable.DNBR => "",
            ProbeVariable.ORCEfficiency => "%",
            ProbeVariable.TurbinePower => "kW",
            ProbeVariable.CondenserDuty => "kW",
            _ => ""
        };

        public abstract string GetProbeTypeDescription();
    }

    /// <summary>
    /// Point probe - tracks variable at a single location over time
    /// </summary>
    public class PointProbe : SimulationProbe
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public PointProbe() { }

        public PointProbe(double x, double y, double z, string name = "Point Probe")
        {
            X = x;
            Y = y;
            Z = z;
            Name = name;
        }

        public (double X, double Y, double Z) Position
        {
            get => (X, Y, Z);
            set { X = value.X; Y = value.Y; Z = value.Z; }
        }

        public override string GetProbeTypeDescription() => $"Point at ({X:F2}, {Y:F2}, {Z:F2})";
    }

    /// <summary>
    /// Line probe - tracks averaged variable along a line over time
    /// </summary>
    public class LineProbe : SimulationProbe
    {
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartZ { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double EndZ { get; set; }
        public int SamplePoints { get; set; } = 50;

        public LineProbe() { }

        public LineProbe(double x1, double y1, double z1, double x2, double y2, double z2, string name = "Line Probe")
        {
            StartX = x1; StartY = y1; StartZ = z1;
            EndX = x2; EndY = y2; EndZ = z2;
            Name = name;
        }

        public (double X, double Y, double Z) StartPoint
        {
            get => (StartX, StartY, StartZ);
            set { StartX = value.X; StartY = value.Y; StartZ = value.Z; }
        }

        public (double X, double Y, double Z) EndPoint
        {
            get => (EndX, EndY, EndZ);
            set { EndX = value.X; EndY = value.Y; EndZ = value.Z; }
        }

        public double Length => Math.Sqrt(
            Math.Pow(EndX - StartX, 2) +
            Math.Pow(EndY - StartY, 2) +
            Math.Pow(EndZ - StartZ, 2));

        /// <summary>
        /// Get sample positions along the line
        /// </summary>
        public IEnumerable<(double X, double Y, double Z)> GetSamplePositions()
        {
            for (int i = 0; i < SamplePoints; i++)
            {
                double t = SamplePoints > 1 ? (double)i / (SamplePoints - 1) : 0.5;
                yield return (
                    StartX + t * (EndX - StartX),
                    StartY + t * (EndY - StartY),
                    StartZ + t * (EndZ - StartZ)
                );
            }
        }

        public override string GetProbeTypeDescription() =>
            $"Line from ({StartX:F2}, {StartY:F2}, {StartZ:F2}) to ({EndX:F2}, {EndY:F2}, {EndZ:F2})";
    }

    /// <summary>
    /// Planar probe - creates 2D cross-section views with colormap
    /// </summary>
    public class PlaneProbe : SimulationProbe
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double CenterZ { get; set; }
        public double Width { get; set; } = 1.0;
        public double Height { get; set; } = 1.0;
        public ProbePlaneOrientation Orientation { get; set; } = ProbePlaneOrientation.XY;
        public int ResolutionX { get; set; } = 64;
        public int ResolutionY { get; set; } = 64;

        // Store 2D field snapshots
        public List<ProbeFieldData> FieldHistory { get; set; } = new();
        public int MaxFieldSnapshots { get; set; } = 100;

        public PlaneProbe() { }

        public PlaneProbe(double cx, double cy, double cz, double width, double height,
            ProbePlaneOrientation orientation, string name = "Plane Probe")
        {
            CenterX = cx; CenterY = cy; CenterZ = cz;
            Width = width; Height = height;
            Orientation = orientation;
            Name = name;
        }

        public (double X, double Y, double Z) Center
        {
            get => (CenterX, CenterY, CenterZ);
            set { CenterX = value.X; CenterY = value.Y; CenterZ = value.Z; }
        }

        /// <summary>
        /// Record a 2D field snapshot
        /// </summary>
        public void RecordField(double time, double[,] values)
        {
            double minVal = double.MaxValue, maxVal = double.MinValue;
            for (int i = 0; i < values.GetLength(0); i++)
            {
                for (int j = 0; j < values.GetLength(1); j++)
                {
                    minVal = Math.Min(minVal, values[i, j]);
                    maxVal = Math.Max(maxVal, values[i, j]);
                }
            }

            FieldHistory.Add(new ProbeFieldData
            {
                Time = time,
                Values = (double[,])values.Clone(),
                MinValue = minVal,
                MaxValue = maxVal,
                ResolutionX = values.GetLength(0),
                ResolutionY = values.GetLength(1)
            });

            while (FieldHistory.Count > MaxFieldSnapshots)
                FieldHistory.RemoveAt(0);

            // Also record average to scalar history
            double sum = 0;
            int count = values.GetLength(0) * values.GetLength(1);
            foreach (var v in values) sum += v;
            RecordValue(time, sum / count, minVal, maxVal);
        }

        /// <summary>
        /// Get sample positions on the plane
        /// </summary>
        public IEnumerable<(double X, double Y, double Z, int i, int j)> GetSamplePositions()
        {
            for (int i = 0; i < ResolutionX; i++)
            {
                for (int j = 0; j < ResolutionY; j++)
                {
                    double u = ResolutionX > 1 ? (double)i / (ResolutionX - 1) - 0.5 : 0;
                    double v = ResolutionY > 1 ? (double)j / (ResolutionY - 1) - 0.5 : 0;

                    double x = CenterX, y = CenterY, z = CenterZ;

                    switch (Orientation)
                    {
                        case ProbePlaneOrientation.XY:
                            x += u * Width;
                            y += v * Height;
                            break;
                        case ProbePlaneOrientation.XZ:
                            x += u * Width;
                            z += v * Height;
                            break;
                        case ProbePlaneOrientation.YZ:
                            y += u * Width;
                            z += v * Height;
                            break;
                    }

                    yield return (x, y, z, i, j);
                }
            }
        }

        public override string GetProbeTypeDescription() =>
            $"Plane {Orientation} at ({CenterX:F2}, {CenterY:F2}, {CenterZ:F2}), {Width:F2}x{Height:F2}m";
    }

    /// <summary>
    /// Manager for simulation probes - handles collection and data recording
    /// </summary>
    public class ProbeManager
    {
        public List<PointProbe> PointProbes { get; set; } = new();
        public List<LineProbe> LineProbes { get; set; } = new();
        public List<PlaneProbe> PlaneProbes { get; set; } = new();

        /// <summary>
        /// Get all probes as a single enumerable
        /// </summary>
        public IEnumerable<SimulationProbe> AllProbes =>
            PointProbes.Cast<SimulationProbe>()
                .Concat(LineProbes)
                .Concat(PlaneProbes);

        /// <summary>
        /// Get all active probes
        /// </summary>
        public IEnumerable<SimulationProbe> ActiveProbes =>
            AllProbes.Where(p => p.IsActive);

        /// <summary>
        /// Total number of probes
        /// </summary>
        public int Count => PointProbes.Count + LineProbes.Count + PlaneProbes.Count;

        /// <summary>
        /// Add a point probe
        /// </summary>
        public PointProbe AddPointProbe(double x, double y, double z, string name = "Point Probe")
        {
            var probe = new PointProbe(x, y, z, name);
            PointProbes.Add(probe);
            return probe;
        }

        /// <summary>
        /// Add a line probe
        /// </summary>
        public LineProbe AddLineProbe(double x1, double y1, double z1, double x2, double y2, double z2, string name = "Line Probe")
        {
            var probe = new LineProbe(x1, y1, z1, x2, y2, z2, name);
            LineProbes.Add(probe);
            return probe;
        }

        /// <summary>
        /// Add a plane probe
        /// </summary>
        public PlaneProbe AddPlaneProbe(double cx, double cy, double cz, double width, double height,
            ProbePlaneOrientation orientation, string name = "Plane Probe")
        {
            var probe = new PlaneProbe(cx, cy, cz, width, height, orientation, name);
            PlaneProbes.Add(probe);
            return probe;
        }

        /// <summary>
        /// Remove a probe by ID
        /// </summary>
        public bool RemoveProbe(string id)
        {
            var point = PointProbes.FirstOrDefault(p => p.Id == id);
            if (point != null) return PointProbes.Remove(point);

            var line = LineProbes.FirstOrDefault(p => p.Id == id);
            if (line != null) return LineProbes.Remove(line);

            var plane = PlaneProbes.FirstOrDefault(p => p.Id == id);
            if (plane != null) return PlaneProbes.Remove(plane);

            return false;
        }

        /// <summary>
        /// Get a probe by ID
        /// </summary>
        public SimulationProbe? GetProbe(string id) =>
            AllProbes.FirstOrDefault(p => p.Id == id);

        /// <summary>
        /// Clear all probe history
        /// </summary>
        public void ClearAllHistory()
        {
            foreach (var probe in AllProbes)
            {
                probe.ClearHistory();
                if (probe is PlaneProbe plane)
                    plane.FieldHistory.Clear();
            }
        }

        /// <summary>
        /// Clear all probes
        /// </summary>
        public void ClearAllProbes()
        {
            PointProbes.Clear();
            LineProbes.Clear();
            PlaneProbes.Clear();
        }

        /// <summary>
        /// Export probe data to CSV
        /// </summary>
        public string ExportToCSV(SimulationProbe probe)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Probe: {probe.Name}");
            sb.AppendLine($"# Type: {probe.GetProbeTypeDescription()}");
            sb.AppendLine($"# Variable: {probe.GetVariableDisplayName()} ({probe.GetVariableUnits()})");
            sb.AppendLine();
            sb.AppendLine("Time,Value,Min,Max,StdDev");

            foreach (var point in probe.History)
            {
                sb.AppendLine($"{point.Time:G6},{point.Value:G6},{point.Min?.ToString("G6") ?? ""},{point.Max?.ToString("G6") ?? ""},{point.StdDev?.ToString("G6") ?? ""}");
            }

            return sb.ToString();
        }
    }
}
