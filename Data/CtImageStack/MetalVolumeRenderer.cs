// GeoscientistToolkit/Data/CtImageStack/MetalVolumeRenderer.cs
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Util;
using Veldrid;
using Veldrid.SPIRV;
using System.Linq;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Metal-specific volume renderer that handles Metal's unique requirements
    /// including lack of 1D texture support and different resource binding
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

        // Textures - using 2D textures instead of 1D for Metal compatibility
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

            // Create geometry buffers (shared with standard renderer)
            CreateGeometry(factory);

            // Create Metal-specific shaders
            CreateMetalShaders(factory);

            // Create textures (2D instead of 1D for Metal)
            CreateMetalTextures(factory);

            // Create pipeline
            CreateMetalPipeline(factory);

            // Create constant buffers
            _constantBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<CtVolume3DViewer.VolumeConstants>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _planeVisualizationConstantBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)Marshal.SizeOf<CtVolume3DViewer.PlaneVisualizationConstants>(),
                BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            // Create resource sets
            CreateResourceSets(factory);

            Logger.Log("[MetalVolumeRenderer] Initialization complete");
        }

        private void CreateGeometry(ResourceFactory factory)
        {
            // Cube vertices
            Vector3[] vertices = {
                new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
                new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)
            };
            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);

            // Cube indices
            ushort[] indices = {
                0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1, 3, 2, 6, 3, 6, 7,
                0, 7, 4, 0, 3, 7, 1, 5, 6, 1, 6, 2
            };
            _indexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);
        }

        private void CreateMetalShaders(ResourceFactory factory)
        {
            // Metal vertex shader
            string metalVertexShader = @"
#include <metal_stdlib>
using namespace metal;

struct VolumeConstants {
    float4x4 ViewProj;
    float4x4 InvView;
    float4 CameraPosition;
    float4 VolumeSize;
    float4 ThresholdParams;
    float4 SliceParams;
    float4 RenderParams;
    float4 CutPlaneX;
    float4 CutPlaneY;
    float4 CutPlaneZ;
    float4 ClippingPlanesData[8];
    float4 ClippingPlanesInfo;
    float4 PreviewParams;
    float4 PreviewAlpha;
};

struct VertexIn {
    float3 position [[attribute(0)]];
};

struct VertexOut {
    float4 position [[position]];
    float3 modelPos;
};

vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                            constant VolumeConstants& constants [[buffer(0)]]) {
    VertexOut out;
    out.modelPos = in.position;
    out.position = constants.ViewProj * float4(in.position, 1.0);
    return out;
}";

            // Metal fragment shader - using 2D textures instead of 1D
            string metalFragmentShader = @"
#include <metal_stdlib>
using namespace metal;

struct VolumeConstants {
    float4x4 ViewProj;
    float4x4 InvView;
    float4 CameraPosition;
    float4 VolumeSize;
    float4 ThresholdParams;
    float4 SliceParams;
    float4 RenderParams;
    float4 CutPlaneX;
    float4 CutPlaneY;
    float4 CutPlaneZ;
    float4 ClippingPlanesData[8];
    float4 ClippingPlanesInfo;
    float4 PreviewParams;
    float4 PreviewAlpha;
};

struct VertexOut {
    float4 position [[position]];
    float3 modelPos;
};

bool IntersectBox(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax, thread float& tNear, thread float& tFar) {
    float3 invRayDir = 1.0 / (rayDir + 1e-8);
    float3 t1 = (boxMin - rayOrigin) * invRayDir;
    float3 t2 = (boxMax - rayOrigin) * invRayDir;
    float3 tMin = min(t1, t2);
    float3 tMax = max(t1, t2);
    tNear = max(max(tMin.x, tMin.y), tMin.z);
    tFar = min(min(tMax.x, tMax.y), tMax.z);
    return tFar >= tNear && tFar > 0.0;
}

