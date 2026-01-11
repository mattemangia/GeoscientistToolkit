// GeoscientistToolkit/Business/GIS/GeologicalLayerPresets.cs

using System.Numerics;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Provides complete geological cross-section presets with properly stacked layers.
/// All presets generate geologically valid cross-sections where:
/// 1. Formations NEVER overlap - each layer sits exactly on top of the layer below
/// 2. Formations are BELOW topography - layers are clipped/positioned correctly
/// 3. Layer boundaries are properly aligned - no gaps between layers
/// </summary>
public static class GeologicalLayerPresets
{
    public enum PresetScenario
    {
        SimpleLayers,
        ErodedAnticline,
        ErodedSyncline,
        JuraMountains,
        FaultedLayers,
        ThrustFault,
        FoldedSequence,
        UnconformitySequence,
        BasinFilling,
        ChannelFill
    }

    /// <summary>
    /// Standard stratigraphic column used by all presets.
    /// Colors are geologically appropriate (sedimentary = yellows/browns, limestone = grays/blues).
    /// </summary>
    private static readonly LayerDefinition[] StandardColumn = new[]
    {
        new LayerDefinition("Quaternary Alluvium", new Vector4(0.95f, 0.9f, 0.7f, 0.85f), 50f),
        new LayerDefinition("Tertiary Molasse", new Vector4(0.85f, 0.75f, 0.55f, 0.85f), 120f),
        new LayerDefinition("Upper Cretaceous Limestone", new Vector4(0.7f, 0.8f, 0.85f, 0.85f), 200f),
        new LayerDefinition("Lower Cretaceous Marl", new Vector4(0.6f, 0.7f, 0.6f, 0.85f), 180f),
        new LayerDefinition("Malm Limestone", new Vector4(0.75f, 0.82f, 0.88f, 0.85f), 250f),
        new LayerDefinition("Dogger Oolite", new Vector4(0.88f, 0.85f, 0.7f, 0.85f), 200f),
        new LayerDefinition("Lias Shale", new Vector4(0.45f, 0.5f, 0.55f, 0.85f), 180f),
        new LayerDefinition("Keuper Evaporites", new Vector4(0.9f, 0.65f, 0.5f, 0.85f), 150f),
    };

    public static CrossSection CreatePreset(PresetScenario scenario, float totalDistance = 10000f, float baseElevation = -2000f)
    {
        Logger.Log($"Creating geological preset: {scenario}");

        var section = scenario switch
        {
            PresetScenario.SimpleLayers => CreateSimpleLayers(totalDistance, baseElevation),
            PresetScenario.ErodedAnticline => CreateErodedAnticline(totalDistance, baseElevation),
            PresetScenario.ErodedSyncline => CreateErodedSyncline(totalDistance, baseElevation),
            PresetScenario.JuraMountains => CreateJuraMountains(totalDistance, baseElevation),
            PresetScenario.FaultedLayers => CreateFaultedLayers(totalDistance, baseElevation),
            PresetScenario.ThrustFault => CreateThrustFault(totalDistance, baseElevation),
            PresetScenario.FoldedSequence => CreateFoldedSequence(totalDistance, baseElevation),
            PresetScenario.UnconformitySequence => CreateUnconformitySequence(totalDistance, baseElevation),
            PresetScenario.BasinFilling => CreateBasinFilling(totalDistance, baseElevation),
            PresetScenario.ChannelFill => CreateChannelFill(totalDistance, baseElevation),
            _ => CreateSimpleLayers(totalDistance, baseElevation)
        };

        // Final validation - ensure no overlaps
        ValidateFinalGeology(section);

        Logger.Log($"Preset created with {section.Formations.Count} formations and {section.Faults.Count} faults");

        return section;
    }

    #region Preset Implementations

