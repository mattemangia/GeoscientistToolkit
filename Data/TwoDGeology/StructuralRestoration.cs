// GeoscientistToolkit/Business/GIS/StructuralRestoration.cs

using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
///     Performs structural restoration (unfolding) and forward modeling (folding)
///     on geological cross-sections.
///     This implementation is bidirectional and can:
///     1. Restore (unfold + unfault) deformed sections to pre-deformation state
///     2. Forward model (fold + fault) undeformed sections to predict future/alternative states
///     3. Validate interpretations by checking if restoration produces geologically reasonable results
///     References:
///     - Flexural slip unfolding: Chamberlin (1910), Dahlstrom (1969), Ramsay & Huber (1987)
///     - Fault restoration: Gibbs (1983), Suppe (1983), Allmendinger et al. (2012)
///     - Fold mechanics: Biot (1961), Ramsay (1967), Hudleston & Lan (1993)
///     - SIMD optimization: Intel Intrinsics Guide, ARM NEON Programmer's Guide
/// </summary>
public class StructuralRestoration
{
    // Store transformation parameters by INDEX for reliable matching
    private readonly List<FaultRestorationData> _faultDataByIndex = new();
    private readonly List<FoldingData> _foldingDataByIndex = new();
    private readonly CrossSectionGenerator.CrossSection _originalSection;

    public StructuralRestoration(CrossSectionGenerator.CrossSection section)
    {
        _originalSection = section ?? throw new ArgumentNullException(nameof(section));
        RestoredSection = DeepCopySection(section);
        AnalyzeStructures();
    }

    public CrossSectionGenerator.CrossSection RestoredSection { get; private set; }

    /// <summary>
    ///     Analyze the original section to extract fold and fault geometries
    /// </summary>
    private void AnalyzeStructures()
    {
        _faultDataByIndex.Clear();
        _foldingDataByIndex.Clear();

        // Analyze each fault BY INDEX
        for (int i = 0; i < _originalSection.Faults.Count; i++)
        {
            var fault = _originalSection.Faults[i];
            var data = new FaultRestorationData
            {
                OriginalTrace = new List<Vector2>(fault.FaultTrace),
                Displacement = CalculateFaultDisplacement(fault),
                HangingWallCutoff = fault.FaultTrace.Count > 0 ? fault.FaultTrace[0] : Vector2.Zero,
                FootwallCutoff = fault.FaultTrace.Count > 0 ? fault.FaultTrace[^1] : Vector2.Zero,
                FaultType = fault.Type,
                Dip = fault.Dip
            };
            _faultDataByIndex.Add(data);
        }

        // Analyze each formation for fold geometry BY INDEX
        for (int i = 0; i < _originalSection.Formations.Count; i++)
        {
            var formation = _originalSection.Formations[i];
            var data = new FoldingData
            {
                OriginalTop = new List<Vector2>(formation.TopBoundary),
                OriginalBottom = new List<Vector2>(formation.BottomBoundary),
                FoldStyle = DetermineFoldStyle(formation),
                Wavelength = CalculateFoldWavelength(formation.TopBoundary),
                Amplitude = CalculateFoldAmplitude(formation.TopBoundary),
                AxialSurfacePosition = FindAxialSurface(formation.TopBoundary),
                OriginalThickness = CalculateFormationThickness(formation)
            };
            _foldingDataByIndex.Add(data);
        }
    }

    #region Public Interface

    /// <summary>
    ///     Restores the section to a flattened state by a given percentage.
    ///     0% = fully deformed (original), 100% = fully restored (flat).
    /// </summary>
    public void Restore(float percentage)
    {
        percentage = Math.Clamp(percentage, 0f, 100f);

        // Always start fresh from the original section
        RestoredSection = DeepCopySection(_originalSection);

        if (percentage < 0.01f)
        {
            // At 0%, just show the original - no transformation needed
            return;
        }

        // Step 1: Remove fault displacements (unfaulting)
        UnfaultAllFaults(percentage);

        // Step 2: Remove folds (unfolding) - this is the main restoration step
        UnfoldAllFormations(percentage);
    }

