// GeoscientistToolkit/Business/GIS/GeologicalProfileIntegration.cs

using System.Numerics;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
///     Integrates borehole data with topographic profiles to create realistic geological cross-sections
///     using proper tectonic models and structural geology principles.
///     References:
///     - Ramsay, J.G. & Huber, M.I. (1987). The Techniques of Modern Structural Geology, Volume 2: Folds and Fractures
///     - Allmendinger, R.W. et al. (2012). Structural Geology Algorithms: Vectors and Tensors
///     - Groshong, R.H. (2006). 3-D Structural Geology: A Practical Guide to Quantitative Surface and Subsurface Map
///     Interpretation
///     - Erslev, E.A. (1991). Trishear fault-propagation folding. Geology, 19(6), 617-620
/// </summary>
public static class GeologicalProfileIntegration
{
    #region Main Integration Methods

    /// <summary>
    ///     Generate an enhanced topographic profile with integrated borehole data and tectonic modeling
    /// </summary>
    public static EnhancedGeologicalProfile GenerateIntegratedProfile(
        float[,] demData,
        BoundingBox demBounds,
        Vector2 startPoint,
        Vector2 endPoint,
        List<GeologicalFeature> geologicalFeatures,
        List<BoreholeDataset> boreholeDatasets,
        TectonicModelParameters modelParams,
        int numSamples = 200)
    {
        var profile = new EnhancedGeologicalProfile
        {
            StartPoint = startPoint,
            EndPoint = endPoint,
            CreatedAt = DateTime.Now,
            Name = $"Integrated Profile {DateTime.Now:yyyy-MM-dd HH:mm}",
            ModelParameters = modelParams ?? new TectonicModelParameters()
        };

        // Generate base topographic profile
        var baseProfile = ProfileGenerator.GenerateProfile(
            demData, demBounds, startPoint, endPoint, numSamples, geologicalFeatures);

        profile.TopographicProfile = baseProfile;

        // Project boreholes onto profile
        ProjectBoreholes(profile, boreholeDatasets, 500f); // 500m search corridor

        // Build stratigraphic model from surface geology and boreholes
        BuildStratigraphicModel(profile, geologicalFeatures);

        // Apply tectonic deformation model
        ApplyTectonicModel(profile);

        // Generate subsurface interpretation
        GenerateSubsurfaceInterpretation(profile);

        return profile;
    }

    #endregion

    #region Constants and Configuration

    private const float FaultPropagationRatio = 2.0f; // P/S ratio from trishear model (Erslev, 1991)
    private const float TrishearAngle = 60f; // degrees - triangular zone of distributed shear

    #endregion

    #region Borehole Projection

    /// <summary>
    ///     Project boreholes onto the profile line within a search corridor
    ///     Based on principles from Lisle, R.J. (2004). Geological Structures and Maps: A Practical Guide
    /// </summary>
    private static void ProjectBoreholes(
        EnhancedGeologicalProfile profile,
        List<BoreholeDataset> boreholeDatasets,
        float searchCorridor)
    {
        var profileLine = new LineSegment(profile.StartPoint, profile.EndPoint);

        foreach (var borehole in boreholeDatasets)
        {
            if (borehole.DatasetMetadata?.Latitude == null || borehole.DatasetMetadata?.Longitude == null)
                continue;

            var boreholePos = new Vector2(
                (float)borehole.DatasetMetadata.Longitude.Value,
                (float)borehole.DatasetMetadata.Latitude.Value);

            // Calculate perpendicular distance to profile line
            var (closestPoint, distance) = GetClosestPointOnLine(boreholePos, profileLine);

            if (distance <= searchCorridor)
            {
                // Calculate position along profile
                var profileDistance = Vector2.Distance(profile.StartPoint, closestPoint);

                // Get surface elevation at projected point
                var surfaceElevation = InterpolateElevation(
                    profile.TopographicProfile.Points, profileDistance);

                var projectedBorehole = new ProjectedBorehole
                {
                    Name = borehole.Name,
                    OriginalPosition = boreholePos,
                    ProjectedPosition = closestPoint,
                    DistanceAlongProfile = profileDistance,
                    OffsetDistance = distance,
                    SurfaceElevation = surfaceElevation,
                    TotalDepth = borehole.TotalDepth,
                    Layers = ConvertBoreholeLayersToGeological(borehole)
                };

                profile.ProjectedBoreholes.Add(projectedBorehole);

                Logger.Log($"Projected borehole '{borehole.Name}' at {profileDistance:F1}m along profile, " +
                           $"offset by {distance:F1}m");
            }
        }
    }

