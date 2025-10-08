// GeoscientistToolkit/ImGuiController.cs (Fixed Unicode Ranges for Scientific Characters)

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.SPIRV;

namespace GeoscientistToolkit;

public sealed class ImGuiController : IDisposable
{
    // GLSL 330 for better OpenGL compatibility
    private const string VsGlsl330 = @"#version 330 core
layout(location=0) in vec2 in_position;
layout(location=1) in vec2 in_texCoord;
layout(location=2) in vec4 in_color;
uniform mat4 Projection;
out vec2 fs_Tex;
out vec4 fs_Col;
void main()
{
    fs_Tex = in_texCoord;
    fs_Col = in_color;
    gl_Position = Projection * vec4(in_position,0,1);
}";

    private const string FsGlsl330 = @"#version 330 core
in vec2 fs_Tex;
in vec4 fs_Col;
out vec4 out_Color;
uniform sampler2D MainTexture;
void main()
{
    out_Color = fs_Col * texture(MainTexture, fs_Tex);
}";

    // SPIR-V 450 for Vulkan
    private const string VsSpirv450 = @"#version 450
layout(location=0) in vec2 in_position;
layout(location=1) in vec2 in_texCoord;
layout(location=2) in vec4 in_color;
layout(set=0,binding=0) uniform ProjectionBuffer { mat4 Projection; };
layout(location=0) out vec2 fs_Tex;
layout(location=1) out vec4 fs_Col;
void main()
{
    fs_Tex = in_texCoord;
    fs_Col = in_color;
    gl_Position = Projection * vec4(in_position,0,1);
}";

    private const string FsSpirv450 = @"#version 450
layout(location=0) in vec2 fs_Tex;
layout(location=1) in vec4 fs_Col;
layout(location=0) out vec4 out_Color;
layout(set=0,binding=1) uniform texture2D MainTexture;
layout(set=0,binding=2) uniform sampler MainSampler;
void main()
{
    out_Color = fs_Col * texture(sampler2D(MainTexture,MainSampler), fs_Tex);
}";

    // HLSL for Direct3D11 with proper semantics
    private const string VsHlsl = @"
cbuffer ProjectionBuffer : register(b0)
{
    float4x4 Projection;
};

struct VS_INPUT
{
    float2 pos : POSITION;
    float2 uv  : TEXCOORD0;
    float4 col : COLOR0;
};

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv  : TEXCOORD0;
};

PS_INPUT VS(VS_INPUT input)
{
    PS_INPUT output;
    output.pos = mul(Projection, float4(input.pos.xy, 0.0, 1.0));
    output.col = input.col;
    output.uv  = input.uv;
    return output;
}";

    private const string FsHlsl = @"
struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv  : TEXCOORD0;
};

sampler sampler0 : register(s0);
Texture2D texture0 : register(t0);

float4 PS(PS_INPUT input) : SV_Target
{
    float4 out_col = input.col * texture0.Sample(sampler0, input.uv);
    return out_col;
}";

    // Metal shaders with fixed coordinate system
    private const string VsMetalFixed = @"
#include <metal_stdlib>
using namespace metal;

struct ProjectionBuffer
{
    float4x4 projection;
};

struct VertexIn
{
    float2 position [[attribute(0)]];
    float2 texCoord [[attribute(1)]];
    float4 color    [[attribute(2)]];
};

struct VertexOut
{
    float4 position [[position]];
    float2 texCoord;
    float4 color;
};

vertex VertexOut main0(VertexIn in [[stage_in]],
                       constant ProjectionBuffer& proj [[buffer(0)]])
{
    VertexOut out;
    out.position = proj.projection * float4(in.position, 0.0, 1.0);
    out.texCoord = in.texCoord;
    out.color = in.color;
    return out;
}";

    private const string FsMetalFixed = @"
#include <metal_stdlib>
using namespace metal;

struct VertexOut
{
    float4 position [[position]];
    float2 texCoord;
    float4 color;
};

