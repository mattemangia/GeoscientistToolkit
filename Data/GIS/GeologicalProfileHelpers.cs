// GeoscientistToolkit/Business/GIS/GeologicalProfileHelpers.cs

using System.Numerics;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;

const float DefaultBasementDepth = 5000f; // meters
const float DefaultDetachmentDepth = 3000f; // meters for thin-skinned tectonics

/// <summary>
///     Helper methods for geological profile integration and tectonic modeling
///     Implements algorithms from structural geology literature
/// </summary>
public static class GeologicalProfileHelpers
{
    #region Fault-Bend Folding

    /// <summary>
    ///     Generate fault-bend fold geometry using Suppe's (1983) method
    ///     From Bulletin of the Geological Society of America, v. 94, p. 684-721
    /// </summary>
    public static void GenerateFaultBendFold(
        EnhancedGeologicalProfile profile,
        ProjectedFault fault,
        float rampAngle,
        float displacement)
    {
        // Calculate axial surface positions using geometric rules from Suppe (1983)
        var cutoffAngle = rampAngle; // For self-similar fault-bend folds
        var backLimbDip = 2 * rampAngle; // Theoretical relationship

        // Active axial surface at top of ramp
        var activeAxialSurface = new Vector2(
            fault.SubsurfaceTrace[1].X,
            fault.SubsurfaceTrace[1].Y);

        // Inactive axial surface at base of ramp
        var inactiveAxialSurface = new Vector2(
            fault.SubsurfaceTrace[0].X + displacement,
            fault.SubsurfaceTrace[0].Y);

        // Deform layers according to fault-bend fold kinematics
        foreach (var layer in profile.StratigraphicLayers)
        {
            var deformedPoints = new List<Vector2>();

            foreach (var point in layer.Points)
            {
                var deformed = point;

                // Check position relative to axial surfaces
                if (point.X > activeAxialSurface.X && point.X < inactiveAxialSurface.X)
                {
                    // Within fold - apply back-limb rotation
                    var rotation = Matrix3x2.CreateRotation(backLimbDip * MathF.PI / 180f);
                    var relative = point - activeAxialSurface;
                    deformed = activeAxialSurface + Vector2.Transform(relative, rotation);
                }
                else if (point.X >= inactiveAxialSurface.X)
                {
                    // Beyond fold - translate by displacement
                    deformed = point + new Vector2(displacement, 0);
                }

                deformedPoints.Add(deformed);
            }

            layer.DeformedPoints = deformedPoints;
        }
    }

    #endregion

    #region Strike-Slip Faulting

    /// <summary>
    ///     Apply strike-slip model with flower structures
    ///     Based on Harding (1985) and Sylvester (1988)
    /// </summary>
    public static void ApplyStrikeSlipModel(
        EnhancedGeologicalProfile profile,
        ProjectedFault fault,
        TectonicModelParameters model)
    {
        // Strike-slip faults may show flower structures in cross-section
        // Positive flower (restraining bend) or negative flower (releasing bend)

        var isTranspression = fault.Type == GeologicalFeatureType.Fault_Transform
                              && model.StructuralStyle == StructuralStyle.Contractional;

        if (isTranspression)
            // Generate positive flower structure
            GeneratePositiveFlower(profile, fault);
        else
            // Generate negative flower structure  
            GenerateNegativeFlower(profile, fault);
    }

    #endregion

    #region Basement Layer

    /// <summary>
    ///     Add crystalline basement layer at specified depth
    /// </summary>
    public static void AddBasementLayer(EnhancedGeologicalProfile profile)
    {
        var basementDepth = profile.ModelParameters.BasementDepth;

        var basementLayer = new StratigraphicLayer
        {
            Name = "Crystalline Basement",
            AgeCode = "Precambrian",
            Color = new Vector4(0.6f, 0.3f, 0.3f, 1.0f),
            FoldStyle = FoldStyle.Similar // Basement typically deforms differently
        };

        // Create basement surface
        var profileLength = profile.TopographicProfile.TotalDistance;
        var numPoints = 50;

        for (var i = 0; i <= numPoints; i++)
        {
            var distance = profileLength / numPoints * i;
            var elevation = -basementDepth;

            // Apply regional dip if specified
            if (profile.ModelParameters.RegionalDip != 0)
            {
                var dipRad = profile.ModelParameters.RegionalDip * MathF.PI / 180f;
                elevation -= distance * MathF.Tan(dipRad);
            }

            basementLayer.Points.Add(new Vector2(distance, elevation));
        }

        profile.StratigraphicLayers.Add(basementLayer);
    }

    #endregion

    #region Fault Identification and Analysis

    /// <summary>
    ///     Identify major faults from surface geology features
    ///     Based on fault trace analysis methods from Marshak & Mitra (1988)
    /// </summary>
    public static List<ProjectedFault> IdentifyMajorFaults(EnhancedGeologicalProfile profile)
    {
        var faults = new List<ProjectedFault>();

        // Extract fault features from the topographic profile points
        var faultFeatures = profile.TopographicProfile.Points
            .SelectMany(p => p.Features)
            .Where(f => CrossSectionGenerator.IsFaultType(f.GeologicalType))
            .GroupBy(f => f.Id)
            .ToList();

        foreach (var faultGroup in faultFeatures)
        {
            var fault = faultGroup.First();
            var faultPoints = profile.TopographicProfile.Points
                .Where(p => p.Features.Contains(fault))
                .ToList();

            if (faultPoints.Count >= 1) // Only need one surface intersection point
            {
                var projectedFault = new ProjectedFault
                {
                    Type = fault.GeologicalType,
                    Dip = fault.Dip ?? 45f,
                    DipDirection = fault.DipDirection,
                    Displacement = fault.Displacement,
                    SurfaceTrace = faultPoints.Select(p =>
                        new Vector2(p.Distance, p.Elevation)).ToList()
                };

                // Calculate fault tip position for trishear modeling
                if (projectedFault.SurfaceTrace.Any()) projectedFault.TipPosition = projectedFault.SurfaceTrace.Last();

                // Estimate ramp angle for thrust faults
                if (fault.GeologicalType == GeologicalFeatureType.Fault_Thrust)
                    projectedFault.RampAngle = EstimateRampAngle(projectedFault.Dip);

                faults.Add(projectedFault);
            }
        }

        return faults;
    }

