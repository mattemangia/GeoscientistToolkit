// GeoscientistToolkit/Data/CtImageStack/MetalVolumeRenderer.cs
using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;
using Veldrid;
using Veldrid.SPIRV;
using System.Reflection;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Handles all Metal-specific rendering logic for the 3D volume viewer.
    /// This class creates and manages Metal-specific shaders, pipelines, and resources.
    /// </summary>
    internal class MetalVolumeRenderer : IDisposable
    {
        private readonly CtVolume3DViewer _viewer;

        // Metal-specific Veldrid resources
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
        private Texture _colorMapTexture;
        private Texture _materialParamsTexture;
        private Texture _materialColorsTexture;

        // Shared resources (references)
        private Texture _volumeTexture;
        private Texture _labelTexture;
        private Texture _previewTexture;
        private Sampler _volumeSampler;

        public MetalVolumeRenderer(CtVolume3DViewer viewer)
        {
            _viewer = viewer;
        }

        public void InitializeResources(ResourceFactory factory, Framebuffer framebuffer, Texture volumeTexture, Texture labelTexture, Texture previewTexture, Sampler volumeSampler)
        {
            Logger.Log("[MetalVolumeRenderer] Initializing Metal-specific resources...");
            // Store references to shared resources
            _volumeTexture = volumeTexture;
            _labelTexture = labelTexture;
            _previewTexture = previewTexture;
            _volumeSampler = volumeSampler;

            // Create Metal-specific resources
            CreateMetalShaders(factory);
            CreateMetalTextures(factory);
            CreateMetalPipeline(factory, framebuffer);

            _constantBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<CtVolume3DViewer.VolumeConstants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _planeVisualizationConstantBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<CtVolume3DViewer.PlaneVisualizationConstants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            CreateMetalResourceSet(factory);
            Logger.Log("[MetalVolumeRenderer] Metal initialization complete.");
        }

        private void CreateMetalShaders(ResourceFactory factory)
        {
            Logger.Log("[MetalVolumeRenderer] Compiling Metal shaders...");
            string metalVertexShaderGlsl = @"
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
    vec4 clipPos = ViewProj * vec4(in_Position, 1.0);
    // Metal-specific: ensure proper depth range
    clipPos.z = clipPos.z * 0.5 + clipPos.w * 0.5;
    gl_Position = clipPos;
}";

            string metalFragmentShaderGlsl = @"
#version 450
#extension GL_EXT_samplerless_texture_functions : enable

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
layout(set = 0, binding = 4) uniform texture2D ColorMapTexture; // Changed to 2D for Metal
layout(set = 0, binding = 5) uniform texture2D MaterialParamsTexture; // Changed to 2D for Metal
layout(set = 0, binding = 6) uniform texture2D MaterialColorsTexture; // Changed to 2D for Metal
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
            vec3 normal = planeData.xyz; 
            float dist = length(normal);
            if (dist > 0.001)
            {
                normal /= dist; 
                float mirror = step(1.5, dist);
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
    // Use 2D texture for Metal (1 pixel height)
    return textureLod(sampler2D(ColorMapTexture, VolumeSampler), vec2(samplePos, 0.5), 0.0);
}

void main()
{
    vec3 rayOrigin = CameraPosition.xyz;
    vec3 rayDir = normalize(in_ModelPos - rayOrigin);

    float tNear, tFar;
    if (!IntersectBox(rayOrigin, rayDir, vec3(0.0), vec3(1.0), tNear, tFar))
    {
        discard; // This will result in a black (or clear color) pixel if hit for every fragment.
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
        if (PreviewParams.x > 0.5 && textureLod(sampler3D(PreviewTexture, VolumeSampler), currentPos, 0.0).r > 0.5)
        {
            sampledColor = vec4(PreviewParams.yzw, PreviewAlpha.x * 5.0);
        }
        else
        {
            int materialId = int(textureLod(sampler3D(LabelTexture, VolumeSampler), currentPos, 0.0).r * 255.0 + 0.5);
            vec2 materialParams = texelFetch(sampler2D(MaterialParamsTexture, VolumeSampler), ivec2(materialId, 0), 0).xy;

            if (materialId > 0 && materialParams.x > 0.5) // materialParams.x is IsVisible
            {
                sampledColor = texelFetch(sampler2D(MaterialColorsTexture, VolumeSampler), ivec2(materialId, 0), 0);
                sampledColor.a = materialParams.y * 5.0;
            }
            else if (ThresholdParams.w > 0.5) // ShowGrayscale
            {
                float intensity = textureLod(sampler3D(VolumeTexture, VolumeSampler), currentPos, 0.0).r;
                if (intensity >= ThresholdParams.x && intensity <= ThresholdParams.y)
                {
                    float normIntensity = (intensity - ThresholdParams.x) / (ThresholdParams.y - ThresholdParams.x + 0.001);
                    sampledColor = (RenderParams.x > 0.5) ? ApplyColorMap(normIntensity) : vec4(vec3(normIntensity), normIntensity);
                    sampledColor.a = pow(sampledColor.a, 2.0);
                }
            }
        }
        
        if (sampledColor.a > 0.0)
        {
            float correctedAlpha = clamp(sampledColor.a * step * opacityScalar, 0.0, 1.0);
            accumulatedColor += (1.0 - accumulatedColor.a) * vec4(sampledColor.rgb * correctedAlpha, correctedAlpha);
        }
        t += step;
    }

    if (accumulatedColor.a == 0.0) {
        // If alpha is still 0, the pixel will be black.
        // This is not a real log, but a comment to highlight a key point for debugging.
    }

    out_Color = accumulatedColor;
}";

            string planeVertexShaderGlsl = @"
#version 450
layout(location = 0) in vec3 in_Position;
layout(set = 0, binding = 0) uniform Constants { mat4 ViewProj; vec4 PlaneColor; };
void main() { 
    vec4 clipPos = ViewProj * vec4(in_Position, 1.0);
    clipPos.z = clipPos.z * 0.5 + clipPos.w * 0.5; // Metal depth range fix
    gl_Position = clipPos;
}";

            string planeFragmentShaderGlsl = @"
#version 450
layout(location = 0) out vec4 out_Color;
layout(set = 0, binding = 0) uniform Constants { mat4 ViewProj; vec4 PlaneColor; };
void main() { out_Color = PlaneColor; }";

            try
            {
                // DEBUG: Verify Metal-specific compilation options
                var options = new CrossCompileOptions(fixClipSpaceZ: false, invertVertexOutputY: false, normalizeResourceNames: true);
                Logger.Log($"[MetalVolumeRenderer] CrossCompileOptions: FixClipSpaceZ={options.FixClipSpaceZ}, InvertY={options.InvertVertexOutputY}");

                var mainVertexDesc = new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(metalVertexShaderGlsl), "main");
                var mainFragmentDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(metalFragmentShaderGlsl), "main");
                _shaders = factory.CreateFromSpirv(mainVertexDesc, mainFragmentDesc, options);

                var planeVertexDesc = new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(planeVertexShaderGlsl), "main");
                var planeFragmentDesc = new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(planeFragmentShaderGlsl), "main");
                _planeVisualizationShaders = factory.CreateFromSpirv(planeVertexDesc, planeFragmentDesc, options);

                Logger.Log($"[MetalVolumeRenderer] Metal shaders compiled successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[MetalVolumeRenderer] FATAL: Failed to create Metal shaders. This is a primary cause for a black screen. Error: {ex.Message}\n{ex.StackTrace}");
                throw new InvalidOperationException("Failed to create Metal shaders for 3D volume rendering", ex);
            }
        }

        private void CreateMetalPipeline(ResourceFactory factory, Framebuffer framebuffer)
        {
            Logger.Log("[MetalVolumeRenderer] Creating Metal pipeline...");
            var resourceLayoutDesc = new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("VolumeTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("LabelTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorMapTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialParamsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialColorsTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("PreviewTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment));

            // DEBUG: Log resource layout to check binding order against the shader.
            for (int i = 0; i < resourceLayoutDesc.Elements.Length; i++)
            {
                Logger.Log($"[MetalVolumeRenderer] Main Layout Binding {i}: {resourceLayoutDesc.Elements[i].Name} ({resourceLayoutDesc.Elements[i].Kind})");
            }

            _resourceLayout = factory.CreateResourceLayout(resourceLayoutDesc);

            _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, _shaders),
                new[] { _resourceLayout }, framebuffer.OutputDescription));
            Logger.Log("[MetalVolumeRenderer] Main pipeline created.");

            // --- FIX START ---
            // First, create the description for the plane visualization layout.
            var planeResourceLayoutDesc = new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment));

            // DEBUG: Log the description's elements, not the final resource's.
            Logger.Log($"[MetalVolumeRenderer] Plane Layout Binding 0: {planeResourceLayoutDesc.Elements[0].Name} ({planeResourceLayoutDesc.Elements[0].Kind})");

            // Now, create the layout from the description.
            _planeVisualizationResourceLayout = factory.CreateResourceLayout(planeResourceLayoutDesc);
            // --- FIX END ---

            _planeVisualizationPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                new BlendStateDescription(RgbaFloat.Black, BlendAttachmentDescription.AlphaBlend),
                new DepthStencilStateDescription(true, false, ComparisonKind.Less),
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(new[] { new VertexLayoutDescription(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)) }, _planeVisualizationShaders),
                new[] { _planeVisualizationResourceLayout }, framebuffer.OutputDescription));
            Logger.Log("[MetalVolumeRenderer] Plane visualization pipeline created.");
        }

        private void CreateMetalTextures(ResourceFactory factory)
        {
            const int mapSize = 256;
            const int numMaps = 4;
            var colorMapData = new RgbaFloat[mapSize * numMaps];
            for (int i = 0; i < mapSize; i++) { float v = i / (float)(mapSize - 1); colorMapData[i] = new RgbaFloat(v, v, v, 1); }
            for (int i = 0; i < mapSize; i++) { float t = i / (float)(mapSize - 1); float r = Math.Min(1.0f, 3.0f * t); float g = Math.Clamp(3.0f * t - 1.0f, 0.0f, 1.0f); float b = Math.Clamp(3.0f * t - 2.0f, 0.0f, 1.0f); colorMapData[mapSize * 1 + i] = new RgbaFloat(r, g, b, 1); }
            for (int i = 0; i < mapSize; i++) { float t = i / (float)(mapSize - 1); colorMapData[mapSize * 2 + i] = new RgbaFloat(t, 1 - t, 1, 1); }
            for (int i = 0; i < mapSize; i++) { float h = (i / (float)(mapSize - 1)) * 0.7f; var rgb = HsvToRgb(h, 1.0f, 1.0f); colorMapData[mapSize * 3 + i] = new RgbaFloat(rgb.X, rgb.Y, rgb.Z, 1.0f); }

            // Use 2D texture for Metal (1 pixel height)
            _colorMapTexture = factory.CreateTexture(TextureDescription.Texture2D((uint)(mapSize * numMaps), 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            VeldridManager.GraphicsDevice.UpdateTexture(_colorMapTexture, colorMapData, 0, 0, 0, (uint)(mapSize * numMaps), 1, 1, 0, 0);

            _materialParamsTexture = factory.CreateTexture(TextureDescription.Texture2D(256, 1, 1, 1, PixelFormat.R32_G32_Float, TextureUsage.Sampled));
            _materialColorsTexture = factory.CreateTexture(TextureDescription.Texture2D(256, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));

            Logger.Log($"[MetalVolumeRenderer] Metal-specific 2D textures created (ColorMap: {_colorMapTexture.Width}x{_colorMapTexture.Height}, MaterialParams: {_materialParamsTexture.Width}x{_materialParamsTexture.Height}).");

            _viewer.UpdateMaterialTextures();
        }

        private void CreateMetalResourceSet(ResourceFactory factory)
        {
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _constantBuffer, _volumeSampler, _volumeTexture, _labelTexture, _colorMapTexture, _materialParamsTexture, _materialColorsTexture, _previewTexture));
            _planeVisualizationResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_planeVisualizationResourceLayout, _planeVisualizationConstantBuffer));
            Logger.Log("[MetalVolumeRenderer] Resource sets created.");
        }

        private unsafe void UpdateConstantBuffer(Matrix4x4 view, Matrix4x4 proj, Vector3 camPos)
        {
            Matrix4x4.Invert(view, out var invView);
            var constants = new CtVolume3DViewer.VolumeConstants
            {
                ViewProj = view * proj,
                InvView = invView,
                CameraPosition = new Vector4(camPos, 1),
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

            // DEBUG: Log key uniform values to check for bad data (NaN, etc.)
            if (float.IsNaN(constants.ViewProj.M11))
            {
                Logger.LogError("[MetalVolumeRenderer] ViewProj matrix contains NaN. This will cause a black screen.");
            }
            Logger.Log($"[MetalVolumeRenderer] Updating UBO: CamPos={constants.CameraPosition}, Thresholds={constants.ThresholdParams.X:F2}-{constants.ThresholdParams.Y:F2}, ShowGrayscale={constants.ThresholdParams.W > 0.5f}");

            VeldridManager.GraphicsDevice.UpdateBuffer(_constantBuffer, 0, ref constants);
        }

        public void UpdateMaterialTextures()
        {
            if (_materialParamsTexture == null || _materialColorsTexture == null) return;
            Logger.Log("[MetalVolumeRenderer] Updating material textures...");

            var paramData = new Vector2[256];
            var colorData = new RgbaFloat[256];
            bool anyVisible = false;
            for (int i = 0; i < 256; i++)
            {
                var material = _viewer._editableDataset.Materials.FirstOrDefault(m => m.ID == i);
                if (material != null)
                {
                    paramData[i] = new Vector2(material.IsVisible ? 1.0f : 0.0f, 1.0f);
                    colorData[i] = new RgbaFloat(material.Color);
                    if (material.IsVisible && i > 0) anyVisible = true;
                }
                else
                {
                    paramData[i] = new Vector2(0, 1);
                    colorData[i] = RgbaFloat.Black;
                }
            }

            // DEBUG: Warn if no materials are visible, as this would result in a black image (if ShowGrayscale is also off).
            if (!anyVisible && !(_viewer.ShowGrayscale))
            {
                Logger.LogWarning("[MetalVolumeRenderer] No materials are marked as visible and ShowGrayscale is OFF. The volume will likely appear black.");
            }

            VeldridManager.GraphicsDevice.UpdateTexture(_materialParamsTexture, paramData, 0, 0, 0, 256, 1, 1, 0, 0);
            VeldridManager.GraphicsDevice.UpdateTexture(_materialColorsTexture, colorData, 0, 0, 0, 256, 1, 1, 0, 0);
        }

        public void UpdateLabelTexture(Texture newLabelTexture)
        {
            _labelTexture = newLabelTexture;
            _resourceSet?.Dispose();
            Logger.LogWarning("[MetalVolumeRenderer] Label data changed, recreating resource set.");
            CreateMetalResourceSet(VeldridManager.Factory);
        }

        public void Render(CommandList cl, Framebuffer framebuffer, Matrix4x4 view, Matrix4x4 proj, Vector3 camPos, DeviceBuffer planeVtx, DeviceBuffer planeIdx)
        {
            Logger.Log("[MetalVolumeRenderer] Frame Render Start.");
            cl.Begin();
            cl.SetFramebuffer(framebuffer);
            cl.ClearColorTarget(0, RgbaFloat.Black);
            cl.ClearDepthStencil(1f);

            UpdateConstantBuffer(view, proj, camPos);

            cl.SetPipeline(_pipeline);

            // DEBUG: Getting buffers via reflection is fragile. Warn if it fails.
            var vertexBuffer = (DeviceBuffer)typeof(CtVolume3DViewer).GetField("_vertexBuffer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_viewer);
            var indexBuffer = (DeviceBuffer)typeof(CtVolume3DViewer).GetField("_indexBuffer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_viewer);

            if (vertexBuffer == null || indexBuffer == null)
            {
                Logger.LogError("[MetalVolumeRenderer] FATAL: Vertex or Index buffer is null. Cannot draw.");
                cl.End();
                VeldridManager.GraphicsDevice.SubmitCommands(cl); // Submit the clear command
                return;
            }

            cl.SetVertexBuffer(0, vertexBuffer);
            cl.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.DrawIndexed(36, 1, 0, 0, 0);
            Logger.Log("[MetalVolumeRenderer] Main volume cube drawn.");

            if (_viewer.ShowPlaneVisualizations)
            {
                RenderPlaneVisualizations(cl, view * proj, planeVtx, planeIdx);
                Logger.Log("[MetalVolumeRenderer] Plane visualizations drawn.");
            }

            cl.End();
            VeldridManager.GraphicsDevice.SubmitCommands(cl);
            VeldridManager.GraphicsDevice.WaitForIdle();
            Logger.Log("[MetalVolumeRenderer] Frame Render End.");
        }

        private void RenderPlaneVisualizations(CommandList cl, Matrix4x4 viewProj, DeviceBuffer planeVtx, DeviceBuffer planeIdx)
        {
            cl.SetPipeline(_planeVisualizationPipeline);
            cl.SetVertexBuffer(0, planeVtx);
            cl.SetIndexBuffer(planeIdx, IndexFormat.UInt16);

            if (_viewer.CutXEnabled && _viewer.ShowCutXPlaneVisual) RenderCuttingPlane(cl, viewProj, Vector3.UnitX, _viewer.CutXPosition, new Vector4(1, 0.2f, 0.2f, 0.3f));
            if (_viewer.CutYEnabled && _viewer.ShowCutYPlaneVisual) RenderCuttingPlane(cl, viewProj, Vector3.UnitY, _viewer.CutYPosition, new Vector4(0.2f, 1, 0.2f, 0.3f));
            if (_viewer.CutZEnabled && _viewer.ShowCutZPlaneVisual) RenderCuttingPlane(cl, viewProj, Vector3.UnitZ, _viewer.CutZPosition, new Vector4(0.2f, 0.2f, 1, 0.3f));
            foreach (var plane in _viewer.ClippingPlanes.Where(p => p.Enabled && p.IsVisualizationVisible)) RenderClippingPlane(cl, viewProj, plane, new Vector4(1, 1, 0.2f, 0.3f));
        }

        private void RenderCuttingPlane(CommandList cl, Matrix4x4 viewProj, Vector3 normal, float position, Vector4 color)
        {
            var transform = Matrix4x4.CreateScale(1.5f);
            if (normal == Vector3.UnitX) { transform *= Matrix4x4.CreateRotationY(MathF.PI / 2); transform *= Matrix4x4.CreateTranslation(position, 0.5f, 0.5f); }
            else if (normal == Vector3.UnitY) { transform *= Matrix4x4.CreateRotationX(-MathF.PI / 2); transform *= Matrix4x4.CreateTranslation(0.5f, position, 0.5f); }
            else if (normal == Vector3.UnitZ) { transform *= Matrix4x4.CreateTranslation(0.5f, 0.5f, position); }
            var constants = new CtVolume3DViewer.PlaneVisualizationConstants { ViewProj = transform * viewProj, PlaneColor = color };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
            cl.DrawIndexed(6, 1, 0, 0, 0);
        }

        private void RenderClippingPlane(CommandList cl, Matrix4x4 viewProj, ClippingPlane plane, Vector4 color)
        {
            var forward = plane.Normal;
            var right = Vector3.Cross(Vector3.UnitY, forward);
            if (right.LengthSquared() < 0.001f) right = Vector3.Cross(Vector3.UnitX, forward);
            right = Vector3.Normalize(right);
            var up = Vector3.Cross(forward, right);
            var rotation = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1);
            var transform = Matrix4x4.CreateScale(1.5f) * rotation * Matrix4x4.CreateTranslation(Vector3.One * 0.5f + plane.Normal * (plane.Distance - 0.5f));
            var constants = new CtVolume3DViewer.PlaneVisualizationConstants { ViewProj = transform * viewProj, PlaneColor = color };
            VeldridManager.GraphicsDevice.UpdateBuffer(_planeVisualizationConstantBuffer, 0, ref constants);
            cl.SetGraphicsResourceSet(0, _planeVisualizationResourceSet);
            cl.DrawIndexed(6, 1, 0, 0, 0);
        }

        private static Vector3 HsvToRgb(float h, float s, float v)
        {
            float r, g, b; int i = (int)(h * 6); float f = h * 6 - i; float p = v * (1 - s); float q = v * (1 - f * s); float t = v * (1 - (1 - f) * s);
            switch (i % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
            return new Vector3(r, g, b);
        }

        public void Dispose()
        {
            Logger.LogWarning("[MetalVolumeRenderer] Disposing Metal-specific resources.");
            _planeVisualizationResourceSet?.Dispose();
            _planeVisualizationResourceLayout?.Dispose();
            _planeVisualizationPipeline?.Dispose();
            if (_planeVisualizationShaders != null) { foreach (var shader in _planeVisualizationShaders) shader?.Dispose(); }
            _planeVisualizationConstantBuffer?.Dispose();

            _resourceSet?.Dispose();
            _resourceLayout?.Dispose();

            _materialColorsTexture?.Dispose();
            _materialParamsTexture?.Dispose();
            _colorMapTexture?.Dispose();

            _pipeline?.Dispose();
            if (_shaders != null) { foreach (var shader in _shaders) shader?.Dispose(); }

            _constantBuffer?.Dispose();
        }
    }
}