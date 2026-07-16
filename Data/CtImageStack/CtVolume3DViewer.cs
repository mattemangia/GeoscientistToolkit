using System.Numerics;
using GAIA.Business;
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
    private int _volumeTexture, _labelTexture, _previewTexture;
    private int _renderWidth = 1280, _renderHeight = 720;
    private Vector3 _cameraTarget;
    private float _cameraYaw = -MathF.PI / 4f, _cameraPitch = MathF.PI / 6f, _cameraDistance = 2f;
    private Vector2 _lastMouse;
    private bool _dragging, _panning, _disposed, _previewDirty, _labelsDirty;
    private Matrix4x4 _view, _projection;
    private byte[] _previewMask;
    internal Vector4 _previewColor = new(1, 0, 0, 0.5f);
    internal bool _showPreview;

    public Vector3 VolumeScale { get; private set; } = Vector3.One;
    public int ColorMapIndex;
    public bool CutXEnabled, CutYEnabled, CutZEnabled;
    public bool CutXForward = true, CutYForward = true, CutZForward = true;
    public float CutXPosition = 0.5f, CutYPosition = 0.5f, CutZPosition = 0.5f;
    public float MinThreshold = 0.05f, MaxThreshold = 1f, StepSize = 2f;
    public bool ShowGrayscale = true, ShowSlices;
    public Vector3 SlicePositions = new(0.5f);
    public bool ShowCutXPlaneVisual { get; set; } = true;
    public bool ShowCutYPlaneVisual { get; set; } = true;
    public bool ShowCutZPlaneVisual { get; set; } = true;
    public bool ShowPlaneVisualizations { get; set; } = true;
    public List<ClippingPlane> ClippingPlanes { get; } = new();

    public CtVolume3DViewer(StreamingCtVolumeDataset dataset)
    {
        if (!OpenTkManager.IsInitialized)
            throw new InvalidOperationException("The CT 3D viewer requires the OpenTK renderer.");
        _streamingDataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _editableDataset = dataset.EditablePartner ?? throw new InvalidOperationException("Missing editable CT partner.");
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
        CreateResources();
        ResetCamera();
        ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
        CtImageStackTools.Preview3DChanged += OnPreviewChanged;
    }

    public void DrawToolbarControls() { }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        var available = ImGui.GetContentRegionAvail();
        if (available.X < 2 || available.Y < 2) return;
        var desiredW = Math.Clamp((int)available.X, 320, 1920);
        var desiredH = Math.Clamp((int)available.Y, 240, 1080);
        if (desiredW != _renderWidth || desiredH != _renderHeight) ResizeTarget(desiredW, desiredH);
        HandleInput();
        Render();
        ImGui.Image((IntPtr)_colorTexture, available, new Vector2(0, 1), new Vector2(1, 0));
    }

    private void CreateResources()
    {
        _program = CreateProgram(VertexShader, FragmentShader);
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
        var lod = _streamingDataset.RenderLod ?? _streamingDataset.BaseLod;
        var bricks = _streamingDataset.RenderLodVolumeData ?? _streamingDataset.BaseLodVolumeData;
        var density = ReconstructVolume(lod, bricks, _streamingDataset.BrickSize);
        MinThreshold = Math.Max(MinThreshold, CalculateOtsuThreshold(density) / 255f * 0.8f);
        _volumeTexture = CreateTexture3D(lod.Width, lod.Height, lod.Depth, density);
        var aux = CreateDownsampledLabels(lod.Width, lod.Height, lod.Depth, 128L * 1024 * 1024);
        _labelTexture = CreateTexture3D(aux.w, aux.h, aux.d, aux.data);
        _previewTexture = CreateTexture3D(aux.w, aux.h, aux.d, new byte[aux.data.Length]);
        ResizeTarget(_renderWidth, _renderHeight);
    }

    private void Render()
    {
        if (_labelsDirty) { UploadLabels(); _labelsDirty = false; }
        if (_previewDirty) { UploadPreview(); _previewDirty = false; }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.Viewport(0, 0, _renderWidth, _renderHeight);
        GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace); GL.CullFace(CullFaceMode.Back);
        GL.ClearColor(0.015f, 0.018f, 0.025f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.UseProgram(_program);
        SetMatrix("uView", _view); SetMatrix("uProjection", _projection);
        Set3("uScale", VolumeScale); Set3("uCamera", CameraPosition);
        Set3("uVolumeSize", new Vector3(_streamingDataset.RenderLod.Width, _streamingDataset.RenderLod.Height, _streamingDataset.RenderLod.Depth));
        Set1("uMin", MinThreshold); Set1("uMax", MaxThreshold); Set1("uStep", StepSize);
        Set1("uShowGray", ShowGrayscale ? 1 : 0); Set1("uColorMap", ColorMapIndex);
        Set4("uCutX", new Vector4(CutXEnabled ? 1 : 0, CutXForward ? 1 : -1, CutXPosition, 0));
        Set4("uCutY", new Vector4(CutYEnabled ? 1 : 0, CutYForward ? 1 : -1, CutYPosition, 0));
        Set4("uCutZ", new Vector4(CutZEnabled ? 1 : 0, CutZForward ? 1 : -1, CutZPosition, 0));
        Set1("uPlaneCount", Math.Min(MAX_CLIPPING_PLANES, ClippingPlanes.Count(p => p.Enabled)));
        var pi = 0;
        foreach (var p in ClippingPlanes.Where(p => p.Enabled).Take(MAX_CLIPPING_PLANES))
        {
            Set4($"uPlanes[{pi++}]", new Vector4(p.Normal, p.Mirror ? -p.Distance : p.Distance));
        }
        Set1("uShowPreview", _showPreview ? 1 : 0); Set4("uPreviewColor", _previewColor);
        Bind3D(0, _volumeTexture, "uVolume"); Bind3D(1, _labelTexture, "uLabels"); Bind3D(2, _previewTexture, "uPreview");
        GL.BindVertexArray(_vao); GL.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, 0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private Vector3 CameraPosition => _cameraTarget + new Vector3(MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch), MathF.Sin(_cameraPitch), MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch)) * _cameraDistance;

    private void HandleInput()
    {
        if (!ImGui.IsWindowHovered()) { _dragging = _panning = false; return; }
        var io = ImGui.GetIO(); var mouse = io.MousePos;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { _dragging = true; _lastMouse = mouse; }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) _dragging = false;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) { _panning = true; _lastMouse = mouse; }
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle)) _panning = false;
        var delta = mouse - _lastMouse; _lastMouse = mouse;
        if (_dragging) { _cameraYaw += delta.X * 0.008f; _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.008f, -1.5f, 1.5f); }
        if (_panning) _cameraTarget += new Vector3(-delta.X, delta.Y, 0) * (_cameraDistance * 0.001f);
        if (Math.Abs(io.MouseWheel) > 0) _cameraDistance = Math.Clamp(_cameraDistance * MathF.Pow(0.88f, io.MouseWheel), 0.15f, 20f);
        UpdateCamera();
    }

    public void ResetCamera() { _cameraTarget = VolumeScale * 0.5f; _cameraYaw = -MathF.PI / 4; _cameraPitch = MathF.PI / 6; _cameraDistance = Math.Max(1.5f, VolumeScale.Length() * 1.25f); UpdateCamera(); }
    public void ResetView() { ResetCamera(); CutXEnabled = CutYEnabled = CutZEnabled = false; ClippingPlanes.Clear(); }
    private void UpdateCamera() { _view = Matrix4x4.CreateLookAt(CameraPosition, _cameraTarget, Vector3.UnitY); _projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, _renderWidth / (float)_renderHeight, 0.01f, 100f); }
    public void UpdateClippingPlaneNormal(ClippingPlane p) { var q = Quaternion.CreateFromYawPitchRoll(p.Rotation.Y, p.Rotation.X, p.Rotation.Z); p.Normal = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, q)); }
    public void MarkLabelsAsDirty() => _labelsDirty = true;
    public bool GetMaterialVisibility(byte id) => !_materialVisibility.TryGetValue(id, out var v) || v;
    public float GetMaterialOpacity(byte id) => _materialOpacity.GetValueOrDefault(id, 1f);
    public void SetMaterialVisibility(byte id, bool value) => _materialVisibility[id] = value;
    public void SetMaterialOpacity(byte id, float value) => _materialOpacity[id] = value;
    public void SetAllMaterialsVisibility(bool value) { foreach (var m in _editableDataset.Materials) _materialVisibility[m.ID] = value; }
    public void ResetAllMaterialOpacities() { foreach (var m in _editableDataset.Materials) _materialOpacity[m.ID] = 1; }

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

    private static int CreateTexture3D(int w, int h, int d, byte[] data) { var t=GL.GenTexture(); GL.BindTexture(TextureTarget.Texture3D,t); GL.TexImage3D(TextureTarget.Texture3D,0,PixelInternalFormat.R8,w,h,d,0,PixelFormat.Red,PixelType.UnsignedByte,data); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureWrapS,(int)TextureWrapMode.ClampToEdge); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureWrapT,(int)TextureWrapMode.ClampToEdge); GL.TexParameter(TextureTarget.Texture3D,TextureParameterName.TextureWrapR,(int)TextureWrapMode.ClampToEdge); return t; }
    private static byte[] ReconstructVolume(GvtLodInfo l, byte[] b, int bs) { var r=new byte[l.Width*l.Height*l.Depth]; var bx=(l.Width+bs-1)/bs; var by=(l.Height+bs-1)/bs; for(int z=0;z<l.Depth;z++) for(int y=0;y<l.Height;y++) for(int x=0;x<l.Width;x++){var bi=((z/bs)*by*bx+(y/bs)*bx+x/bs)*bs*bs*bs+(z%bs)*bs*bs+(y%bs)*bs+x%bs; if(bi<b.Length)r[(z*l.Height+y)*l.Width+x]=b[bi];} return r; }
    private (int w,int h,int d,byte[] data) CreateDownsampledLabels(int w,int h,int d,long budget) { var n=(long)w*h*d; var s=n>budget?Math.Pow(budget/(double)n,1.0/3):1; var tw=Math.Max(1,(int)(w*s));var th=Math.Max(1,(int)(h*s));var td=Math.Max(1,(int)(d*s));var a=new byte[tw*th*td]; if(_editableDataset.LabelData!=null) Parallel.For(0,td,z=>{for(int y=0;y<th;y++)for(int x=0;x<tw;x++)a[(z*th+y)*tw+x]=_editableDataset.LabelData[Math.Min(_editableDataset.Width-1,x*_editableDataset.Width/tw),Math.Min(_editableDataset.Height-1,y*_editableDataset.Height/th),Math.Min(_editableDataset.Depth-1,z*_editableDataset.Depth/td)];}); return(tw,th,td,a); }
    private void UploadLabels() { var a=CreateDownsampledLabels((int)TextureWidth(_labelTexture), (int)TextureHeight(_labelTexture), (int)TextureDepth(_labelTexture), long.MaxValue); GL.BindTexture(TextureTarget.Texture3D,_labelTexture); GL.TexSubImage3D(TextureTarget.Texture3D,0,0,0,0,a.w,a.h,a.d,PixelFormat.Red,PixelType.UnsignedByte,a.data); }
    private void UploadPreview() { if(_previewMask==null)return; GL.BindTexture(TextureTarget.Texture3D,_previewTexture); var w=(int)TextureWidth(_previewTexture);var h=(int)TextureHeight(_previewTexture);var d=(int)TextureDepth(_previewTexture);var a=new byte[w*h*d];Parallel.For(0,d,z=>{for(int y=0;y<h;y++)for(int x=0;x<w;x++)a[(z*h+y)*w+x]=_previewMask[(Math.Min(_editableDataset.Depth-1,z*_editableDataset.Depth/d)*_editableDataset.Height+Math.Min(_editableDataset.Height-1,y*_editableDataset.Height/h))*_editableDataset.Width+Math.Min(_editableDataset.Width-1,x*_editableDataset.Width/w)];});GL.TexSubImage3D(TextureTarget.Texture3D,0,0,0,0,w,h,d,PixelFormat.Red,PixelType.UnsignedByte,a); }
    private static long TextureWidth(int t){GL.BindTexture(TextureTarget.Texture3D,t);GL.GetTexLevelParameter(TextureTarget.Texture3D,0,GetTextureParameter.TextureWidth,out int v);return v;} private static long TextureHeight(int t){GL.BindTexture(TextureTarget.Texture3D,t);GL.GetTexLevelParameter(TextureTarget.Texture3D,0,GetTextureParameter.TextureHeight,out int v);return v;} private static long TextureDepth(int t){GL.BindTexture(TextureTarget.Texture3D,t);GL.GetTexLevelParameter(TextureTarget.Texture3D,0,GetTextureParameter.TextureDepth,out int v);return v;}
    private void OnDatasetDataChanged(Dataset d){if(d==_editableDataset)_labelsDirty=true;} private void OnPreviewChanged(CtImageStackDataset d,byte[] m,Vector4 c){if(d!=_editableDataset)return;_previewMask=m;_previewColor=c;_showPreview=m!=null;_previewDirty=true;}
    private void Bind3D(int unit,int tex,string name){GL.ActiveTexture(TextureUnit.Texture0+unit);GL.BindTexture(TextureTarget.Texture3D,tex);Set1(name,unit);} private void Set1(string n,int v)=>GL.Uniform1(GL.GetUniformLocation(_program,n),v);private void Set1(string n,float v)=>GL.Uniform1(GL.GetUniformLocation(_program,n),v);private void Set3(string n,Vector3 v)=>GL.Uniform3(GL.GetUniformLocation(_program,n),v.X,v.Y,v.Z);private void Set4(string n,Vector4 v)=>GL.Uniform4(GL.GetUniformLocation(_program,n),v.X,v.Y,v.Z,v.W);private void SetMatrix(string n,Matrix4x4 m){var a=new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};GL.UniformMatrix4(GL.GetUniformLocation(_program,n),1,true,a);}
    private static int CreateProgram(string vs,string fs){int Compile(ShaderType t,string s){var x=GL.CreateShader(t);GL.ShaderSource(x,s);GL.CompileShader(x);GL.GetShader(x,ShaderParameter.CompileStatus,out var ok);if(ok==0)throw new InvalidOperationException(GL.GetShaderInfoLog(x));return x;}var v=Compile(ShaderType.VertexShader,vs);var f=Compile(ShaderType.FragmentShader,fs);var p=GL.CreateProgram();GL.AttachShader(p,v);GL.AttachShader(p,f);GL.LinkProgram(p);GL.GetProgram(p,GetProgramParameterName.LinkStatus,out var ok);GL.DeleteShader(v);GL.DeleteShader(f);if(ok==0)throw new InvalidOperationException(GL.GetProgramInfoLog(p));return p;}

    public static Vector3 CalculateNormalizedPhysicalScale(int w,int h,int d,float px,float st){var xy=px>0&&float.IsFinite(px)?px:1;var z=st>0&&float.IsFinite(st)?st:xy;var p=new Vector3(Math.Max(1,w)*xy,Math.Max(1,h)*xy,Math.Max(1,d)*z);return p/Math.Max(p.X,Math.Max(p.Y,p.Z));}
    public static byte CalculateOtsuThreshold(byte[] data){if(data==null||data.Length==0)return 0;var h=new long[256];foreach(var v in data)h[v]++;double sum=0,sb=0,best=-1;long wb=0;for(int i=0;i<256;i++)sum+=i*h[i];byte t=0;for(int i=0;i<255;i++){wb+=h[i];if(wb==0)continue;var wf=data.LongLength-wb;if(wf==0)break;sb+=i*h[i];var mb=sb/wb;var mf=(sum-sb)/wf;var v=wb*(double)wf*(mb-mf)*(mb-mf);if(v>best){best=v;t=(byte)i;}}return t;}
    public void Dispose(){if(_disposed)return;_disposed=true;ProjectManager.Instance.DatasetDataChanged-=OnDatasetDataChanged;CtImageStackTools.Preview3DChanged-=OnPreviewChanged;_controlPanel?.Dispose();foreach(var t in new[]{_volumeTexture,_labelTexture,_previewTexture,_colorTexture})if(t!=0)GL.DeleteTexture(t);if(_depthBuffer!=0)GL.DeleteRenderbuffer(_depthBuffer);if(_fbo!=0)GL.DeleteFramebuffer(_fbo);if(_vbo!=0)GL.DeleteBuffer(_vbo);if(_ebo!=0)GL.DeleteBuffer(_ebo);if(_vao!=0)GL.DeleteVertexArray(_vao);if(_program!=0)GL.DeleteProgram(_program);}

    private const string VertexShader=@"#version 330 core
