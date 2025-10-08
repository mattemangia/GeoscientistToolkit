// GeoscientistToolkit/Data/CtImageStack/CtVolume3DViewer.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;
using Veldrid;
using Veldrid.SPIRV;

namespace GeoscientistToolkit.Data.CtImageStack;

public class ClippingPlane
{
    public ClippingPlane(string name)
    {
        Name = name;
        Normal = -Vector3.UnitZ;
        Distance = 0.5f;
        Enabled = true;
        Mirror = false;
        Rotation = Vector3.Zero;
    }

    public string Name { get; set; }
    public Vector3 Normal { get; set; }
    public float Distance { get; set; }
    public bool Enabled { get; set; }
    public bool Mirror { get; set; }
    public Vector3 Rotation { get; set; } // Euler angles for UI
    public bool IsVisualizationVisible { get; set; } = true;
}

public class CtVolume3DViewer : IDatasetViewer, IDisposable
{
    private const long VRAM_BUDGET_LABELS = 512L * 1024 * 1024;
    internal const int MAX_CLIPPING_PLANES = 8;
    private readonly CtVolume3DControlPanel _controlPanel;
    internal readonly CtImageStackDataset _editableDataset;

    private readonly Dictionary<byte, float> _materialOpacity = new();

    // --- Renderer specifico per piattaforma ---
    private readonly MetalVolumeRenderer _metalRenderer;
    private readonly ImGuiExportFileDialog _screenshotDialog;

    // --- DATASET REFERENCES ---
    private readonly StreamingCtVolumeDataset _streamingDataset;
    private float _cameraDistance = 2.0f;
    private float _cameraPitch = MathF.PI / 6f;
    private Vector3 _cameraPosition = new(0.5f, 0.5f, 2.5f);
    private Vector3 _cameraTarget = new(0.5f);
    private float _cameraYaw = -MathF.PI / 4f;
    private Texture _colorMapTexture;
    private CommandList _commandList;
    private DeviceBuffer _constantBuffer;
    private Texture _depthTexture;
    private Framebuffer _framebuffer;
    private DeviceBuffer _indexBuffer;
    private bool _isDragging;
    private bool _isPanning;
    private bool _labelsDirty;
    private Texture _labelTexture;
    private Vector2 _lastMousePos;
    private Texture _materialColorsTexture;

    private bool _materialParamsDirty = true;
    private Texture _materialParamsTexture;
    private Pipeline _pipeline;
    private DeviceBuffer _planeVisualizationConstantBuffer;
    private DeviceBuffer _planeVisualizationIndexBuffer;
    private Pipeline _planeVisualizationPipeline;
    private ResourceLayout _planeVisualizationResourceLayout;
    private ResourceSet _planeVisualizationResourceSet;
    private Shader[] _planeVisualizationShaders;
    private DeviceBuffer _planeVisualizationVertexBuffer;
    internal Vector4 _previewColor = new(1, 0, 0, 0.5f);
    private bool _previewDirty;
    private Texture _previewTexture;
    private Matrix4x4 _projMatrix;
    private Texture _renderTexture;
    private TextureManager _renderTextureManager;
    private ResourceLayout _resourceLayout;
    private ResourceSet _resourceSet;
    private Shader[] _shaders;

    // Preview state
    internal bool _showPreview;

    // Veldrid resources (per renderer standard)
    private DeviceBuffer _vertexBuffer;

    // Camera and interaction
    private Matrix4x4 _viewMatrix;
    private Sampler _volumeSampler;
    private Texture _volumeTexture;
    public int ColorMapIndex = 0;

    // Axis-aligned cutting planes
    public bool CutXEnabled;
    public bool CutXForward = true;
    public float CutXPosition = 0.5f;
    public bool CutYEnabled;
    public bool CutYForward = true;
    public float CutYPosition = 0.5f;
    public bool CutZEnabled;
    public bool CutZForward = true;
    public float CutZPosition = 0.5f;
    public float MaxThreshold = 0.8f;
    public float MinThreshold = 0.05f;
    public bool ShowGrayscale = true;
    public bool ShowSlices = false;
    public Vector3 SlicePositions = new(0.5f);

    // Rendering parameters
    public float StepSize = 2.0f;

    public CtVolume3DViewer(StreamingCtVolumeDataset dataset)
    {
        _streamingDataset = dataset;
        _editableDataset = dataset.EditablePartner;
        if (_editableDataset == null)
            throw new InvalidOperationException(
                "StreamingCtVolumeDataset must have a valid EditablePartner for 3D viewing.");

        _streamingDataset.Load();
        _editableDataset.Load();

        Logger.Log(
            $"[CtVolume3DViewer] Streaming dataset LOD dimensions: {_streamingDataset.BaseLod.Width}x{_streamingDataset.BaseLod.Height}x{_streamingDataset.BaseLod.Depth}");
        Logger.Log(
            $"[CtVolume3DViewer] Editable dataset dimensions: {_editableDataset.Width}x{_editableDataset.Height}x{_editableDataset.Depth}");

        // Initialize material opacities
        foreach (var material in _editableDataset.Materials) _materialOpacity[material.ID] = 1.0f;

        _controlPanel = new CtVolume3DControlPanel(this, _editableDataset);
        _screenshotDialog = new ImGuiExportFileDialog("ScreenshotDialog3D", "Save Screenshot");
        _screenshotDialog.SetExtensions((".png", "PNG Image"));

        if (VeldridManager.GraphicsDevice.BackendType == GraphicsBackend.Metal)
            _metalRenderer = new MetalVolumeRenderer(this);

        InitializeVeldridResources();

        ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
        CtImageStackTools.Preview3DChanged += OnPreview3DChanged;
    }

    public bool ShowCutXPlaneVisual { get; set; } = true;
    public bool ShowCutYPlaneVisual { get; set; } = true;
    public bool ShowCutZPlaneVisual { get; set; } = true;

    // Multiple arbitrary clipping planes
    public List<ClippingPlane> ClippingPlanes { get; } = new();
    public bool ShowPlaneVisualizations { get; set; } = true;

    public void Dispose()
    {
        ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
        CtImageStackTools.Preview3DChanged -= OnPreview3DChanged;

        _metalRenderer?.Dispose();
        _controlPanel?.Dispose();
        _commandList?.Dispose();
        _renderTextureManager?.Dispose();

        _planeVisualizationResourceSet?.Dispose();
        _planeVisualizationResourceLayout?.Dispose();
        _planeVisualizationPipeline?.Dispose();
        if (_planeVisualizationShaders != null)
            foreach (var shader in _planeVisualizationShaders)
                shader?.Dispose();
        _planeVisualizationIndexBuffer?.Dispose();
        _planeVisualizationVertexBuffer?.Dispose();
        _planeVisualizationConstantBuffer?.Dispose();

        _resourceSet?.Dispose();
        _resourceLayout?.Dispose();
        _volumeSampler?.Dispose();
        _materialColorsTexture?.Dispose();
        _materialParamsTexture?.Dispose();
        _labelTexture?.Dispose();
        _previewTexture?.Dispose();
        _colorMapTexture?.Dispose();
        _volumeTexture?.Dispose();
        _depthTexture?.Dispose();
        _framebuffer?.Dispose();
        _renderTexture?.Dispose();

        _pipeline?.Dispose();
        if (_shaders != null)
            foreach (var shader in _shaders)
                shader?.Dispose();

        _constantBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer?.Dispose();
    }