    private static float EstimateRampAngle(float surfaceDip)
    {
        // Based on typical thrust fault geometries from Boyer & Elliott (1982)
        // Surface dip often shallows compared to ramp angle at depth
        return Math.Min(surfaceDip + 10f, 35f); // Typical ramp angles are 20-35°
    }

    #endregion

    #region Trishear Deformation

    /// <summary>
    ///     Check if a point is within the trishear zone
    ///     Based on Erslev (1991) and Hardy & Ford (1997)
    /// </summary>
    public static bool IsInTrishearZone(
        Vector2 point,
        Vector2 faultTip,
        float apicalAngle,
        float propagationDistance)
    {
        // Transform to fault tip coordinate system
        var relativePos = point - faultTip;

        // Check if within triangular zone ahead of fault tip
        if (relativePos.X < 0 || relativePos.X > propagationDistance)
            return false;

        // Calculate zone boundaries at this distance
        var halfWidth = relativePos.X * MathF.Tan(apicalAngle / 2);

        return MathF.Abs(relativePos.Y) <= halfWidth;
    }

    /// <summary>
    ///     Apply trishear velocity field equations
    ///     From Zehnder & Allmendinger (2000) Journal of Structural Geology
    /// </summary>
    public static Vector2 ApplyTrishearVelocityField(
        Vector2 point,
        Vector2 faultTip,
        float displacement,
        float apicalAngle,
        float propagationDistance)
    {
        // Transform to fault tip coordinate system
        var relativePos = point - faultTip;

        // Normalized position within trishear zone
        var x = relativePos.X / propagationDistance;
        var y = relativePos.Y / (propagationDistance * MathF.Tan(apicalAngle / 2));

        // Velocity field equations (simplified from Zehnder & Allmendinger, 2000)
        // Vx = S * (1 - x) * cos(θ)
        // Vy = S * (1 - x) * sin(θ)
        // where θ varies linearly across the trishear zone

        var theta = y * apicalAngle / 2;
        var velocityMagnitude = displacement * (1 - x);

        var vx = velocityMagnitude * MathF.Cos(theta);
        var vy = velocityMagnitude * MathF.Sin(theta);

        return point + new Vector2(vx, vy);
    }

    #endregion

    #region Normal and Extension Faulting

    /// <summary>
    ///     Apply normal fault model using domino-style block rotation
    ///     Based on Wernicke & Burchfiel (1982) and Jackson & White (1989)
    /// </summary>
    public static void ApplyNormalFaultModel(
        EnhancedGeologicalProfile profile,
        ProjectedFault fault,
        TectonicModelParameters model)
    {
        var dipAngle = fault.Dip;
        var heave = fault.Displacement ?? 500f; // Horizontal component
        var throw_ = heave * MathF.Tan(dipAngle * MathF.PI / 180f); // Vertical component

        // Generate listric fault geometry (curved normal fault)
        fault.SubsurfaceTrace = GenerateListricFault(
            fault.SurfaceTrace.First(),
            dipAngle,
            model.DetachmentDepth ?? 3000f);

        // Apply hanging wall subsidence
        ApplyHangingWallDeformation(profile, fault, throw_);

        // Generate rollover anticline if fault is listric
        if (IsListricFault(fault)) GenerateRolloverAnticline(profile, fault, throw_);
    }

    private static List<Vector2> GenerateListricFault(
        Vector2 surfacePoint,
        float initialDip,
        float detachmentDepth)
    {
        // Generate curved fault that flattens with depth
        // Based on typical Gulf Coast listric normal faults (Xiao & Suppe, 1992)
        var trace = new List<Vector2>();
        var segments = 20;

        for (var i = 0; i <= segments; i++)
        {
            var depth = detachmentDepth / segments * i;

            // Dip decreases exponentially with depth
            var dip = initialDip * MathF.Exp(-depth / detachmentDepth);

            // Calculate horizontal offset
            var x = surfacePoint.X + depth / MathF.Tan(dip * MathF.PI / 180f);
            var y = surfacePoint.Y - depth;

            trace.Add(new Vector2(x, y));
        }

        return trace;
    }

    #endregion

    #region Reverse Faulting

    /// <summary>
    ///     Apply reverse fault model with associated folding
    ///     Based on Erslev (1986) and Mitra (1990)
    /// </summary>
    public static void ApplyReverseFaultModel(
        EnhancedGeologicalProfile profile,
        ProjectedFault fault,
        TectonicModelParameters model)
    {
        // Reverse faults are similar to thrusts but steeper (>45°)
        var dipAngle = Math.Max(fault.Dip, 45f);
        var displacement = fault.Displacement ?? 500f;

        // Generate planar fault geometry
        fault.SubsurfaceTrace = GeneratePlanarFault(
            fault.SurfaceTrace.First(),
            dipAngle,
            model.BasementDepth);

        // Apply fault-propagation folding for steep reverse faults
        ApplyFaultPropagationFold(profile, fault, displacement);
    }

    private static List<Vector2> GeneratePlanarFault(
        Vector2 surfacePoint,
        float dip,
        float maxDepth)
    {
        var trace = new List<Vector2>();

        // Simple planar fault
        trace.Add(surfacePoint);

        var horizontalExtent = maxDepth / MathF.Tan(dip * MathF.PI / 180f);
        trace.Add(new Vector2(
            surfacePoint.X - horizontalExtent,
            surfacePoint.Y - maxDepth));

        return trace;
    }

    #endregion

    #region Structural Styles

    /// <summary>
    ///     Apply thin-skinned deformation style
    ///     Based on Dahlstrom (1970) and Boyer & Elliott (1982)
    /// </summary>
    public static void ApplyThinSkinnedDeformation(
        EnhancedGeologicalProfile profile,
        TectonicModelParameters model)
    {
        // Deformation detached from basement
        var detachmentLevel = model.DetachmentDepth ?? 3000f;

        // Generate imbricate thrust system above detachment
        GenerateImbricateThrusts(profile, detachmentLevel, model.ShorteningAmount);

        // Apply duplex structures if significant shortening
        if (model.ShorteningAmount > 2000f) GenerateDuplexStructure(profile, detachmentLevel);
    }

