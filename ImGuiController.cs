// GeoscientistToolkit/ImGuiController.cs
// This is the definitive, working version.
// It fixes the compilation error by passing the required ImDrawDataPtr to RenderImDrawData.

using ImGuiNET;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Veldrid;

namespace GeoscientistToolkit
{
    public class ImGuiController : IDisposable
    {
        private GraphicsDevice _gd;
        private bool _frameBegun;
        
        private DeviceBuffer _vertexBuffer, _indexBuffer, _projMatrixBuffer;
        private Texture _fontTexture;
        private Shader _vertexShader, _fragmentShader;
        private ResourceLayout _layout;
        private Pipeline _pipeline;
        private ResourceSet _mainResourceSet;

        private IntPtr _fontAtlasID = (IntPtr)1;
        private int _windowWidth, _windowHeight;
        private Vector2 _scaleFactor = Vector2.One;
        private bool _controlDown, _shiftDown, _altDown, _winKeyDown;
        
        private const uint TextureBinding  = 1;
        private const uint SamplerBinding  = 2;

        public ImGuiController(GraphicsDevice gd, OutputDescription outputDescription, int width, int height)
        {
            _gd = gd; _windowWidth = width; _windowHeight = height;
            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
            CreateDeviceResources(gd, outputDescription);
            SetPerFrameImGuiData(1f / 60f);
            ImGui.NewFrame();
            _frameBegun = true;
        }

        public void Render(GraphicsDevice gd, CommandList cl)
        {
            if (_frameBegun)
            {
                ImGui.Render();
                // THIS IS THE FIX: Pass the draw data to the method.
                RenderImDrawData(ImGui.GetDrawData(), gd, cl);
                _frameBegun = false;
            }
        }
        
        #region RUN
        public void CreateDeviceResources(GraphicsDevice gd, OutputDescription outputDescription)
{
    _gd = gd;
    ResourceFactory factory = gd.ResourceFactory;

    // --- GPU buffers -------------------------------------------------------
    _vertexBuffer = factory.CreateBuffer(
        new BufferDescription(10000, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
    _indexBuffer  = factory.CreateBuffer(
        new BufferDescription(2000,  BufferUsage.IndexBuffer  | BufferUsage.Dynamic));
    _projMatrixBuffer = factory.CreateBuffer(
        new BufferDescription(64,    BufferUsage.UniformBuffer | BufferUsage.Dynamic));

    // --- Shaders -----------------------------------------------------------
    (string vsSource, string fsSource) = GetShaders(gd.BackendType);
    _vertexShader   = factory.CreateShader(
        new ShaderDescription(ShaderStages.Vertex,   Encoding.UTF8.GetBytes(vsSource), "VS"));
    _fragmentShader = factory.CreateShader(
        new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fsSource), "FS"));

    // --- Vertex layout -----------------------------------------------------
    var vertexLayouts = new[]
    {
        new VertexLayoutDescription(
            new VertexElementDescription("in_position", VertexElementSemantic.Position,          VertexElementFormat.Float2),
            new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("in_color",    VertexElementSemantic.Color,             VertexElementFormat.Byte4_Norm))
    };

    // --- Resource layout (single set: UBO + texture + sampler) ------------
    _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
        new ResourceLayoutElementDescription(
            "ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
        new ResourceLayoutElementDescription(
            "MainTexture",            ResourceKind.TextureReadOnly, ShaderStages.Fragment),
        new ResourceLayoutElementDescription(
            "MainSampler",            ResourceKind.Sampler,         ShaderStages.Fragment)));

