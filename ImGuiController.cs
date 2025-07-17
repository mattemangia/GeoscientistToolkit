// GeoscientistToolkit/ImGuiController.cs
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

        private DeviceBuffer _vb, _ib, _ub;
        private Texture      _fontTex;
        private ResourceSet  _set;

        private Shader        _vs, _fs;
        private ResourceLayout _layout;
        private Pipeline       _pipe;

        private readonly IntPtr _fontID = (IntPtr)1;
        private int  _winW, _winH;
        private Vector2 _scale = Vector2.One;

        // ------------------------------------------------------------------
        public ImGuiController(GraphicsDevice gd,
                               OutputDescription fbDesc,
                               int width, int height)
        {
            _gd   = gd;
            _winW = width;
            _winH = height;

            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags  |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;

            CreateDeviceResources(gd, fbDesc);
            SetPerFrameImGuiData(1f / 60f);

            ImGui.NewFrame();
            _frameBegun = true;
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

            _pipe = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.Default,
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

        // ------------------------------------------------------------------
        public void Update(float dt, InputSnapshot snap)
        {
            if (_frameBegun) ImGui.Render();

            SetPerFrameImGuiData(dt);
            UpdateInputs(snap);

            _frameBegun = true;
            ImGui.NewFrame();
        }

        // ------------------------------------------------------------------
        public void Render(GraphicsDevice gd, CommandList cl)
        {
            if (!_frameBegun) return;
            ImGui.Render();
            DrawImGui(ImGui.GetDrawData(), gd, cl);
            _frameBegun = false;
        }

        // ------------------------------------------------------------------
        private void DrawImGui(ImDrawDataPtr dd,
                               GraphicsDevice gd, CommandList cl)
        {
            if (dd.CmdListsCount == 0) return;

            uint vbSize = (uint)(dd.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
            if (vbSize > _vb.SizeInBytes)
            {
                _vb.Dispose();
                _vb = gd.ResourceFactory.CreateBuffer(
                    new BufferDescription(vbSize * 2, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            }

            uint ibSize = (uint)(dd.TotalIdxCount * sizeof(ushort));
            if (ibSize > _ib.SizeInBytes)
            {
                _ib.Dispose();
                _ib = gd.ResourceFactory.CreateBuffer(
                    new BufferDescription(ibSize * 2, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
            }

            uint vOffset = 0, iOffset = 0;
            for (int n = 0; n < dd.CmdListsCount; ++n)
            {
                ImDrawListPtr clist = dd.CmdLists[n];
                cl.UpdateBuffer(_vb, vOffset * (uint)Unsafe.SizeOf<ImDrawVert>(),
                                clist.VtxBuffer.Data,
                                (uint)(clist.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));
                cl.UpdateBuffer(_ib, iOffset * sizeof(ushort),
                                clist.IdxBuffer.Data,
                                (uint)(clist.IdxBuffer.Size * sizeof(ushort)));
                vOffset += (uint)clist.VtxBuffer.Size;
                iOffset += (uint)clist.IdxBuffer.Size;
            }

            cl.SetVertexBuffer(0, _vb);
            cl.SetIndexBuffer(_ib, IndexFormat.UInt16);
            cl.SetPipeline(_pipe);
            cl.SetGraphicsResourceSet(0, _set);

            var io = ImGui.GetIO();
            Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(
                0, io.DisplaySize.X, io.DisplaySize.Y, 0, -1, 1);
            gd.UpdateBuffer(_ub, 0, ref mvp);

            dd.ScaleClipRects(io.DisplayFramebufferScale);

            int baseVtx = 0, baseIdx = 0;
            for (int n = 0; n < dd.CmdListsCount; ++n)
            {
                ImDrawListPtr clist = dd.CmdLists[n];
                for (int cmd_i = 0; cmd_i < clist.CmdBuffer.Size; ++cmd_i)
                {
                    ImDrawCmdPtr pcmd = clist.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback != IntPtr.Zero) continue;

                    cl.SetScissorRect(0,
                        (uint)pcmd.ClipRect.X,
                        (uint)pcmd.ClipRect.Y,
                        (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                        (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y));

                    cl.DrawIndexed(pcmd.ElemCount, 1,
                                   pcmd.IdxOffset + (uint)baseIdx,
                                   (int)pcmd.VtxOffset + baseVtx, 0);
                }
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
            foreach (var e in s.KeyEvents) io.AddKeyEvent((ImGuiKey)e.Key, e.Down);
        }

        // ------------------------------------------------------------------
        private (string, string) GetShaders(GraphicsBackend b)
            => b == GraphicsBackend.Metal
               ? (VsMetal, FsMetal)
               : (VsGlsl,  FsGlsl);

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
            _vb.Dispose(); _ib.Dispose(); _ub.Dispose();
            _fontTex.Dispose(); _set.Dispose();
            _vs.Dispose(); _fs.Dispose(); _layout.Dispose(); _pipe.Dispose();
        }
    }
}