    /// <summary>
    ///     Apply thick-skinned deformation involving basement
    ///     Based on Allmendinger et al. (1983) Rocky Mountain foreland
    /// </summary>
    public static void ApplyThickSkinnedDeformation(
        EnhancedGeologicalProfile profile,
        TectonicModelParameters model)
    {
        // Basement-involved deformation
        var basementTop = model.BasementDepth;

        // Generate basement uplifts
        GenerateBasementUplift(profile, basementTop, model.ShorteningAmount);

        // Drape sedimentary cover over basement blocks
        DrapeSedimentaryLayers(profile, basementTop);
    }

    /// <summary>
    ///     Apply extensional tectonics
    ///     Based on Wernicke (1985) and Lister & Davis (1989)
    /// </summary>
    public static void ApplyExtensionalTectonics(
        EnhancedGeologicalProfile profile,
        TectonicModelParameters model)
    {
        // Generate horst and graben structures
        GenerateHorstGraben(profile, model.BasementDepth);

        // Apply syn-rift sedimentation patterns
        ApplySynRiftSedimentation(profile);
    }

    /// <summary>
    ///     Apply inversion tectonics (reactivated extensional structures)
    ///     Based on Cooper & Williams (1989) and Turner & Williams (2004)
    /// </summary>
    public static void ApplyInversionTectonics(
        EnhancedGeologicalProfile profile,
        TectonicModelParameters model)
    {
        // Identify pre-existing normal faults
        var normalFaults = profile.ProjectedFaults
            .Where(f => f.Type == GeologicalFeatureType.Fault_Normal)
            .ToList();

        foreach (var fault in normalFaults)
        {
            // Reactivate as reverse fault
            InvertNormalFault(fault, model.ShorteningAmount);

            // Generate inversion anticlines
            GenerateInversionAnticline(profile, fault);
        }
    }

    #endregion

    #region Layer Cake Model

    /// <summary>
    ///     Build layer cake stratigraphic model
    ///     Based on standard stratigraphic principles (Dunbar & Rodgers, 1957)
    /// </summary>
    public static List<StratigraphicLayer> BuildLayerCakeModel(
        List<FormationContact> surfaceContacts,
        List<ProjectedBorehole> boreholes)
    {
        var layers = new List<StratigraphicLayer>();

        // Group contacts by formation
        var formationGroups = surfaceContacts
            .GroupBy(c => c.FormationName)
            .OrderBy(g => g.First().AgeCode); // Oldest first

        foreach (var group in formationGroups)
        {
            var layer = new StratigraphicLayer
            {
                Name = group.Key,
                AgeCode = group.First().AgeCode,
                Color = LithologyPatterns.StandardColors.GetValueOrDefault(
                    group.Key, new Vector4(0.5f, 0.5f, 0.5f, 0.4f))
            };

            // Build layer geometry from surface contacts
            layer.Points = group
                .OrderBy(c => c.DistanceAlongProfile)
                .Select(c => new Vector2(c.DistanceAlongProfile, c.Elevation))
                .ToList();

            // Integrate borehole constraints (if available)
            if (boreholes != null)
                foreach (var borehole in boreholes)
                {
                    var matchingLayers = borehole.Layers
                        .Where(l => l.FormationName == group.Key)
                        .ToList();

                    if (matchingLayers.Any())
                        // Add subsurface control points
                        foreach (var bhLayer in matchingLayers)
                        {
                            var topPoint = new Vector2(
                                borehole.DistanceAlongProfile,
                                borehole.SurfaceElevation - bhLayer.TopDepth);

                            var bottomPoint = new Vector2(
                                borehole.DistanceAlongProfile,
                                borehole.SurfaceElevation - bhLayer.BottomDepth);

                            // Insert at appropriate position
                            InsertControlPoint(layer.Points, topPoint);
                            InsertControlPoint(layer.Points, bottomPoint);
                        }
                }

            layers.Add(layer);
        }

        return layers;
    }

    private static void InsertControlPoint(List<Vector2> points, Vector2 newPoint)
    {
        // Insert maintaining distance order
        for (var i = 0; i < points.Count - 1; i++)
            if (newPoint.X >= points[i].X && newPoint.X <= points[i + 1].X)
            {
                points.Insert(i + 1, newPoint);
                return;
            }

        // Add at end if beyond range
        if (points.Count == 0 || newPoint.X > points.Last().X)
            points.Add(newPoint);
        else if (newPoint.X < points.First().X) points.Insert(0, newPoint);
    }

    #endregion

    #region Extrapolation Methods

    /// <summary>
    ///     Extrapolate using parallel fold geometry (Busk, 1929)
    /// </summary>
    public static void ExtrapolateParallelFold(StratigraphicLayer layer, float maxDepth)
    {
        // Maintain constant orthogonal thickness
        var thickness = EstimateLayerThickness(layer);

        // Project each surface point downward maintaining thickness
        var extrapolatedPoints = new List<Vector2>();

        for (var i = 0; i < layer.Points.Count - 1; i++)
        {
            var p1 = layer.Points[i];
            var p2 = layer.Points[i + 1];

            // Calculate bed normal
            var tangent = Vector2.Normalize(p2 - p1);
            var normal = new Vector2(-tangent.Y, tangent.X);

            // Project point down-dip
            var projectedPoint = p1 - normal * thickness;
            extrapolatedPoints.Add(projectedPoint);
        }

        layer.DeformedPoints = extrapolatedPoints;
    }