    /// <summary>
    ///     Forward models deformation on the section by a given percentage.
    ///     0% = undeformed (flat), 100% = fully deformed.
    ///     Can be used independently to predict future deformation states.
    /// </summary>
    public void Deform(float percentage)
    {
        percentage = Math.Clamp(percentage, 0f, 100f);

        // Start from a flat reference state
        CreateFlatReference();

        if (percentage < 0.01f)
        {
            // At 0%, show the flat reference
            return;
        }

        // Apply folding first (chronologically correct for many settings)
        FoldAllFormations(percentage);

        // Then apply faulting
        FaultAllFaults(percentage);
    }

    /// <summary>
    ///     Creates a completely flat reference state for forward modeling
    /// </summary>
    public void CreateFlatReference()
    {
        RestoredSection = DeepCopySection(_originalSection);

        // Flatten all formations to horizontal layers stacked from top to bottom
        var sortedFormations = RestoredSection.Formations
            .Select((f, idx) => (formation: f, data: idx < _foldingDataByIndex.Count ? _foldingDataByIndex[idx] : null))
            .Where(x => x.data != null)
            .OrderByDescending(x => x.data.OriginalTop.Count > 0 ? x.data.OriginalTop.Average(p => p.Y) : float.MinValue)
            .ToList();

        float datumElevation = 0f;
        float cumulativeDepth = 0f;

        foreach (var (formation, data) in sortedFormations)
        {
            var thickness = data.OriginalThickness;
            var topElev = datumElevation - cumulativeDepth;
            var bottomElev = topElev - thickness;

            FlattenBoundary(formation.TopBoundary, topElev);
            FlattenBoundary(formation.BottomBoundary, bottomElev);

            cumulativeDepth += thickness;
        }

        // Flatten faults to vertical lines
        foreach (var fault in RestoredSection.Faults)
        {
            if (fault.FaultTrace.Count >= 2)
            {
                var x = fault.FaultTrace[0].X;
                var minY = fault.FaultTrace.Min(p => p.Y);
                var maxY = fault.FaultTrace.Max(p => p.Y);
                fault.FaultTrace.Clear();
                fault.FaultTrace.Add(new Vector2(x, maxY));
                fault.FaultTrace.Add(new Vector2(x, minY));
            }
        }
    }

    #endregion

    #region Unfaulting (Restoration)

    private void UnfaultAllFaults(float percentage)
    {
        // Process faults from youngest to oldest (typically top to bottom in section)
        // Use indices to properly match faults to their restoration data
        var faultIndices = Enumerable.Range(0, Math.Min(RestoredSection.Faults.Count, _faultDataByIndex.Count))
            .OrderByDescending(i => RestoredSection.Faults[i].FaultTrace.Count > 0
                ? RestoredSection.Faults[i].FaultTrace[0].Y
                : float.MinValue)
            .ToList();

        foreach (var faultIndex in faultIndices)
        {
            var fault = RestoredSection.Faults[faultIndex];
            var data = _faultDataByIndex[faultIndex];

            // Calculate restoration vector (opposite of displacement, scaled by percentage)
            var restorationVector = -data.Displacement * (percentage / 100f);

            // Apply to all formations in the hanging wall
            foreach (var formation in RestoredSection.Formations)
            {
                ApplyVectorToHangingWall(formation.TopBoundary, fault, data, restorationVector);
                ApplyVectorToHangingWall(formation.BottomBoundary, fault, data, restorationVector);
            }
        }
    }

    private Vector2 CalculateFaultDisplacement(CrossSectionGenerator.ProjectedFault fault)
    {
        if (fault.FaultTrace.Count < 2) return Vector2.Zero;

        // Use defined displacement if available
        if (fault.Displacement.HasValue && fault.Displacement.Value > 0)
        {
            var dipRad = fault.Dip * MathF.PI / 180f;
            var heave = fault.Displacement.Value * MathF.Cos(dipRad);
            var throw_ = fault.Displacement.Value * MathF.Sin(dipRad);

            // Direction depends on fault type
            if (fault.Type == GeologicalFeatureType.Fault_Normal)
            {
                return new Vector2(heave, -throw_); // Normal faults: hanging wall moves down
            }
            else if (fault.Type == GeologicalFeatureType.Fault_Reverse ||
                     fault.Type == GeologicalFeatureType.Fault_Thrust)
            {
                return new Vector2(-heave, throw_); // Reverse/thrust: hanging wall moves up
            }
            return new Vector2(heave, throw_);
        }

        // Estimate from geometry if no displacement specified
        var p1 = fault.FaultTrace[0];
        var p2 = fault.FaultTrace[^1];
        var estimatedDisplacement = Vector2.Distance(p1, p2) * 0.1f; // Estimate 10% of trace length

        var dipRad2 = fault.Dip * MathF.PI / 180f;
        return new Vector2(
            estimatedDisplacement * MathF.Cos(dipRad2),
            estimatedDisplacement * MathF.Sin(dipRad2)
        );
    }

