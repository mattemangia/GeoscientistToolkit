using System.Numerics;
using OpenTK.Graphics.OpenGL;

namespace GAIA.Data.PhysicoChem;

/// <summary>Depth-tested off-screen reactor renderer with a dedicated integer picking target.</summary>
internal sealed class OpenTkPhysicoChemRenderer : IDisposable
{
    private int _vao, _vbo, _ebo, _program, _fbo, _depth, _pickTexture, _indexCount;
    private readonly List<string> _pickIds = new();
    public int ColorTexture { get; private set; }
    public int Width { get; private set; } = 1;
    public int Height { get; private set; } = 1;

    public void Initialize()
    {
        _program = CreateProgram(VertexShader, FragmentShader);
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();
        Resize(1, 1);
    }

    public void Upload(PhysicoChemDataset dataset, string fieldName = null, PhysicoChemState state = null, bool showCells = true)
    {
        var vertices = new List<float>();
        var indices = new List<uint>();
        _pickIds.Clear();
        var field = ResolveField(fieldName, state);
        var range = FieldRange(field);
        foreach (var cell in showCells ? dataset.Mesh.Cells.Values.Where(c => c.IsVisible) : Enumerable.Empty<Cell>())
        {
            _pickIds.Add(cell.ID);
            var pickId = _pickIds.Count;
            var selected = dataset.SelectedCellIDs.Contains(cell.ID);
            var material = dataset.Materials.FirstOrDefault(m => m.MaterialID == cell.MaterialID);
            var color = selected ? new Vector4(1f, .67f, .08f, 1f) :
                TryCellIndex(cell.ID, field, out var ix, out var iy, out var iz)
                    ? Turbo((field[ix, iy, iz] - range.Min) / Math.Max(1e-20f, range.Max - range.Min))
                    : material?.Color ?? new Vector4(.42f, .67f, .82f, 1f);
            AddCube(vertices, indices, new Vector3((float)cell.Center.X, (float)cell.Center.Y, (float)cell.Center.Z),
                (float)Math.Cbrt(Math.Max(cell.Volume, 1e-18)), color, pickId);
        }

        _indexCount = indices.Count;
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.DynamicDraw);
        const int stride = 11 * sizeof(float);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, 10 * sizeof(float));
    }

    private static float[,,] ResolveField(string name, PhysicoChemState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(name)) return null;
        if (name == "Velocity Magnitude")
        {
            var result = new float[state.VelocityX.GetLength(0), state.VelocityX.GetLength(1), state.VelocityX.GetLength(2)];
            for (var i=0;i<result.GetLength(0);i++) for(var j=0;j<result.GetLength(1);j++) for(var k=0;k<result.GetLength(2);k++)
                result[i,j,k] = MathF.Sqrt(state.VelocityX[i,j,k]*state.VelocityX[i,j,k] + state.VelocityY[i,j,k]*state.VelocityY[i,j,k] + state.VelocityZ[i,j,k]*state.VelocityZ[i,j,k]);
            return result;
        }
        return name switch
        {
            "Temperature" => state.Temperature, "Pressure" => state.Pressure,
            "Porosity" => state.Porosity, "Permeability" => state.Permeability,
            "Liquid Saturation" => state.LiquidSaturation, "Vapor Saturation" => state.VaporSaturation,
            "Gas Saturation" => state.GasSaturation, _ => null
        };
    }

    private static (float Min, float Max) FieldRange(float[,,] field)
    {
        if (field == null) return (0, 1);
        var min = float.MaxValue; var max = float.MinValue;
        foreach (var value in field) if (float.IsFinite(value)) { min = Math.Min(min, value); max = Math.Max(max, value); }
        return min == float.MaxValue ? (0, 1) : (min, max <= min ? min + 1e-6f : max);
    }

    private static bool TryCellIndex(string id, float[,,] field, out int x, out int y, out int z)
    {
        x = y = z = 0; if (field == null) return false;
        var parts = id.Split('_');
        return parts.Length >= 4 && int.TryParse(parts[^3].Split('.')[0], out x) &&
               int.TryParse(parts[^2].Split('.')[0], out y) && int.TryParse(parts[^1].Split('.')[0], out z) &&
               x >= 0 && y >= 0 && z >= 0 && x < field.GetLength(0) && y < field.GetLength(1) && z < field.GetLength(2);
    }

    private static Vector4 Turbo(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var r = Math.Clamp(1.5f - Math.Abs(4f * t - 3f), 0f, 1f);
        var g = Math.Clamp(1.5f - Math.Abs(4f * t - 2f), 0f, 1f);
        var b = Math.Clamp(1.5f - Math.Abs(4f * t - 1f), 0f, 1f);
        return new Vector4(r, g, b, 1f);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        if (width == Width && height == Height && _fbo != 0) return;
        Width = width; Height = height;
        if (_fbo == 0) _fbo = GL.GenFramebuffer();
        if (ColorTexture != 0) GL.DeleteTexture(ColorTexture);
        if (_pickTexture != 0) GL.DeleteTexture(_pickTexture);
        if (_depth != 0) GL.DeleteRenderbuffer(_depth);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        ColorTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTexture, 0);

        _pickTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _pickTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32ui, Width, Height, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, TextureTarget.Texture2D, _pickTexture, 0);

        _depth = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, Width, Height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depth);
        GL.DrawBuffers(2, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 });
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException("PhysicoChem renderer framebuffer is incomplete.");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Render(Matrix4x4 view, Matrix4x4 projection, RenderMode mode)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.Viewport(0, 0, Width, Height);
        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace); // all six faces remain valid from inside/outside views
        GL.ClearColor(.035f, .045f, .065f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.ClearBuffer(ClearBuffer.Color, 1, new uint[] { 0, 0, 0, 0 });
        GL.UseProgram(_program);
        SetMatrix("uMvp", view * projection);
        GL.BindVertexArray(_vao);
        SetInt("uWireframePass", mode == RenderMode.Wireframe ? 1 : 0);
        GL.PolygonMode(MaterialFace.FrontAndBack, mode == RenderMode.Wireframe ? PolygonMode.Line : PolygonMode.Fill);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
        if (mode == RenderMode.SolidWireframe)
        {
            GL.ColorMask(1, false, false, false, false);
            SetInt("uWireframePass", 1);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            GL.Enable(EnableCap.PolygonOffsetLine);
            GL.PolygonOffset(-1f, -1f);
            GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
            GL.Disable(EnableCap.PolygonOffsetLine);
            GL.ColorMask(1, true, true, true, true);
        }
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public string Pick(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
        var value = new uint[1];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
        GL.ReadPixels(x, Height - 1 - y, 1, 1, PixelFormat.RedInteger, PixelType.UnsignedInt, value);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return value[0] > 0 && value[0] <= _pickIds.Count ? _pickIds[(int)value[0] - 1] : null;
    }

    private static void AddCube(List<float> v, List<uint> idx, Vector3 c, float size, Vector4 color, int pickId)
    {
        var h = size * .48f;
        var faces = new (Vector3 N, Vector3[] P)[]
        {
            (Vector3.UnitX, new[]{new Vector3(h,-h,-h),new Vector3(h,h,-h),new Vector3(h,h,h),new Vector3(h,-h,h)}),
            (-Vector3.UnitX,new[]{new Vector3(-h,-h,h),new Vector3(-h,h,h),new Vector3(-h,h,-h),new Vector3(-h,-h,-h)}),
            (Vector3.UnitY, new[]{new Vector3(-h,h,-h),new Vector3(-h,h,h),new Vector3(h,h,h),new Vector3(h,h,-h)}),
            (-Vector3.UnitY,new[]{new Vector3(-h,-h,h),new Vector3(-h,-h,-h),new Vector3(h,-h,-h),new Vector3(h,-h,h)}),
            (Vector3.UnitZ, new[]{new Vector3(-h,-h,h),new Vector3(h,-h,h),new Vector3(h,h,h),new Vector3(-h,h,h)}),
            (-Vector3.UnitZ,new[]{new Vector3(h,-h,-h),new Vector3(-h,-h,-h),new Vector3(-h,h,-h),new Vector3(h,h,-h)})
        };
        foreach (var face in faces)
        {
            var start = (uint)(v.Count / 11);
            foreach (var p in face.P)
                v.AddRange(new[] { p.X+c.X,p.Y+c.Y,p.Z+c.Z,face.N.X,face.N.Y,face.N.Z,color.X,color.Y,color.Z,color.W,(float)pickId });
            idx.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
    }

    private void SetMatrix(string name, Matrix4x4 m)
    {
        var a = new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
        GL.UniformMatrix4(GL.GetUniformLocation(_program, name), 1, false, a);
    }

    private void SetInt(string name, int value)
    {
        GL.Uniform1(GL.GetUniformLocation(_program, name), value);
    }

    private static int CreateProgram(string vs, string fs)
    {
        int Compile(ShaderType type, string source) { var s=GL.CreateShader(type);GL.ShaderSource(s,source);GL.CompileShader(s);GL.GetShader(s,ShaderParameter.CompileStatus,out var ok);if(ok==0)throw new InvalidOperationException(GL.GetShaderInfoLog(s));return s; }
        var v=Compile(ShaderType.VertexShader,vs);var f=Compile(ShaderType.FragmentShader,fs);var p=GL.CreateProgram();GL.AttachShader(p,v);GL.AttachShader(p,f);GL.LinkProgram(p);GL.GetProgram(p,GetProgramParameterName.LinkStatus,out var ok);GL.DeleteShader(v);GL.DeleteShader(f);if(ok==0)throw new InvalidOperationException(GL.GetProgramInfoLog(p));return p;
    }

    public void Dispose()
    {
        if(_vbo!=0)GL.DeleteBuffer(_vbo);if(_ebo!=0)GL.DeleteBuffer(_ebo);if(_vao!=0)GL.DeleteVertexArray(_vao);
        if(_program!=0)GL.DeleteProgram(_program);if(ColorTexture!=0)GL.DeleteTexture(ColorTexture);if(_pickTexture!=0)GL.DeleteTexture(_pickTexture);
        if(_depth!=0)GL.DeleteRenderbuffer(_depth);if(_fbo!=0)GL.DeleteFramebuffer(_fbo);
    }

    private const string VertexShader = @"#version 330 core
layout(location=0) in vec3 p; layout(location=1) in vec3 n; layout(location=2) in vec4 c; layout(location=3) in float id;
uniform mat4 uMvp; out vec3 N; out vec4 C; flat out uint PickId;
void main(){gl_Position=uMvp*vec4(p,1);N=n;C=c;PickId=uint(id+0.5);}";
    private const string FragmentShader = @"#version 330 core
in vec3 N; in vec4 C; flat in uint PickId; layout(location=0) out vec4 Color; layout(location=1) out uint ObjectId;
uniform int uWireframePass;
void main(){float d=.28+.72*abs(dot(normalize(N),normalize(vec3(.35,.65,1))));vec3 shaded=C.rgb*d;float luminance=dot(shaded,vec3(.2126,.7152,.0722));vec3 wireColor=luminance>.5?vec3(.04):vec3(.96);Color=vec4(uWireframePass!=0?wireColor:shaded,C.a);ObjectId=PickId;}";
}
