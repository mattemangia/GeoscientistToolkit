// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/GeomechanicalVisualization2D.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Result field types for visualization
/// </summary>
public enum ResultField2D
{
    // Displacement
    DisplacementX,
    DisplacementY,
    DisplacementMagnitude,

    // Velocity
    VelocityX,
    VelocityY,
    VelocityMagnitude,

    // Stress components
    StressXX,
    StressYY,
    StressZZ,
    StressXY,

    // Principal stresses
    Sigma1,
    Sigma2,
    Sigma3,
    PrincipalAngle,

    // Derived stress quantities
    MeanStress,
    DeviatoricStress,
    VonMisesStress,
    MaxShearStress,
    OctahedralStress,

    // Strain
    StrainXX,
    StrainYY,
    StrainXY,
    VolumetricStrain,
    ShearStrain,

    // Plasticity and failure
    PlasticStrain,
    YieldIndex,
    DamageVariable,
    SafetyFactor,

    // Physical
    PorePressure,
    Temperature,
    Permeability,

    // Material
    MaterialId,

    // Custom
    None
}

/// <summary>
/// Color map types for result visualization
/// </summary>
public enum ColorMapType
{
    Rainbow,
    Jet,
    Viridis,
    Plasma,
    Inferno,
    Magma,
    Turbo,
    Grayscale,
    RedBlue,
    GreenRed,
    BlueWhiteRed,
    Terrain,
    Custom
}

/// <summary>
/// Vector display modes
/// </summary>
public enum VectorDisplayMode
{
    None,
    Arrows,
    Streamlines,
    PrincipalStress,
    Displacement,
    Velocity
}

/// <summary>
/// Color map for result visualization
/// </summary>
public class ColorMap
{
    public ColorMapType Type { get; set; } = ColorMapType.Jet;
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public bool AutoScale { get; set; } = true;
    public bool LogScale { get; set; } = false;
    public bool Symmetric { get; set; } = false;  // Center on zero
    public int NumContours { get; set; } = 10;
    public bool ShowContours { get; set; } = true;
    public float Opacity { get; set; } = 0.8f;

    // Custom color stops
    public List<(double position, Vector4 color)> CustomStops { get; set; } = new();

    /// <summary>
    /// Get color for normalized value (0-1)
    /// </summary>
    public Vector4 GetColor(double normalizedValue)
    {
        normalizedValue = Math.Clamp(normalizedValue, 0, 1);

        return Type switch
        {
            ColorMapType.Rainbow => GetRainbowColor(normalizedValue),
            ColorMapType.Jet => GetJetColor(normalizedValue),
            ColorMapType.Viridis => GetViridisColor(normalizedValue),
            ColorMapType.Plasma => GetPlasmaColor(normalizedValue),
            ColorMapType.Inferno => GetInfernoColor(normalizedValue),
            ColorMapType.Turbo => GetTurboColor(normalizedValue),
            ColorMapType.Grayscale => GetGrayscaleColor(normalizedValue),
            ColorMapType.RedBlue => GetRedBlueColor(normalizedValue),
            ColorMapType.BlueWhiteRed => GetBlueWhiteRedColor(normalizedValue),
            ColorMapType.Terrain => GetTerrainColor(normalizedValue),
            ColorMapType.Custom => GetCustomColor(normalizedValue),
            _ => GetJetColor(normalizedValue)
        };
    }

    /// <summary>
    /// Get color for actual value (maps to range)
    /// </summary>
    public Vector4 GetColorForValue(double value)
    {
        double range = MaxValue - MinValue;
        if (Math.Abs(range) < 1e-20) return GetColor(0.5);

        double normalized;
        if (LogScale && value > 0 && MinValue > 0)
        {
            normalized = (Math.Log10(value) - Math.Log10(MinValue)) /
                        (Math.Log10(MaxValue) - Math.Log10(MinValue));
        }
        else
        {
            normalized = (value - MinValue) / range;
        }

        return GetColor(normalized);
    }