    #endregion

    #region Faulting (Forward Modeling)

    private void FaultAllFaults(float percentage)
    {
        // Process faults from oldest to youngest (bottom to top)
        var faultIndices = Enumerable.Range(0, Math.Min(RestoredSection.Faults.Count, _faultDataByIndex.Count))
            .OrderBy(i => RestoredSection.Faults[i].FaultTrace.Count > 0
                ? RestoredSection.Faults[i].FaultTrace[0].Y
                : float.MaxValue)
            .ToList();

        foreach (var faultIndex in faultIndices)
        {
            var fault = RestoredSection.Faults[faultIndex];
            var data = _faultDataByIndex[faultIndex];

            // Calculate displacement vector (scaled by percentage)
            var displacementVector = data.Displacement * (percentage / 100f);

            // Apply to all formations
            foreach (var formation in RestoredSection.Formations)
            {
                ApplyVectorToHangingWall(formation.TopBoundary, fault, data, displacementVector);
                ApplyVectorToHangingWall(formation.BottomBoundary, fault, data, displacementVector);
            }
        }
    }

    #endregion

    #region Unfolding (Restoration)

    private void UnfoldAllFormations(float percentage)
    {
        if (RestoredSection.Formations.Count == 0 || _foldingDataByIndex.Count == 0)
            return;

        // Find a datum - use the stratigraphically highest formation's average top elevation
        var formationsWithData = RestoredSection.Formations
            .Select((f, idx) => (formation: f, data: idx < _foldingDataByIndex.Count ? _foldingDataByIndex[idx] : null, index: idx))
            .Where(x => x.data != null)
            .ToList();

        if (formationsWithData.Count == 0) return;

        // Sort by average Y to establish stratigraphic order (highest = youngest)
        var sortedByElevation = formationsWithData
            .OrderByDescending(x => x.data.OriginalTop.Count > 0 ? x.data.OriginalTop.Average(p => p.Y) : float.MinValue)
            .ToList();

        // The datum is the average elevation of the top formation (this becomes horizontal at 100%)
        var datumFormationData = sortedByElevation[0].data;
        var datumElevation = datumFormationData.OriginalTop.Count > 0
            ? datumFormationData.OriginalTop.Average(p => p.Y)
            : 0f;

        // Calculate cumulative depth from datum for each formation
        float cumulativeDepth = 0f;

        foreach (var (formation, data, originalIndex) in sortedByElevation)
        {
            var thickness = data.OriginalThickness;

            // Target elevations for this formation when fully restored
            var targetTopElevation = datumElevation - cumulativeDepth;
            var targetBottomElevation = targetTopElevation - thickness;

            // Unfold using flexural slip method
            UnfoldBoundaryFlexuralSlip(
                formation.TopBoundary,
                data.OriginalTop,
                targetTopElevation,
                percentage,
                data
            );

            UnfoldBoundaryFlexuralSlip(
                formation.BottomBoundary,
                data.OriginalBottom,
                targetBottomElevation,
                percentage,
                data
            );

            cumulativeDepth += thickness;
        }
    }