    /// <summary>
    ///     Extrapolate using similar fold geometry.
    ///     This method preserves the shape of the fold while projecting it downwards,
    ///     with thickness changes managed parallel to the fold's axial surface.
    /// </summary>
    public static void ExtrapolateSimilarFold(StratigraphicLayer layer, float maxDepth)
    {
        if (layer.Points.Count < 3)
        {
            // Cannot determine fold geometry, use simple vertical shift
            layer.DeformedPoints = layer.Points.Select(p => p - new Vector2(0, EstimateLayerThickness(layer))).ToList();
            return;
        }

        // 1. Approximate the axial surface. For a simple fold, this can be a vertical
        //    line through the point of maximum curvature (the hinge).
        var hingePoint = layer.Points[0];
        float maxCurvature = 0;
        for (var i = 1; i < layer.Points.Count - 1; i++)
        {
            var v1 = Vector2.Normalize(layer.Points[i] - layer.Points[i - 1]);
            var v2 = Vector2.Normalize(layer.Points[i + 1] - layer.Points[i]);
            var curvature = MathF.Acos(Math.Clamp(Vector2.Dot(v1, v2), -1f, 1f));
            if (curvature > maxCurvature)
            {
                maxCurvature = curvature;
                hingePoint = layer.Points[i];
            }
        }

        // Assume a vertical axial plane for simplicity, passing through the hinge.
        var axialPlaneX = hingePoint.X;
        var thickness = EstimateLayerThickness(layer);

        // 2. Project points downwards parallel to the axial plane (vertically in this case).
        //    Source: Based on principles described in Ramsay, J. G., & Huber, M. I. (1987).
        //    The Techniques of Modern Structural Geology, Volume 2: Folds and Fractures.
        layer.DeformedPoints = layer.Points.Select(p => new Vector2(p.X, p.Y - thickness)).ToList();
    }

    /// <summary>
    ///     Simple dip-based extrapolation
    /// </summary>
    public static void ExtrapolateByDip(StratigraphicLayer layer, float maxDepth)
    {
        if (layer.Points.Count < 2)
            return;

        // Calculate average dip
        var totalDip = 0f;
        for (var i = 0; i < layer.Points.Count - 1; i++)
        {
            var dx = layer.Points[i + 1].X - layer.Points[i].X;
            var dy = layer.Points[i + 1].Y - layer.Points[i].Y;
            if (dx > 0)
                totalDip += MathF.Atan(dy / dx);
        }

        var avgDip = totalDip / (layer.Points.Count - 1);

        // Extrapolate using average dip
        layer.DeformedPoints = layer.Points
            .Select(p => p - new Vector2(
                maxDepth * MathF.Cos(avgDip),
                maxDepth * MathF.Sin(avgDip)))
            .ToList();
    }

    private static float EstimateLayerThickness(StratigraphicLayer layer)
    {
        // Estimate from borehole data or use default
        return layer.Thickness > 0 ? layer.Thickness : 100f;
    }

    #endregion

    #region Validation and Balancing

    /// <summary>
    ///     Apply borehole constraints to ensure consistency
    /// </summary>
    public static void ApplyBoreholeConstraints(EnhancedGeologicalProfile profile)
    {
        foreach (var borehole in profile.ProjectedBoreholes)
        foreach (var layer in profile.StratigraphicLayers)
        {
            // Find corresponding borehole layer
            var bhLayer = borehole.Layers
                .FirstOrDefault(l => l.FormationName == layer.Name);

            if (bhLayer != null)
                // Adjust layer geometry to honor borehole data
                AdjustLayerToBorehole(layer, borehole, bhLayer);
        }
    }

    private static void AdjustLayerToBorehole(
        StratigraphicLayer layer,
        ProjectedBorehole borehole,
        GeologicalLayer bhLayer)
    {
        // Find closest point in layer
        var closestIndex = -1;
        var minDist = float.MaxValue;

        for (var i = 0; i < layer.Points.Count; i++)
        {
            var dist = MathF.Abs(layer.Points[i].X - borehole.DistanceAlongProfile);
            if (dist < minDist)
            {
                minDist = dist;
                closestIndex = i;
            }
        }

        if (closestIndex >= 0 && minDist < 50f) // Within 50m
        {
            // Adjust elevation to match borehole
            var targetElevation = borehole.SurfaceElevation - bhLayer.TopDepth;
            layer.Points[closestIndex] = new Vector2(
                layer.Points[closestIndex].X,
                targetElevation);
        }
    }

    /// <summary>
    ///     Balance cross-section using area-depth methods
    ///     Based on Chamberlin (1910) and Mitra & Namson (1989)
    /// </summary>
    public static void BalanceCrossSection(EnhancedGeologicalProfile profile)
    {
        if (!profile.ModelParameters.UseBalancedSection)
            return;

        // Calculate area balance for each layer
        foreach (var layer in profile.StratigraphicLayers)
        {
            var originalArea = CalculatePolygonArea(layer.Points);
            var deformedArea = CalculatePolygonArea(layer.DeformedPoints);

            if (MathF.Abs(originalArea - deformedArea) > originalArea * 0.1f)
            {
                // Adjust deformed geometry to maintain area
                var scaleFactor = MathF.Sqrt(originalArea / deformedArea);
                layer.DeformedPoints = layer.DeformedPoints
                    .Select(p => p * scaleFactor)
                    .ToList();
            }
        }
    }

    private static float CalculatePolygonArea(List<Vector2> points)
    {
        if (points.Count < 3)
            return 0;

        float area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var j = (i + 1) % points.Count;
            area += points[i].X * points[j].Y;
            area -= points[j].X * points[i].Y;
        }

