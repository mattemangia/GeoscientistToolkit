// GeoscientistToolkit/ImGuiController.cs (Updated with Context property)
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit
{
    public sealed class ImGuiController : IDisposable
    {
        private GraphicsDevice  _gd;
        private bool            _frameBegun;
        private IntPtr          _context;

        private DeviceBuffer _vb, _ib, _ub;
        private Texture      _fontTex;
        private ResourceSet  _set;

        private Shader        _vs, _fs;
        private ResourceLayout _layout;
        private Pipeline       _pipe;

        private readonly IntPtr _fontID = (IntPtr)1;
        private int  _winW, _winH;
        private Vector2 _scale = Vector2.One;
        
        private readonly Dictionary<TextureView, ResourceSet> _setsByView = new();
        private readonly Dictionary<Texture, TextureView> _autoViewsByTexture = new();
        private readonly Dictionary<IntPtr, ResourceSet> _setsById = new();
        private IntPtr _nextTextureId = (IntPtr)100;

        public IntPtr Context => _context;

        // ------------------------------------------------------------------
        public ImGuiController(GraphicsDevice gd,
                               OutputDescription fbDesc,
                               int width, int height)
        {
            _gd   = gd;
            _winW = width;
            _winH = height;

            _context = ImGui.CreateContext();
            ImGui.SetCurrentContext(_context);
            
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags  |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;

            CreateDeviceResources(gd, fbDesc);
            SetPerFrameImGuiData(1f / 60f);

            // Don't call NewFrame in constructor - let Update handle it
            _frameBegun = false;
        }

        // ------------------------------------------------------------------
        private void CreateDeviceResources(GraphicsDevice gd,
                                           OutputDescription fbDesc)
        {
            var factory = gd.ResourceFactory;

            _vb = factory.CreateBuffer(new BufferDescription(10_000,
                                  BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            _ib = factory.CreateBuffer(new BufferDescription(2_000,
                                  BufferUsage.IndexBuffer  | BufferUsage.Dynamic));
            _ub = factory.CreateBuffer(new BufferDescription(64,
                                  BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            (string vsSrc, string fsSrc) = GetShaders(gd.BackendType);
            _vs = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex,
                        Encoding.UTF8.GetBytes(vsSrc), "VS"));
            _fs = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment,
                        Encoding.UTF8.GetBytes(fsSrc), "FS"));

            var vLayout = new VertexLayoutDescription(
                new VertexElementDescription("in_position", VertexElementSemantic.Position,          VertexElementFormat.Float2),
                new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("in_color",    VertexElementSemantic.Color,             VertexElementFormat.Byte4_Norm));

            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Projection",  ResourceKind.UniformBuffer,   ShaderStages.Vertex),
                new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler,         ShaderStages.Fragment)));

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
                ResourceBindingModel.Improved));

            RecreateFontTexture(gd);      // creates _fontTex *and* _set
        }

        // ------------------------------------------------------------------
        public void WindowResized(int width, int height)
        {
            // store the new size so SetPerFrameImGuiData() will publish it
            _winW = width;
            _winH = height;

            // If you handle Hi-DPI later you can also update _scale here,
            // but for now the pixel size change alone is enough.
        }
        
        private void RecreateFontTexture(GraphicsDevice gd)
        {
            ImGui.SetCurrentContext(_context);
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int w, out int h, out int bpp);
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
        /// Gets or creates a handle for a texture to be displayed with ImGui.
        /// Pass the returned handle to Image() or ImageButton().
        /// </summary>
        public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView)
        {
            if (!_setsByView.TryGetValue(textureView, out ResourceSet resourceSet))
            {
                resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout, _ub, textureView, _gd.Aniso4xSampler));
                IntPtr newId = _nextTextureId++;
                _setsByView.Add(textureView, resourceSet);
                _setsById.Add(newId, resourceSet);
                return newId;
            }
            // Find the existing ID
            return _setsById.First(kvp => kvp.Value == resourceSet).Key;
        }
        /// <summary>
        /// Retrieves the shader texture binding for the given helper handle.
        /// </summary>
        public ResourceSet GetImageResourceSet(IntPtr imGuiBinding)
        {
            return _setsById[imGuiBinding];
        }
        /// <summary>
        /// Removes a particular texture binding.
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
            ImGui.SetCurrentContext(_context);
            
            if (_frameBegun) 
            {
                ImGui.Render();
            }

            SetPerFrameImGuiData(dt);
            UpdateInputs(snap);

            _frameBegun = true;
            ImGui.NewFrame();
        }

        // ------------------------------------------------------------------
        public void Render(GraphicsDevice gd, CommandList cl)
        {
            ImGui.SetCurrentContext(_context);
            
            if (!_frameBegun) return;
            
            ImGui.Render();
            DrawImGui(ImGui.GetDrawData(), gd, cl);
            _frameBegun = false;
        }

        // ------------------------------------------------------------------
        private void DrawImGui(ImDrawDataPtr dd, GraphicsDevice gd, CommandList cl)
        {
            // If there's nothing to draw, don't do anything.
            if (dd.CmdListsCount == 0 || dd.TotalVtxCount == 0)
            {
                return;
            }

            // --- 1. Resize and Update Buffers ---

            // Ensure our vertex buffer is large enough.
            uint vbSize = (uint)(dd.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
            if (vbSize > _vb.SizeInBytes)
            {
                _vb.Dispose();
                // Double the size to avoid frequent re-allocations.
                _vb = gd.ResourceFactory.CreateBuffer(new BufferDescription(vbSize * 2, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            }

            // Ensure our index buffer is large enough.
            uint ibSize = (uint)(dd.TotalIdxCount * sizeof(ushort));
            if (ibSize > _ib.SizeInBytes)
            {
                _ib.Dispose();
                // Double the size.
                _ib = gd.ResourceFactory.CreateBuffer(new BufferDescription(ibSize * 2, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
            }

            // Upload the vertex and index data for all command lists into our single GPU buffers.
            uint vOffset = 0;
            uint iOffset = 0;
            for (int n = 0; n < dd.CmdListsCount; ++n)
            {
                ImDrawListPtr clist = dd.CmdLists[n];
                
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

            // Update the projection matrix uniform.
            var io = ImGui.GetIO();
            Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(
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

            int baseVtx = 0;
            int baseIdx = 0;
            for (int n = 0; n < dd.CmdListsCount; ++n)
            {
                ImDrawListPtr clist = dd.CmdLists[n];
                for (int cmd_i = 0; cmd_i < clist.CmdBuffer.Size; ++cmd_i)
                {
                    ImDrawCmdPtr pcmd = clist.CmdBuffer[cmd_i];
                    
                    // Skip user-defined callbacks.
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        continue;
                    }

                    // Set the correct texture resource set for this draw command.
                    if (_setsById.TryGetValue(pcmd.TextureId, out ResourceSet resourceSet))
                    {
                        cl.SetGraphicsResourceSet(0, resourceSet);
                    }
                    else
                    {
                        // This can happen if a texture is destroyed but ImGui still tries to render it.
                        // In our case, we always set the font texture, so this should be safe.
                        cl.SetGraphicsResourceSet(0, _set); // Fallback to font texture.
                    }

                    // Set the scissor rectangle to clip rendering.
                    cl.SetScissorRect(
                        0,
                        (uint)pcmd.ClipRect.X,
                        (uint)pcmd.ClipRect.Y,
                        (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                        (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y));

                    // Issue the draw call.
                    cl.DrawIndexed(
                        indexCount: pcmd.ElemCount,
                        instanceCount: 1,
                        indexStart: pcmd.IdxOffset + (uint)baseIdx,
                        vertexOffset: (int)pcmd.VtxOffset + baseVtx,
                        instanceStart: 0);
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
            io.DisplaySize            = new Vector2(_winW / _scale.X, _winH / _scale.Y);
            io.DisplayFramebufferScale = _scale;
            io.DeltaTime = dt;
        }

        private void UpdateInputs(InputSnapshot s)
        {
            var io = ImGui.GetIO();
            io.MouseDown[0] = s.IsMouseDown(MouseButton.Left);
            io.MouseDown[1] = s.IsMouseDown(MouseButton.Right);
            io.MouseDown[2] = s.IsMouseDown(MouseButton.Middle);
            io.MouseWheel   = s.WheelDelta;
            io.MousePos     = s.MousePosition;

            foreach (char c in s.KeyCharPresses) io.AddInputCharacter(c);
            foreach (var e in s.KeyEvents) 
            {
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
        private (string, string) GetShaders(GraphicsBackend backend)
        {
            switch (backend)
            {
                case GraphicsBackend.Direct3D11:
                    return (VsHlsl, FsHlsl);
                case GraphicsBackend.Metal:
                    return (VsMetal, FsMetal);
                case GraphicsBackend.Vulkan:
                    return (VsSpirv, FsSpirv);
                case GraphicsBackend.OpenGL:
                case GraphicsBackend.OpenGLES:
                default:
                    return (VsGlsl, FsGlsl);
            }
        }

        private const string VsGlsl = @"#version 450
layout(location=0) in vec2 in_position;
layout(location=1) in vec2 in_texCoord;
layout(location=2) in vec4 in_color;
layout(set=0,binding=0) uniform Projection { mat4 M; };
layout(location=0) out vec2 fs_Tex;
layout(location=1) out vec4 fs_Col;
void main()
{
    fs_Tex = in_texCoord;
    fs_Col = in_color;
    gl_Position = M * vec4(in_position,0,1);
}";

        private const string FsGlsl = @"#version 450
layout(location=0) in vec2 fs_Tex;
layout(location=1) in vec4 fs_Col;
layout(location=0) out vec4 out_Color;
layout(set=0,binding=1) uniform texture2D MainTex;
layout(set=0,binding=2) uniform sampler   MainSamp;
void main()
{
    out_Color = fs_Col * texture(sampler2D(MainTex,MainSamp), fs_Tex);
}";

        private const string VsSpirv = @"#version 450
layout(location=0) in vec2 in_position;
layout(location=1) in vec2 in_texCoord;
layout(location=2) in vec4 in_color;
layout(set=0,binding=0) uniform Projection { mat4 M; };
layout(location=0) out vec2 fs_Tex;
layout(location=1) out vec4 fs_Col;
void main()
{
    fs_Tex = in_texCoord;
    fs_Col = in_color;
    gl_Position = M * vec4(in_position,0,1);
}";

        private const string FsSpirv = @"#version 450
layout(location=0) in vec2 fs_Tex;
layout(location=1) in vec4 fs_Col;
layout(location=0) out vec4 out_Color;
layout(set=0,binding=1) uniform texture2D MainTex;
layout(set=0,binding=2) uniform sampler   MainSamp;
void main()
{
    out_Color = fs_Col * texture(sampler2D(MainTex,MainSamp), fs_Tex);
}";

        private const string VsHlsl = @"
cbuffer Projection : register(b0)
{
    float4x4 M;
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
    output.pos = mul(M, float4(input.pos.xy, 0.0, 1.0));
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

float4 FS(PS_INPUT input) : SV_Target
{
    float4 out_col = input.col * texture0.Sample(sampler0, input.uv);
    return out_col;
}";

        private const string VsMetal = @"
#include <metal_stdlib>
using namespace metal;

struct UBO  { float4x4 M; };

struct VIN  { float2 pos [[attribute(0)]];
              float2 uv  [[attribute(1)]];
              float4 col [[attribute(2)]]; };

struct VOUT { float4 pos [[position]];
              float2 uv;
              float4 col; };

vertex VOUT VS(VIN v [[stage_in]],
               constant UBO& u [[buffer(0)]])
{
    VOUT o;
    o.pos = u.M * float4(v.pos,0,1);
    o.uv  = v.uv;
    o.col = v.col;
    return o;
}";

        private const string FsMetal = @"
#include <metal_stdlib>
using namespace metal;

struct FSin { float4 pos [[position]];
              float2 uv;
              float4 col; };

fragment float4 FS(FSin  inp [[stage_in]],
                   texture2d<float> tex [[texture(1)]],
                   sampler          smp [[sampler(2)]])
{
    return inp.col * tex.sample(smp, inp.uv);
}";

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
            
            if (_context != IntPtr.Zero)
            {
                ImGui.DestroyContext(_context);
            }
        }
    }
}