    private static List<GeologicalLayer> ConvertBoreholeLayersToGeological(BoreholeDataset borehole)
    {
        var geologicalLayers = new List<GeologicalLayer>();

        // Access layers from BoreholeDataset.LithologyUnits property
        var lithologyUnits = borehole.LithologyUnits ?? new List<LithologyUnit>();

        foreach (var unit in lithologyUnits)
        {
            var geoLayer = new GeologicalLayer
            {
                TopDepth = unit.DepthFrom,
                BottomDepth = unit.DepthTo,
                LithologyCode = unit.LithologyType ?? "Unknown",
                FormationName = unit.Name, // Use Name for FormationName
                Age = null, // No direct 'Age' property in LithologyUnit
                Color = GetLithologyColor(unit.LithologyType)
            };
            geologicalLayers.Add(geoLayer);
        }

        return geologicalLayers;
    }

    #endregion

    #region Stratigraphic Model Building

    /// <summary>
    ///     Build a stratigraphic model from surface geology and borehole data
    ///     Using principles from Groshong (2006) for structural validation
    /// </summary>
    private static void BuildStratigraphicModel(
        EnhancedGeologicalProfile profile,
        List<GeologicalFeature> geologicalFeatures)
    {
        // Extract formation contacts from surface geology
        var formationContacts = ExtractFormationContacts(profile, geologicalFeatures);

        // Correlate borehole stratigraphy
        if (profile.ProjectedBoreholes.Any()) CorrelateBoreholeStratigraphy(profile, formationContacts);

        // Build layer cake model
        profile.StratigraphicLayers =
            GeologicalProfileHelpers.BuildLayerCakeModel(formationContacts, profile.ProjectedBoreholes);

        // Add basement at specified depth
        GeologicalProfileHelpers.AddBasementLayer(profile);
    }

    private static List<FormationContact> ExtractFormationContacts(
        EnhancedGeologicalProfile profile,
        List<GeologicalFeature> geologicalFeatures)
    {
        var contacts = new List<FormationContact>();

        // Find formation boundaries along profile
        foreach (var point in profile.TopographicProfile.Points)
        {
            var formations = point.Features
                .Where(f => f.GeologicalType == GeologicalFeatureType.Formation)
                .ToList();

            foreach (var formation in formations)
                contacts.Add(new FormationContact
                {
                    Position = point.Position,
                    DistanceAlongProfile = point.Distance,
                    Elevation = point.Elevation,
                    FormationName = formation.FormationName,
                    AgeCode = formation.AgeCode,
                    DipAngle = formation.Dip ?? 0,
                    DipDirection = formation.DipDirection
                });
        }

        return contacts;
    }

    private static void CorrelateBoreholeStratigraphy(
        EnhancedGeologicalProfile profile,
        List<FormationContact> surfaceContacts)
    {
        // Correlate formations between boreholes using lithostratigraphic principles
        // Based on Shaw & Suppe (1994) balanced cross-section methods

        foreach (var borehole in profile.ProjectedBoreholes)
        foreach (var layer in borehole.Layers)
        {
            // Find matching surface formations
            var matchingContact = surfaceContacts
                .Where(c => IsFormationMatch(c.FormationName, layer.FormationName))
                .OrderBy(c => Math.Abs(c.DistanceAlongProfile - borehole.DistanceAlongProfile))
                .FirstOrDefault();

            if (matchingContact != null)
            {
                layer.CorrelatedFormation = matchingContact.FormationName;

                // Calculate structural dip from borehole data
                // Using apparent dip calculation from Ragan (2009)
                layer.ApparentDip = CalculateApparentDip(
                    borehole, layer, profile.TopographicProfile);
            }
        }
    }

    #endregion

    #region Tectonic Modeling

