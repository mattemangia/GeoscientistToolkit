// GAIA/UI/IconFactory.cs

using System.Numerics;
using SkiaSharp;
using Veldrid;

namespace GAIA.UI;

/// <summary>
///     Icons available to the toolbar and menus. Grouped by the palette family they belong to,
///     which is what gives related actions a shared look.
/// </summary>
public enum GaiaIcon
{
    // Project / file operations
    NewProject,
    OpenProject,
    SaveProject,
    SaveProjectAs,
    ImportData,
    Export,
    Exit,

    // Generic verbs
    Undo,
    Redo,
    Settings,
    Screenshot,

    // Dataset kinds / creation
    Mesh3D,
    Borehole,
    GisMap,
    TwoDGeology,
    Reactor,
    Table,
    Text,
    CtStack,
    MicroXrf,
    PointCloud,
    Image,
    Group,
    Seismic,
    AcousticVolume,
    Earthquake,
    PoreNetwork,
    Nerf,
    SlopeStability,
    Video,
    Audio,

    // Panels / layout
    PanelDatasets,
    PanelProperties,
    PanelTools,
    PanelLog,
    ResetLayout,
    FullScreen,

    // Scripting
    ScriptEditor,
    ScriptTerminal,

    // Libraries / chemistry
    MaterialLibrary,
    CompoundLibrary,
    FluidLibrary,

    // Analysis tools
    Stratigraphy,
    Triaxial,
    Photogrammetry,
    NodeManager,
    VolumeDebug,

    // Metadata
    Metadata,
    MetadataTable,

    // Help
    About,
    SystemInfo
}

/// <summary>
///     Builds GAIA's toolbar/menu icons by drawing them with Skia at load time and uploading each
///     one as a small Veldrid texture bound for ImGui.
///     Drawing them in code rather than shipping image assets keeps them crisp at any UI scale and
///     avoids an icon-font dependency.
///     Icons are created on first use and cached, so unused ones cost nothing.
/// </summary>
public sealed class IconFactory : IDisposable
{
    private const int IconSize = 32;

    private readonly Dictionary<GaiaIcon, IntPtr> _bindings = new();
    private readonly GraphicsDevice _gd;
    private readonly ImGuiController _controller;
    private readonly List<Texture> _textures = new();
    private readonly List<TextureView> _views = new();
    private readonly Dictionary<GaiaIcon, (SKColor Background, Action<SKCanvas, float, float> Draw)> _recipes;
    private bool _disposed;

    public IconFactory(GraphicsDevice gd, ImGuiController controller)
    {
        _gd = gd;
        _controller = controller;
        _recipes = BuildRecipes();
    }

    /// <summary>
    ///     Returns the ImGui texture handle for an icon, creating it on first request.
    ///     Returns <see cref="IntPtr.Zero" /> if the icon could not be built, which callers should
    ///     treat as "draw a text-only control instead".
    /// </summary>
    public IntPtr Get(GaiaIcon icon)
    {
        if (_disposed) return IntPtr.Zero;
        if (_bindings.TryGetValue(icon, out var existing)) return existing;
        if (!_recipes.TryGetValue(icon, out var recipe)) return IntPtr.Zero;

        IntPtr binding;
        try
        {
            binding = CreateTexture(recipe.Background, recipe.Draw);
        }
        catch (Exception ex)
        {
            // A texture failure must never take the UI down; fall back to a text-only control.
            Util.Logger.LogError($"Could not build icon {icon}: {ex.Message}");
            binding = IntPtr.Zero;
        }

        _bindings[icon] = binding;
        return binding;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Drop each ImGui binding before its view: the controller's resource sets reference them.
        foreach (var view in _views)
        {
            _controller.RemoveImGuiBinding(view);
            view.Dispose();
        }

        foreach (var texture in _textures) texture.Dispose();

        _views.Clear();
        _textures.Clear();
        _bindings.Clear();
    }

    private IntPtr CreateTexture(SKColor background, Action<SKCanvas, float, float> draw)
    {
        // Rgba8888 matches Veldrid's R8_G8_B8_A8_UNorm below; SKBitmap's platform default is BGRA,
        // so it is requested explicitly rather than relying on byte order.
        var info = new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            DrawBackground(canvas, background, IconSize, IconSize);
            draw(canvas, IconSize, IconSize);
            canvas.Flush();
        }

        var pixels = bitmap.Bytes;

        var texture = _gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            IconSize, IconSize, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _gd.UpdateTexture(texture, pixels, 0, 0, 0, IconSize, IconSize, 1, 0, 0);

        var view = _gd.ResourceFactory.CreateTextureView(texture);
        _textures.Add(texture);
        _views.Add(view);