    private Vector4 GetRainbowColor(double t)
    {
        float r = (float)Math.Abs(Math.Sin(Math.PI * t));
        float g = (float)Math.Abs(Math.Sin(Math.PI * (t + 0.33)));
        float b = (float)Math.Abs(Math.Sin(Math.PI * (t + 0.67)));
        return new Vector4(r, g, b, Opacity);
    }

    private Vector4 GetJetColor(double t)
    {
        float r, g, b;

        if (t < 0.25)
        {
            r = 0;
            g = (float)(4 * t);
            b = 1;
        }
        else if (t < 0.5)
        {
            r = 0;
            g = 1;
            b = (float)(1 - 4 * (t - 0.25));
        }
        else if (t < 0.75)
        {
            r = (float)(4 * (t - 0.5));
            g = 1;
            b = 0;
        }
        else
        {
            r = 1;
            g = (float)(1 - 4 * (t - 0.75));
            b = 0;
        }

        return new Vector4(r, g, b, Opacity);
    }

    private Vector4 GetViridisColor(double t)
    {
        // Approximate viridis colormap
        float r = (float)(0.267 + 0.004 * t + 1.264 * t * t - 0.635 * t * t * t);
        float g = (float)(0.004 + 1.003 * t - 0.208 * t * t - 0.296 * t * t * t);
        float b = (float)(0.329 + 1.442 * t - 2.129 * t * t + 0.769 * t * t * t);
        return new Vector4(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), Opacity);
    }

    private Vector4 GetPlasmaColor(double t)
    {
        float r = (float)(0.050 + 2.535 * t - 1.631 * t * t);
        float g = (float)(0.030 + 0.263 * t + 0.707 * t * t);
        float b = (float)(0.529 + 0.741 * t - 1.268 * t * t);
        return new Vector4(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), Opacity);
    }

    private Vector4 GetInfernoColor(double t)
    {
        float r = (float)(0.001 + 1.132 * t + 0.867 * t * t - 1.0 * t * t * t);
        float g = (float)(0.001 + 0.051 * t + 1.449 * t * t - 0.5 * t * t * t);
        float b = (float)(0.014 + 0.847 * t - 1.861 * t * t + 1.0 * t * t * t);
        return new Vector4(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), Opacity);
    }

    private Vector4 GetTurboColor(double t)
    {
        float r = (float)(0.18995 + 2.80 * t - 3.55 * t * t + 1.73 * t * t * t);
        float g = (float)(0.07176 + 2.97 * t - 3.01 * t * t + 0.89 * t * t * t);
        float b = (float)(0.23217 + 2.21 * t - 5.77 * t * t + 3.37 * t * t * t);
        return new Vector4(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), Opacity);
    }

    private Vector4 GetGrayscaleColor(double t)
    {
        float v = (float)t;
        return new Vector4(v, v, v, Opacity);
    }

    private Vector4 GetRedBlueColor(double t)
    {
        float r = (float)(1 - t);
        float b = (float)t;
        return new Vector4(r, 0, b, Opacity);
    }

    private Vector4 GetBlueWhiteRedColor(double t)
    {
        float r, g, b;
        if (t < 0.5)
        {
            r = (float)(2 * t);
            g = (float)(2 * t);
            b = 1;
        }
        else
        {
            r = 1;
            g = (float)(2 * (1 - t));
            b = (float)(2 * (1 - t));
        }
        return new Vector4(r, g, b, Opacity);
    }

    private Vector4 GetTerrainColor(double t)
    {
        // Blue (water) -> Green (low) -> Yellow/Brown (mid) -> White (high)
        if (t < 0.2)
        {
            float s = (float)(t / 0.2);
            return new Vector4(0, 0, 0.5f + 0.5f * s, Opacity);
        }
        else if (t < 0.4)
        {
            float s = (float)((t - 0.2) / 0.2);
            return new Vector4(s * 0.2f, 0.5f + s * 0.3f, 1 - s * 0.7f, Opacity);
        }
        else if (t < 0.7)
        {
            float s = (float)((t - 0.4) / 0.3);
            return new Vector4(0.2f + s * 0.6f, 0.8f - s * 0.3f, 0.3f - s * 0.2f, Opacity);
        }
        else
        {
            float s = (float)((t - 0.7) / 0.3);
            return new Vector4(0.8f + s * 0.2f, 0.5f + s * 0.5f, 0.1f + s * 0.9f, Opacity);
        }
    }

    private Vector4 GetCustomColor(double t)
    {
        if (CustomStops.Count == 0)
            return GetJetColor(t);

        if (CustomStops.Count == 1)
            return CustomStops[0].color;

        // Find surrounding stops
        for (int i = 0; i < CustomStops.Count - 1; i++)
        {
            if (t <= CustomStops[i + 1].position)
            {
                double localT = (t - CustomStops[i].position) /
                               (CustomStops[i + 1].position - CustomStops[i].position);
                return Vector4.Lerp(CustomStops[i].color, CustomStops[i + 1].color, (float)localT);
            }
        }

        return CustomStops[^1].color;
    }
}