    // --- Pipeline ----------------------------------------------------------
    _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
        BlendStateDescription.SingleAlphaBlend,
        DepthStencilStateDescription.Disabled,
        new RasterizerStateDescription(
            FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise,
            depthClipEnabled: true, scissorTestEnabled: true),
        PrimitiveTopology.TriangleList,
        new ShaderSetDescription(vertexLayouts, new[] { _vertexShader, _fragmentShader }),
        new[] { _layout },
        outputDescription,
        ResourceBindingModel.Improved));      // same model as GraphicsDevice

    // --- Font texture + resource-set --------------------------------------
    RecreateFontDeviceTexture(gd);           // this creates _fontTexture and _mainResourceSet
}
        
        public void RecreateFontDeviceTexture(GraphicsDevice gd)
        {
            // --- get RGBA pixels from ImGui ---------------------------------------
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels,
                out int width, out int height, out int bytesPerPixel);
            io.Fonts.SetTexID(_fontAtlasID);

            // --- (re)create GPU texture -------------------------------------------
            _fontTexture?.Dispose();
            _fontTexture = gd.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    (uint)width, (uint)height, mipLevels: 1, arrayLayers: 1,
                    PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

            gd.UpdateTexture(
                _fontTexture, pixels,
                (uint)(bytesPerPixel * width * height),
                0, 0, 0,
                (uint)width, (uint)height, depth: 1,
                mipLevel: 0, arrayLayer: 0);

            // --- (re)create resource-set ------------------------------------------
            _mainResourceSet?.Dispose();
            _mainResourceSet = gd.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(_layout, _projMatrixBuffer, _fontTexture, gd.Aniso4xSampler));

            io.Fonts.ClearTexData();                 // pixels no longer needed on CPU
        }

        private void RenderImDrawData(ImDrawDataPtr draw_data, GraphicsDevice gd, CommandList cl)
        {
            if (draw_data.CmdListsCount == 0) return;
            
            uint totalVBSize = (uint)(draw_data.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
            if (totalVBSize > _vertexBuffer.SizeInBytes)
            {
                _vertexBuffer.Dispose();
                _vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalVBSize * 1.5f), BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            }

            uint totalIBSize = (uint)(draw_data.TotalIdxCount * sizeof(ushort));
            if (totalIBSize > _indexBuffer.SizeInBytes)
            {
                _indexBuffer.Dispose();
                _indexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalIBSize * 1.5f), BufferUsage.IndexBuffer | BufferUsage.Dynamic));
            }

            uint vertexOffset = 0, indexOffset = 0;
            for (int i = 0; i < draw_data.CmdListsCount; i++)
            {
                ImDrawListPtr cmd_list = draw_data.CmdLists[i];
                cl.UpdateBuffer(_vertexBuffer, vertexOffset, cmd_list.VtxBuffer.Data, (uint)(cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));
                cl.UpdateBuffer(_indexBuffer, indexOffset, cmd_list.IdxBuffer.Data, (uint)(cmd_list.IdxBuffer.Size * sizeof(ushort)));
                vertexOffset += (uint)(cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>());
                indexOffset += (uint)(cmd_list.IdxBuffer.Size * sizeof(ushort));
            }
            
            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _mainResourceSet);
            
            var io = ImGui.GetIO();
            Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0.0f, -1.0f, 1.0f);
            gd.UpdateBuffer(_projMatrixBuffer, 0, ref mvp);
            
            draw_data.ScaleClipRects(io.DisplayFramebufferScale);
            
            int vtx_offset = 0, idx_offset = 0;
            for (int n = 0; n < draw_data.CmdListsCount; n++)
            {
                ImDrawListPtr cmd_list = draw_data.CmdLists[n];
                for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback == IntPtr.Zero)
                    {
                       

                        cl.SetScissorRect(0, (uint)pcmd.ClipRect.X, (uint)pcmd.ClipRect.Y, (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X), (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y));
                        cl.DrawIndexed(pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)idx_offset, (int)pcmd.VtxOffset + vtx_offset, 0);
                    }
                }
                idx_offset += cmd_list.IdxBuffer.Size;
                vtx_offset += cmd_list.VtxBuffer.Size;      
            }
        }
        
        public void WindowResized(int width, int height) { _windowWidth = width; _windowHeight = height; }
        public void Update(float deltaSeconds, InputSnapshot snapshot) { if (_frameBegun) { ImGui.Render(); } SetPerFrameImGuiData(deltaSeconds); UpdateImGuiInput(snapshot); _frameBegun = true; ImGui.NewFrame(); }
        private void UpdateImGuiInput(InputSnapshot snapshot) { var io = ImGui.GetIO(); io.MouseDown[0] = snapshot.IsMouseDown(MouseButton.Left); io.MouseDown[1] = snapshot.IsMouseDown(MouseButton.Right); io.MouseDown[2] = snapshot.IsMouseDown(MouseButton.Middle); io.MousePos = snapshot.MousePosition; io.MouseWheel = snapshot.WheelDelta; var keyCharPresses = snapshot.KeyCharPresses; for (int i = 0; i < keyCharPresses.Count; i++) { io.AddInputCharacter(keyCharPresses[i]); } var keyEvents = snapshot.KeyEvents; for (int i = 0; i < keyEvents.Count; i++) { var keyEvent = keyEvents[i]; var imguikey = TranslateKey(keyEvent.Key); if (imguikey != ImGuiKey.None) { io.AddKeyEvent(imguikey, keyEvent.Down); } if (keyEvent.Key == Key.ControlLeft || keyEvent.Key == Key.ControlRight) { _controlDown = keyEvent.Down; } if (keyEvent.Key == Key.ShiftLeft || keyEvent.Key == Key.ShiftRight) { _shiftDown = keyEvent.Down; } if (keyEvent.Key == Key.AltLeft || keyEvent.Key == Key.AltRight) { _altDown = keyEvent.Down; } if (keyEvent.Key == Key.WinLeft || keyEvent.Key == Key.WinRight) { _winKeyDown = keyEvent.Down; } } io.KeyCtrl = _controlDown; io.KeyShift = _shiftDown; io.KeyAlt = _altDown; io.KeySuper = _winKeyDown; }
        private void SetPerFrameImGuiData(float deltaSeconds) { var io = ImGui.GetIO(); io.DisplaySize = new Vector2(_windowWidth / _scaleFactor.X, _windowHeight / _scaleFactor.Y); io.DisplayFramebufferScale = _scaleFactor; io.DeltaTime = deltaSeconds; }
        private static ImGuiKey TranslateKey(Key key) { if (key >= Key.F1 && key <= Key.F24) return ImGuiKey.F1 + (key - Key.F1); if (key >= Key.Keypad0 && key <= Key.Keypad9) return ImGuiKey.Keypad0 + (key - Key.Keypad0); if (key >= Key.Number0 && key <= Key.Number9) return ImGuiKey._0 + (key - Key.Number0); if (key >= Key.A && key <= Key.Z) return ImGuiKey.A + (key - Key.A); switch (key) { case Key.Tab: return ImGuiKey.Tab; case Key.Left: return ImGuiKey.LeftArrow; case Key.Right: return ImGuiKey.RightArrow; case Key.Up: return ImGuiKey.UpArrow; case Key.Down: return ImGuiKey.DownArrow; case Key.PageUp: return ImGuiKey.PageUp; case Key.PageDown: return ImGuiKey.PageDown; case Key.Home: return ImGuiKey.Home; case Key.End: return ImGuiKey.End; case Key.Insert: return ImGuiKey.Insert; case Key.Delete: return ImGuiKey.Delete; case Key.BackSpace: return ImGuiKey.Backspace; case Key.Space: return ImGuiKey.Space; case Key.Enter: return ImGuiKey.Enter; case Key.Escape: return ImGuiKey.Escape; case Key.Quote: return ImGuiKey.Apostrophe; case Key.Comma: return ImGuiKey.Comma; case Key.Minus: return ImGuiKey.Minus; case Key.Period: return ImGuiKey.Period; case Key.Slash: return ImGuiKey.Slash; case Key.Semicolon: return ImGuiKey.Semicolon; case Key.Plus: return ImGuiKey.Equal; case Key.BracketLeft: return ImGuiKey.LeftBracket; case Key.BackSlash: return ImGuiKey.Backslash; case Key.BracketRight: return ImGuiKey.RightBracket; case Key.Grave: return ImGuiKey.GraveAccent; case Key.CapsLock: return ImGuiKey.CapsLock; case Key.ScrollLock: return ImGuiKey.ScrollLock; case Key.NumLock: return ImGuiKey.NumLock; case Key.PrintScreen: return ImGuiKey.PrintScreen; case Key.Pause: return ImGuiKey.Pause; case Key.KeypadDecimal: return ImGuiKey.KeypadDecimal; case Key.KeypadDivide: return ImGuiKey.KeypadDivide; case Key.KeypadMultiply: return ImGuiKey.KeypadMultiply; case Key.KeypadSubtract: return ImGuiKey.KeypadSubtract; case Key.KeypadAdd: return ImGuiKey.KeypadAdd; case Key.KeypadEnter: return ImGuiKey.KeypadEnter; case Key.ControlLeft: return ImGuiKey.LeftCtrl; case Key.ShiftLeft: return ImGuiKey.LeftShift; case Key.AltLeft: return ImGuiKey.LeftAlt; case Key.WinLeft: return ImGuiKey.LeftSuper; case Key.ControlRight: return ImGuiKey.RightCtrl; case Key.ShiftRight: return ImGuiKey.RightShift; case Key.AltRight: return ImGuiKey.RightAlt; case Key.WinRight: return ImGuiKey.RightSuper; case Key.Menu: return ImGuiKey.Menu; default: return ImGuiKey.None; } }
        public void Dispose() {  _mainResourceSet.Dispose(); _vertexBuffer.Dispose(); _indexBuffer.Dispose(); _projMatrixBuffer.Dispose(); _fontTexture.Dispose(); _vertexShader.Dispose(); _fragmentShader.Dispose(); _layout.Dispose();  _pipeline.Dispose(); }
        #endregion

        #region Shader Definitions
        private (string, string) GetShaders(GraphicsBackend backend) { switch (backend) { case GraphicsBackend.Metal: return (VertexShaderMetal, FragmentShaderMetal); default: return (VertexShaderGlsl, FragmentShaderGlsl); } }
        private const string VertexShaderGlsl = @"#version 450
