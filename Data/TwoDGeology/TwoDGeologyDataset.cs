// GeoscientistToolkit/Data/TwoDGeology/TwoDGeologyDataset.cs

using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data.TwoDGeology.Geomechanics;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Data.TwoDGeology;

/// <summary>
///     Represents a 2D geological profile dataset with integrated geomechanical simulation capabilities.
///     Supports geological cross-section editing, restoration, and FEM-based stress/strain analysis.
/// </summary>
public class TwoDGeologyDataset : Dataset, ISerializableDataset
{
    private TwoDGeologyViewer _viewer;
    private bool _hasUnsavedChanges = false;

    // Geomechanical simulation components
    private TwoDGeomechanicalSimulator _geomechanicalSimulator;
    private TwoDGeomechanicsTools _geomechanicsTools;
    private PrimitiveManager2D _primitives;
    private JointSetManager _jointSets;
    private GeomechanicalMaterialLibrary2D _materialLibrary;

    public TwoDGeologyDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.TwoDGeology;
        InitializeGeomechanics();
    }

    public CrossSection ProfileData { get; set; }

    /// <summary>
    /// Geomechanical simulator for FEM-based stress/strain analysis
    /// </summary>
    public TwoDGeomechanicalSimulator GeomechanicalSimulator => _geomechanicalSimulator;

    /// <summary>
    /// Tools for geomechanical modeling and simulation control
    /// </summary>
    public TwoDGeomechanicsTools GeomechanicsTools => _geomechanicsTools;

    /// <summary>
    /// Collection of geometric primitives (foundations, tunnels, etc.)
    /// </summary>
    public PrimitiveManager2D Primitives => _primitives;

    /// <summary>
    /// Joint set manager for discontinuities
    /// </summary>
    public JointSetManager JointSets => _jointSets;

    /// <summary>
    /// Material library for geomechanical properties
    /// </summary>
    public GeomechanicalMaterialLibrary2D MaterialLibrary => _materialLibrary;

    /// <summary>
    /// Whether geomechanical simulation has been run
    /// </summary>
    public bool HasGeomechanicalResults => _geomechanicalSimulator?.Results != null &&
                                            _geomechanicalSimulator.Results.DisplacementMagnitude != null;

    private void InitializeGeomechanics()
    {
        _geomechanicalSimulator = new TwoDGeomechanicalSimulator();
        _geomechanicsTools = new TwoDGeomechanicsTools(_geomechanicalSimulator);
        _primitives = _geomechanicsTools.Primitives;
        _jointSets = _geomechanicsTools.JointSets;
        _materialLibrary = _geomechanicalSimulator.Mesh.Materials;
    }
    
    public bool HasUnsavedChanges 
    { 
        get => _hasUnsavedChanges;
        set => _hasUnsavedChanges = value;
    }

    public object ToSerializableObject()
    {
        return new TwoDGeologyDatasetDTO
        {
            TypeName = nameof(TwoDGeologyDataset),
            Name = Name,
            FilePath = FilePath
        };
    }

    public void RegisterViewer(TwoDGeologyViewer viewer)
    {
        _viewer = viewer;
    }

    public TwoDGeologyViewer GetViewer()
    {
        return _viewer;
    }

    public void SetRestorationData(CrossSection data)
    {
        _viewer?.SetRestorationData(data);
    }

    public void ClearRestorationData()
    {
        _viewer?.ClearRestorationData();
    }

    public override long GetSizeInBytes()
    {
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath)) 
            return new FileInfo(FilePath).Length;
        return 0;
    }

    public override void Load()
    {
        if (IsMissing || ProfileData != null)
            return;

        try
        {
            if (File.Exists(FilePath))
            {
                ProfileData = TwoDGeologySerializer.Read(FilePath);
                Logger.Log($"Loaded 2D Geology profile data for '{Name}'");
            }
            else
            {
                // Create new empty profile if file doesn't exist
                ProfileData = CreateDefaultProfile();
                Logger.Log($"Created new 2D Geology profile for '{Name}'");
                _hasUnsavedChanges = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load 2D Geology profile from '{FilePath}': {ex.Message}");
            
            // Create empty profile on error
            ProfileData = CreateDefaultProfile();
            _hasUnsavedChanges = true;
        }
    }

    public void Save()
    {
        if (ProfileData == null)
        {
            Logger.LogWarning($"No profile data to save for '{Name}'");
            return;
        }

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            TwoDGeologySerializer.Write(FilePath, ProfileData);
            _hasUnsavedChanges = false;
            Logger.Log($"Saved 2D Geology profile to '{FilePath}'");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save 2D Geology profile to '{FilePath}': {ex.Message}");
            throw;
        }
    }

    public static TwoDGeologyDataset CreateEmpty(string name, string filePath)
    {
        var dataset = new TwoDGeologyDataset(name, filePath);
        dataset.ProfileData = CreateDefaultProfile();
        dataset._hasUnsavedChanges = true;
        
        Logger.Log($"Created empty 2D geology dataset: {name}");
        return dataset;
    }
    
    private static CrossSection CreateDefaultProfile()
    {
        var profile = new CrossSection
        {
            Profile = new GeologicalMapping.ProfileGenerator.TopographicProfile
            {
                Name = "Default Profile",
                TotalDistance = 10000f, // Default 10km profile
                MinElevation = -2000f,
                MaxElevation = 1000f,
                StartPoint = new Vector2(0, 0),
                EndPoint = new Vector2(10000f, 0),
                CreatedAt = DateTime.Now,
                VerticalExaggeration = 2.0f,
                Points = new List<GeologicalMapping.ProfileGenerator.ProfilePoint>()
            },
            VerticalExaggeration = 2.0f,
            Formations = new List<ProjectedFormation>(),
            Faults = new List<GeologicalMapping.CrossSectionGenerator.ProjectedFault>()
        };

        // Generate default flat topography at sea level
        var numPoints = 50;
        
        for (var i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * profile.Profile.TotalDistance;
            
            // Simple flat topography at sea level
            var baseElevation = 0f;
            
            profile.Profile.Points.Add(new GeologicalMapping.ProfileGenerator.ProfilePoint
            {
                Position = new Vector2(distance, baseElevation),
                Distance = distance,
                Elevation = baseElevation,
                Features = new List<GeologicalMapping.GeologicalFeature>()
            });
        }
        
        // NO DEFAULT BEDROCK - start with a clean slate
        // Users can add formations as needed using the tools
        
        return profile;
    }

    public override void Unload()
    {
        // Check for unsaved changes before unloading
        if (_hasUnsavedChanges)
        {
            Logger.LogWarning($"Unloading dataset '{Name}' with unsaved changes");
        }
        
        ProfileData = null;
        _viewer?.UndoRedo.Clear(); // Clear undo history on unload
    }
    
    // Mark dataset as modified when changes are made
    public void MarkAsModified()
    {
        _hasUnsavedChanges = true;
    }

    #region Geomechanical Analysis

    /// <summary>
    /// Generate FEM mesh from the current geological cross-section.
    /// Converts formations to material zones and faults to discontinuities.
    /// </summary>
    public void GenerateMeshFromGeology(double elementSize = 50.0)
    {
        if (ProfileData == null)
        {
            Logger.LogWarning("No profile data available for mesh generation");
            return;
        }

        _geomechanicalSimulator.Mesh.Clear();
        _materialLibrary.Clear();
        _materialLibrary.LoadDefaults();

        // Create mesh for each formation
        int materialIndex = 0;
        foreach (var formation in ProfileData.Formations)
        {
            // Create material for this formation
            var material = new GeomechanicalMaterial2D
            {
                Name = formation.Name,
                Color = formation.Color,
                YoungModulus = GetDefaultYoungModulus(formation.Name),
                PoissonRatio = 0.25,
                Cohesion = GetDefaultCohesion(formation.Name),
                FrictionAngle = GetDefaultFrictionAngle(formation.Name),
                Density = GetDefaultDensity(formation.Name)
            };
            int matId = _materialLibrary.AddMaterial(material);

            // Create polygon from formation boundaries
            var polygon = new List<Vector2>();
            polygon.AddRange(formation.TopBoundary);

            var reversedBottom = new List<Vector2>(formation.BottomBoundary);
            reversedBottom.Reverse();
            polygon.AddRange(reversedBottom);

            if (polygon.Count >= 3)
            {
                _geomechanicalSimulator.Mesh.DefaultElementSize = elementSize;
                _geomechanicalSimulator.Mesh.GeneratePolygonMesh(polygon, matId, elementSize);
            }

            materialIndex++;
        }

        // Add faults as joint sets
        foreach (var fault in ProfileData.Faults)
        {
            var jointSet = new JointSet2D
            {
                Name = $"Fault - {fault.Type}",
                Type = DiscontinuityType.Fault,
                MeanDipAngle = fault.Dip,
                MeanSpacing = 1000, // Single fault
                FrictionAngle = 20,
                Cohesion = 0
            };

            var joint = new Discontinuity2D
            {
                Type = DiscontinuityType.Fault,
                Points = fault.FaultTrace,
                StartPoint = fault.FaultTrace.FirstOrDefault(),
                EndPoint = fault.FaultTrace.LastOrDefault(),
                DipAngle = fault.Dip,
                FrictionAngle = 20,
                Cohesion = 0
            };
            jointSet.Joints.Add(joint);
            _jointSets.AddJointSet(jointSet);
        }

        // Apply default boundary conditions
        _geomechanicalSimulator.Mesh.FixBottom();

        Logger.Log($"Generated mesh: {_geomechanicalSimulator.Mesh.Nodes.Count} nodes, {_geomechanicalSimulator.Mesh.Elements.Count} elements");
    }

    /// <summary>
    /// Run geomechanical simulation on the current mesh
    /// </summary>
    public async Task RunGeomechanicalSimulationAsync(CancellationToken cancellationToken = default)
    {
        if (_geomechanicalSimulator.Mesh.Elements.Count == 0)
        {
            Logger.LogWarning("No mesh available. Generate mesh first.");
            return;
        }

        _geomechanicalSimulator.ApplyGravity = true;
        _geomechanicalSimulator.AnalysisType = AnalysisType2D.QuasiStatic;
        _geomechanicalSimulator.NumLoadSteps = 10;

        await _geomechanicalSimulator.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Get simulation results for a specific formation
    /// </summary>
    public (double avgStress, double maxStress, double avgStrain, int yieldedElements) GetFormationResults(string formationName)
    {
        if (_geomechanicalSimulator.Results == null)
            return (0, 0, 0, 0);

        var formation = ProfileData?.Formations.FirstOrDefault(f => f.Name == formationName);
        if (formation == null)
            return (0, 0, 0, 0);

        var material = _materialLibrary.Materials.Values.FirstOrDefault(m => m.Name == formationName);
        if (material == null)
            return (0, 0, 0, 0);

        // Find elements with this material
        var elementIndices = _geomechanicalSimulator.Mesh.Elements
            .Where(e => e.MaterialId == material.Id)
            .Select(e => e.Id)
            .ToList();

        if (elementIndices.Count == 0)
            return (0, 0, 0, 0);

        var r = _geomechanicalSimulator.Results;
        double avgStress = elementIndices.Average(i => r.VonMisesStress[i]);
        double maxStress = elementIndices.Max(i => r.VonMisesStress[i]);
        double avgStrain = elementIndices.Average(i =>
            Math.Sqrt(r.StrainXX[i] * r.StrainXX[i] + r.StrainYY[i] * r.StrainYY[i]));
        int yieldedElements = elementIndices.Count(i => r.HasYielded[i]);

        return (avgStress, maxStress, avgStrain, yieldedElements);
    }

    /// <summary>
    /// Add a geometric primitive to the model
    /// </summary>
    public void AddPrimitive(GeometricPrimitive2D primitive)
    {
        _primitives.AddPrimitive(primitive);
        MarkAsModified();
    }

    /// <summary>
    /// Add a joint set to the model
    /// </summary>
    public void AddJointSet(JointSet2D jointSet)
    {
        _jointSets.AddJointSet(jointSet);
        MarkAsModified();
    }

    /// <summary>
    /// Generate automatic joint sets based on geological rules
    /// </summary>
    public void GenerateGeologicalJoints(double spacing = 2.0, bool includeVertical = true, bool includeBedding = true)
    {
        if (ProfileData == null) return;

        var (min, max) = _geomechanicalSimulator.Mesh.GetBoundingBox();
        if (min == max)
        {
            min = new Vector2(0, -1000);
            max = new Vector2(ProfileData.Profile.TotalDistance, ProfileData.Profile.MaxElevation);
        }

        if (includeVertical)
        {
            var verticalSet = JointSetManager.Presets.CreateVerticalJoints(spacing);
            verticalSet.GenerateInRegion(min, max);
            _jointSets.AddJointSet(verticalSet);
        }

        if (includeBedding && ProfileData.Formations.Count > 0)
        {
            // Estimate average bedding dip from formations
            double avgDip = 15;  // Default
            foreach (var formation in ProfileData.Formations)
            {
                if (formation.TopBoundary.Count >= 2)
                {
                    var first = formation.TopBoundary.First();
                    var last = formation.TopBoundary.Last();
                    double dx = last.X - first.X;
                    double dy = last.Y - first.Y;
                    if (Math.Abs(dx) > 1)
                    {
                        avgDip = Math.Atan(dy / dx) * 180 / Math.PI;
                    }
                }
            }

            var beddingSet = JointSetManager.Presets.CreateBeddingPlanes(spacing * 0.5, avgDip);
            beddingSet.GenerateInRegion(min, max);
            _jointSets.AddJointSet(beddingSet);
        }

        MarkAsModified();
    }

    // Default material property estimation based on formation name
    private double GetDefaultYoungModulus(string formationName)
    {
        var lower = formationName.ToLower();
        if (lower.Contains("granite") || lower.Contains("basalt"))
            return 50e9;
        if (lower.Contains("limestone") || lower.Contains("dolomite"))
            return 30e9;
        if (lower.Contains("sandstone"))
            return 20e9;
        if (lower.Contains("shale") || lower.Contains("mudstone"))
            return 10e9;
        if (lower.Contains("clay") || lower.Contains("soil"))
            return 50e6;
        if (lower.Contains("sand") || lower.Contains("gravel"))
            return 100e6;
        return 20e9; // Default
    }

    private double GetDefaultCohesion(string formationName)
    {
        var lower = formationName.ToLower();
        if (lower.Contains("granite") || lower.Contains("basalt"))
            return 30e6;
        if (lower.Contains("limestone") || lower.Contains("dolomite"))
            return 15e6;
        if (lower.Contains("sandstone"))
            return 10e6;
        if (lower.Contains("shale") || lower.Contains("mudstone"))
            return 5e6;
        if (lower.Contains("clay"))
            return 20e3;
        if (lower.Contains("sand"))
            return 0;
        return 10e6;
    }

    private double GetDefaultFrictionAngle(string formationName)
    {
        var lower = formationName.ToLower();
        if (lower.Contains("granite") || lower.Contains("basalt"))
            return 45;
        if (lower.Contains("limestone") || lower.Contains("dolomite"))
            return 38;
        if (lower.Contains("sandstone"))
            return 35;
        if (lower.Contains("shale") || lower.Contains("mudstone"))
            return 25;
        if (lower.Contains("clay"))
            return 20;
        if (lower.Contains("sand"))
            return 33;
        return 35;
    }

    private double GetDefaultDensity(string formationName)
    {
        var lower = formationName.ToLower();
        if (lower.Contains("granite") || lower.Contains("basalt"))
            return 2700;
        if (lower.Contains("limestone") || lower.Contains("dolomite"))
            return 2600;
        if (lower.Contains("sandstone"))
            return 2400;
        if (lower.Contains("shale") || lower.Contains("mudstone"))
            return 2500;
        if (lower.Contains("clay"))
            return 1800;
        if (lower.Contains("sand"))
            return 1600;
        return 2500;
    }

    #endregion
}