    /// <summary>
    ///     Apply tectonic deformation model to create realistic subsurface structure
    ///     Based on fault-related folding theories (Suppe, 1983; Erslev, 1991; Allmendinger et al., 2012)
    /// </summary>
    private static void ApplyTectonicModel(EnhancedGeologicalProfile profile)
    {
        var model = profile.ModelParameters;

        // Identify major faults from surface geology
        var faults = GeologicalProfileHelpers.IdentifyMajorFaults(profile);

        foreach (var fault in faults)
            switch (fault.Type)
            {
                case GeologicalFeatureType.Fault_Thrust:
                    ApplyThrustFaultModel(profile, fault, model);
                    break;

                case GeologicalFeatureType.Fault_Normal:
                    GeologicalProfileHelpers.ApplyNormalFaultModel(profile, fault, model);
                    break;

                case GeologicalFeatureType.Fault_Reverse:
                    GeologicalProfileHelpers.ApplyReverseFaultModel(profile, fault, model);
                    break;

                case GeologicalFeatureType.Fault_Transform:
                    // Transform faults have minimal vertical expression in cross-section
                    GeologicalProfileHelpers.ApplyStrikeSlipModel(profile, fault, model);
                    break;
            }

        // Apply regional structural style
        switch (model.StructuralStyle)
        {
            case StructuralStyle.ThinSkinned:
                GeologicalProfileHelpers.ApplyThinSkinnedDeformation(profile, model);
                break;

            case StructuralStyle.ThickSkinned:
                GeologicalProfileHelpers.ApplyThickSkinnedDeformation(profile, model);
                break;

            case StructuralStyle.Extensional:
                GeologicalProfileHelpers.ApplyExtensionalTectonics(profile, model);
                break;

            case StructuralStyle.Inverted:
                GeologicalProfileHelpers.ApplyInversionTectonics(profile, model);
                break;
        }
    }

    /// <summary>
    ///     Apply thrust fault model using fault-bend fold theory (Suppe, 1983)
    ///     and trishear kinematics (Erslev, 1991)
    /// </summary>
    private static void ApplyThrustFaultModel(
        EnhancedGeologicalProfile profile,
        ProjectedFault fault,
        TectonicModelParameters model)
    {
        // Calculate fault geometry using balanced cross-section principles
        // From Dahlstrom (1969) and Boyer & Elliott (1982)

        var rampAngle = fault.RampAngle ?? 30f; // degrees
        var displacement = fault.Displacement ?? 1000f; // meters

        // Generate fault trajectory
        var faultTrace = new List<Vector2>();

        // Basal detachment (flat)
        var detachmentDepth = model.DetachmentDepth ?? 3000f; // Using the constant
        faultTrace.Add(new Vector2(0, -detachmentDepth));

        // Ramp
        var rampHeight = detachmentDepth - 500f; // Ramp to 500m depth
        var rampLength = rampHeight / MathF.Tan(rampAngle * MathF.PI / 180f);
        faultTrace.Add(new Vector2(rampLength, -500f));

        // Upper flat
        faultTrace.Add(new Vector2(rampLength + displacement, -500f));

        fault.SubsurfaceTrace = faultTrace;

        // Apply trishear deformation to hanging wall
        ApplyTrishearDeformation(profile, fault, displacement, TrishearAngle);

        // Generate fault-bend fold geometry
        GeologicalProfileHelpers.GenerateFaultBendFold(profile, fault, rampAngle, displacement);

        Logger.Log($"Applied thrust fault model: ramp angle {rampAngle}°, displacement {displacement}m");
    }

    /// <summary>
    ///     Apply trishear deformation model (Erslev, 1991; Allmendinger, 1998)
    ///     Creates distributed deformation in triangular zone ahead of fault tip
    /// </summary>
    private static void ApplyTrishearDeformation(
        EnhancedGeologicalProfile profile,
        ProjectedFault fault,
        float displacement,
        float trishearAngle)
    {
        // Calculate trishear parameters
        var propagationDistance = displacement / FaultPropagationRatio;
        var apicalAngle = trishearAngle * MathF.PI / 180f;

        // Deform layers within trishear zone
        foreach (var layer in profile.StratigraphicLayers)
        {
            var deformedPoints = new List<Vector2>();

            foreach (var point in layer.Points)
                // Check if point is within trishear zone
                if (GeologicalProfileHelpers.IsInTrishearZone(point, fault.TipPosition, apicalAngle,
                        propagationDistance))
                {
                    // Apply velocity field equations from Zehnder & Allmendinger (2000)
                    var deformed = GeologicalProfileHelpers.ApplyTrishearVelocityField(
                        point, fault.TipPosition, displacement, apicalAngle, propagationDistance);
                    deformedPoints.Add(deformed);
                }
                else
                {
                    deformedPoints.Add(point);
                }

            layer.DeformedPoints = deformedPoints;
        }
    }

