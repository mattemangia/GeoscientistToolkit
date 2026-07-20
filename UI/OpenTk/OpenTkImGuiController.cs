using System.Runtime.InteropServices;

using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using NumVector2 = System.Numerics.Vector2;

namespace GAIA.UI.OpenTk;

public sealed class ImGuiController : IDisposable
{
    // GLFW reports high-resolution wheels and touchpads as fractional offsets. On the devices
    // used with GAIA those offsets are roughly one third of the logical notch expected by ImGui,
    // which made both ordinary panel scrolling and viewer zoom feel unresponsive.
    internal const float MouseWheelSensitivity = 3f;

    private static readonly Keys[] AllKeys = (Keys[])Enum.GetValues(typeof(Keys));

    // Fonts belong to the atlas of the context that built them, and each pop-out window runs
    // its own controller and context, so callers must look up the font for whichever context
    // is current rather than caching a single ImFontPtr.
    private static readonly Dictionary<IntPtr, ImGuiController> ByContext = new();
    private static readonly ushort[] UiGlyphRanges =
    [
        0x0020, 0x00FF, // Basic Latin + Latin-1 Supplement: accents, degree, micro, superscript 1/2/3.
        0x0100, 0x017F, // Latin Extended-A.
        0x0300, 0x036F, // Combining diacritics used by a few scientific labels.
        0x0370, 0x03FF, // Greek and Coptic: sigma, lambda, kappa, phi, nu, rho, gamma, etc.
        0x0400, 0x04FF, // Cyrillic fallback for copied labels containing homoglyphs.
        0x1D00, 0x1D7F, // Phonetic extensions: modifier/subscript letters used in symbols.
        0x1E00, 0x1EFF, // Latin Extended Additional.
        0x2000, 0x206F, // General punctuation: en/em dash, quotes, bullets, ellipsis.
        0x2070, 0x209F, // Superscripts and subscripts, including chemical-formula digits.
        0x20A0, 0x20CF, // Currency symbols (euro, etc.).
        0x2100, 0x214F, // Letterlike symbols: info (i), trademark, numero, script small l.
        0x2190, 0x21FF, // Arrows.
        0x2200, 0x22FF, // Mathematical operators.
        0x2300, 0x23FF, // Misc technical symbols.
        0x2500, 0x257F, // Box drawing.
        0x25A0, 0x25FF, // Geometric shapes.
        0x2600, 0x26FF, // Misc symbols.
        0x2700, 0x27BF, // Dingbats.
        0x27C0, 0x27EF, // Misc mathematical symbols-A.
        0x27F0, 0x27FF, // Supplemental arrows-A.
        0x2900, 0x297F, // Supplemental arrows-B.
        0xA720, 0xA7FF, // Latin Extended-D: geological era symbols such as the Cambrian stroked C.
        0xFE00, 0xFE0F, // Variation selectors, so emoji-style selectors do not fall back to '?'.
        0
    ];
    private static readonly GCHandle UiGlyphRangesHandle = GCHandle.Alloc(UiGlyphRanges, GCHandleType.Pinned);

    private bool _frameBegun;
    private int _vertexArray;
    private int _vertexBuffer;
    private int _vertexBufferSize;
    private int _indexBuffer;
    private int _indexBufferSize;
    private int _fontTexture;
    private int _shader;
    private int _shaderFontTextureLocation;
    private int _shaderProjectionMatrixLocation;
    private int _windowWidth;
    private int _windowHeight;
    private int _framebufferWidth;
    private int _framebufferHeight;
    private readonly IntPtr _context;
    private ImFontPtr _titleFont;

    /// <summary>Larger face for panel headers. Scaling the 14px atlas instead would blur it.</summary>
    public ImFontPtr TitleFont => _titleFont;

    /// <summary>Whether <see cref="TitleFont"/> was built; false when the atlas fell back to the
    /// built-in font. Callers test this instead of the pointer, which needs an unsafe context.</summary>
    public bool HasTitleFont { get; private set; }

    /// <summary>The controller owning the ImGui context that is current on this thread, if any.</summary>
    public static ImGuiController ForCurrentContext()
    {
        var current = ImGui.GetCurrentContext();
        return current != IntPtr.Zero && ByContext.TryGetValue(current, out var controller) ? controller : null;
    }

    /// <summary>
    /// The ImGui context this controller created. Useful for multi-window setups
    /// where each window/thread needs to switch ImGui current-context before issuing
    /// ImGui commands (detached viewport windows in particular).
    /// </summary>
    public IntPtr Context => _context;

