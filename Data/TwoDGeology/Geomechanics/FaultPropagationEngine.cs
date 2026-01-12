// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/FaultPropagationEngine.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Rupture mode classification for fault nucleation
/// </summary>
public enum RuptureMode
{
    None,
    TensileOpening,         // Mode I - opening/extension
    InPlaneShear,           // Mode II - sliding
    MixedMode,              // Combined tensile + shear
    Compressive             // Pure compressive yielding (no fault)
}

/// <summary>
/// Fault propagation direction strategy
/// </summary>
public enum PropagationStrategy
{
    StressGuided,           // Follow maximum shear stress direction
    EnergyMinimizing,       // Follow path of least resistance
    PrincipalStressAligned, // Align with σ1 direction (for tensile)
    ConjugateAngle,         // Form at Coulomb angle to σ1
    StrainLocalization      // Follow regions of maximum plastic strain
}

/// <summary>
/// Configuration for automatic fault generation during simulation
/// </summary>
public class AutoFaultSettings
{
    /// <summary>Enable automatic fault generation</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Minimum yield index to trigger fault nucleation (f/f_yield > threshold)</summary>
    public double RuptureThreshold { get; set; } = 1.0;

    /// <summary>Number of contiguous failed elements required to nucleate a fault</summary>
    public int MinFailedClusterSize { get; set; } = 3;

    /// <summary>Strategy for determining fault propagation direction</summary>
    public PropagationStrategy PropagationStrategy { get; set; } = PropagationStrategy.ConjugateAngle;

    /// <summary>Maximum fault propagation distance per step (m)</summary>
    public double MaxPropagationPerStep { get; set; } = 10.0;

    /// <summary>Minimum fault segment length (m)</summary>
    public double MinFaultSegmentLength { get; set; } = 0.1;

    /// <summary>Normal stiffness for generated fault interfaces (Pa/m)</summary>
    public double FaultNormalStiffness { get; set; } = 1e10;

    /// <summary>Shear stiffness for generated fault interfaces (Pa/m)</summary>
    public double FaultShearStiffness { get; set; } = 1e9;

    /// <summary>Cohesion for generated faults (Pa) - usually 0 for new ruptures</summary>
    public double FaultCohesion { get; set; } = 0;

    /// <summary>Friction angle for generated faults (degrees)</summary>
    public double FaultFrictionAngle { get; set; } = 25;

    /// <summary>Tensile strength for generated faults (Pa)</summary>
    public double FaultTensileStrength { get; set; } = 0;

    /// <summary>Allow faults to propagate through existing faults</summary>
    public bool AllowFaultIntersection { get; set; } = true;

    /// <summary>Maximum number of faults to generate per simulation</summary>
    public int MaxFaultsPerSimulation { get; set; } = 100;

    /// <summary>Delay fault generation until this load step (for staged analysis)</summary>
    public int StartAtLoadStep { get; set; } = 1;

    /// <summary>Check for new fault nucleation every N steps</summary>
    public int CheckInterval { get; set; } = 1;

    /// <summary>Enable fault propagation from existing fault tips</summary>
    public bool EnableTipPropagation { get; set; } = true;

    /// <summary>Stress intensity factor threshold for tip propagation (Mode I)</summary>
    public double KI_Threshold { get; set; } = 1e6;

    /// <summary>Stress intensity factor threshold for tip propagation (Mode II)</summary>
    public double KII_Threshold { get; set; } = 1e6;
}

/// <summary>
/// Represents a nucleation site where a new fault may form
/// </summary>
public class FaultNucleationSite
{
    public int ElementId { get; set; }
    public Vector2 Location { get; set; }
    public RuptureMode Mode { get; set; }
    public double YieldExcess { get; set; }         // How much yield function exceeded
    public double Sigma1 { get; set; }              // Maximum principal stress
    public double Sigma3 { get; set; }              // Minimum principal stress
    public double PrincipalAngle { get; set; }      // Angle of σ1 from horizontal (degrees)
    public double PlasticStrain { get; set; }
    public List<int> ClusterElementIds { get; set; } = new();
}

/// <summary>
/// Represents a generated fault segment
/// </summary>
public class GeneratedFault
{
    public int Id { get; set; }
    public List<Vector2> Points { get; set; } = new();
    public RuptureMode Mode { get; set; }
    public double DipAngle { get; set; }
    public double TotalSlip { get; set; }
    public double OpeningDisplacement { get; set; }
    public List<int> InterfaceElementIds { get; set; } = new();
    public int NucleationStep { get; set; }
    public double CreationTime { get; set; }
    public bool IsActive { get; set; } = true;

