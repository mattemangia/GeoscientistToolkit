// GeoscientistToolkit/Data/CtImageStack/MetalVolumeRenderer.cs
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Util;
using Veldrid;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Metal-specific volume renderer with Metal Shading Language shaders and debug support.
    /// </summary>
    public class MetalVolumeRenderer : IDisposable
    {
        private readonly CtVolume3DViewer _viewer;
        
        // Veldrid resources
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private DeviceBuffer _constantBuffer;
        private DeviceBuffer _planeVisualizationConstantBuffer;
        private Pipeline _pipeline;
        private Pipeline _planeVisualizationPipeline;
        private ResourceLayout _resourceLayout;
        private ResourceLayout _planeVisualizationResourceLayout;
        private ResourceSet _resourceSet;
        private ResourceSet _planeVisualizationResourceSet;
        private Shader[] _shaders;
        private Shader[] _planeVisualizationShaders;
        
        // Textures (Metal uses 2D instead of 1D for compatibility)
        private Texture _volumeTexture;
        private Texture _labelTexture;
        private Texture _colorMapTexture;      // 2D texture (256x4) instead of 1D
        private Texture _materialParamsTexture; // 2D texture (256x1) instead of 1D
        private Texture _materialColorsTexture; // 2D texture (256x1) instead of 1D
        private Texture _previewTexture;
        private Sampler _volumeSampler;
        
        private Framebuffer _framebuffer;
        
        private const int COLOR_MAP_SIZE = 256;
        private const int NUM_COLOR_MAPS = 4;

        // Debug flag: when true, fragment shader outputs solid white (for pipeline debug)
        private bool _debugForceWhiteOutput = false;
        public bool DebugForceWhiteOutput 
        { 
            get => _debugForceWhiteOutput; 
            set => _debugForceWhiteOutput = value; 
        }

        public MetalVolumeRenderer(CtVolume3DViewer viewer)
        {
            _viewer = viewer;
        }

        public void InitializeResources(ResourceFactory factory, Framebuffer framebuffer,
                                        Texture volumeTexture, Texture labelTexture, Texture previewTexture, Sampler volumeSampler)
        {
            _framebuffer = framebuffer;
            _volumeTexture = volumeTexture;
            _labelTexture = labelTexture;
            _previewTexture = previewTexture;
            _volumeSampler = volumeSampler;
            
            CreateGeometry(factory);
            CreateMetalShaders(factory);
            CreateMetalTextures(factory);
            CreateMetalPipeline(factory);
            
            // Allocate constant buffers for volume and plane visualization
            _constantBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<CtVolume3DViewer.VolumeConstants>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _planeVisualizationConstantBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<CtVolume3DViewer.PlaneVisualizationConstants>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            
            CreateResourceSets(factory);
            Logger.Log("[MetalVolumeRenderer] Initialization complete");
        }

        private void CreateGeometry(ResourceFactory factory)
        {
            // Define cube vertices (0-1 range) and indices
            Vector3[] vertices = {
                new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
                new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)
            };
            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);
            
            ushort[] indices = {
                0, 1, 2, 0, 2, 3,   // front face
                4, 6, 5, 4, 7, 6,   // back face
                0, 4, 5, 0, 5, 1,   // bottom face
                3, 2, 6, 3, 6, 7,   // top face
                0, 7, 4, 0, 3, 7,   // left face
                1, 5, 6, 1, 6, 2    // right face
            };
            _indexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);
        }

        private void CreateMetalShaders(ResourceFactory factory)
{
    // Complete Metal Shading Language source for both volume and plane shaders.
    const string mslSource = @"
#include <metal_stdlib>
using namespace metal;

// --- CONSTANT BUFFER STRUCTS ---

struct Constants {
    float4x4 ViewProj;
    float4x4 InvView;
    float4   CameraPosition;
    float4   VolumeSize;
    float4   ThresholdParams;  // x=minThresh, y=maxThresh, z=stepSize, w=showGrayscale
    float4   SliceParams;      // xyz=slicePos, w=showSlices
    float4   RenderParams;     // x=colormapIndex, w=debugWhiteOverride
    float4   CutPlaneX;        // x=enabled, y=dir, z=position
    float4   CutPlaneY;
    float4   CutPlaneZ;
    float4   ClippingPlanesData[8]; // xyz=normal*dist (+mirror offset), w=enabled
    float4   ClippingPlanesInfo;    // x=numPlanes
    float4   PreviewParams;    // x=showPreview, yzw=color
    float4   PreviewAlpha;     // x=alpha
};

struct PlaneConstants {
    float4x4 ViewProj;
    float4   PlaneColor;
};

// --- INPUT/OUTPUT STRUCTS ---

struct VertexIn {
    float3 Position [[attribute(0)]];
};

struct VSOutput {
    float4 position [[position]];
    float3 modelPos;
};

// --- HELPERS ---

// Ray‐box intersection
bool IntersectBox(float3 rayOrigin, float3 rayDir,
                  float3 boxMin, float3 boxMax,
                  thread float& tNear, thread float& tFar)
{
    float3 invDir = 1.0f / (rayDir + 1e-8f);
    float3 t1 = (boxMin - rayOrigin) * invDir;
    float3 t2 = (boxMax - rayOrigin) * invDir;
    float3 tMin = min(t1, t2);
    float3 tMax = max(t1, t2);
    tNear = max(max(tMin.x, tMin.y), tMin.z);
    tFar  = min(min(tMax.x, tMax.y), tMax.z);
    return (tFar >= tNear) && (tFar > 0.0f);
}

// Axis‐aligned + arbitrary clipping
bool IsCutByPlanes(float3 pos, constant Constants& c)
{
    if (c.CutPlaneX.x > 0.5f && (pos.x - c.CutPlaneX.z) * c.CutPlaneX.y > 0.0f) return true;
    if (c.CutPlaneY.x > 0.5f && (pos.y - c.CutPlaneY.z) * c.CutPlaneY.y > 0.0f) return true;
    if (c.CutPlaneZ.x > 0.5f && (pos.z - c.CutPlaneZ.z) * c.CutPlaneZ.y > 0.0f) return true;
    int np = (int)c.ClippingPlanesInfo.x;
    for (int i = 0; i < np; ++i)
    {
        float4 pd = c.ClippingPlanesData[i];
        if (pd.w > 0.5f)
        {
            float3 n = pd.xyz;
            float dist = length(n);
            if (dist > 0.001f)
            {
                n /= dist;
                float mirror = (dist >= 1.5f) ? 1.0f : 0.0f;
                float planeDist = dot(pos - float3(0.5f), n) - (dist - 0.5f - mirror);
                if (mirror > 0.5f ? (planeDist < 0.0f) : (planeDist > 0.0f))
                    return true;
            }
        }
    }
    return false;
}

// Color map lookup (2D texture: 256×4)
float4 ApplyColorMap(float normI, constant Constants& c,
                     texture2d<float> cmap, sampler samp)
{
    float u = normI;
    float v = (c.RenderParams.x + 0.5f) / 4.0f;
    return cmap.sample(samp, float2(u, v));
}

// Convert HSV→RGB for rainbow colormap if needed
float4 HsvToRgb(float h, float s, float v)
{
    float r, g, b;
    int i = int(h * 6.0);
    float f = h * 6.0 - float(i);
    float p = v * (1.0 - s);
    float q = v * (1.0 - f * s);
    float t = v * (1.0 - (1.0 - f) * s);
    switch (i % 6)
    {
        case 0: r=v; g=t; b=p; break;
        case 1: r=q; g=v; b=p; break;
        case 2: r=p; g=v; b=t; break;
        case 3: r=p; g=q; b=v; break;
        case 4: r=t; g=p; b=v; break;
        default: r=v; g=p; b=q; break;
    }
    return float4(r,g,b,1.0);
}

// --- VOLUME RENDERING SHADERS ---

// Vertex shader
vertex VSOutput vertex_main(VertexIn vin         [[stage_in]],
                            constant Constants& c [[buffer(0)]])
{
    VSOutput o;
    o.modelPos = vin.Position;
    o.position = c.ViewProj * float4(vin.Position, 1.0f);
    return o;
}

// Fragment shader (ray‐marching)
fragment float4 fragment_main(VSOutput in              [[stage_in]],
                              constant Constants& c    [[buffer(0)]],
                              texture3d<float> vol     [[texture(2)]],
                              texture3d<float> lab     [[texture(3)]],
                              texture2d<float> cmap    [[texture(4)]],
                              texture2d<float> mparams [[texture(5)]],
                              texture2d<float> mcolors [[texture(6)]],
                              texture3d<float> prev    [[texture(7)]],
                              sampler samp             [[sampler(1)]])
{
    // Debug override: white cube if flag set
    if (c.RenderParams.w > 0.5f)
        return float4(1.0);
    
    float3 ro = c.CameraPosition.xyz;
    float3 rd = normalize(in.modelPos - ro);
    float tNear, tFar;
    if (!IntersectBox(ro, rd, float3(0), float3(1), tNear, tFar))
        discard_fragment();
    tNear = max(tNear, 0.0f);
    float4 accum = float4(0.0);
    
    float maxD = max(c.VolumeSize.x, max(c.VolumeSize.y, c.VolumeSize.z));
    float baseStep = 1.0f / maxD;
    float stepSz = baseStep * c.ThresholdParams.z;
    int maxSteps = int((tFar - tNear) / stepSz);
    float opacityScalar = 40.0f;
    float t = tNear;
    
    for (int i = 0; i < 768; ++i)
    {
        if (i >= maxSteps || t > tFar || accum.a > 0.95f) break;
        float3 p = ro + t * rd;
        if (any(p < float3(0)) || any(p > float3(1)) || IsCutByPlanes(p, c))
        {
            t += stepSz;
            continue;
        }
        float4 sampleC = float4(0.0);
        // Preview mask overlay
        if (c.PreviewParams.x > 0.5f &&
            prev.sample(samp, p).r > 0.5f)
        {
            sampleC = float4(c.PreviewParams.yzw, c.PreviewAlpha.x * 5.0f);
        }
        else
        {
            // Label/material lookup
            float lbl = lab.sample(samp, p).r;
            int mid = int(lbl * 255.0f + 0.5f);
            float2 mp = mparams.read(uint2(mid,0), 0).xy;
            if (mid > 0 && mp.x > 0.5f)
            {
                float4 mc = mcolors.read(uint2(mid,0), 0);
                sampleC = float4(mc.rgb, mp.y * 5.0f);
            }
            else if (c.ThresholdParams.w > 0.5f)
            {
                float inten = vol.sample(samp, p).r;
                if (inten >= c.ThresholdParams.x && inten <= c.ThresholdParams.y)
                {
                    float normI = (inten - c.ThresholdParams.x)
                                / (c.ThresholdParams.y - c.ThresholdParams.x + 0.001f);
                    if (c.RenderParams.x >= 0.0f)
                        sampleC = ApplyColorMap(normI, c, cmap, samp);
                    else
                        sampleC = float4(normI, normI, normI, normI);
                    sampleC.a = sampleC.a * sampleC.a;
                }
            }
        }
        if (sampleC.a > 0.0f)
        {
            float a = clamp(sampleC.a * stepSz * opacityScalar, 0.0f, 1.0f);
            accum += (1.0f - accum.a) * float4(sampleC.rgb * a, a);
        }
        t += stepSz;
    }
    return accum;
}

// --- PLANE VISUALIZATION SHADERS ---

vertex float4 plane_vertex_main(VertexIn vin             [[stage_in]],
                                constant PlaneConstants& pc [[buffer(0)]])
{
    return pc.ViewProj * float4(vin.Position, 1.0f);
}

fragment float4 plane_fragment_main()
{
    return float4(1.0); // uses PlaneColor from constant buffer
}
";

    // Create the Shader objects directly from this MSL source
    _shaders = new Shader[]
    {
        factory.CreateShader(new ShaderDescription(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(mslSource),
            "vertex_main")),
        factory.CreateShader(new ShaderDescription(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(mslSource),
            "fragment_main"))
    };

    _planeVisualizationShaders = new Shader[]
    {
        factory.CreateShader(new ShaderDescription(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(mslSource),
            "plane_vertex_main")),
        factory.CreateShader(new ShaderDescription(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(mslSource),
            "plane_fragment_main"))
    };

    Logger.Log("[MetalVolumeRenderer] MSL shaders compiled successfully.");
}


        private void CreateMetalTextures(ResourceFactory factory)
        {
            // Create 2D color map texture (256x4) instead of 1D (Metal doesn't support 1D textures)
            var colorMapData = new RgbaFloat[COLOR_MAP_SIZE * NUM_COLOR_MAPS];
            // Grayscale colormap (row 0)
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float v = i / (float)(COLOR_MAP_SIZE - 1);
                colorMapData[i] = new RgbaFloat(v, v, v, 1);
            }
            // Hot colormap (row 1)
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float t = i / (float)(COLOR_MAP_SIZE - 1);
                float r = Math.Min(1.0f, 3.0f * t);
                float g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f);
                float b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f);
                colorMapData[COLOR_MAP_SIZE + i] = new RgbaFloat(r, g, b, 1);
            }
            // Cool colormap (row 2)
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float t = i / (float)(COLOR_MAP_SIZE - 1);
                colorMapData[COLOR_MAP_SIZE * 2 + i] = new RgbaFloat(t, 1 - t, 1, 1);
            }
            // Rainbow colormap (row 3)
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float h = (i / (float)(COLOR_MAP_SIZE - 1)) * 0.7f;
                colorMapData[COLOR_MAP_SIZE * 3 + i] = HsvToRgb(h, 1.0f, 1.0f);
            }
            // Create 2D texture for color maps
            _colorMapTexture = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)COLOR_MAP_SIZE, (uint)NUM_COLOR_MAPS, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            VeldridManager.GraphicsDevice.UpdateTexture(_colorMapTexture, colorMapData,
                0, 0, 0, (uint)COLOR_MAP_SIZE, (uint)NUM_COLOR_MAPS, 1, 0, 0);
            
            // Create material parameter and color lookup textures as 2D (256x1 each)
            _materialParamsTexture = factory.CreateTexture(TextureDescription.Texture2D(
                256, 1, 1, 1, PixelFormat.R32_G32_Float, TextureUsage.Sampled));
            _materialColorsTexture = factory.CreateTexture(TextureDescription.Texture2D(
                256, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            UpdateMaterialTextures();
        }

        private void CreateMetalPipeline(ResourceFactory factory)
        {
            // Define resource layouts (binding order must match shader parameters)
            _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("LabelTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorMapTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialParamsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialColorsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("PreviewTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
            ));
            
            // Main volume rendering pipeline (enable depth test, alpha blending, back-face culling)
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
                // Use clockwise front face for Metal (left-handed NDC) to ensure correct culling
                new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    vertexLayouts: new[] {
                        new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                    },
                    shaders: _shaders),
                resourceLayouts: new[] { _resourceLayout },
                outputs: _framebuffer.OutputDescription
            ));
            
            // Plane visualization pipeline (no culling, always render plane overlay with transparency)
            _planeVisualizationResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)
            ));
            _planeVisualizationPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                new BlendStateDescription(RgbaFloat.Black, BlendAttachmentDescription.AlphaBlend),
                new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: false, comparisonKind: ComparisonKind.Less),
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    vertexLayouts: new[] {
                        new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                    },
                    shaders: _planeVisualizationShaders),
                resourceLayouts: new[] { _planeVisualizationResourceLayout },
                outputs: _framebuffer.OutputDescription
            ));
        }

        private void CreateResourceSets(ResourceFactory factory)
        {
            // Create resource set for main volume shader (bind constant buffer, sampler, and textures)
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _constantBuffer,
                _volumeSampler,
                _volumeTexture,
                _labelTexture,
                _colorMapTexture,
                _materialParamsTexture,
                _materialColorsTexture,
                _previewTexture
            ));
            // Resource set for plane visualization (only constant buffer)
            _planeVisualizationResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _planeVisualizationResourceLayout,
                _planeVisualizationConstantBuffer
            ));
        }

        public void UpdateMaterialTextures()
        {
            // Populate material parameter (visibility, alpha factor) and color arrays
            var paramData = new Vector2[256];
            var colorData = new RgbaFloat[256];
            for (int i = 0; i < 256; i++)
            {
                var material = _viewer._editableDataset.Materials.FirstOrDefault(m => m.ID == i);
                if (material != null)
                {
                    paramData[i] = new Vector2(material.IsVisible ? 1.0f : 0.0f, 1.0f);

                    colorData[i] = new RgbaFloat(material.Color);
                }
                else
                {
                    paramData[i] = new Vector2(0, 1);         // default: not visible, alpha factor 1
                    colorData[i] = RgbaFloat.Black;
                }
            }
            // Update 2D textures with new material data
            VeldridManager.GraphicsDevice.UpdateTexture(_materialParamsTexture, paramData, 0, 0, 0, 256, 1, 1, 0, 0);
            VeldridManager.GraphicsDevice.UpdateTexture(_materialColorsTexture, colorData, 0, 0, 0, 256, 1, 1, 0, 0);
        }

        public void UpdateLabelTexture(Texture newLabelTexture)
        {
            _labelTexture = newLabelTexture;
            // Recreate resource set with the updated label texture
            _resourceSet?.Dispose();
            _resourceSet = VeldridManager.Factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _constantBuffer,
                _volumeSampler,
                _volumeTexture,
                _labelTexture,
                _colorMapTexture,
                _materialParamsTexture,
                _materialColorsTexture,
                _previewTexture
            ));
        }

        public void Render(CommandList cl, Framebuffer framebuffer,
                           Matrix4x4 viewMatrix, Matrix4x4 projMatrix, Vector3 cameraPosition,
                           DeviceBuffer planeVertexBuffer, DeviceBuffer planeIndexBuffer)
        {
            cl.Begin();
            cl.SetFramebuffer(framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Black);
            cl.ClearDepthStencil(1f);
            
            // Update constant buffer with current view parameters and viewer settings
            UpdateConstantBuffer(viewMatrix, projMatrix, cameraPosition);
            
            // Render volume (raymarching in fragment shader)
            cl.SetPipeline(_pipeline);
            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.DrawIndexed(indexCount: 36, instanceCount: 1, indexStart: 0, vertexOffset: 0, instanceStart: 0);
            
            // Render cutting plane visualizations if enabled
            if (_viewer.ShowPlaneVisualizations)
            {
                RenderPlaneVisualizations(cl, viewMatrix, projMatrix, planeVertexBuffer, planeIndexBuffer);
            }
            
            cl.End();
            VeldridManager.GraphicsDevice.SubmitCommands(cl);
            VeldridManager.GraphicsDevice.WaitForIdle();
        }

        private unsafe void UpdateConstantBuffer(Matrix4x4 viewMatrix, Matrix4x4 projMatrix, Vector3 cameraPosition)
        {
            Matrix4x4.Invert(viewMatrix, out var invView);
            var constants = new CtVolume3DViewer.VolumeConstants
            {
                ViewProj        = viewMatrix * projMatrix,
                InvView         = invView,
                CameraPosition  = new Vector4(cameraPosition, 1.0f),
                VolumeSize      = new Vector4(_viewer._editableDataset.Width, _viewer._editableDataset.Height, _viewer._editableDataset.Depth, 0.0f),
                ThresholdParams = new Vector4(_viewer.MinThreshold, _viewer.MaxThreshold, _viewer.StepSize, _viewer.ShowGrayscale ? 1 : 0),
                SliceParams     = new Vector4(_viewer.SlicePositions, _viewer.ShowSlices ? 1 : 0),
                // RenderParams: x = colorMapIndex (negative for grayscale), w = debug flag
                RenderParams    = new Vector4(_viewer.ColorMapIndex, 0, 0, _debugForceWhiteOutput ? 1 : 0),
                CutPlaneX       = new Vector4(_viewer.CutXEnabled ? 1 : 0, _viewer.CutXForward ? 1 : -1, _viewer.CutXPosition, 0),
                CutPlaneY       = new Vector4(_viewer.CutYEnabled ? 1 : 0, _viewer.CutYForward ? 1 : -1, _viewer.CutYPosition, 0),
                CutPlaneZ       = new Vector4(_viewer.CutZEnabled ? 1 : 0, _viewer.CutZForward ? 1 : -1, _viewer.CutZPosition, 0),
                ClippingPlanesInfo = new Vector4(0, 0, 0, 0), // will be updated below
                PreviewParams   = new Vector4(_viewer._showPreview ? 1 : 0, _viewer._previewColor.X, _viewer._previewColor.Y, _viewer._previewColor.Z),
                PreviewAlpha    = new Vector4(_viewer._previewColor.W, 0, 0, 0)
            };
            // Populate ClippingPlanesData array
            int enabledPlanes = 0;
            foreach (var plane in _viewer.ClippingPlanes)
            {
                if (plane.Enabled)
                {
                    // Pack plane normal and distance (mirror if needed) into 4 floats
                    var normal = plane.Normal;
                    constants.ClippingPlanesData[enabledPlanes * 4 + 0] = normal.X;
                    constants.ClippingPlanesData[enabledPlanes * 4 + 1] = normal.Y;
                    constants.ClippingPlanesData[enabledPlanes * 4 + 2] = normal.Z;
                    constants.ClippingPlanesData[enabledPlanes * 4 + 3] = 1; // enabled flag
                    enabledPlanes++;
                }
            }
            constants.ClippingPlanesInfo.X = enabledPlanes;
            // Update the GPU constant buffer
            VeldridManager.GraphicsDevice.UpdateBuffer(_constantBuffer, 0, ref constants);
        }

        private void RenderPlaneVisualizations(CommandList cl, Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
                                               DeviceBuffer planeVertexBuffer, DeviceBuffer planeIndexBuffer)
        {
            cl.SetPipeline(_planeVisualizationPipeline);
            cl.SetVertexBuffer(0, planeVertexBuffer);
            cl.SetIndexBuffer(planeIndexBuffer, IndexFormat.UInt16);
            
            Matrix4x4 viewProj = viewMatrix * projMatrix;
            // Render axis-aligned cutting planes (if enabled and visualizations toggled on)
            if (_viewer.CutXEnabled && _viewer.ShowCutXPlaneVisual)
                RenderCuttingPlane(cl, viewProj, Vector3.UnitX, _viewer.CutXPosition, new Vector4(1, 0.2f, 0.2f, 0.3f));
            if (_viewer.CutYEnabled && _viewer.ShowCutYPlaneVisual)
                RenderCuttingPlane(cl, viewProj, Vector3.UnitY, _viewer.CutYPosition, new Vector4(0.2f, 1, 0.2f, 0.3f));
            if (_viewer.CutZEnabled && _viewer.ShowCutZPlaneVisual)
                RenderCuttingPlane(cl, viewProj, Vector3.UnitZ, _viewer.CutZPosition, new Vector4(0.2f, 0.2f, 1, 0.3f));
            // Render any arbitrary clipping plane visualizations
            foreach (var plane in _viewer.ClippingPlanes)
            {
                if (plane.Enabled && plane.IsVisualizationVisible)
                {
                    RenderClippingPlane(cl, viewProj, plane, new Vector4(1, 1, 0.2f, 0.3f));
                }
            }
        }

        private void RenderCuttingPlane(CommandList cl, Matrix4x4 viewProj, Vector3 normal, float position, Vector4 color)
        {
            // Compute transform for an axis-aligned cutting plane (scale slightly larger than volume, oriented and translated)
            Matrix4x4 transform = Matrix4x4.CreateScale(1.5f);
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
            // Set plane constants (MVP matrix and plane color)
            var planeConstants = new CtVolume3DViewer.PlaneVisualizationConstants
            {
                ViewProj  = transform * viewProj,
                PlaneColor = color
            };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref planeConstants);
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
            cl.DrawIndexed(indexCount: 6, instanceCount: 1, indexStart: 0, vertexOffset: 0, instanceStart: 0);
        }

        private void RenderClippingPlane(CommandList cl, Matrix4x4 viewProj, ClippingPlane plane, Vector4 color)
        {
            // Compute orientation basis for arbitrary clipping plane quad
            Vector3 forward = plane.Normal;
            Vector3 right = Vector3.Cross(Vector3.UnitY, forward);
            if (right.LengthSquared() < 0.001f)
                right = Vector3.Cross(Vector3.UnitX, forward);
            right = Vector3.Normalize(right);
            Vector3 up = Vector3.Cross(forward, right);
            // Build rotation matrix from basis vectors and translate plane to correct distance
            var rotation = new Matrix4x4(
                right.X,   right.Y,   right.Z,   0,
                up.X,      up.Y,      up.Z,      0,
                forward.X, forward.Y, forward.Z, 0,
                0,         0,         0,         1
            );
            Matrix4x4 transform = Matrix4x4.CreateScale(1.5f) * rotation *
                                   Matrix4x4.CreateTranslation(Vector3.One * 0.5f + plane.Normal * (plane.Distance - 0.5f));
            // Update and bind plane constants for this clipping plane
            var planeConstants = new CtVolume3DViewer.PlaneVisualizationConstants
            {
                ViewProj = transform * viewProj,
                PlaneColor = color
            };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref planeConstants);
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
            cl.DrawIndexed(indexCount: 6, instanceCount: 1, indexStart: 0, vertexOffset: 0, instanceStart: 0);
        }

        private static RgbaFloat HsvToRgb(float h, float s, float v)
        {
            // Utility: convert HSV to RGBA (with alpha=1)
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

        public void Dispose()
        {
            _planeVisualizationResourceSet?.Dispose();
            _planeVisualizationResourceLayout?.Dispose();
            _planeVisualizationPipeline?.Dispose();
            if (_planeVisualizationShaders != null)
            {
                foreach (var shader in _planeVisualizationShaders)
                    shader?.Dispose();
            }
            _planeVisualizationConstantBuffer?.Dispose();
            
            _resourceSet?.Dispose();
            _resourceLayout?.Dispose();
            _volumeSampler?.Dispose();
            _materialColorsTexture?.Dispose();
            _materialParamsTexture?.Dispose();
            _colorMapTexture?.Dispose();
            
            _pipeline?.Dispose();
            if (_shaders != null)
            {
                foreach (var shader in _shaders)
                    shader?.Dispose();
            }
            _constantBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _vertexBuffer?.Dispose();
        }
    }
}