    #endregion

    #region Subsurface Interpretation

    /// <summary>
    ///     Generate subsurface interpretation using structural geology principles
    ///     and borehole constraints
    /// </summary>
    private static void GenerateSubsurfaceInterpretation(EnhancedGeologicalProfile profile)
    {
        // Extrapolate formation contacts to depth
        ExtrapolateFormations(profile);

        // Apply structural constraints from boreholes
        GeologicalProfileHelpers.ApplyBoreholeConstraints(profile);

        // Balance the cross-section using area-depth methods (Chamberlin, 1910; Mitra & Namson, 1989)
        GeologicalProfileHelpers.BalanceCrossSection(profile);

        // Add minor structures (parasitic folds, minor faults)
        GeologicalProfileHelpers.AddMinorStructures(profile);

        // Validate using geometric rules (Dahlstrom, 1969)
        GeologicalProfileHelpers.ValidateStructuralGeometry(profile);
    }

    private static void ExtrapolateFormations(EnhancedGeologicalProfile profile)
    {
        foreach (var layer in profile.StratigraphicLayers)
            // Use Busk construction method (1929) for parallel folding
            // or kink-band method (Suppe, 1985) for angular folding
            if (layer.FoldStyle == FoldStyle.Parallel)
                // Maintain constant thickness perpendicular to bedding
                GeologicalProfileHelpers.ExtrapolateParallelFold(layer, profile.ModelParameters.BasementDepth);
            else if (layer.FoldStyle == FoldStyle.Similar)
                // Maintain constant thickness parallel to axial surface
                GeologicalProfileHelpers.ExtrapolateSimilarFold(layer, profile.ModelParameters.BasementDepth);
            else
                // Simple dip projection
                GeologicalProfileHelpers.ExtrapolateByDip(layer, profile.ModelParameters.BasementDepth);
    }

    #endregion

    #region Helper Methods

    private static (Vector2 closest, float distance) GetClosestPointOnLine(
        Vector2 point, LineSegment line)
    {
        var lineVec = line.End - line.Start;
        var pointVec = point - line.Start;
        var lineLength = lineVec.Length();
        var lineDir = lineVec / lineLength;

        var t = Math.Clamp(Vector2.Dot(pointVec, lineDir), 0, lineLength);
        var closest = line.Start + lineDir * t;
        var distance = Vector2.Distance(point, closest);

        return (closest, distance);
    }

    private static float InterpolateElevation(
        List<ProfileGenerator.ProfilePoint> points, float distance)
    {
        if (points.Count < 2)
            return 0;

        // Find bracketing points
        for (var i = 0; i < points.Count - 1; i++)
            if (distance >= points[i].Distance && distance <= points[i + 1].Distance)
            {
                var t = (distance - points[i].Distance) /
                        (points[i + 1].Distance - points[i].Distance);
                return points[i].Elevation + t * (points[i + 1].Elevation - points[i].Elevation);
            }

        // Extrapolate if beyond range
        if (distance < points[0].Distance)
            return points[0].Elevation;
        return points[^1].Elevation;
    }

    private static Vector4 GetLithologyColor(string lithology)
    {
        return LithologyPatterns.StandardColors.GetValueOrDefault(
            lithology ?? "Unknown",
            new Vector4(0.5f, 0.5f, 0.5f, 0.4f));
    }

    private static bool IsFormationMatch(string formation1, string formation2)
    {
        if (string.IsNullOrEmpty(formation1) || string.IsNullOrEmpty(formation2))
            return false;

        // Simple matching - could be enhanced with fuzzy matching
        return formation1.Equals(formation2, StringComparison.OrdinalIgnoreCase);
    }

    private static float CalculateApparentDip(
        ProjectedBorehole borehole,
        GeologicalLayer layer,
        ProfileGenerator.TopographicProfile profile)
    {
        // Calculate apparent dip in plane of section
        // From Ragan, D.M. (2009). Structural Geology: An Introduction to Geometrical Techniques

        if (layer.TopDepth >= layer.BottomDepth)
            return 0;

        var thickness = layer.BottomDepth - layer.TopDepth;
        var horizontalDistance = 100f; // Assumed lateral extent

        var apparentDip = MathF.Atan(thickness / horizontalDistance) * 180f / MathF.PI;

        return apparentDip;
    }

    #endregion
}