// GeoscientistToolkit/UI/Utils/SvgExporter.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.UI.Utils;

/// <summary>
///     Exports 2D geological cross-sections to SVG (Scalable Vector Graphics) format.
///     SVG is ideal for publication-quality figures and further editing in Inkscape/Illustrator.
/// </summary>
public class SvgExporter
{
    private readonly int _imageHeight;
    private readonly int _imageWidth;
    private readonly float _margin = 80f;
    private readonly StringBuilder _svg;

    public SvgExporter(int width = 1920, int height = 1080)
    {
        _imageWidth = width;
        _imageHeight = height;
        _svg = new StringBuilder();
    }

    public string ExportToSvg(GeologicalMapping.CrossSectionGenerator.CrossSection section,
        bool includeLabels = true, bool includeGrid = true, bool includeLegend = true)
    {
        _svg.Clear();

        // SVG header
        _svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        _svg.AppendLine($"<svg width=\"{_imageWidth}\" height=\"{_imageHeight}\" " +
                        $"viewBox=\"0 0 {_imageWidth} {_imageHeight}\" " +
                        "xmlns=\"http://www.w3.org/2000/svg\" " +
                        "xmlns:xlink=\"http://www.w3.org/1999/xlink\">");

        // Add metadata
        _svg.AppendLine("  <metadata>");
        _svg.AppendLine("    <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        _svg.AppendLine("      <rdf:Description>");
        _svg.AppendLine("        <dc:title xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
        _svg.AppendLine("          Geological Cross-Section");
        _svg.AppendLine("        </dc:title>");
        _svg.AppendLine("        <dc:date xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
        _svg.AppendLine($"          {DateTime.Now:yyyy-MM-dd}");
        _svg.AppendLine("        </dc:date>");
        _svg.AppendLine("        <dc:creator xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
        _svg.AppendLine("          Geoscientist Toolkit");
        _svg.AppendLine("        </dc:creator>");
        _svg.AppendLine("      </rdf:Description>");
        _svg.AppendLine("    </rdf:RDF>");
        _svg.AppendLine("  </metadata>");

        // Define styles
        AddStyles();

        // Create layers for different elements
        _svg.AppendLine("  <!-- Cross-Section Content -->");
        _svg.AppendLine("  <g id=\"cross-section\">");

        // White background
        _svg.AppendLine($"    <rect width=\"{_imageWidth}\" height=\"{_imageHeight}\" fill=\"white\"/>");

        // Calculate drawing parameters
        var profile = section.Profile;
        var drawWidth = _imageWidth - _margin * 2;
        var drawHeight = _imageHeight - _margin * 2 - (includeLabels ? 80 : 0);
        var distRange = profile.TotalDistance;
        var elevRange = profile.MaxElevation - profile.MinElevation;
        if (elevRange < 1f) elevRange = 1f;
        var ve = section.VerticalExaggeration;

        Vector2 WorldToSvg(Vector2 worldPos)
        {
            var x = worldPos.X / distRange * drawWidth + _margin;
            var y = _imageHeight - _margin - (includeLabels ? 80 : 0) -
                    (worldPos.Y - profile.MinElevation) / elevRange * drawHeight * ve;
            return new Vector2(x, y);
        }

        // Draw grid if requested
        if (includeGrid) DrawGrid(distRange, elevRange, profile.MinElevation, WorldToSvg);

        // Draw formations (with gradient fills)
        _svg.AppendLine("    <g id=\"formations\">");
        foreach (var formation in section.Formations)
        {
            if (formation.TopBoundary.Count < 2) continue;
            DrawFormation(formation, WorldToSvg);
        }

        _svg.AppendLine("    </g>");

        // Draw faults
        _svg.AppendLine("    <g id=\"faults\">");
        foreach (var fault in section.Faults) DrawFault(fault, WorldToSvg);
        _svg.AppendLine("    </g>");

        // Draw topography
        _svg.AppendLine("    <g id=\"topography\">");
        DrawTopography(profile, WorldToSvg);
        _svg.AppendLine("    </g>");

        // Draw axes
        DrawAxes(distRange, elevRange, profile, WorldToSvg);

        // Draw labels
        if (includeLabels) DrawLabels(section, WorldToSvg);

        // Draw legend
        if (includeLegend) DrawLegend(section);

        _svg.AppendLine("  </g>");
        _svg.AppendLine("</svg>");

        return _svg.ToString();
    }

    private void AddStyles()
    {
        _svg.AppendLine("  <defs>");
        _svg.AppendLine("    <style type=\"text/css\">");
        _svg.AppendLine("      <![CDATA[");
        _svg.AppendLine("        .formation { stroke: #000000; stroke-width: 1.5; }");
        _svg.AppendLine("        .fault { stroke: #cc0000; stroke-width: 2.5; fill: none; stroke-linecap: round; }");
        _svg.AppendLine("        .fault-normal { stroke-dasharray: none; }");
        _svg.AppendLine("        .fault-thrust { stroke-dasharray: none; }");
        _svg.AppendLine("        .fault-reverse { stroke-dasharray: none; }");
        _svg.AppendLine(
            "        .topography { stroke: #000000; stroke-width: 3; fill: none; stroke-linejoin: round; }");
        _svg.AppendLine("        .grid-line { stroke: #cccccc; stroke-width: 0.5; }");
        _svg.AppendLine("        .axis { stroke: #000000; stroke-width: 2; }");
        _svg.AppendLine("        .axis-label { font-family: Arial, sans-serif; font-size: 14px; fill: #000000; }");
        _svg.AppendLine(
            "        .title { font-family: Arial, sans-serif; font-size: 24px; font-weight: bold; fill: #000000; }");
        _svg.AppendLine("        .formation-label { font-family: Arial, sans-serif; font-size: 12px; fill: #000000; }");
        _svg.AppendLine("      ]]>");
        _svg.AppendLine("    </style>");

        // Define patterns for lithologies
        AddLithologyPatterns();

        _svg.AppendLine("  </defs>");
    }

    private void AddLithologyPatterns()
    {
        // Sandstone pattern (dots)
        _svg.AppendLine("    <pattern id=\"sandstone\" width=\"10\" height=\"10\" patternUnits=\"userSpaceOnUse\">");
        _svg.AppendLine("      <circle cx=\"2\" cy=\"2\" r=\"1\" fill=\"#8B7355\"/>");
        _svg.AppendLine("      <circle cx=\"7\" cy=\"7\" r=\"1\" fill=\"#8B7355\"/>");
        _svg.AppendLine("      <circle cx=\"5\" cy=\"9\" r=\"1\" fill=\"#8B7355\"/>");
        _svg.AppendLine("    </pattern>");

        // Limestone pattern (horizontal lines)
        _svg.AppendLine("    <pattern id=\"limestone\" width=\"10\" height=\"10\" patternUnits=\"userSpaceOnUse\">");
        _svg.AppendLine("      <line x1=\"0\" y1=\"3\" x2=\"10\" y2=\"3\" stroke=\"#4682B4\" stroke-width=\"0.5\"/>");
        _svg.AppendLine("      <line x1=\"0\" y1=\"7\" x2=\"10\" y2=\"7\" stroke=\"#4682B4\" stroke-width=\"0.5\"/>");
        _svg.AppendLine("    </pattern>");

        // Shale pattern (diagonal lines)
        _svg.AppendLine("    <pattern id=\"shale\" width=\"10\" height=\"10\" patternUnits=\"userSpaceOnUse\">");
        _svg.AppendLine("      <line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"10\" stroke=\"#696969\" stroke-width=\"0.5\"/>");
        _svg.AppendLine("    </pattern>");
    }

    private void DrawFormation(GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation,
        Func<Vector2, Vector2> transform)
    {
        var polygon = new List<Vector2>(formation.TopBoundary);
        polygon.AddRange(formation.BottomBoundary.AsEnumerable().Reverse());

        var points = string.Join(" ", polygon.Select(p =>
        {
            var svgPoint = transform(p);
            return $"{svgPoint.X:F2},{svgPoint.Y:F2}";
        }));

        var fillColor = ColorToHex(formation.Color);

        _svg.AppendLine($"      <polygon points=\"{points}\" " +
                        $"fill=\"{fillColor}\" " +
                        $"fill-opacity=\"0.7\" " +
                        $"class=\"formation\" " +
                        $"id=\"formation-{EscapeId(formation.Name)}\">");
        _svg.AppendLine($"        <title>{formation.Name}</title>");
        _svg.AppendLine("      </polygon>");
    }

    private void DrawFault(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault,
        Func<Vector2, Vector2> transform)
    {
        if (fault.FaultTrace.Count < 2) return;

        var points = string.Join(" ", fault.FaultTrace.Select(p =>
        {
            var svgPoint = transform(p);
            return $"{svgPoint.X:F2},{svgPoint.Y:F2}";
        }));

        var faultClass = fault.Type switch
        {
            GeologicalMapping.GeologicalFeatureType.Fault_Normal => "fault fault-normal",
            GeologicalMapping.GeologicalFeatureType.Fault_Thrust => "fault fault-thrust",
            GeologicalMapping.GeologicalFeatureType.Fault_Reverse => "fault fault-reverse",
            _ => "fault"
        };

        _svg.AppendLine($"      <polyline points=\"{points}\" class=\"{faultClass}\" " +
                        $"id=\"fault-{fault.Type}\">");
        _svg.AppendLine($"        <title>{fault.Type} (Dip: {fault.Dip:F1}°)</title>");
        _svg.AppendLine("      </polyline>");

        // Add fault symbols
        DrawFaultSymbols(fault, transform);
    }

    private void DrawFaultSymbols(GeologicalMapping.CrossSectionGenerator.ProjectedFault fault,
        Func<Vector2, Vector2> transform)
    {
        if (fault.FaultTrace.Count < 2) return;

        // Add tick marks for normal faults
        if (fault.Type == GeologicalMapping.GeologicalFeatureType.Fault_Normal)
            for (var i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                var p1 = transform(fault.FaultTrace[i]);
                var p2 = transform(fault.FaultTrace[i + 1]);

                var mid = (p1 + p2) / 2;
                var dir = Vector2.Normalize(p2 - p1);
                var normal = new Vector2(-dir.Y, dir.X);
                var tickEnd = mid + normal * 10;

                _svg.AppendLine($"      <line x1=\"{mid.X:F2}\" y1=\"{mid.Y:F2}\" " +
                                $"x2=\"{tickEnd.X:F2}\" y2=\"{tickEnd.Y:F2}\" " +
                                $"stroke=\"#cc0000\" stroke-width=\"2\"/>");
                _svg.AppendLine($"      <circle cx=\"{tickEnd.X:F2}\" cy=\"{tickEnd.Y:F2}\" " +
                                $"r=\"4\" fill=\"#cc0000\"/>");
            }
        // Add triangles for thrust faults
        else if (fault.Type == GeologicalMapping.GeologicalFeatureType.Fault_Thrust)
            for (var i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                var p1 = transform(fault.FaultTrace[i]);
                var p2 = transform(fault.FaultTrace[i + 1]);

                var mid = (p1 + p2) / 2;
                var dir = Vector2.Normalize(p2 - p1);
                var normal = new Vector2(-dir.Y, dir.X);

                var tip = mid + normal * 12;
                var base1 = mid + dir * 6;
                var base2 = mid - dir * 6;

                _svg.AppendLine(
                    $"      <polygon points=\"{tip.X:F2},{tip.Y:F2} {base1.X:F2},{base1.Y:F2} {base2.X:F2},{base2.Y:F2}\" " +
                    $"fill=\"#cc0000\" stroke=\"#cc0000\" stroke-width=\"1\"/>");
            }
    }

    private void DrawTopography(GeologicalMapping.ProfileGenerator.TopographicProfile profile,
        Func<Vector2, Vector2> transform)
    {
        var points = string.Join(" ", profile.Points.Select(p =>
        {
            var worldPos = new Vector2(p.Distance, p.Elevation);
            var svgPoint = transform(worldPos);
            return $"{svgPoint.X:F2},{svgPoint.Y:F2}";
        }));

        _svg.AppendLine($"      <polyline points=\"{points}\" class=\"topography\"/>");
    }

    private void DrawGrid(float distRange, float elevRange, float minElevation,
        Func<Vector2, Vector2> transform)
    {
        _svg.AppendLine("    <g id=\"grid\" opacity=\"0.3\">");

        // Vertical grid lines (every 1000m horizontally)
        var vLineSpacing = 1000;
        for (float x = 0; x <= distRange; x += vLineSpacing)
        {
            var top = transform(new Vector2(x, minElevation + elevRange));
            var bottom = transform(new Vector2(x, minElevation));
            _svg.AppendLine($"      <line x1=\"{top.X:F2}\" y1=\"{top.Y:F2}\" " +
                            $"x2=\"{bottom.X:F2}\" y2=\"{bottom.Y:F2}\" class=\"grid-line\"/>");
        }

        // Horizontal grid lines (every 500m vertically)
        var hLineSpacing = 500;
        for (var y = minElevation; y <= minElevation + elevRange; y += hLineSpacing)
        {
            var left = transform(new Vector2(0, y));
            var right = transform(new Vector2(distRange, y));
            _svg.AppendLine($"      <line x1=\"{left.X:F2}\" y1=\"{left.Y:F2}\" " +
                            $"x2=\"{right.X:F2}\" y2=\"{right.Y:F2}\" class=\"grid-line\"/>");
        }

        _svg.AppendLine("    </g>");
    }

    private void DrawAxes(float distRange, float elevRange,
        GeologicalMapping.ProfileGenerator.TopographicProfile profile,
        Func<Vector2, Vector2> transform)
    {
        _svg.AppendLine("    <g id=\"axes\">");

        var origin = transform(new Vector2(0, profile.MinElevation));
        var xAxisEnd = transform(new Vector2(distRange, profile.MinElevation));
        var yAxisEnd = transform(new Vector2(0, profile.MaxElevation));

        // X axis
        _svg.AppendLine($"      <line x1=\"{origin.X:F2}\" y1=\"{origin.Y:F2}\" " +
                        $"x2=\"{xAxisEnd.X:F2}\" y2=\"{xAxisEnd.Y:F2}\" class=\"axis\"/>");

        // Y axis
        _svg.AppendLine($"      <line x1=\"{origin.X:F2}\" y1=\"{origin.Y:F2}\" " +
                        $"x2=\"{yAxisEnd.X:F2}\" y2=\"{yAxisEnd.Y:F2}\" class=\"axis\"/>");

        // Axis labels
        var xLabelPos = new Vector2((origin.X + xAxisEnd.X) / 2, origin.Y + 30);
        _svg.AppendLine($"      <text x=\"{xLabelPos.X:F2}\" y=\"{xLabelPos.Y:F2}\" " +
                        $"text-anchor=\"middle\" class=\"axis-label\">Distance (m)</text>");

        var yLabelPos = new Vector2(origin.X - 40, (origin.Y + yAxisEnd.Y) / 2);
        _svg.AppendLine($"      <text x=\"{yLabelPos.X:F2}\" y=\"{yLabelPos.Y:F2}\" " +
                        $"text-anchor=\"middle\" transform=\"rotate(-90 {yLabelPos.X:F2} {yLabelPos.Y:F2})\" " +
                        $"class=\"axis-label\">Elevation (m)</text>");

        _svg.AppendLine("    </g>");
    }

    private void DrawLabels(GeologicalMapping.CrossSectionGenerator.CrossSection section,
        Func<Vector2, Vector2> transform)
    {
        _svg.AppendLine("    <g id=\"labels\">");

        // Title
        _svg.AppendLine($"      <text x=\"{_imageWidth / 2}\" y=\"30\" " +
                        $"text-anchor=\"middle\" class=\"title\">Geological Cross-Section</text>");

        // Formation labels
        foreach (var formation in section.Formations)
        {
            if (formation.TopBoundary.Count < 3) continue;

            // Calculate centroid
            var allPoints = formation.TopBoundary.Concat(formation.BottomBoundary).ToList();
            var centerWorld = new Vector2(
                allPoints.Average(p => p.X),
                allPoints.Average(p => p.Y));
            var centerSvg = transform(centerWorld);

            _svg.AppendLine($"      <text x=\"{centerSvg.X:F2}\" y=\"{centerSvg.Y:F2}\" " +
                            $"text-anchor=\"middle\" class=\"formation-label\">{formation.Name}</text>");
        }

        // Scale bar
        DrawScaleBar(section.Profile, transform);

        _svg.AppendLine("    </g>");
    }

    private void DrawScaleBar(GeologicalMapping.ProfileGenerator.TopographicProfile profile,
        Func<Vector2, Vector2> transform)
    {
        var scaleLength = 1000f; // 1km
        var scaleStart = transform(new Vector2(profile.TotalDistance * 0.1f, profile.MinElevation - 100));
        var scaleEnd = transform(new Vector2(profile.TotalDistance * 0.1f + scaleLength, profile.MinElevation - 100));

        _svg.AppendLine($"      <line x1=\"{scaleStart.X:F2}\" y1=\"{scaleStart.Y:F2}\" " +
                        $"x2=\"{scaleEnd.X:F2}\" y2=\"{scaleEnd.Y:F2}\" " +
                        $"stroke=\"#000000\" stroke-width=\"3\"/>");

        // Tick marks
        _svg.AppendLine($"      <line x1=\"{scaleStart.X:F2}\" y1=\"{scaleStart.Y - 5:F2}\" " +
                        $"x2=\"{scaleStart.X:F2}\" y2=\"{scaleStart.Y + 5:F2}\" " +
                        $"stroke=\"#000000\" stroke-width=\"2\"/>");
        _svg.AppendLine($"      <line x1=\"{scaleEnd.X:F2}\" y1=\"{scaleEnd.Y - 5:F2}\" " +
                        $"x2=\"{scaleEnd.X:F2}\" y2=\"{scaleEnd.Y + 5:F2}\" " +
                        $"stroke=\"#000000\" stroke-width=\"2\"/>");

        var scaleMid = (scaleStart + scaleEnd) / 2;
        _svg.AppendLine($"      <text x=\"{scaleMid.X:F2}\" y=\"{scaleMid.Y + 20:F2}\" " +
                        $"text-anchor=\"middle\" class=\"axis-label\">{scaleLength:F0} m</text>");
    }

    private void DrawLegend(GeologicalMapping.CrossSectionGenerator.CrossSection section)
    {
        _svg.AppendLine("    <g id=\"legend\">");

        float legendX = _imageWidth - 200;
        float legendY = 100;
        float boxSize = 20;
        float spacing = 30;

        _svg.AppendLine($"      <rect x=\"{legendX - 10}\" y=\"{legendY - 25}\" " +
                        $"width=\"190\" height=\"{section.Formations.Count * spacing + 40}\" " +
                        $"fill=\"white\" stroke=\"black\" stroke-width=\"1\"/>");

        _svg.AppendLine($"      <text x=\"{legendX + 80}\" y=\"{legendY - 5}\" " +
                        $"text-anchor=\"middle\" class=\"axis-label\" font-weight=\"bold\">Legend</text>");

        var index = 0;
        foreach (var formation in section.Formations)
        {
            var y = legendY + index * spacing;
            var color = ColorToHex(formation.Color);

            _svg.AppendLine($"      <rect x=\"{legendX}\" y=\"{y}\" width=\"{boxSize}\" height=\"{boxSize}\" " +
                            $"fill=\"{color}\" stroke=\"black\" stroke-width=\"1\"/>");
            _svg.AppendLine($"      <text x=\"{legendX + boxSize + 5}\" y=\"{y + 15}\" " +
                            $"class=\"formation-label\">{formation.Name}</text>");

            index++;
        }

        _svg.AppendLine("    </g>");
    }

    private string ColorToHex(Vector4 color)
    {
        var r = (int)(color.X * 255);
        var g = (int)(color.Y * 255);
        var b = (int)(color.Z * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private string EscapeId(string text)
    {
        return text.Replace(" ", "_").Replace("(", "").Replace(")", "");
    }

    public void SaveToFile(string filePath, GeologicalMapping.CrossSectionGenerator.CrossSection section,
        bool includeLabels = true, bool includeGrid = true, bool includeLegend = true)
    {
        var svgContent = ExportToSvg(section, includeLabels, includeGrid, includeLegend);
        File.WriteAllText(filePath, svgContent);
        Logger.Log($"Exported cross-section to SVG: {filePath}");
    }
}