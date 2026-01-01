// GeoscientistToolkit/Analysis/Geomechanics/MohrCircleRenderer.cs

using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class MohrCircleRenderer : IDisposable
{
    private float _plotScale = 1.0f;
    private int _selectedCircleIndex;
    private bool _showFailureEnvelope = true;
    private bool _showPoles = true;

    public void Dispose()
    {
        // No resources to dispose
    }

    public void Draw(GeomechanicalResults results, GeomechanicalParameters parameters)
    {
        if (results?.MohrCircles == null || !results.MohrCircles.Any())
        {
            ImGui.Text("No Mohr circle data available");
            return;
        }

        // Split window: controls on left, plot on right
        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGui.BeginChild("MohrControls", new Vector2(250, 0), ImGuiChildFlags.Border);
        DrawControls(results, parameters);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("MohrPlot", new Vector2(0, 0), ImGuiChildFlags.Border);
        DrawMohrCirclePlot(results, parameters);
        ImGui.EndChild();
    }

    private void DrawControls(GeomechanicalResults results, GeomechanicalParameters parameters)
    {
        ImGui.Text("Mohr Circle Locations:");
        ImGui.Separator();

        var circleNames = results.MohrCircles.Select((c, i) =>
            $"{i}: {c.Location} {(c.HasFailed ? "⚠" : "")}").ToArray();

        ImGui.ListBox("##Circles", ref _selectedCircleIndex, circleNames, circleNames.Length, 10);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var circle = results.MohrCircles[_selectedCircleIndex];

        ImGui.Text("Stress State:");
        ImGui.Indent();
        ImGui.Text($"σ1: {circle.Sigma1:F2} MPa");
        ImGui.Text($"σ2: {circle.Sigma2:F2} MPa");
        ImGui.Text($"σ3: {circle.Sigma3:F2} MPa");
        ImGui.Text($"τmax: {circle.MaxShearStress:F2} MPa");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.Text("Position:");
        ImGui.Indent();
        ImGui.Text($"X: {circle.Position.X:F1}");
        ImGui.Text($"Y: {circle.Position.Y:F1}");
        ImGui.Text($"Z: {circle.Position.Z:F1}");
        ImGui.Unindent();

        ImGui.Spacing();

        if (circle.HasFailed)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
            ImGui.Text("⚠ FAILED");
            ImGui.PopStyleColor();
            ImGui.Text($"Failure Angle: {circle.FailureAngle:F1}°");
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 1, 0, 1));
            ImGui.Text("✓ Stable");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Display Options:");
        ImGui.Checkbox("Show Failure Envelope", ref _showFailureEnvelope);
        ImGui.Checkbox("Show Poles", ref _showPoles);
        ImGui.DragFloat("Scale", ref _plotScale, 0.1f, 0.1f, 5f);
    }

    private void DrawMohrCirclePlot(GeomechanicalResults results, GeomechanicalParameters parameters)
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();

        if (canvasSize.X < 50 || canvasSize.Y < 50)
            return;

        // Background
        drawList.AddRectFilled(canvasPos,
            new Vector2(canvasPos.X + canvasSize.X, canvasPos.Y + canvasSize.Y),
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1)));

        var circle = results.MohrCircles[_selectedCircleIndex];

        // Calculate plot bounds
        var maxStress = Math.Max(Math.Abs(circle.Sigma1), Math.Abs(circle.Sigma3)) * 1.2f;
        var maxShear = circle.MaxShearStress * 1.2f;
        if (maxStress < 1f) maxStress = 1f;
        if (maxShear < 1f) maxShear = 1f;

        // Apply scale
        maxStress *= _plotScale;
        maxShear *= _plotScale;

        var margin = 50f;
        var plotWidth = canvasSize.X - 2 * margin;
        var plotHeight = canvasSize.Y - 2 * margin;
        var originX = canvasPos.X + margin;
        var originY = canvasPos.Y + canvasSize.Y - margin;

        // Helper functions
        Vector2 ToScreen(float sigma, float tau)
        {
            var x = originX + sigma / maxStress * (plotWidth / 2f) + plotWidth / 2f;
            var y = originY - tau / maxShear * plotHeight;
            return new Vector2(x, y);
        }

        // Draw axes
        var axisColor = ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1));
        drawList.AddLine(ToScreen(-maxStress, 0), ToScreen(maxStress, 0), axisColor, 2f);
        drawList.AddLine(ToScreen(0, 0), ToScreen(0, maxShear), axisColor, 2f);

        // Draw grid
        var gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1));
        for (var i = 1; i <= 4; i++)
        {
            var stress = maxStress * i / 4;
            var shear = maxShear * i / 4;

            drawList.AddLine(ToScreen(stress, 0), ToScreen(stress, maxShear), gridColor, 1f);
            drawList.AddLine(ToScreen(-stress, 0), ToScreen(-stress, maxShear), gridColor, 1f);
            drawList.AddLine(ToScreen(-maxStress, shear), ToScreen(maxStress, shear), gridColor, 1f);
        }

        // Draw axis labels
        var white = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
        drawList.AddText(new Vector2(originX + plotWidth + 5, originY - 10), white, "σ (MPa)");
        drawList.AddText(new Vector2(originX - 40, canvasPos.Y + margin - 20), white, "τ (MPa)");

        // Draw tick marks and values
        for (var i = 0; i <= 4; i++)
        {
            var stress = maxStress * i / 4;
            var shear = maxShear * i / 4;

            if (i > 0)
            {
                var posX = ToScreen(stress, 0);
                drawList.AddText(new Vector2(posX.X - 15, posX.Y + 5), white, $"{stress:F0}");

                var negX = ToScreen(-stress, 0);
                drawList.AddText(new Vector2(negX.X - 20, negX.Y + 5), white, $"{-stress:F0}");
            }

            var posY = ToScreen(0, shear);
            drawList.AddText(new Vector2(posY.X - 35, posY.Y - 10), white, $"{shear:F0}");
        }

        // Draw Mohr circles (3 circles for 3D stress state)
        var circleColor = ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 1));

        // Major circle (σ1, σ3)
        var center13 = ToScreen((circle.Sigma1 + circle.Sigma3) / 2, 0);
        var radius13 = (circle.Sigma1 - circle.Sigma3) / 2;
        var radiusPixels13 = ToScreen(circle.Sigma1, 0).X - center13.X;
        DrawCircle(drawList, center13, radiusPixels13, circleColor, 2f);

        // Intermediate circles
        if (Math.Abs(circle.Sigma2 - circle.Sigma1) > 0.1f)
        {
            var center12 = ToScreen((circle.Sigma1 + circle.Sigma2) / 2, 0);
            var radius12 = Math.Abs(circle.Sigma1 - circle.Sigma2) / 2;
            var radiusPixels12 = ToScreen(circle.Sigma1, 0).X - center12.X;
            DrawCircle(drawList, center12, radiusPixels12,
                ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 1f, 0.5f)), 1f);
        }

        if (Math.Abs(circle.Sigma3 - circle.Sigma2) > 0.1f)
        {
            var center23 = ToScreen((circle.Sigma2 + circle.Sigma3) / 2, 0);
            var radius23 = Math.Abs(circle.Sigma2 - circle.Sigma3) / 2;
            var radiusPixels23 = ToScreen(circle.Sigma2, 0).X - center23.X;
            DrawCircle(drawList, center23, radiusPixels23,
                ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 1f, 0.5f)), 1f);
        }

        // Draw principal stress points
        var pointColor = ImGui.GetColorU32(new Vector4(1, 1, 0, 1));
        drawList.AddCircleFilled(ToScreen(circle.Sigma1, 0), 5f, pointColor);
        drawList.AddCircleFilled(ToScreen(circle.Sigma3, 0), 5f, pointColor);

        // Labels for principal stresses
        drawList.AddText(ToScreen(circle.Sigma1, 0) + new Vector2(8, -5), white, "σ1");
        drawList.AddText(ToScreen(circle.Sigma3, 0) + new Vector2(8, -5), white, "σ3");

        // Draw failure envelope
        if (_showFailureEnvelope) DrawFailureEnvelope(drawList, parameters, ToScreen, maxStress, maxShear);

        // Draw poles
        if (_showPoles) DrawPoles(drawList, circle, ToScreen, white);

        // Draw failure plane if failed
        if (circle.HasFailed)
        {
            DrawFailurePlane(drawList, circle, ToScreen, canvasPos, canvasSize);
            // Draw tangent line at failure point on circle
            DrawFailureTangent(drawList, circle, parameters, ToScreen, maxStress, maxShear);
        }
    }

    private void DrawCircle(ImDrawListPtr drawList, Vector2 center, float radius, uint color, float thickness)
    {
        if (radius <= 0) return;
        const int segments = 64;
        drawList.AddCircle(center, radius, color, segments, thickness);
    }

    private void DrawFailureEnvelope(ImDrawListPtr drawList, GeomechanicalParameters parameters,
        Func<float, float, Vector2> toScreen, float maxStress, float maxShear)
    {
        var envelopeColor = ImGui.GetColorU32(new Vector4(1, 0.3f, 0.3f, 1));

        switch (parameters.FailureCriterion)
        {
            case FailureCriterion.MohrCoulomb:
                DrawMohrCoulombEnvelope(drawList, parameters, toScreen, maxStress, maxShear, envelopeColor);
                break;

            case FailureCriterion.DruckerPrager:
                DrawDruckerPragerEnvelope(drawList, parameters, toScreen, maxStress, maxShear, envelopeColor);
                break;

            case FailureCriterion.HoekBrown:
                DrawHoekBrownEnvelope(drawList, parameters, toScreen, maxStress, maxShear, envelopeColor);
                break;

            case FailureCriterion.Griffith:
                DrawGriffithEnvelope(drawList, parameters, toScreen, maxStress, maxShear, envelopeColor);
                break;
        }
    }

    private void DrawMohrCoulombEnvelope(ImDrawListPtr drawList, GeomechanicalParameters parameters,
        Func<float, float, Vector2> toScreen, float maxStress, float maxShear, uint color)
    {
        var c = parameters.Cohesion;
        var phi = parameters.FrictionAngle * MathF.PI / 180f;

        // τ = c + σ·tan(φ)
        var points = new List<Vector2>();
        for (var sigma = -maxStress; sigma <= maxStress; sigma += maxStress / 50)
        {
            var tau = c + sigma * MathF.Tan(phi);
            if (tau >= 0 && tau <= maxShear)
                points.Add(toScreen(sigma, tau));
        }

        for (var i = 0; i < points.Count - 1; i++)
            drawList.AddLine(points[i], points[i + 1], color, 2f);

        // Add label
        var labelPos = toScreen(maxStress * 0.7f, c + maxStress * 0.7f * MathF.Tan(phi));
        drawList.AddText(labelPos + new Vector2(5, -20), color,
            $"τ = {c:F1} + σ·tan({parameters.FrictionAngle:F0}°)");
    }

    private void DrawDruckerPragerEnvelope(ImDrawListPtr drawList, GeomechanicalParameters parameters,
        Func<float, float, Vector2> toScreen, float maxStress, float maxShear, uint color)
    {
        var c = parameters.Cohesion;
        var phi = parameters.FrictionAngle * MathF.PI / 180f;

        // Approximate Drucker-Prager as cone
        var alpha = 2 * MathF.Sin(phi) / (3 - MathF.Sin(phi));
        var k = 6 * c * MathF.Cos(phi) / (3 - MathF.Sin(phi));

        var points = new List<Vector2>();
        for (var sigma = -maxStress; sigma <= maxStress; sigma += maxStress / 50)
        {
            var tau = k + alpha * sigma;
            if (tau >= 0 && tau <= maxShear)
                points.Add(toScreen(sigma, tau));
        }

        for (var i = 0; i < points.Count - 1; i++)
            drawList.AddLine(points[i], points[i + 1], color, 2f);
    }

    private void DrawHoekBrownEnvelope(ImDrawListPtr drawList, GeomechanicalParameters parameters,
        Func<float, float, Vector2> toScreen, float maxStress, float maxShear, uint color)
    {
        var c = parameters.Cohesion;
        var phi = parameters.FrictionAngle * MathF.PI / 180f;
        var ucs = 2 * c * MathF.Cos(phi) / (1 - MathF.Sin(phi));
        var mb = parameters.HoekBrown_mb;
        var s = parameters.HoekBrown_s;
        var a = parameters.HoekBrown_a;

        var points = new List<Vector2>();
        for (float sigma3 = 0; sigma3 <= maxStress; sigma3 += maxStress / 100)
        {
            var sigma1 = sigma3 + ucs * MathF.Pow(mb * sigma3 / ucs + s, a);
            // Convert principal stresses to (σn, τ) on the failure plane
            var normal_stress = (sigma1 + sigma3) / 2 - (sigma1 - sigma3) / 2 *
                (sigma1 - sigma3 - ucs * mb * MathF.Pow(mb * sigma3 / ucs + s, a - 1)) / (2 *
                    MathF.Sqrt(ucs * (sigma1 - sigma3) * mb * MathF.Pow(mb * sigma3 / ucs + s, a - 1)));
            var shear_stress = MathF.Sqrt(MathF.Pow((sigma1 - sigma3) / 2, 2) -
                                          MathF.Pow(normal_stress - (sigma1 + sigma3) / 2, 2));

            if (shear_stress <= maxShear)
                points.Add(toScreen(normal_stress, shear_stress));
        }

        for (var i = 0; i < points.Count - 1; i++)
            drawList.AddLine(points[i], points[i + 1], color, 2f);
    }


    private void DrawGriffithEnvelope(ImDrawListPtr drawList, GeomechanicalParameters parameters,
        Func<float, float, Vector2> toScreen, float maxStress, float maxShear, uint color)
    {
        var T0 = parameters.TensileStrength;

        // Griffith parabola: τ² = 4*T0*(σ + T0)
        var points = new List<Vector2>();
        for (var sigma = -T0; sigma <= maxStress; sigma += maxStress / 50)
        {
            var tau = MathF.Sqrt(Math.Max(0, 4 * T0 * (sigma + T0)));
            if (tau <= maxShear)
                points.Add(toScreen(sigma, tau));
        }

        for (var i = 0; i < points.Count - 1; i++)
            drawList.AddLine(points[i], points[i + 1], color, 2f);
    }

    private void DrawPoles(ImDrawListPtr drawList, MohrCircleData circle,
        Func<float, float, Vector2> toScreen, uint color)
    {
        // Pole for failure plane (if failed)
        if (circle.HasFailed)
        {
            var polePos = toScreen(circle.NormalStressAtFailure, circle.ShearStressAtFailure);
            drawList.AddCircleFilled(polePos, 4f, ImGui.GetColorU32(new Vector4(1, 0, 0, 1)));
            drawList.AddText(polePos + new Vector2(8, -5), color, "Pole");
        }
    }

    private void DrawFailureTangent(ImDrawListPtr drawList, MohrCircleData circle,
        GeomechanicalParameters parameters, Func<float, float, Vector2> toScreen, float maxStress, float maxShear)
    {
        // Calculate the point where the failure envelope is tangent to the Mohr circle
        // For Mohr-Coulomb: The tangent point is at angle θ = 45° + φ/2 from σ1 direction
        float phi_rad = parameters.FrictionAngle * MathF.PI / 180f;
        float c = parameters.Cohesion;

        // Circle center and radius
        float center_sigma = (circle.Sigma1 + circle.Sigma3) / 2f;
        float radius = (circle.Sigma1 - circle.Sigma3) / 2f;

        if (radius <= 0) return;

        // The tangent point on the circle where the Mohr-Coulomb envelope touches
        // At failure, the angle from center to tangent point is (90° - φ)
        // So the point is: σ_n = center - radius * sin(φ), τ = radius * cos(φ)
        float sigma_tangent = center_sigma - radius * MathF.Sin(phi_rad);
        float tau_tangent = radius * MathF.Cos(phi_rad);

        // Draw the tangent point (larger, more visible)
        var tangentPos = toScreen(sigma_tangent, tau_tangent);
        var failurePointColor = ImGui.GetColorU32(new Vector4(1f, 0.3f, 0f, 1f)); // Orange
        drawList.AddCircleFilled(tangentPos, 7f, failurePointColor);

        // Draw a ring around the tangent point for emphasis
        drawList.AddCircle(tangentPos, 10f, ImGui.GetColorU32(new Vector4(1f, 1f, 0f, 1f)), 16, 2f);

        // Label the failure point
        var white = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
        drawList.AddText(tangentPos + new Vector2(12, -8), white, "Failure Point");
        drawList.AddText(tangentPos + new Vector2(12, 4), white, $"σ={sigma_tangent:F1}, τ={tau_tangent:F1}");

        // Draw the tangent line (the failure envelope line passing through this point)
        // The tangent line has slope tan(φ) and passes through (sigma_tangent, tau_tangent)
        // Line: τ = τ_tangent + tan(φ) * (σ - σ_tangent)
        // Which simplifies to: τ = c + σ * tan(φ) (the Mohr-Coulomb envelope)

        var tangentLineColor = ImGui.GetColorU32(new Vector4(1f, 0.5f, 0f, 1f)); // Orange line

        // Calculate line endpoints extending beyond the tangent point
        float lineExtent = maxStress * 0.5f;
        float sigma_start = sigma_tangent - lineExtent;
        float sigma_end = sigma_tangent + lineExtent;

        float tau_start = tau_tangent + MathF.Tan(phi_rad) * (sigma_start - sigma_tangent);
        float tau_end = tau_tangent + MathF.Tan(phi_rad) * (sigma_end - sigma_tangent);

        // Clip to visible range
        if (tau_start >= 0 && tau_start <= maxShear && tau_end >= 0 && tau_end <= maxShear)
        {
            var lineStart = toScreen(sigma_start, tau_start);
            var lineEnd = toScreen(sigma_end, tau_end);
            drawList.AddLine(lineStart, lineEnd, tangentLineColor, 2.5f);
        }

        // Draw a line from circle center to tangent point (radius at failure)
        var centerPos = toScreen(center_sigma, 0);
        drawList.AddLine(centerPos, tangentPos, ImGui.GetColorU32(new Vector4(0.5f, 0.8f, 1f, 0.7f)), 1.5f);

        // Draw the 2θ angle arc (angle from σ axis to the radius at failure)
        // θ = 45° + φ/2, so 2θ = 90° + φ
        float twoTheta = MathF.PI / 2f + phi_rad;
        var arcColor = ImGui.GetColorU32(new Vector4(0.7f, 1f, 0.7f, 0.8f));
        float arcRadius = 25f;

        // Draw arc from 0 to 2θ
        const int arcSegments = 16;
        for (int i = 0; i < arcSegments; i++)
        {
            float a1 = twoTheta * i / arcSegments;
            float a2 = twoTheta * (i + 1) / arcSegments;

            // Arc in screen space (note: screen Y is inverted)
            var p1 = centerPos + new Vector2(arcRadius * MathF.Cos(a1), -arcRadius * MathF.Sin(a1));
            var p2 = centerPos + new Vector2(arcRadius * MathF.Cos(a2), -arcRadius * MathF.Sin(a2));
            drawList.AddLine(p1, p2, arcColor, 1.5f);
        }

        // Label the angle
        var angleLabel = centerPos + new Vector2(arcRadius + 5, -arcRadius / 2);
        drawList.AddText(angleLabel, arcColor, $"2θ = {(twoTheta * 180f / MathF.PI):F0}°");
    }

    private void DrawFailurePlane(ImDrawListPtr drawList, MohrCircleData circle,
        Func<float, float, Vector2> toScreen, Vector2 canvasPos, Vector2 canvasSize)
    {
        // Draw small diagram showing failure plane orientation
        var diagSize = 100f;
        var diagPos = new Vector2(canvasPos.X + canvasSize.X - diagSize - 10, canvasPos.Y + 10);

        // Background
        drawList.AddRectFilled(diagPos, diagPos + new Vector2(diagSize, diagSize),
            ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.9f)));

        var center = diagPos + new Vector2(diagSize / 2, diagSize / 2);
        var radius = diagSize * 0.4f;

        // Draw reference circle
        drawList.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1)), 32, 2f);

        // Draw σ1 direction
        var sigma1Dir = new Vector2(0, -1);
        drawList.AddLine(center, center + sigma1Dir * radius,
            ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), 2f);
        drawList.AddText(center + sigma1Dir * (radius + 15),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), "σ1");

        // Draw failure plane
        var angle = circle.FailureAngle * MathF.PI / 180f;
        var normal = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
        var tangent = new Vector2(normal.Y, -normal.X);

        var planeColor = ImGui.GetColorU32(new Vector4(1, 0, 0, 1));
        drawList.AddLine(center - tangent * radius, center + tangent * radius, planeColor, 3f);

        // Draw normal to failure plane
        drawList.AddLine(center, center + normal * radius * 0.7f,
            ImGui.GetColorU32(new Vector4(1, 0.5f, 0, 1)), 2f);

        // Label
        drawList.AddText(diagPos + new Vector2(5, diagSize - 20),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)),
            $"β = {circle.FailureAngle:F1}°");
    }
}