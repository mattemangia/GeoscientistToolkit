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
    // Store transformation parameters for reversibility
    private readonly Dictionary<CrossSectionGenerator.ProjectedFault, FaultRestorationData> _faultData = new();
    private readonly Dictionary<CrossSectionGenerator.ProjectedFormation, FoldingData> _foldingData = new();
    private readonly CrossSectionGenerator.CrossSection _originalSection;

    public StructuralRestoration(CrossSectionGenerator.CrossSection section)
    {
        _originalSection = section;
        RestoredSection = DeepCopySection(section);
        AnalyzeStructures();
    }

    public CrossSectionGenerator.CrossSection RestoredSection { get; private set; }

    /// <summary>
    ///     Analyze the original section to extract fold and fault geometries
    /// </summary>
    private void AnalyzeStructures()
    {
        _faultData.Clear();
        _foldingData.Clear();

        // Analyze each fault
        foreach (var fault in _originalSection.Faults)
        {
            var data = new FaultRestorationData
            {
                OriginalTrace = new List<Vector2>(fault.FaultTrace),
                Displacement = CalculateFaultDisplacement(fault),
                HangingWallCutoff = fault.FaultTrace.FirstOrDefault(),
                FootwallCutoff = fault.FaultTrace.LastOrDefault()
            };
            _faultData[fault] = data;
        }

        // Analyze each formation for fold geometry
        foreach (var formation in _originalSection.Formations)
        {
            var data = new FoldingData
            {
                OriginalTop = new List<Vector2>(formation.TopBoundary),
                OriginalBottom = new List<Vector2>(formation.BottomBoundary),
                FoldStyle = DetermineFoldStyle(formation),
                Wavelength = CalculateFoldWavelength(formation.TopBoundary),
                Amplitude = CalculateFoldAmplitude(formation.TopBoundary),
                AxialSurfacePosition = FindAxialSurface(formation.TopBoundary)
            };
            _foldingData[formation] = data;
        }
    }

    #region Faulting (Forward Modeling)

    private void FaultAllFaults(float percentage)
    {
        // Process faults from oldest to youngest (bottom to top)
        var sortedFaults = RestoredSection.Faults
            .OrderBy(f => f.FaultTrace.FirstOrDefault().Y)
            .ToList();

        foreach (var fault in sortedFaults)
        {
            if (!_faultData.ContainsKey(fault)) continue;
            var data = _faultData[fault];

            // Calculate displacement vector
            var displacementVector = data.Displacement * (percentage / 100f);

            // Apply to all formations
            foreach (var formation in RestoredSection.Formations)
            {
                ApplyVectorToPoints(formation.TopBoundary, fault, displacementVector);
                ApplyVectorToPoints(formation.BottomBoundary, fault, displacementVector);
            }
        }
    }

    #endregion

    #region Public Interface

    /// <summary>
    ///     Restores the section to a flattened state by a given percentage.
    ///     0% = fully deformed (original), 100% = fully restored (flat).
    /// </summary>
    public void Restore(float percentage)
    {
        percentage = Math.Clamp(percentage, 0f, 100f);
        RestoredSection = DeepCopySection(_originalSection);

        // Step 1: Remove fault displacements (unfaulting)
        UnfaultAllFaults(percentage);

        // Step 2: Remove folds (unfolding)
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

        // Start from the original (which might already be deformed) 
        // or from a flat state if we want pure forward modeling
        RestoredSection = DeepCopySection(_originalSection);

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

        // Flatten all formations to a horizontal datum
        var datumElevation = 0f;
        var cumulativeThickness = 0f;

        foreach (var formation in RestoredSection.Formations.OrderBy(f => f.TopBoundary.Select(p => p.Y).Max()))
        {
            var thickness = CalculateFormationThickness(formation);
            var topElev = datumElevation - cumulativeThickness;
            var bottomElev = topElev - thickness;

            FlattenBoundary(formation.TopBoundary, topElev);
            FlattenBoundary(formation.BottomBoundary, bottomElev);

            cumulativeThickness += thickness;
        }

        // Remove faults (set to vertical lines)
        foreach (var fault in RestoredSection.Faults)
            if (fault.FaultTrace.Count >= 2)
            {
                var x = fault.FaultTrace[0].X;
                fault.FaultTrace.Clear();
                fault.FaultTrace.Add(new Vector2(x, 0));
                fault.FaultTrace.Add(new Vector2(x, -1000));
            }
    }

    #endregion

    #region Unfaulting (Restoration)

    private void UnfaultAllFaults(float percentage)
    {
        // Process faults from youngest to oldest (top to bottom in section)
        var sortedFaults = RestoredSection.Faults
            .OrderByDescending(f => f.FaultTrace.FirstOrDefault().Y)
            .ToList();

        foreach (var fault in sortedFaults)
        {
            if (!_faultData.ContainsKey(fault)) continue;
            var data = _faultData[fault];

            // Calculate restoration vector (opposite of displacement)
            var restorationVector = -data.Displacement * (percentage / 100f);

            // Apply to all formations
            foreach (var formation in RestoredSection.Formations)
            {
                ApplyVectorToPoints(formation.TopBoundary, fault, restorationVector);
                ApplyVectorToPoints(formation.BottomBoundary, fault, restorationVector);
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
            return new Vector2(heave, throw_);
        }

        // Estimate from geometry
        var p1 = fault.FaultTrace[0];
        var p2 = fault.FaultTrace[^1];
        return new Vector2(p1.X - p2.X, p1.Y - p2.Y);
    }

    #endregion

    #region Unfolding (Restoration)

    private void UnfoldAllFormations(float percentage)
    {
        // Find the stratigraphically highest (youngest) formation as datum
        var datumFormation = RestoredSection.Formations
            .OrderByDescending(f => f.TopBoundary.Select(p => p.Y).Max())
            .FirstOrDefault();

        if (datumFormation == null) return;

        // Use the mean elevation of the datum formation's top
        var datumElevation = datumFormation.TopBoundary.Select(p => p.Y).Average();

        // Process each formation
        foreach (var formation in RestoredSection.Formations)
        {
            if (!_foldingData.ContainsKey(formation)) continue;
            var data = _foldingData[formation];

            // Calculate target elevations based on stratigraphic position
            var formationThickness = CalculateFormationThickness(formation);
            var topTarget = datumElevation;
            var bottomTarget = topTarget - formationThickness;

            // Unfold boundaries using flexural slip method
            UnfoldBoundaryFlexuralSlip(formation.TopBoundary, data.OriginalTop,
                topTarget, percentage, data.FoldStyle);
            UnfoldBoundaryFlexuralSlip(formation.BottomBoundary, data.OriginalBottom,
                bottomTarget, percentage, data.FoldStyle);

            // Update datum for next formation
            datumElevation = bottomTarget;
        }
    }

    /// <summary>
    ///     Unfolds a boundary using flexural slip mechanics.
    ///     This method preserves bed length, which is the fundamental constraint
    ///     for flexural slip folding (Ramsay, 1967; Ramsay & Huber, 1987).
    /// </summary>
    private void UnfoldBoundaryFlexuralSlip(List<Vector2> boundary, List<Vector2> originalBoundary,
        float targetElevation, float percentage, FoldStyle style)
    {
        if (boundary.Count < 2) return;

        // Calculate the deformed bed length
        var deformedLength = CalculateLineLength(originalBoundary);

        // Define pin line - use the point with minimum curvature (most stable)
        var pinIndex = FindPinPoint(originalBoundary);
        var pinPoint = originalBoundary[pinIndex];
        var pinX = pinPoint.X;

        // Create flattened boundary preserving bed length
        var flattenedBoundary = new List<Vector2>(boundary.Count);

        // Calculate cumulative arc lengths from pin point
        var arcLengths = CalculateArcLengths(originalBoundary, pinIndex);

        // Generate flattened positions
        for (var i = 0; i < boundary.Count; i++)
        {
            float x, y;

            if (i < pinIndex)
                // Points before pin - extend to the left
                x = pinX - arcLengths[i];
            else if (i > pinIndex)
                // Points after pin - extend to the right
                x = pinX + arcLengths[i];
            else
                // Pin point
                x = pinX;

            y = targetElevation;
            flattenedBoundary.Add(new Vector2(x, y));
        }

        // Interpolate between current deformed state and flattened state
        for (var i = 0; i < boundary.Count; i++)
        {
            var current = boundary[i];
            var target = flattenedBoundary[i];

            var t = percentage / 100f;
            boundary[i] = Vector2.Lerp(current, target, t);
        }
    }

    /// <summary>
    ///     Finds the best pin point - the point with minimum curvature
    /// </summary>
    private int FindPinPoint(List<Vector2> boundary)
    {
        if (boundary.Count < 3) return 0;

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
        var v1 = Vector2.Normalize(p2 - p1);
        var v2 = Vector2.Normalize(p3 - p2);
        var angle = MathF.Acos(Math.Clamp(Vector2.Dot(v1, v2), -1f, 1f));
        return angle;
    }

    /// <summary>
    ///     Calculates cumulative arc lengths from a reference index
    /// </summary>
    private List<float> CalculateArcLengths(List<Vector2> boundary, int referenceIndex)
    {
        var arcLengths = new List<float>(boundary.Count);

        for (var i = 0; i < boundary.Count; i++) arcLengths.Add(0f);

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
        foreach (var formation in RestoredSection.Formations)
        {
            if (!_foldingData.ContainsKey(formation)) continue;
            var data = _foldingData[formation];

            // Apply folding based on the analyzed fold style
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

        var foldedBoundary = new List<Vector2>(boundary.Count);

        for (var i = 0; i < boundary.Count; i++)
        {
            var point = boundary[i];

            // Calculate fold displacement using sine wave
            // z(x) = A * sin(2π * x / λ + φ)
            var phase = 2f * MathF.PI * (point.X - data.AxialSurfacePosition) / data.Wavelength;
            var verticalDisplacement = data.Amplitude * MathF.Sin(phase);

            // Apply fold style modification
            if (data.FoldStyle == FoldStyle.Similar)
            {
                // Similar folds maintain constant thickness parallel to axial surface
                // Amplitude increases with distance from axial surface
                var distance = MathF.Abs(point.Y);
                verticalDisplacement *= 1f + distance / 1000f;
            }

            var foldedPoint = new Vector2(point.X, point.Y + verticalDisplacement);
            foldedBoundary.Add(foldedPoint);
        }

        // Interpolate between current flat state and folded state
        for (var i = 0; i < boundary.Count; i++)
        {
            var t = percentage / 100f;
            boundary[i] = Vector2.Lerp(boundary[i], foldedBoundary[i], t);
        }
    }

    #endregion

    #region Geometry Analysis

    private FoldStyle DetermineFoldStyle(CrossSectionGenerator.ProjectedFormation formation)
    {
        // Analyze thickness variation across the fold
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
        var variance = thicknesses.Select(t => (t - meanThickness) * (t - meanThickness)).Average();
        var coefficientOfVariation = MathF.Sqrt(variance) / meanThickness;

        // If thickness varies significantly, it's similar fold
        if (coefficientOfVariation > 0.2f)
            return FoldStyle.Similar;

        return FoldStyle.Parallel;
    }

    private float CalculateFoldWavelength(List<Vector2> boundary)
    {
        if (boundary.Count < 3) return 1000f; // Default

        // Find peaks and troughs
        var extrema = new List<int>();
        for (var i = 1; i < boundary.Count - 1; i++)
        {
            var isPeak = boundary[i].Y > boundary[i - 1].Y && boundary[i].Y > boundary[i + 1].Y;
            var isTrough = boundary[i].Y < boundary[i - 1].Y && boundary[i].Y < boundary[i + 1].Y;

            if (isPeak || isTrough)
                extrema.Add(i);
        }

        if (extrema.Count < 2)
            // No clear fold - use total length
            return boundary[^1].X - boundary[0].X;

        // Calculate average distance between extrema as wavelength
        var totalDistance = 0f;
        for (var i = 0; i < extrema.Count - 1; i++)
            totalDistance += boundary[extrema[i + 1]].X - boundary[extrema[i]].X;

        return totalDistance / (extrema.Count - 1) * 2f; // *2 for full wavelength
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
        if (boundary.Count < 3) return boundary[0].X;

        // Find point of maximum curvature as approximate axial surface
        var maxCurvature = 0f;
        var maxIndex = 0;

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
            return 100f; // Default

        // Calculate average vertical distance
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
        for (var i = 0; i < boundary.Count; i++) boundary[i] = new Vector2(boundary[i].X, elevation);
    }

    private static float CalculateLineLength(List<Vector2> points)
    {
        float length = 0;
        for (var i = 0; i < points.Count - 1; i++) length += Vector2.Distance(points[i], points[i + 1]);
        return length;
    }

    /// <summary>
    ///     Applies a vector transformation to points in the hanging wall of a fault.
    ///     Uses SIMD when available for performance.
    /// </summary>
    private void ApplyVectorToPoints(List<Vector2> points, CrossSectionGenerator.ProjectedFault fault, Vector2 vector)
    {
        if (Avx2.IsSupported)
            ApplyVectorAvx2(points, fault, vector);
        else if (AdvSimd.IsSupported)
            ApplyVectorNeon(points, fault, vector);
        else
            ApplyVectorScalar(points, fault, vector);
    }

    private void ApplyVectorScalar(List<Vector2> points, CrossSectionGenerator.ProjectedFault fault, Vector2 vector)
    {
        for (var i = 0; i < points.Count; i++)
            if (IsHangingWall(points[i], fault))
                points[i] += vector;
    }

    private unsafe void ApplyVectorAvx2(List<Vector2> points, CrossSectionGenerator.ProjectedFault fault,
        Vector2 vector)
    {
        var count = points.Count;
        var restorationVec =
            Vector256.Create(vector.X, vector.Y, vector.X, vector.Y, vector.X, vector.Y, vector.X, vector.Y);

        for (var i = 0; i <= count - 4; i += 4)
        {
            var pointsVec = Vector256.Create(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y,
                points[i + 2].X, points[i + 2].Y, points[i + 3].X, points[i + 3].Y);

            var m1 = IsHangingWall(points[i], fault) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var m2 = IsHangingWall(points[i + 1], fault) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var m3 = IsHangingWall(points[i + 2], fault) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var m4 = IsHangingWall(points[i + 3], fault) ? BitConverter.Int32BitsToSingle(-1) : 0f;
            var maskVec = Vector256.Create(m1, m1, m2, m2, m3, m3, m4, m4);

            var maskedAdd = Avx.BlendVariable(Vector256<float>.Zero, restorationVec, maskVec);
            var result = Avx.Add(pointsVec, maskedAdd);

            var resPtr = (float*)&result;
            points[i] = new Vector2(resPtr[0], resPtr[1]);
            points[i + 1] = new Vector2(resPtr[2], resPtr[3]);
            points[i + 2] = new Vector2(resPtr[4], resPtr[5]);
            points[i + 3] = new Vector2(resPtr[6], resPtr[7]);
        }

        for (var i = count - count % 4; i < count; i++)
            if (IsHangingWall(points[i], fault))
                points[i] += vector;
    }

    private void ApplyVectorNeon(List<Vector2> points, CrossSectionGenerator.ProjectedFault fault, Vector2 vector)
    {
        var count = points.Count;
        var restorationVec = Vector128.Create(vector.X, vector.Y, vector.X, vector.Y);

        for (var i = 0; i <= count - 2; i += 2)
        {
            var pointsVec = Vector128.Create(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y);

            var m1 = IsHangingWall(points[i], fault) ? 0xFFFFFFFF : 0;
            var m2 = IsHangingWall(points[i + 1], fault) ? 0xFFFFFFFF : 0;
            var mask = Vector128.Create(m1, m1, m2, m2);

            var maskedAddVec = AdvSimd.And(restorationVec, mask.AsSingle());
            var result = AdvSimd.Add(pointsVec, maskedAddVec);

            points[i] = new Vector2(result.GetElement(0), result.GetElement(1));
            points[i + 1] = new Vector2(result.GetElement(2), result.GetElement(3));
        }

        if (count % 2 != 0)
            if (IsHangingWall(points[count - 1], fault))
                points[count - 1] += vector;
    }

    private bool IsHangingWall(Vector2 point, CrossSectionGenerator.ProjectedFault fault)
    {
        if (fault.FaultTrace.Count < 2) return false;

        var minDistanceSq = float.MaxValue;
        var closestSegmentIndex = 0;

        for (var i = 0; i < fault.FaultTrace.Count - 1; i++)
        {
            var distSq = ProfileGenerator.DistanceToLineSegment(point, fault.FaultTrace[i], fault.FaultTrace[i + 1]);
            distSq *= distSq;
            if (distSq < minDistanceSq)
            {
                minDistanceSq = distSq;
                closestSegmentIndex = i;
            }
        }

        var p1 = fault.FaultTrace[closestSegmentIndex];
        var p2 = fault.FaultTrace[closestSegmentIndex + 1];

        var crossProduct = (p2.X - p1.X) * (point.Y - p1.Y) - (p2.Y - p1.Y) * (point.X - p1.X);
        var isDippingRight = p2.X > p1.X;

        return isDippingRight ? crossProduct < 0 : crossProduct > 0;
    }

    private CrossSectionGenerator.CrossSection DeepCopySection(CrossSectionGenerator.CrossSection source)
    {
        var copy = new CrossSectionGenerator.CrossSection
        {
            Profile = source.Profile,
            VerticalExaggeration = source.VerticalExaggeration,
            Formations = source.Formations.Select(f => new CrossSectionGenerator.ProjectedFormation
            {
                Name = f.Name,
                Color = f.Color,
                TopBoundary = new List<Vector2>(f.TopBoundary),
                BottomBoundary = new List<Vector2>(f.BottomBoundary)
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
        public List<Vector2> OriginalTrace { get; set; }
        public Vector2 Displacement { get; set; }
        public Vector2 HangingWallCutoff { get; set; }
        public Vector2 FootwallCutoff { get; set; }
    }

    private class FoldingData
    {
        public List<Vector2> OriginalTop { get; set; }
        public List<Vector2> OriginalBottom { get; set; }
        public FoldStyle FoldStyle { get; set; }
        public float Wavelength { get; set; }
        public float Amplitude { get; set; }
        public float AxialSurfacePosition { get; set; }
    }

    #endregion
}