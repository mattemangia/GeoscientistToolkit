using System.Numerics;
using System.Runtime.InteropServices;
using GAIA.Util;
using OpenTK.Graphics.OpenGL;

namespace GAIA.UI;

/// <summary>OpenGL 3.3 renderer for large pore networks using instanced pores and streamed throat lines.</summary>
internal sealed class OpenTkPnmRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PoreGpuData
    {
        public readonly Vector3 Position;
        public readonly float Value;
        public readonly float Radius;
        public PoreGpuData(Vector3 position, float value, float radius) =>
            (Position, Value, Radius) = (position, value, radius);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ThroatGpuData
    {
        public readonly Vector3 Position;
        public readonly float Value;
        public ThroatGpuData(Vector3 position, float value) => (Position, Value) = (position, value);
    }

    private int _fbo, _color, _depth;
    private int _poreVao, _sphereVbo, _sphereEbo, _instanceVbo, _poreProgram;
    private int _throatVao, _throatVbo, _throatProgram;
    private int _sphereIndexCount, _poreCount, _throatVertexCount;
    public int Width { get; private set; } = 1280;
    public int Height { get; private set; } = 720;
    public IntPtr TextureId => (IntPtr)_color;

    public OpenTkPnmRenderer()
    {
        _poreProgram = CreateProgram(PoreVertex, PoreFragment);
        _throatProgram = CreateProgram(ThroatVertex, ThroatFragment);
        CreateSphereResources();
        CreateThroatResources();
        Resize(Width, Height);
    }

    public void Resize(int width, int height)
    {
        var maxTextureSize = GL.GetInteger(GetPName.MaxTextureSize);
        if (maxTextureSize < 1) maxTextureSize = OpenTkTextureManager.MaxTextureSizeFallback;
        width = Math.Clamp(width, 1, maxTextureSize);
        height = Math.Clamp(height, 1, maxTextureSize);
        if (_fbo != 0 && width == Width && height == Height) return;
        Width = width; Height = height;
        if (_fbo == 0) _fbo = GL.GenFramebuffer();
        if (_color != 0) GL.DeleteTexture(_color);
        if (_depth != 0) GL.DeleteRenderbuffer(_depth);
        _color = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _color);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _depth = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, width, height);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _color, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _depth);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException("PNM OpenGL framebuffer is incomplete.");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Upload(IReadOnlyList<PoreGpuData> pores, IReadOnlyList<ThroatGpuData> throats)
    {
        _poreCount = pores.Count;
        _throatVertexCount = throats.Count;
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        if (pores.Count == 0)
            GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        else
            GL.BufferData(BufferTarget.ArrayBuffer, pores.Count * Marshal.SizeOf<PoreGpuData>(),
                pores.ToArray(), BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _throatVbo);
        if (throats.Count == 0)
            GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        else
            GL.BufferData(BufferTarget.ArrayBuffer, throats.Count * Marshal.SizeOf<ThroatGpuData>(),
                throats.ToArray(), BufferUsageHint.DynamicDraw);
    }

    public void Render(Matrix4x4 viewProjection, Vector3 camera, float minValue, float maxValue,
        float poreScale, bool showPores, bool showThroats)
    {
        var invRange = 1f / Math.Max(1e-20f, maxValue - minValue);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.Viewport(0, 0, Width, Height);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Multisample);
        GL.ClearColor(.08f, .08f, .10f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (showThroats && _throatVertexCount > 0)
        {
            GL.UseProgram(_throatProgram);
            SetMatrix(_throatProgram, "uVp", viewProjection);
            GL.Uniform3(GL.GetUniformLocation(_throatProgram, "uRange"), minValue, maxValue, invRange);
            GL.BindVertexArray(_throatVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, _throatVertexCount);
        }
        if (showPores && _poreCount > 0)
        {
            GL.UseProgram(_poreProgram);
            SetMatrix(_poreProgram, "uVp", viewProjection);
            GL.Uniform3(GL.GetUniformLocation(_poreProgram, "uCamera"), camera.X, camera.Y, camera.Z);
            GL.Uniform3(GL.GetUniformLocation(_poreProgram, "uRange"), minValue, maxValue, invRange);
            GL.Uniform1(GL.GetUniformLocation(_poreProgram, "uScale"), poreScale * .1f);
            GL.BindVertexArray(_poreVao);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, _sphereIndexCount,
                DrawElementsType.UnsignedShort, IntPtr.Zero, _poreCount);
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void CreateSphereResources()
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new[] { new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),
            new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),
            new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1) };
        for (var i=0;i<v.Length;i++) v[i]=Vector3.Normalize(v[i]);
        ushort[] idx = {0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
            3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        _sphereIndexCount=idx.Length; _poreVao=GL.GenVertexArray(); _sphereVbo=GL.GenBuffer();
        _sphereEbo=GL.GenBuffer(); _instanceVbo=GL.GenBuffer(); GL.BindVertexArray(_poreVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer,_sphereVbo);GL.BufferData(BufferTarget.ArrayBuffer,v.Length*12,v,BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer,_sphereEbo);GL.BufferData(BufferTarget.ElementArrayBuffer,idx.Length*2,idx,BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);GL.VertexAttribPointer(0,3,VertexAttribPointerType.Float,false,12,0);
        GL.BindBuffer(BufferTarget.ArrayBuffer,_instanceVbo);
        GL.EnableVertexAttribArray(1);GL.VertexAttribPointer(1,3,VertexAttribPointerType.Float,false,20,0);GL.VertexAttribDivisor(1,1);
        GL.EnableVertexAttribArray(2);GL.VertexAttribPointer(2,1,VertexAttribPointerType.Float,false,20,12);GL.VertexAttribDivisor(2,1);
        GL.EnableVertexAttribArray(3);GL.VertexAttribPointer(3,1,VertexAttribPointerType.Float,false,20,16);GL.VertexAttribDivisor(3,1);
    }

    private void CreateThroatResources()
    {
        _throatVao=GL.GenVertexArray();_throatVbo=GL.GenBuffer();GL.BindVertexArray(_throatVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer,_throatVbo);GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0,3,VertexAttribPointerType.Float,false,16,0);GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1,1,VertexAttribPointerType.Float,false,16,12);
    }

    private static int CreateProgram(string vs, string fs)
    {
        static int Compile(ShaderType t,string s){var x=GL.CreateShader(t);GL.ShaderSource(x,s);GL.CompileShader(x);
            GL.GetShader(x,ShaderParameter.CompileStatus,out var ok);if(ok==0)throw new InvalidOperationException(GL.GetShaderInfoLog(x));return x;}
        var v=Compile(ShaderType.VertexShader,vs);var f=Compile(ShaderType.FragmentShader,fs);var p=GL.CreateProgram();
        GL.AttachShader(p,v);GL.AttachShader(p,f);GL.LinkProgram(p);GL.GetProgram(p,GetProgramParameterName.LinkStatus,out var ok);
        GL.DeleteShader(v);GL.DeleteShader(f);if(ok==0)throw new InvalidOperationException(GL.GetProgramInfoLog(p));return p;
    }
    // System.Numerics is row-vector (v*M); GLSL is column-vector (M*v). Passing the row-major
    // array with transpose=false makes GL read it column-major, which is the transpose GLSL needs.
    private static void SetMatrix(int p,string n,Matrix4x4 m){var a=new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};GL.UniformMatrix4(GL.GetUniformLocation(p,n),1,false,a);}
    public void Dispose(){if(_sphereVbo!=0)GL.DeleteBuffer(_sphereVbo);if(_sphereEbo!=0)GL.DeleteBuffer(_sphereEbo);if(_instanceVbo!=0)GL.DeleteBuffer(_instanceVbo);if(_poreVao!=0)GL.DeleteVertexArray(_poreVao);if(_throatVbo!=0)GL.DeleteBuffer(_throatVbo);if(_throatVao!=0)GL.DeleteVertexArray(_throatVao);if(_poreProgram!=0)GL.DeleteProgram(_poreProgram);if(_throatProgram!=0)GL.DeleteProgram(_throatProgram);if(_color!=0)GL.DeleteTexture(_color);if(_depth!=0)GL.DeleteRenderbuffer(_depth);if(_fbo!=0)GL.DeleteFramebuffer(_fbo);}

    private const string ColorFunction=@"vec3 ramp(float t){t=clamp(t,0.,1.);return clamp(vec3(1.5-abs(4.*t-3.),1.5-abs(4.*t-2.),1.5-abs(4.*t-1.)),0.,1.);}";
    private const string PoreVertex=@"#version 330 core