/// <summary>
/// Renderer for 2D geomechanical results
/// </summary>
public class GeomechanicalRenderer2D
{
    public FEMMesh2D Mesh { get; set; }
    public SimulationResults2D Results { get; set; }
    public ColorMap ColorMap { get; } = new();

    // Display settings
    public ResultField2D DisplayField { get; set; } = ResultField2D.DisplacementMagnitude;
    public VectorDisplayMode VectorMode { get; set; } = VectorDisplayMode.None;
    public bool ShowMesh { get; set; } = true;
    public bool ShowNodes { get; set; } = false;
    public bool ShowElementCentroids { get; set; } = false;
    public bool ShowBoundaryConditions { get; set; } = true;
    public bool ShowLoads { get; set; } = true;
    public bool ShowDeformed { get; set; } = true;
    public float DeformationScale { get; set; } = 1.0f;
    public float VectorScale { get; set; } = 1.0f;
    public float NodeSize { get; set; } = 3.0f;
    public float LineWidth { get; set; } = 1.0f;

    // Mohr circle display
    public bool ShowMohrCircle { get; set; } = false;
    public int MohrCircleElementId { get; set; } = -1;

    /// <summary>
    /// Get the value array for the current display field
    /// </summary>
    public double[] GetFieldValues()
    {
        if (Results == null) return null;

        return DisplayField switch
        {
            ResultField2D.DisplacementX => Results.DisplacementX,
            ResultField2D.DisplacementY => Results.DisplacementY,
            ResultField2D.DisplacementMagnitude => Results.DisplacementMagnitude,
            ResultField2D.StressXX => Results.StressXX,
            ResultField2D.StressYY => Results.StressYY,
            ResultField2D.StressZZ => Results.StressZZ,
            ResultField2D.StressXY => Results.StressXY,
            ResultField2D.Sigma1 => Results.Sigma1,
            ResultField2D.Sigma2 => Results.Sigma2,
            ResultField2D.Sigma3 => Results.Sigma3,
            ResultField2D.PrincipalAngle => Results.PrincipalAngle,
            ResultField2D.MeanStress => Results.MeanStress,
            ResultField2D.DeviatoricStress => Results.DeviatoricStress,
            ResultField2D.VonMisesStress => Results.VonMisesStress,
            ResultField2D.MaxShearStress => Results.MaxShearStress,
            ResultField2D.StrainXX => Results.StrainXX,
            ResultField2D.StrainYY => Results.StrainYY,
            ResultField2D.StrainXY => Results.StrainXY,
            ResultField2D.VolumetricStrain => Results.VolumetricStrain,
            ResultField2D.PlasticStrain => Results.PlasticStrain,
            ResultField2D.YieldIndex => Results.YieldIndex,
            ResultField2D.DamageVariable => Results.DamageVariable,
            ResultField2D.PorePressure => Results.PorePressure,
            ResultField2D.Temperature => Results.Temperature,
            _ => null
        };
    }

    /// <summary>
    /// Update color map range from current data
    /// </summary>
    public void UpdateColorMapRange()
    {
        if (!ColorMap.AutoScale) return;

        var values = GetFieldValues();
        if (values == null || values.Length == 0) return;

        double min = values.Min();
        double max = values.Max();

        if (ColorMap.Symmetric)
        {
            double absMax = Math.Max(Math.Abs(min), Math.Abs(max));
            ColorMap.MinValue = -absMax;
            ColorMap.MaxValue = absMax;
        }
        else
        {
            ColorMap.MinValue = min;
            ColorMap.MaxValue = max;
        }
    }