bool IsCutByPlanes(float3 pos, constant VolumeConstants& constants) {
    if (constants.CutPlaneX.x > 0.5 && (pos.x - constants.CutPlaneX.z) * constants.CutPlaneX.y > 0.0) return true;
    if (constants.CutPlaneY.x > 0.5 && (pos.y - constants.CutPlaneY.z) * constants.CutPlaneY.y > 0.0) return true;
    if (constants.CutPlaneZ.x > 0.5 && (pos.z - constants.CutPlaneZ.z) * constants.CutPlaneZ.y > 0.0) return true;
    
    int numPlanes = int(constants.ClippingPlanesInfo.x);
    for (int i = 0; i < numPlanes; i++) {
        float4 planeData = constants.ClippingPlanesData[i];
        if (planeData.w > 0.5) {
            float3 normal = planeData.xyz;
            float dist = length(normal);
            if (dist > 0.001) {
                normal /= dist;
                float mirror = step(1.5, dist);
                float planeDist = dot(pos - float3(0.5), normal) - (dist - 0.5 - mirror);
                if (mirror > 0.5 ? planeDist < 0.0 : planeDist > 0.0) return true;
            }
        }
    }
    return false;
}

float4 ApplyColorMap(float intensity, texture2d<float> colorMapTexture, sampler volumeSampler, float mapIndex) {
    // Sample from 2D texture (256x4) instead of 1D
    float2 uv = float2(intensity, (mapIndex + 0.5) / 4.0);
    return colorMapTexture.sample(volumeSampler, uv);
}

fragment float4 fragment_main(VertexOut in [[stage_in]],
                            constant VolumeConstants& constants [[buffer(0)]],
                            texture3d<float> volumeTexture [[texture(0)]],
                            texture3d<float> labelTexture [[texture(1)]],
                            texture2d<float> colorMapTexture [[texture(2)]],
                            texture2d<float> materialParamsTexture [[texture(3)]],
                            texture2d<float> materialColorsTexture [[texture(4)]],
                            texture3d<float> previewTexture [[texture(5)]],
                            sampler volumeSampler [[sampler(0)]]) {
    
    float3 rayOrigin = constants.CameraPosition.xyz;
    float3 rayDir = normalize(in.modelPos - rayOrigin);
    
    float tNear, tFar;
    if (!IntersectBox(rayOrigin, rayDir, float3(0.0), float3(1.0), tNear, tFar)) {
        discard_fragment();
    }
    
    tNear = max(tNear, 0.0);
    float4 accumulatedColor = float4(0.0);
    
    float maxDim = max(constants.VolumeSize.x, max(constants.VolumeSize.y, constants.VolumeSize.z));
    float baseStepSize = 1.0 / maxDim;
    float step = baseStepSize * constants.ThresholdParams.z;
    
    int maxSteps = int((tFar - tNear) / step);
    float opacityScalar = 40.0;
    float t = tNear;
    
    for (int i = 0; i < 768; i++) {
        if (i >= maxSteps || t > tFar || accumulatedColor.a > 0.95) break;
        
        float3 currentPos = rayOrigin + t * rayDir;
        if (any(currentPos < 0.0) || any(currentPos > 1.0) || IsCutByPlanes(currentPos, constants)) {
            t += step;
            continue;
        }
        
        float4 sampledColor = float4(0.0);
        
        if (constants.PreviewParams.x > 0.5 && previewTexture.sample(volumeSampler, currentPos).r > 0.5) {
            sampledColor = float4(constants.PreviewParams.yzw, constants.PreviewAlpha.x * 5.0);
        } else {
            int materialId = int(labelTexture.sample(volumeSampler, currentPos).r * 255.0 + 0.5);
            
            // Sample from 2D textures using material ID
            float2 matParamUV = float2((float(materialId) + 0.5) / 256.0, 0.5);
            float2 materialParams = materialParamsTexture.sample(volumeSampler, matParamUV).xy;
            
            if (materialId > 0 && materialParams.x > 0.5) {
                float2 matColorUV = float2((float(materialId) + 0.5) / 256.0, 0.5);
                sampledColor = materialColorsTexture.sample(volumeSampler, matColorUV);
                sampledColor.a = materialParams.y * 5.0;
            } else if (constants.ThresholdParams.w > 0.5) {
                float intensity = volumeTexture.sample(volumeSampler, currentPos).r;
                if (intensity >= constants.ThresholdParams.x && intensity <= constants.ThresholdParams.y) {
                    float normIntensity = (intensity - constants.ThresholdParams.x) / 
                                        (constants.ThresholdParams.y - constants.ThresholdParams.x + 0.001);
                    
                    if (constants.RenderParams.x > 0.5) {
                        sampledColor = ApplyColorMap(normIntensity, colorMapTexture, volumeSampler, constants.RenderParams.x);
                    } else {
                        sampledColor = float4(float3(normIntensity), normIntensity);
                    }
                    sampledColor.a = pow(sampledColor.a, 2.0);
                }
            }
        }
        
        if (sampledColor.a > 0.0) {
            float correctedAlpha = clamp(sampledColor.a * step * opacityScalar, 0.0, 1.0);
            accumulatedColor += (1.0 - accumulatedColor.a) * float4(sampledColor.rgb * correctedAlpha, correctedAlpha);
        }
        t += step;
    }
    
    return accumulatedColor;
}";

            // Plane visualization shaders for Metal
            string planeVertexShader = @"