layout(location=0) in vec3 p; uniform mat4 uView,uProjection; uniform vec3 uScale; out vec3 world; void main(){world=p*uScale;gl_Position=uProjection*uView*vec4(world,1);}";
    private const string FragmentShader=@"#version 330 core
in vec3 world;out vec4 outColor;uniform sampler3D uVolume,uLabels,uPreview;uniform vec3 uScale,uCamera,uVolumeSize;uniform float uMin,uMax,uStep;uniform int uShowGray,uColorMap,uShowPreview,uPlaneCount;uniform vec4 uCutX,uCutY,uCutZ,uPlanes[8],uPreviewColor;
bool boxHit(vec3 ro,vec3 rd,out float a,out float b){vec3 q0=(vec3(0)-ro)/rd,q1=(uScale-ro)/rd,mn=min(q0,q1),mx=max(q0,q1);a=max(max(mn.x,mn.y),mn.z);b=min(min(mx.x,mx.y),mx.z);return b>=max(a,0.0);}bool cut(vec3 p){if(uCutX.x>.5&&(p.x-uCutX.z)*uCutX.y>0)return true;if(uCutY.x>.5&&(p.y-uCutY.z)*uCutY.y>0)return true;if(uCutZ.x>.5&&(p.z-uCutZ.z)*uCutZ.y>0)return true;for(int i=0;i<uPlaneCount;i++){vec3 n=normalize(uPlanes[i].xyz);float d=abs(uPlanes[i].w);float s=dot(p-vec3(.5),n)-(d-.5);if(uPlanes[i].w<0?s<0:s>0)return true;}return false;}vec3 cmap(float x){if(uColorMap==0)return vec3(x);if(uColorMap==1)return clamp(vec3(3*x,3*x-1,3*x-2),0,1);if(uColorMap==2)return vec3(x,1-x,1);return clamp(abs(mod(x*6+vec3(0,4,2),6)-3)-1,0,1);}void main(){vec3 ro=uCamera,rd=normalize(world-ro);float a,b;if(!boxHit(ro,rd,a,b))discard;a=max(a,0);vec4 acc=vec4(0);float base=min(uScale.x/uVolumeSize.x,min(uScale.y/uVolumeSize.y,uScale.z/uVolumeSize.z));float ds=max(base*uStep,(b-a)/2048.0);for(int i=0;i<2048&&a<=b&&acc.a<.985;i++,a+=ds){vec3 p=(ro+rd*a)/uScale;if(cut(p))continue;float den=texture(uVolume,p).r;float n=clamp((den-uMin)/max(.001,uMax-uMin),0,1);float al=uShowGray!=0?smoothstep(0,1,n):0;vec3 col=cmap(n);if(al>.01){vec3 e=1/uVolumeSize;vec3 g=vec3(texture(uVolume,p+vec3(e.x,0,0)).r-texture(uVolume,p-vec3(e.x,0,0)).r,texture(uVolume,p+vec3(0,e.y,0)).r-texture(uVolume,p-vec3(0,e.y,0)).r,texture(uVolume,p+vec3(0,0,e.z)).r-texture(uVolume,p-vec3(0,0,e.z)).r);if(length(g)>.0001)col*=.3+.7*abs(dot(normalize(g),normalize(vec3(.4,.6,1))));}if(uShowPreview!=0&&texture(uPreview,p).r>.5){col=mix(col,uPreviewColor.rgb,uPreviewColor.a);al=max(al,uPreviewColor.a);}float ca=clamp(al*ds*80,0,1);acc+=(1-acc.a)*vec4(col*ca,ca);}outColor=acc;}";
}