    /// <summary>
    /// Render the mesh and results to ImGui draw list
    /// </summary>
    public void Render(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        if (Mesh == null) return;

        var nodes = Mesh.Nodes.ToArray();
        var values = GetFieldValues();

        // Update range
        UpdateColorMapRange();

        // Render elements
        foreach (var element in Mesh.Elements)
        {
            RenderElement(drawList, worldToScreen, element, nodes, values);
        }

        // Render mesh edges if enabled
        if (ShowMesh)
        {
            RenderMeshEdges(drawList, worldToScreen, nodes);
        }

        // Render nodes if enabled
        if (ShowNodes)
        {
            RenderNodes(drawList, worldToScreen, nodes);
        }

        // Render boundary conditions
        if (ShowBoundaryConditions)
        {
            RenderBoundaryConditions(drawList, worldToScreen, nodes);
        }

        // Render loads
        if (ShowLoads)
        {
            RenderLoads(drawList, worldToScreen, nodes);
        }

        // Render vectors
        if (VectorMode != VectorDisplayMode.None)
        {
            RenderVectors(drawList, worldToScreen, nodes);
        }
    }

    private void RenderElement(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen,
        FEMElement2D element, FEMNode2D[] nodes, double[] values)
    {
        var screenPoints = new List<Vector2>();

        foreach (int nodeId in element.NodeIds)
        {
            var pos = ShowDeformed
                ? nodes[nodeId].InitialPosition + nodes[nodeId].GetDisplacement() * DeformationScale
                : nodes[nodeId].InitialPosition;
            screenPoints.Add(worldToScreen(pos));
        }

        // Get color from element value
        Vector4 color = new(0.5f, 0.5f, 0.5f, 0.8f);
        if (values != null && element.Id < values.Length)
        {
            color = ColorMap.GetColorForValue(values[element.Id]);
        }

        uint colorU32 = ImGui.ColorConvertFloat4ToU32(color);

        // Fill polygon
        if (element.Type == ElementType2D.Triangle3 || element.Type == ElementType2D.Triangle6)
        {
            if (screenPoints.Count >= 3)
            {
                drawList.AddTriangleFilled(screenPoints[0], screenPoints[1], screenPoints[2], colorU32);
            }
        }
        else if (element.Type == ElementType2D.Quad4 || element.Type == ElementType2D.Quad8)
        {
            if (screenPoints.Count >= 4)
            {
                drawList.AddQuadFilled(screenPoints[0], screenPoints[1], screenPoints[2], screenPoints[3], colorU32);
            }
        }

        // Mark failed elements
        if (Results?.HasFailed != null && element.Id < Results.HasFailed.Length && Results.HasFailed[element.Id])
        {
            var center = screenPoints.Aggregate(Vector2.Zero, (a, b) => a + b) / screenPoints.Count;
            uint failColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1));
            drawList.AddCircleFilled(center, 4, failColor);
        }
    }

    private void RenderMeshEdges(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen, FEMNode2D[] nodes)
    {
        uint edgeColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 0.8f));

        foreach (var element in Mesh.Elements)
        {
            for (int i = 0; i < element.NodeIds.Count; i++)
            {
                int j = (i + 1) % element.NodeIds.Count;
                var n1 = nodes[element.NodeIds[i]];
                var n2 = nodes[element.NodeIds[j]];

                var p1 = ShowDeformed ? n1.InitialPosition + n1.GetDisplacement() * DeformationScale : n1.InitialPosition;
                var p2 = ShowDeformed ? n2.InitialPosition + n2.GetDisplacement() * DeformationScale : n2.InitialPosition;

                drawList.AddLine(worldToScreen(p1), worldToScreen(p2), edgeColor, LineWidth);
            }
        }
    }

    private void RenderNodes(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen, FEMNode2D[] nodes)
    {
        uint nodeColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 1f));

        foreach (var node in nodes)
        {
            var pos = ShowDeformed ? node.InitialPosition + node.GetDisplacement() * DeformationScale : node.InitialPosition;
            drawList.AddCircleFilled(worldToScreen(pos), NodeSize, nodeColor);
        }
    }

    private void RenderBoundaryConditions(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen, FEMNode2D[] nodes)
    {
        uint fixedColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.6f, 0.2f, 1f));
        uint rollerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.6f, 0.2f, 1f));

        foreach (var node in nodes)
        {
            if (!node.FixedX && !node.FixedY) continue;

            var pos = worldToScreen(node.InitialPosition);
            float size = NodeSize * 2;

            if (node.FixedX && node.FixedY)
            {
                // Fixed support - triangle
                drawList.AddTriangleFilled(
                    pos - new Vector2(size, 0),
                    pos + new Vector2(size, 0),
                    pos + new Vector2(0, size * 1.5f),
                    fixedColor);
            }
            else if (node.FixedX)
            {
                // Roller in X - circle with horizontal line
                drawList.AddCircle(pos, size, rollerColor, 12, 2);
                drawList.AddLine(pos - new Vector2(size, 0), pos + new Vector2(size, 0), rollerColor, 2);
            }
            else if (node.FixedY)
            {
                // Roller in Y - circle with vertical line
                drawList.AddCircle(pos, size, rollerColor, 12, 2);
                drawList.AddLine(pos - new Vector2(0, size), pos + new Vector2(0, size), rollerColor, 2);
            }
        }
    }

    private void RenderLoads(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen, FEMNode2D[] nodes)
    {
        uint loadColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.2f, 0.2f, 1f));

        // Find max force for scaling
        double maxForce = nodes.Max(n => Math.Sqrt(n.Fx * n.Fx + n.Fy * n.Fy));
        if (maxForce < 1e-10) return;

        foreach (var node in nodes)
        {
            double fx = node.Fx;
            double fy = node.Fy;
            double mag = Math.Sqrt(fx * fx + fy * fy);
            if (mag < maxForce * 0.01) continue;

            var pos = worldToScreen(node.InitialPosition);
            float arrowLength = 30 * (float)(mag / maxForce) * VectorScale;
            var dir = new Vector2((float)(fx / mag), (float)(fy / mag));
            var end = pos + dir * arrowLength;

            // Arrow line
            drawList.AddLine(pos, end, loadColor, 2);

            // Arrow head
            var perpDir = new Vector2(-dir.Y, dir.X);
            drawList.AddTriangleFilled(
                end,
                end - dir * 8 + perpDir * 4,
                end - dir * 8 - perpDir * 4,
                loadColor);
        }
    }

    private void RenderVectors(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen, FEMNode2D[] nodes)
    {
        if (Results == null) return;

        switch (VectorMode)
        {
            case VectorDisplayMode.Displacement:
                RenderDisplacementVectors(drawList, worldToScreen, nodes);
                break;
            case VectorDisplayMode.PrincipalStress:
                RenderPrincipalStressVectors(drawList, worldToScreen);
                break;
        }
    }

    private void RenderDisplacementVectors(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen, FEMNode2D[] nodes)
    {
        uint dispColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.8f, 1f));

        double maxDisp = Results.DisplacementMagnitude.Max();
        if (maxDisp < 1e-20) return;

        foreach (var node in nodes)
        {
            double ux = Results.DisplacementX[node.Id];
            double uy = Results.DisplacementY[node.Id];
            double mag = Math.Sqrt(ux * ux + uy * uy);
            if (mag < maxDisp * 0.01) continue;

            var pos = worldToScreen(node.InitialPosition);
            float arrowLength = 20 * (float)(mag / maxDisp) * VectorScale;
            var dir = new Vector2((float)(ux / mag), (float)(uy / mag));
            var end = pos + dir * arrowLength;

            drawList.AddLine(pos, end, dispColor, 1);

            // Small arrow head
            var perpDir = new Vector2(-dir.Y, dir.X);
            drawList.AddTriangleFilled(
                end,
                end - dir * 5 + perpDir * 2.5f,
                end - dir * 5 - perpDir * 2.5f,
                dispColor);
        }
    }

    private void RenderPrincipalStressVectors(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        uint tensionColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.2f, 0.2f, 1f));
        uint compressionColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.8f, 1f));

        double maxStress = Results.Sigma1.Select(Math.Abs).Max();
        if (maxStress < 1e-10) return;

        var nodes = Mesh.Nodes.ToArray();

        foreach (var element in Mesh.Elements)
        {
            var centroid = element.GetCentroid(nodes);
            var screenPos = worldToScreen(centroid);

            double s1 = Results.Sigma1[element.Id];
            double s2 = Results.Sigma2[element.Id];
            double angle = Results.PrincipalAngle[element.Id] * Math.PI / 180;

            // σ1 direction
            float len1 = 15 * (float)(Math.Abs(s1) / maxStress) * VectorScale;
            var dir1 = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            uint color1 = s1 > 0 ? tensionColor : compressionColor;
            drawList.AddLine(screenPos - dir1 * len1, screenPos + dir1 * len1, color1, 2);

            // σ2 direction (perpendicular)
            float len2 = 15 * (float)(Math.Abs(s2) / maxStress) * VectorScale;
            var dir2 = new Vector2(-dir1.Y, dir1.X);
            uint color2 = s2 > 0 ? tensionColor : compressionColor;
            drawList.AddLine(screenPos - dir2 * len2, screenPos + dir2 * len2, color2, 1);
        }
    }

    /// <summary>
    /// Render color bar legend
    /// </summary>
    public void RenderColorBar(ImDrawListPtr drawList, Vector2 position, Vector2 size)
    {
        int numSteps = 64;
        float stepHeight = size.Y / numSteps;

        for (int i = 0; i < numSteps; i++)
        {
            double t = 1.0 - (double)i / numSteps;
            var color = ColorMap.GetColor(t);
            uint colorU32 = ImGui.ColorConvertFloat4ToU32(color);

            var topLeft = position + new Vector2(0, i * stepHeight);
            var bottomRight = topLeft + new Vector2(size.X, stepHeight);
            drawList.AddRectFilled(topLeft, bottomRight, colorU32);
        }

        // Border
        drawList.AddRect(position, position + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)));

        // Labels
        uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1));
        drawList.AddText(position + new Vector2(size.X + 5, 0), textColor, $"{ColorMap.MaxValue:G4}");
        drawList.AddText(position + new Vector2(size.X + 5, size.Y - 15), textColor, $"{ColorMap.MinValue:G4}");
        drawList.AddText(position + new Vector2(size.X + 5, size.Y / 2 - 7), textColor,
            $"{(ColorMap.MaxValue + ColorMap.MinValue) / 2:G4}");
    }

    /// <summary>
    /// Render Mohr circle for selected element
    /// </summary>
    public void RenderMohrCircle(ImDrawListPtr drawList, Vector2 center, float radius)
    {
        if (Results == null || MohrCircleElementId < 0 || MohrCircleElementId >= Results.StressXX.Length)
            return;

        double sxx = Results.StressXX[MohrCircleElementId];
        double syy = Results.StressYY[MohrCircleElementId];
        double sxy = Results.StressXY[MohrCircleElementId];
        double s1 = Results.Sigma1[MohrCircleElementId];
        double s2 = Results.Sigma2[MohrCircleElementId];

        // Find stress range for scaling
        double maxStress = Math.Max(Math.Abs(s1), Math.Abs(s2)) * 1.2;
        if (maxStress < 1e-10) maxStress = 1;
        float scale = radius / (float)maxStress;

        uint axisColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1f));
        uint circleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.8f, 1f));
        uint pointColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.2f, 0.2f, 1f));

        // Draw axes
        drawList.AddLine(center - new Vector2(radius * 1.2f, 0), center + new Vector2(radius * 1.2f, 0), axisColor, 1);
        drawList.AddLine(center - new Vector2(0, radius * 1.2f), center + new Vector2(0, radius * 1.2f), axisColor, 1);

        // Draw Mohr circle
        float mohrRadius = (float)((s1 - s2) / 2) * scale;
        float mohrCenter = (float)((s1 + s2) / 2) * scale;
        drawList.AddCircle(center + new Vector2(mohrCenter, 0), Math.Abs(mohrRadius), circleColor, 64, 2);

        // Draw current stress state points
        var pointA = center + new Vector2((float)sxx * scale, (float)sxy * scale);
        var pointB = center + new Vector2((float)syy * scale, -(float)sxy * scale);
        drawList.AddCircleFilled(pointA, 5, pointColor);
        drawList.AddCircleFilled(pointB, 5, pointColor);

        // Draw pole
        drawList.AddLine(pointA, pointB, pointColor, 1);

        // Labels
        uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1));
        drawList.AddText(center + new Vector2((float)s1 * scale, -15), textColor, $"σ₁={s1:G3}");
        drawList.AddText(center + new Vector2((float)s2 * scale, -15), textColor, $"σ₃={s2:G3}");
        drawList.AddText(center + new Vector2(radius * 1.1f, 10), textColor, "σₙ");
        drawList.AddText(center + new Vector2(10, -radius * 1.1f), textColor, "τ");
    }
}