    public ImGuiController(int width, int height, int framebufferWidth, int framebufferHeight)
    {
        _windowWidth = Math.Max(1, width);
        _windowHeight = Math.Max(1, height);
        _framebufferWidth = Math.Max(1, framebufferWidth);
        _framebufferHeight = Math.Max(1, framebufferHeight);

        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigWindowsMoveFromTitleBarOnly = true;
        ConfigureFonts(io);

        // Padding, rounding and the rest are in pixels too, so they have to follow the font.
        // ThemeManager only ever writes style colours, so this survives ApplyTheme.
        if (Math.Abs(UiScaling.Scale - 1f) > 0.001f) ImGui.GetStyle().ScaleAllSizes(UiScaling.Scale);

        ByContext[_context] = this;

        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);
        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void WindowResized(int width, int height, int framebufferWidth, int framebufferHeight)
    {
        _windowWidth = Math.Max(1, width);
        _windowHeight = Math.Max(1, height);
        _framebufferWidth = Math.Max(1, framebufferWidth);
        _framebufferHeight = Math.Max(1, framebufferHeight);
    }

    public void PressChar(char keyChar)
    {
        if (keyChar != '\t')
        {
            ImGui.GetIO().AddInputCharacter(keyChar);
        }
    }

    public void Update(NativeWindow wnd, float dt)
    {
        // Make sure ImGui issues commands against the context this controller created
        // (relevant in the multi-window detached viewport setup where two controllers
        // share the same ImGui process-global current-context slot).
        ImGui.SetCurrentContext(_context);

        if (_frameBegun)
        {
            ImGui.Render();
        }

        WindowResized(wnd.ClientSize.X, wnd.ClientSize.Y, wnd.FramebufferSize.X, wnd.FramebufferSize.Y);
        SetPerFrameImGuiData(dt);
        UpdateImGuiInput(wnd);

        _frameBegun = true;
        ImGui.NewFrame();
    }