fragment float4 main0(VertexOut in [[stage_in]],
                      texture2d<float> tex [[texture(0)]],
                      sampler samp [[sampler(0)]])
{
    return in.color * tex.sample(samp, in.texCoord);
}";

    private readonly Dictionary<Texture, TextureView> _autoViewsByTexture = new();

    private readonly IntPtr _fontID = 1;
    private readonly GraphicsDevice _gd;
    private readonly Vector2 _scale = Vector2.One;
    private readonly Dictionary<IntPtr, ResourceSet> _setsById = new();

    private readonly Dictionary<TextureView, ResourceSet> _setsByView = new();
    private Texture _fontTex;
    private bool _frameBegun;
    private ResourceLayout _layout;
    private IntPtr _nextTextureId = 100;
    private Pipeline _pipe;
    private ResourceSet _set;

    private DeviceBuffer _vb, _ib, _ub;

    private Shader _vs, _fs;
    private int _winW, _winH;

    // ------------------------------------------------------------------
    public ImGuiController(GraphicsDevice gd,
        OutputDescription fbDesc,
        int width, int height)
    {
        _gd = gd;
        _winW = width;
        _winH = height;

        Context = ImGui.CreateContext();
        ImGui.SetCurrentContext(Context);

        var io = ImGui.GetIO();
        io.Fonts.AddFontDefault();
        unsafe
        {
            // Extended ranges for scientific notation and symbols:
            // - Basic Latin + Latin-1 (© ® ° × ÷, ², ³, ¹, µ)
            // - Greek and Coptic (α β γ δ μ π τ Ω etc.)
            // - General Punctuation (– — … • ' ' " ")
            // - Superscripts and Subscripts (₀ ₁ ₂ ₃ ₄ ₅ ₆ ₇ ₈ ₉)
            // - Letterlike Symbols (™ ℹ Å)
            // - Mathematical Operators (± √ ≤ ≥ ≈ ≠ ∞)
            // - Miscellaneous Symbols (⛶ ☑ ✓ ✗ ⚡)
            var ranges = new ushort[]
            {
                0x0020, 0x00FF, // Basic Latin + Latin-1 (includes ² ³ ¹ µ °)
                0x0370, 0x03FF, // Greek and Coptic (μ τ π α β γ δ etc.)
                0x2000, 0x206F, // General Punctuation
                0x2070, 0x209F, // Superscripts and Subscripts (₀₁₂₃ etc.)
                0x20A0, 0x20CF, // Currency Symbols (€ etc.)
                0x2100, 0x214F, // Letterlike Symbols (™ ℹ Å)
                0x2150, 0x218F, // Number Forms (⅓, etc.)
                0x2190, 0x21FF, // Arrows (→ ↗ …)
                0x2200, 0x22FF, // Mathematical Operators (± √ ≤ ≥ ∞)
                0x2300, 0x23FF, // Misc Technical (⌘ ⌥ ⌫)
                0x2460, 0x24FF, // Enclosed Alphanumerics (① ②)
                0x2500, 0x257F, // Box Drawing
                0x2580, 0x259F, // Block Elements
                0x25A0, 0x25FF, // Geometric Shapes (◆ ● ▸)
                0x2600, 0x26FF, // Misc Symbols (★ ☑ ⛶)
                0x2700, 0x27BF, // Dingbats (✓ ✗ ❗)
                0x2B00, 0x2BFF, // Misc Symbols & Arrows
                0x03BC, 0x03BC, // Ensure μ (mu) is included explicitly
                0x03C4, 0x03C4, // Ensure τ (tau) is included explicitly
                0 // terminator
            };

            // Try fonts with good Unicode coverage
            string[] candidates;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                candidates = new[]
                {
                    @"C:\Windows\Fonts\segoeui.ttf", // Great Unicode coverage
                    @"C:\Windows\Fonts\seguisym.ttf", // Symbol extras
                    @"C:\Windows\Fonts\cambria.ttc", // Math symbols
                    @"C:\Windows\Fonts\arial.ttf", // Fallback
                    @"C:\Windows\Fonts\arialuni.ttf" // If present
                };
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                candidates = new[]
                {
                    "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
                    "/System/Library/Fonts/Helvetica.ttc",
                    "/System/Library/Fonts/Apple Symbols.ttf",
                    "/System/Library/Fonts/Supplemental/Symbol.ttf"
                };
            else
                candidates = new[]
                {
                    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                    "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
                    "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
                    "/usr/share/fonts/truetype/noto/NotoSansSymbols2-Regular.ttf",
                    "/usr/share/fonts/truetype/freefont/FreeSans.ttf"
                };

            ImFontConfigPtr cfg = ImGuiNative.ImFontConfig_ImFontConfig();
            cfg.MergeMode = true; // merge into default font
            cfg.PixelSnapH = true;

            fixed (ushort* pRanges = ranges)
            {
                // Load multiple fonts to maximize coverage
                foreach (var path in candidates)
                    if (File.Exists(path))
                    {
                        ImGui.GetIO().Fonts.AddFontFromFileTTF(path, 16f, cfg, (nint)pRanges);
                        Logger.Log(
                            $"[ImGuiController] Loaded font: {Path.GetFileName(path)} for extended Unicode support");
                    }
            }

            cfg.Destroy();
        }

        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;

        CreateDeviceResources(gd, fbDesc);
        SetPerFrameImGuiData(1f / 60f);

        // Don't call NewFrame in constructor - let Update handle it
        _frameBegun = false;
    }

    public IntPtr Context { get; }

    // ------------------------------------------------------------------
    public void Dispose()
    {
        _vb?.Dispose();
        _ib?.Dispose();
        _ub?.Dispose();
        _fontTex?.Dispose();
        _set?.Dispose();
        _vs?.Dispose();
        _fs?.Dispose();
        _layout?.Dispose();
        _pipe?.Dispose();

        foreach (var kvp in _setsByView) kvp.Value?.Dispose();

        if (Context != IntPtr.Zero) ImGui.DestroyContext(Context);
    }

    // ------------------------------------------------------------------
    private void CreateDeviceResources(GraphicsDevice gd,
        OutputDescription fbDesc)
    {
        var factory = gd.ResourceFactory;

        _vb = factory.CreateBuffer(new BufferDescription(10_000,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _ib = factory.CreateBuffer(new BufferDescription(2_000,
            BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        _ub = factory.CreateBuffer(new BufferDescription(64,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        // Create shaders - try SPIR-V first, fall back to backend-specific if needed
        var shadersCreated = false;

        // Only try SPIR-V cross-compilation if not on Windows D3D11 
        // (it has known issues with SPIR-V tools on some systems)
        if (!(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && gd.BackendType == GraphicsBackend.Direct3D11))
            try
            {
                CreateShadersWithSpirvCrossCompilation(factory);
                shadersCreated = true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"SPIR-V cross-compilation failed: {ex.Message}. Will use backend-specific shaders.");
            }

        if (!shadersCreated) CreateBackendSpecificShaders(factory);

        var vLayout = new VertexLayoutDescription(
            new VertexElementDescription("in_position", VertexElementSemantic.Position, VertexElementFormat.Float2),
            new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate,
                VertexElementFormat.Float2),
            new VertexElementDescription("in_color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Projection", ResourceKind.UniformBuffer, ShaderStages.Vertex),
            new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

        var blendState = BlendStateDescription.SingleAlphaBlend;
        var depthState = DepthStencilStateDescription.Disabled;
        var rasterState = RasterizerStateDescription.Default;
        rasterState.CullMode = FaceCullMode.None;
        rasterState.FrontFace = FrontFace.CounterClockwise;
        rasterState.ScissorTestEnabled = true;

        _pipe = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            blendState,
            depthState,
            rasterState,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vLayout }, new[] { _vs, _fs }),
            new[] { _layout },
            fbDesc,
            gd.BackendType == GraphicsBackend.Direct3D11
                ? ResourceBindingModel.Default
                : ResourceBindingModel.Improved));

        RecreateFontTexture(gd); // creates _fontTex *and* _set
    }

    private void CreateShadersWithSpirvCrossCompilation(ResourceFactory factory)
    {
        // GLSL shaders - simplified for better compatibility
        var vertexShaderGlsl = @"
#version 450
layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_texCoord;
layout(location = 2) in vec4 in_color;

layout(set = 0, binding = 0) uniform ProjectionBuffer
{
    mat4 Projection;
};

layout(location = 0) out vec2 frag_texCoord;
layout(location = 1) out vec4 frag_color;

void main()
{
    frag_texCoord = in_texCoord;
    frag_color = in_color;
    gl_Position = Projection * vec4(in_position, 0, 1);
}";

        var fragmentShaderGlsl = @"
#version 450
layout(location = 0) in vec2 frag_texCoord;
layout(location = 1) in vec4 frag_color;

layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 1) uniform texture2D MainTexture;
layout(set = 0, binding = 2) uniform sampler MainSampler;

void main()
{
    out_Color = frag_color * texture(sampler2D(MainTexture, MainSampler), frag_texCoord);
}";

        // Use SPIR-V cross compilation extension methods
        var vertexShaderDesc = new ShaderDescription(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(vertexShaderGlsl),
            "main");

        var fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(fragmentShaderGlsl),
            "main");

        var options = new CrossCompileOptions();

        // Configure options based on backend
        if (_gd.BackendType == GraphicsBackend.Metal)
        {
            options.FixClipSpaceZ = true;
            options.InvertVertexOutputY = false;
        }
        else if (_gd.BackendType == GraphicsBackend.Direct3D11)
        {
            options.FixClipSpaceZ = true;
            options.InvertVertexOutputY = false;
        }

        // Use the CreateFromSpirv extension method on ResourceFactory
        var shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc, options);
        _vs = shaders[0];
        _fs = shaders[1];

        Logger.Log($"Successfully created shaders using SPIR-V cross-compilation for {_gd.BackendType}");
    }

    private void CreateBackendSpecificShaders(ResourceFactory factory)
    {
        // Get backend-specific shaders based on the graphics backend
        var (vsSrc, fsSrc, vsEntry, fsEntry) = GetBackendSpecificShaders(_gd.BackendType);

        _vs = factory.CreateShader(new ShaderDescription(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(vsSrc),
            vsEntry));
        _fs = factory.CreateShader(new ShaderDescription(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(fsSrc),
            fsEntry));

        Logger.Log($"Created backend-specific shaders for {_gd.BackendType}");
    }

    // ------------------------------------------------------------------
    public void WindowResized(int width, int height)
    {
        // store the new size so SetPerFrameImGuiData() will publish it
        _winW = width;
        _winH = height;
    }

    private void RecreateFontTexture(GraphicsDevice gd)
    {
        ImGui.SetCurrentContext(Context);
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out var w, out var h, out var bpp);
        io.Fonts.SetTexID(_fontID);

        _fontTex?.Dispose();
        _fontTex = gd.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D((uint)w, (uint)h, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));

        gd.UpdateTexture(_fontTex, pixels,
            (uint)(bpp * w * h),
            0, 0, 0, (uint)w, (uint)h, 1, 0, 0);

        _set?.Dispose();
        _set = gd.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(_layout, _ub, _fontTex, gd.Aniso4xSampler));

        io.Fonts.ClearTexData();
    }

    /// <summary>
    ///     Gets or creates a handle for a texture to be displayed with ImGui.
    ///     Pass the returned handle to Image() or ImageButton().
    /// </summary>
    public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView)
    {
        if (!_setsByView.TryGetValue(textureView, out var resourceSet))
        {
            resourceSet =
                factory.CreateResourceSet(new ResourceSetDescription(_layout, _ub, textureView, _gd.Aniso4xSampler));
            var newId = _nextTextureId++;
            _setsByView.Add(textureView, resourceSet);
            _setsById.Add(newId, resourceSet);
            return newId;
        }

        // Find the existing ID
        return _setsById.First(kvp => kvp.Value == resourceSet).Key;
    }

    /// <summary>
    ///     Retrieves the shader texture binding for the given helper handle.
    /// </summary>
    public ResourceSet GetImageResourceSet(IntPtr imGuiBinding)
    {
        return _setsById[imGuiBinding];
    }

    /// <summary>
    ///     Removes a particular texture binding.
    /// </summary>
    public void RemoveImGuiBinding(TextureView textureView)
    {
        if (_setsByView.Remove(textureView, out var resourceSet))
        {
            var id = _setsById.First(kvp => kvp.Value == resourceSet).Key;
            _setsById.Remove(id);
            resourceSet.Dispose();
        }
    }

    // ------------------------------------------------------------------
    public void Update(float dt, InputSnapshot snap)
    {
        ImGui.SetCurrentContext(Context);

        SetPerFrameImGuiData(dt);
        UpdateInputs(snap);

        _frameBegun = true;
        ImGui.NewFrame();
    }

    // ------------------------------------------------------------------
    public void Render(GraphicsDevice gd, CommandList cl)
    {
        ImGui.SetCurrentContext(Context);

        if (!_frameBegun) return;

        ImGui.Render();
        DrawImGui(ImGui.GetDrawData(), gd, cl);
        ViewerScreenshotUtility.ProcessDeferredCaptures();
        _frameBegun = false;
    }

    // ------------------------------------------------------------------
    private void DrawImGui(ImDrawDataPtr dd, GraphicsDevice gd, CommandList cl)
    {
        // If there's nothing to draw, don't do anything.
        if (dd.CmdListsCount == 0 || dd.TotalVtxCount == 0) return;

        // --- 1. Resize and Update Buffers ---

        // Ensure our vertex buffer is large enough.
        var vbSize = (uint)(dd.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
        if (vbSize > _vb.SizeInBytes)
        {
            _vb.Dispose();
            // Double the size to avoid frequent re-allocations.
            _vb = gd.ResourceFactory.CreateBuffer(new BufferDescription(vbSize * 2,
                BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        }

        // Ensure our index buffer is large enough.
        var ibSize = (uint)(dd.TotalIdxCount * sizeof(ushort));
        if (ibSize > _ib.SizeInBytes)
        {
            _ib.Dispose();
            // Double the size.
            _ib = gd.ResourceFactory.CreateBuffer(new BufferDescription(ibSize * 2,
                BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        }

        // Upload the vertex and index data for all command lists into our single GPU buffers.
        uint vOffset = 0;
        uint iOffset = 0;
        for (var n = 0; n < dd.CmdListsCount; ++n)
        {
            var clist = dd.CmdLists[n];

            // Copy vertex data
            cl.UpdateBuffer(
                _vb,
                vOffset * (uint)Unsafe.SizeOf<ImDrawVert>(),
                clist.VtxBuffer.Data,
                (uint)(clist.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));

            // Copy index data
            cl.UpdateBuffer(
                _ib,
                iOffset * sizeof(ushort),
                clist.IdxBuffer.Data,
                (uint)(clist.IdxBuffer.Size * sizeof(ushort)));

            vOffset += (uint)clist.VtxBuffer.Size;
            iOffset += (uint)clist.IdxBuffer.Size;
        }

        // --- 2. Setup Graphics State ---

        cl.SetVertexBuffer(0, _vb);
        cl.SetIndexBuffer(_ib, IndexFormat.UInt16);
        cl.SetPipeline(_pipe);

        // Update the projection matrix uniform
        var io = ImGui.GetIO();
        var mvp = Matrix4x4.CreateOrthographicOffCenter(
            0f,
            io.DisplaySize.X,
            io.DisplaySize.Y,
            0f,
            -1.0f,
            1.0f);

        gd.UpdateBuffer(_ub, 0, ref mvp);

        // Scale clip rectangles for HiDPI displays.
        dd.ScaleClipRects(io.DisplayFramebufferScale);

        // --- 3. Render all Command Lists ---

        var baseVtx = 0;
        var baseIdx = 0;
        for (var n = 0; n < dd.CmdListsCount; ++n)
        {
            var clist = dd.CmdLists[n];
            for (var cmd_i = 0; cmd_i < clist.CmdBuffer.Size; ++cmd_i)
            {
                var pcmd = clist.CmdBuffer[cmd_i];

                // Skip user-defined callbacks.
                if (pcmd.UserCallback != IntPtr.Zero) continue;

                // Set the correct texture resource set for this draw command.
                if (_setsById.TryGetValue(pcmd.TextureId, out var resourceSet))
                    cl.SetGraphicsResourceSet(0, resourceSet);
                else
                    // Fallback to font texture if texture not found
                    cl.SetGraphicsResourceSet(0, _set);

                // Set the scissor rectangle to clip rendering.
                cl.SetScissorRect(
                    0,
                    (uint)pcmd.ClipRect.X,
                    (uint)pcmd.ClipRect.Y,
                    (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                    (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y));

                // Issue the draw call.
                cl.DrawIndexed(
                    pcmd.ElemCount,
                    1,
                    pcmd.IdxOffset + (uint)baseIdx,
                    (int)pcmd.VtxOffset + baseVtx,
                    0);
            }

            // Update the base offsets for the next command list.
            baseIdx += clist.IdxBuffer.Size;
            baseVtx += clist.VtxBuffer.Size;
        }
    }

    // ------------------------------------------------------------------
    private void SetPerFrameImGuiData(float dt)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_winW / _scale.X, _winH / _scale.Y);
        io.DisplayFramebufferScale = _scale;
        io.DeltaTime = dt;
    }

    private void UpdateInputs(InputSnapshot s)
    {
        var io = ImGui.GetIO();
        io.MouseDown[0] = s.IsMouseDown(MouseButton.Left);
        io.MouseDown[1] = s.IsMouseDown(MouseButton.Right);
        io.MouseDown[2] = s.IsMouseDown(MouseButton.Middle);
        io.MouseWheel = s.WheelDelta;
        io.MousePos = s.MousePosition;

        foreach (var c in s.KeyCharPresses) io.AddInputCharacter(c);
        foreach (var e in s.KeyEvents)
            if (e.Key == Key.ControlLeft || e.Key == Key.ControlRight)
                io.AddKeyEvent(ImGuiKey.ModCtrl, e.Down);
            else if (e.Key == Key.ShiftLeft || e.Key == Key.ShiftRight)
                io.AddKeyEvent(ImGuiKey.ModShift, e.Down);
            else if (e.Key == Key.AltLeft || e.Key == Key.AltRight)
                io.AddKeyEvent(ImGuiKey.ModAlt, e.Down);
            else if (e.Key == Key.WinLeft || e.Key == Key.WinRight)
                io.AddKeyEvent(ImGuiKey.ModSuper, e.Down);
            else
                io.AddKeyEvent(ConvertKeyToImGuiKey(e.Key), e.Down);
    }

    private ImGuiKey ConvertKeyToImGuiKey(Key key)
    {
        return key switch
        {
            Key.Tab => ImGuiKey.Tab,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.Delete => ImGuiKey.Delete,
            Key.BackSpace => ImGuiKey.Backspace,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Space => ImGuiKey.Space,
            Key.A => ImGuiKey.A,
            Key.C => ImGuiKey.C,
            Key.V => ImGuiKey.V,
            Key.X => ImGuiKey.X,
            Key.Y => ImGuiKey.Y,
            Key.Z => ImGuiKey.Z,
            _ => ImGuiKey.None
        };
    }

    // ------------------------------------------------------------------
    // Backend-specific shaders with proper entry points
    private (string vs, string fs, string vsEntry, string fsEntry) GetBackendSpecificShaders(GraphicsBackend backend)
    {
        switch (backend)
        {
            case GraphicsBackend.Direct3D11:
                return (VsHlsl, FsHlsl, "VS", "PS");
            case GraphicsBackend.Metal:
                return (VsMetalFixed, FsMetalFixed, "main0", "main0");
            case GraphicsBackend.Vulkan:
                return (VsSpirv450, FsSpirv450, "main", "main");
            case GraphicsBackend.OpenGL:
            case GraphicsBackend.OpenGLES:
            default:
                // For OpenGL, use GLSL 330 which is more compatible
                return (VsGlsl330, FsGlsl330, "main", "main");
        }
    }
}