    /// <summary>
    ///     Unfolds a boundary using flexural slip mechanics.
    ///     This method preserves bed length, which is the fundamental constraint
    ///     for flexural slip folding (Ramsay, 1967; Ramsay & Huber, 1987).
    /// </summary>
    private void UnfoldBoundaryFlexuralSlip(
        List<Vector2> boundary,
        List<Vector2> originalBoundary,
        float targetElevation,
        float percentage,
        FoldingData foldData)
    {
        if (boundary.Count < 2 || originalBoundary.Count < 2) return;

        // Calculate the total arc length of the original (deformed) boundary
        var totalArcLength = CalculateLineLength(originalBoundary);

        // Find a stable pin point (minimum curvature point)
        var pinIndex = FindPinPoint(originalBoundary);
        var pinX = originalBoundary[pinIndex].X;

        // Calculate arc lengths from pin point for the original deformed shape
        var arcLengths = CalculateArcLengths(originalBoundary, pinIndex);

        // Create the target flattened positions (preserving arc length = bed length)
        var flattenedBoundary = new List<Vector2>(boundary.Count);

        for (var i = 0; i < originalBoundary.Count; i++)
        {
            float x;
            if (i < pinIndex)
            {
                // Points before pin - extend to the left based on arc length
                x = pinX - arcLengths[i];
            }
            else if (i > pinIndex)
            {
                // Points after pin - extend to the right based on arc length
                x = pinX + arcLengths[i];
            }
            else
            {
                // Pin point stays at same X
                x = pinX;
            }

            flattenedBoundary.Add(new Vector2(x, targetElevation));
        }

        // Ensure we have the same number of points
        while (flattenedBoundary.Count < boundary.Count)
        {
            flattenedBoundary.Add(flattenedBoundary[^1]);
        }
        while (flattenedBoundary.Count > boundary.Count)
        {
            flattenedBoundary.RemoveAt(flattenedBoundary.Count - 1);
        }

        // Interpolate between current (deformed) state and flattened state based on percentage
        var t = percentage / 100f;
        for (var i = 0; i < boundary.Count; i++)
        {
            var current = boundary[i];
            var target = flattenedBoundary[i];
            boundary[i] = Vector2.Lerp(current, target, t);
        }
    }

    /// <summary>
    ///     Finds the best pin point - the point with minimum curvature (most stable for restoration)
    /// </summary>
    private int FindPinPoint(List<Vector2> boundary)
    {
        if (boundary.Count < 3) return boundary.Count / 2;

        var minCurvatureIndex = 1;
        var minCurvature = float.MaxValue;

        for (var i = 1; i < boundary.Count - 1; i++)
        {
            var curvature = CalculateCurvature(boundary[i - 1], boundary[i], boundary[i + 1]);
            if (curvature < minCurvature)
            {
                minCurvature = curvature;
                minCurvatureIndex = i;
            }
        }

        return minCurvatureIndex;
    }

    private float CalculateCurvature(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        var v1 = p2 - p1;
        var v2 = p3 - p2;

        var len1 = v1.Length();
        var len2 = v2.Length();

        if (len1 < 1e-6f || len2 < 1e-6f) return 0f;

        v1 /= len1;
        v2 /= len2;

        var dotProduct = Math.Clamp(Vector2.Dot(v1, v2), -1f, 1f);
        var angle = MathF.Acos(dotProduct);
        return angle;
    }

    /// <summary>
    ///     Calculates cumulative arc lengths from a reference index
    /// </summary>
    private List<float> CalculateArcLengths(List<Vector2> boundary, int referenceIndex)
    {
        var arcLengths = new List<float>(boundary.Count);
        for (var i = 0; i < boundary.Count; i++)
            arcLengths.Add(0f);

        // Calculate lengths moving backward from reference
        var cumulative = 0f;
        for (var i = referenceIndex - 1; i >= 0; i--)
        {
            cumulative += Vector2.Distance(boundary[i], boundary[i + 1]);
            arcLengths[i] = cumulative;
        }

        // Calculate lengths moving forward from reference
        cumulative = 0f;
        for (var i = referenceIndex + 1; i < boundary.Count; i++)
        {
            cumulative += Vector2.Distance(boundary[i - 1], boundary[i]);
            arcLengths[i] = cumulative;
        }

        return arcLengths;
    }

    #endregion

    #region Folding (Forward Modeling)

    private void FoldAllFormations(float percentage)
    {
        for (int i = 0; i < RestoredSection.Formations.Count && i < _foldingDataByIndex.Count; i++)
        {
            var formation = RestoredSection.Formations[i];
            var data = _foldingDataByIndex[i];

            FoldBoundary(formation.TopBoundary, data, percentage);
            FoldBoundary(formation.BottomBoundary, data, percentage);
        }
    }