        return _controller.GetOrCreateImGuiBinding(_gd.ResourceFactory, view);
    }

    /// <summary>
    ///     Rounded tile with a soft top-down gradient, giving the set a consistent modern base
    ///     instead of PRISM's flat squares.
    /// </summary>
    private static void DrawBackground(SKCanvas canvas, SKColor color, float w, float h)
    {
        var rect = new SKRoundRect(new SKRect(1, 1, w - 1, h - 1), 7f);
        var top = Lighten(color, 0.18f);
        var bottom = Darken(color, 0.12f);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, h),
                new[] { top, bottom },
                null,
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRoundRect(rect, paint);

        // Hairline highlight along the top edge to lift the tile off dark backgrounds.
        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = SKColors.White.WithAlpha(38)
        };
        canvas.DrawRoundRect(rect, edge);
    }

    private static SKColor Lighten(SKColor c, float amount) => new(
        (byte)Math.Clamp(c.Red + 255 * amount, 0, 255),
        (byte)Math.Clamp(c.Green + 255 * amount, 0, 255),
        (byte)Math.Clamp(c.Blue + 255 * amount, 0, 255));

    private static SKColor Darken(SKColor c, float amount) => new(
        (byte)Math.Clamp(c.Red - 255 * amount, 0, 255),
        (byte)Math.Clamp(c.Green - 255 * amount, 0, 255),
        (byte)Math.Clamp(c.Blue - 255 * amount, 0, 255));

    private static SKPaint Fill(SKColor color) =>
        new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke,
        StrokeWidth = width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
    };

    private static SKPaint Ink(float width = 2.2f) => Stroke(SKColors.White, width);

    private static SKPaint InkSoft(byte alpha = 150, float width = 1.5f) =>
        Stroke(SKColors.White.WithAlpha(alpha), width);

    // ── Palette families ──────────────────────────────────────────────────────
    // GAIA's own domain groupings, not PRISM's: the toolbar reads as families of related work
    // (imaging, geology, spatial, waveforms, ...). Action verbs keep semantic colours.
    private static readonly SKColor PaletteProject = new(58, 96, 138); // steel blue  - project / file
    private static readonly SKColor PaletteImaging = new(122, 68, 150); // violet      - CT / XRF / images
    private static readonly SKColor PaletteGeology = new(126, 82, 46); // earth brown - boreholes / strata
    private static readonly SKColor PaletteSpatial = new(32, 116, 108); // teal        - GIS / meshes / clouds
    private static readonly SKColor PaletteWave = new(176, 106, 38); // amber       - seismic / acoustic
    private static readonly SKColor PaletteData = new(78, 92, 112); // slate       - tables / text / metadata
    private static readonly SKColor PaletteScript = new(72, 78, 160); // indigo      - GeoScript
    private static readonly SKColor PaletteChem = new(46, 122, 88); // sea green   - reactors / libraries
    private static readonly SKColor PaletteView = new(70, 88, 130); // muted indigo- panels / layout

    private static Dictionary<GaiaIcon, (SKColor, Action<SKCanvas, float, float>)> BuildRecipes() => new()
    {
        // ── Project / file (steel blue) ──────────────────────────────────────
        [GaiaIcon.NewProject] = (SKColors.SeaGreen, DrawNewProject), // creation verb -> semantic green
        [GaiaIcon.OpenProject] = (PaletteProject, DrawFolder),
        [GaiaIcon.SaveProject] = (PaletteProject, DrawSave),
        [GaiaIcon.SaveProjectAs] = (PaletteProject, DrawSaveAs),
        [GaiaIcon.ImportData] = (PaletteProject, DrawImport),
        [GaiaIcon.Export] = (PaletteProject, DrawExport),
        [GaiaIcon.Exit] = (SKColors.IndianRed, DrawExit), // destructive-ish -> semantic red

        // ── Generic verbs ────────────────────────────────────────────────────
        [GaiaIcon.Undo] = (PaletteView, DrawUndo),
        [GaiaIcon.Redo] = (PaletteView, DrawRedo),
        [GaiaIcon.Settings] = (PaletteView, DrawGear),
        [GaiaIcon.Screenshot] = (PaletteView, DrawCamera),

        // ── Imaging (violet) ─────────────────────────────────────────────────
        [GaiaIcon.CtStack] = (PaletteImaging, DrawCtStack),
        [GaiaIcon.MicroXrf] = (PaletteImaging, DrawMicroXrf),
        [GaiaIcon.Image] = (PaletteImaging, DrawImage),
        [GaiaIcon.Video] = (PaletteImaging, DrawVideo),
        [GaiaIcon.Audio] = (PaletteWave, DrawAudio),

        // ── Geology (earth brown) ────────────────────────────────────────────
        [GaiaIcon.Borehole] = (PaletteGeology, DrawBorehole),
        [GaiaIcon.TwoDGeology] = (PaletteGeology, DrawTwoDGeology),
        [GaiaIcon.Stratigraphy] = (PaletteGeology, DrawStratigraphy),
        [GaiaIcon.SlopeStability] = (PaletteGeology, DrawSlopeStability),
        [GaiaIcon.Triaxial] = (PaletteGeology, DrawTriaxial),

        // ── Spatial (teal) ───────────────────────────────────────────────────
        [GaiaIcon.GisMap] = (PaletteSpatial, DrawGisMap),
        [GaiaIcon.Mesh3D] = (PaletteSpatial, DrawMesh3D),
        [GaiaIcon.PointCloud] = (PaletteSpatial, DrawPointCloud),
        [GaiaIcon.Nerf] = (PaletteSpatial, DrawNerf),
        [GaiaIcon.Photogrammetry] = (PaletteSpatial, DrawPhotogrammetry),
        [GaiaIcon.PoreNetwork] = (PaletteSpatial, DrawPoreNetwork),

        // ── Waveforms (amber) ────────────────────────────────────────────────
        [GaiaIcon.Seismic] = (PaletteWave, DrawSeismic),
        [GaiaIcon.AcousticVolume] = (PaletteWave, DrawAcousticVolume),
        [GaiaIcon.Earthquake] = (PaletteWave, DrawEarthquake),

        // ── Tabular / text / metadata (slate) ────────────────────────────────
        [GaiaIcon.Table] = (PaletteData, DrawTable),
        [GaiaIcon.Text] = (PaletteData, DrawText),
        [GaiaIcon.Metadata] = (PaletteData, DrawMetadata),
        [GaiaIcon.MetadataTable] = (PaletteData, DrawMetadataTable),
        [GaiaIcon.Group] = (PaletteData, DrawGroup),

        // ── Scripting (indigo) ───────────────────────────────────────────────
        [GaiaIcon.ScriptEditor] = (PaletteScript, DrawScriptEditor),
        [GaiaIcon.ScriptTerminal] = (PaletteScript, DrawScriptTerminal),
        [GaiaIcon.NodeManager] = (PaletteScript, DrawNodeManager),

        // ── Chemistry / libraries (sea green) ────────────────────────────────
        [GaiaIcon.Reactor] = (PaletteChem, DrawReactor),
        [GaiaIcon.MaterialLibrary] = (PaletteChem, DrawMaterialLibrary),
        [GaiaIcon.CompoundLibrary] = (PaletteChem, DrawCompoundLibrary),
        [GaiaIcon.FluidLibrary] = (PaletteChem, DrawFluidLibrary),

        // ── Panels / layout (muted indigo) ───────────────────────────────────
        [GaiaIcon.PanelDatasets] = (PaletteView, DrawPanelDatasets),
        [GaiaIcon.PanelProperties] = (PaletteView, DrawPanelProperties),
        [GaiaIcon.PanelTools] = (PaletteView, DrawPanelTools),
        [GaiaIcon.PanelLog] = (PaletteView, DrawPanelLog),
        [GaiaIcon.ResetLayout] = (PaletteView, DrawResetLayout),
        [GaiaIcon.FullScreen] = (PaletteView, DrawFullScreen),

        // ── Help ─────────────────────────────────────────────────────────────
        [GaiaIcon.About] = (PaletteProject, DrawAbout),
        [GaiaIcon.SystemInfo] = (PaletteProject, DrawSystemInfo),
        [GaiaIcon.VolumeDebug] = (PaletteImaging, DrawVolumeDebug)
    };

    // ── Project / file glyphs ────────────────────────────────────────────────

    private static void DrawDocument(SKCanvas c, float w, float h)
    {
        using var path = new SKPath();
        path.MoveTo(w * 0.26f, h * 0.16f);
        path.LineTo(w * 0.58f, h * 0.16f);
        path.LineTo(w * 0.74f, h * 0.34f);
        path.LineTo(w * 0.74f, h * 0.84f);
        path.LineTo(w * 0.26f, h * 0.84f);
        path.Close();
        c.DrawPath(path, Ink());

        // Folded corner.
        using var fold = new SKPath();
        fold.MoveTo(w * 0.58f, h * 0.16f);
        fold.LineTo(w * 0.58f, h * 0.34f);
        fold.LineTo(w * 0.74f, h * 0.34f);
        c.DrawPath(fold, InkSoft(190, 1.6f));
    }

    private static void DrawNewProject(SKCanvas c, float w, float h)
    {
        DrawDocument(c, w, h);
        // Plus badge, bottom-right.
        var badge = Ink(2.4f);
        c.DrawLine(w * 0.60f, h * 0.66f, w * 0.84f, h * 0.66f, badge);
        c.DrawLine(w * 0.72f, h * 0.54f, w * 0.72f, h * 0.78f, badge);
    }

    private static void DrawFolder(SKCanvas c, float w, float h)
    {
        using var path = new SKPath();
        path.MoveTo(w * 0.16f, h * 0.74f);
        path.LineTo(w * 0.16f, h * 0.28f);
        path.LineTo(w * 0.42f, h * 0.28f);
        path.LineTo(w * 0.50f, h * 0.38f);
        path.LineTo(w * 0.84f, h * 0.38f);
        path.LineTo(w * 0.84f, h * 0.74f);
        path.Close();
        c.DrawPath(path, Ink());
        c.DrawLine(w * 0.16f, h * 0.50f, w * 0.84f, h * 0.50f, InkSoft(120, 1.3f));
    }

    private static void DrawSave(SKCanvas c, float w, float h)
    {
        // Floppy outline.
        c.DrawRect(w * 0.20f, h * 0.20f, w * 0.60f, h * 0.60f, Ink());
        // Shutter.
        c.DrawRect(w * 0.34f, h * 0.20f, w * 0.32f, h * 0.20f, InkSoft(200, 1.6f));
        // Label.
        c.DrawRect(w * 0.32f, h * 0.52f, w * 0.36f, h * 0.28f, InkSoft(200, 1.6f));
    }

    private static void DrawSaveAs(SKCanvas c, float w, float h)
    {
        c.DrawRect(w * 0.16f, h * 0.20f, w * 0.52f, h * 0.52f, Ink(2f));
        c.DrawRect(w * 0.28f, h * 0.20f, w * 0.26f, h * 0.16f, InkSoft(190, 1.4f));
        // Pencil over the corner marks "as...".
        using var pencil = new SKPath();
        pencil.MoveTo(w * 0.56f, h * 0.84f);
        pencil.LineTo(w * 0.60f, h * 0.68f);
        pencil.LineTo(w * 0.86f, h * 0.42f);
        pencil.LineTo(w * 0.92f, h * 0.50f);
        pencil.LineTo(w * 0.66f, h * 0.76f);
        pencil.Close();
        c.DrawPath(pencil, Ink(1.8f));
    }

    private static void DrawImport(SKCanvas c, float w, float h)
    {
        // Tray.
        using var tray = new SKPath();
        tray.MoveTo(w * 0.20f, h * 0.60f);
        tray.LineTo(w * 0.20f, h * 0.82f);
        tray.LineTo(w * 0.80f, h * 0.82f);
        tray.LineTo(w * 0.80f, h * 0.60f);
        c.DrawPath(tray, Ink());
        // Inbound arrow.
        var arrow = Ink(2.4f);
        c.DrawLine(w * 0.50f, h * 0.16f, w * 0.50f, h * 0.58f, arrow);
        c.DrawLine(w * 0.38f, h * 0.46f, w * 0.50f, h * 0.60f, arrow);
        c.DrawLine(w * 0.62f, h * 0.46f, w * 0.50f, h * 0.60f, arrow);
    }

    private static void DrawExport(SKCanvas c, float w, float h)
    {
        using var tray = new SKPath();
        tray.MoveTo(w * 0.20f, h * 0.60f);
        tray.LineTo(w * 0.20f, h * 0.82f);
        tray.LineTo(w * 0.80f, h * 0.82f);
        tray.LineTo(w * 0.80f, h * 0.60f);
        c.DrawPath(tray, Ink());
        var arrow = Ink(2.4f);
        c.DrawLine(w * 0.50f, h * 0.16f, w * 0.50f, h * 0.58f, arrow);
        c.DrawLine(w * 0.38f, h * 0.30f, w * 0.50f, h * 0.16f, arrow);
        c.DrawLine(w * 0.62f, h * 0.30f, w * 0.50f, h * 0.16f, arrow);
    }

    private static void DrawExit(SKCanvas c, float w, float h)
    {
        // Door frame with an outbound arrow.
        using var door = new SKPath();
        door.MoveTo(w * 0.56f, h * 0.18f);
        door.LineTo(w * 0.24f, h * 0.18f);
        door.LineTo(w * 0.24f, h * 0.82f);
        door.LineTo(w * 0.56f, h * 0.82f);
        c.DrawPath(door, Ink());
        var arrow = Ink(2.4f);
        c.DrawLine(w * 0.46f, h * 0.50f, w * 0.86f, h * 0.50f, arrow);
        c.DrawLine(w * 0.72f, h * 0.36f, w * 0.86f, h * 0.50f, arrow);
        c.DrawLine(w * 0.72f, h * 0.64f, w * 0.86f, h * 0.50f, arrow);
    }

    // ── Generic verbs ────────────────────────────────────────────────────────

    private static void DrawUndoArrow(SKCanvas c, float w, float h, bool mirrored)
    {
        c.Save();
        if (mirrored)
        {
            c.Translate(w, 0);
            c.Scale(-1, 1);
        }

        // Shaft running right, then hooking down and back: the classic undo tail.
        using var shaft = new SKPath();
        shaft.MoveTo(w * 0.30f, h * 0.38f);
        shaft.LineTo(w * 0.60f, h * 0.38f);
        shaft.CubicTo(w * 0.86f, h * 0.38f, w * 0.84f, h * 0.78f, w * 0.56f, h * 0.78f);
        c.DrawPath(shaft, Ink(2.6f));

        // Arrowhead on the left tip.
        var head = Ink(2.6f);
        c.DrawLine(w * 0.30f, h * 0.38f, w * 0.44f, h * 0.26f, head);
        c.DrawLine(w * 0.30f, h * 0.38f, w * 0.44f, h * 0.50f, head);
        c.Restore();
    }

    private static void DrawUndo(SKCanvas c, float w, float h) => DrawUndoArrow(c, w, h, false);

    private static void DrawRedo(SKCanvas c, float w, float h) => DrawUndoArrow(c, w, h, true);

    private static void DrawGear(SKCanvas c, float w, float h)
    {
        float cx = w * 0.5f, cy = h * 0.5f;
        var teeth = Ink(2f);
        for (var i = 0; i < 8; i++)
        {
            var a = (float)(Math.PI / 4 * i);
            var x0 = cx + MathF.Cos(a) * w * 0.22f;
            var y0 = cy + MathF.Sin(a) * h * 0.22f;
            var x1 = cx + MathF.Cos(a) * w * 0.34f;
            var y1 = cy + MathF.Sin(a) * h * 0.34f;
            c.DrawLine(x0, y0, x1, y1, teeth);
        }

        c.DrawCircle(cx, cy, w * 0.20f, Ink(2.2f));
        c.DrawCircle(cx, cy, w * 0.08f, InkSoft(190, 1.6f));
    }

    private static void DrawCamera(SKCanvas c, float w, float h)
    {
        using var body = new SKPath();
        body.MoveTo(w * 0.14f, h * 0.34f);
        body.LineTo(w * 0.32f, h * 0.34f);
        body.LineTo(w * 0.38f, h * 0.24f);
        body.LineTo(w * 0.62f, h * 0.24f);
        body.LineTo(w * 0.68f, h * 0.34f);
        body.LineTo(w * 0.86f, h * 0.34f);
        body.LineTo(w * 0.86f, h * 0.78f);
        body.LineTo(w * 0.14f, h * 0.78f);
        body.Close();
        c.DrawPath(body, Ink(2f));
        c.DrawCircle(w * 0.5f, h * 0.56f, w * 0.14f, Ink(2f));
    }

    // ── Imaging glyphs ───────────────────────────────────────────────────────

    private static void DrawCtStack(SKCanvas c, float w, float h)
    {
        // Stacked slices seen in perspective - the CT image stack.
        for (var i = 2; i >= 0; i--)
        {
            var y = h * (0.34f + i * 0.16f);
            using var slice = new SKPath();
            slice.MoveTo(w * 0.5f, y - h * 0.10f);
            slice.LineTo(w * 0.84f, y);
            slice.LineTo(w * 0.5f, y + h * 0.10f);
            slice.LineTo(w * 0.16f, y);
            slice.Close();
            c.DrawPath(slice, i == 0 ? Ink(2f) : InkSoft(140, 1.6f));
        }
    }

    private static void DrawMicroXrf(SKCanvas c, float w, float h)
    {
        // Element spectrum: baseline with characteristic peaks.
        c.DrawLine(w * 0.14f, h * 0.78f, w * 0.86f, h * 0.78f, InkSoft(150, 1.4f));
        using var spectrum = new SKPath();
        spectrum.MoveTo(w * 0.16f, h * 0.74f);
        spectrum.LineTo(w * 0.30f, h * 0.70f);
        spectrum.LineTo(w * 0.36f, h * 0.30f);
        spectrum.LineTo(w * 0.42f, h * 0.70f);
        spectrum.LineTo(w * 0.54f, h * 0.66f);
        spectrum.LineTo(w * 0.60f, h * 0.44f);
        spectrum.LineTo(w * 0.66f, h * 0.68f);
        spectrum.LineTo(w * 0.84f, h * 0.72f);
        c.DrawPath(spectrum, Ink(2.2f));
    }

    private static void DrawImage(SKCanvas c, float w, float h)
    {
        c.DrawRect(w * 0.16f, h * 0.22f, w * 0.68f, h * 0.56f, Ink(2f));
        c.DrawCircle(w * 0.34f, h * 0.38f, w * 0.05f, Fill(SKColors.White));
        using var hills = new SKPath();
        hills.MoveTo(w * 0.20f, h * 0.74f);
        hills.LineTo(w * 0.40f, h * 0.50f);
        hills.LineTo(w * 0.54f, h * 0.66f);
        hills.LineTo(w * 0.66f, h * 0.54f);
        hills.LineTo(w * 0.80f, h * 0.74f);
        c.DrawPath(hills, Ink(2f));
    }

    private static void DrawVideo(SKCanvas c, float w, float h)
    {
        c.DrawRect(w * 0.16f, h * 0.26f, w * 0.68f, h * 0.48f, Ink(2f));
        using var play = new SKPath();
        play.MoveTo(w * 0.42f, h * 0.38f);
        play.LineTo(w * 0.64f, h * 0.50f);
        play.LineTo(w * 0.42f, h * 0.62f);
        play.Close();
        c.DrawPath(play, Fill(SKColors.White));
        // Sprocket ticks.
        for (var i = 0; i < 3; i++)
        {
            var y = h * (0.32f + i * 0.14f);
            c.DrawLine(w * 0.20f, y, w * 0.26f, y, InkSoft(150, 1.4f));
        }
    }

    private static void DrawAudio(SKCanvas c, float w, float h)
    {
        using var speaker = new SKPath();
        speaker.MoveTo(w * 0.20f, h * 0.40f);
        speaker.LineTo(w * 0.32f, h * 0.40f);
        speaker.LineTo(w * 0.46f, h * 0.26f);
        speaker.LineTo(w * 0.46f, h * 0.74f);
        speaker.LineTo(w * 0.32f, h * 0.60f);
        speaker.LineTo(w * 0.20f, h * 0.60f);
        speaker.Close();
        c.DrawPath(speaker, Ink(2f));
        for (var i = 0; i < 3; i++)
        {
            var r = w * (0.12f + i * 0.09f);
            c.DrawArc(new SKRect(w * 0.54f - r, h * 0.5f - r, w * 0.54f + r, h * 0.5f + r),
                -50, 100, false, InkSoft((byte)(220 - i * 55), 1.8f));
        }
    }

    private static void DrawVolumeDebug(SKCanvas c, float w, float h)
    {
        DrawCube(c, w, h, 0.16f, 0.22f, 0.52f);
        // Magnifier over the cube.
        c.DrawCircle(w * 0.66f, h * 0.64f, w * 0.16f, Ink(2.2f));
        c.DrawLine(w * 0.78f, h * 0.76f, w * 0.90f, h * 0.88f, Ink(2.4f));
    }

    // ── Geology glyphs ───────────────────────────────────────────────────────

    private static void DrawBorehole(SKCanvas c, float w, float h)
    {
        // Wellhead on the surface.
        c.DrawRect(w * 0.40f, h * 0.10f, w * 0.20f, h * 0.08f, Ink(2f));

        // Beds interrupted by the hole, so the casing reads as cutting through them.
        for (var i = 0; i < 3; i++)
        {
            var y = h * (0.36f + i * 0.18f);
            var alpha = (byte)(210 - i * 45);
            c.DrawLine(w * 0.10f, y, w * 0.42f, y, InkSoft(alpha, 1.7f));
            c.DrawLine(w * 0.58f, y, w * 0.90f, y, InkSoft(alpha, 1.7f));
        }

        // Casing.
        c.DrawLine(w * 0.44f, h * 0.18f, w * 0.44f, h * 0.86f, Ink(2.2f));
        c.DrawLine(w * 0.56f, h * 0.18f, w * 0.56f, h * 0.86f, Ink(2.2f));
        c.DrawLine(w * 0.44f, h * 0.86f, w * 0.56f, h * 0.86f, Ink(2.2f));
    }

    private static void DrawTwoDGeology(SKCanvas c, float w, float h)
    {
        // Folded strata in section.
        for (var i = 0; i < 3; i++)
        {
            var y = h * (0.38f + i * 0.16f);
            using var layer = new SKPath();
            layer.MoveTo(w * 0.12f, y);
            layer.CubicTo(w * 0.34f, y - h * 0.16f, w * 0.62f, y + h * 0.12f, w * 0.88f, y - h * 0.04f);
            c.DrawPath(layer, i == 0 ? Ink(2.2f) : InkSoft((byte)(190 - i * 40), 1.8f));
        }
    }

    private static void DrawStratigraphy(SKCanvas c, float w, float h)
    {
        // Two logs whose beds sit at different depths, tied by correlation lines.
        var left = new SKRect(w * 0.12f, h * 0.14f, w * 0.34f, h * 0.82f);
        var right = new SKRect(w * 0.66f, h * 0.22f, w * 0.88f, h * 0.90f);
        c.DrawRect(left, Ink(2f));
        c.DrawRect(right, Ink(2f));

        // Marker bed filled on both logs; that is what the correlation ties together.
        var bed = Fill(SKColors.White.WithAlpha(190));
        c.DrawRect(left.Left, h * 0.36f, left.Width, h * 0.07f, bed);
        c.DrawRect(right.Left, h * 0.46f, right.Width, h * 0.07f, bed);
        c.DrawLine(left.Left, h * 0.60f, left.Right, h * 0.60f, InkSoft(170, 1.5f));
        c.DrawLine(right.Left, h * 0.70f, right.Right, h * 0.70f, InkSoft(170, 1.5f));

        var tie = InkSoft(150, 1.4f);
        tie.PathEffect = SKPathEffect.CreateDash(new[] { 3f, 2f }, 0);
        c.DrawLine(left.Right, h * 0.39f, right.Left, h * 0.49f, tie);
        c.DrawLine(left.Right, h * 0.60f, right.Left, h * 0.70f, tie);
    }

    private static void DrawSlopeStability(SKCanvas c, float w, float h)
    {
        // Slope profile with a circular failure surface.
        using var slope = new SKPath();
        slope.MoveTo(w * 0.12f, h * 0.32f);
        slope.LineTo(w * 0.46f, h * 0.32f);
        slope.LineTo(w * 0.82f, h * 0.76f);
        slope.LineTo(w * 0.12f, h * 0.76f);
        slope.Close();
        c.DrawPath(slope, Ink(2f));

        using var arc = new SKPath();
        arc.MoveTo(w * 0.30f, h * 0.32f);
        arc.CubicTo(w * 0.30f, h * 0.70f, w * 0.54f, h * 0.78f, w * 0.72f, h * 0.66f);
        var dashed = InkSoft(220, 1.8f);
        dashed.PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0);
        c.DrawPath(arc, dashed);
    }

    private static void DrawTriaxial(SKCanvas c, float w, float h)
    {
        // Cylindrical specimen under axial load.
        c.DrawOval(new SKRect(w * 0.32f, h * 0.28f, w * 0.68f, h * 0.40f), Ink(2f));
        c.DrawLine(w * 0.32f, h * 0.34f, w * 0.32f, h * 0.70f, Ink(2f));
        c.DrawLine(w * 0.68f, h * 0.34f, w * 0.68f, h * 0.70f, Ink(2f));
        c.DrawArc(new SKRect(w * 0.32f, h * 0.64f, w * 0.68f, h * 0.76f), 0, 180, false, Ink(2f));
        // Axial arrows.
        var arrow = Ink(2.2f);
        c.DrawLine(w * 0.5f, h * 0.08f, w * 0.5f, h * 0.24f, arrow);
        c.DrawLine(w * 0.44f, h * 0.18f, w * 0.5f, h * 0.26f, arrow);
        c.DrawLine(w * 0.56f, h * 0.18f, w * 0.5f, h * 0.26f, arrow);
        c.DrawLine(w * 0.5f, h * 0.92f, w * 0.5f, h * 0.78f, arrow);
        c.DrawLine(w * 0.44f, h * 0.84f, w * 0.5f, h * 0.76f, arrow);
        c.DrawLine(w * 0.56f, h * 0.84f, w * 0.5f, h * 0.76f, arrow);
    }

    // ── Spatial glyphs ───────────────────────────────────────────────────────

    private static void DrawGisMap(SKCanvas c, float w, float h)
    {
        // Folded map with a pin.
        using var map = new SKPath();
        map.MoveTo(w * 0.12f, h * 0.30f);
        map.LineTo(w * 0.37f, h * 0.20f);
        map.LineTo(w * 0.63f, h * 0.32f);
        map.LineTo(w * 0.88f, h * 0.22f);
        map.LineTo(w * 0.88f, h * 0.72f);
        map.LineTo(w * 0.63f, h * 0.82f);
        map.LineTo(w * 0.37f, h * 0.70f);
        map.LineTo(w * 0.12f, h * 0.80f);
        map.Close();
        c.DrawPath(map, Ink(2f));
        c.DrawLine(w * 0.37f, h * 0.20f, w * 0.37f, h * 0.70f, InkSoft(120, 1.3f));
        c.DrawLine(w * 0.63f, h * 0.32f, w * 0.63f, h * 0.82f, InkSoft(120, 1.3f));
        c.DrawCircle(w * 0.5f, h * 0.46f, w * 0.07f, Fill(SKColors.White));
    }

    private static void DrawCube(SKCanvas c, float w, float h, float x, float y, float size)
    {
        var d = size * 0.32f;
        var front = new SKRect(w * x, h * (y + d), w * (x + size), h * (y + size + d));
        c.DrawRect(front, Ink(2f));
        using var top = new SKPath();
        top.MoveTo(front.Left, front.Top);
        top.LineTo(front.Left + w * d, h * y);
        top.LineTo(front.Right + w * d, h * y);
        top.LineTo(front.Right, front.Top);
        c.DrawPath(top, Ink(2f));
        using var side = new SKPath();
        side.MoveTo(front.Right, front.Top);
        side.LineTo(front.Right + w * d, h * y);
        side.LineTo(front.Right + w * d, front.Bottom - h * d);
        side.LineTo(front.Right, front.Bottom);
        c.DrawPath(side, Ink(2f));
    }

    private static void DrawMesh3D(SKCanvas c, float w, float h)
    {
        // Triangulated surface rather than a plain cube, to read as "mesh".
        using var outline = new SKPath();
        outline.MoveTo(w * 0.5f, h * 0.16f);
        outline.LineTo(w * 0.88f, h * 0.40f);
        outline.LineTo(w * 0.72f, h * 0.82f);
        outline.LineTo(w * 0.28f, h * 0.82f);
        outline.LineTo(w * 0.12f, h * 0.40f);
        outline.Close();
        c.DrawPath(outline, Ink(2f));

        var wire = InkSoft(160, 1.4f);
        c.DrawLine(w * 0.5f, h * 0.16f, w * 0.5f, h * 0.82f, wire);
        c.DrawLine(w * 0.12f, h * 0.40f, w * 0.72f, h * 0.82f, wire);
        c.DrawLine(w * 0.88f, h * 0.40f, w * 0.28f, h * 0.82f, wire);
        c.DrawLine(w * 0.12f, h * 0.40f, w * 0.88f, h * 0.40f, wire);
    }

    private static void DrawPointCloud(SKCanvas c, float w, float h)
    {
        // Deterministic scatter so the icon is stable across runs.
        var rng = new Random(19);
        var dot = Fill(SKColors.White);
        for (var i = 0; i < 26; i++)
        {
            var x = 0.18f + (float)rng.NextDouble() * 0.64f;
            var y = 0.20f + (float)rng.NextDouble() * 0.60f;
            // Fake depth: points lower in the tile read as nearer, so draw them larger.
            var r = 1.0f + y * 1.8f;
            c.DrawCircle(w * x, h * y, r, dot);
        }
    }

    private static void DrawNerf(SKCanvas c, float w, float h)
    {
        // Camera frustums looking at a captured object.
        c.DrawCircle(w * 0.5f, h * 0.54f, w * 0.12f, Ink(2f));
        for (var i = 0; i < 3; i++)
        {
            var a = (float)(-Math.PI / 2 + i * (2 * Math.PI / 3));
            var cx = w * 0.5f + MathF.Cos(a) * w * 0.30f;
            var cy = h * 0.54f + MathF.Sin(a) * h * 0.30f;
            using var frustum = new SKPath();
            frustum.MoveTo(cx, cy);
            frustum.LineTo(cx + MathF.Cos(a + 0.5f) * -w * 0.12f, cy + MathF.Sin(a + 0.5f) * -h * 0.12f);
            frustum.LineTo(cx + MathF.Cos(a - 0.5f) * -w * 0.12f, cy + MathF.Sin(a - 0.5f) * -h * 0.12f);
            frustum.Close();
            c.DrawPath(frustum, InkSoft(200, 1.5f));
        }
    }

    private static void DrawPhotogrammetry(SKCanvas c, float w, float h)
    {
        // Camera above a reconstructed surface.
        c.DrawRect(w * 0.34f, h * 0.14f, w * 0.32f, h * 0.20f, Ink(2f));
        c.DrawCircle(w * 0.5f, h * 0.24f, w * 0.06f, InkSoft(200, 1.5f));
        var ray = InkSoft(130, 1.3f);
        c.DrawLine(w * 0.40f, h * 0.34f, w * 0.20f, h * 0.66f, ray);
        c.DrawLine(w * 0.60f, h * 0.34f, w * 0.80f, h * 0.66f, ray);
        using var surface = new SKPath();
        surface.MoveTo(w * 0.14f, h * 0.78f);
        surface.LineTo(w * 0.36f, h * 0.62f);
        surface.LineTo(w * 0.56f, h * 0.74f);
        surface.LineTo(w * 0.86f, h * 0.58f);
        c.DrawPath(surface, Ink(2.2f));
    }

    private static void DrawPoreNetwork(SKCanvas c, float w, float h)
    {
        // Pore bodies joined by throats - the PNM dataset.
        var nodes = new[]
        {
            (0.24f, 0.28f, 4.0f), (0.62f, 0.20f, 2.8f), (0.80f, 0.50f, 3.4f),
            (0.46f, 0.52f, 4.6f), (0.22f, 0.72f, 3.0f), (0.66f, 0.80f, 3.6f)
        };
        var link = InkSoft(150, 1.5f);
        c.DrawLine(w * 0.24f, h * 0.28f, w * 0.46f, h * 0.52f, link);
        c.DrawLine(w * 0.62f, h * 0.20f, w * 0.46f, h * 0.52f, link);
        c.DrawLine(w * 0.80f, h * 0.50f, w * 0.46f, h * 0.52f, link);
        c.DrawLine(w * 0.22f, h * 0.72f, w * 0.46f, h * 0.52f, link);
        c.DrawLine(w * 0.66f, h * 0.80f, w * 0.46f, h * 0.52f, link);
        c.DrawLine(w * 0.62f, h * 0.20f, w * 0.80f, h * 0.50f, link);

        var body = Fill(SKColors.White);
        foreach (var (x, y, r) in nodes) c.DrawCircle(w * x, h * y, r, body);
    }

    // ── Waveform glyphs ──────────────────────────────────────────────────────

    private static void DrawSeismic(SKCanvas c, float w, float h)
    {
        // Several wiggle traces side by side.
        for (var t = 0; t < 3; t++)
        {
            var x = w * (0.28f + t * 0.22f);
            using var trace = new SKPath();
            trace.MoveTo(x, h * 0.14f);
            for (var i = 0; i <= 8; i++)
            {
                var y = h * (0.14f + i * 0.09f);
                var swing = MathF.Sin(i * 1.1f + t * 1.7f) * w * 0.07f;
                trace.LineTo(x + swing, y);
            }

            c.DrawPath(trace, t == 1 ? Ink(2f) : InkSoft(170, 1.6f));
        }
    }

    private static void DrawAcousticVolume(SKCanvas c, float w, float h)
    {
        DrawCube(c, w, h, 0.14f, 0.24f, 0.44f);
        // Wavefronts radiating out of the volume.
        for (var i = 0; i < 3; i++)
        {
            var r = w * (0.14f + i * 0.09f);
            c.DrawArc(new SKRect(w * 0.66f - r, h * 0.58f - r, w * 0.66f + r, h * 0.58f + r),
                -70, 140, false, InkSoft((byte)(220 - i * 55), 1.7f));
        }
    }

    private static void DrawEarthquake(SKCanvas c, float w, float h)
    {
        c.DrawLine(w * 0.10f, h * 0.5f, w * 0.28f, h * 0.5f, InkSoft(150, 1.6f));
        using var trace = new SKPath();
        trace.MoveTo(w * 0.28f, h * 0.5f);
        trace.LineTo(w * 0.38f, h * 0.42f);
        trace.LineTo(w * 0.46f, h * 0.74f);
        trace.LineTo(w * 0.54f, h * 0.18f);
        trace.LineTo(w * 0.62f, h * 0.80f);
        trace.LineTo(w * 0.70f, h * 0.40f);
        trace.LineTo(w * 0.78f, h * 0.5f);
        c.DrawPath(trace, Ink(2.4f));
        c.DrawLine(w * 0.78f, h * 0.5f, w * 0.90f, h * 0.5f, InkSoft(150, 1.6f));
    }

    // ── Tabular / text / metadata glyphs ─────────────────────────────────────

    private static void DrawTable(SKCanvas c, float w, float h)
    {
        var rect = new SKRect(w * 0.16f, h * 0.22f, w * 0.84f, h * 0.78f);
        c.DrawRect(rect, Ink(2f));
        // Header band.
        c.DrawLine(rect.Left, h * 0.36f, rect.Right, h * 0.36f, Ink(2f));
        var grid = InkSoft(130, 1.3f);
        c.DrawLine(rect.Left, h * 0.57f, rect.Right, h * 0.57f, grid);
        c.DrawLine(w * 0.39f, rect.Top, w * 0.39f, rect.Bottom, grid);
        c.DrawLine(w * 0.61f, rect.Top, w * 0.61f, rect.Bottom, grid);
    }

    private static void DrawText(SKCanvas c, float w, float h)
    {
        DrawDocument(c, w, h);
        var line = InkSoft(180, 1.5f);
        c.DrawLine(w * 0.34f, h * 0.50f, w * 0.66f, h * 0.50f, line);
        c.DrawLine(w * 0.34f, h * 0.62f, w * 0.66f, h * 0.62f, line);
        c.DrawLine(w * 0.34f, h * 0.74f, w * 0.54f, h * 0.74f, line);
    }

    private static void DrawTag(SKCanvas c, float w, float h)
    {
        using var tag = new SKPath();
        tag.MoveTo(w * 0.14f, h * 0.46f);
        tag.LineTo(w * 0.48f, h * 0.14f);
        tag.LineTo(w * 0.84f, h * 0.14f);
        tag.LineTo(w * 0.84f, h * 0.50f);
        tag.LineTo(w * 0.50f, h * 0.84f);
        tag.Close();
        c.DrawPath(tag, Ink(2f));
        c.DrawCircle(w * 0.68f, h * 0.32f, w * 0.05f, Fill(SKColors.White));
    }

    private static void DrawMetadata(SKCanvas c, float w, float h) => DrawTag(c, w, h);

    private static void DrawMetadataTable(SKCanvas c, float w, float h)
    {
        c.DrawRect(w * 0.12f, h * 0.26f, w * 0.52f, h * 0.48f, Ink(2f));
        c.DrawLine(w * 0.12f, h * 0.40f, w * 0.64f, h * 0.40f, Ink(2f));
        c.DrawLine(w * 0.38f, h * 0.26f, w * 0.38f, h * 0.74f, InkSoft(130, 1.3f));
        // Small tag badge marks it as the metadata view of a table.
        using var tag = new SKPath();
        tag.MoveTo(w * 0.56f, h * 0.62f);
        tag.LineTo(w * 0.74f, h * 0.44f);
        tag.LineTo(w * 0.92f, h * 0.62f);
        tag.LineTo(w * 0.74f, h * 0.88f);
        tag.Close();
        c.DrawPath(tag, Ink(1.8f));
    }

    private static void DrawGroup(SKCanvas c, float w, float h)
    {
        // Stacked folders.
        using var back = new SKPath();
        back.MoveTo(w * 0.24f, h * 0.28f);
        back.LineTo(w * 0.46f, h * 0.28f);
        back.LineTo(w * 0.52f, h * 0.36f);
        back.LineTo(w * 0.88f, h * 0.36f);
        back.LineTo(w * 0.88f, h * 0.64f);
        c.DrawPath(back, InkSoft(140, 1.6f));

        using var front = new SKPath();
        front.MoveTo(w * 0.12f, h * 0.76f);
        front.LineTo(w * 0.12f, h * 0.42f);
        front.LineTo(w * 0.36f, h * 0.42f);
        front.LineTo(w * 0.42f, h * 0.50f);
        front.LineTo(w * 0.76f, h * 0.50f);
        front.LineTo(w * 0.76f, h * 0.76f);
        front.Close();
        c.DrawPath(front, Ink(2f));
    }

    // ── Scripting glyphs ─────────────────────────────────────────────────────

    private static void DrawScriptEditor(SKCanvas c, float w, float h)
    {
        var ink = Ink(2.4f);
        // Angle brackets around a slash: code.
        using var left = new SKPath();
        left.MoveTo(w * 0.36f, h * 0.30f);
        left.LineTo(w * 0.18f, h * 0.50f);
        left.LineTo(w * 0.36f, h * 0.70f);
        c.DrawPath(left, ink);

        using var right = new SKPath();
        right.MoveTo(w * 0.64f, h * 0.30f);
        right.LineTo(w * 0.82f, h * 0.50f);
        right.LineTo(w * 0.64f, h * 0.70f);
        c.DrawPath(right, ink);

        c.DrawLine(w * 0.56f, h * 0.24f, w * 0.44f, h * 0.76f, InkSoft(200, 2f));
    }

    private static void DrawScriptTerminal(SKCanvas c, float w, float h)
    {
        c.DrawRect(w * 0.14f, h * 0.22f, w * 0.72f, h * 0.56f, Ink(2f));
        c.DrawLine(w * 0.14f, h * 0.34f, w * 0.86f, h * 0.34f, InkSoft(140, 1.3f));
        // Prompt chevron + caret.
        using var chevron = new SKPath();
        chevron.MoveTo(w * 0.26f, h * 0.46f);
        chevron.LineTo(w * 0.36f, h * 0.56f);
        chevron.LineTo(w * 0.26f, h * 0.66f);
        c.DrawPath(chevron, Ink(2.2f));
        c.DrawLine(w * 0.44f, h * 0.66f, w * 0.62f, h * 0.66f, Ink(2.2f));
    }

    private static void DrawNodeManager(SKCanvas c, float w, float h)
    {
        // Compute nodes wired to a hub.
        var link = InkSoft(150, 1.5f);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.22f, h * 0.24f, link);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.78f, h * 0.24f, link);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.22f, h * 0.76f, link);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.78f, h * 0.76f, link);

        c.DrawCircle(w * 0.5f, h * 0.5f, w * 0.10f, Ink(2.2f));
        var node = Fill(SKColors.White);
        c.DrawCircle(w * 0.22f, h * 0.24f, 3.2f, node);
        c.DrawCircle(w * 0.78f, h * 0.24f, 3.2f, node);
        c.DrawCircle(w * 0.22f, h * 0.76f, 3.2f, node);
        c.DrawCircle(w * 0.78f, h * 0.76f, 3.2f, node);
    }

    // ── Chemistry / library glyphs ───────────────────────────────────────────

    private static void DrawReactor(SKCanvas c, float w, float h)
    {
        // Erlenmeyer flask with reacting contents.
        using var flask = new SKPath();
        flask.MoveTo(w * 0.40f, h * 0.16f);
        flask.LineTo(w * 0.40f, h * 0.40f);
        flask.LineTo(w * 0.20f, h * 0.80f);
        flask.LineTo(w * 0.80f, h * 0.80f);
        flask.LineTo(w * 0.60f, h * 0.40f);
        flask.LineTo(w * 0.60f, h * 0.16f);
        c.DrawPath(flask, Ink(2.2f));
        c.DrawLine(w * 0.36f, h * 0.16f, w * 0.64f, h * 0.16f, Ink(2.2f));
        // Liquid level + bubbles.
        c.DrawLine(w * 0.29f, h * 0.62f, w * 0.71f, h * 0.62f, InkSoft(200, 1.6f));
        c.DrawCircle(w * 0.44f, h * 0.72f, 2.2f, Fill(SKColors.White));
        c.DrawCircle(w * 0.58f, h * 0.70f, 1.6f, Fill(SKColors.White));
    }

    private static void DrawMaterialLibrary(SKCanvas c, float w, float h)
    {
        // Swatch grid.
        float m = w * 0.18f, size = w * 0.28f;
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
        {
            var rect = SKRect.Create(m + x * size, m + y * size, size - w * 0.06f, size - h * 0.06f);
            var solid = (x + y) % 2 == 0;
            c.DrawRect(rect, solid ? Fill(SKColors.White.WithAlpha(210)) : Ink(1.8f));
        }
    }

    private static void DrawCompoundLibrary(SKCanvas c, float w, float h)
    {
        // Molecule: central atom with bonds.
        var bond = InkSoft(170, 1.6f);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.24f, h * 0.30f, bond);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.76f, h * 0.32f, bond);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.40f, h * 0.80f, bond);
        c.DrawLine(w * 0.5f, h * 0.5f, w * 0.78f, h * 0.72f, bond);

        c.DrawCircle(w * 0.5f, h * 0.5f, w * 0.11f, Fill(SKColors.White));
        var atom = Ink(1.8f);
        c.DrawCircle(w * 0.24f, h * 0.30f, w * 0.06f, atom);
        c.DrawCircle(w * 0.76f, h * 0.32f, w * 0.06f, atom);
        c.DrawCircle(w * 0.40f, h * 0.80f, w * 0.06f, atom);
        c.DrawCircle(w * 0.78f, h * 0.72f, w * 0.06f, atom);
    }

    private static void DrawFluidLibrary(SKCanvas c, float w, float h)
    {
        // Droplet with a cycle arrow: working fluid.
        using var drop = new SKPath();
        drop.MoveTo(w * 0.5f, h * 0.18f);
        drop.CubicTo(w * 0.78f, h * 0.46f, w * 0.72f, h * 0.76f, w * 0.5f, h * 0.76f);
        drop.CubicTo(w * 0.28f, h * 0.76f, w * 0.22f, h * 0.46f, w * 0.5f, h * 0.18f);
        c.DrawPath(drop, Ink(2.2f));
        c.DrawArc(new SKRect(w * 0.34f, h * 0.44f, w * 0.66f, h * 0.72f), 30, 260, false, InkSoft(200, 1.6f));
    }

    // ── Panels / layout glyphs ───────────────────────────────────────────────

    private static void DrawPanelFrame(SKCanvas c, float w, float h)
    {
        c.DrawRect(w * 0.14f, h * 0.18f, w * 0.72f, h * 0.64f, Ink(2f));
    }

    private static void DrawPanelDatasets(SKCanvas c, float w, float h)
    {
        // Database cylinder: the dataset tree.
        c.DrawOval(new SKRect(w * 0.24f, h * 0.18f, w * 0.76f, h * 0.34f), Ink(2f));
        c.DrawLine(w * 0.24f, h * 0.26f, w * 0.24f, h * 0.70f, Ink(2f));
        c.DrawLine(w * 0.76f, h * 0.26f, w * 0.76f, h * 0.70f, Ink(2f));
        c.DrawArc(new SKRect(w * 0.24f, h * 0.62f, w * 0.76f, h * 0.78f), 0, 180, false, Ink(2f));
        c.DrawArc(new SKRect(w * 0.24f, h * 0.40f, w * 0.76f, h * 0.56f), 0, 180, false, InkSoft(150, 1.5f));
    }

    private static void DrawPanelProperties(SKCanvas c, float w, float h)
    {
        // Slider rows.
        for (var i = 0; i < 3; i++)
        {
            var y = h * (0.30f + i * 0.20f);
            c.DrawLine(w * 0.16f, y, w * 0.84f, y, InkSoft(150, 1.6f));
            var knob = w * (0.32f + i * 0.20f);
            c.DrawCircle(knob, y, 3.4f, Fill(SKColors.White));
        }
    }

    private static void DrawPanelTools(SKCanvas c, float w, float h)
    {
        // Wrench.
        c.DrawLine(w * 0.34f, h * 0.66f, w * 0.74f, h * 0.26f, Ink(3f));
        c.DrawCircle(w * 0.30f, h * 0.70f, w * 0.12f, Ink(2.2f));
        using var head = new SKPath();
        head.AddArc(new SKRect(w * 0.60f, h * 0.12f, w * 0.88f, h * 0.40f), 60, 250);
        c.DrawPath(head, Ink(2.4f));
    }

    private static void DrawPanelLog(SKCanvas c, float w, float h)
    {
        DrawPanelFrame(c, w, h);
        var line = InkSoft(180, 1.5f);
        c.DrawLine(w * 0.22f, h * 0.34f, w * 0.70f, h * 0.34f, line);
        c.DrawLine(w * 0.22f, h * 0.46f, w * 0.78f, h * 0.46f, line);
        c.DrawLine(w * 0.22f, h * 0.58f, w * 0.62f, h * 0.58f, line);
        c.DrawLine(w * 0.22f, h * 0.70f, w * 0.74f, h * 0.70f, line);
    }

    private static void DrawResetLayout(SKCanvas c, float w, float h)
    {
        // The target layout in miniature: side rails plus a bottom bar.
        c.DrawRect(w * 0.12f, h * 0.18f, w * 0.76f, h * 0.64f, Ink(2f));
        c.DrawLine(w * 0.32f, h * 0.18f, w * 0.32f, h * 0.66f, InkSoft(170, 1.5f));
        c.DrawLine(w * 0.68f, h * 0.18f, w * 0.68f, h * 0.66f, InkSoft(170, 1.5f));
        c.DrawLine(w * 0.12f, h * 0.66f, w * 0.88f, h * 0.66f, InkSoft(170, 1.5f));
    }

    private static void DrawFullScreen(SKCanvas c, float w, float h)
    {
        var ink = Ink(2.4f);
        float a = w * 0.12f, lo = 0.18f, hi = 0.82f;
        // Four corner brackets pointing outward.
        c.DrawLine(w * lo, h * lo, w * lo + a, h * lo, ink);
        c.DrawLine(w * lo, h * lo, w * lo, h * lo + a, ink);
        c.DrawLine(w * hi, h * lo, w * hi - a, h * lo, ink);
        c.DrawLine(w * hi, h * lo, w * hi, h * lo + a, ink);
        c.DrawLine(w * lo, h * hi, w * lo + a, h * hi, ink);
        c.DrawLine(w * lo, h * hi, w * lo, h * hi - a, ink);
        c.DrawLine(w * hi, h * hi, w * hi - a, h * hi, ink);
        c.DrawLine(w * hi, h * hi, w * hi, h * hi - a, ink);
    }

    // ── Help glyphs ──────────────────────────────────────────────────────────

    private static void DrawAbout(SKCanvas c, float w, float h)
    {
        c.DrawCircle(w * 0.5f, h * 0.5f, w * 0.32f, Ink(2.2f));
        c.DrawCircle(w * 0.5f, h * 0.32f, 2.2f, Fill(SKColors.White));
        c.DrawLine(w * 0.5f, h * 0.44f, w * 0.5f, h * 0.70f, Ink(2.6f));
    }

    private static void DrawSystemInfo(SKCanvas c, float w, float h)
    {
        // Chip with pins.
        c.DrawRect(w * 0.28f, h * 0.28f, w * 0.44f, h * 0.44f, Ink(2.2f));
        c.DrawRect(w * 0.40f, h * 0.40f, w * 0.20f, h * 0.20f, InkSoft(190, 1.6f));
        var pin = InkSoft(170, 1.6f);
        for (var i = 0; i < 3; i++)
        {
            var o = w * (0.36f + i * 0.14f);
            c.DrawLine(o, h * 0.28f, o, h * 0.16f, pin);
            c.DrawLine(o, h * 0.72f, o, h * 0.84f, pin);
            c.DrawLine(w * 0.28f, o, w * 0.16f, o, pin);
            c.DrawLine(w * 0.72f, o, w * 0.84f, o, pin);
        }
    }
}
