// GeoscientistToolkit/Data/CtImageStack/MetalVolumeRenderer.cs
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Util;
using Veldrid;
using System.Linq;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Metal-specific volume renderer with material support
    /// </summary>
    public class MetalVolumeRenderer : IDisposable
    {
        private readonly CtVolume3DViewer _viewer;

        // Core Veldrid resources
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private DeviceBuffer _constantBuffer;
        private Pipeline _pipeline;
        private ResourceLayout _resourceLayout;
        private ResourceSet _resourceSet;
        private Shader _vertexShader;
        private Shader _fragmentShader;

        // Textures
        private Texture _volumeTexture;
        private Texture _labelTexture;
        private Texture _colorMapTexture;
        private Texture _materialParamsTexture;
        private Texture _materialColorsTexture;
        private Texture _previewTexture;
        private Sampler _volumeSampler;

        // Plane visualization
        private Pipeline _planeVisualizationPipeline;
        private ResourceLayout _planeVisualizationResourceLayout;
        private ResourceSet _planeVisualizationResourceSet;
        private DeviceBuffer _planeVisualizationConstantBuffer;
        private Shader _planeVertexShader;
        private Shader _planeFragmentShader;

        private Framebuffer _framebuffer;
        private bool _isInitialized = false;

        public MetalVolumeRenderer(CtVolume3DViewer viewer)
        {
            _viewer = viewer;
            Logger.Log("[MetalVolumeRenderer] Created");
        }

        public void InitializeResources(ResourceFactory factory, Framebuffer framebuffer,
                                       Texture volumeTexture, Texture labelTexture,
                                       Texture previewTexture, Sampler volumeSampler)
        {
            try
            {
                Logger.Log("[MetalVolumeRenderer] Starting initialization...");

                _framebuffer = framebuffer;
                _volumeTexture = volumeTexture;
                _labelTexture = labelTexture;
                _previewTexture = previewTexture;
                _volumeSampler = volumeSampler;

                // Create geometry
                CreateGeometry(factory);

                // Create shaders
                CreateShaders(factory);

                // Create textures
                CreateTextures(factory);

                // Create pipeline
                CreatePipeline(factory);

                // Create constant buffer
                _constantBuffer = factory.CreateBuffer(new BufferDescription(
                    (uint)Marshal.SizeOf<CtVolume3DViewer.VolumeConstants>(),
                    BufferUsage.UniformBuffer | BufferUsage.Dynamic));

                _planeVisualizationConstantBuffer = factory.CreateBuffer(new BufferDescription(
                    (uint)Marshal.SizeOf<CtVolume3DViewer.PlaneVisualizationConstants>(),
                    BufferUsage.UniformBuffer | BufferUsage.Dynamic));

                // Create resource sets
                CreateResourceSets(factory);

                _isInitialized = true;
                Logger.Log("[MetalVolumeRenderer] Initialization complete!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[MetalVolumeRenderer] Initialization failed: {ex.Message}");
                throw;
            }
        }

        private void CreateGeometry(ResourceFactory factory)
        {
            // Create cube vertices
            float[] vertices = {
                // Position only (3 floats per vertex)
                0, 0, 0,
                1, 0, 0,
                1, 1, 0,
                0, 1, 0,
                0, 0, 1,
                1, 0, 1,
                1, 1, 1,
                0, 1, 1
            };

            _vertexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(vertices.Length * sizeof(float)),
                BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertices);

            // Create indices
            ushort[] indices = {
                0, 1, 2, 0, 2, 3,  // Front
                4, 6, 5, 4, 7, 6,  // Back
                0, 4, 5, 0, 5, 1,  // Bottom
                3, 2, 6, 3, 6, 7,  // Top
                0, 7, 4, 0, 3, 7,  // Left
                1, 5, 6, 1, 6, 2   // Right
            };

            _indexBuffer = factory.CreateBuffer(new BufferDescription(
                (uint)(indices.Length * sizeof(ushort)),
                BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);

            Logger.Log("[MetalVolumeRenderer] Geometry created");
        }

        private void CreateShaders(ResourceFactory factory)
        {
            // Metal shader with material and preview support
            const string metalShaderSource = @"
#include <metal_stdlib>
using namespace metal;

struct Constants {
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

struct PlaneConstants {
    float4x4 mvp;
    float4 color;
};

struct VertexIn {
    float3 Position [[attribute(0)]];
};

struct VertexOut {
    float4 Position [[position]];
    float3 ModelPos;
};

// Vertex shader
vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                            constant Constants& constants [[buffer(0)]]) {
    VertexOut out;
    out.ModelPos = in.Position;
    out.Position = constants.ViewProj * float4(in.Position, 1.0);
    return out;
}

// Fragment shader with material and preview support
fragment float4 fragment_main(VertexOut in [[stage_in]],
                             constant Constants& constants [[buffer(0)]],
                             texture3d<float> volumeTex [[texture(0)]],
                             texture3d<float> labelTex [[texture(1)]],
                             texture2d<float> materialParams [[texture(2)]],
                             texture2d<float> materialColors [[texture(3)]],
                             texture3d<float> previewTex [[texture(4)]],
                             sampler volumeSampler [[sampler(0)]]) {
    // Ray setup
    float3 rayOrigin = constants.CameraPosition.xyz;
    float3 rayDir = normalize(in.ModelPos - rayOrigin);
    
    // Ray-box intersection
    float3 invRayDir = 1.0 / (rayDir + 1e-8);
    float3 t1 = (float3(0.0) - rayOrigin) * invRayDir;
    float3 t2 = (float3(1.0) - rayOrigin) * invRayDir;
    float3 tMin = min(t1, t2);
    float3 tMax = max(t1, t2);
    float tNear = max(max(tMin.x, tMin.y), tMin.z);
    float tFar = min(min(tMax.x, tMax.y), tMax.z);
    
    if (tFar < tNear || tFar < 0.0) {
        discard_fragment();
    }
    
    tNear = max(tNear, 0.0);
    
    // Check cutting planes
    float3 cutPlanes[3] = {constants.CutPlaneX.xyz, constants.CutPlaneY.xyz, constants.CutPlaneZ.xyz};
    
    // Volume rendering with materials
    float4 color = float4(0.0);
    float stepSize = 0.01 * constants.ThresholdParams.z;
    int maxSteps = 500;
    
    for (int i = 0; i < maxSteps; i++) {
        float t = tNear + float(i) * stepSize;
        if (t > tFar) break;
        
        float3 pos = rayOrigin + t * rayDir;
        if (any(pos < 0.0) || any(pos > 1.0)) continue;
        
        // Check cutting planes
        bool cut = false;
        if (constants.CutPlaneX.x > 0.5 && (pos.x - constants.CutPlaneX.z) * constants.CutPlaneX.y > 0.0) cut = true;
        if (constants.CutPlaneY.x > 0.5 && (pos.y - constants.CutPlaneY.z) * constants.CutPlaneY.y > 0.0) cut = true;
        if (constants.CutPlaneZ.x > 0.5 && (pos.z - constants.CutPlaneZ.z) * constants.CutPlaneZ.y > 0.0) cut = true;
        
        if (cut) continue;
        
        float4 sampleColor = float4(0.0);
        
        // Check preview first (highest priority for acoustic simulation visualization)
        if (constants.PreviewParams.x > 0.5) {
            float previewValue = previewTex.sample(volumeSampler, pos).r;
            if (previewValue > 0.01) {
                // Acoustic wave visualization with intensity
                float intensity = previewValue;
                sampleColor = float4(
                    constants.PreviewParams.y * intensity,
                    constants.PreviewParams.z * intensity, 
                    constants.PreviewParams.w * intensity,
                    constants.PreviewAlpha.x * intensity
                );
            }
        }
        
        // If no preview, check for materials
        if (sampleColor.a == 0.0) {
            float labelValue = labelTex.sample(volumeSampler, pos).r;
            int materialId = int(labelValue * 255.0 + 0.5);
            
            if (materialId > 0) {
                // Sample material parameters and colors
                float2 params = materialParams.read(uint2(materialId, 0), 0).xy;
                bool isVisible = params.x > 0.5;
                
                if (isVisible) {
                    float4 matColor = materialColors.read(uint2(materialId, 0), 0);
                    sampleColor = float4(matColor.rgb, matColor.a * 0.5);
                }
            } else if (constants.ThresholdParams.w > 0.5) {
                // Sample grayscale volume
                float density = volumeTex.sample(volumeSampler, pos).r;
                
                if (density > constants.ThresholdParams.x && density < constants.ThresholdParams.y) {
                    float normalizedDensity = (density - constants.ThresholdParams.x) / 
                                            (constants.ThresholdParams.y - constants.ThresholdParams.x + 0.001);
                    sampleColor = float4(normalizedDensity, normalizedDensity, normalizedDensity, normalizedDensity * 0.3);
                }
            }
        }
        
        // Accumulate color
        if (sampleColor.a > 0.0) {
            float opacity = sampleColor.a * stepSize * 50.0;
            opacity = clamp(opacity, 0.0, 1.0);
            color.rgb += (1.0 - color.a) * sampleColor.rgb * opacity;
            color.a += (1.0 - color.a) * opacity;
            
            if (color.a > 0.95) break;
        }
    }
    
    return color;
}

// Plane visualization shaders
vertex float4 plane_vertex_main(VertexIn in [[stage_in]],
                               constant PlaneConstants& constants [[buffer(0)]]) {
    return constants.mvp * float4(in.Position, 1.0);
}

fragment float4 plane_fragment_main(constant PlaneConstants& constants [[buffer(0)]]) {
    return constants.color;
}
";

            try
            {
                // Create main shaders
                _vertexShader = factory.CreateShader(new ShaderDescription(
                    ShaderStages.Vertex,
                    Encoding.UTF8.GetBytes(metalShaderSource),
                    "vertex_main"));

                _fragmentShader = factory.CreateShader(new ShaderDescription(
                    ShaderStages.Fragment,
                    Encoding.UTF8.GetBytes(metalShaderSource),
                    "fragment_main"));

                // Create plane visualization shaders  
                _planeVertexShader = factory.CreateShader(new ShaderDescription(
                    ShaderStages.Vertex,
                    Encoding.UTF8.GetBytes(metalShaderSource),
                    "plane_vertex_main"));

                _planeFragmentShader = factory.CreateShader(new ShaderDescription(
                    ShaderStages.Fragment,
                    Encoding.UTF8.GetBytes(metalShaderSource),
                    "plane_fragment_main"));

                Logger.Log("[MetalVolumeRenderer] Shaders created successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[MetalVolumeRenderer] Shader creation failed: {ex.Message}");
                throw;
            }
        }

        private void CreateTextures(ResourceFactory factory)
        {
            // Create 2D textures for Metal compatibility
            const int size = 256;

            // Color map texture (grayscale for now)
            var colorMapData = new RgbaFloat[size * 4];
            for (int i = 0; i < size; i++)
            {
                float v = i / (float)(size - 1);
                for (int j = 0; j < 4; j++)
                {
                    colorMapData[i + j * size] = new RgbaFloat(v, v, v, 1);
                }
            }

            _colorMapTexture = factory.CreateTexture(TextureDescription.Texture2D(
                size, 4, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            VeldridManager.GraphicsDevice.UpdateTexture(_colorMapTexture, colorMapData,
                0, 0, 0, size, 4, 1, 0, 0);

            // Material textures - 2D for Metal
            _materialParamsTexture = factory.CreateTexture(TextureDescription.Texture2D(
                256, 1, 1, 1, PixelFormat.R32_G32_Float, TextureUsage.Sampled));
            _materialColorsTexture = factory.CreateTexture(TextureDescription.Texture2D(
                256, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));

            UpdateMaterialTextures();

            Logger.Log("[MetalVolumeRenderer] Textures created");
        }

        private void CreatePipeline(ResourceFactory factory)
        {
            // Create resource layout with all textures including preview
            _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("LabelTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialParamsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialColorsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("PreviewTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            // Create pipeline
            var pipelineDesc = new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    new[] {
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                    },
                    new[] { _vertexShader, _fragmentShader }),
                new[] { _resourceLayout },
                _framebuffer.OutputDescription);

            _pipeline = factory.CreateGraphicsPipeline(pipelineDesc);

            // Create plane visualization pipeline
            _planeVisualizationResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));

            _planeVisualizationPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                new BlendStateDescription(RgbaFloat.Black, BlendAttachmentDescription.AlphaBlend),
                new DepthStencilStateDescription(true, false, ComparisonKind.Less),
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    new[] {
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                    },
                    new[] { _planeVertexShader, _planeFragmentShader }),
                new[] { _planeVisualizationResourceLayout },
                _framebuffer.OutputDescription));

            Logger.Log("[MetalVolumeRenderer] Pipeline created");
        }

        private void CreateResourceSets(ResourceFactory factory)
        {
            // Create main resource set with all resources including preview texture
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _constantBuffer,
                _volumeTexture,
                _labelTexture,
                _materialParamsTexture,
                _materialColorsTexture,
                _previewTexture,
                _volumeSampler));

            // Plane visualization resource set
            _planeVisualizationResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _planeVisualizationResourceLayout,
                _planeVisualizationConstantBuffer));

            Logger.Log("[MetalVolumeRenderer] Resource sets created");
        }

        public void UpdateMaterialTextures()
        {
            if (_materialParamsTexture == null || _materialColorsTexture == null) return;

            var paramData = new Vector2[256];
            var colorData = new RgbaFloat[256];

            for (int i = 0; i < 256; i++)
            {
                var material = _viewer._editableDataset.Materials.FirstOrDefault(m => m.ID == i);
                if (material != null)
                {
                    // Get the opacity value from the viewer
                    float opacity = _viewer.GetMaterialOpacity((byte)i);
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

        public void UpdateLabelTexture(Texture newLabelTexture)
        {
            _labelTexture = newLabelTexture;

            // Recreate resource set with new label texture and preview texture
            _resourceSet?.Dispose();
            _resourceSet = VeldridManager.Factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _constantBuffer,
                _volumeTexture,
                _labelTexture,
                _materialParamsTexture,
                _materialColorsTexture,
                _previewTexture,
                _volumeSampler));

            Logger.Log("[MetalVolumeRenderer] Label texture updated");
        }

        public void UpdatePreviewTexture(Texture newPreviewTexture)
        {
            _previewTexture = newPreviewTexture;

            // Recreate resource set with new preview texture
            _resourceSet?.Dispose();
            _resourceSet = VeldridManager.Factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _constantBuffer,
                _volumeTexture,
                _labelTexture,
                _materialParamsTexture,
                _materialColorsTexture,
                _previewTexture,
                _volumeSampler));

            Logger.Log("[MetalVolumeRenderer] Preview texture updated");
        }

        public void Render(CommandList cl, Framebuffer framebuffer,
                          Matrix4x4 viewMatrix, Matrix4x4 projMatrix, Vector3 cameraPosition,
                          DeviceBuffer planeVertexBuffer, DeviceBuffer planeIndexBuffer)
        {
            if (!_isInitialized)
            {
                Logger.LogWarning("[MetalVolumeRenderer] Not initialized, skipping render");
                return;
            }

            try
            {
                cl.Begin();
                cl.SetFramebuffer(framebuffer);
                cl.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.1f, 1.0f));
                cl.ClearDepthStencil(1f);

                // Update constants with all parameters including preview
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

                // Set clipping planes data
                unsafe
                {
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
                }

                VeldridManager.GraphicsDevice.UpdateBuffer(_constantBuffer, 0, ref constants);

                // Make sure material textures are up to date
                _viewer.UpdateMaterialTextures();

                // Draw volume
                cl.SetPipeline(_pipeline);
                cl.SetVertexBuffer(0, _vertexBuffer);
                cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
                cl.SetGraphicsResourceSet(0, _resourceSet);
                cl.DrawIndexed(36, 1, 0, 0, 0);

                // Draw plane visualizations if enabled
                if (_viewer.ShowPlaneVisualizations && planeVertexBuffer != null && planeIndexBuffer != null)
                {
                    RenderPlaneVisualizations(cl, viewMatrix, projMatrix, planeVertexBuffer, planeIndexBuffer);
                }

                if (AcousticIntegration.IsActiveFor(_viewer._editableDataset))
                {
                    RenderTransducerMarkers(cl, viewMatrix, projMatrix);
                }


                cl.End();
                VeldridManager.GraphicsDevice.SubmitCommands(cl);
                VeldridManager.GraphicsDevice.WaitForIdle();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[MetalVolumeRenderer] Render error: {ex.Message}");
            }
        }
        
        private void RenderTransducerMarkers(CommandList cl, Matrix4x4 viewMatrix, Matrix4x4 projMatrix)
        {
            cl.SetPipeline(_planeVisualizationPipeline);
            cl.SetVertexBuffer(0, _vertexBuffer); // Use the cube vertex buffer
            cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16); // Use the cube index buffer
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);

            var viewProj = viewMatrix * projMatrix;

            // Draw TX Marker
            var txTransform = Matrix4x4.CreateScale(0.02f) * Matrix4x4.CreateTranslation(AcousticIntegration.TxPosition);
            var txConstants = new CtVolume3DViewer.PlaneVisualizationConstants { ViewProj = txTransform * viewProj, PlaneColor = new Vector4(0, 1, 1, 1) };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref txConstants);
            cl.DrawIndexed(36, 1, 0, 0, 0);

            // Draw RX Marker
            var rxTransform = Matrix4x4.CreateScale(0.02f) * Matrix4x4.CreateTranslation(AcousticIntegration.RxPosition);
            var rxConstants = new CtVolume3DViewer.PlaneVisualizationConstants { ViewProj = rxTransform * viewProj, PlaneColor = new Vector4(0, 1, 0, 1) };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref rxConstants);
            cl.DrawIndexed(36, 1, 0, 0, 0);
        }

        private void RenderPlaneVisualizations(CommandList cl, Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
                                              DeviceBuffer planeVertexBuffer, DeviceBuffer planeIndexBuffer)
        {
            cl.SetPipeline(_planeVisualizationPipeline);
            cl.SetVertexBuffer(0, planeVertexBuffer);
            cl.SetIndexBuffer(planeIndexBuffer, IndexFormat.UInt16);
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);

            var viewProj = viewMatrix * projMatrix;

            // Render cutting planes
            if (_viewer.CutXEnabled && _viewer.ShowCutXPlaneVisual)
            {
                RenderCuttingPlane(cl, viewProj, Vector3.UnitX, _viewer.CutXPosition, new Vector4(1, 0.2f, 0.2f, 0.3f));
            }

            if (_viewer.CutYEnabled && _viewer.ShowCutYPlaneVisual)
            {
                RenderCuttingPlane(cl, viewProj, Vector3.UnitY, _viewer.CutYPosition, new Vector4(0.2f, 1, 0.2f, 0.3f));
            }

            if (_viewer.CutZEnabled && _viewer.ShowCutZPlaneVisual)
            {
                RenderCuttingPlane(cl, viewProj, Vector3.UnitZ, _viewer.CutZPosition, new Vector4(0.2f, 0.2f, 1, 0.3f));
            }

            // Render clipping planes
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

            var constants = new CtVolume3DViewer.PlaneVisualizationConstants { ViewProj = transform * viewProj, PlaneColor = color };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
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

            var constants = new CtVolume3DViewer.PlaneVisualizationConstants { ViewProj = transform * viewProj, PlaneColor = color };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
            cl.DrawIndexed(6, 1, 0, 0, 0);
        }

        public void Dispose()
        {
            _planeVisualizationResourceSet?.Dispose();
            _planeVisualizationResourceLayout?.Dispose();
            _planeVisualizationPipeline?.Dispose();
            _planeVisualizationConstantBuffer?.Dispose();
            _planeFragmentShader?.Dispose();
            _planeVertexShader?.Dispose();

            _resourceSet?.Dispose();
            _resourceLayout?.Dispose();
            _pipeline?.Dispose();
            _fragmentShader?.Dispose();
            _vertexShader?.Dispose();

            _materialColorsTexture?.Dispose();
            _materialParamsTexture?.Dispose();
            _colorMapTexture?.Dispose();

            _constantBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _vertexBuffer?.Dispose();

            Logger.Log("[MetalVolumeRenderer] Disposed");
        }
    }
}