    /// <summary>
    /// Simple horizontal sedimentary sequence - ideal for testing restoration.
    /// Layers are perfectly horizontal and stacked from topography downward.
    /// </summary>
    private static CrossSection CreateSimpleLayers(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Create flat topography at sea level
        ApplyFlatTopography(section.Profile, 0f);

        // Build layers from topography down
        BuildStackedLayers(section, StandardColumn.Take(5).ToArray(), deformationFunc: null);

        // Add crystalline basement at bottom
        AddBasement(section, totalDistance, baseElevation);

        return section;
    }

    /// <summary>
    /// Anticline structure with erosion at crest - classic fold exposure.
    /// Shows older layers exposed at core due to erosion.
    /// </summary>
    private static CrossSection CreateErodedAnticline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Create valley topography that cuts into the anticline
        ApplyValleyTopography(section.Profile, totalDistance * 0.5f, 200f, totalDistance * 0.4f);

        // Parameters for the anticline
        float foldCenter = totalDistance * 0.5f;
        float foldWidth = totalDistance * 0.4f;
        float amplitude = 350f;

        // Deformation function for anticline shape
        Func<float, float, int, float> anticlineDeform = (x, baseY, layerIndex) =>
        {
            float distFromCenter = x - foldCenter;
            float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.5f));
            // Amplitude decreases with depth (deeper layers are less folded)
            float layerAmplitude = amplitude * (1.0f - layerIndex * 0.12f);
            return baseY + layerAmplitude * foldFactor;
        };

        // Build folded layers
        BuildStackedLayers(section, StandardColumn.Take(6).ToArray(), anticlineDeform);

        // Add basement
        AddBasement(section, totalDistance, baseElevation, anticlineDeform);

        return section;
    }

    /// <summary>
    /// Syncline structure with valley fill - classic down-fold structure.
    /// Shows younger layers preserved in core.
    /// </summary>
    private static CrossSection CreateErodedSyncline(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Create matching valley topography
        ApplyValleyTopography(section.Profile, totalDistance * 0.5f, 150f, totalDistance * 0.5f);

        // Parameters for the syncline
        float foldCenter = totalDistance * 0.5f;
        float foldWidth = totalDistance * 0.4f;
        float amplitude = -280f; // Negative for syncline (downward fold)

        // Deformation function for syncline shape
        Func<float, float, int, float> synclineDeform = (x, baseY, layerIndex) =>
        {
            float distFromCenter = x - foldCenter;
            float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.5f));
            float layerAmplitude = amplitude * (1.0f - layerIndex * 0.1f);
            return baseY + layerAmplitude * foldFactor;
        };

        // Build folded layers
        var layers = StandardColumn.Take(5).ToArray();
        BuildStackedLayers(section, layers, synclineDeform);

        // Mark fold style
        foreach (var formation in section.Formations)
        {
            formation.FoldStyle = FoldStyle.Syncline;
        }

        // Add basement
        AddBasement(section, totalDistance, baseElevation, synclineDeform);

        return section;
    }

    /// <summary>
    /// Jura Mountains style - multiple gentle folds with thrust faults.
    /// Classic thin-skinned tectonics with decollement.
    /// </summary>
    private static CrossSection CreateJuraMountains(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Create hilly topography matching the folds
        ApplyHillyTopography(section.Profile, 3, 150f);

        // Multiple fold parameters
        float[] foldCenters = { totalDistance * 0.25f, totalDistance * 0.5f, totalDistance * 0.75f };
        float foldWidth = totalDistance * 0.18f;
        float[] amplitudes = { 180f, 220f, 160f }; // Varying fold amplitudes

        // Deformation function for multiple folds
        Func<float, float, int, float> juraMtnDeform = (x, baseY, layerIndex) =>
        {
            float totalOffset = 0f;
            for (int i = 0; i < foldCenters.Length; i++)
            {
                float distFromCenter = x - foldCenters[i];
                float foldFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (foldWidth * foldWidth * 0.5f));
                float layerAmplitude = amplitudes[i] * (1.0f - layerIndex * 0.08f);
                totalOffset += layerAmplitude * foldFactor;
            }
            return baseY + totalOffset;
        };

        // Build folded layers
        BuildStackedLayers(section, StandardColumn.Take(6).ToArray(), juraMtnDeform);

        // Add basement
        AddBasement(section, totalDistance, baseElevation, juraMtnDeform);

        // Add thrust faults at fold limbs
        float avgTopoElev = section.Profile.Points.Average(p => p.Elevation);
        section.Faults.Add(CreateThrustFault(
            totalDistance * 0.35f, baseElevation + 500f,
            totalDistance * 0.4f, avgTopoElev,
            30f
        ));

        section.Faults.Add(CreateThrustFault(
            totalDistance * 0.6f, baseElevation + 500f,
            totalDistance * 0.65f, avgTopoElev,
            30f
        ));

        return section;
    }

    /// <summary>
    /// Faulted layers forming a graben structure - normal fault system.
    /// Classic extensional tectonics.
    /// </summary>
    private static CrossSection CreateFaultedLayers(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Create escarpment-like topography
        ApplyEscarpmentTopography(section.Profile, totalDistance * 0.3f, 150f);

        // Build horizontal layers first
        BuildStackedLayers(section, StandardColumn.Take(5).ToArray(), deformationFunc: null);

        // Add basement
        AddBasement(section, totalDistance, baseElevation);

        // Add normal faults creating a graben
        float avgTopoElev = section.Profile.Points.Average(p => p.Elevation);

        // Western fault (dipping east)
        section.Faults.Add(CreateNormalFault(
            totalDistance * 0.35f, baseElevation,
            totalDistance * 0.3f, avgTopoElev + 100f,
            65f, "E"
        ));

        // Eastern fault (dipping west)
        section.Faults.Add(CreateNormalFault(
            totalDistance * 0.65f, baseElevation,
            totalDistance * 0.7f, avgTopoElev + 100f,
            65f, "W"
        ));

        return section;
    }

    /// <summary>
    /// Thrust fault system - compressional tectonics with hanging wall over footwall.
    /// Shows older rocks thrust over younger.
    /// </summary>
    private static CrossSection CreateThrustFault(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Create asymmetric topography
        ApplyAsymmetricTopography(section.Profile, totalDistance * 0.4f, 200f);

        // Slight regional dip
        Func<float, float, int, float> dippingDeform = (x, baseY, layerIndex) =>
        {
            float dipOffset = (x / totalDistance) * 100f; // 100m elevation change over section
            return baseY - dipOffset;
        };

        // Build dipping layers
        BuildStackedLayers(section, StandardColumn.Take(5).ToArray(), dippingDeform);

        // Add basement
        AddBasement(section, totalDistance, baseElevation, dippingDeform);

        // Add main thrust fault
        float avgTopoElev = section.Profile.Points.Average(p => p.Elevation);
        section.Faults.Add(CreateThrustFault(
            totalDistance * 0.35f, baseElevation + 300f,
            totalDistance * 0.5f, avgTopoElev,
            25f
        ));

        return section;
    }

    /// <summary>
    /// Anticline-syncline pair - complete fold train.
    /// Shows both fold geometries in one section.
    /// </summary>
    private static CrossSection CreateFoldedSequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Create gentle hills matching the folds
        ApplyHillyTopography(section.Profile, 2, 100f);

        // Fold parameters
        float anticlineCenter = totalDistance * 0.35f;
        float synclineCenter = totalDistance * 0.65f;
        float foldWidth = totalDistance * 0.2f;
        float amplitude = 250f;

        // Combined deformation function
        Func<float, float, int, float> foldPairDeform = (x, baseY, layerIndex) =>
        {
            float layerFactor = 1.0f - layerIndex * 0.1f;

            // Anticline contribution
            float distFromAnticline = x - anticlineCenter;
            float anticlineFactor = MathF.Exp(-(distFromAnticline * distFromAnticline) / (foldWidth * foldWidth * 0.5f));
            float anticlineOffset = amplitude * layerFactor * anticlineFactor;

            // Syncline contribution
            float distFromSyncline = x - synclineCenter;
            float synclineFactor = MathF.Exp(-(distFromSyncline * distFromSyncline) / (foldWidth * foldWidth * 0.5f));
            float synclineOffset = -amplitude * 0.8f * layerFactor * synclineFactor;

            return baseY + anticlineOffset + synclineOffset;
        };

        // Build folded layers
        BuildStackedLayers(section, StandardColumn.Take(5).ToArray(), foldPairDeform);

        // Add basement
        AddBasement(section, totalDistance, baseElevation, foldPairDeform);

        return section;
    }

    /// <summary>
    /// Angular unconformity - tilted lower sequence overlain by horizontal upper sequence.
    /// Shows geological time gap and erosion surface.
    /// </summary>
    private static CrossSection CreateUnconformitySequence(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Flat topography
        ApplyFlatTopography(section.Profile, 0f);

        var numPoints = 100;
        float[] xPositions = new float[numPoints + 1];
        for (int i = 0; i <= numPoints; i++)
        {
            xPositions[i] = i / (float)numPoints * totalDistance;
        }

        // Lower sequence - tilted 15 degrees
        float tiltAngle = 15f * MathF.PI / 180f;
        var lowerLayers = StandardColumn.Skip(4).Take(3).ToArray();
        float lowerBaseTop = -400f;

        for (int layerIdx = 0; layerIdx < lowerLayers.Length; layerIdx++)
        {
            var layer = lowerLayers[layerIdx];
            var formation = new ProjectedFormation
            {
                Name = layer.Name + " (Lower)",
                Color = layer.Color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            for (int i = 0; i <= numPoints; i++)
            {
                float x = xPositions[i];
                float tiltOffset = x * MathF.Tan(tiltAngle);
                float topY = lowerBaseTop + tiltOffset;
                float bottomY = topY - layer.Thickness;

                formation.TopBoundary.Add(new Vector2(x, topY));
                formation.BottomBoundary.Add(new Vector2(x, bottomY));
            }

            section.Formations.Add(formation);
            lowerBaseTop -= layer.Thickness;
        }

        // Upper sequence - horizontal, on top of erosion surface
        var upperLayers = StandardColumn.Take(4).ToArray();
        float upperBaseTop = -10f; // Just below topography

        for (int layerIdx = 0; layerIdx < upperLayers.Length; layerIdx++)
        {
            var layer = upperLayers[layerIdx];
            var formation = new ProjectedFormation
            {
                Name = layer.Name + " (Upper)",
                Color = layer.Color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            for (int i = 0; i <= numPoints; i++)
            {
                float x = xPositions[i];
                float topY = upperBaseTop;
                float bottomY = topY - layer.Thickness;

                formation.TopBoundary.Add(new Vector2(x, topY));
                formation.BottomBoundary.Add(new Vector2(x, bottomY));
            }

            section.Formations.Add(formation);
            upperBaseTop -= layer.Thickness;
        }

        // Add basement
        AddBasement(section, totalDistance, baseElevation);

        return section;
    }

    /// <summary>
    /// Sedimentary basin with layers thickening toward center.
    /// Classic syn-sedimentary basin geometry.
    /// </summary>
    private static CrossSection CreateBasinFilling(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Flat topography
        ApplyFlatTopography(section.Profile, 0f);

        var numPoints = 100;
        float[] xPositions = new float[numPoints + 1];
        for (int i = 0; i <= numPoints; i++)
        {
            xPositions[i] = i / (float)numPoints * totalDistance;
        }

        float basinCenter = totalDistance * 0.5f;
        float basinWidth = totalDistance * 0.7f;
        var layers = StandardColumn.Take(6).ToArray();

        float currentTop = -10f; // Start just below topography

        for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
        {
            var layer = layers[layerIdx];
            var formation = new ProjectedFormation
            {
                Name = layer.Name,
                Color = layer.Color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            // Older layers have more basin shape (thicker in center)
            float basinFactor = 1.0f + layerIdx * 0.3f; // Increases with depth

            for (int i = 0; i <= numPoints; i++)
            {
                float x = xPositions[i];
                float distFromCenter = MathF.Abs(x - basinCenter);

                // Basin shape - thicker in center, thinner at edges
                float normalizedDist = distFromCenter / (basinWidth / 2f);
                normalizedDist = Math.Clamp(normalizedDist, 0f, 1f);

                // Thickness varies from full at center to reduced at edges
                float thicknessMult = 1.0f - normalizedDist * normalizedDist * 0.6f * basinFactor;
                thicknessMult = Math.Clamp(thicknessMult, 0.4f, 1.0f);

                float actualThickness = layer.Thickness * thicknessMult;

                float topY = layerIdx == 0 ? currentTop : formation.TopBoundary.Count > 0 ?
                    section.Formations[^1].BottomBoundary[i].Y : currentTop;
                float bottomY = topY - actualThickness;

                formation.TopBoundary.Add(new Vector2(x, topY));
                formation.BottomBoundary.Add(new Vector2(x, bottomY));
            }

            section.Formations.Add(formation);
        }

        // Add basement
        AddBasement(section, totalDistance, baseElevation);

        return section;
    }

    /// <summary>
    /// Incised valley with channel fill - lens-shaped sand body.
    /// Shows fluvial geology in cross-section.
    /// </summary>
    private static CrossSection CreateChannelFill(float totalDistance, float baseElevation)
    {
        var section = CreateBaseCrossSection(totalDistance, baseElevation, 0f);

        // Flat topography
        ApplyFlatTopography(section.Profile, 0f);

        var numPoints = 100;
        float[] xPositions = new float[numPoints + 1];
        for (int i = 0; i <= numPoints; i++)
        {
            xPositions[i] = i / (float)numPoints * totalDistance;
        }

        // Build base layers first
        var baseLayers = StandardColumn.Skip(4).Take(3).ToArray();
        float currentTop = -10f;

        foreach (var layer in baseLayers)
        {
            var formation = new ProjectedFormation
            {
                Name = layer.Name,
                Color = layer.Color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            for (int i = 0; i <= numPoints; i++)
            {
                float x = xPositions[i];
                formation.TopBoundary.Add(new Vector2(x, currentTop));
                formation.BottomBoundary.Add(new Vector2(x, currentTop - layer.Thickness));
            }

            section.Formations.Add(formation);
            currentTop -= layer.Thickness;
        }

        // Add channel fill (lens-shaped)
        float channelCenter = totalDistance * 0.5f;
        float channelWidth = totalDistance * 0.25f;
        float channelDepth = 120f;

        var channelFill = new ProjectedFormation
        {
            Name = "Channel Sand",
            Color = new Vector4(0.95f, 0.88f, 0.6f, 0.85f),
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };

        float channelTop = section.Formations[0].BottomBoundary[0].Y; // Just below first layer

        for (int i = 0; i <= numPoints; i++)
        {
            float x = xPositions[i];
            float distFromCenter = MathF.Abs(x - channelCenter);

            float thickness = 0f;
            if (distFromCenter < channelWidth / 2f)
            {
                float normalizedDist = distFromCenter / (channelWidth / 2f);
                // Parabolic channel shape
                thickness = channelDepth * (1.0f - normalizedDist * normalizedDist);
            }

            // Channel cuts into underlying layers
            float topY = channelTop + thickness * 0.1f; // Slight mound
            float bottomY = channelTop - thickness;

            channelFill.TopBoundary.Add(new Vector2(x, topY));
            channelFill.BottomBoundary.Add(new Vector2(x, bottomY));
        }

        section.Formations.Insert(0, channelFill);

        // Add basement
        AddBasement(section, totalDistance, baseElevation);

        return section;
    }

    #endregion

    #region Helper Methods

    private static void BuildStackedLayers(
        CrossSection section,
        LayerDefinition[] layers,
        Func<float, float, int, float> deformationFunc)
    {
        if (section.Profile?.Points == null || section.Profile.Points.Count < 2)
            return;

        var numPoints = 100;
        float[] xPositions = new float[numPoints + 1];
        for (int i = 0; i <= numPoints; i++)
        {
            xPositions[i] = i / (float)numPoints * section.Profile.TotalDistance;
        }

        // Start from topography and build down
        float[] topographyElevations = new float[numPoints + 1];
        for (int i = 0; i <= numPoints; i++)
        {
            topographyElevations[i] = GetTopographyElevationAt(section.Profile, xPositions[i]) - 10f;
        }

        // Track the bottom of each previous layer to stack correctly
        float[] previousBottoms = (float[])topographyElevations.Clone();

        for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
        {
            var layer = layers[layerIdx];
            var formation = new ProjectedFormation
            {
                Name = layer.Name,
                Color = layer.Color,
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };

            float[] currentTops = new float[numPoints + 1];
            float[] currentBottoms = new float[numPoints + 1];

            for (int i = 0; i <= numPoints; i++)
            {
                float x = xPositions[i];

                // Top of this layer is bottom of previous layer
                float topY = previousBottoms[i];

                // Apply deformation if specified
                if (deformationFunc != null)
                {
                    // Calculate base position before deformation
                    float baseY = topY;
                    topY = deformationFunc(x, baseY, layerIdx);
                }

                // Bottom is top minus thickness
                float bottomY = topY - layer.Thickness;

                // Clip to topography if necessary
                float topoElev = GetTopographyElevationAt(section.Profile, x);
                if (topY > topoElev)
                {
                    topY = topoElev - 5f; // Keep slightly below
                }

                currentTops[i] = topY;
                currentBottoms[i] = bottomY;

                formation.TopBoundary.Add(new Vector2(x, topY));
                formation.BottomBoundary.Add(new Vector2(x, bottomY));
            }

            section.Formations.Add(formation);
            previousBottoms = currentBottoms;
        }
    }

    private static void AddBasement(
        CrossSection section,
        float totalDistance,
        float baseElevation,
        Func<float, float, int, float> deformationFunc = null)
    {
        var basement = new ProjectedFormation
        {
            Name = "Crystalline Basement",
            Color = new Vector4(0.4f, 0.35f, 0.35f, 0.9f),
            TopBoundary = new List<Vector2>(),
            BottomBoundary = new List<Vector2>()
        };

        var numPoints = 100;
        float basementTop = baseElevation + 200f;

        for (int i = 0; i <= numPoints; i++)
        {
            float x = i / (float)numPoints * totalDistance;
            float topY = basementTop;
            float bottomY = baseElevation - 500f;

            if (deformationFunc != null)
            {
                topY = deformationFunc(x, topY, 10); // Use high layer index for reduced deformation
            }

            basement.TopBoundary.Add(new Vector2(x, topY));
            basement.BottomBoundary.Add(new Vector2(x, bottomY));
        }

        section.Formations.Add(basement);
    }

    private static CrossSection CreateBaseCrossSection(float totalDistance, float baseElevation, float topElevation)
    {
        var profile = new ProfileGenerator.TopographicProfile
        {
            Name = "Cross Section",
            TotalDistance = totalDistance,
            MinElevation = baseElevation,
            MaxElevation = topElevation + 500f,
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(totalDistance, 0),
            CreatedAt = DateTime.Now,
            VerticalExaggeration = 2.0f,
            Points = new List<ProfileGenerator.ProfilePoint>()
        };

        // Generate initial flat topography
        var numPoints = 50;
        for (var i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * totalDistance;
            profile.Points.Add(new ProfileGenerator.ProfilePoint
            {
                Position = new Vector2(distance, topElevation),
                Distance = distance,
                Elevation = topElevation,
                Features = new List<GeologicalFeature>()
            });
        }

        return new CrossSection
        {
            Profile = profile,
            VerticalExaggeration = 2.0f,
            Formations = new List<ProjectedFormation>(),
            Faults = new List<ProjectedFault>()
        };
    }

    #region Topography Presets

    private static void ApplyFlatTopography(ProfileGenerator.TopographicProfile profile, float elevation)
    {
        foreach (var point in profile.Points)
        {
            point.Elevation = elevation;
            point.Position = new Vector2(point.Distance, elevation);
        }
    }

    private static void ApplyValleyTopography(ProfileGenerator.TopographicProfile profile, float center, float depth, float width)
    {
        foreach (var point in profile.Points)
        {
            float distFromCenter = MathF.Abs(point.Distance - center);
            float valleyFactor = MathF.Exp(-(distFromCenter * distFromCenter) / (width * width * 0.3f));
            float elevation = 100f - depth * valleyFactor;
            point.Elevation = elevation;
            point.Position = new Vector2(point.Distance, elevation);
        }
        profile.MaxElevation = profile.Points.Max(p => p.Elevation) + 200f;
    }

    private static void ApplyHillyTopography(ProfileGenerator.TopographicProfile profile, int numHills, float amplitude)
    {
        float totalDist = profile.TotalDistance;
        float hillSpacing = totalDist / (numHills + 1);

        foreach (var point in profile.Points)
        {
            float elevation = 0f;
            for (int h = 0; h < numHills; h++)
            {
                float hillCenter = hillSpacing * (h + 1);
                float distFromHill = point.Distance - hillCenter;
                float hillWidth = hillSpacing * 0.4f;
                float hillFactor = MathF.Exp(-(distFromHill * distFromHill) / (hillWidth * hillWidth));
                elevation += amplitude * hillFactor;
            }
            point.Elevation = elevation;
            point.Position = new Vector2(point.Distance, elevation);
        }
        profile.MaxElevation = profile.Points.Max(p => p.Elevation) + 200f;
    }

    private static void ApplyEscarpmentTopography(ProfileGenerator.TopographicProfile profile, float escarpmentX, float height)
    {
        foreach (var point in profile.Points)
        {
            float transition = (point.Distance - escarpmentX) / 500f; // 500m transition zone
            transition = Math.Clamp(transition, -1f, 1f);
            float elevation = height * 0.5f * (1f + transition);
            point.Elevation = elevation;
            point.Position = new Vector2(point.Distance, elevation);
        }
        profile.MaxElevation = profile.Points.Max(p => p.Elevation) + 200f;
    }

    private static void ApplyAsymmetricTopography(ProfileGenerator.TopographicProfile profile, float peakX, float height)
    {
        float totalDist = profile.TotalDistance;
        foreach (var point in profile.Points)
        {
            float normalizedX = point.Distance / totalDist;
            float peakNorm = peakX / totalDist;

            float elevation;
            if (point.Distance < peakX)
            {
                // Steeper west side
                elevation = height * (point.Distance / peakX);
            }
            else
            {
                // Gentler east side
                elevation = height * (1f - (point.Distance - peakX) / (totalDist - peakX));
            }

            point.Elevation = elevation;
            point.Position = new Vector2(point.Distance, elevation);
        }
        profile.MaxElevation = profile.Points.Max(p => p.Elevation) + 200f;
    }

    #endregion

    #region Fault Creation

    private static ProjectedFault CreateThrustFault(float startX, float startY, float endX, float endY, float dip)
    {
        return new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Thrust,
            FaultTrace = new List<Vector2>
            {
                new Vector2(startX, startY),
                new Vector2(endX, endY)
            },
            Dip = dip,
            DipDirection = "E",
            Displacement = 200f
        };
    }

    private static ProjectedFault CreateNormalFault(float startX, float startY, float endX, float endY, float dip, string dipDir)
    {
        return new ProjectedFault
        {
            Type = GeologicalFeatureType.Fault_Normal,
            FaultTrace = new List<Vector2>
            {
                new Vector2(startX, startY),
                new Vector2(endX, endY)
            },
            Dip = dip,
            DipDirection = dipDir,
            Displacement = 150f
        };
    }

    #endregion

    private static float GetTopographyElevationAt(ProfileGenerator.TopographicProfile profile, float x)
    {
        if (profile.Points.Count == 0) return 0f;

        for (int i = 0; i < profile.Points.Count - 1; i++)
        {
            var p1 = profile.Points[i];
            var p2 = profile.Points[i + 1];

            if (x >= p1.Distance && x <= p2.Distance)
            {
                if (p2.Distance - p1.Distance < 0.001f)
                    return p1.Elevation;

                float t = (x - p1.Distance) / (p2.Distance - p1.Distance);
                return p1.Elevation + t * (p2.Elevation - p1.Elevation);
            }
        }

        if (x <= profile.Points[0].Distance)
            return profile.Points[0].Elevation;

        return profile.Points[^1].Elevation;
    }

    private static void ValidateFinalGeology(CrossSection section)
    {
        // Check for any overlaps and fix them
        var overlaps = GeologicalConstraints.FindAllOverlaps(section.Formations, tolerance: 1f);
        if (overlaps.Count > 0)
        {
            Logger.LogWarning($"Preset has {overlaps.Count} overlaps - auto-fixing...");
            GeologicalConstraints.ResolveAllOverlaps(section.Formations);
        }
    }

    #endregion

    #region Public Info Methods

    public static string GetPresetName(PresetScenario scenario) => scenario switch
    {
        PresetScenario.SimpleLayers => "Simple Horizontal Layers",
        PresetScenario.ErodedAnticline => "Eroded Anticline",
        PresetScenario.ErodedSyncline => "Eroded Syncline",
        PresetScenario.JuraMountains => "Jura Mountains Style",
        PresetScenario.FaultedLayers => "Normal Faults (Graben)",
        PresetScenario.ThrustFault => "Thrust Fault System",
        PresetScenario.FoldedSequence => "Anticline-Syncline Pair",
        PresetScenario.UnconformitySequence => "Angular Unconformity",
        PresetScenario.BasinFilling => "Sedimentary Basin",
        PresetScenario.ChannelFill => "Channel Fill",
        _ => "Unknown"
    };

    public static string GetPresetDescription(PresetScenario scenario) => scenario switch
    {
        PresetScenario.SimpleLayers => "Horizontal sedimentary layers - ideal for testing restoration",
        PresetScenario.ErodedAnticline => "Anticline with erosion exposing older rocks at core",
        PresetScenario.ErodedSyncline => "Syncline preserving younger rocks in trough",
        PresetScenario.JuraMountains => "Multiple folds with thrust faults (thin-skinned tectonics)",
        PresetScenario.FaultedLayers => "Graben structure bounded by normal faults",
        PresetScenario.ThrustFault => "Thrust system with older rocks over younger",
        PresetScenario.FoldedSequence => "Complete fold train with anticline and syncline",
        PresetScenario.UnconformitySequence => "Tilted sequence overlain by horizontal beds",
        PresetScenario.BasinFilling => "Basin with layers thickening toward center",
        PresetScenario.ChannelFill => "Incised valley with lens-shaped channel sand",
        _ => "Unknown preset"
    };

    #endregion

    private readonly struct LayerDefinition
    {
        public readonly string Name;
        public readonly Vector4 Color;
        public readonly float Thickness;

        public LayerDefinition(string name, Vector4 color, float thickness)
        {
            Name = name;
            Color = color;
            Thickness = thickness;
        }
    }
}
