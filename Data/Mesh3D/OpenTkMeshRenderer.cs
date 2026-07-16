using System.Numerics;
using OpenTK.Graphics.OpenGL;

namespace GAIA.Data.Mesh3D;

internal sealed class OpenTkMeshRenderer : IDisposable
{
    private int _vao, _vbo, _ebo, _program, _fbo, _depth;
    private int _indexCount;
    public int Width { get; private set; } = 1280;
    public int Height { get; private set; } = 720;
    public int ColorTexture { get; private set; }

    public void Initialize(Mesh3DDataset dataset)
    {
        _program = Program(Vertex, Fragment);
        Upload(dataset);
        Resize(Width, Height);
    }

    public void Upload(Mesh3DDataset dataset)
    {
        if (_vao == 0) { _vao=GL.GenVertexArray();_vbo=GL.GenBuffer();_ebo=GL.GenBuffer(); }
        var packed = new float[dataset.Vertices.Count * 10];
        for (var i=0;i<dataset.Vertices.Count;i++)
        {
            var p=dataset.Vertices[i];var n=i<dataset.Normals.Count?dataset.Normals[i]:Vector3.UnitY;var c=i<dataset.Colors.Count?dataset.Colors[i]:new Vector4(.55f,.68f,.78f,1);
            var o=i*10;packed[o]=p.X;packed[o+1]=p.Y;packed[o+2]=p.Z;packed[o+3]=n.X;packed[o+4]=n.Y;packed[o+5]=n.Z;packed[o+6]=c.X;packed[o+7]=c.Y;packed[o+8]=c.Z;packed[o+9]=c.W;
        }
        var indices=dataset.Faces.SelectMany(f=>f.Length==3?f:f.Skip(1).Take(f.Length-2).SelectMany((_,i)=>new[]{f[0],f[i+1],f[i+2]})).ToArray();_indexCount=indices.Length;
        GL.BindVertexArray(_vao);GL.BindBuffer(BufferTarget.ArrayBuffer,_vbo);GL.BufferData(BufferTarget.ArrayBuffer,packed.Length*4,packed,BufferUsageHint.DynamicDraw);GL.BindBuffer(BufferTarget.ElementArrayBuffer,_ebo);GL.BufferData(BufferTarget.ElementArrayBuffer,indices.Length*4,indices,BufferUsageHint.DynamicDraw);
        for(var i=0;i<3;i++)GL.EnableVertexAttribArray(i);GL.VertexAttribPointer(0,3,VertexAttribPointerType.Float,false,40,0);GL.VertexAttribPointer(1,3,VertexAttribPointerType.Float,false,40,12);GL.VertexAttribPointer(2,4,VertexAttribPointerType.Float,false,40,24);
    }

    public void Resize(int w,int h)
    {
        Width=Math.Max(1,w);Height=Math.Max(1,h);if(_fbo==0)_fbo=GL.GenFramebuffer();if(ColorTexture!=0)GL.DeleteTexture(ColorTexture);if(_depth!=0)GL.DeleteRenderbuffer(_depth);
        ColorTexture=GL.GenTexture();GL.BindTexture(TextureTarget.Texture2D,ColorTexture);GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,Width,Height,0,PixelFormat.Rgba,PixelType.UnsignedByte,IntPtr.Zero);GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
        _depth=GL.GenRenderbuffer();GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer,_depth);GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,RenderbufferStorage.DepthComponent24,Width,Height);GL.BindFramebuffer(FramebufferTarget.Framebuffer,_fbo);GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,ColorTexture,0);GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,FramebufferAttachment.DepthAttachment,RenderbufferTarget.Renderbuffer,_depth);GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);
    }

    public void Render(Matrix4x4 view,Matrix4x4 projection)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer,_fbo);GL.Viewport(0,0,Width,Height);GL.Enable(EnableCap.DepthTest);GL.Enable(EnableCap.CullFace);GL.ClearColor(.035f,.045f,.06f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);GL.UseProgram(_program);SetMatrix("uMvp",view*projection);GL.BindVertexArray(_vao);GL.DrawElements(PrimitiveType.Triangles,_indexCount,DrawElementsType.UnsignedInt,0);GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);
    }
    private void SetMatrix(string n,Matrix4x4 m){var a=new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};GL.UniformMatrix4(GL.GetUniformLocation(_program,n),1,true,a);}
    private static int Program(string vs,string fs){int C(ShaderType t,string s){var x=GL.CreateShader(t);GL.ShaderSource(x,s);GL.CompileShader(x);GL.GetShader(x,ShaderParameter.CompileStatus,out var ok);if(ok==0)throw new InvalidOperationException(GL.GetShaderInfoLog(x));return x;}var v=C(ShaderType.VertexShader,vs);var f=C(ShaderType.FragmentShader,fs);var p=GL.CreateProgram();GL.AttachShader(p,v);GL.AttachShader(p,f);GL.LinkProgram(p);GL.DeleteShader(v);GL.DeleteShader(f);return p;}
    public void Dispose(){if(_vbo!=0)GL.DeleteBuffer(_vbo);if(_ebo!=0)GL.DeleteBuffer(_ebo);if(_vao!=0)GL.DeleteVertexArray(_vao);if(_program!=0)GL.DeleteProgram(_program);if(ColorTexture!=0)GL.DeleteTexture(ColorTexture);if(_depth!=0)GL.DeleteRenderbuffer(_depth);if(_fbo!=0)GL.DeleteFramebuffer(_fbo);}
    private const string Vertex=@"#version 330 core
layout(location=0)in vec3 p;layout(location=1)in vec3 n;layout(location=2)in vec4 c;uniform mat4 uMvp;out vec3 N;out vec4 C;void main(){gl_Position=uMvp*vec4(p,1);N=n;C=c;}";
    private const string Fragment=@"#version 330 core
in vec3 N;in vec4 C;out vec4 o;void main(){float d=.25+.75*abs(dot(normalize(N),normalize(vec3(.4,.7,1))));o=vec4(C.rgb*d,C.a);}";
}