    private void OnDatasetDataChanged(Dataset dataset)
    {
        if (dataset == _editableDataset)
        {
            Logger.Log("[CtVolume3DViewer] Dataset changed - updating materials and labels");

            // Force reload of material parameters
            _materialParamsDirty = true;

            // Mark labels for re-upload
            MarkLabelsAsDirty();

            // Update material visibility from dataset
            foreach (var material in _editableDataset.Materials)
                if (material.ID != 0) // Skip exterior
                    // Sync visibility state
                    SetMaterialVisibility(material.ID, material.IsVisible);
        }
    }

    private void OnPreview3DChanged(CtImageStackDataset dataset, byte[] previewMask, Vector4 color)
    {
        if (dataset == _editableDataset)
        {
            _showPreview = previewMask != null;
            _previewColor = color;
            _previewDirty = true;

            if (previewMask != null) UpdatePreviewTexture(previewMask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VolumeConstants
    {
        public Matrix4x4 ViewProj;
        public Matrix4x4 InvView;
        public Vector4 CameraPosition;
        public Vector4 VolumeSize;
        public Vector4 ThresholdParams;
        public Vector4 SliceParams;
        public Vector4 RenderParams;
        public Vector4 CutPlaneX;
        public Vector4 CutPlaneY;
        public Vector4 CutPlaneZ;
        public fixed float ClippingPlanesData[32];
        public Vector4 ClippingPlanesInfo;
        public Vector4 PreviewParams;
        public Vector4 PreviewAlpha;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PlaneVisualizationConstants
    {
        public Matrix4x4 ViewProj;
        public Vector4 PlaneColor;
    }

    #region Resource Creation and Management

    private void InitializeVeldridResources()
    {
        var factory = VeldridManager.Factory;

        // Shared resources
        _renderTexture = factory.CreateTexture(TextureDescription.Texture2D(1280, 720, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
        var depthDesc = TextureDescription.Texture2D(_renderTexture.Width, _renderTexture.Height, 1, 1,
            PixelFormat.R32_Float, TextureUsage.DepthStencil);
        _depthTexture = factory.CreateTexture(depthDesc);
        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTexture, _renderTexture));
        _renderTextureManager = TextureManager.CreateFromTexture(_renderTexture);

        CreateCubeGeometry(factory);
        CreatePlaneVisualizationGeometry(factory);
        CreateVolumeTextures(factory);

        _commandList = factory.CreateCommandList();

        // Platform-specific renderer initialization
        if (_metalRenderer != null)
        {
            _metalRenderer.InitializeResources(factory, _framebuffer, _volumeTexture, _labelTexture, _previewTexture,
                _volumeSampler);
        }
        else
        {
            // Standard (Windows) renderer initialization
            CreateStandardShaders(factory);
            CreateStandardPipeline(factory);
            CreateStandardMaterialTextures(factory);
            _constantBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<VolumeConstants>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            CreateStandardResourceSet(factory);
        }

        UpdateCameraMatrices();
    }

    private void CreateVolumeTextures(ResourceFactory factory)
    {
        var baseLodInfo = _streamingDataset.BaseLod;
        var volumeData = ReconstructVolumeFromBricks(baseLodInfo, _streamingDataset.BaseLodVolumeData,
            _streamingDataset.BrickSize);
        var desc = TextureDescription.Texture3D((uint)baseLodInfo.Width, (uint)baseLodInfo.Height,
            (uint)baseLodInfo.Depth, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        _volumeTexture = factory.CreateTexture(desc);
        VeldridManager.GraphicsDevice.UpdateTexture(_volumeTexture, volumeData, 0, 0, 0, (uint)baseLodInfo.Width,
            (uint)baseLodInfo.Height, (uint)baseLodInfo.Depth, 0, 0);

        _labelTexture = CreateDownsampledTexture3D(factory, _editableDataset.LabelData, VRAM_BUDGET_LABELS);

        _previewTexture = factory.CreateTexture(desc);
        var emptyData = new byte[baseLodInfo.Width * baseLodInfo.Height * baseLodInfo.Depth];
        VeldridManager.GraphicsDevice.UpdateTexture(_previewTexture, emptyData, 0, 0, 0, (uint)baseLodInfo.Width,
            (uint)baseLodInfo.Height, (uint)baseLodInfo.Depth, 0, 0);

        _volumeSampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));
    }

    private void UpdatePreviewTexture(byte[] previewMask)
    {
        if (previewMask == null || _previewTexture == null) return;
        var baseLod = _streamingDataset.BaseLod;
        var downsampledPreview = DownsampleVolumeData(previewMask, _editableDataset.Width, _editableDataset.Height,
            _editableDataset.Depth, baseLod.Width, baseLod.Height, baseLod.Depth);
        VeldridManager.GraphicsDevice.UpdateTexture(_previewTexture, downsampledPreview, 0, 0, 0, (uint)baseLod.Width,
            (uint)baseLod.Height, (uint)baseLod.Depth, 0, 0);
        _previewDirty = false;
    }

    private byte[] DownsampleVolumeData(byte[] sourceData, int srcW, int srcH, int srcD, int dstW, int dstH, int dstD)
    {
        var result = new byte[dstW * dstH * dstD];
        var scaleX = (float)srcW / dstW;
        var scaleY = (float)srcH / dstH;
        var scaleZ = (float)srcD / dstD;
        Parallel.For(0, dstD, z =>
        {
            for (var y = 0; y < dstH; y++)
            for (var x = 0; x < dstW; x++)
            {
                var srcX = Math.Min((int)(x * scaleX), srcW - 1);
                var srcY = Math.Min((int)(y * scaleY), srcH - 1);
                var srcZ = Math.Min((int)(z * scaleZ), srcD - 1);
                result[z * dstW * dstH + y * dstW + x] = sourceData[srcZ * srcW * srcH + srcY * srcW + srcX];
            }
        });
        return result;
    }

    // In CtVolume3DViewer.cs

    private byte[] ReconstructVolumeFromBricks(GvtLodInfo lodInfo, byte[] brickData, int brickSize)
    {
        var width = lodInfo.Width;
        var height = lodInfo.Height;
        var depth = lodInfo.Depth;
        var bricksX = (width + brickSize - 1) / brickSize;
        var bricksY = (height + brickSize - 1) / brickSize;
        var bricksZ = (depth + brickSize - 1) / brickSize;
        var brickVolumeSize = brickSize * brickSize * brickSize;

        // --- SOLUTION: Parallel-Safe Reconstruction ---
        // Instead of all threads writing to one large shared array (which causes race conditions),
        // we parallelize the reconstruction of independent chunks (Z-slices of bricks).
        // Each thread produces a result, and a final, fast copy assembles these results.
        // This maintains performance while guaranteeing correctness on all platforms.

        // Step 1: Create a container for the independent results from each parallel task.
        var reconstructedSlices = new byte[bricksZ][];

        // Step 2: Process each Z-slice of bricks in parallel.
        Parallel.For(0, bricksZ, bz =>
        {
            // Calculate the dimensions and data for THIS slice only.
            var sliceStartX = 0;
            var sliceStartY = 0;
            var sliceStartZ = bz * brickSize;

            // This is the output buffer for the current thread ONLY. No other thread will touch this.
            var sliceData = new byte[width * height * brickSize];

            for (var by = 0; by < bricksY; by++)
            for (var bx = 0; bx < bricksX; bx++)
            {
                var brickIndex = (bz * bricksY + by) * bricksX + bx;
                var brickOffset = brickIndex * brickVolumeSize;

                if (brickOffset >= brickData.Length) continue; // Safety check

                // Reconstruct one brick into the thread-local sliceData buffer.
                for (var z = 0; z < brickSize; z++)
                {
                    var gz = sliceStartZ + z;
                    if (gz >= depth) continue;

                    for (var y = 0; y < brickSize; y++)
                    {
                        var gy = by * brickSize + y;
                        if (gy >= height) continue;

                        for (var x = 0; x < brickSize; x++)
                        {
                            var gx = bx * brickSize + x;
                            if (gx >= width) continue;

                            var brickLocalIndex = z * brickSize * brickSize + y * brickSize + x;

                            if (brickOffset + brickLocalIndex < brickData.Length)
                            {
                                // Calculate the destination index within the local slice buffer
                                var sliceIndex = z * height * width + gy * width + gx;
                                if (sliceIndex < sliceData.Length)
                                    sliceData[sliceIndex] = brickData[brickOffset + brickLocalIndex];
                            }
                        }
                    }
                }
            }

            // The parallel task is done. Store its result in the shared container.
            // This is a safe write, as each thread has a unique 'bz' index.
            reconstructedSlices[bz] = sliceData;
        });

        // Step 3: All parallel work is finished. Now, assemble the final volume.
        // This is a fast, single-threaded copy from the reconstructed slices into the final array.
        var finalVolumeData = new byte[width * height * depth];
        for (var bz = 0; bz < bricksZ; bz++)
        {
            var slice = reconstructedSlices[bz];
            if (slice == null) continue;

            var sliceDepth = Math.Min(brickSize, depth - bz * brickSize);
            var bytesToCopy = (long)width * height * sliceDepth;
            var destinationOffset = (long)bz * brickSize * width * height;

            if (destinationOffset + bytesToCopy > finalVolumeData.Length)
                bytesToCopy = finalVolumeData.Length - destinationOffset;

            if (bytesToCopy > 0 && bytesToCopy <= slice.Length)
                Buffer.BlockCopy(slice, 0, finalVolumeData, (int)destinationOffset, (int)bytesToCopy);
        }

        return finalVolumeData;
    }

    private void CreateCubeGeometry(ResourceFactory factory)
    {
        Vector3[] vertices =
        {
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 1), new(1, 1, 1),
            new(0, 1, 1)
        };
        _vertexBuffer =
            factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
        VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);
        ushort[] indices =
        {
            0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 3, 2, 6, 3, 6, 7, 0, 7, 4, 0, 3, 7, 1, 5, 6, 1, 6, 2
        };
        _indexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
        VeldridManager.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);
    }

