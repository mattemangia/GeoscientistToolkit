using System.Numerics;
using System.Buffers;
using System.Collections.Concurrent;
using GAIA.Business;
using GAIA.Data.VolumeData;
using GAIA.UI.Interfaces;
using GAIA.UI.Utils;
using GAIA.Util;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using StbImageWriteSharp;

namespace GAIA.Data.CtImageStack;

public class ClippingPlane
{
    public ClippingPlane(string name)
    {
        Name = name;
        Normal = -Vector3.UnitZ;
    }
    public string Name { get; set; }
    public Vector3 Normal { get; set; }
    public float Distance { get; set; } = 0.5f;
    public bool Enabled { get; set; } = true;
    public bool Mirror { get; set; }
    public Vector3 Rotation { get; set; }
    public bool IsVisualizationVisible { get; set; } = true;
}

/// <summary>OpenTK/OpenGL high-resolution micro-CT volume renderer.</summary>
public sealed class CtVolume3DViewer : IDatasetViewer, IDisposable
{
    internal const int MAX_CLIPPING_PLANES = 8;
    private readonly StreamingCtVolumeDataset _streamingDataset;
    internal readonly CtImageStackDataset _editableDataset;
    private readonly CtVolume3DControlPanel _controlPanel;
    private readonly ImGuiExportFileDialog _screenshotDialog;
    private readonly Dictionary<byte, float> _materialOpacity = new();
    private readonly Dictionary<byte, bool> _materialVisibility = new();
    private int _program, _vao, _vbo, _ebo, _fbo, _colorTexture, _depthBuffer;
    private int _labelTexture, _previewTexture, _materialTexture;
    private int _labelTextureWidth, _labelTextureHeight, _labelTextureDepth;
    private Task<(int w, int h, int d, byte[] data)> _labelBuildTask;
    private int _maxTexture3DSize = 2048;
    private SparseCtVolume _sparse;
    private Task<List<(int z, byte[] data)>> _labelPatchTask;
    private byte[] _labelCacheData;
    private readonly ConcurrentDictionary<int, byte> _dirtyLabelSlices = new();
    private volatile bool _incrementalLabelsPending;
    private ChunkedLabelVolume _observedLabelVolume;
    private volatile float _labelBuildProgress;
    private volatile string _labelBuildStatus;
    private volatile bool _labelCacheInvalidated;
    private int _observedVirtualLabelRevision;
    private int _lineProgram, _lineVao, _lineVbo, _lineBufferBytes;
    private readonly int[] _sliceTextures = new int[3];
    private readonly int[] _sliceTextureIndex = { -1, -1, -1 };
    private float _maxLineWidth = 1f;
    private int _renderWidth = 1280, _renderHeight = 720;
    private Vector3 _cameraTarget;
    private float _cameraYaw = -MathF.PI / 4f, _cameraPitch = MathF.PI / 6f, _cameraDistance = 2f;
    private Vector2 _lastMouse;
    private bool _dragging, _panning, _disposed, _previewDirty, _labelsDirty, _materialsDirty;
    private Matrix4x4 _view, _projection;
    private CtPreviewVolume _previewMask;
    private Task<(int version, byte[] data)> _previewBuildTask;
    private int _previewVersion;
    internal Vector4 _previewColor = new(1, 0, 0, 0.5f);
    internal bool _showPreview;

    public Vector3 VolumeScale { get; private set; } = Vector3.One;
    public int ColorMapIndex;
    public bool CutXEnabled, CutYEnabled, CutZEnabled;
    public bool CutXForward = true, CutYForward = true, CutZForward = true;
    public float CutXPosition = 0.5f, CutYPosition = 0.5f, CutZPosition = 0.5f;
    public float MinThreshold = 0.05f, MaxThreshold = 1f, StepSize = 0.5f, VolumeOpacity = 1f;
    public bool ShowGrayscale = true, ShowSlices = true;
    public Vector3 SlicePositions = new(0.5f);
    public bool ShowCutXPlaneVisual { get; set; } = true;
    public bool ShowCutYPlaneVisual { get; set; } = true;
    public bool ShowCutZPlaneVisual { get; set; } = true;
    public bool ShowPlaneVisualizations { get; set; } = true;

    /// <summary>
    ///     Textures the plane faces with the slice read from the full-resolution volume. The
    ///     ray-marched body samples a downsampled LOD, so hairline features like cracks only
    ///     survive on these planes.
    /// </summary>
    public bool ShowSliceOverlay { get; set; } = true;
    public bool ShowBoundingBox { get; set; } = true;
    public bool ShowBoundingBoxLabels { get; set; } = true;
    public List<ClippingPlane> ClippingPlanes { get; } = new();

    public CtVolume3DViewer(StreamingCtVolumeDataset dataset, Action<float, string> loadingProgress = null)
    {
        if (!OpenTkManager.IsInitialized)
            throw new InvalidOperationException("The CT 3D viewer requires the OpenTK renderer.");
        _streamingDataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _editableDataset = dataset.EditablePartner ?? throw new InvalidOperationException("Missing editable CT partner.");
        _observedVirtualLabelRevision = _editableDataset.VirtualLabelRevision;
        dataset.Load();
        _editableDataset.Load();
        VolumeScale = CalculateNormalizedPhysicalScale(_editableDataset.Width, _editableDataset.Height,
            _editableDataset.Depth, _editableDataset.PixelSize, _editableDataset.SliceThickness);
        foreach (var material in _editableDataset.Materials)
        {
            _materialOpacity[material.ID] = 1f;
            _materialVisibility[material.ID] = material.IsVisible;
        }
        _controlPanel = new CtVolume3DControlPanel(this, _editableDataset);
        _screenshotDialog = new ImGuiExportFileDialog("ScreenshotDialog3D", "Save Screenshot");
        _screenshotDialog.SetExtensions((".png", "PNG Image"));
        CreateResources(loadingProgress);
        ResetCamera();
        ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
        CtImageStackTools.Preview3DChanged += OnPreviewChanged;
        _observedLabelVolume = _editableDataset.LabelData;
        if (_observedLabelVolume != null) _observedLabelVolume.SliceChanged += OnLabelSliceChanged;
    }