#include <metal_stdlib>
using namespace metal;

struct PlaneConstants {
    float4x4 ViewProj;
    float4 PlaneColor;
};

struct VertexIn {
    float3 position [[attribute(0)]];
};

struct VertexOut {
    float4 position [[position]];
};

vertex VertexOut plane_vertex_main(VertexIn in [[stage_in]],
                                  constant PlaneConstants& constants [[buffer(0)]]) {
    VertexOut out;
    out.position = constants.ViewProj * float4(in.position, 1.0);
    return out;
}";

            string planeFragmentShader = @"
#include <metal_stdlib>
using namespace metal;

struct PlaneConstants {
    float4x4 ViewProj;
    float4 PlaneColor;
};

fragment float4 plane_fragment_main(constant PlaneConstants& constants [[buffer(0)]]) {
    return constants.PlaneColor;
}";

            try
            {
                // Create main shaders
                var mainVertexDesc = new ShaderDescription(ShaderStages.Vertex,
                    System.Text.Encoding.UTF8.GetBytes(metalVertexShader), "vertex_main");
                var mainFragmentDesc = new ShaderDescription(ShaderStages.Fragment,
                    System.Text.Encoding.UTF8.GetBytes(metalFragmentShader), "fragment_main");

                var options = new CrossCompileOptions(fixClipSpaceZ: true, invertVertexOutputY: false);
                _shaders = factory.CreateFromSpirv(mainVertexDesc, mainFragmentDesc, options);

                // Create plane visualization shaders
                var planeVertexDesc = new ShaderDescription(ShaderStages.Vertex,
                    System.Text.Encoding.UTF8.GetBytes(planeVertexShader), "plane_vertex_main");
                var planeFragmentDesc = new ShaderDescription(ShaderStages.Fragment,
                    System.Text.Encoding.UTF8.GetBytes(planeFragmentShader), "plane_fragment_main");

                _planeVisualizationShaders = factory.CreateFromSpirv(planeVertexDesc, planeFragmentDesc, options);

                Logger.Log("[MetalVolumeRenderer] Shaders compiled successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[MetalVolumeRenderer] Failed to create shaders: {ex.Message}");
                throw;
            }
        }

        private void CreateMetalTextures(ResourceFactory factory)
        {
            // Create 2D color map texture (256x4) instead of 1D
            var colorMapData = new RgbaFloat[COLOR_MAP_SIZE * NUM_COLOR_MAPS];

            // Grayscale
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float v = i / (float)(COLOR_MAP_SIZE - 1);
                colorMapData[i] = new RgbaFloat(v, v, v, 1);
            }

            // Hot
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float t = i / (float)(COLOR_MAP_SIZE - 1);
                float r = Math.Min(1.0f, 3.0f * t);
                float g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f);
                float b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f);
                colorMapData[COLOR_MAP_SIZE + i] = new RgbaFloat(r, g, b, 1);
            }

            // Cool
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float t = i / (float)(COLOR_MAP_SIZE - 1);
                colorMapData[COLOR_MAP_SIZE * 2 + i] = new RgbaFloat(t, 1 - t, 1, 1);
            }

            // Rainbow
            for (int i = 0; i < COLOR_MAP_SIZE; i++)
            {
                float h = (i / (float)(COLOR_MAP_SIZE - 1)) * 0.7f;
                colorMapData[COLOR_MAP_SIZE * 3 + i] = HsvToRgb(h, 1.0f, 1.0f);
            }

            // Create as 2D texture
            _colorMapTexture = factory.CreateTexture(TextureDescription.Texture2D(
                COLOR_MAP_SIZE, NUM_COLOR_MAPS, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            VeldridManager.GraphicsDevice.UpdateTexture(_colorMapTexture, colorMapData,
                0, 0, 0, COLOR_MAP_SIZE, NUM_COLOR_MAPS, 1, 0, 0);

            // Create material textures as 2D (256x1)
            _materialParamsTexture = factory.CreateTexture(TextureDescription.Texture2D(
                256, 1, 1, 1, PixelFormat.R32_G32_Float, TextureUsage.Sampled));
            _materialColorsTexture = factory.CreateTexture(TextureDescription.Texture2D(
                256, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));

            UpdateMaterialTextures();
        }

        private void CreateMetalPipeline(ResourceFactory factory)
        {
            // Main pipeline resource layout
            _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("LabelTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorMapTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialParamsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialColorsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("PreviewTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Main pipeline
            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    new[] { new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) },
                    _shaders),
                new[] { _resourceLayout },
                _framebuffer.OutputDescription));

            // Plane visualization pipeline
            _planeVisualizationResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            _planeVisualizationPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                new BlendStateDescription(RgbaFloat.Black, BlendAttachmentDescription.AlphaBlend),
                new DepthStencilStateDescription(true, false, ComparisonKind.Less),
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    new[] { new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) },
                    _planeVisualizationShaders),
                new[] { _planeVisualizationResourceLayout },
                _framebuffer.OutputDescription));
        }

        private void CreateResourceSets(ResourceFactory factory)
        {
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _constantBuffer,
                _volumeTexture,
                _labelTexture,
                _colorMapTexture,
                _materialParamsTexture,
                _materialColorsTexture,
                _previewTexture,
                _volumeSampler));

            _planeVisualizationResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _planeVisualizationResourceLayout,
                _planeVisualizationConstantBuffer));
        }

        public void UpdateMaterialTextures()
        {
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
                    paramData[i] = new Vector2(0, 1);
                    colorData[i] = RgbaFloat.Black;
                }
            }

            // Update 2D textures
            VeldridManager.GraphicsDevice.UpdateTexture(_materialParamsTexture, paramData, 0, 0, 0, 256, 1, 1, 0, 0);
            VeldridManager.GraphicsDevice.UpdateTexture(_materialColorsTexture, colorData, 0, 0, 0, 256, 1, 1, 0, 0);
        }

        public void UpdateLabelTexture(Texture newLabelTexture)
        {
            _labelTexture = newLabelTexture;

            // Recreate resource set with new texture
            _resourceSet?.Dispose();
            _resourceSet = VeldridManager.Factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _constantBuffer,
                _volumeTexture,
                _labelTexture,
                _colorMapTexture,
                _materialParamsTexture,
                _materialColorsTexture,
                _previewTexture,
                _volumeSampler));
        }

        public void Render(CommandList cl, Framebuffer framebuffer,
            Matrix4x4 viewMatrix, Matrix4x4 projMatrix, Vector3 cameraPosition,
            DeviceBuffer planeVertexBuffer, DeviceBuffer planeIndexBuffer)
        {
            cl.Begin();
            cl.SetFramebuffer(framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Black);
            cl.ClearDepthStencil(1f);

            // Update constant buffer
            UpdateConstantBuffer(viewMatrix, projMatrix, cameraPosition);

            // Render volume
            cl.SetPipeline(_pipeline);
            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.DrawIndexed(36, 1, 0, 0, 0);

            // Render plane visualizations if enabled
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
                ViewProj = viewMatrix * projMatrix,
                InvView = invView,
                CameraPosition = new Vector4(cameraPosition, 1),
                VolumeSize = new Vector4(_viewer._editableDataset.Width, _viewer._editableDataset.Height, _viewer._editableDataset.Depth, 0),
                ThresholdParams = new Vector4(_viewer.MinThreshold, _viewer.MaxThreshold, _viewer.StepSize, _viewer.ShowGrayscale ? 1 : 0),
                SliceParams = new Vector4(_viewer.SlicePositions, _viewer.ShowSlices ? 1 : 0),
                RenderParams = new Vector4(_viewer.ColorMapIndex, 0, 0, 0),
                CutPlaneX = new Vector4(_viewer.CutXEnabled ? 1 : 0, _viewer.CutXForward ? 1 : -1, _viewer.CutXPosition, 0),
                CutPlaneY = new Vector4(_viewer.CutYEnabled ? 1 : 0, _viewer.CutYForward ? 1 : -1, _viewer.CutYPosition, 0),
                CutPlaneZ = new Vector4(_viewer.CutZEnabled ? 1 : 0, _viewer.CutZForward ? 1 : -1, _viewer.CutZPosition, 0),
                ClippingPlanesInfo = new Vector4(0, _viewer.ShowPlaneVisualizations ? 1 : 0, 0, 0),
                PreviewParams = new Vector4(_viewer._showPreview ? 1 : 0, _viewer._previewColor.X, _viewer._previewColor.Y, _viewer._previewColor.Z),
                PreviewAlpha = new Vector4(_viewer._previewColor.W, 0, 0, 0)
            };

            // Add clipping planes
            int enabledPlanes = 0;
            for (int i = 0; i < Math.Min(_viewer.ClippingPlanes.Count, CtVolume3DViewer.MAX_CLIPPING_PLANES); i++)
            {
                var plane = _viewer.ClippingPlanes[i];
                if (plane.Enabled)
                {
                    float dist = plane.Distance + (plane.Mirror ? 1.0f : 0.0f);
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

        private void RenderPlaneVisualizations(CommandList cl, Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
            DeviceBuffer planeVertexBuffer, DeviceBuffer planeIndexBuffer)
        {
            cl.SetPipeline(_planeVisualizationPipeline);
            cl.SetVertexBuffer(0, planeVertexBuffer);
            cl.SetIndexBuffer(planeIndexBuffer, IndexFormat.UInt16);

            var viewProj = viewMatrix * projMatrix;

            // Render axis-aligned cutting planes
            if (_viewer.CutXEnabled && _viewer.ShowCutXPlaneVisual)
                RenderCuttingPlane(cl, viewProj, Vector3.UnitX, _viewer.CutXPosition, new Vector4(1, 0.2f, 0.2f, 0.3f));
            if (_viewer.CutYEnabled && _viewer.ShowCutYPlaneVisual)
                RenderCuttingPlane(cl, viewProj, Vector3.UnitY, _viewer.CutYPosition, new Vector4(0.2f, 1, 0.2f, 0.3f));
            if (_viewer.CutZEnabled && _viewer.ShowCutZPlaneVisual)
                RenderCuttingPlane(cl, viewProj, Vector3.UnitZ, _viewer.CutZPosition, new Vector4(0.2f, 0.2f, 1, 0.3f));

            // Render arbitrary clipping planes
            foreach (var plane in _viewer.ClippingPlanes.Where(p => p.Enabled && p.IsVisualizationVisible))
            {
                RenderClippingPlane(cl, viewProj, plane, new Vector4(1, 1, 0.2f, 0.3f));
            }
        }

        private void RenderCuttingPlane(CommandList cl, Matrix4x4 viewProj, Vector3 normal, float position, Vector4 color)
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

            var constants = new CtVolume3DViewer.PlaneVisualizationConstants
            {
                ViewProj = transform * viewProj,
                PlaneColor = color
            };

            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
            cl.DrawIndexed(6, 1, 0, 0, 0);
        }

        private void RenderClippingPlane(CommandList cl, Matrix4x4 viewProj, ClippingPlane plane, Vector4 color)
        {
            var forward = plane.Normal;
            var right = Vector3.Cross(Vector3.UnitY, forward);
            if (right.LengthSquared() < 0.001f)
                right = Vector3.Cross(Vector3.UnitX, forward);
            right = Vector3.Normalize(right);
            var up = Vector3.Cross(forward, right);

            var rotation = new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                up.X, up.Y, up.Z, 0,
                forward.X, forward.Y, forward.Z, 0,
                0, 0, 0, 1);

            var transform = Matrix4x4.CreateScale(1.5f) * rotation *
                          Matrix4x4.CreateTranslation(Vector3.One * 0.5f + plane.Normal * (plane.Distance - 0.5f));

            var constants = new CtVolume3DViewer.PlaneVisualizationConstants
            {
                ViewProj = transform * viewProj,
                PlaneColor = color
            };

            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
            cl.DrawIndexed(6, 1, 0, 0, 0);
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