    /// <summary>
    ///     Applies folding to a boundary using sine wave approximation.
    ///     This is appropriate for many natural folds (Hudleston & Lan, 1993).
    /// </summary>
    private void FoldBoundary(List<Vector2> boundary, FoldingData data, float percentage)
    {
        if (boundary.Count < 2 || data.Wavelength <= 0) return;

        var t = percentage / 100f;

        for (var i = 0; i < boundary.Count; i++)
        {
            var point = boundary[i];

            // Calculate fold displacement using cosine wave for anticline
            // z(x) = A * cos(2*pi * (x - center) / wavelength)
            var normalizedX = (point.X - data.AxialSurfacePosition) / data.Wavelength;
            var verticalDisplacement = data.Amplitude * MathF.Cos(2f * MathF.PI * normalizedX);

            // Apply fold style modification
            if (data.FoldStyle == FoldStyle.Similar)
            {
                // Similar folds maintain constant thickness parallel to axial surface
                var distance = MathF.Abs(point.Y);
                verticalDisplacement *= 1f + distance / 2000f;
            }

            // Interpolate from flat to folded
            boundary[i] = new Vector2(point.X, point.Y + verticalDisplacement * t);
        }
    }

    #endregion

    #region Geometry Analysis

    private FoldStyle DetermineFoldStyle(CrossSectionGenerator.ProjectedFormation formation)
    {
        // Return the formation's stored fold style if available
        if (formation.FoldStyle.HasValue)
            return formation.FoldStyle.Value;

        // Otherwise analyze thickness variation
        if (formation.TopBoundary.Count < 3 || formation.BottomBoundary.Count < 3)
            return FoldStyle.Parallel;

        var thicknesses = new List<float>();
        var samples = Math.Min(formation.TopBoundary.Count, formation.BottomBoundary.Count);

        for (var i = 0; i < samples; i++)
        {
            var thickness = MathF.Abs(formation.TopBoundary[i].Y - formation.BottomBoundary[i].Y);
            thicknesses.Add(thickness);
        }

        if (thicknesses.Count == 0) return FoldStyle.Parallel;

        var meanThickness = thicknesses.Average();
        if (meanThickness < 1e-6f) return FoldStyle.Parallel;

        var variance = thicknesses.Select(t => (t - meanThickness) * (t - meanThickness)).Average();
        var coefficientOfVariation = MathF.Sqrt(variance) / meanThickness;

        // If thickness varies significantly, it's similar fold
        return coefficientOfVariation > 0.2f ? FoldStyle.Similar : FoldStyle.Parallel;
    }

    private float CalculateFoldWavelength(List<Vector2> boundary)
    {
        if (boundary.Count < 3) return 1000f;

        // Find peaks and troughs (local extrema in Y)
        var extrema = new List<int>();
        for (var i = 1; i < boundary.Count - 1; i++)
        {
            var isPeak = boundary[i].Y > boundary[i - 1].Y && boundary[i].Y > boundary[i + 1].Y;
            var isTrough = boundary[i].Y < boundary[i - 1].Y && boundary[i].Y < boundary[i + 1].Y;

            if (isPeak || isTrough)
                extrema.Add(i);
        }

        if (extrema.Count < 2)
        {
            // No clear fold - use total X range
            return boundary[^1].X - boundary[0].X;
        }

        // Calculate average distance between consecutive extrema (half-wavelength)
        var totalDistance = 0f;
        for (var i = 0; i < extrema.Count - 1; i++)
            totalDistance += boundary[extrema[i + 1]].X - boundary[extrema[i]].X;

        var halfWavelength = totalDistance / (extrema.Count - 1);
        return halfWavelength * 2f; // Full wavelength
    }

    private float CalculateFoldAmplitude(List<Vector2> boundary)
    {
        if (boundary.Count < 2) return 0f;

        var maxY = boundary.Max(p => p.Y);
        var minY = boundary.Min(p => p.Y);

        return (maxY - minY) / 2f;
    }

    private float FindAxialSurface(List<Vector2> boundary)
    {
        if (boundary.Count < 3) return boundary.Count > 0 ? boundary[boundary.Count / 2].X : 0f;

        // Find point of maximum curvature as approximate axial surface position
        var maxCurvature = 0f;
        var maxIndex = boundary.Count / 2;

        for (var i = 1; i < boundary.Count - 1; i++)
        {
            var curvature = CalculateCurvature(boundary[i - 1], boundary[i], boundary[i + 1]);
            if (curvature > maxCurvature)
            {
                maxCurvature = curvature;
                maxIndex = i;
            }
        }

        return boundary[maxIndex].X;
    }