    public void DrawToolbarControls() { }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        var available = ImGui.GetContentRegionAvail();
        if (available.X < 2 || available.Y < 2) return;
        var desiredW = Math.Clamp((int)available.X, 320, 1920);
        var desiredH = Math.Clamp((int)available.Y, 240, 1080);
        if (desiredW != _renderWidth || desiredH != _renderHeight) ResizeTarget(desiredW, desiredH);
        Render();
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Image((IntPtr)_colorTexture, available, new Vector2(0, 1), new Vector2(1, 0));
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##CtVolume3DViewport", available,
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
        var viewportHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var cutHandled = GAIA.Analysis.VolumeCut.VolumeCutIntegration.HandleViewportInput(_editableDataset,
            ImGui.GetIO().MousePos, origin, available, _view * _projection, _view, VolumeScale,
            viewportHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left),
            ImGui.IsMouseDragging(ImGuiMouseButton.Left),
            ImGui.IsMouseReleased(ImGuiMouseButton.Left));
        HandleInput(viewportHovered || _dragging || _panning, cutHandled);
        DrawOverlayLabels(origin, available);
        GAIA.Analysis.VolumeCut.VolumeCutIntegration.DrawViewport(_editableDataset, ImGui.GetWindowDrawList(),
            _view * _projection, origin, available, VolumeScale);
        if (_labelBuildTask is { IsCompleted: false })
        {
            var savedCursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(origin + new Vector2(16, 16));
            ImGui.BeginGroup();
            ImGui.TextUnformatted(_labelBuildStatus ?? "Loading material labels...");
            ImGui.ProgressBar(_labelBuildProgress, new Vector2(Math.Min(360, available.X - 32), 18),
                $"{_labelBuildProgress * 100:0}%");
            ImGui.EndGroup();
            ImGui.SetCursorScreenPos(savedCursor);
        }
    }

    private void CreateResources(Action<float, string> progress)
    {
        progress?.Invoke(0.03f, "Compiling volume shaders...");
        GL.GetInteger(GetPName.Max3DTextureSize, out _maxTexture3DSize);
        if (_maxTexture3DSize <= 0) _maxTexture3DSize = 2048;
        _program = CreateProgram(VertexShader, FragmentShader);
        _lineProgram = CreateProgram(LineVertexShader, LineFragmentShader);
        float[] vertices =
        {
            0,0,0, 1,0,0, 1,1,0, 0,1,0, 0,0,1, 1,0,1, 1,1,1, 0,1,1
        };
        uint[] indices =
        {
            0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,2,3,7,2,7,6,0,4,7,0,7,3,1,2,6,1,6,5
        };
        _vao = GL.GenVertexArray(); _vbo = GL.GenBuffer(); _ebo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo); GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * 4, vertices, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo); GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * 4, indices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
        CreateLineResources();
        progress?.Invoke(0.12f, "Preparing the sparse brick volume...");
        // Bricked, out-of-core density: resident GPU memory is bounded by the atlas budget, not by
        // the dataset size, so multi-GB scans open on modest cards without OOM. Give the atlas half
        // the configured texture pool.
        var memoryLimitMb = GAIA.Settings.SettingsManager.Instance.Settings.Hardware.TextureMemoryLimit;
        var atlasBudget = Math.Clamp(memoryLimitMb * 1024L * 1024L / 2, 64L * 1024 * 1024, 4096L * 1024 * 1024);
        _sparse = new SparseCtVolume(_streamingDataset, atlasBudget, _maxTexture3DSize);
        progress?.Invoke(0.45f, "Calculating automatic density threshold...");
        MinThreshold = Math.Max(MinThreshold, _sparse.SuggestedThreshold01 * 0.8f);
        // Label/preview textures stay sized to the finest LOD dimensions, capped to their own budget.
        var finest = _streamingDataset.LodInfos[0];
        progress?.Invoke(0.61f, "Allocating label texture...");
        var (labelWidth, labelHeight, labelDepth) = CalculateLabelTextureDimensions(
            finest.Width, finest.Height, finest.Depth, 32L * 1024 * 1024);
        var emptyLabels = new byte[labelWidth * labelHeight * labelDepth];
        // Label IDs are categorical: linear filtering would blend id 1 and id 2 into a
        // non-existent id 2 at every boundary, so these two sample nearest.
        _labelTexture = CreateTexture3D(labelWidth, labelHeight, labelDepth, emptyLabels, false);
        _labelTextureWidth = labelWidth; _labelTextureHeight = labelHeight; _labelTextureDepth = labelDepth;
        _previewTexture = CreateTexture3D(labelWidth, labelHeight, labelDepth, new byte[emptyLabels.Length], false);
        // Never scan a potentially multi-terabyte label MMF while constructing the viewer.
        // Existing labels are streamed into this texture by a below-normal background worker.
        _labelsDirty = _editableDataset.Materials.Any(material => material.ID != 0);
        progress?.Invoke(0.90f, "Uploading material palette...");
        _materialTexture = CreateMaterialTexture();
        UploadMaterials();
        progress?.Invoke(0.96f, "Creating render target...");
        ResizeTarget(_renderWidth, _renderHeight);
        progress?.Invoke(1f, "3D volume resources ready");
    }

    private void CreateLineResources()
    {
        _lineVao = GL.GenVertexArray(); _lineVbo = GL.GenBuffer();
        GL.BindVertexArray(_lineVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _lineVbo);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, 12);
        // Core profiles are only required to support a width of 1, so ask before asking for more.
        var range = new float[2];
        GL.GetFloat(GetPName.AliasedLineWidthRange, range);
        _maxLineWidth = range[1] > 0 ? range[1] : 1f;
    }

    private const float FieldOfView = MathF.PI / 4f;

    private void Render()
    {
        _sparse?.Update(_view * _projection, CameraPosition, VolumeScale, MinThreshold, _renderHeight, FieldOfView);
        ProcessPendingLabelRefresh();
        ProcessPreviewRefresh();
        if (_materialsDirty) { UploadMaterials(); _materialsDirty = false; }
        UpdateSlicePlaneTextures();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.Viewport(0, 0, _renderWidth, _renderHeight);
        // Rasterize the box's back faces: front faces vanish as soon as the camera dollies inside
        // the volume, which blanked the whole render at close zoom. The ray marches from
        // max(entry, 0) toward the exit either way, so the image is identical from outside.
        GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace); GL.CullFace(CullFaceMode.Front);
        GL.ClearColor(0.015f, 0.018f, 0.025f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.UseProgram(_program);
        SetMatrix("uView", _view); SetMatrix("uProjection", _projection);
        Set3("uScale", VolumeScale); Set3("uCamera", CameraPosition);
        var finestLod = _streamingDataset.LodInfos[0];
        Set3("uVolumeSize", new Vector3(finestLod.Width, finestLod.Height, finestLod.Depth));
        Set1("uMin", MinThreshold); Set1("uMax", MaxThreshold); Set1("uStep", StepSize); Set1("uOpacity", VolumeOpacity);
        Set1("uShowGray", ShowGrayscale ? 1 : 0); Set1("uColorMap", ColorMapIndex);
        Set4("uCutX", new Vector4(CutXEnabled ? 1 : 0, CutXForward ? 1 : -1, CutXPosition, 0));
        Set4("uCutY", new Vector4(CutYEnabled ? 1 : 0, CutYForward ? 1 : -1, CutYPosition, 0));
        Set4("uCutZ", new Vector4(CutZEnabled ? 1 : 0, CutZForward ? 1 : -1, CutZPosition, 0));
        Set1("uPlaneCount", Math.Min(MAX_CLIPPING_PLANES, ClippingPlanes.Count(p => p.Enabled)));
        var pi = 0;
        foreach (var p in ClippingPlanes.Where(p => p.Enabled).Take(MAX_CLIPPING_PLANES))
        {
            // The side is carried separately: folding Mirror into the sign of the distance made a
            // negative distance flip the kept half as a side effect.
            Set4($"uPlanes[{pi}]", new Vector4(Vector3.Normalize(p.Normal), p.Distance));
            Set1($"uPlaneMirror[{pi}]", p.Mirror ? 1 : 0);
            pi++;
        }
        var planePos = Vector3.Zero; var planeOn = Vector3.Zero;
        for (var axis = 0; axis < 3; axis++)
        {
            var pos = ResolvePlanePosition(axis);
            if (pos == null || _sliceTextures[axis] == 0) continue;
            SetComponent(ref planePos, axis, pos.Value);
            SetComponent(ref planeOn, axis, 1f);
        }
        Set3("uSlicePlanePos", planePos); Set3("uSlicePlaneOn", planeOn);
        Set1("uShowPreview", _showPreview ? 1 : 0); Set4("uPreviewColor", _previewColor);
        var thresholdPreview = CtImageStackTools.Get3DThresholdPreviewState();
        Set1("uShowThresholdPreview", thresholdPreview.IsActive ? 1 : 0);
        GL.Uniform2(GL.GetUniformLocation(_program, "uThresholdRange"),
            thresholdPreview.Min / 255f, thresholdPreview.Max / 255f);
        Set4("uThresholdColor", thresholdPreview.Color);
        var virtualRules = _editableDataset.VirtualThresholdRules;
        var virtualRuleCount = Math.Min(32, virtualRules.Count);
        Set1("uVirtualRuleCount", virtualRuleCount);
        for (var ruleIndex = 0; ruleIndex < virtualRuleCount; ruleIndex++)
        {
            var rule = virtualRules[ruleIndex];
            Set4($"uVirtualRules[{ruleIndex}]", new Vector4(rule.Min / 255f, rule.Max / 255f,
                rule.MaterialId / 255f, rule.Add ? 1f : 0f));
        }
        // Sparse density path: bind the brick atlas / page table / base fallback and set the
        // frame-dependent LOD scale so the shader selects the same per-brick level the CPU
        // refinement streamed in.
        _sparse?.SetUniformsAndBind(_program, 8, 9, 10);
        Set1("uLodBias", _sparse?.LodBias(_renderHeight, FieldOfView) ?? 1f);
        Bind3D(1, _labelTexture, "uLabels"); Bind3D(2, _previewTexture, "uPreview");
        GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D, _materialTexture); Set1("uMaterials", 3);
        for (var axis = 0; axis < 3; axis++)
        {
            GL.ActiveTexture(TextureUnit.Texture4 + axis);
            GL.BindTexture(TextureTarget.Texture2D, _sliceTextures[axis]);
            Set1(axis switch { 0 => "uSliceX", 1 => "uSliceY", _ => "uSliceZ" }, 4 + axis);
        }
        // The shader already composites front-to-back into acc, so it writes the frame directly;
        // leaving ImGui's blend enabled here would attenuate it a second time.
        GL.Disable(EnableCap.Blend);
        GL.BindVertexArray(_vao); GL.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, 0);
        RenderOverlayLines();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Box, plane borders and the slice cross, drawn over the volume so they stay legible.</summary>
    private void RenderOverlayLines()
    {
        var verts = new List<float>();
        BuildOverlayLines(verts);
        if (verts.Count == 0) return;
        GL.UseProgram(_lineProgram);
        GL.UniformMatrix4(GL.GetUniformLocation(_lineProgram, "uView"), 1, false, ToArray(_view));
        GL.UniformMatrix4(GL.GetUniformLocation(_lineProgram, "uProjection"), 1, false, ToArray(_projection));
        GL.BindVertexArray(_lineVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _lineVbo);
        var bytes = verts.Count * sizeof(float);
        if (bytes > _lineBufferBytes)
        {
            _lineBufferBytes = (int)(bytes * 1.5f);
            GL.BufferData(BufferTarget.ArrayBuffer, _lineBufferBytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, bytes, verts.ToArray());
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.LineWidth(Math.Min(1.5f, _maxLineWidth));
        GL.DrawArrays(PrimitiveType.Lines, 0, verts.Count / 7);
        GL.LineWidth(1f);
        GL.Enable(EnableCap.DepthTest);
    }

    private void BuildOverlayLines(List<float> v)
    {
        if (ShowBoundingBox) AddBox(v, Vector3.Zero, VolumeScale, CtViewPalette.BoundingBox, 0.85f);
        GAIA.Analysis.VolumeCut.VolumeCutIntegration.BuildOverlayLines(_editableDataset, VolumeScale,
            (a, b, color, alpha) => AddLine(v, a, b, color, alpha));
        if (!ShowPlaneVisualizations) return;

        for (var axis = 0; axis < 3; axis++)
        {
            if (CutEnabled(axis) && CutVisual(axis))
                AddAxisRect(v, axis, CutPosition(axis), CtViewPalette.Cut(axis), 0.95f);
            if (ShowSlices)
                AddAxisRect(v, axis, Component(SlicePositions, axis), CtViewPalette.Crosshair(axis), 0.9f);
        }

        if (ShowSlices)
        {
            // The three-way intersection: one line per direction, each in that axis' crosshair colour.
            var c = SlicePositions * VolumeScale;
            AddLine(v, new Vector3(0, c.Y, c.Z), new Vector3(VolumeScale.X, c.Y, c.Z), CtViewPalette.Crosshair(0), 1f);
            AddLine(v, new Vector3(c.X, 0, c.Z), new Vector3(c.X, VolumeScale.Y, c.Z), CtViewPalette.Crosshair(1), 1f);
            AddLine(v, new Vector3(c.X, c.Y, 0), new Vector3(c.X, c.Y, VolumeScale.Z), CtViewPalette.Crosshair(2), 1f);
        }

        foreach (var p in ClippingPlanes.Where(p => p.Enabled && p.IsVisualizationVisible).Take(MAX_CLIPPING_PLANES))
            AddClipPolygon(v, p, CtViewPalette.ClipPlane, 0.9f);
    }

    private void AddBox(List<float> v, Vector3 lo, Vector3 hi, Vector3 color, float alpha)
    {
        Vector3 C(int i) => new(((i & 1) != 0) ? hi.X : lo.X, ((i & 2) != 0) ? hi.Y : lo.Y, ((i & 4) != 0) ? hi.Z : lo.Z);
        int[] edges = { 0,1, 1,3, 3,2, 2,0, 4,5, 5,7, 7,6, 6,4, 0,4, 1,5, 2,6, 3,7 };
        for (var i = 0; i < edges.Length; i += 2) AddLine(v, C(edges[i]), C(edges[i + 1]), color, alpha);
    }

    /// <summary>Outlines the full extent of the volume at <paramref name="pos" /> along an axis.</summary>
    private void AddAxisRect(List<float> v, int axis, float pos, Vector3 color, float alpha)
    {
        var s = VolumeScale;
        var p = Math.Clamp(pos, 0f, 1f) * Component(s, axis);
        Vector3[] q = axis switch
        {
            0 => new[] { new Vector3(p, 0, 0), new Vector3(p, s.Y, 0), new Vector3(p, s.Y, s.Z), new Vector3(p, 0, s.Z) },
            1 => new[] { new Vector3(0, p, 0), new Vector3(s.X, p, 0), new Vector3(s.X, p, s.Z), new Vector3(0, p, s.Z) },
            _ => new[] { new Vector3(0, 0, p), new Vector3(s.X, 0, p), new Vector3(s.X, s.Y, p), new Vector3(0, s.Y, p) }
        };
        for (var i = 0; i < 4; i++) AddLine(v, q[i], q[(i + 1) % 4], color, alpha);
    }

    /// <summary>Traces where an arbitrary plane crosses the volume, by cutting the 12 box edges.</summary>
    private void AddClipPolygon(List<float> v, ClippingPlane plane, Vector3 color, float alpha)
    {
        var n = Vector3.Normalize(plane.Normal);
        if (!float.IsFinite(n.X) || n.LengthSquared() < 1e-6f) return;
        // Matches the shader: dot(p - 0.5, n) == Distance - 0.5, in normalized volume space.
        var c = plane.Distance - 0.5f + Vector3.Dot(new Vector3(0.5f), n);
        Vector3 Corner(int i) => new((i & 1) != 0 ? 1 : 0, (i & 2) != 0 ? 1 : 0, (i & 4) != 0 ? 1 : 0);
        int[] edges = { 0,1, 1,3, 3,2, 2,0, 4,5, 5,7, 7,6, 6,4, 0,4, 1,5, 2,6, 3,7 };
        var hits = new List<Vector3>();
        for (var i = 0; i < edges.Length; i += 2)
        {
            var a = Corner(edges[i]); var b = Corner(edges[i + 1]);
            var da = Vector3.Dot(a, n) - c; var db = Vector3.Dot(b, n) - c;
            if (da * db > 0f || Math.Abs(da - db) < 1e-9f) continue;
            hits.Add(Vector3.Lerp(a, b, da / (da - db)));
        }
        if (hits.Count < 3) return;

        // Order the hits around the polygon centroid, in a basis lying in the plane.
        var centre = hits.Aggregate(Vector3.Zero, (s, p) => s + p) / hits.Count;
        var u = Vector3.Normalize(Vector3.Cross(n, Math.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
        var w = Vector3.Cross(n, u);
        hits.Sort((p, q) => MathF.Atan2(Vector3.Dot(p - centre, w), Vector3.Dot(p - centre, u))
            .CompareTo(MathF.Atan2(Vector3.Dot(q - centre, w), Vector3.Dot(q - centre, u))));
        for (var i = 0; i < hits.Count; i++)
            AddLine(v, hits[i] * VolumeScale, hits[(i + 1) % hits.Count] * VolumeScale, color, alpha);
    }

    private static void AddLine(List<float> v, Vector3 a, Vector3 b, Vector3 color, float alpha)
    {
        v.Add(a.X); v.Add(a.Y); v.Add(a.Z); v.Add(color.X); v.Add(color.Y); v.Add(color.Z); v.Add(alpha);
        v.Add(b.X); v.Add(b.Y); v.Add(b.Z); v.Add(color.X); v.Add(color.Y); v.Add(color.Z); v.Add(alpha);
    }

    /// <summary>
    ///     Where the textured plane for an axis sits, or null when that axis has none. A cut being
    ///     inspected wins over the crosshair slice: one plane per axis keeps a single full-resolution
    ///     slice texture per axis.
    /// </summary>
    private float? ResolvePlanePosition(int axis)
    {
        if (ShowPlaneVisualizations && ShowSliceOverlay && CutEnabled(axis) && CutVisual(axis)) return CutPosition(axis);
        if (ShowSlices) return Component(SlicePositions, axis);
        return null;
    }

    private bool CutEnabled(int axis) => axis switch { 0 => CutXEnabled, 1 => CutYEnabled, _ => CutZEnabled };
    private bool CutVisual(int axis) => axis switch { 0 => ShowCutXPlaneVisual, 1 => ShowCutYPlaneVisual, _ => ShowCutZPlaneVisual };
    private float CutPosition(int axis) => axis switch { 0 => CutXPosition, 1 => CutYPosition, _ => CutZPosition };
    private int AxisLength(int axis) => axis switch { 0 => _editableDataset.Width, 1 => _editableDataset.Height, _ => _editableDataset.Depth };
    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
    private static void SetComponent(ref Vector3 v, int axis, float value)
    {
        if (axis == 0) v.X = value; else if (axis == 1) v.Y = value; else v.Z = value;
    }

    /// <summary>The plane texture for an axis: (u, v) run along the two axes it does not span.</summary>
    private (int w, int h) SliceTextureSize(int axis) => axis switch
    {
        0 => (_editableDataset.Height, _editableDataset.Depth),
        1 => (_editableDataset.Width, _editableDataset.Depth),
        _ => (_editableDataset.Width, _editableDataset.Height)
    };

    private void UpdateSlicePlaneTextures()
    {
        for (var axis = 0; axis < 3; axis++)
        {
            var pos = ResolvePlanePosition(axis);
            if (pos == null) continue;
            var length = AxisLength(axis);
            if (length <= 0) continue;
            var index = Math.Clamp((int)MathF.Round(pos.Value * (length - 1)), 0, length - 1);
            if (_sliceTextures[axis] != 0 && _sliceTextureIndex[axis] == index) continue;
            var (w, h) = SliceTextureSize(axis);
            if (w <= 0 || h <= 0) continue;
            var data = ExtractSlice(axis, index, w, h);
            if (_sliceTextures[axis] == 0) _sliceTextures[axis] = CreateSliceTexture(w, h);
            GL.BindTexture(TextureTarget.Texture2D, _sliceTextures[axis]);
            // R8 rows are not padded to 4 bytes, which the default unpack alignment assumes.
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, w, h, PixelFormat.Red, PixelType.UnsignedByte, data);
            _sliceTextureIndex[axis] = index;
        }
    }

    private byte[] ExtractSlice(int axis, int index, int w, int h)
    {
        var data = new byte[w * h];
        var volume = _editableDataset.VolumeData;
        if (volume == null) return data;
        try
        {
            switch (axis)
            {
                case 0:
                    Parallel.For(0, h, z => { for (var y = 0; y < w; y++) data[z * w + y] = volume[index, y, z]; });
                    break;
                case 1:
                    Parallel.For(0, h, z => { for (var x = 0; x < w; x++) data[z * w + x] = volume[x, index, z]; });
                    break;
                default:
                    volume.ReadSliceZ(index, data);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtVolume3DViewer] Could not read the slice for axis {axis} at {index}: {ex.Message}");
        }
        return data;
    }

    private static int CreateSliceTexture(int w, int h)
    {
        var t = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, t);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, w, h, 0, PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return t;
    }

    private void InvalidateSlicePlaneTextures()
    {
        for (var i = 0; i < 3; i++) _sliceTextureIndex[i] = -1;
    }

    // --- Screen-space annotation -------------------------------------------------------------

    private void DrawOverlayLabels(Vector2 origin, Vector2 size)
    {
        if (!ShowBoundingBox || !ShowBoundingBoxLabels) return;
        var dl = ImGui.GetWindowDrawList();
        var vp = _view * _projection;
        var s = VolumeScale;
        var unit = _editableDataset.Unit ?? "µm";
        var px = _editableDataset.PixelSize;
        var thickness = _editableDataset.SliceThickness > 0 ? _editableDataset.SliceThickness : px;

        Label(dl, vp, origin, size, Vector3.Zero, "0, 0, 0", CtViewPalette.BoundingBox);
        Label(dl, vp, origin, size, new Vector3(s.X * 0.5f, 0, 0),
            $"X  {_editableDataset.Width} px · {FormatLength(_editableDataset.Width, px, unit)}", CtViewPalette.BoundingBox);
        Label(dl, vp, origin, size, new Vector3(0, s.Y * 0.5f, 0),
            $"Y  {_editableDataset.Height} px · {FormatLength(_editableDataset.Height, px, unit)}", CtViewPalette.BoundingBox);
        Label(dl, vp, origin, size, new Vector3(0, 0, s.Z * 0.5f),
            $"Z  {_editableDataset.Depth} px · {FormatLength(_editableDataset.Depth, thickness, unit)}", CtViewPalette.BoundingBox);

        if (!ShowPlaneVisualizations) return;
        for (var axis = 0; axis < 3; axis++)
        {
            var voxelSize = axis == 2 ? thickness : px;
            var name = axis switch { 0 => "X", 1 => "Y", _ => "Z" };
            if (CutEnabled(axis) && CutVisual(axis))
            {
                var voxel = (int)MathF.Round(CutPosition(axis) * (AxisLength(axis) - 1));
                Label(dl, vp, origin, size, PlaneCentre(axis, CutPosition(axis)),
                    $"{name} cut  {voxel} px · {FormatLength(voxel, voxelSize, unit)}", CtViewPalette.Cut(axis));
            }
            else if (ShowSlices)
            {
                var voxel = (int)MathF.Round(Component(SlicePositions, axis) * (AxisLength(axis) - 1));
                Label(dl, vp, origin, size, PlaneCentre(axis, Component(SlicePositions, axis)),
                    $"{name} {voxel} px", CtViewPalette.Crosshair(axis));
            }
        }
    }

    private Vector3 PlaneCentre(int axis, float pos)
    {
        var s = VolumeScale;
        var p = Math.Clamp(pos, 0f, 1f) * Component(s, axis);
        return axis switch
        {
            0 => new Vector3(p, s.Y * 0.5f, s.Z),
            1 => new Vector3(s.X * 0.5f, p, s.Z),
            _ => new Vector3(s.X * 0.5f, s.Y, p)
        };
    }

    private static void Label(ImDrawListPtr dl, Matrix4x4 vp, Vector2 origin, Vector2 size, Vector3 world,
        string text, Vector3 color)
    {
        if (!Project(vp, origin, size, world, out var screen)) return;
        dl.AddText(screen + new Vector2(1, 1), CtViewPalette.ToImGui(Vector3.Zero, 0.8f), text);
        dl.AddText(screen, CtViewPalette.ToImGui(color), text);
    }

    /// <summary>Projects a world point onto the displayed image. The image is drawn flipped, so
    /// that NDC +Y lands at the top of the rect.</summary>
    private static bool Project(Matrix4x4 viewProj, Vector2 origin, Vector2 size, Vector3 world, out Vector2 screen)
    {
        screen = Vector2.Zero;
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-5f) return false;
        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (ndc.X < -1.5f || ndc.X > 1.5f || ndc.Y < -1.5f || ndc.Y > 1.5f) return false;
        screen = origin + new Vector2((ndc.X * 0.5f + 0.5f) * size.X, (1f - (ndc.Y * 0.5f + 0.5f)) * size.Y);
        return true;
    }

    private static string FormatLength(int voxels, float voxelSize, string unit)
    {
        if (voxelSize <= 0 || !float.IsFinite(voxelSize)) return $"{voxels} px";
        var length = voxels * voxelSize;
        if ((unit == "µm" || unit == "um") && length >= 1000f) return $"{length / 1000f:0.##} mm";
        return $"{length:0.##} {unit}";
    }

    private Vector3 CameraPosition => _cameraTarget + new Vector3(MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch), MathF.Sin(_cameraPitch), MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch)) * _cameraDistance;

    private void HandleInput(bool isHovered, bool suppressLeftDrag = false)
    {
        if (!isHovered) { _dragging = _panning = false; return; }
        var io = ImGui.GetIO(); var mouse = io.MousePos;
        if (suppressLeftDrag) _dragging = false;
        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { _dragging = true; _lastMouse = mouse; }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) _dragging = false;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) { _panning = true; _lastMouse = mouse; }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle)) _panning = false;
        var delta = mouse - _lastMouse; _lastMouse = mouse;
        if (_dragging) { _cameraYaw += delta.X * 0.008f; _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.008f, -1.5f, 1.5f); }
        if (_panning)
        {
            // Pan in the camera's image plane. Moving along global X/Y made a vertical drag turn
            // into a dolly movement after orbiting the volume, while lateral movement could almost
            // disappear depending on yaw.
            var forward = Vector3.Normalize(_cameraTarget - CameraPosition);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));
            _cameraTarget += (-delta.X * right + delta.Y * up) * (_cameraDistance * 0.001f);
        }
        if (Math.Abs(io.MouseWheel) > 0) _cameraDistance = Math.Clamp(_cameraDistance * MathF.Pow(0.88f, io.MouseWheel), 0.15f, 20f);
        UpdateCamera();
    }

    public void ResetCamera() { _cameraTarget = VolumeScale * 0.5f; _cameraYaw = -MathF.PI / 4; _cameraPitch = MathF.PI / 6; _cameraDistance = Math.Max(1.5f, VolumeScale.Length() * 1.25f); UpdateCamera(); }
    public void ResetView() { ResetCamera(); CutXEnabled = CutYEnabled = CutZEnabled = false; ClippingPlanes.Clear(); InvalidateSlicePlaneTextures(); }
    private void UpdateCamera() { _view = Matrix4x4.CreateLookAt(CameraPosition, _cameraTarget, Vector3.UnitY); _projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, _renderWidth / (float)_renderHeight, 0.01f, 100f); }
    public void UpdateClippingPlaneNormal(ClippingPlane p) { var q = Quaternion.CreateFromYawPitchRoll(p.Rotation.Y, p.Rotation.X, p.Rotation.Z); p.Normal = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, q)); }
    public void MarkLabelsAsDirty() => _labelsDirty = true;
    public bool GetMaterialVisibility(byte id) => !_materialVisibility.TryGetValue(id, out var v) || v;
    public float GetMaterialOpacity(byte id) => _materialOpacity.GetValueOrDefault(id, 1f);
    public void SetMaterialVisibility(byte id, bool value) { _materialVisibility[id] = value; _materialsDirty = true; }
    public void SetMaterialOpacity(byte id, float value) { _materialOpacity[id] = value; _materialsDirty = true; }
    public void SetAllMaterialsVisibility(bool value) { foreach (var m in _editableDataset.Materials) _materialVisibility[m.ID] = value; _materialsDirty = true; }
    public void ResetAllMaterialOpacities() { foreach (var m in _editableDataset.Materials) _materialOpacity[m.ID] = 1; _materialsDirty = true; }

    /// <summary>Number of resolution levels in the multiresolution file.</summary>
    public int RenderLodCount => _streamingDataset.LodCount;

    /// <summary>True once the sparse brick renderer is live (per-brick streaming detail).</summary>
    public bool SparseActive => _sparse is { Ready: true };

    public void SaveScreenshot(string path)
    {
        var pixels = new byte[_renderWidth * _renderHeight * 4];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo); GL.ReadPixels(0, 0, _renderWidth, _renderHeight, PixelFormat.Rgba, PixelType.UnsignedByte, pixels); GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        var flipped = new byte[pixels.Length]; var stride = _renderWidth * 4;
        for (var y = 0; y < _renderHeight; y++) System.Buffer.BlockCopy(pixels, y * stride, flipped, (_renderHeight - 1 - y) * stride, stride);
        using var fs = File.Create(path); new ImageWriter().WritePng(flipped, _renderWidth, _renderHeight, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fs);
    }

    private void ResizeTarget(int w, int h)
    {
        _renderWidth = w; _renderHeight = h;
        if (_fbo == 0) _fbo = GL.GenFramebuffer();
        if (_colorTexture != 0) GL.DeleteTexture(_colorTexture); if (_depthBuffer != 0) GL.DeleteRenderbuffer(_depthBuffer);
        _colorTexture = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, _colorTexture); GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero); GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear); GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _depthBuffer = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer); GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, w, h);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo); GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colorTexture, 0); GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthBuffer); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); UpdateCamera();
    }

    private static int CreateTexture3D(int w, int h, int d, byte[] data, bool linear = true) { var t=GL.GenTexture(); var filter=linear?(int)TextureMinFilter.Linear:(int)TextureMinFilter.Nearest; GL.BindTexture(TextureTarget.Texture3D,t); GL.PixelStore(PixelStoreParameter.UnpackAlignment,1); GL.TexImage3D(TextureTarget.Texture3D,0,PixelInternalFormat.R8,w,h,d,0,PixelFormat.Red,PixelType.UnsignedByte,data); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureMinFilter,filter); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureMagFilter,filter); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureWrapR,(int)TextureWrapMode.ClampToEdge); return t; }

    /// <summary>256x1 RGBA lookup indexed by label id: rgb is the material colour, alpha its
    /// effective opacity. A hidden material is stored as opacity 0, which the shader's mix()
    /// and max() already collapse to a no-op, so visibility needs no separate channel.</summary>
    private static int CreateMaterialTexture() { var t=GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D,t); GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,256,1,0,PixelFormat.Rgba,PixelType.UnsignedByte,IntPtr.Zero); GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Nearest); GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMinFilter.Nearest); GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge); GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge); return t; }

    private void UploadMaterials()
    {
        var data = new byte[256 * 4];
        foreach (var m in _editableDataset.Materials)
        {
            // Id 0 is unlabelled background and must stay untouched by the overlay.
            if (m.ID == 0) continue;
            var opacity = GetMaterialVisibility(m.ID) ? Math.Clamp(GetMaterialOpacity(m.ID), 0f, 1f) : 0f;
            var o = m.ID * 4;
            data[o] = (byte)(Math.Clamp(m.Color.X, 0f, 1f) * 255f);
            data[o + 1] = (byte)(Math.Clamp(m.Color.Y, 0f, 1f) * 255f);
            data[o + 2] = (byte)(Math.Clamp(m.Color.Z, 0f, 1f) * 255f);
            data[o + 3] = (byte)(opacity * 255f);
        }
        GL.BindTexture(TextureTarget.Texture2D, _materialTexture);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 256, 1, PixelFormat.Rgba, PixelType.UnsignedByte, data);
    }
    // All index math is 64-bit: LODs near the array-length ceiling overflowed the previous
    // int arithmetic and surfaced as an arithmetic-overflow failure while loading.
    private static (int w, int h, int d) CalculateLabelTextureDimensions(int w, int h, int d, long budget)
    {
        var voxelCount = (long)w * h * d;
        var scale = voxelCount > budget ? Math.Pow(budget / (double)voxelCount, 1.0 / 3) : 1;
        return (Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)),
            Math.Max(1, (int)(d * scale)));
    }

    private (int w, int h, int d, byte[] data) CreateDownsampledLabels(int w, int h, int d,
        long budget, Action<float> progress = null)
    {
        var (tw, th, td) = CalculateLabelTextureDimensions(w, h, d, budget);
        var result = new byte[tw * th * td];
        if (_editableDataset.LabelData == null) return (tw, th, td, result);

        var sourceLength = checked(_editableDataset.Width * _editableDataset.Height);
        var sourceSlice = ArrayPool<byte>.Shared.Rent(sourceLength);
        try
        {
            for (var z = 0; z < td; z++)
            {
                var sourceZ = Math.Min(_editableDataset.Depth - 1, z * _editableDataset.Depth / td);
                _editableDataset.LabelData.ReadSliceZ(sourceZ, sourceSlice);
                for (var y = 0; y < th; y++)
                {
                    var sourceY = Math.Min(_editableDataset.Height - 1, y * _editableDataset.Height / th);
                    var sourceRow = sourceY * _editableDataset.Width;
                    var targetRow = (z * th + y) * tw;
                    for (var x = 0; x < tw; x++)
                        result[targetRow + x] = sourceSlice[sourceRow +
                            Math.Min(_editableDataset.Width - 1, x * _editableDataset.Width / tw)];
                }
                if ((z & 3) == 0 || z == td - 1) progress?.Invoke((z + 1f) / td);
                Thread.Sleep(1); // Yield I/O bandwidth to interactive slice rendering.
            }
        }
        finally { ArrayPool<byte>.Shared.Return(sourceSlice); }
        return (tw, th, td, result);
    }
    private void ProcessPendingLabelRefresh()
    {
        ProcessIncrementalLabelPatches();
        if (_labelBuildTask == null && _labelsDirty)
        {
            _labelsDirty = false;
            var w = _labelTextureWidth; var h = _labelTextureHeight; var d = _labelTextureDepth;
            var allowCachedLabels = !_labelCacheInvalidated;
            _labelBuildProgress = 0;
            _labelBuildStatus = "Checking label render cache...";
            _labelBuildTask = Task.Factory.StartNew(() =>
            {
                try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
                if (allowCachedLabels && TryLoadLabelRenderCache(w, h, d, out var cached))
                {
                    _labelBuildStatus = "Loading cached material labels...";
                    _labelBuildProgress = 1;
                    return (w, h, d, cached);
                }
                _labelBuildStatus = "Building label render cache (one-time)...";
                var built = CreateDownsampledLabels(w, h, d, long.MaxValue, value => _labelBuildProgress = value);
                _labelBuildStatus = "Saving label render cache...";
                SaveLabelRenderCache(built);
                _labelCacheInvalidated = false;
                _labelBuildProgress = 1;
                return built;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return;
        }
        if (_labelBuildTask?.IsCompletedSuccessfully != true) return;
        var a = _labelBuildTask.Result;
        _labelBuildTask = null;
        _labelCacheData = a.data;
        GL.BindTexture(TextureTarget.Texture3D, _labelTexture);
        GL.TexSubImage3D(TextureTarget.Texture3D, 0, 0, 0, 0, a.w, a.h, a.d,
            PixelFormat.Red, PixelType.UnsignedByte, a.data);
    }

    private void OnLabelSliceChanged(int z)
    {
        _incrementalLabelsPending = true;
        _dirtyLabelSlices[z] = 0;
    }

    private void ProcessIncrementalLabelPatches()
    {
        if (_labelBuildTask != null || _labelCacheData == null) return;
        if (_labelPatchTask == null && !_dirtyLabelSlices.IsEmpty)
        {
            var changed = _dirtyLabelSlices.Keys.ToHashSet();
            foreach (var z in changed) _dirtyLabelSlices.TryRemove(z, out _);
            var tw = _labelTextureWidth; var th = _labelTextureHeight; var td = _labelTextureDepth;
            _labelPatchTask = Task.Run(() =>
            {
                var patches = new List<(int z, byte[] data)>();
                var source = ArrayPool<byte>.Shared.Rent(checked(_editableDataset.Width * _editableDataset.Height));
                try
                {
                    for (var targetZ = 0; targetZ < td; targetZ++)
                    {
                        var sourceZ = Math.Min(_editableDataset.Depth - 1,
                            targetZ * _editableDataset.Depth / td);
                        if (!changed.Contains(sourceZ)) continue;
                        _editableDataset.LabelData.ReadSliceZ(sourceZ, source);
                        var layer = new byte[tw * th];
                        for (var y = 0; y < th; y++)
                        {
                            var sourceY = Math.Min(_editableDataset.Height - 1,
                                y * _editableDataset.Height / th);
                            for (var x = 0; x < tw; x++)
                                layer[y * tw + x] = source[sourceY * _editableDataset.Width +
                                    Math.Min(_editableDataset.Width - 1, x * _editableDataset.Width / tw)];
                        }
                        patches.Add((targetZ, layer));
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(source); }
                return patches;
            });
            return;
        }
        if (_labelPatchTask?.IsCompletedSuccessfully != true) return;
        var completed = _labelPatchTask.Result;
        _labelPatchTask = null;
        _incrementalLabelsPending = !_dirtyLabelSlices.IsEmpty;
        if (completed.Count == 0) return;
        GL.BindTexture(TextureTarget.Texture3D, _labelTexture);
        foreach (var patch in completed)
        {
            patch.data.CopyTo(_labelCacheData, patch.z * patch.data.Length);
            GL.TexSubImage3D(TextureTarget.Texture3D, 0, 0, 0, patch.z,
                _labelTextureWidth, _labelTextureHeight, 1, PixelFormat.Red, PixelType.UnsignedByte, patch.data);
            PatchLabelRenderCache(patch.z, patch.data);
        }
    }

    private void PatchLabelRenderCache(int z, byte[] layer)
    {
        try
        {
            var path = _editableDataset.GetLabelRenderCachePath();
            if (!File.Exists(path)) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read,
                1024 * 1024, FileOptions.RandomAccess);
            stream.Position = 16L + (long)z * layer.Length;
            stream.Write(layer);
        }
        catch (Exception ex) { Logger.LogWarning($"[CtVolume3DViewer] Cannot patch label render cache: {ex.Message}"); }
    }

    private bool TryLoadLabelRenderCache(int w, int h, int d, out byte[] data)
    {
        data = null;
        var path = _editableDataset.GetLabelRenderCachePath();
        if (!File.Exists(path)) return false;
        try
        {
            using var reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            if (reader.ReadInt32() != 0x47414C52 || reader.ReadInt32() != w || reader.ReadInt32() != h ||
                reader.ReadInt32() != d) return false;
            var length = checked(w * h * d);
            data = reader.ReadBytes(length);
            return data.Length == length;
        }
        catch { data = null; return false; }
    }

    private void SaveLabelRenderCache((int w, int h, int d, byte[] data) cache)
    {
        try
        {
            var path = _editableDataset.GetLabelRenderCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var temporaryPath = path + ".tmp";
            using (var writer = new BinaryWriter(new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                       FileShare.None, 4 * 1024 * 1024, FileOptions.SequentialScan)))
            {
                writer.Write(0x47414C52); writer.Write(cache.w); writer.Write(cache.h); writer.Write(cache.d);
                writer.Write(cache.data);
            }
            File.Move(temporaryPath, path, true);
        }
        catch (Exception ex) { Logger.LogWarning($"[CtVolume3DViewer] Cannot save label render cache: {ex.Message}"); }
    }
    private void ProcessPreviewRefresh()
    {
        if (_previewBuildTask == null && _previewDirty)
        {
            _previewDirty = false;
            var preview = _previewMask;
            if (preview == null) return;
            var version = Volatile.Read(ref _previewVersion);
            var w = _labelTextureWidth; var h = _labelTextureHeight; var d = _labelTextureDepth;
            _previewBuildTask = Task.Run(() => (version, preview.BuildLod(w, h, d)));
            return;
        }
        if (_previewBuildTask is { IsFaulted: true })
        {
            Logger.LogWarning($"[CtVolume3DViewer] Preview LOD failed: {_previewBuildTask.Exception?.GetBaseException().Message}");
            _previewBuildTask = null;
            return;
        }
        if (_previewBuildTask?.IsCompletedSuccessfully != true) return;
        var built = _previewBuildTask.Result;
        _previewBuildTask = null;
        if (built.version != Volatile.Read(ref _previewVersion)) { _previewDirty = true; return; }
        GL.BindTexture(TextureTarget.Texture3D, _previewTexture);
        GL.TexSubImage3D(TextureTarget.Texture3D, 0, 0, 0, 0,
            _labelTextureWidth, _labelTextureHeight, _labelTextureDepth,
            PixelFormat.Red, PixelType.UnsignedByte, built.data);
    }
    private static long TextureWidth(int t){GL.BindTexture(TextureTarget.Texture3D,t);GL.GetTexLevelParameter(TextureTarget.Texture3D,0,GetTextureParameter.TextureWidth,out int v);return v;} private static long TextureHeight(int t){GL.BindTexture(TextureTarget.Texture3D,t);GL.GetTexLevelParameter(TextureTarget.Texture3D,0,GetTextureParameter.TextureHeight,out int v);return v;} private static long TextureDepth(int t){GL.BindTexture(TextureTarget.Texture3D,t);GL.GetTexLevelParameter(TextureTarget.Texture3D,0,GetTextureParameter.TextureDepth,out int v);return v;}
    // Material colours can be edited outside the 3D panel (segmentation, material editor),
    // so refresh the lookup alongside the labels rather than only on visibility/opacity changes.
    private void OnDatasetDataChanged(Dataset d){if(d==_editableDataset){if(_observedLabelVolume!=_editableDataset.LabelData){if(_observedLabelVolume!=null)_observedLabelVolume.SliceChanged-=OnLabelSliceChanged;_observedLabelVolume=_editableDataset.LabelData;if(_observedLabelVolume!=null)_observedLabelVolume.SliceChanged+=OnLabelSliceChanged;_labelsDirty=true;_labelCacheInvalidated=true;}var virtualOnly=_observedVirtualLabelRevision!=_editableDataset.VirtualLabelRevision;_observedVirtualLabelRevision=_editableDataset.VirtualLabelRevision;if(!virtualOnly&&!_incrementalLabelsPending){_labelsDirty=true;_labelCacheInvalidated=true;}_materialsDirty=true;InvalidateSlicePlaneTextures();}} private void OnPreviewChanged(CtImageStackDataset d,CtPreviewVolume m,Vector4 c){if(d!=_editableDataset)return;_previewMask=m;_previewColor=c;_showPreview=m!=null;Interlocked.Increment(ref _previewVersion);_previewDirty=true;}
    private void Bind3D(int unit,int tex,string name){GL.ActiveTexture(TextureUnit.Texture0+unit);GL.BindTexture(TextureTarget.Texture3D,tex);Set1(name,unit);} private void Set1(string n,int v)=>GL.Uniform1(GL.GetUniformLocation(_program,n),v);private void Set1(string n,float v)=>GL.Uniform1(GL.GetUniformLocation(_program,n),v);private void Set3(string n,Vector3 v)=>GL.Uniform3(GL.GetUniformLocation(_program,n),v.X,v.Y,v.Z);private void Set4(string n,Vector4 v)=>GL.Uniform4(GL.GetUniformLocation(_program,n),v.X,v.Y,v.Z,v.W);
    // System.Numerics is row-vector (v*M); GLSL is column-vector (M*v). Passing the row-major
    // array with transpose=false makes GL read it column-major, which is the transpose GLSL needs.
    private static float[] ToArray(Matrix4x4 m)=>new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
    private void SetMatrix(string n,Matrix4x4 m){GL.UniformMatrix4(GL.GetUniformLocation(_program,n),1,false,ToArray(m));}
    private static int CreateProgram(string vs,string fs){int Compile(ShaderType t,string s){var x=GL.CreateShader(t);
        // Without a current context glCreateShader silently returns 0 and the info log comes back
        // empty, so report the real cause instead of an InvalidOperationException with no message.
        if(x==0)throw new InvalidOperationException($"glCreateShader({t}) returned 0: no OpenGL context is current on thread {Environment.CurrentManagedThreadId}. Create the CT viewer on the render thread (see OpenTkManager.ExecuteOnMainThread).");
        GL.ShaderSource(x,s);GL.CompileShader(x);GL.GetShader(x,ShaderParameter.CompileStatus,out var ok);if(ok==0)throw new InvalidOperationException($"{t} compilation failed: {GL.GetShaderInfoLog(x)}");return x;}var v=Compile(ShaderType.VertexShader,vs);var f=Compile(ShaderType.FragmentShader,fs);var p=GL.CreateProgram();GL.AttachShader(p,v);GL.AttachShader(p,f);GL.LinkProgram(p);GL.GetProgram(p,GetProgramParameterName.LinkStatus,out var ok);GL.DeleteShader(v);GL.DeleteShader(f);if(ok==0)throw new InvalidOperationException(GL.GetProgramInfoLog(p));return p;}

    public static Vector3 CalculateNormalizedPhysicalScale(int w,int h,int d,float px,float st){var xy=px>0&&float.IsFinite(px)?px:1;var z=st>0&&float.IsFinite(st)?st:xy;var p=new Vector3(Math.Max(1,w)*xy,Math.Max(1,h)*xy,Math.Max(1,d)*z);return p/Math.Max(p.X,Math.Max(p.Y,p.Z));}
    public static byte CalculateOtsuThreshold(byte[] data){if(data==null||data.Length==0)return 0;var h=new long[256];foreach(var v in data)h[v]++;double sum=0,sb=0,best=-1;long wb=0;for(int i=0;i<256;i++)sum+=i*h[i];byte t=0;for(int i=0;i<255;i++){wb+=h[i];if(wb==0)continue;var wf=data.LongLength-wb;if(wf==0)break;sb+=i*h[i];var mb=sb/wb;var mf=(sum-sb)/wf;var v=wb*(double)wf*(mb-mf)*(mb-mf);if(v>best){best=v;t=(byte)i;}}return t;}
    public void Dispose(){if(_disposed)return;_disposed=true;ProjectManager.Instance.DatasetDataChanged-=OnDatasetDataChanged;CtImageStackTools.Preview3DChanged-=OnPreviewChanged;if(_observedLabelVolume!=null)_observedLabelVolume.SliceChanged-=OnLabelSliceChanged;_controlPanel?.Dispose();_sparse?.Dispose();foreach(var t in new[]{_labelTexture,_previewTexture,_materialTexture,_colorTexture,_sliceTextures[0],_sliceTextures[1],_sliceTextures[2]})if(t!=0)GL.DeleteTexture(t);if(_depthBuffer!=0)GL.DeleteRenderbuffer(_depthBuffer);if(_fbo!=0)GL.DeleteFramebuffer(_fbo);if(_vbo!=0)GL.DeleteBuffer(_vbo);if(_ebo!=0)GL.DeleteBuffer(_ebo);if(_vao!=0)GL.DeleteVertexArray(_vao);if(_lineVbo!=0)GL.DeleteBuffer(_lineVbo);if(_lineVao!=0)GL.DeleteVertexArray(_lineVao);if(_program!=0)GL.DeleteProgram(_program);if(_lineProgram!=0)GL.DeleteProgram(_lineProgram);}

    private const string VertexShader=@"#version 330 core
layout(location=0) in vec3 p; uniform mat4 uView,uProjection; uniform vec3 uScale; out vec3 world; void main(){world=p*uScale;gl_Position=uProjection*uView*vec4(world,1);}";

    private const string LineVertexShader=@"#version 330 core
layout(location=0) in vec3 p; layout(location=1) in vec4 c; uniform mat4 uView,uProjection; out vec4 vColor;
void main(){vColor=c;gl_Position=uProjection*uView*vec4(p,1);}";

    private const string LineFragmentShader=@"#version 330 core
in vec4 vColor; out vec4 outColor; void main(){outColor=vColor;}";

    private const string FragmentShader=@"#version 330 core
in vec3 world;
out vec4 outColor;

uniform sampler3D uLabels,uPreview;
uniform sampler3D uBase,uAtlas;   // coarsest-LOD fallback + resident brick atlas
uniform usampler3D uPage;         // stacked page table: status + atlas slot per (level,brick)
uniform sampler2D uMaterials;
uniform sampler2D uSliceX,uSliceY,uSliceZ;   // full-resolution slice at each plane
uniform vec3 uScale,uCamera,uVolumeSize;
uniform vec3 uAtlasDim;
uniform vec3 uLodDim[16];
uniform int uPageZOff[16];
uniform int uSparseOn,uLevelCount,uMaxAtlasLevel;
uniform float uMin,uMax,uStep,uOpacity,uBrickCore,uBrickTile,uLodBias;
uniform int uShowGray,uColorMap,uShowPreview,uShowThresholdPreview,uPlaneCount,uVirtualRuleCount;
uniform vec2 uThresholdRange;
uniform vec4 uCutX,uCutY,uCutZ,uPlanes[8],uPreviewColor,uThresholdColor;
uniform vec4 uVirtualRules[32];
uniform float uPlaneMirror[8];
uniform vec3 uSlicePlanePos;   // normalized position of the textured plane, per axis
uniform vec3 uSlicePlaneOn;    // 1 = that axis has a textured plane

bool boxHit(vec3 ro,vec3 rd,out float a,out float b){
    vec3 q0=(vec3(0)-ro)/rd,q1=(uScale-ro)/rd,mn=min(q0,q1),mx=max(q0,q1);
    a=max(max(mn.x,mn.y),mn.z);b=min(min(mx.x,mx.y),mx.z);return b>=max(a,0.0);
}

bool cut(vec3 p){
    if(uCutX.x>.5&&(p.x-uCutX.z)*uCutX.y>0.0)return true;
    if(uCutY.x>.5&&(p.y-uCutY.z)*uCutY.y>0.0)return true;
    if(uCutZ.x>.5&&(p.z-uCutZ.z)*uCutZ.y>0.0)return true;
    for(int i=0;i<uPlaneCount;i++){
        vec3 n=normalize(uPlanes[i].xyz);
        float s=dot(p-vec3(.5),n)-(uPlanes[i].w-.5);
        if(uPlaneMirror[i]>.5?s<0.0:s>0.0)return true;
    }
    return false;
}

// Interleaved gradient noise (Jimenez). Every ray starts at the box entry and steps by a
// fixed ds, and for a perspective camera that entry varies as d/cos(theta) around the face
// normal, so the samples line up into concentric shells that read as rings while orbiting.
// Offsetting each ray by a per-pixel fraction of ds turns that coherent banding into
// high-frequency noise, which the eye tolerates far better.
float ign(vec2 q){return fract(52.9829189*fract(dot(q,vec2(.06711056,.00583715))));}

// Out-of-core density lookup. The desired resolution level follows the distance to the camera;
// starting there we walk toward coarser levels until the page table reports a resident brick,
// sampling it from the atlas (with the one-voxel apron for correct trilinear across bricks). A
// brick flagged empty short-circuits to air; if nothing is resident yet the coarsest LOD answers,
// so the image is always complete and sharpens as finer bricks stream in.
float den(vec3 p){
    if(uSparseOn==0||uMaxAtlasLevel<0) return texture(uBase,p).r;
    float dist=length(p*uScale-uCamera);
    int wanted=clamp(int(floor(log2(max(dist*uLodBias,1.0)))),0,uMaxAtlasLevel);
    for(int L=wanted;L<=uMaxAtlasLevel;L++){
        vec3 fdim=uLodDim[L];
        ivec3 grid=ivec3(ceil(fdim/uBrickCore));
        vec3 f=p*fdim;
        ivec3 bc=clamp(ivec3(floor(f/uBrickCore)),ivec3(0),grid-ivec3(1));
        uvec4 e=texelFetch(uPage,ivec3(bc.x,bc.y,uPageZOff[L]+bc.z),0);
        uint st=e.r;
        if(st==2u) return 0.0;
        if(st==1u){
            vec3 slot=vec3(e.gba);
            vec3 local=f-vec3(bc)*uBrickCore;
            vec3 coord=slot*uBrickTile+1.0+local;
            return texture(uAtlas,(coord+0.5)/uAtlasDim).r;
        }
    }
    return texture(uBase,p).r;
}

vec3 cmap(float x){
    if(uColorMap==0)return vec3(x);
    if(uColorMap==1)return clamp(vec3(3*x,3*x-1,3*x-2),0,1);
    if(uColorMap==2)return vec3(x,1-x,1);
    return clamp(abs(mod(x*6+vec3(0,4,2),6)-3)-1,0,1);
}

float window(float density){return clamp((density-uMin)/max(.001,uMax-uMin),0,1);}

float samplePlane(int axis,vec3 p){
    if(axis==0)return texture(uSliceX,vec2(p.y,p.z)).r;
    if(axis==1)return texture(uSliceY,vec2(p.x,p.z)).r;
    return texture(uSliceZ,vec2(p.x,p.y)).r;
}

void main(){
    vec3 ro=uCamera,rd=normalize(world-ro);
    float a,b;
    if(!boxHit(ro,rd,a,b))discard;
    a=max(a,0);

    // Normalized-space ray: p(t) = ron + rdn*t stays on the same parameter t as the world ray.
    vec3 ron=ro/uScale,rdn=rd/uScale;

    // The planes are opaque, so the nearest one both terminates the march and is composited
    // underneath whatever the volume accumulated in front of it.
    float tPlane=1e30;int planeAxis=-1;vec3 planeP=vec3(0);
    for(int axis=0;axis<3;axis++){
        if(uSlicePlaneOn[axis]<.5)continue;
        if(abs(rdn[axis])<1e-6)continue;
        float t=(uSlicePlanePos[axis]-ron[axis])/rdn[axis];
        if(t<a||t>b||t>=tPlane)continue;
        vec3 p=ron+rdn*t;
        // Snap the spanned axis: a cut tests (p[axis] - cutPos) * dir > 0, and rounding either
        // side of an exact hit would speckle the plane away half the time.
        p[axis]=uSlicePlanePos[axis];
        if(any(lessThan(p,vec3(0)))||any(greaterThan(p,vec3(1))))continue;
        if(cut(p))continue;
        tPlane=t;planeAxis=axis;planeP=p;
    }

    vec4 acc=vec4(0);
    // A finest-LOD voxel sets the base march step; the step then scales with the LOD the ray is
    // actually sampling, so far/coarse regions cost few samples while near/fine regions stay at
    // ~2 samples per voxel. A hard floor bounds the iteration count regardless.
    float baseStep=min(uScale.x/uVolumeSize.x,min(uScale.y/uVolumeSize.y,uScale.z/uVolumeSize.z));
    float minStep=(b-a)/4096.0;
    float end=min(b,tPlane);
    a+=baseStep*ign(gl_FragCoord.xy);
    for(int i=0;i<4096&&a<=end&&acc.a<.985;i++){
        vec3 p=(ro+rd*a)/uScale;
        // Coarse empty-space skip: when the largest brick covering p is known air, jump to its exit.
        if(uSparseOn!=0&&uMaxAtlasLevel>=0){
            vec3 fdim=uLodDim[uMaxAtlasLevel];
            ivec3 grid=ivec3(ceil(fdim/uBrickCore));
            ivec3 bc=clamp(ivec3(floor(p*fdim/uBrickCore)),ivec3(0),grid-ivec3(1));
            if(texelFetch(uPage,ivec3(bc.x,bc.y,uPageZOff[uMaxAtlasLevel]+bc.z),0).r==2u){
                vec3 lo=vec3(bc)*uBrickCore/fdim, hi=(vec3(bc)+1.0)*uBrickCore/fdim;
                vec3 tx=max((lo-ron)/rdn,(hi-ron)/rdn);
                a=min(min(tx.x,tx.y),tx.z)+minStep; continue;
            }
        }
        if(cut(p)){a+=baseStep;continue;}
        float dist=length(p*uScale-uCamera);
        int wl=uSparseOn!=0?clamp(int(floor(log2(max(dist*uLodBias,1.0)))),0,max(uMaxAtlasLevel,0)):0;
        float stp=max(baseStep*exp2(float(wl))*uStep,minStep);
        float dsty=den(p);
        float n=window(dsty);
        float al=uShowGray!=0?smoothstep(0,1,n):0;
        vec3 col=cmap(n);
        if(al>.01){
            vec3 e=1/uVolumeSize;
            vec3 g=vec3(den(p+vec3(e.x,0,0))-den(p-vec3(e.x,0,0)),
                        den(p+vec3(0,e.y,0))-den(p-vec3(0,e.y,0)),
                        den(p+vec3(0,0,e.z))-den(p-vec3(0,0,e.z)));
            if(length(g)>.0001)col*=.3+.7*abs(dot(normalize(g),normalize(vec3(.4,.6,1))));
        }
        int mid=int(texture(uLabels,p).r*255.0+0.5);
        for(int ri=0;ri<uVirtualRuleCount;ri++){
            vec4 rule=uVirtualRules[ri];
            int rid=int(rule.z*255.0+0.5);
            if(dsty>=rule.x&&dsty<=rule.y){if(rule.w>.5)mid=rid;else if(mid==rid)mid=0;}
        }
        if(mid>0){
            vec4 mat=texelFetch(uMaterials,ivec2(mid,0),0);
            if(mat.a>.002){col=mix(col,mat.rgb,mat.a);al=max(al,mat.a);}
        }
        if(uShowPreview!=0&&texture(uPreview,p).r>.5){col=mix(col,uPreviewColor.rgb,uPreviewColor.a);al=max(al,uPreviewColor.a);}
        if(uShowThresholdPreview!=0&&dsty>=uThresholdRange.x&&dsty<=uThresholdRange.y){col=mix(col,uThresholdColor.rgb,.55);al=max(al,.45);}
        float ca=clamp(al*stp*80*uOpacity,0,1);
        acc+=(1-acc.a)*vec4(col*ca,ca);
        a+=stp;
    }

    if(planeAxis>=0&&acc.a<.985){
        // Same window and colour map as the slice views, so a feature reads identically in both.
        float planeDensity=samplePlane(planeAxis,planeP);
        vec3 col=cmap(window(planeDensity));
        int mid=int(texture(uLabels,planeP).r*255.0+0.5);
        for(int ri=0;ri<uVirtualRuleCount;ri++){
            vec4 rule=uVirtualRules[ri];int rid=int(rule.z*255.0+0.5);
            if(planeDensity>=rule.x&&planeDensity<=rule.y){if(rule.w>.5)mid=rid;else if(mid==rid)mid=0;}
        }
        if(mid>0){
            vec4 mat=texelFetch(uMaterials,ivec2(mid,0),0);
            if(mat.a>.002)col=mix(col,mat.rgb,mat.a*.65);
        }
        if(uShowPreview!=0&&texture(uPreview,planeP).r>.5)col=mix(col,uPreviewColor.rgb,uPreviewColor.a);
        if(uShowThresholdPreview!=0&&planeDensity>=uThresholdRange.x&&planeDensity<=uThresholdRange.y)col=mix(col,uThresholdColor.rgb,.55);
        acc+=(1-acc.a)*vec4(col,1);
    }

    // Resolve against the clear color and write full alpha: this texture is later drawn by the
    // UI with standard blending, and a partially accumulated alpha would let the window
    // background bleed through, so the volume reads as translucent no matter how high the
    // opacity control is pushed. GLSL 3.30 sources must stay pure ASCII.
    outColor=vec4(acc.rgb+vec3(0.015,0.018,0.025)*(1-acc.a),1);
}";
}
