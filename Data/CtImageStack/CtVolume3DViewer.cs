// GeoscientistToolkit/Data/CtImageStack/CtVolume3DViewer.cs
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.SPIRV;
using System.IO;
using System.Linq;
using StbImageWriteSharp;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtVolume3DViewer : IDatasetViewer, IDisposable
    {
        // --- DATASET REFERENCES (THE CORE FIX) ---
        private readonly StreamingCtVolumeDataset _streamingDataset;
        private readonly CtImageStackDataset _editableDataset;

        // Veldrid resources
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private DeviceBuffer _constantBuffer;
        private Pipeline _pipeline;
        private ResourceLayout _resourceLayout;
        private ResourceSet _resourceSet;
        private Shader[] _shaders;
        private Texture _volumeTexture; // This will now be the base LOD texture from the .gvt file
        private Texture _labelTexture;
        private Texture _colorMapTexture;
        private Texture _materialParamsTexture;
        private Texture _materialColorsTexture;
        private Sampler _volumeSampler;
        private CommandList _commandList;
        private Texture _renderTexture;
        private Framebuffer _framebuffer;
        private TextureManager _renderTextureManager;

        // Camera and interaction
        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projMatrix;
        private Vector3 _cameraPosition = new(0.5f, 0.5f, 2.5f);
        private Vector3 _cameraTarget = new(0.5f);
        private float _cameraYaw = -MathF.PI / 4f;
        private float _cameraPitch = MathF.PI / 6f;
        private float _cameraDistance = 2.0f;
        private bool _isDragging = false;
        private bool _isPanning = false;
        private Vector2 _lastMousePos;

        // Rendering parameters
        public float StepSize = 1.0f;
        public float MinThreshold = 0.1f;
        public float MaxThreshold = 1.0f;
        public bool ShowGrayscale = true;
        public int ColorMapIndex = 0;
        public bool ShowSlices = false;
        public Vector3 SlicePositions = new(0.5f);
        public bool CutXEnabled;
        public bool CutYEnabled;
        public bool CutZEnabled;
        public bool CutXForward = true;
        public bool CutYForward = true;
        public bool CutZForward = true;
        public float CutXPosition = 0.5f;
        public float CutYPosition = 0.5f;
        public float CutZPosition = 0.5f;
        public bool ClippingEnabled;
        public Vector3 ClippingNormal = -Vector3.UnitZ;
        public float ClippingDistance = 0.5f;
        public bool ClippingMirror;

        private bool _materialParamsDirty = true;
        private bool _labelsDirty = true;
        private readonly CtVolume3DControlPanel _controlPanel;
        
        private const long VRAM_BUDGET_LABELS = 512L * 1024 * 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct VolumeConstants
        {
            public Matrix4x4 InvViewProj;
            public Vector4 CameraPosition;
            public Vector4 VolumeSize;
            public Vector4 ThresholdParams;
            public Vector4 SliceParams;
            public Vector4 RenderParams;
            public Vector4 CutPlaneX;
            public Vector4 CutPlaneY;
            public Vector4 CutPlaneZ;
            public Vector4 ClippingPlane;
            public Vector4 ClippingParams;
        }

        // --- THE FIX IS HERE: THE CONSTRUCTOR SIGNATURE IS NOW CORRECT ---
        public CtVolume3DViewer(StreamingCtVolumeDataset dataset)
        {
            _streamingDataset = dataset;
            _editableDataset = dataset.EditablePartner; 
            if (_editableDataset == null)
            {
                throw new InvalidOperationException("StreamingCtVolumeDataset must have a valid EditablePartner for 3D viewing.");
            }

            _streamingDataset.Load();
            _editableDataset.Load();
            
            // The control panel needs the editable dataset to access the material list
            _controlPanel = new CtVolume3DControlPanel(this, _editableDataset);
            InitializeVeldridResources();

            ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
        }

        private void OnDatasetDataChanged(Dataset dataset)
        {
            if (dataset == _editableDataset)
            {
                Logger.Log("[CtVolume3DViewer] Received notification that labels have changed. Marking for re-upload.");
                MarkLabelsAsDirty();
            }
        }

        #region Resource Creation and Management

        private void InitializeVeldridResources()
        {
            var factory = VeldridManager.Factory;
            _renderTexture = factory.CreateTexture(TextureDescription.Texture2D(1280, 720, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
            _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, _renderTexture));
            _renderTextureManager = TextureManager.CreateFromTexture(_renderTexture);
            CreateCubeGeometry(factory);
            CreateShaders(factory);
            CreatePipeline(factory);
            CreateVolumeTextures(factory);
            _constantBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<VolumeConstants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            CreateResourceSet(factory);
            _commandList = factory.CreateCommandList();
            UpdateCameraMatrices();
        }

        private void CreateVolumeTextures(ResourceFactory factory)
        {
            // Grayscale texture comes from the streaming dataset's pre-calculated base LOD
            var baseLodInfo = _streamingDataset.BaseLod;
            var desc = TextureDescription.Texture3D((uint)baseLodInfo.Width, (uint)baseLodInfo.Height, (uint)baseLodInfo.Depth, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled);
            _volumeTexture = factory.CreateTexture(desc);
            VeldridManager.GraphicsDevice.UpdateTexture(_volumeTexture, _streamingDataset.BaseLodVolumeData, 0, 0, 0, (uint)baseLodInfo.Width, (uint)baseLodInfo.Height, (uint)baseLodInfo.Depth, 0, 0);
            
            // Label texture comes from the editable partner dataset
            _labelTexture = CreateDownsampledTexture3D(factory, _editableDataset.LabelData, VRAM_BUDGET_LABELS);
            
            _volumeSampler = factory.CreateSampler(new SamplerDescription(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerFilter.MinLinear_MagLinear_MipPoint, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));
            CreateColorMapTexture(factory);
            CreateMaterialTextures(factory);
        }
        
        private void CreateCubeGeometry(ResourceFactory factory)
        {
            Vector3[] vertices = { new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0), new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1) };
            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);
            ushort[] indices = { 0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4, 3,6,2,3,7,6, 0,7,3,0,4,7, 1,2,6,1,6,5 };
            _indexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);
        }

       private void CreateShaders(ResourceFactory factory)
{
    // Embed shaders directly in code to avoid file path issues
    string vertexShaderGlsl = @"
#version 450

layout(location = 0) in vec3 in_Position;
layout(location = 0) out vec3 out_TexCoord;

void main() 
{
    out_TexCoord = in_Position;
    gl_Position = vec4(in_Position * 2.0 - 1.0, 1.0);
    gl_Position.y = -gl_Position.y;
}";

    string fragmentShaderGlsl = @"
#version 450

layout(location = 0) in vec3 in_TexCoord;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform Constants
{
    mat4 InvViewProj;
    vec4 CameraPosition;
    vec4 VolumeSize;
    vec4 ThresholdParams;
    vec4 SliceParams;
    vec4 RenderParams;
    vec4 CutPlaneX;
    vec4 CutPlaneY;
    vec4 CutPlaneZ;
    vec4 ClippingPlane;
    vec4 ClippingParams;
};

layout(set = 0, binding = 1) uniform sampler VolumeSampler;
layout(set = 0, binding = 2) uniform texture3D VolumeTexture;
layout(set = 0, binding = 3) uniform texture3D LabelTexture;
layout(set = 0, binding = 4) uniform texture1D ColorMapTexture;
layout(set = 0, binding = 5) uniform texture1D MaterialParamsTexture;
layout(set = 0, binding = 6) uniform texture1D MaterialColorsTexture;

bool IntersectBox(vec3 rayOrigin, vec3 rayDir, vec3 boxMin, vec3 boxMax, out float tNear, out float tFar)
{
    vec3 invRayDir = 1.0 / rayDir;
    vec3 t1 = (boxMin - rayOrigin) * invRayDir;
    vec3 t2 = (boxMax - rayOrigin) * invRayDir;
    vec3 tMin = min(t1, t2);
    vec3 tMax = max(t1, t2);
    tNear = max(max(tMin.x, tMin.y), tMin.z);
    tFar = min(min(tMax.x, tMax.y), tMax.z);
    return tFar > tNear && tFar > 0.0;
}

bool IsCutByPlanes(vec3 pos)
{
    if (CutPlaneX.x > 0.5 && (pos.x - CutPlaneX.z) * CutPlaneX.y > 0.0) return true;
    if (CutPlaneY.x > 0.5 && (pos.y - CutPlaneY.z) * CutPlaneY.y > 0.0) return true;
    if (CutPlaneZ.x > 0.5 && (pos.z - CutPlaneZ.z) * CutPlaneZ.y > 0.0) return true;
    return false;
}

bool IsClipped(vec3 pos)
{
    if (ClippingParams.x < 0.5) return false;
    float dist = dot(pos, ClippingPlane.xyz) - ClippingPlane.w;
    return ClippingParams.y > 0.5 ? dist < 0.0 : dist > 0.0;
}

vec4 ApplyColorMap(float intensity)
{
    float mapOffset = RenderParams.x * 256.0;
    float samplePos = (mapOffset + intensity * 255.0) / 1024.0;
    return textureLod(sampler1D(ColorMapTexture, VolumeSampler), samplePos, 0.0);
}

void main()
{
    vec4 worldPos = InvViewProj * vec4(in_TexCoord.xy * 2.0 - 1.0, 0.0, 1.0);
    worldPos /= worldPos.w;
    vec3 rayOrigin = CameraPosition.xyz;
    vec3 rayDir = normalize(worldPos.xyz - rayOrigin);

    float tNear, tFar;
    if (!IntersectBox(rayOrigin, rayDir, vec3(0.0), vec3(1.0), tNear, tFar))
    {
        discard;
    }
    tNear = max(tNear, 0.0);

    vec4 accumulatedColor = vec4(0.0);
    float t = tNear;
    float step = ThresholdParams.z / length(VolumeSize.xyz);
    
    // Calculate texture dimensions for texelFetch
    ivec3 volumeTexSize = textureSize(sampler3D(VolumeTexture, VolumeSampler), 0);
    ivec3 labelTexSize = textureSize(sampler3D(LabelTexture, VolumeSampler), 0);

    // Limit iterations to prevent unroll issues
    int maxSteps = min(512, int((tFar - tNear) / step) + 1);
    
    for (int i = 0; i < maxSteps; i++)
    {
        if (t > tFar || accumulatedColor.a > 0.99) break;

        vec3 currentPos = rayOrigin + t * rayDir;
        
        if (any(lessThan(currentPos, vec3(0.0))) || any(greaterThan(currentPos, vec3(1.0))) || 
            IsCutByPlanes(currentPos) || IsClipped(currentPos))
        {
            t += step;
            continue;
        }

        vec4 sampledColor = vec4(0.0);
        
        // Use texelFetch instead of texture to avoid gradient issues
        ivec3 labelCoord = ivec3(currentPos * vec3(labelTexSize));
        labelCoord = clamp(labelCoord, ivec3(0), labelTexSize - 1);
        int materialId = int(texelFetch(sampler3D(LabelTexture, VolumeSampler), labelCoord, 0).r * 255.0 + 0.5);
        
        vec2 materialParams = texelFetch(sampler1D(MaterialParamsTexture, VolumeSampler), materialId, 0).xy;

        if (materialId > 0 && materialParams.x > 0.5)
        {
            vec4 materialColor = texelFetch(sampler1D(MaterialColorsTexture, VolumeSampler), materialId, 0);
            materialColor.a *= materialParams.y;
            sampledColor = materialColor;
        }
        else if (ThresholdParams.w > 0.5)
        {
            // Use texelFetch for volume texture too
            ivec3 volumeCoord = ivec3(currentPos * vec3(volumeTexSize));
            volumeCoord = clamp(volumeCoord, ivec3(0), volumeTexSize - 1);
            float intensity = texelFetch(sampler3D(VolumeTexture, VolumeSampler), volumeCoord, 0).r;
            
            if (intensity >= ThresholdParams.x && intensity <= ThresholdParams.y)
            {
                float normIntensity = (intensity - ThresholdParams.x) / (ThresholdParams.y - ThresholdParams.x);
                if (RenderParams.x > 0)
                {
                    sampledColor = ApplyColorMap(normIntensity);
                }
                else
                {
                    sampledColor = vec4(vec3(normIntensity), normIntensity);
                }
                sampledColor.a *= 0.1;
            }
        }
        
        if (sampledColor.a > 0.0)
        {
            sampledColor.rgb *= sampledColor.a;
            accumulatedColor += (1.0 - accumulatedColor.a) * sampledColor;
        }
        
        t += step;
    }

    if (SliceParams.w > 0.5)
    {
        vec3 invDir = 1.0 / rayDir;
        vec3 tSlice = (SliceParams.xyz - rayOrigin) * invDir;
        float tIntersect = min(tSlice.x, min(tSlice.y, tSlice.z));
        if (tIntersect > tNear && tIntersect < tFar)
        {
            vec3 intersectPos = rayOrigin + tIntersect * rayDir;
            if (!IsCutByPlanes(intersectPos) && !IsClipped(intersectPos))
            {
                // Use textureLod for the slice visualization
                float intensity = textureLod(sampler3D(VolumeTexture, VolumeSampler), intersectPos, 0.0).r;
                out_Color = vec4(vec3(intensity), 1.0);
                return;
            }
        }
    }

    out_Color = accumulatedColor;
}";

    try
    {
        // Use CreateFromSpirv with GLSL source - Veldrid.SPIRV will compile it
        _shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, 
                System.Text.Encoding.UTF8.GetBytes(vertexShaderGlsl), "main"),
            new ShaderDescription(ShaderStages.Fragment, 
                System.Text.Encoding.UTF8.GetBytes(fragmentShaderGlsl), "main")
        );
    }
    catch (Exception ex)
    {
        Logger.LogError($"[CtVolume3DViewer] Failed to create shaders: {ex.Message}");
        throw new InvalidOperationException("Failed to create shaders for 3D volume rendering", ex);
    }
}

        private void CreatePipeline(ResourceFactory factory)
        {
            _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("LabelTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorMapTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialParamsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialColorsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(false, false, ComparisonKind.Always),
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, _shaders),
                new[] { _resourceLayout }, _framebuffer.OutputDescription));
        }
        
        private void CreateColorMapTexture(ResourceFactory factory) 
        {
            const int mapSize = 256;
            const int numMaps = 4;
            var colorMapData = new RgbaFloat[mapSize * numMaps];
            for (int i = 0; i < mapSize; i++) { float v = i / (float)(mapSize - 1); colorMapData[i] = new RgbaFloat(v, v, v, 1); }
            for (int i = 0; i < mapSize; i++) { float t = i / (float)(mapSize - 1); float r = Math.Min(1.0f, 3.0f * t); float g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f); float b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f); colorMapData[mapSize * 1 + i] = new RgbaFloat(r, g, b, 1); }
            for (int i = 0; i < mapSize; i++) { float t = i / (float)(mapSize - 1); colorMapData[mapSize * 2 + i] = new RgbaFloat(t, 1 - t, 1, 1); }
            for (int i = 0; i < mapSize; i++) { float h = (i / (float)(mapSize - 1)) * 0.7f; colorMapData[mapSize * 3 + i] = HsvToRgb(h, 1.0f, 1.0f); }
            _colorMapTexture = factory.CreateTexture(TextureDescription.Texture1D((uint)(mapSize * numMaps), 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            VeldridManager.GraphicsDevice.UpdateTexture(_colorMapTexture, colorMapData, 0, 0, 0, (uint)(mapSize * numMaps), 1, 1, 0, 0);
        }

        private void CreateMaterialTextures(ResourceFactory factory)
        {
            _materialParamsTexture = factory.CreateTexture(TextureDescription.Texture1D(256, 1, 1, PixelFormat.R32_G32_Float, TextureUsage.Sampled));
            _materialColorsTexture = factory.CreateTexture(TextureDescription.Texture1D(256, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            UpdateMaterialTextures();
        }

        private void CreateResourceSet(ResourceFactory factory)
        {
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout,
                _constantBuffer, _volumeSampler, _volumeTexture, _labelTexture, _colorMapTexture, _materialParamsTexture, _materialColorsTexture));
        }

        #endregion

        #region Drawing and Interaction

        private void UpdateCameraMatrices()
        {
            _cameraPosition = _cameraTarget + new Vector3(
                MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch),
                MathF.Sin(_cameraPitch),
                MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch)
            ) * _cameraDistance;
            _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, Vector3.UnitY);
            _projMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, _renderTexture.Width / (float)_renderTexture.Height, 0.1f, 1000f);
        }

        private void UpdateConstantBuffer()
        {
            if (!Matrix4x4.Invert(_viewMatrix * _projMatrix, out var invViewProj)) { invViewProj = Matrix4x4.Identity; }
            var constants = new VolumeConstants
            {
                InvViewProj = invViewProj,
                CameraPosition = new Vector4(_cameraPosition, 1),
                VolumeSize = new Vector4(_editableDataset.Width, _editableDataset.Height, _editableDataset.Depth, 0),
                ThresholdParams = new Vector4(MinThreshold, MaxThreshold, StepSize, ShowGrayscale ? 1 : 0),
                SliceParams = new Vector4(SlicePositions, ShowSlices ? 1 : 0),
                RenderParams = new Vector4(ColorMapIndex, 0, 0, 0),
                CutPlaneX = new Vector4(CutXEnabled ? 1 : 0, CutXForward ? 1 : -1, CutXPosition, 0),
                CutPlaneY = new Vector4(CutYEnabled ? 1 : 0, CutYForward ? 1 : -1, CutYPosition, 0),
                CutPlaneZ = new Vector4(CutZEnabled ? 1 : 0, CutZForward ? 1 : -1, CutZPosition, 0),
                ClippingPlane = new Vector4(ClippingNormal, ClippingDistance),
                ClippingParams = new Vector4(ClippingEnabled ? 1 : 0, ClippingMirror ? 1 : 0, 0, 0)
            };
            VeldridManager.GraphicsDevice.UpdateBuffer(_constantBuffer, 0, ref constants);
        }

        private void UpdateMaterialTextures()
        {
            if (!_materialParamsDirty) return;
            var paramData = new Vector2[256];
            var colorData = new RgbaFloat[256];
            for (int i = 0; i < 256; i++)
            {
                var material = _editableDataset.Materials.FirstOrDefault(m => m.ID == i);
                if (material != null)
                {
                    paramData[i] = new Vector2(material.IsVisible ? 1.0f : 0.0f, 1.0f);
                    colorData[i] = new RgbaFloat(material.Color);
                }
                else
                {
                    paramData[i] = new Vector2(0, 1);
                    colorData[i] = RgbaFloat.Black;
                }
            }
            VeldridManager.GraphicsDevice.UpdateTexture(_materialParamsTexture, paramData, 0, 0, 0, 256, 1, 1, 0, 0);
            VeldridManager.GraphicsDevice.UpdateTexture(_materialColorsTexture, colorData, 0, 0, 0, 256, 1, 1, 0, 0);
            _materialParamsDirty = false;
        }
        
        private void ReuploadLabelData()
        {
            if (_labelTexture != null)
            {
                _labelTexture.Dispose();
                _resourceSet.Dispose(); 
            }

            _labelTexture = CreateDownsampledTexture3D(VeldridManager.Factory, _editableDataset.LabelData, VRAM_BUDGET_LABELS);
            CreateResourceSet(VeldridManager.Factory);
            _labelsDirty = false;
        }

        private void Render()
        {
            if (_labelsDirty) ReuploadLabelData();

            _commandList.Begin();
            _commandList.SetFramebuffer(_framebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            UpdateConstantBuffer();
            if (_materialParamsDirty) UpdateMaterialTextures();
            _commandList.SetPipeline(_pipeline);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            _commandList.SetGraphicsResourceSet(0, _resourceSet);
            _commandList.DrawIndexed(36, 1, 0, 0, 0);
            _commandList.End();
            VeldridManager.GraphicsDevice.SubmitCommands(_commandList);
            VeldridManager.GraphicsDevice.WaitForIdle();
        }

        public void DrawToolbarControls()
        {
            if (ImGui.Button("Reset Camera"))
            {
                _cameraTarget = new Vector3(0.5f);
                _cameraYaw = -MathF.PI / 4f;
                _cameraPitch = MathF.PI / 6f;
                _cameraDistance = 2.0f;
                UpdateCameraMatrices();
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            Render();
            var textureId = _renderTextureManager.GetImGuiTextureId();
            if (textureId != IntPtr.Zero)
            {
                var availableSize = ImGui.GetContentRegionAvail();
                ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));
                if (ImGui.IsItemHovered()) HandleMouseInput();
            }
        }

        private void HandleMouseInput()
        {
            var io = ImGui.GetIO();
            if (io.MouseWheel != 0) _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * 0.1f), 0.5f, 20.0f);
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                if (!_isDragging && !_isPanning) { _isDragging = ImGui.IsMouseDown(ImGuiMouseButton.Left); _isPanning = ImGui.IsMouseDown(ImGuiMouseButton.Right); _lastMousePos = io.MousePos; }
                var delta = io.MousePos - _lastMousePos;
                if (_isDragging) { _cameraYaw -= delta.X * 0.01f; _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.01f, -MathF.PI / 2.01f, MathF.PI / 2.01f); }
                if (_isPanning) { Matrix4x4.Invert(_viewMatrix, out var invView); var right = Vector3.Normalize(new Vector3(invView.M11, invView.M12, invView.M13)); var up = Vector3.Normalize(new Vector3(invView.M21, invView.M22, invView.M23)); float panSpeed = _cameraDistance * 0.001f; _cameraTarget -= right * delta.X * panSpeed; _cameraTarget += up * delta.Y * panSpeed; }
                _lastMousePos = io.MousePos;
            }
            else { _isDragging = _isPanning = false; }
            UpdateCameraMatrices();
        }

        #endregion

        #region Public Control Methods

        public void MarkLabelsAsDirty() => _labelsDirty = true;
        public bool GetMaterialVisibility(byte id) => _editableDataset.Materials.FirstOrDefault(m => m.ID == id)?.IsVisible ?? false;
        public float GetMaterialOpacity(byte id) => 1.0f;
        public void SetMaterialVisibility(byte id, bool visible) { var mat = _editableDataset.Materials.FirstOrDefault(m => m.ID == id); if (mat != null) { mat.IsVisible = visible; _materialParamsDirty = true; } }
        public void SetMaterialOpacity(byte id, float opacity) { _materialParamsDirty = true; }
        public void SetAllMaterialsVisibility(bool visible) { foreach (var mat in _editableDataset.Materials.Where(m => m.ID != 0)) mat.IsVisible = visible; _materialParamsDirty = true; }
        public void ResetAllMaterialOpacities() { _materialParamsDirty = true; }

        public void SaveScreenshot(string filePath)
        {
            var device = VeldridManager.GraphicsDevice;
            var stagingDesc = new TextureDescription(_renderTexture.Width, _renderTexture.Height, 1, 1, 1, _renderTexture.Format, TextureUsage.Staging, TextureType.Texture2D);
            var stagingTexture = device.ResourceFactory.CreateTexture(stagingDesc);
            var cl = device.ResourceFactory.CreateCommandList();
            cl.Begin();
            cl.CopyTexture(_renderTexture, stagingTexture);
            cl.End();
            device.SubmitCommands(cl);
            device.WaitForIdle();
            MappedResource mapped = device.Map(stagingTexture, MapMode.Read);
            try
            {
                uint width = stagingTexture.Width;
                uint height = stagingTexture.Height;
                uint rowPitch = mapped.RowPitch;
                uint bytesPerPixel = 4;
                byte[] imageData = new byte[width * height * bytesPerPixel];
                for (uint y = 0; y < height; y++)
                {
                    IntPtr sourceRowPtr = IntPtr.Add(mapped.Data, (int)(y * rowPitch));
                    int destIndex = (int)(y * width * bytesPerPixel);
                    Marshal.Copy(sourceRowPtr, imageData, destIndex, (int)(width * bytesPerPixel));
                }
                using (var stream = File.Create(filePath))
                {
                    var writer = new ImageWriter();
                    writer.WritePng(imageData, (int)width, (int)height, ColorComponents.RedGreenBlueAlpha, stream);
                }
                Logger.Log($"[CtVolume3DViewer] Screenshot saved to {filePath}");
            }
            catch (Exception e) { Logger.LogError($"[CtVolume3DViewer] Failed to save screenshot: {e.Message}"); }
            finally { device.Unmap(stagingTexture); stagingTexture.Dispose(); cl.Dispose(); }
        }
        
        #endregion
        
        #region Helpers

        private Texture CreateDownsampledTexture3D(ResourceFactory factory, IVolumeData volume, long budget)
        {
            if (volume == null)
            {
                var dummy = factory.CreateTexture(TextureDescription.Texture3D(1, 1, 1, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled));
                VeldridManager.GraphicsDevice.UpdateTexture(dummy, new byte[]{0}, 0,0,0,1,1,1,0,0);
                return dummy;
            }

            long neededMemory = (long)volume.Width * volume.Height * volume.Depth;
            int factor = 1;
            while (neededMemory / (factor * factor * factor) > budget && factor < 16) { factor *= 2; }

            uint w = (uint)Math.Max(1, volume.Width / factor);
            uint h = (uint)Math.Max(1, volume.Height / factor);
            uint d = (uint)Math.Max(1, volume.Depth / factor);
            var desc = TextureDescription.Texture3D(w, h, d, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled);
            var texture = factory.CreateTexture(desc);

            if (factor == 1)
            {
                Logger.Log($"[CtVolume3DViewer] Uploading {volume.GetType().Name} at full resolution ({w}x{h}x{d}).");
                var fullData = (volume as ChunkedVolume)?.GetAllData() ?? (volume as ChunkedLabelVolume)?.GetAllData();
                VeldridManager.GraphicsDevice.UpdateTexture(texture, fullData, 0, 0, 0, w, h, d, 0, 0);
            }
            else
            {
                Logger.LogWarning($"[CtVolume3DViewer] Volume is too large. Downsampling by {factor}x to {w}x{h}x{d}.");
                byte[] downsampledData = new byte[w * h * d];
                Parallel.For(0, d, z =>
                {
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        downsampledData[z * w * h + y * w + x] = volume[x * factor, y * factor, (int)z * factor];
                });
                VeldridManager.GraphicsDevice.UpdateTexture(texture, downsampledData, 0, 0, 0, w, h, d, 0, 0);
            }
            return texture;
        }

        private static RgbaFloat HsvToRgb(float h, float s, float v)
        {
            float r, g, b;
            int i = (int)(h * 6);
            float f = h * 6 - i;
            float p = v * (1 - s);
            float q = v * (1 - f * s);
            float t = v * (1 - (1 - f) * s);
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return new RgbaFloat(r, g, b, 1.0f);
        }

        #endregion

        public void Dispose()
        {
            ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
            _controlPanel?.Dispose();
            _commandList?.Dispose();
            _renderTextureManager?.Dispose();
            _resourceSet?.Dispose();
            _resourceLayout?.Dispose();
            _volumeSampler?.Dispose();
            _materialColorsTexture?.Dispose();
            _materialParamsTexture?.Dispose();
            _labelTexture?.Dispose();
            _colorMapTexture?.Dispose();
            _volumeTexture?.Dispose();
            _framebuffer?.Dispose();
            _renderTexture?.Dispose();
            _pipeline?.Dispose();
            if (_shaders != null) { foreach (var shader in _shaders) shader?.Dispose(); }
            _constantBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _vertexBuffer?.Dispose();
        }
    }
}