layout(location=0) in vec2 in_position; layout(location=1) in vec2 in_texCoord; layout(location=2) in vec4 in_color;
layout(location=0) out vec2 fs_TexCoords; layout(location=1) out vec4 fs_Color;
layout(set=0, binding=0) uniform ProjectionMatrixBuffer { mat4 ProjectionMatrix; };
void main() { fs_TexCoords=in_texCoord; fs_Color=in_color; gl_Position=ProjectionMatrix*vec4(in_position.xy,0,1); }";
        private const string FragmentShaderGlsl = @"#version 450
layout(location=0) in  vec2 fs_TexCoords;
layout(location=1) in  vec4 fs_Color;
layout(location=0) out vec4 out_Color;

layout(set=0, binding=1) uniform texture2D MainTexture;
layout(set=0, binding=2) uniform sampler   MainSampler;

void main()
{
    out_Color = fs_Color * texture(sampler2D(MainTexture, MainSampler), fs_TexCoords);
}";
        private const string VertexShaderMetal = @"#include <metal_stdlib>
using namespace metal;
struct Uniforms { float4x4 projectionMatrix; };
struct VertexIn { float2 position [[attribute(0)]]; float2 texCoord [[attribute(1)]]; float4 color [[attribute(2)]]; };
struct VertexOut { float4 position [[position]]; float2 texCoord; float4 color; };
vertex VertexOut VS(VertexIn v_in [[stage_in]], constant Uniforms& uniforms [[buffer(0)]])
{ VertexOut v_out; v_out.position=uniforms.projectionMatrix*float4(v_in.position,0,1); v_out.texCoord=v_in.texCoord; v_out.color=v_in.color; return v_out; }";
        private const string FragmentShaderMetal = @"#include <metal_stdlib>
using namespace metal;
struct VertexOut { float4 position [[position]]; float2 texCoord; float4 color; };
fragment float4 FS(VertexOut v_in [[stage_in]],
                   texture2d<float> mainTexture [[texture(1)]],
                   sampler          mainSampler [[sampler(2)]])
{
    return v_in.color * mainTexture.sample(mainSampler, v_in.texCoord);
}";
        #endregion
    }
}