    private float CalculateFormationThickness(CrossSectionGenerator.ProjectedFormation formation)
    {
        if (formation.TopBoundary.Count == 0 || formation.BottomBoundary.Count == 0)
            return 100f;

        // Calculate average vertical distance between top and bottom
        var samples = Math.Min(formation.TopBoundary.Count, formation.BottomBoundary.Count);
        var totalThickness = 0f;
        var count = 0;

        for (var i = 0; i < samples; i++)
        {
            var thickness = MathF.Abs(formation.TopBoundary[i].Y - formation.BottomBoundary[i].Y);
            if (thickness > 0)
            {
                totalThickness += thickness;
                count++;
            }
        }

        return count > 0 ? totalThickness / count : 100f;
    }

    #endregion

    #region Utility Methods

    private void FlattenBoundary(List<Vector2> boundary, float elevation)
    {
        for (var i = 0; i < boundary.Count; i++)
        {
            boundary[i] = new Vector2(boundary[i].X, elevation);
        }
    }

    private static float CalculateLineLength(List<Vector2> points)
    {
        float length = 0;
        for (var i = 0; i < points.Count - 1; i++)
            length += Vector2.Distance(points[i], points[i + 1]);
        return length;
    }

    /// <summary>
    ///     Applies a vector transformation to points in the hanging wall of a fault.
    ///     Uses SIMD when available for performance.
    /// </summary>
    private void ApplyVectorToHangingWall(
        List<Vector2> points,
        CrossSectionGenerator.ProjectedFault fault,
        FaultRestorationData data,
        Vector2 vector)
    {
        if (Avx2.IsSupported)
            ApplyVectorAvx2(points, fault, data, vector);
        else if (AdvSimd.IsSupported)
            ApplyVectorNeon(points, fault, data, vector);
        else
            ApplyVectorScalar(points, fault, data, vector);
    }