    public double Length => Points.Count < 2 ? 0 :
        Points.Zip(Points.Skip(1), (a, b) => Vector2.Distance(a, b)).Sum();

    public Vector2 TipStart => Points.FirstOrDefault();
    public Vector2 TipEnd => Points.LastOrDefault();
}

/// <summary>
/// Event arguments for fault generation events
/// </summary>
public class FaultGeneratedEventArgs : EventArgs
{
    public GeneratedFault Fault { get; set; }
    public int SimulationStep { get; set; }
    public double Time { get; set; }
}

/// <summary>
/// Engine for automatic fault generation based on rupture criteria.
/// Detects failure zones, nucleates faults, and propagates them through the mesh
/// following stress-guided or energy-minimizing paths.
/// </summary>
public class FaultPropagationEngine
{
    #region Properties

    public AutoFaultSettings Settings { get; } = new();
    public List<GeneratedFault> GeneratedFaults { get; } = new();
    public List<FaultNucleationSite> PotentialNucleationSites { get; } = new();

    /// <summary>Event fired when a new fault is nucleated</summary>
    public event EventHandler<FaultGeneratedEventArgs> OnFaultNucleated;

    /// <summary>Event fired when an existing fault propagates</summary>
    public event EventHandler<FaultGeneratedEventArgs> OnFaultPropagated;

    /// <summary>Event fired when rupture is detected but fault not yet formed</summary>
    public event EventHandler<FaultNucleationSite> OnRuptureDetected;

    private int _nextFaultId = 1;

    #endregion

    #region Main Detection and Generation Methods

    /// <summary>
    /// Analyze mesh for potential fault nucleation sites based on current stress state
    /// </summary>
    public void DetectRuptureSites(FEMMesh2D mesh, SimulationResults2D results)
    {
        PotentialNucleationSites.Clear();

        var failedElements = new List<int>();

        // Find all elements that have exceeded the yield criterion
        for (int e = 0; e < mesh.Elements.Count; e++)
        {
            if (results.YieldIndex[e] > Settings.RuptureThreshold)
            {
                failedElements.Add(e);
            }
        }

        if (failedElements.Count == 0) return;

        // Cluster adjacent failed elements
        var clusters = ClusterAdjacentElements(mesh, failedElements);

        // Create nucleation sites for clusters meeting size threshold
        foreach (var cluster in clusters)
        {
            if (cluster.Count >= Settings.MinFailedClusterSize)
            {
                var site = CreateNucleationSite(mesh, results, cluster);
                if (site != null)
                {
                    PotentialNucleationSites.Add(site);
                    OnRuptureDetected?.Invoke(this, site);
                }
            }
        }
    }

    /// <summary>
    /// Generate faults from detected nucleation sites
    /// </summary>
    public List<GeneratedFault> GenerateFaults(FEMMesh2D mesh, SimulationResults2D results, int currentStep, double currentTime)
    {
        var newFaults = new List<GeneratedFault>();

        if (!Settings.Enabled) return newFaults;
        if (currentStep < Settings.StartAtLoadStep) return newFaults;
        if (currentStep % Settings.CheckInterval != 0) return newFaults;
        if (GeneratedFaults.Count >= Settings.MaxFaultsPerSimulation) return newFaults;

        // Detect rupture sites
        DetectRuptureSites(mesh, results);

        // Generate faults from nucleation sites
        foreach (var site in PotentialNucleationSites)
        {
            // Check if a fault already exists at this location
            if (FaultExistsNear(site.Location, Settings.MinFaultSegmentLength * 2))
                continue;

            var fault = NucleateFault(mesh, results, site, currentStep, currentTime);
            if (fault != null)
            {
                GeneratedFaults.Add(fault);
                newFaults.Add(fault);
                OnFaultNucleated?.Invoke(this, new FaultGeneratedEventArgs
                {
                    Fault = fault,
                    SimulationStep = currentStep,
                    Time = currentTime
                });
            }
        }

        // Propagate existing fault tips if enabled
        if (Settings.EnableTipPropagation)
        {
            foreach (var fault in GeneratedFaults.Where(f => f.IsActive))
            {
                var extended = PropagateExistingFault(mesh, results, fault, currentStep, currentTime);
                if (extended)
                {
                    OnFaultPropagated?.Invoke(this, new FaultGeneratedEventArgs
                    {
                        Fault = fault,
                        SimulationStep = currentStep,
                        Time = currentTime
                    });
                }
            }
        }

        return newFaults;
    }