    private void CreatePlaneVisualizationGeometry(ResourceFactory factory)
    {
        Vector3[] vertices = { new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0) };
        _planeVisualizationVertexBuffer =
            factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
        VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationVertexBuffer, 0, vertices);
        ushort[] indices = { 0, 1, 2, 0, 2, 3 };
        _planeVisualizationIndexBuffer =
            factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
        VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationIndexBuffer, 0, indices);
    }

    private void CreateStandardShaders(ResourceFactory factory)
    {
        var vertexShaderGlsl = @"
#version 450
layout(location = 0) in vec3 in_Position;

layout(set = 0, binding = 0) uniform Constants
{
    mat4 ViewProj;
    mat4 InvView;
    vec4 CameraPosition;
};

layout(location = 0) out vec3 out_ModelPos;

void main() 
{
    out_ModelPos = in_Position; 
    gl_Position = ViewProj * vec4(in_Position, 1.0);
}";
        var fragmentShaderGlsl = @"
#version 450
layout(location = 0) in vec3 in_ModelPos;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform Constants
{
    mat4 ViewProj;
    mat4 InvView;
    vec4 CameraPosition;
    vec4 VolumeSize;
    vec4 ThresholdParams;
    vec4 SliceParams;
    vec4 RenderParams;
    vec4 CutPlaneX;
    vec4 CutPlaneY;
    vec4 CutPlaneZ;
    vec4 ClippingPlanesData[8];
    vec4 ClippingPlanesInfo;
    vec4 PreviewParams;
    vec4 PreviewAlpha;
};

layout(set = 0, binding = 1) uniform sampler VolumeSampler;
layout(set = 0, binding = 2) uniform texture3D VolumeTexture;
layout(set = 0, binding = 3) uniform texture3D LabelTexture;
layout(set = 0, binding = 4) uniform texture1D ColorMapTexture;
layout(set = 0, binding = 5) uniform texture1D MaterialParamsTexture;
layout(set = 0, binding = 6) uniform texture1D MaterialColorsTexture;
layout(set = 0, binding = 7) uniform texture3D PreviewTexture;

bool IntersectBox(vec3 rayOrigin, vec3 rayDir, vec3 boxMin, vec3 boxMax, out float tNear, out float tFar)
{
    vec3 invRayDir = 1.0 / (rayDir + 1e-8);
    vec3 t1 = (boxMin - rayOrigin) * invRayDir;
    vec3 t2 = (boxMax - rayOrigin) * invRayDir;
    vec3 tMin = min(t1, t2);
    vec3 tMax = max(t1, t2);
    tNear = max(max(tMin.x, tMin.y), tMin.z);
    tFar = min(min(tMax.x, tMax.y), tMax.z);
    return tFar >= tNear && tFar > 0.0;
}

bool IsCutByPlanes(vec3 pos)
{
    if (CutPlaneX.x > 0.5 && (pos.x - CutPlaneX.z) * CutPlaneX.y > 0.0) return true;
    if (CutPlaneY.x > 0.5 && (pos.y - CutPlaneY.z) * CutPlaneY.y > 0.0) return true;
    if (CutPlaneZ.x > 0.5 && (pos.z - CutPlaneZ.z) * CutPlaneZ.y > 0.0) return true;
    int numPlanes = int(ClippingPlanesInfo.x);
    for (int i = 0; i < numPlanes; i++)
    {
        vec4 planeData = ClippingPlanesData[i];
        if (planeData.w > 0.5)
        {
            vec3 normal = planeData.xyz; float dist = length(normal);
            if (dist > 0.001)
            {
                normal /= dist; float mirror = step(1.5, dist);
                float planeDist = dot(pos - vec3(0.5), normal) - (dist - 0.5 - mirror);
                if (mirror > 0.5 ? planeDist < 0.0 : planeDist > 0.0) return true;
            }
        }
    }
    return false;
}

vec4 ApplyColorMap(float intensity)
{
    float mapOffset = RenderParams.x * 256.0;
    float samplePos = clamp((mapOffset + intensity * 255.0) / 1024.0, 0.0, 1.0);
    return textureLod(sampler1D(ColorMapTexture, VolumeSampler), samplePos, 0.0);
}

void main()
{
    vec3 rayOrigin = CameraPosition.xyz;
    vec3 rayDir = normalize(in_ModelPos - rayOrigin);

    float tNear, tFar;
    if (!IntersectBox(rayOrigin, rayDir, vec3(0.0), vec3(1.0), tNear, tFar))
    {
        discard;
    }
    
    tNear = max(tNear, 0.0);
    vec4 accumulatedColor = vec4(0.0);
    
    float maxDim = max(VolumeSize.x, max(VolumeSize.y, VolumeSize.z));
    float baseStepSize = 1.0 / maxDim;
    float step = baseStepSize * ThresholdParams.z;
    
    int maxSteps = int((tFar - tNear) / step);
    float opacityScalar = 40.0;
    float t = tNear;

    for (int i = 0; i < 768; i++)
    {
        if (i >= maxSteps || t > tFar || accumulatedColor.a > 0.95) break;

        vec3 currentPos = rayOrigin + t * rayDir;
        if (any(lessThan(currentPos, vec3(0.0))) || any(greaterThan(currentPos, vec3(1.0))) || IsCutByPlanes(currentPos))
        {
            t += step;
            continue;
        }

        vec4 sampledColor = vec4(0.0);

        // 1. Base Grayscale Rendering
        if (ThresholdParams.w > 0.5) // Show Grayscale
        {
            float intensity = textureLod(sampler3D(VolumeTexture, VolumeSampler), currentPos, 0.0).r;
            if (intensity >= ThresholdParams.x && intensity <= ThresholdParams.y)
            {
                float normIntensity = (intensity - ThresholdParams.x) / (ThresholdParams.y - ThresholdParams.x + 0.001);
                sampledColor = (RenderParams.x > 0.5) ? ApplyColorMap(normIntensity) : vec4(vec3(normIntensity), normIntensity);
                sampledColor.a = pow(sampledColor.a, 2.0);
            }
        }

        // 2. Material Overlay
        int materialId = int(textureLod(sampler3D(LabelTexture, VolumeSampler), currentPos, 0.0).r * 255.0 + 0.5);
        if (materialId > 0)
        {
            vec2 materialParams = texelFetch(sampler1D(MaterialParamsTexture, VolumeSampler), materialId, 0).xy;
            bool isVisible = materialParams.x > 0.5;
            if (isVisible)
            {
                vec4 materialColor = texelFetch(sampler1D(MaterialColorsTexture, VolumeSampler), materialId, 0);
                float opacity = materialParams.y;
                
                // Blend over grayscale
                sampledColor.rgb = mix(sampledColor.rgb, materialColor.rgb, opacity);
                sampledColor.a = max(sampledColor.a, opacity);
            }
        }

        // 3. Preview Overlay (highest priority)
        if (PreviewParams.x > 0.5 && textureLod(sampler3D(PreviewTexture, VolumeSampler), currentPos, 0.0).r > 0.5)
        {
            vec4 previewRgba = vec4(PreviewParams.yzw, PreviewAlpha.x);
            // Blend over material/grayscale
            sampledColor.rgb = mix(sampledColor.rgb, previewRgba.rgb, previewRgba.a);
            sampledColor.a = max(sampledColor.a, previewRgba.a);
        }
        
        if (sampledColor.a > 0.0)
        {
            float correctedAlpha = clamp(sampledColor.a * step * opacityScalar, 0.0, 1.0);
            accumulatedColor += (1.0 - accumulatedColor.a) * vec4(sampledColor.rgb * correctedAlpha, correctedAlpha);
        }
        t += step;
    }
    out_Color = accumulatedColor;
}";

        var planeVertexShaderGlsl = @"
#version 450
layout(location = 0) in vec3 in_Position;
layout(set = 0, binding = 0) uniform Constants { mat4 ViewProj; vec4 PlaneColor; };
void main() { gl_Position = ViewProj * vec4(in_Position, 1.0); }";
        var planeFragmentShaderGlsl = @"
#version 450
layout(location = 0) out vec4 out_Color;
layout(set = 0, binding = 0) uniform Constants { mat4 ViewProj; vec4 PlaneColor; };
void main() { out_Color = PlaneColor; }";

        try
        {
            var options = new CrossCompileOptions(true, !VeldridManager.GraphicsDevice.IsClipSpaceYInverted);

            var mainVertexDesc =
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexShaderGlsl), "main");
            var mainFragmentDesc =
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentShaderGlsl), "main");
            _shaders = factory.CreateFromSpirv(mainVertexDesc, mainFragmentDesc, options);

            var planeVertexDesc = new ShaderDescription(ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(planeVertexShaderGlsl), "main");
            var planeFragmentDesc = new ShaderDescription(ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(planeFragmentShaderGlsl), "main");
            _planeVisualizationShaders = factory.CreateFromSpirv(planeVertexDesc, planeFragmentDesc, options);

            Logger.Log(
                $"[CtVolume3DViewer] Standard shaders compiled successfully for backend: {VeldridManager.GraphicsDevice.BackendType}.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtVolume3DViewer] Failed to create standard shaders: {ex.Message}");
            throw new InvalidOperationException("Failed to create shaders for 3D volume rendering", ex);
        }
    }

    private void CreateStandardPipeline(ResourceFactory factory)
    {
        _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("VolumeSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("VolumeTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LabelTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ColorMapTexture", ResourceKind.TextureReadOnly,
                ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MaterialParamsTexture", ResourceKind.TextureReadOnly,
                ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MaterialColorsTexture", ResourceKind.TextureReadOnly,
                ShaderStages.Fragment),
            new ResourceLayoutElementDescription("PreviewTexture", ResourceKind.TextureReadOnly,
                ShaderStages.Fragment)));

        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true,
                false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(new VertexElementDescription("Position",
                        VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                }, _shaders),
            new[] { _resourceLayout }, _framebuffer.OutputDescription));

        _planeVisualizationResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment)));

        _planeVisualizationPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            new BlendStateDescription(RgbaFloat.Black, BlendAttachmentDescription.AlphaBlend),
            new DepthStencilStateDescription(true, false, ComparisonKind.Less),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true,
                false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(new VertexElementDescription("Position",
                        VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                }, _planeVisualizationShaders),
            new[] { _planeVisualizationResourceLayout }, _framebuffer.OutputDescription));

        _planeVisualizationConstantBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<PlaneVisualizationConstants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _planeVisualizationResourceSet = factory.CreateResourceSet(
            new ResourceSetDescription(_planeVisualizationResourceLayout, _planeVisualizationConstantBuffer));

        // This texture is created here because it's only needed for the standard pipeline
        CreateStandardColorMapTexture(factory);
    }

    private void CreateStandardColorMapTexture(ResourceFactory factory)
    {
        const int mapSize = 256;
        const int numMaps = 4;
        var colorMapData = new RgbaFloat[mapSize * numMaps];

        for (var i = 0; i < mapSize; i++)
        {
            var v = i / (float)(mapSize - 1);
            colorMapData[i] = new RgbaFloat(v, v, v, 1);
        }

        for (var i = 0; i < mapSize; i++)
        {
            var t = i / (float)(mapSize - 1);
            var r = Math.Min(1.0f, 3.0f * t);
            var g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f);
            var b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f);
            colorMapData[mapSize * 1 + i] = new RgbaFloat(r, g, b, 1);
        }

        for (var i = 0; i < mapSize; i++)
        {
            var t = i / (float)(mapSize - 1);
            colorMapData[mapSize * 2 + i] = new RgbaFloat(t, 1 - t, 1, 1);
        }

        for (var i = 0; i < mapSize; i++)
        {
            var h = i / (float)(mapSize - 1) * 0.7f;
            colorMapData[mapSize * 3 + i] = HsvToRgb(h, 1.0f, 1.0f);
        }

        // Use 1D texture for standard backends
        _colorMapTexture = factory.CreateTexture(TextureDescription.Texture1D(mapSize * numMaps, 1, 1,
            PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
        VeldridManager.GraphicsDevice.UpdateTexture(_colorMapTexture, colorMapData, 0, 0, 0, mapSize * numMaps, 1, 1, 0,
            0);
    }

    private void CreateStandardMaterialTextures(ResourceFactory factory)
    {
        // Use 1D textures for standard backends
        _materialParamsTexture =
            factory.CreateTexture(TextureDescription.Texture1D(256, 1, 1, PixelFormat.R32_G32_Float,
                TextureUsage.Sampled));
        _materialColorsTexture =
            factory.CreateTexture(TextureDescription.Texture1D(256, 1, 1, PixelFormat.R32_G32_B32_A32_Float,
                TextureUsage.Sampled));
        UpdateMaterialTextures();
    }

    private void CreateStandardResourceSet(ResourceFactory factory)
    {
        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _constantBuffer,
            _volumeSampler, _volumeTexture, _labelTexture, _colorMapTexture, _materialParamsTexture,
            _materialColorsTexture, _previewTexture));
    }

    #endregion

    #region Drawing and Interaction

    private void UpdateCameraMatrices()
    {
        _cameraPosition = _cameraTarget + new Vector3(MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch),
            MathF.Sin(_cameraPitch), MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch)) * _cameraDistance;
        _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, Vector3.UnitY);
        var aspectRatio = _renderTexture.Width / (float)Math.Max(1, _renderTexture.Height);
        _projMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspectRatio, 0.1f, 1000f);
    }

    private unsafe void UpdateConstantBuffer()
    {
        Matrix4x4.Invert(_viewMatrix, out var invView);
        var constants = new VolumeConstants
        {
            ViewProj = _viewMatrix * _projMatrix,
            InvView = invView,
            CameraPosition = new Vector4(_cameraPosition, 1),
            VolumeSize = new Vector4(_editableDataset.Width, _editableDataset.Height, _editableDataset.Depth, 0),
            ThresholdParams = new Vector4(MinThreshold, MaxThreshold, StepSize, ShowGrayscale ? 1 : 0),
            SliceParams = new Vector4(SlicePositions, ShowSlices ? 1 : 0),
            RenderParams = new Vector4(ColorMapIndex, 0, 0, 0),
            CutPlaneX = new Vector4(CutXEnabled ? 1 : 0, CutXForward ? 1 : -1, CutXPosition, 0),
            CutPlaneY = new Vector4(CutYEnabled ? 1 : 0, CutYForward ? 1 : -1, CutYPosition, 0),
            CutPlaneZ = new Vector4(CutZEnabled ? 1 : 0, CutZForward ? 1 : -1, CutZPosition, 0),
            ClippingPlanesInfo = new Vector4(0, ShowPlaneVisualizations ? 1 : 0, 0, 0),
            PreviewParams = new Vector4(_showPreview ? 1 : 0, _previewColor.X, _previewColor.Y, _previewColor.Z),
            PreviewAlpha = new Vector4(_previewColor.W, 0, 0, 0)
        };

        var enabledPlanes = 0;
        for (var i = 0; i < Math.Min(ClippingPlanes.Count, MAX_CLIPPING_PLANES); i++)
        {
            var plane = ClippingPlanes[i];
            if (plane.Enabled)
            {
                var dist = plane.Distance + (plane.Mirror ? 1.0f : 0.0f);
                var normal = plane.Normal * dist;
                constants.ClippingPlanesData[enabledPlanes * 4] = normal.X;
                constants.ClippingPlanesData[enabledPlanes * 4 + 1] = normal.Y;
                constants.ClippingPlanesData[enabledPlanes * 4 + 2] = normal.Z;
                constants.ClippingPlanesData[enabledPlanes * 4 + 3] = 1;
                enabledPlanes++;
            }
        }

        constants.ClippingPlanesInfo.X = enabledPlanes;

        VeldridManager.GraphicsDevice.UpdateBuffer(_constantBuffer, 0, ref constants);
    }

    internal void UpdateMaterialTextures()
    {
        if (!_materialParamsDirty) return;
        if (_metalRenderer != null)
        {
            _metalRenderer.UpdateMaterialTextures();
        }
        else
        {
            var paramData = new Vector2[256];
            var colorData = new RgbaFloat[256];
            for (var i = 0; i < 256; i++)
            {
                var material = _editableDataset.Materials.FirstOrDefault(m => m.ID == i);
                if (material != null)
                {
                    // Get the opacity value from our dictionary, default to 1.0f if not set
                    var opacity = GetMaterialOpacity((byte)i);
                    paramData[i] = new Vector2(material.IsVisible ? 1.0f : 0.0f, opacity);
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
        }

        _materialParamsDirty = false;
    }

    private void ReuploadLabelData()
    {
        _labelTexture?.Dispose();
        _labelTexture =
            CreateDownsampledTexture3D(VeldridManager.Factory, _editableDataset.LabelData, VRAM_BUDGET_LABELS);

        if (_metalRenderer != null)
        {
            _metalRenderer.UpdateLabelTexture(_labelTexture);
        }
        else
        {
            _resourceSet?.Dispose();
            CreateStandardResourceSet(VeldridManager.Factory);
        }

        _labelsDirty = false;
    }

    private void Render()
    {
        if (_labelsDirty) ReuploadLabelData();

        if (_metalRenderer != null)
        {
            if (_materialParamsDirty) UpdateMaterialTextures();
            _metalRenderer.Render(_commandList, _framebuffer, _viewMatrix, _projMatrix, _cameraPosition,
                _planeVisualizationVertexBuffer, _planeVisualizationIndexBuffer);
        }
        else
        {
            // Standard Render Path
            _commandList.Begin();
            _commandList.SetFramebuffer(_framebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);

            UpdateConstantBuffer();
            if (_materialParamsDirty) UpdateMaterialTextures();

            _commandList.SetPipeline(_pipeline);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            _commandList.SetGraphicsResourceSet(0, _resourceSet);
            _commandList.DrawIndexed(36, 1, 0, 0, 0);

            if (ShowPlaneVisualizations) RenderPlaneVisualizations();

            if (AcousticIntegration.IsActiveFor(_editableDataset)) RenderTransducerMarkers();


            _commandList.End();
            VeldridManager.GraphicsDevice.SubmitCommands(_commandList);
            VeldridManager.GraphicsDevice.WaitForIdle();
        }
    }

    private void RenderTransducerMarkers()
    {
        // Use the plane visualization pipeline to draw simple cubes for TX/RX
        _commandList.SetPipeline(_planeVisualizationPipeline);
        _commandList.SetVertexBuffer(0, _vertexBuffer); // Use the cube vertex buffer
        _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16); // Use the cube index buffer

        var viewProj = _viewMatrix * _projMatrix;

        // Draw TX Marker
        var txTransform = Matrix4x4.CreateScale(0.02f) * Matrix4x4.CreateTranslation(AcousticIntegration.TxPosition);
        var txConstants = new PlaneVisualizationConstants
            { ViewProj = txTransform * viewProj, PlaneColor = new Vector4(0, 1, 1, 1) };
        VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref txConstants);
        _commandList.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
        _commandList.DrawIndexed(36, 1, 0, 0, 0); // 36 indices for a cube

        // Draw RX Marker
        var rxTransform = Matrix4x4.CreateScale(0.02f) * Matrix4x4.CreateTranslation(AcousticIntegration.RxPosition);
        var rxConstants = new PlaneVisualizationConstants
            { ViewProj = rxTransform * viewProj, PlaneColor = new Vector4(0, 1, 0, 1) };
        VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref rxConstants);
        _commandList.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
        _commandList.DrawIndexed(36, 1, 0, 0, 0);
    }

    private void RenderPlaneVisualizations()
    {
        _commandList.SetPipeline(_planeVisualizationPipeline);
        _commandList.SetVertexBuffer(0, _planeVisualizationVertexBuffer);
        _commandList.SetIndexBuffer(_planeVisualizationIndexBuffer, IndexFormat.UInt16);
        var viewProj = _viewMatrix * _projMatrix;
        if (CutXEnabled && ShowCutXPlaneVisual)
            RenderCuttingPlane(viewProj, Vector3.UnitX, CutXPosition, new Vector4(1, 0.2f, 0.2f, 0.3f));
        if (CutYEnabled && ShowCutYPlaneVisual)
            RenderCuttingPlane(viewProj, Vector3.UnitY, CutYPosition, new Vector4(0.2f, 1, 0.2f, 0.3f));
        if (CutZEnabled && ShowCutZPlaneVisual)
            RenderCuttingPlane(viewProj, Vector3.UnitZ, CutZPosition, new Vector4(0.2f, 0.2f, 1, 0.3f));
        foreach (var plane in ClippingPlanes.Where(p => p.Enabled && p.IsVisualizationVisible))
            RenderClippingPlane(viewProj, plane, new Vector4(1, 1, 0.2f, 0.3f));
    }

    private void RenderCuttingPlane(Matrix4x4 viewProj, Vector3 normal, float position, Vector4 color)
    {
        var transform = Matrix4x4.CreateScale(1.5f);
        if (normal == Vector3.UnitX)
        {
            transform *= Matrix4x4.CreateRotationY(MathF.PI / 2);
            transform *= Matrix4x4.CreateTranslation(position, 0.5f, 0.5f);
        }
        else if (normal == Vector3.UnitY)
        {
            transform *= Matrix4x4.CreateRotationX(-MathF.PI / 2);
            transform *= Matrix4x4.CreateTranslation(0.5f, position, 0.5f);
        }
        else if (normal == Vector3.UnitZ)
        {
            transform *= Matrix4x4.CreateTranslation(0.5f, 0.5f, position);
        }

        var constants = new PlaneVisualizationConstants { ViewProj = transform * viewProj, PlaneColor = color };
        VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
        _commandList.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
        _commandList.DrawIndexed(6, 1, 0, 0, 0);
    }

    private void RenderClippingPlane(Matrix4x4 viewProj, ClippingPlane plane, Vector4 color)
    {
        var forward = plane.Normal;
        var right = Vector3.Cross(Vector3.UnitY, forward);
        if (right.LengthSquared() < 0.001f) right = Vector3.Cross(Vector3.UnitX, forward);
        right = Vector3.Normalize(right);
        var up = Vector3.Cross(forward, right);
        var rotation = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z,
            0, 0, 0, 0, 1);
        var transform = Matrix4x4.CreateScale(1.5f) * rotation *
                        Matrix4x4.CreateTranslation(Vector3.One * 0.5f + plane.Normal * (plane.Distance - 0.5f));
        var constants = new PlaneVisualizationConstants { ViewProj = transform * viewProj, PlaneColor = color };
        VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
        _commandList.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
        _commandList.DrawIndexed(6, 1, 0, 0, 0);
    }

    public void DrawToolbarControls()
    {
        if (ImGui.Button("Reset Camera")) ResetCamera();
        ImGui.SameLine();

        var showPlanes = ShowPlaneVisualizations;
        if (ImGui.Checkbox("Show Planes", ref showPlanes)) ShowPlaneVisualizations = showPlanes;

        ImGui.SameLine();
        ImGui.Text($"Step: {StepSize:F1}");
        ImGui.SameLine();
        ImGui.Text($"Threshold: {MinThreshold:F2}-{MaxThreshold:F2}");
        if (_showPreview)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Preview Active");
        }
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        Render();
        var textureId = _renderTextureManager.GetImGuiTextureId();
        if (textureId != IntPtr.Zero)
        {
            var availableSize = ImGui.GetContentRegionAvail();

            // Store the cursor position before drawing the image
            var imagePos = ImGui.GetCursorScreenPos();

            // Draw the image
            ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));

            // Create an invisible button over the image to capture mouse input
            // This prevents the window from being dragged when interacting with the 3D view
            ImGui.SetCursorScreenPos(imagePos);
            ImGui.InvisibleButton("3DViewInteraction", availableSize);

            // Handle mouse input only when the invisible button is hovered
            if (ImGui.IsItemHovered()) HandleMouseInput(imagePos, availableSize);

            // Context menu on the invisible button
            if (ImGui.BeginPopupContextItem("3DViewerContextMenu"))
            {
                if (ImGui.MenuItem("Reset Camera")) ResetCamera();
                if (ImGui.MenuItem("Reset View")) ResetView();
                ImGui.Separator();
                if (ImGui.MenuItem("Toggle Plane Visualizations", null, ShowPlaneVisualizations))
                    ShowPlaneVisualizations = !ShowPlaneVisualizations;
                if (ImGui.MenuItem("Clear All Clipping Planes"))
                    ClippingPlanes.Clear();
                ImGui.Separator();
                if (ImGui.MenuItem("Save Screenshot..."))
                    _screenshotDialog.Open("screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                ImGui.EndPopup();
            }
        }

        if (_screenshotDialog.Submit())
            SaveScreenshot(_screenshotDialog.SelectedPath);
    }

    private void HandleMouseInput(Vector2 viewPos, Vector2 viewSize)
    {
        var io = ImGui.GetIO();

        // Handle Acoustic Transducer Placement (only on left click, not drag)
        if (AcousticIntegration.IsPlacingFor(_editableDataset) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var mousePos = io.MousePos - viewPos;
            if (Raycast(mousePos, viewSize, out var intersectionPoint))
                AcousticIntegration.UpdatePosition(intersectionPoint);
            return; // Prevent camera movement on the click that places the transducer
        }

        // Allow zooming even when placing transducers
        if (io.MouseWheel != 0)
            _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * 0.1f), 0.5f, 20.0f);

        // Handle camera rotation and panning
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right) ||
            ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            // Allow rotation with right-click even when placing transducers
            var allowRotation = ImGui.IsMouseDown(ImGuiMouseButton.Right) ||
                                (ImGui.IsMouseDown(ImGuiMouseButton.Left) &&
                                 !AcousticIntegration.IsPlacingFor(_editableDataset));
            var allowPanning = ImGui.IsMouseDown(ImGuiMouseButton.Middle);

            if (!_isDragging && !_isPanning)
            {
                if (allowRotation)
                {
                    _isDragging = true;
                    _lastMousePos = io.MousePos;
                }
                else if (allowPanning)
                {
                    _isPanning = true;
                    _lastMousePos = io.MousePos;
                }
            }

            if (_isDragging || _isPanning)
            {
                var delta = io.MousePos - _lastMousePos;

                if (_isDragging)
                {
                    _cameraYaw -= delta.X * 0.01f;
                    _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.01f, -MathF.PI / 2.01f, MathF.PI / 2.01f);
                }

                if (_isPanning)
                {
                    Matrix4x4.Invert(_viewMatrix, out var invView);
                    var right = Vector3.Normalize(new Vector3(invView.M11, invView.M12, invView.M13));
                    var up = Vector3.Normalize(new Vector3(invView.M21, invView.M22, invView.M23));
                    var panSpeed = _cameraDistance * 0.001f;
                    _cameraTarget -= right * delta.X * panSpeed;
                    _cameraTarget += up * delta.Y * panSpeed;
                }

                _lastMousePos = io.MousePos;
            }
        }
        else
        {
            _isDragging = false;
            _isPanning = false;
        }

        UpdateCameraMatrices();
    }

    private bool Raycast(Vector2 mousePos, Vector2 viewSize, out Vector3 intersection)
    {
        intersection = Vector3.Zero;

        // 1. Unproject mouse coordinates to a 3D ray
        var viewProj = _viewMatrix * _projMatrix;
        if (!Matrix4x4.Invert(viewProj, out var invViewProj)) return false;

        var ndc = new Vector2(
            mousePos.X / viewSize.X * 2.0f - 1.0f,
            1.0f - mousePos.Y / viewSize.Y * 2.0f
        );

        var nearPointH = Vector4.Transform(new Vector4(ndc.X, ndc.Y, 0.0f, 1.0f), invViewProj);
        var farPointH = Vector4.Transform(new Vector4(ndc.X, ndc.Y, 1.0f, 1.0f), invViewProj);

        if (nearPointH.W == 0 || farPointH.W == 0) return false;

        var rayOrigin = new Vector3(nearPointH.X, nearPointH.Y, nearPointH.Z) / nearPointH.W;
        var farPoint = new Vector3(farPointH.X, farPointH.Y, farPointH.Z) / farPointH.W;
        var rayDir = Vector3.Normalize(farPoint - rayOrigin);

        // 2. Intersect ray with the unit cube bounding box
        float tNear, tFar;
        var boxMin = Vector3.Zero;
        var boxMax = Vector3.One;

        var invRayDir = new Vector3(1.0f / rayDir.X, 1.0f / rayDir.Y, 1.0f / rayDir.Z);
        var t1 = (boxMin - rayOrigin) * invRayDir;
        var t2 = (boxMax - rayOrigin) * invRayDir;
        var tMin = Vector3.Min(t1, t2);
        var tMax = Vector3.Max(t1, t2);

        tNear = Math.Max(Math.Max(tMin.X, tMin.Y), tMin.Z);
        tFar = Math.Min(Math.Min(tMax.X, tMax.Y), tMax.Z);

        if (tFar < tNear || tFar < 0.0) return false;

        intersection = rayOrigin + rayDir * tNear;
        intersection = Vector3.Clamp(intersection, Vector3.Zero, Vector3.One); // Clamp to be safe
        return true;
    }

    #endregion

    #region Public Control Methods

    public void UpdateClippingPlaneNormal(ClippingPlane plane)
    {
        var rotX = Matrix4x4.CreateRotationX(plane.Rotation.X * MathF.PI / 180.0f);
        var rotY = Matrix4x4.CreateRotationY(plane.Rotation.Y * MathF.PI / 180.0f);
        var rotZ = Matrix4x4.CreateRotationZ(plane.Rotation.Z * MathF.PI / 180.0f);
        plane.Normal = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, rotZ * rotY * rotX));
    }

    public void MarkLabelsAsDirty()
    {
        _labelsDirty = true;
    }

    public bool GetMaterialVisibility(byte id)
    {
        return _editableDataset.Materials.FirstOrDefault(m => m.ID == id)?.IsVisible ?? false;
    }

    public float GetMaterialOpacity(byte id)
    {
        if (_materialOpacity.TryGetValue(id, out var opacity))
            return opacity;
        return 1.0f;
    }

    public void SetMaterialVisibility(byte id, bool visible)
    {
        var mat = _editableDataset.Materials.FirstOrDefault(m => m.ID == id);
        if (mat != null)
        {
            mat.IsVisible = visible;
            _materialParamsDirty = true;
        }
    }

    public void SetMaterialOpacity(byte id, float opacity)
    {
        _materialOpacity[id] = opacity;
        _materialParamsDirty = true;
    }

    public void SetAllMaterialsVisibility(bool visible)
    {
        foreach (var mat in _editableDataset.Materials.Where(m => m.ID != 0)) mat.IsVisible = visible;
        _materialParamsDirty = true;
    }

    public void ResetAllMaterialOpacities()
    {
        _materialOpacity.Clear();
        foreach (var mat in _editableDataset.Materials.Where(m => m.ID != 0)) _materialOpacity[mat.ID] = 1.0f;
        _materialParamsDirty = true;
    }

    public void SaveScreenshot(string filePath)
    {
        var device = VeldridManager.GraphicsDevice;
        var stagingDesc = new TextureDescription(_renderTexture.Width, _renderTexture.Height, 1, 1, 1,
            _renderTexture.Format, TextureUsage.Staging, TextureType.Texture2D);
        var stagingTexture = device.ResourceFactory.CreateTexture(stagingDesc);
        var cl = device.ResourceFactory.CreateCommandList();
        cl.Begin();
        cl.CopyTexture(_renderTexture, stagingTexture);
        cl.End();
        device.SubmitCommands(cl);
        device.WaitForIdle();
        var mapped = device.Map(stagingTexture, MapMode.Read);
        try
        {
            var imageData = new byte[stagingTexture.Width * stagingTexture.Height * 4];
            for (uint y = 0; y < stagingTexture.Height; y++)
            {
                var sourceRowPtr = IntPtr.Add(mapped.Data, (int)(y * mapped.RowPitch));
                Marshal.Copy(sourceRowPtr, imageData, (int)(y * stagingTexture.Width * 4),
                    (int)(stagingTexture.Width * 4));
            }

            using (var stream = File.Create(filePath))
            {
                new ImageWriter().WritePng(imageData, (int)stagingTexture.Width, (int)stagingTexture.Height,
                    ColorComponents.RedGreenBlueAlpha, stream);
            }

            Logger.Log($"[CtVolume3DViewer] Screenshot saved to {filePath}");
        }
        catch (Exception e)
        {
            Logger.LogError($"[CtVolume3DViewer] Failed to save screenshot: {e.Message}");
        }
        finally
        {
            device.Unmap(stagingTexture);
            stagingTexture.Dispose();
            cl.Dispose();
        }
    }

    public void ResetCamera()
    {
        _cameraTarget = new Vector3(0.5f);
        _cameraYaw = -MathF.PI / 4f;
        _cameraPitch = MathF.PI / 6f;
        _cameraDistance = 2.0f;
        UpdateCameraMatrices();
    }

    public void ResetView()
    {
        ResetCamera();
        CutXEnabled = CutYEnabled = CutZEnabled = false;
        CutXPosition = CutYPosition = CutZPosition = 0.5f;
        ClippingPlanes.Clear();
    }

    #endregion

    #region Helpers

    private Texture CreateDownsampledTexture3D(ResourceFactory factory, IVolumeData volume, long budget)
    {
        if (volume == null)
        {
            var dummy = factory.CreateTexture(TextureDescription.Texture3D(1, 1, 1, 1, PixelFormat.R8_UNorm,
                TextureUsage.Sampled));
            VeldridManager.GraphicsDevice.UpdateTexture(dummy, new byte[] { 0 }, 0, 0, 0, 1, 1, 1, 0, 0);
            return dummy;
        }

        var baseLod = _streamingDataset.BaseLod;
        var targetWidth = baseLod.Width;
        var targetHeight = baseLod.Height;
        var targetDepth = baseLod.Depth;
        Logger.Log(
            $"[CtVolume3DViewer] Creating label texture to match volume LOD: {targetWidth}x{targetHeight}x{targetDepth}");
        var factorX = Math.Max(1, volume.Width / targetWidth);
        var factorY = Math.Max(1, volume.Height / targetHeight);
        var factorZ = Math.Max(1, volume.Depth / targetDepth);
        var w = (uint)targetWidth;
        var h = (uint)targetHeight;
        var d = (uint)targetDepth;
        var desc = TextureDescription.Texture3D(w, h, d, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        var texture = factory.CreateTexture(desc);
        var downsampledData = new byte[w * h * d];
        Parallel.For(0, targetDepth, z =>
        {
            for (var y = 0; y < targetHeight; y++)
            for (var x = 0; x < targetWidth; x++)
            {
                var srcX = Math.Min(x * factorX, volume.Width - 1);
                var srcY = Math.Min(y * factorY, volume.Height - 1);
                var srcZ = Math.Min(z * factorZ, volume.Depth - 1);
                downsampledData[z * targetWidth * targetHeight + y * targetWidth + x] = volume[srcX, srcY, srcZ];
            }
        });
        VeldridManager.GraphicsDevice.UpdateTexture(texture, downsampledData, 0, 0, 0, w, h, d, 0, 0);
        return texture;
    }

    private static RgbaFloat HsvToRgb(float h, float s, float v)
    {
        float r, g, b;
        var i = (int)(h * 6);
        var f = h * 6 - i;
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);
        switch (i % 6)
        {
            case 0:
                r = v;
                g = t;
                b = p;
                break;
            case 1:
                r = q;
                g = v;
                b = p;
                break;
            case 2:
                r = p;
                g = v;
                b = t;
                break;
            case 3:
                r = p;
                g = q;
                b = v;
                break;
            case 4:
                r = t;
                g = p;
                b = v;
                break;
            default:
                r = v;
                g = p;
                b = q;
                break;
        }

        return new RgbaFloat(r, g, b, 1.0f);
    }

    #endregion
}