    private void ApplyVectorScalar(
        List<Vector2> points,
        CrossSectionGenerator.ProjectedFault fault,
        FaultRestorationData data,
        Vector2 vector)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (IsHangingWall(points[i], fault, data))
                points[i] += vector;
        }
    }

    private unsafe void ApplyVectorAvx2(
        List<Vector2> points,
        CrossSectionGenerator.ProjectedFault fault,
        FaultRestorationData data,
        Vector2 vector)
    {
        var count = points.Count;
        var restorationVec =
            Vector256.Create(vector.X, vector.Y, vector.X, vector.Y, vector.X, vector.Y, vector.X, vector.Y);

        for (var i = 0; i <= count - 4; i += 4)
        {
            var pointsVec = Vector256.Create(
                points[i].X, points[i].Y,
                points[i + 1].X, points[i + 1].Y,
                points[i + 2].X, points[i + 2].Y,
                points[i + 3].X, points[i + 3].Y);

            var m1 = IsHangingWall(points[i], fault, data) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var m2 = IsHangingWall(points[i + 1], fault, data) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var m3 = IsHangingWall(points[i + 2], fault, data) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var m4 = IsHangingWall(points[i + 3], fault, data) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var maskVec = Vector256.Create(m1, m1, m2, m2, m3, m3, m4, m4);

            var maskedAdd = Avx.BlendVariable(Vector256<float>.Zero, restorationVec, maskVec);
            var result = Avx.Add(pointsVec, maskedAdd);

            var resPtr = (float*)&result;
            points[i] = new Vector2(resPtr[0], resPtr[1]);
            points[i + 1] = new Vector2(resPtr[2], resPtr[3]);
            points[i + 2] = new Vector2(resPtr[4], resPtr[5]);
            points[i + 3] = new Vector2(resPtr[6], resPtr[7]);
        }

        // Handle remaining elements
        for (var i = count - count % 4; i < count; i++)
        {
            if (IsHangingWall(points[i], fault, data))
                points[i] += vector;
        }
    }

    private void ApplyVectorNeon(
        List<Vector2> points,
        CrossSectionGenerator.ProjectedFault fault,
        FaultRestorationData data,
        Vector2 vector)
    {
        var count = points.Count;
        var restorationVec = Vector128.Create(vector.X, vector.Y, vector.X, vector.Y);

        for (var i = 0; i <= count - 2; i += 2)
        {
            var pointsVec = Vector128.Create(
                points[i].X, points[i].Y,
                points[i + 1].X, points[i + 1].Y);

            var m1 = IsHangingWall(points[i], fault, data) ? 0xFFFFFFFF : 0u;
            var m2 = IsHangingWall(points[i + 1], fault, data) ? 0xFFFFFFFF : 0u;
            var mask = Vector128.Create(m1, m1, m2, m2);

            var maskedAddVec = AdvSimd.And(restorationVec, mask.AsSingle());
            var result = AdvSimd.Add(pointsVec, maskedAddVec);

            points[i] = new Vector2(result.GetElement(0), result.GetElement(1));
            points[i + 1] = new Vector2(result.GetElement(2), result.GetElement(3));
        }

        // Handle remaining element
        if (count % 2 != 0)
        {
            if (IsHangingWall(points[count - 1], fault, data))
                points[count - 1] += vector;
        }
    }

    private bool IsHangingWall(
        Vector2 point,
        CrossSectionGenerator.ProjectedFault fault,
        FaultRestorationData data)
    {
        if (fault.FaultTrace.Count < 2) return false;

        // Find the closest segment on the fault trace
        var minDistanceSq = float.MaxValue;
        var closestSegmentIndex = 0;

        for (var i = 0; i < fault.FaultTrace.Count - 1; i++)
        {
            var dist = ProfileGenerator.DistanceToLineSegment(point, fault.FaultTrace[i], fault.FaultTrace[i + 1]);
            var distSq = dist * dist;
            if (distSq < minDistanceSq)
            {
                minDistanceSq = distSq;
                closestSegmentIndex = i;
            }
        }

        var p1 = fault.FaultTrace[closestSegmentIndex];
        var p2 = fault.FaultTrace[closestSegmentIndex + 1];

        // Cross product to determine which side of the fault line the point is on
        var crossProduct = (p2.X - p1.X) * (point.Y - p1.Y) - (p2.Y - p1.Y) * (point.X - p1.X);

        // Determine which side is hanging wall based on dip direction
        var isDippingRight = data.OriginalTrace.Count >= 2 && data.OriginalTrace[^1].X > data.OriginalTrace[0].X;

        // For normal and reverse faults, hanging wall is on the dip side
        return isDippingRight ? crossProduct < 0 : crossProduct > 0;
    }

    private CrossSectionGenerator.CrossSection DeepCopySection(CrossSectionGenerator.CrossSection source)
    {
        var copy = new CrossSectionGenerator.CrossSection
        {
            Profile = source.Profile, // Profile is shared (not modified during restoration)
            VerticalExaggeration = source.VerticalExaggeration,
            Formations = source.Formations.Select(f => new CrossSectionGenerator.ProjectedFormation
            {
                Name = f.Name,
                Color = f.Color,
                TopBoundary = new List<Vector2>(f.TopBoundary),
                BottomBoundary = new List<Vector2>(f.BottomBoundary),
                FoldStyle = f.FoldStyle
            }).ToList(),
            Faults = source.Faults.Select(f => new CrossSectionGenerator.ProjectedFault
            {
                Type = f.Type,
                Dip = f.Dip,
                DipDirection = f.DipDirection,
                Displacement = f.Displacement,
                FaultTrace = new List<Vector2>(f.FaultTrace)
            }).ToList()
        };
        return copy;
    }

    #endregion

    #region Data Classes

    private class FaultRestorationData
    {
        public List<Vector2> OriginalTrace { get; set; } = new();
        public Vector2 Displacement { get; set; }
        public Vector2 HangingWallCutoff { get; set; }
        public Vector2 FootwallCutoff { get; set; }
        public GeologicalFeatureType FaultType { get; set; }
        public float Dip { get; set; }
    }

    private class FoldingData
    {
        public List<Vector2> OriginalTop { get; set; } = new();
        public List<Vector2> OriginalBottom { get; set; } = new();
        public FoldStyle FoldStyle { get; set; }
        public float Wavelength { get; set; }
        public float Amplitude { get; set; }
        public float AxialSurfacePosition { get; set; }
        public float OriginalThickness { get; set; }
    }

    #endregion
}