layout(location=0)in vec3 p;layout(location=1)in vec3 ip;layout(location=2)in float value;layout(location=3)in float radius;uniform mat4 uVp;uniform float uScale;out vec3 N;out vec3 W;out float V;void main(){N=p;W=ip+p*radius*uScale;V=value;gl_Position=uVp*vec4(W,1);}";
    private const string PoreFragment=@"#version 330 core
in vec3 N;in vec3 W;in float V;uniform vec3 uCamera;uniform vec3 uRange;out vec4 o;"+ColorFunction+@"void main(){vec3 n=normalize(N);vec3 l=normalize(vec3(1));float d=.3+.7*max(dot(n,l),0.);float s=.35*pow(max(dot(normalize(uCamera-W),reflect(-l,n)),0.),24.);o=vec4(ramp((V-uRange.x)*uRange.z)*d+s,1);}";
    private const string ThroatVertex=@"#version 330 core
layout(location=0)in vec3 p;layout(location=1)in float value;uniform mat4 uVp;out float V;void main(){V=value;gl_Position=uVp*vec4(p,1);}";
    private const string ThroatFragment=@"#version 330 core
in float V;uniform vec3 uRange;out vec4 o;"+ColorFunction+@"void main(){o=vec4(ramp((V-uRange.x)*uRange.z),1);}";
}