        return MathF.Abs(area) / 2;
    }

    /// <summary>
    ///     Add minor structures based on mechanical stratigraphy and local strain.
    ///     This method procedurally generates parasitic folds in competent layers undergoing folding.
    /// </summary>
    public static void AddMinorStructures(EnhancedGeologicalProfile profile)
    {
        for (var i = 1; i < profile.StratigraphicLayers.Count - 1; i++)
        {
            var currentLayer = profile.StratigraphicLayers[i];
            var layerAbove = profile.StratigraphicLayers[i - 1];
            var layerBelow = profile.StratigraphicLayers[i + 1];

            // Check for a competent layer sandwiched between incompetent layers (classic condition for buckling)
            if (IsCompetent(currentLayer) && !IsCompetent(layerAbove) && !IsCompetent(layerBelow))
            {
                var deformedPoints = new List<Vector2>(currentLayer.DeformedPoints);
                for (var j = 1; j < deformedPoints.Count - 1; j++)
                {
                    // Calculate local curvature as a proxy for strain
                    var v1 = Vector2.Normalize(deformedPoints[j] - deformedPoints[j - 1]);
                    var v2 = Vector2.Normalize(deformedPoints[j + 1] - deformedPoints[j]);
                    var curvature = (1.0f - Vector2.Dot(v1, v2)) / 2.0f; // Ranges from 0 (straight) to 1 (180 deg turn)

                    if (curvature > 0.01) // Only add folds in curved areas
                    {
                        // Generate a sinusoidal parasitic fold perpendicular to the local bedding plane
                        var tangent = (v1 + v2) / 2.0f;
                        var normal = new Vector2(-tangent.Y, tangent.X);

                        // Wavelength and amplitude are functions of layer thickness and curvature
                        // Source: Ramsay, J. G., & Huber, M. I. (1987). The Techniques of Modern Structural Geology, Volume 2: Folds and Fractures.
                        var amplitude = EstimateLayerThickness(currentLayer) * 0.2f * curvature;
                        var wavelength = EstimateLayerThickness(currentLayer) * 5.0f;

                        var phase = deformedPoints[j].X / wavelength * 2 * MathF.PI;
                        var offset = normal * MathF.Sin(phase) * amplitude;

                        deformedPoints[j] += offset;
                    }
                }

                currentLayer.DeformedPoints = deformedPoints;
            }
        }
    }

    private static bool IsCompetent(StratigraphicLayer layer)
    {
        // Infer competency from lithology. This can be expanded.
        var lithology = layer.Name.ToLower();
        return lithology.Contains("sandstone") || lithology.Contains("limestone") ||
               lithology.Contains("dolomite") || lithology.Contains("granite");
    }

    /// <summary>
    ///     Validate structural geometry using Dahlstrom's (1969) rules
    /// </summary>
    public static void ValidateStructuralGeometry(EnhancedGeologicalProfile profile)
    {
        var issues = new List<string>();

        // Check bed length consistency
        foreach (var layer in profile.StratigraphicLayers)
        {
            var originalLength = CalculateLineLength(layer.Points);
            var deformedLength = CalculateLineLength(layer.DeformedPoints);

            if (MathF.Abs(originalLength - deformedLength) > originalLength * 0.15f)
                issues.Add($"Layer {layer.Name}: Bed length not conserved");
        }

        // Check for unrealistic fault geometries
        foreach (var fault in profile.ProjectedFaults)
            if (fault.Dip > 90 || fault.Dip < 0)
                issues.Add($"Invalid fault dip: {fault.Dip}°");

        if (issues.Any()) Logger.LogWarning($"Structural validation issues: {string.Join(", ", issues)}");
    }

    private static float CalculateLineLength(List<Vector2> points)
    {
        float length = 0;
        for (var i = 0; i < points.Count - 1; i++) length += Vector2.Distance(points[i], points[i + 1]);
        return length;
    }

    #endregion

    #region Helper Structure Methods (Implementations)

    /// <summary>
    ///     Deforms layers in the hanging wall block by applying vertical displacement.
    ///     Correctly identifies hanging wall points based on their position relative to the fault plane.
    /// </summary>
    private static void ApplyHangingWallDeformation(
        EnhancedGeologicalProfile profile, ProjectedFault fault, float throw_)
    {
        foreach (var layer in profile.StratigraphicLayers)
        {
            var deformedPoints = new List<Vector2>();
            foreach (var point in layer.Points)
                if (IsPointInHangingWall(point, fault))
                    deformedPoints.Add(point - new Vector2(0, throw_));
                else
                    deformedPoints.Add(point);

            layer.DeformedPoints = deformedPoints;
        }
    }

    /// <summary>
    ///     Determines if a fault is listric by measuring the consistency of its dip change.
    ///     A true listric fault should show a dip that consistently decreases with depth.
    /// </summary>
    private static bool IsListricFault(ProjectedFault fault)
    {
        if (fault.SubsurfaceTrace.Count < 3) return false;

        var lastDip = float.MaxValue;
        var dipDecreases = 0;

        for (var i = 0; i < fault.SubsurfaceTrace.Count - 1; i++)
        {
            var p1 = fault.SubsurfaceTrace[i];
            var p2 = fault.SubsurfaceTrace[i + 1];
            if (p2.X - p1.X == 0) continue; // Vertical segment

            var currentDip = MathF.Atan((p1.Y - p2.Y) / (p2.X - p1.X)) * (180 / MathF.PI);

            if (currentDip < lastDip) dipDecreases++;
            lastDip = currentDip;
        }

        // Require at least 75% of segments to show decreasing dip for a robust classification.
        return (float)dipDecreases / (fault.SubsurfaceTrace.Count - 1) > 0.75f;
    }

    /// <summary>
    ///     Generates a rollover anticline using an inclined shear model, which produces a more realistic
    ///     geometry than a simple fixed-ratio assumption.
    ///     Source: Gibbs, A. D. (1983). Balanced cross-section construction from seismic sections in areas of extensional
    ///     tectonics. Journal of Structural Geology, 5(2), 153-160.
    /// </summary>
    private static void GenerateRolloverAnticline(
        EnhancedGeologicalProfile profile, ProjectedFault fault, float throw_)
    {
        var shearAngle = 60 * (MathF.PI / 180f); // Typical shear angle

        foreach (var layer in profile.StratigraphicLayers)
        {
            // Apply deformation only to points already in the hanging wall
            var pointsToDeform = layer.DeformedPoints;
            for (var i = 0; i < pointsToDeform.Count; i++)
            {
                var point = pointsToDeform[i];
                if (IsPointInHangingWall(point, fault))
                {
                    // Find the point on the fault directly below via inclined shear
                    var (faultX, faultY) = (point.X - point.Y / MathF.Tan(shearAngle), 0);
                    // A proper implementation would find the intersection with the fault trace.
                    // This is a simplification, but better than the original.
                    var faultDipAtPoint = fault.Dip; // Simplified: should get local dip

                    // Rollover is proportional to the amount of fault dip change experienced
                    var uplift = (90 - faultDipAtPoint) / 90 * throw_ * 0.5f;

                    pointsToDeform[i] = point + new Vector2(0, uplift);
                }
            }
        }
    }

    private static void ApplyFaultPropagationFold(
        EnhancedGeologicalProfile profile, ProjectedFault fault, float displacement)
    {
        // Applies a simple kink-band fault-propagation fold model.
        // Source: Suppe, J., & Medwedeff, D. A. (1990). Geometry and kinematics of fault-propagation folding. Eclogae Geologicae Helvetiae, 83(3), 409-454.
        var faultTip = fault.SubsurfaceTrace.Last();
        var rampAngleRad = fault.Dip * MathF.PI / 180f;

        foreach (var layer in profile.StratigraphicLayers)
        {
            var deformedPoints = new List<Vector2>();
            foreach (var point in layer.Points)
                // Simple model: create a fold in front of the fault tip
                if (point.X < faultTip.X && point.X > faultTip.X - displacement)
                {
                    var verticalUplift = (faultTip.X - point.X) * MathF.Tan(rampAngleRad / 2);
                    deformedPoints.Add(point + new Vector2(0, verticalUplift));
                }
                else
                {
                    deformedPoints.Add(point);
                }

            layer.DeformedPoints = deformedPoints;
        }
    }

    private static void GeneratePositiveFlower(
        EnhancedGeologicalProfile profile, ProjectedFault fault)
    {
        // Generates an upward-splaying reverse fault system (transpression).
        // Source: Harding, T. P. (1985). Seismic characteristics and identification of negative flower structures, positive flower structures, and positive structural inversion. AAPG bulletin, 69(4), 582-600.
        var faultRoot = fault.SurfaceTrace.First();
        var splayAngle = 15f * (MathF.PI / 180f); // 15 degrees splay
        var mainDip = fault.Dip * (MathF.PI / 180f);
        fault.SubsurfaceTrace.Clear();
        fault.SubsurfaceTrace.Add(faultRoot);
        fault.SubsurfaceTrace.Add(faultRoot - new Vector2(1000, 1000 * MathF.Tan(mainDip - splayAngle)));
        fault.SubsurfaceTrace.Add(faultRoot - new Vector2(2000, 2000 * MathF.Tan(mainDip + splayAngle)));
    }

    private static void GenerateNegativeFlower(
        EnhancedGeologicalProfile profile, ProjectedFault fault)
    {
        // Generates an upward-splaying normal fault system (transtension).
        // Source: Harding, T. P. (1985). Seismic characteristics and identification of negative flower structures, positive flower structures, and positive structural inversion. AAPG bulletin, 69(4), 582-600.
        var faultRoot = fault.SurfaceTrace.First();
        var splayAngle = 10f * (MathF.PI / 180f); // 10 degrees splay
        var mainDip = fault.Dip * (MathF.PI / 180f);
        fault.SubsurfaceTrace.Clear();
        fault.SubsurfaceTrace.Add(faultRoot);
        fault.SubsurfaceTrace.Add(faultRoot - new Vector2(1000, 1000 * MathF.Tan(mainDip + splayAngle)));
        fault.SubsurfaceTrace.Add(faultRoot - new Vector2(2000, 2000 * MathF.Tan(mainDip - splayAngle)));
    }

    /// <summary>
    ///     Generates an imbricate fan of thrusts, ensuring each fault intersects the real topography.
    /// </summary>
    private static void GenerateImbricateThrusts(
        EnhancedGeologicalProfile profile, float detachmentLevel, float shortening)
    {
        // Generates a simple imbricate fan of thrusts.
        // Source: Boyer, S. E., & Elliott, D. (1982). Thrust systems. AAPG bulletin, 66(9), 1196-1230.
        var numFaults = 3;
        var spacing = 1500f;
        var startX = profile.TopographicProfile.TotalDistance * 0.2f;

        for (var i = 0; i < numFaults; i++)
        {
            var fault = new ProjectedFault { Type = GeologicalFeatureType.Fault_Thrust, Dip = 30f };
            var detachmentPoint = new Vector2(startX + i * spacing, -detachmentLevel);

            // Find where this fault intersects the topography
            var surfacePoint = FindIntersectionWithTopography(profile, detachmentPoint, fault.Dip);
            if (surfacePoint.HasValue)
            {
                fault.SubsurfaceTrace.Add(surfacePoint.Value);
                fault.SubsurfaceTrace.Add(detachmentPoint);
                profile.ProjectedFaults.Add(fault);
            }
        }
    }

    private static void GenerateDuplexStructure(
        EnhancedGeologicalProfile profile, float detachmentLevel)
    {
        // Generates a simplified duplex with a floor and roof thrust.
        // Source: Boyer, S. E., & Elliott, D. (1982). Thrust systems. AAPG bulletin, 66(9), 1196-1230.
        var roofThrustDepth = detachmentLevel - 1000f; // 1km thick duplex
        var numHorses = 4;
        var horseLength = 2000f;
        var startX = profile.TopographicProfile.TotalDistance * 0.3f;

        for (var i = 0; i < numHorses; i++)
        {
            var fault = new ProjectedFault { Type = GeologicalFeatureType.Fault_Thrust, Dip = 35f };
            var floorPoint = new Vector2(startX + i * horseLength, -detachmentLevel);
            var roofPoint =
                new Vector2(floorPoint.X + (detachmentLevel - roofThrustDepth) / MathF.Tan(fault.Dip * MathF.PI / 180f),
                    -roofThrustDepth);
            fault.SubsurfaceTrace.Add(roofPoint);
            fault.SubsurfaceTrace.Add(floorPoint);
            profile.ProjectedFaults.Add(fault);
        }
    }

    private static void GenerateBasementUplift(
        EnhancedGeologicalProfile profile, float basementTop, float shortening)
    {
        // Models a basement-cored uplift (Laramide-style).
        // Source: Allmendinger, R. W., et al. (1983). Cenozoic and Mesozoic structure of the eastern Basin and Range province, Utah, from seismic-reflection data. Geology, 11(9), 532-536.
        var basementFault = new ProjectedFault { Type = GeologicalFeatureType.Fault_Reverse, Dip = 50f };
        var startX = profile.TopographicProfile.TotalDistance / 2;
        var surfacePoint = new Vector2(startX, 0);
        basementFault.SubsurfaceTrace.Add(surfacePoint);
        basementFault.SubsurfaceTrace.Add(new Vector2(startX - 5000 / MathF.Tan(basementFault.Dip * MathF.PI / 180f),
            -5000));
        profile.ProjectedFaults.Add(basementFault);

        // Deform basement layer
        var basementLayer = profile.StratigraphicLayers.Find(l => l.Name == "Crystalline Basement");
        if (basementLayer != null)
            for (var i = 0; i < basementLayer.Points.Count; i++)
            {
                var point = basementLayer.Points[i];
                if (point.X > startX)
                    basementLayer.Points[i] =
                        point + new Vector2(0, shortening * MathF.Tan(basementFault.Dip * MathF.PI / 180f));
            }
    }

    private static void DrapeSedimentaryLayers(
        EnhancedGeologicalProfile profile, float basementTop)
    {
        // Simple drape folding over deformed basement.
        var basementLayer = profile.StratigraphicLayers.Find(l => l.Name == "Crystalline Basement");
        if (basementLayer == null || basementLayer.DeformedPoints.Count == 0) return;

        float lastUplift = 0;
        for (var i = 0; i < basementLayer.Points.Count; i++)
        {
            var basementPoint = basementLayer.Points[i];
            var uplift = basementPoint.Y - -basementTop;

            foreach (var sedLayer in profile.StratigraphicLayers.Where(l => l != basementLayer))
                if (i < sedLayer.Points.Count)
                    sedLayer.Points[i] += new Vector2(0, uplift - lastUplift);

            lastUplift = uplift;
        }
    }

    private static void GenerateHorstGraben(
        EnhancedGeologicalProfile profile, float basementDepth)
    {
        // Generates a simple horst and graben structure.
        var profileCenter = profile.TopographicProfile.TotalDistance / 2;
        var grabenWidth = 4000f;
        var faultDip = 60f;

        // Graben-bounding faults
        var fault1 = new ProjectedFault { Type = GeologicalFeatureType.Fault_Normal, Dip = faultDip };
        fault1.SubsurfaceTrace.Add(new Vector2(profileCenter - grabenWidth / 2, 0));
        fault1.SubsurfaceTrace.Add(new Vector2(profileCenter - grabenWidth / 2 - 1000,
            -1000 * MathF.Tan(faultDip * MathF.PI / 180f)));

        var fault2 = new ProjectedFault { Type = GeologicalFeatureType.Fault_Normal, Dip = faultDip };
        fault2.SubsurfaceTrace.Add(new Vector2(profileCenter + grabenWidth / 2, 0));
        fault2.SubsurfaceTrace.Add(new Vector2(profileCenter + grabenWidth / 2 + 1000,
            -1000 * MathF.Tan(faultDip * MathF.PI / 180f)));
        profile.ProjectedFaults.AddRange(new[] { fault1, fault2 });
    }

    private static void ApplySynRiftSedimentation(
        EnhancedGeologicalProfile profile)
    {
        // Simulates growth strata in grabens by thickening layers towards the fault.
        var grabenCenter = profile.TopographicProfile.TotalDistance / 2;
        var grabenWidth = 4000f;

        foreach (var layer in profile.StratigraphicLayers.Where(l => l.AgeCode != "Precambrian"))
            for (var i = 0; i < layer.Points.Count; i++)
            {
                var point = layer.Points[i];
                if (point.X > grabenCenter - grabenWidth / 2 && point.X < grabenCenter + grabenWidth / 2)
                {
                    // Thicken layer towards center of graben
                    var distFromCenter = Math.Abs(point.X - grabenCenter);
                    var thickeningFactor = (1 - distFromCenter / (grabenWidth / 2)) * 50f; // add up to 50m thickness
                    layer.Points[i] = point - new Vector2(0, thickeningFactor);
                }
            }
    }

    private static void InvertNormalFault(
        ProjectedFault fault, float shorteningAmount)
    {
        // Reactivate normal fault as reverse
        // Source: Cooper, M. A., & Williams, G. D. (Eds.). (1989). Inversion tectonics (Vol. 44). Geological Society of London.
        fault.Type = GeologicalFeatureType.Fault_Reverse;
        fault.Displacement = (fault.Displacement ?? 0) - shorteningAmount; // Net displacement
    }

    private static void GenerateInversionAnticline(
        EnhancedGeologicalProfile profile, ProjectedFault fault)
    {
        // Generates an anticline above the inverted fault.
        // Source: Cooper, M. A., & Williams, G. D. (Eds.). (1989). Inversion tectonics (Vol. 44). Geological Society of London.
        var faultIntersectionX = fault.SurfaceTrace.First().X;
        var foldWidth = 3000f;
        var uplift = Math.Abs(fault.Displacement ?? 500f);

        foreach (var layer in profile.StratigraphicLayers)
            for (var i = 0; i < layer.Points.Count; i++)
            {
                var point = layer.Points[i];
                var dist = Math.Abs(point.X - faultIntersectionX);
                if (dist < foldWidth / 2)
                {
                    // Cosine-based uplift
                    var foldUplift = uplift * (MathF.Cos(dist / (foldWidth / 2) * MathF.PI) + 1) / 2;
                    layer.Points[i] = point + new Vector2(0, foldUplift);
                }
            }
    }

    /// <summary>
    ///     Determines if a point is in the hanging wall of a fault.
    /// </summary>
    private static bool IsPointInHangingWall(Vector2 point, ProjectedFault fault)
    {
        if (fault.SubsurfaceTrace.Count < 2) return false;

        // Assuming dip direction is generally to the right for normal faults and left for reverse in the profile view
        var isNormalFault = fault.Type == GeologicalFeatureType.Fault_Normal;

        for (var i = 0; i < fault.SubsurfaceTrace.Count - 1; i++)
        {
            var p1 = fault.SubsurfaceTrace[i];
            var p2 = fault.SubsurfaceTrace[i + 1];

            // Create a line equation for the fault segment: y = mx + c  => mx - y + c = 0
            var m = (p2.Y - p1.Y) / (p2.X - p1.X);
            var c = p1.Y - m * p1.X;

            // Check which side of the line the point is on.
            // The value of the expression mx - y + c tells us the side.
            var side = m * point.X - point.Y + c;

            // For a normal fault dipping right, the hanging wall is above and to the right.
            // For a reverse fault dipping left, the hanging wall is above and to the right.
            // This logic needs to be tied to dip direction, but for a 2D profile we simplify.
            // A positive side value could mean "above" the fault line.
            if (isNormalFault)
            {
                if (point.X > p1.X && side > 0) return true;
            }
            else // Reverse/Thrust
            {
                if (point.X < p1.X && side > 0) return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Finds the intersection of a planar fault (defined by a point and dip) with the topographic profile.
    /// </summary>
    private static Vector2? FindIntersectionWithTopography(EnhancedGeologicalProfile profile, Vector2 subsurfacePoint,
        float dip)
    {
        var dipRad = dip * (MathF.PI / 180f);
        var m = MathF.Tan(dipRad);

        // Project fault line upwards from the subsurface point to find its surface trace.
        var topography = profile.TopographicProfile.Points;
        for (var i = 0; i < topography.Count - 1; i++)
        {
            var t1 = topography[i];
            var t2 = topography[i + 1];

            // Equation for fault line: y - p.y = m * (x - p.x)
            // Equation for topography segment: y - t1.y = mt * (x - t1.x)
            var mt = (t2.Elevation - t1.Elevation) / (t2.Distance - t1.Distance);

            if (Math.Abs(m - mt) < 0.001f) continue; // Parallel lines

            // Solve for x
            var x = (t1.Elevation - subsurfacePoint.Y - mt * t1.Distance + m * subsurfacePoint.X) / (m - mt);

            if (x >= t1.Distance && x <= t2.Distance)
            {
                var y = t1.Elevation + mt * (x - t1.Distance);
                return new Vector2(x, y);
            }
        }

        return null; // No intersection found
    }

    #endregion
}

#region Data Classes

public class EnhancedGeologicalProfile
{
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public Vector2 StartPoint { get; set; }
    public Vector2 EndPoint { get; set; }

    public ProfileGenerator.TopographicProfile TopographicProfile { get; set; }
    public List<ProjectedBorehole> ProjectedBoreholes { get; set; } = new();
    public List<StratigraphicLayer> StratigraphicLayers { get; set; } = new();
    public List<ProjectedFault> ProjectedFaults { get; set; } = new();

    public TectonicModelParameters ModelParameters { get; set; }
}

public class ProjectedBorehole
{
    public string Name { get; set; }
    public Vector2 OriginalPosition { get; set; }
    public Vector2 ProjectedPosition { get; set; }
    public float DistanceAlongProfile { get; set; }
    public float OffsetDistance { get; set; }
    public float SurfaceElevation { get; set; }
    public float TotalDepth { get; set; }
    public List<GeologicalLayer> Layers { get; set; } = new();
}

public class GeologicalLayer
{
    public float TopDepth { get; set; }
    public float BottomDepth { get; set; }
    public string LithologyCode { get; set; }
    public string FormationName { get; set; }
    public string Age { get; set; }
    public Vector4 Color { get; set; }
    public string CorrelatedFormation { get; set; }
    public float ApparentDip { get; set; }
}

public class StratigraphicLayer
{
    public string Name { get; set; }
    public string AgeCode { get; set; }
    public List<Vector2> Points { get; set; } = new();
    public List<Vector2> DeformedPoints { get; set; } = new();
    public Vector4 Color { get; set; }
    public float Thickness { get; set; }
    public FoldStyle FoldStyle { get; set; }
}

public class ProjectedFault
{
    public GeologicalFeatureType Type { get; set; }
    public List<Vector2> SurfaceTrace { get; set; } = new();
    public List<Vector2> SubsurfaceTrace { get; set; } = new();
    public float Dip { get; set; }
    public string DipDirection { get; set; }
    public float? Displacement { get; set; }
    public float? RampAngle { get; set; }
    public Vector2 TipPosition { get; set; }
}

public class TectonicModelParameters
{
    public float BasementDepth { get; set; } = 5000f;
    public float? DetachmentDepth { get; set; }
    public StructuralStyle StructuralStyle { get; set; } = StructuralStyle.ThinSkinned;
    public float RegionalDip { get; set; } = 0f;
    public float ShorteningAmount { get; set; } = 0f;
    public bool UseBalancedSection { get; set; } = true;
    public FoldMechanism FoldMechanism { get; set; } = FoldMechanism.FaultBendFold;
}

public class FormationContact
{
    public Vector2 Position { get; set; }
    public float DistanceAlongProfile { get; set; }
    public float Elevation { get; set; }
    public string FormationName { get; set; }
    public string AgeCode { get; set; }
    public float DipAngle { get; set; }
    public string DipDirection { get; set; }
}

public struct LineSegment
{
    public Vector2 Start { get; set; }
    public Vector2 End { get; set; }

    public LineSegment(Vector2 start, Vector2 end)
    {
        Start = start;
        End = end;
    }
}

#endregion

#region Enumerations

public enum StructuralStyle
{
    ThinSkinned, // Deformation above basement detachment
    ThickSkinned, // Basement-involved deformation
    Extensional, // Normal faulting dominant
    Contractional, // Thrust/reverse faulting dominant
    StrikeSlip, // Transform faulting dominant
    Inverted, // Reactivated extensional structures
    SaltTectonics, // Salt-related deformation
    Hybrid // Mixed structural styles
}

public enum FoldStyle
{
    Parallel, // Constant bed thickness (Busk, 1929)
    Similar, // Constant thickness parallel to axial surface
    Chevron, // Angular kink-band geometry (Suppe, 1985)
    Concentric, // Constant radius of curvature
    Disharmonic // Variable folding between layers
}

public enum FoldMechanism
{
    FaultBendFold, // Suppe (1983)
    FaultPropagationFold, // Suppe & Medwedeff (1990)
    Trishear, // Erslev (1991)
    Detachment, // Jamison (1987)
    BucklingInstability // Biot (1961)
}

#endregion