    /// <summary>
    /// Process simulation step - main entry point for automatic fault generation
    /// </summary>
    public void ProcessStep(FEMMesh2D mesh, SimulationResults2D results, int step, double time)
    {
        if (!Settings.Enabled) return;

        var newFaults = GenerateFaults(mesh, results, step, time);

        // Insert new faults into mesh as interface elements
        foreach (var fault in newFaults)
        {
            InsertFaultIntoMesh(mesh, fault);
        }
    }

    #endregion

    #region Rupture Detection

    /// <summary>
    /// Cluster adjacent failed elements using flood fill
    /// </summary>
    private List<List<int>> ClusterAdjacentElements(FEMMesh2D mesh, List<int> failedElements)
    {
        var clusters = new List<List<int>>();
        var visited = new HashSet<int>();
        var adjacency = BuildAdjacencyMap(mesh);

        foreach (int elementId in failedElements)
        {
            if (visited.Contains(elementId)) continue;

            var cluster = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(elementId);
            visited.Add(elementId);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                cluster.Add(current);

                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor) && failedElements.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (cluster.Count > 0)
            {
                clusters.Add(cluster);
            }
        }

        return clusters;
    }

    /// <summary>
    /// Build adjacency map for elements (elements sharing nodes)
    /// </summary>
    private Dictionary<int, List<int>> BuildAdjacencyMap(FEMMesh2D mesh)
    {
        var adjacency = new Dictionary<int, List<int>>();
        var nodeToElements = new Dictionary<int, List<int>>();

        // Build node-to-element map
        for (int e = 0; e < mesh.Elements.Count; e++)
        {
            foreach (int nodeId in mesh.Elements[e].NodeIds)
            {
                if (!nodeToElements.ContainsKey(nodeId))
                    nodeToElements[nodeId] = new List<int>();
                nodeToElements[nodeId].Add(e);
            }
        }

        // Build element adjacency
        for (int e = 0; e < mesh.Elements.Count; e++)
        {
            adjacency[e] = new List<int>();
            foreach (int nodeId in mesh.Elements[e].NodeIds)
            {
                foreach (int neighbor in nodeToElements[nodeId])
                {
                    if (neighbor != e && !adjacency[e].Contains(neighbor))
                    {
                        adjacency[e].Add(neighbor);
                    }
                }
            }
        }

        return adjacency;
    }

    /// <summary>
    /// Create a nucleation site from a cluster of failed elements
    /// </summary>
    private FaultNucleationSite CreateNucleationSite(FEMMesh2D mesh, SimulationResults2D results, List<int> cluster)
    {
        var nodes = mesh.Nodes.ToArray();

        // Find the element with maximum yield excess
        int maxElement = cluster.OrderByDescending(e => results.YieldIndex[e]).First();

        // Calculate centroid of the cluster
        Vector2 centroid = Vector2.Zero;
        foreach (int e in cluster)
        {
            centroid += mesh.Elements[e].GetCentroid(nodes);
        }
        centroid /= cluster.Count;

        // Determine rupture mode based on stress state
        double sigma1 = results.Sigma1[maxElement];
        double sigma3 = results.Sigma3[maxElement];
        double principalAngle = results.PrincipalAngle[maxElement];

        var material = mesh.Materials.GetMaterial(mesh.Elements[maxElement].MaterialId);
        double tensileStrength = material?.TensileStrength ?? 0;

        RuptureMode mode;
        if (sigma1 > tensileStrength && sigma1 > 0)
        {
            mode = RuptureMode.TensileOpening;
        }
        else if (sigma1 > 0 && sigma3 < 0)
        {
            mode = RuptureMode.MixedMode;
        }
        else if (sigma3 < 0)
        {
            mode = RuptureMode.InPlaneShear;
        }
        else
        {
            mode = RuptureMode.Compressive;
        }

        return new FaultNucleationSite
        {
            ElementId = maxElement,
            Location = centroid,
            Mode = mode,
            YieldExcess = results.YieldIndex[maxElement] - Settings.RuptureThreshold,
            Sigma1 = sigma1,
            Sigma3 = sigma3,
            PrincipalAngle = principalAngle,
            PlasticStrain = results.PlasticStrain[maxElement],
            ClusterElementIds = cluster
        };
    }

    /// <summary>
    /// Determine if rupture mode indicates fault formation
    /// </summary>
    private bool IsValidRuptureMode(RuptureMode mode)
    {
        return mode == RuptureMode.TensileOpening ||
               mode == RuptureMode.InPlaneShear ||
               mode == RuptureMode.MixedMode;
    }

    #endregion

    #region Fault Nucleation

    /// <summary>
    /// Nucleate a new fault from a nucleation site
    /// </summary>
    private GeneratedFault NucleateFault(FEMMesh2D mesh, SimulationResults2D results,
        FaultNucleationSite site, int step, double time)
    {
        if (!IsValidRuptureMode(site.Mode))
            return null;

        // Calculate fault orientation based on stress state
        double faultAngle = CalculateFaultOrientation(site);

        // Calculate initial fault length based on cluster size
        var nodes = mesh.Nodes.ToArray();
        double clusterSize = 0;
        foreach (int e in site.ClusterElementIds)
        {
            clusterSize += Math.Sqrt(mesh.Elements[e].GetArea(nodes));
        }
        double initialLength = Math.Min(clusterSize, Settings.MaxPropagationPerStep);

        if (initialLength < Settings.MinFaultSegmentLength)
            return null;

        // Create fault points
        double angleRad = faultAngle * Math.PI / 180.0;
        Vector2 direction = new((float)Math.Cos(angleRad), (float)Math.Sin(angleRad));

        Vector2 start = site.Location - direction * (float)(initialLength / 2);
        Vector2 end = site.Location + direction * (float)(initialLength / 2);

        // Clip to mesh bounds
        var (minBound, maxBound) = mesh.GetBoundingBox();
        ClipPointToRegion(ref start, minBound, maxBound);
        ClipPointToRegion(ref end, minBound, maxBound);

        var fault = new GeneratedFault
        {
            Id = _nextFaultId++,
            Points = new List<Vector2> { start, site.Location, end },
            Mode = site.Mode,
            DipAngle = faultAngle,
            NucleationStep = step,
            CreationTime = time,
            IsActive = true
        };

        return fault;
    }

    /// <summary>
    /// Calculate fault orientation based on stress state and propagation strategy
    /// </summary>
    private double CalculateFaultOrientation(FaultNucleationSite site)
    {
        double sigma1Angle = site.PrincipalAngle;

        switch (Settings.PropagationStrategy)
        {
            case PropagationStrategy.PrincipalStressAligned:
                // Tensile fractures align with σ1 direction (perpendicular to σ3)
                return sigma1Angle;

            case PropagationStrategy.ConjugateAngle:
                // Shear faults form at Coulomb angle to σ1
                // θ = 45° - φ/2 from σ1
                double phi = Settings.FaultFrictionAngle;
                double coulombAngle = 45.0 - phi / 2.0;
                return sigma1Angle + (90 - coulombAngle);

            case PropagationStrategy.StressGuided:
                // Follow maximum shear stress direction
                // Max shear is at 45° to principal stresses
                return sigma1Angle + 45;

            case PropagationStrategy.StrainLocalization:
                // Use local strain gradient
                // For now, fall back to conjugate angle
                goto case PropagationStrategy.ConjugateAngle;

            case PropagationStrategy.EnergyMinimizing:
                // Would need more complex analysis
                // For now, fall back to conjugate angle
                goto case PropagationStrategy.ConjugateAngle;

            default:
                return sigma1Angle + 45;
        }
    }

    #endregion

    #region Fault Propagation

    /// <summary>
    /// Attempt to propagate an existing fault from its tips
    /// </summary>
    private bool PropagateExistingFault(FEMMesh2D mesh, SimulationResults2D results,
        GeneratedFault fault, int step, double time)
    {
        if (fault.Points.Count < 2) return false;

        bool propagated = false;

        // Try propagating from both tips
        propagated |= PropagateFromTip(mesh, results, fault, true);
        propagated |= PropagateFromTip(mesh, results, fault, false);

        return propagated;
    }

    /// <summary>
    /// Propagate fault from one tip
    /// </summary>
    private bool PropagateFromTip(FEMMesh2D mesh, SimulationResults2D results,
        GeneratedFault fault, bool fromStart)
    {
        Vector2 tip = fromStart ? fault.TipStart : fault.TipEnd;
        Vector2 prevPoint = fromStart ? fault.Points[1] : fault.Points[^2];

        // Current fault direction at tip
        Vector2 direction = Vector2.Normalize(tip - prevPoint);

        // Find element at tip
        int tipElement = FindElementContainingPoint(mesh, tip);
        if (tipElement < 0) return false;

        // Check if stress at tip exceeds threshold
        double yieldIndex = results.YieldIndex[tipElement];
        if (yieldIndex < Settings.RuptureThreshold)
        {
            // Deactivate fault if tip is no longer failing
            fault.IsActive = false;
            return false;
        }

        // Calculate propagation direction based on local stress
        double sigma1Angle = results.PrincipalAngle[tipElement];
        double newAngle = CalculatePropagationAngle(sigma1Angle, direction, fault.Mode);

        // Propagation length based on element size
        var nodes = mesh.Nodes.ToArray();
        double elementSize = Math.Sqrt(mesh.Elements[tipElement].GetArea(nodes));
        double propLength = Math.Min(elementSize, Settings.MaxPropagationPerStep);

        // New tip position
        double angleRad = newAngle * Math.PI / 180.0;
        Vector2 newDirection = new((float)Math.Cos(angleRad), (float)Math.Sin(angleRad));
        Vector2 newTip = tip + newDirection * (float)propLength;

        // Check bounds
        var (minBound, maxBound) = mesh.GetBoundingBox();
        if (!IsPointInRegion(newTip, minBound, maxBound))
        {
            fault.IsActive = false;
            return false;
        }

        // Add new point
        if (fromStart)
        {
            fault.Points.Insert(0, newTip);
        }
        else
        {
            fault.Points.Add(newTip);
        }

        return true;
    }

    /// <summary>
    /// Calculate propagation angle based on local stress and fault mode
    /// </summary>
    private double CalculatePropagationAngle(double sigma1Angle, Vector2 currentDirection, RuptureMode mode)
    {
        double currentAngle = Math.Atan2(currentDirection.Y, currentDirection.X) * 180 / Math.PI;

        switch (mode)
        {
            case RuptureMode.TensileOpening:
                // Tensile fractures try to align perpendicular to σ3 (parallel to σ1)
                return sigma1Angle;

            case RuptureMode.InPlaneShear:
                // Shear follows conjugate angle
                double phi = Settings.FaultFrictionAngle;
                double coulombAngle = 45.0 - phi / 2.0;
                double targetAngle = sigma1Angle + (90 - coulombAngle);

                // Blend between current direction and target
                double blendFactor = 0.7;
                return currentAngle * (1 - blendFactor) + targetAngle * blendFactor;

            case RuptureMode.MixedMode:
                // Average of tensile and shear directions
                double tensileAngle = sigma1Angle;
                double shearAngle = sigma1Angle + 45;
                return (tensileAngle + shearAngle) / 2;

            default:
                return currentAngle;
        }
    }

    #endregion

    #region Mesh Integration

    /// <summary>
    /// Insert a fault into the mesh as interface elements
    /// </summary>
    public void InsertFaultIntoMesh(FEMMesh2D mesh, GeneratedFault fault)
    {
        if (fault.Points.Count < 2) return;

        var nodes = mesh.Nodes.ToArray();

        // Process each fault segment
        for (int i = 0; i < fault.Points.Count - 1; i++)
        {
            Vector2 segStart = fault.Points[i];
            Vector2 segEnd = fault.Points[i + 1];

            // Find elements intersected by this segment
            var intersectedElements = FindIntersectedElements(mesh, segStart, segEnd);

            foreach (int elementId in intersectedElements)
            {
                // Insert interface element
                var interfaceElement = CreateInterfaceForFault(mesh, elementId, segStart, segEnd, fault);
                if (interfaceElement != null)
                {
                    fault.InterfaceElementIds.Add(interfaceElement.Id);
                }
            }
        }
    }

    /// <summary>
    /// Find all elements intersected by a line segment
    /// </summary>
    private List<int> FindIntersectedElements(FEMMesh2D mesh, Vector2 start, Vector2 end)
    {
        var result = new List<int>();
        var nodes = mesh.Nodes.ToArray();

        for (int e = 0; e < mesh.Elements.Count; e++)
        {
            var element = mesh.Elements[e];
            if (element.Type == ElementType2D.Interface4) continue; // Skip existing interfaces

            var vertices = element.NodeIds.Select(id => nodes[id].InitialPosition).ToList();

            if (SegmentIntersectsPolygon(start, end, vertices))
            {
                result.Add(e);
            }
        }

        return result;
    }

    /// <summary>
    /// Create an interface element to represent fault within an element
    /// </summary>
    private InterfaceElement4 CreateInterfaceForFault(FEMMesh2D mesh, int elementId,
        Vector2 faultStart, Vector2 faultEnd, GeneratedFault fault)
    {
        var element = mesh.Elements[elementId];
        var nodes = mesh.Nodes.ToArray();
        var vertices = element.NodeIds.Select(id => nodes[id].InitialPosition).ToList();

        // Find intersection points with element edges
        var intersections = FindEdgeIntersections(faultStart, faultEnd, vertices);
        if (intersections.Count < 2) return null;

        // Sort intersections along fault direction
        Vector2 direction = Vector2.Normalize(faultEnd - faultStart);
        intersections.Sort((a, b) =>
        {
            float da = Vector2.Dot(a - faultStart, direction);
            float db = Vector2.Dot(b - faultStart, direction);
            return da.CompareTo(db);
        });

        Vector2 p1 = intersections[0];
        Vector2 p2 = intersections[^1];

        if (Vector2.Distance(p1, p2) < Settings.MinFaultSegmentLength)
            return null;

        // Create nodes for interface
        // Small offset perpendicular to fault for interface thickness
        Vector2 normal = new(-direction.Y, direction.X);
        float offset = 0.001f; // Small offset for numerical stability

        var n1 = mesh.GetOrCreateNode(p1 - normal * offset, 0.0001);
        var n2 = mesh.GetOrCreateNode(p2 - normal * offset, 0.0001);
        var n3 = mesh.GetOrCreateNode(p2 + normal * offset, 0.0001);
        var n4 = mesh.GetOrCreateNode(p1 + normal * offset, 0.0001);

        // Create interface element
        var interfaceElement = (InterfaceElement4)mesh.AddInterface(
            n1.Id, n2.Id, n3.Id, n4.Id,
            Settings.FaultNormalStiffness,
            Settings.FaultShearStiffness,
            Settings.FaultCohesion,
            Settings.FaultFrictionAngle);

        interfaceElement.JointTensileStrength = Settings.FaultTensileStrength;

        return interfaceElement;
    }

    /// <summary>
    /// Find intersection points of a segment with polygon edges
    /// </summary>
    private List<Vector2> FindEdgeIntersections(Vector2 segStart, Vector2 segEnd, List<Vector2> polygon)
    {
        var intersections = new List<Vector2>();

        for (int i = 0; i < polygon.Count; i++)
        {
            int j = (i + 1) % polygon.Count;
            var intersection = LineSegmentIntersection(segStart, segEnd, polygon[i], polygon[j]);
            if (intersection.HasValue)
            {
                // Check for duplicates
                bool isDuplicate = intersections.Any(p =>
                    Vector2.Distance(p, intersection.Value) < 0.0001f);
                if (!isDuplicate)
                {
                    intersections.Add(intersection.Value);
                }
            }
        }

        return intersections;
    }

    #endregion

    #region Geometry Utilities

    private bool SegmentIntersectsPolygon(Vector2 segStart, Vector2 segEnd, List<Vector2> polygon)
    {
        // Check if segment intersects any edge
        for (int i = 0; i < polygon.Count; i++)
        {
            int j = (i + 1) % polygon.Count;
            if (LineSegmentIntersection(segStart, segEnd, polygon[i], polygon[j]).HasValue)
                return true;
        }

        // Check if segment is entirely inside polygon
        if (PointInPolygon((segStart + segEnd) / 2, polygon))
            return true;

        return false;
    }

    private Vector2? LineSegmentIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        float d1x = a2.X - a1.X;
        float d1y = a2.Y - a1.Y;
        float d2x = b2.X - b1.X;
        float d2y = b2.Y - b1.Y;

        float cross = d1x * d2y - d1y * d2x;
        if (Math.Abs(cross) < 1e-10f) return null;

        float t = ((b1.X - a1.X) * d2y - (b1.Y - a1.Y) * d2x) / cross;
        float u = ((b1.X - a1.X) * d1y - (b1.Y - a1.Y) * d1x) / cross;

        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
        {
            return new Vector2(a1.X + t * d1x, a1.Y + t * d1y);
        }

        return null;
    }

    private bool PointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int j = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            if ((polygon[i].Y < point.Y && polygon[j].Y >= point.Y ||
                 polygon[j].Y < point.Y && polygon[i].Y >= point.Y) &&
                (polygon[i].X + (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) *
                 (polygon[j].X - polygon[i].X) < point.X))
            {
                inside = !inside;
            }
            j = i;
        }

        return inside;
    }

    private int FindElementContainingPoint(FEMMesh2D mesh, Vector2 point)
    {
        var nodes = mesh.Nodes.ToArray();

        for (int e = 0; e < mesh.Elements.Count; e++)
        {
            var element = mesh.Elements[e];
            if (element.Type == ElementType2D.Interface4) continue;

            var vertices = element.NodeIds.Select(id => nodes[id].InitialPosition).ToList();
            if (PointInPolygon(point, vertices))
                return e;
        }

        return -1;
    }

    private bool FaultExistsNear(Vector2 location, double tolerance)
    {
        foreach (var fault in GeneratedFaults)
        {
            foreach (var point in fault.Points)
            {
                if (Vector2.Distance(point, location) < tolerance)
                    return true;
            }
        }
        return false;
    }

    private void ClipPointToRegion(ref Vector2 point, Vector2 min, Vector2 max)
    {
        point.X = Math.Max(min.X, Math.Min(max.X, point.X));
        point.Y = Math.Max(min.Y, Math.Min(max.Y, point.Y));
    }

    private bool IsPointInRegion(Vector2 point, Vector2 min, Vector2 max)
    {
        return point.X >= min.X && point.X <= max.X &&
               point.Y >= min.Y && point.Y <= max.Y;
    }

    #endregion

    #region Conversion to Discontinuity System

    /// <summary>
    /// Convert generated faults to Discontinuity2D objects for integration with JointSetManager
    /// </summary>
    public List<Discontinuity2D> ConvertToDiscontinuities()
    {
        var discontinuities = new List<Discontinuity2D>();

        foreach (var fault in GeneratedFaults)
        {
            if (fault.Points.Count < 2) continue;

            var disc = new Discontinuity2D
            {
                Id = fault.Id,
                Type = DiscontinuityType.Fault,
                StartPoint = fault.TipStart,
                EndPoint = fault.TipEnd,
                Points = new List<Vector2>(fault.Points),
                DipAngle = fault.DipAngle,
                Cohesion = Settings.FaultCohesion,
                FrictionAngle = Settings.FaultFrictionAngle,
                TensileStrength = Settings.FaultTensileStrength,
                NormalStiffness = Settings.FaultNormalStiffness,
                ShearStiffness = Settings.FaultShearStiffness,
                AccumulatedSlip = fault.TotalSlip,
                IsOpen = fault.Mode == RuptureMode.TensileOpening,
                IsSliding = fault.Mode == RuptureMode.InPlaneShear,
                Color = GetFaultColor(fault.Mode)
            };

            discontinuities.Add(disc);
        }

        return discontinuities;
    }

    /// <summary>
    /// Get visualization color based on rupture mode
    /// </summary>
    private Vector4 GetFaultColor(RuptureMode mode)
    {
        return mode switch
        {
            RuptureMode.TensileOpening => new Vector4(1.0f, 0.2f, 0.2f, 1.0f), // Red for tensile
            RuptureMode.InPlaneShear => new Vector4(0.2f, 0.2f, 1.0f, 1.0f),   // Blue for shear
            RuptureMode.MixedMode => new Vector4(0.8f, 0.2f, 0.8f, 1.0f),      // Purple for mixed
            _ => new Vector4(0.5f, 0.5f, 0.5f, 1.0f)                            // Gray default
        };
    }

    #endregion

    #region Reset and Clear

    /// <summary>
    /// Clear all generated faults and reset state
    /// </summary>
    public void Reset()
    {
        GeneratedFaults.Clear();
        PotentialNucleationSites.Clear();
        _nextFaultId = 1;
    }

    /// <summary>
    /// Remove faults that are no longer active
    /// </summary>
    public void PruneInactiveFaults()
    {
        GeneratedFaults.RemoveAll(f => !f.IsActive && f.Length < Settings.MinFaultSegmentLength);
    }

    #endregion
}