/// <summary>
/// Field selector for result display
/// </summary>
public static class ResultFieldInfo
{
    public static string GetDisplayName(ResultField2D field)
    {
        return field switch
        {
            ResultField2D.DisplacementX => "Displacement X",
            ResultField2D.DisplacementY => "Displacement Y",
            ResultField2D.DisplacementMagnitude => "Displacement Magnitude",
            ResultField2D.StressXX => "Stress σxx",
            ResultField2D.StressYY => "Stress σyy",
            ResultField2D.StressXY => "Stress τxy",
            ResultField2D.Sigma1 => "Principal Stress σ₁",
            ResultField2D.Sigma2 => "Principal Stress σ₂",
            ResultField2D.VonMisesStress => "Von Mises Stress",
            ResultField2D.MaxShearStress => "Max Shear Stress",
            ResultField2D.MeanStress => "Mean Stress",
            ResultField2D.PlasticStrain => "Plastic Strain",
            ResultField2D.YieldIndex => "Yield Index",
            ResultField2D.SafetyFactor => "Safety Factor",
            _ => field.ToString()
        };
    }

    public static string GetUnit(ResultField2D field)
    {
        return field switch
        {
            ResultField2D.DisplacementX or ResultField2D.DisplacementY or ResultField2D.DisplacementMagnitude => "m",
            ResultField2D.StressXX or ResultField2D.StressYY or ResultField2D.StressXY or
            ResultField2D.Sigma1 or ResultField2D.Sigma2 or ResultField2D.VonMisesStress or
            ResultField2D.MaxShearStress or ResultField2D.MeanStress or ResultField2D.PorePressure => "Pa",
            ResultField2D.Temperature => "°C",
            ResultField2D.PrincipalAngle => "°",
            _ => ""
        };
    }

    public static ResultField2D[] GetAllFields()
    {
        return Enum.GetValues<ResultField2D>();
    }

    public static ResultField2D[] GetDisplacementFields()
    {
        return new[] { ResultField2D.DisplacementX, ResultField2D.DisplacementY, ResultField2D.DisplacementMagnitude };
    }

    public static ResultField2D[] GetStressFields()
    {
        return new[] { ResultField2D.StressXX, ResultField2D.StressYY, ResultField2D.StressXY,
                       ResultField2D.Sigma1, ResultField2D.Sigma2, ResultField2D.VonMisesStress,
                       ResultField2D.MaxShearStress, ResultField2D.MeanStress };
    }

    public static ResultField2D[] GetStrainFields()
    {
        return new[] { ResultField2D.StrainXX, ResultField2D.StrainYY, ResultField2D.StrainXY,
                       ResultField2D.VolumetricStrain, ResultField2D.ShearStrain };
    }

    public static ResultField2D[] GetPlasticityFields()
    {
        return new[] { ResultField2D.PlasticStrain, ResultField2D.YieldIndex, ResultField2D.DamageVariable };
    }
}