    /// <summary>
    /// Begin a new ImGui frame against an offscreen render target (FBO). Used by
    /// the detached-viewport offscreen renderer where there's no associated
    /// <see cref="NativeWindow"/> — the controller is driven entirely from main
    /// thread per-frame data and never reads OS-level input.
    /// </summary>
    public void UpdateOffscreen(int width, int height, float dt)
    {
        ImGui.SetCurrentContext(_context);

        if (_frameBegun)
        {
            ImGui.Render();
        }

        WindowResized(width, height, width, height);
        SetPerFrameImGuiData(dt);

        // Offscreen render targets don't receive user input. Camera dragging
        // and mouse picking inside the detached viewport are handled on the
        // worker thread, which mutates ViewportState directly.
        var io = ImGui.GetIO();
        io.MouseDown[0] = false;
        io.MouseDown[1] = false;
        io.MouseDown[2] = false;
        io.MousePos = new NumVector2(-1f, -1f);
        io.MouseWheel = 0f;
        io.MouseWheelH = 0f;
        io.KeyCtrl = false;
        io.KeyAlt = false;
        io.KeyShift = false;
        io.KeySuper = false;

        _frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render()
    {
        if (!_frameBegun)
        {
            return;
        }

        ImGui.SetCurrentContext(_context);
        _frameBegun = false;
        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    public void Dispose()
    {
        ByContext.Remove(_context);
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteBuffer(_indexBuffer);
        GL.DeleteVertexArray(_vertexArray);
        GL.DeleteTexture(_fontTexture);
        GL.DeleteProgram(_shader);
        ImGui.DestroyContext(_context);
    }

    private void SetPerFrameImGuiData(float dt)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new NumVector2(_windowWidth, _windowHeight);
        io.DisplayFramebufferScale = new NumVector2(
            _windowWidth > 0 ? (float)_framebufferWidth / _windowWidth : 1f,
            _windowHeight > 0 ? (float)_framebufferHeight / _windowHeight : 1f);
        io.DeltaTime = dt <= 0 ? 1f / 60f : dt;
    }

    private void UpdateImGuiInput(NativeWindow wnd)
    {
        var io = ImGui.GetIO();
        var mouse = wnd.MouseState;
        var keyboard = wnd.KeyboardState;

        io.MouseDown[0] = mouse.IsButtonDown(MouseButton.Left);
        io.MouseDown[1] = mouse.IsButtonDown(MouseButton.Right);
        io.MouseDown[2] = mouse.IsButtonDown(MouseButton.Middle);
        io.MousePos = new NumVector2(mouse.X, mouse.Y);
        var wheelX = NormalizeMouseWheelDelta(mouse.ScrollDelta.X);
        var wheelY = NormalizeMouseWheelDelta(mouse.ScrollDelta.Y);
        if (wheelX != 0f || wheelY != 0f)
            io.AddMouseWheelEvent(wheelX, wheelY);

        io.KeyCtrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        io.KeyAlt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        io.KeyShift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        io.KeySuper = keyboard.IsKeyDown(Keys.LeftSuper) || keyboard.IsKeyDown(Keys.RightSuper);

        foreach (var key in AllKeys)
        {
            if (key == Keys.Unknown)
            {
                continue;
            }

            var imKey = TranslateKey(key);
            if (imKey != ImGuiKey.None)
            {
                io.AddKeyEvent(imKey, keyboard.IsKeyDown(key));
            }
        }
    }

    internal static float NormalizeMouseWheelDelta(float delta) => delta * MouseWheelSensitivity;

    private static ImGuiKey TranslateKey(Keys key)
    {
        var keyValue = (int)key;

        if (keyValue >= (int)Keys.A && keyValue <= (int)Keys.Z)
        {
            return (ImGuiKey)((int)ImGuiKey.A + (keyValue - (int)Keys.A));
        }

        if (keyValue >= (int)Keys.D0 && keyValue <= (int)Keys.D9)
        {
            return (ImGuiKey)((int)ImGuiKey._0 + (keyValue - (int)Keys.D0));
        }

        if (keyValue >= (int)Keys.F1 && keyValue <= (int)Keys.F12)
        {
            return (ImGuiKey)((int)ImGuiKey.F1 + (keyValue - (int)Keys.F1));
        }

        return key switch
        {
            Keys.Tab => ImGuiKey.Tab,
            Keys.Left => ImGuiKey.LeftArrow,
            Keys.Right => ImGuiKey.RightArrow,
            Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow,
            Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown,
            Keys.Home => ImGuiKey.Home,
            Keys.End => ImGuiKey.End,
            Keys.Delete => ImGuiKey.Delete,
            Keys.Backspace => ImGuiKey.Backspace,
            Keys.Enter => ImGuiKey.Enter,
            Keys.Escape => ImGuiKey.Escape,
            Keys.Space => ImGuiKey.Space,
            Keys.LeftShift => ImGuiKey.LeftShift,
            Keys.RightShift => ImGuiKey.RightShift,
            Keys.LeftControl => ImGuiKey.LeftCtrl,
            Keys.RightControl => ImGuiKey.RightCtrl,
            Keys.LeftAlt => ImGuiKey.LeftAlt,
            Keys.RightAlt => ImGuiKey.RightAlt,
            Keys.LeftSuper => ImGuiKey.LeftSuper,
            Keys.RightSuper => ImGuiKey.RightSuper,
            Keys.Comma => ImGuiKey.Comma,
            Keys.Period => ImGuiKey.Period,
            Keys.Slash => ImGuiKey.Slash,
            Keys.Semicolon => ImGuiKey.Semicolon,
            Keys.Minus => ImGuiKey.Minus,
            Keys.Equal => ImGuiKey.Equal,
            Keys.LeftBracket => ImGuiKey.LeftBracket,
            Keys.RightBracket => ImGuiKey.RightBracket,
            Keys.Backslash => ImGuiKey.Backslash,
            Keys.GraveAccent => ImGuiKey.GraveAccent,
            Keys.Apostrophe => ImGuiKey.Apostrophe,
            _ => ImGuiKey.None
        };
    }

    /// <summary>Builds the atlas, adding the panel-title face when a TTF is available.</summary>
    private void ConfigureFonts(ImGuiIOPtr io)
    {
        var fontPath = FindUiFontPath();
        if (!string.IsNullOrWhiteSpace(fontPath))
        {
            try
            {
                // size_pixels is an ascent-descent span rather than the em, and that span is
                // face-specific, so convert to keep one setting meaning one apparent size on
                // every platform. See TrueTypeMetrics.
                var spanPerEm = TrueTypeMetrics.SpanPerEm(fontPath);

                // The first font added stays the default for the rest of the UI.
                io.Fonts.AddFontFromFileTTF(fontPath, UiScaling.UiFontEmPixels * spanPerEm, null,
                    UiGlyphRangesHandle.AddrOfPinnedObject());
                _titleFont = io.Fonts.AddFontFromFileTTF(fontPath, UiScaling.TitleFontEmPixels * spanPerEm, null,
                    UiGlyphRangesHandle.AddrOfPinnedObject());
                HasTitleFont = true;
                return;
            }
            catch
            {
            }
        }

        // Fallback face: bitmap-only, so it cannot be rasterised larger. Scale it instead, or the
        // UI would come back tiny on the very machines that already failed to find a font.
        io.Fonts.AddFontDefault();
        io.FontGlobalScale = Math.Max(1f, UiScaling.Scale);
        HasTitleFont = false;
    }

    internal static bool IsUiGlyphCovered(int codePoint)
    {
        if (codePoint < 0 || codePoint > ushort.MaxValue) return false;

        for (int i = 0; i + 1 < UiGlyphRanges.Length && UiGlyphRanges[i] != 0; i += 2)
        {
            if (codePoint >= UiGlyphRanges[i] && codePoint <= UiGlyphRanges[i + 1])
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindUiFontPath()
    {
        string[] candidates;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            candidates =
            [
                Path.Combine(windowsFonts, "segoeui.ttf"),
                Path.Combine(windowsFonts, "arial.ttf"),
                Path.Combine(windowsFonts, "tahoma.ttf")
            ];
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates =
            [
                "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf",
                "/System/Library/Fonts/Supplemental/Helvetica.ttc"
            ];
        }
        else
        {
            candidates =
            [
                // Unlike NotoSans-Regular, DejaVu Sans contains the arrows,
                // box drawing and scientific symbols requested by UiGlyphRanges.
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
                "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf"
            ];
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private void CreateDeviceResources()
    {
        _vertexBufferSize = 10000;
        _indexBufferSize = 2000;

        _vertexArray = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();
        _indexBuffer = GL.GenBuffer();

        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        const string vertexSource = @"#version 330 core
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
layout (location = 2) in vec4 Color;
uniform mat4 projection_matrix;
out vec2 Frag_UV;
out vec4 Frag_Color;
void main()
{
    Frag_UV = UV;
    Frag_Color = Color;
    gl_Position = projection_matrix * vec4(Position.xy, 0, 1);
}";

        const string fragmentSource = @"#version 330 core
in vec2 Frag_UV;
in vec4 Frag_Color;
uniform sampler2D in_texture;
out vec4 color;
void main()
{
    color = Frag_Color * texture(in_texture, Frag_UV.st);
}";

        _shader = CreateProgram(vertexSource, fragmentSource);
        _shaderProjectionMatrixLocation = GL.GetUniformLocation(_shader, "projection_matrix");
        _shaderFontTextureLocation = GL.GetUniformLocation(_shader, "in_texture");

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.EnableVertexAttribArray(2);

        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 20, 0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 20, 8);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, 20, 16);

        CreateFontTexture();
    }

    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out var width, out var height, out _);

        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private static int CreateProgram(string vertexCode, string fragmentCode)
    {
        int vertex = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertex, vertexCode);
        GL.CompileShader(vertex);

        int fragment = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragment, fragmentCode);
        GL.CompileShader(fragment);

        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        return program;
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawData)
    {
        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0)
        {
            return;
        }

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        GL.Viewport(0, 0, fbWidth, fbHeight);
        var lastActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        var lastProgram = GL.GetInteger(GetPName.CurrentProgram);
        var lastTexture = GL.GetInteger(GetPName.TextureBinding2D);
        var lastArrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);
        var lastVertexArray = GL.GetInteger(GetPName.VertexArrayBinding);

        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        void SetupRenderState()
        {
            GL.UseProgram(_shader);
            GL.Uniform1(_shaderFontTextureLocation, 0);

            Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
                drawData.DisplayPos.X,
                drawData.DisplayPos.X + drawData.DisplaySize.X,
                drawData.DisplayPos.Y + drawData.DisplaySize.Y,
                drawData.DisplayPos.Y,
                -1f,
                1f);

            GL.UniformMatrix4(_shaderProjectionMatrixLocation, false, ref projection);
            GL.BindVertexArray(_vertexArray);
        }

        SetupRenderState();

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            int vertexSize = cmdList.VtxBuffer.Size * sizeof(ImDrawVert);
            if (vertexSize > _vertexBufferSize)
            {
                _vertexBufferSize = (int)(vertexSize * 1.5f);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }

            int indexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
            if (indexSize > _indexBufferSize)
            {
                _indexBufferSize = (int)(indexSize * 1.5f);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertexSize, (IntPtr)cmdList.VtxBuffer.Data);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, indexSize, (IntPtr)cmdList.IdxBuffer.Data);

            for (int cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
            {
                var pcmd = cmdList.CmdBuffer[cmdIndex];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    SetupRenderState();
                    continue;
                }

                GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);
                GL.Scissor(
                    (int)pcmd.ClipRect.X,
                    (int)(fbHeight - pcmd.ClipRect.W),
                    (int)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                    (int)(pcmd.ClipRect.W - pcmd.ClipRect.Y));

                // Index ranges must come from the command itself. Accumulating ElemCount assumes
                // commands are contiguous, which breaks as soon as ImGui splits or merges channels
                // (popups, tables): every later command then draws a neighbour's geometry.
                GL.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    (int)pcmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (IntPtr)(pcmd.IdxOffset * sizeof(ushort)),
                    (int)pcmd.VtxOffset);
            }
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.BindTexture(TextureTarget.Texture2D, lastTexture);
        GL.UseProgram(lastProgram);
        GL.BindBuffer(BufferTarget.ArrayBuffer, lastArrayBuffer);
        GL.BindVertexArray(lastVertexArray);
        GL.ActiveTexture((TextureUnit)lastActiveTexture);
    }
}
