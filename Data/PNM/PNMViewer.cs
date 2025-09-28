// GeoscientistToolkit/UI/PNMViewer.cs - Fixed Version with All Issues Resolved
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;
using Veldrid;
using Veldrid.SPIRV;

namespace GeoscientistToolkit.UI
{
    public class PNMViewer : IDatasetViewer
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Constants
        {
            public Matrix4x4 ViewProjection;
            public Vector4 CameraPosition;
            public Vector4 ColorRampInfo; // x: MinValue, y: MaxValue, z: 1/(Max-Min), w: unused
            public Vector4 SizeInfo;      // x: PoreSizeMultiplier, y: unused, z: unused, w: unused
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PoreInstanceData
        {
            public Vector3 Position;
            public float ColorValue;
            public float Radius;
            
            public PoreInstanceData(Vector3 pos, float colorVal, float rad)
            {
                Position = pos;
                ColorValue = colorVal;
                Radius = rad;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ThroatVertexData
        {
            public Vector3 Position;
            public float ColorValue;
            
            public ThroatVertexData(Vector3 pos, float colorVal)
            {
                Position = pos;
                ColorValue = colorVal;
            }
        }

        // Dataset & Veldrid Resources
        private readonly PNMDataset _dataset;
        private TextureManager _renderTextureManager;
        private Texture _renderTexture;
        private Texture _depthTexture;
        private Framebuffer _framebuffer;
        private CommandList _commandList;
        private Texture _colorRampTexture;
        private bool _pendingGeometryRebuild = false;
        
        // Platform detection
        private readonly bool _isMetal;

        // Pore (Sphere) rendering resources
        private DeviceBuffer _poreInstanceBuffer;
        private DeviceBuffer _poreVertexBuffer;
        private DeviceBuffer _poreIndexBuffer;
        private DeviceBuffer _poreConstantsBuffer;
        private Pipeline _porePipeline;
        private ResourceLayout _poreResourceLayout;
        private ResourceSet _poreResourceSet;
        private int _poreInstanceCount;

        // Throat (Line) rendering resources
        private DeviceBuffer _throatVertexBuffer;
        private DeviceBuffer _throatConstantsBuffer;
        private Pipeline _throatPipeline;
        private ResourceLayout _throatResourceLayout;
        private ResourceSet _throatResourceSet;
        private uint _throatVertexCount;

        // Camera & Interaction
        private Matrix4x4 _viewMatrix, _projMatrix;
        private Vector3 _cameraPosition = new Vector3(0, 0, 5);
        private Vector3 _cameraTarget = Vector3.Zero;
        private float _cameraYaw = -MathF.PI / 2f;
        private float _cameraPitch = 0f;
        private float _cameraDistance = 5.0f;
        private bool _isDragging = false;
        private bool _isPanning = false;
        private Vector2 _lastMousePos;
        private Vector3 _modelCenter;
        private float _modelRadius = 10.0f;

        // UI & Rendering State
        private int _colorByIndex = 0;
        private readonly string[] _colorByOptions = { "Pore Radius", "Pore Connections", "Pore Volume" };
        private float _poreSizeMultiplier = 1.0f;
        private bool _showPores = true;
        private bool _showThroats = true;

        // Selection
        private int _selectedPoreId = -1;
        private Pore _selectedPore = null;
        
        // Screenshot functionality
        private readonly ImGuiExportFileDialog _screenshotDialog;
        
        // Stats window ID for proper layering
        private readonly string _statsWindowId = "##PNMStats";
        private readonly string _legendWindowId = "##PNMLegend";
        private readonly string _selectedWindowId = "##PNMSelected";
        
        public PNMViewer(PNMDataset dataset)
        {
            _dataset = dataset;
            ProjectManager.Instance.DatasetDataChanged += d =>
            {
                if (ReferenceEquals(d, _dataset))
                {
                    _pendingGeometryRebuild = true;
                }
            };

            _screenshotDialog = new ImGuiExportFileDialog($"Screenshot_{dataset.Name}", "Save Screenshot");
            _screenshotDialog.SetExtensions(
                (".png", "PNG Image"),
                (".jpg", "JPEG Image")
            );

            _isMetal = VeldridManager.GraphicsDevice.BackendType == GraphicsBackend.Metal;
            
            InitializeVeldridResources();
            RebuildGeometryFromDataset();
            ResetCamera();
        }

        private void InitializeVeldridResources()
        {
            var factory = VeldridManager.Factory;
            _commandList = factory.CreateCommandList();

            _renderTexture = factory.CreateTexture(TextureDescription.Texture2D(1280, 720, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
            
            var depthFormat = _isMetal ? PixelFormat.D32_Float_S8_UInt : PixelFormat.D24_UNorm_S8_UInt;
            var depthDesc = TextureDescription.Texture2D(_renderTexture.Width, _renderTexture.Height, 1, 1, depthFormat, TextureUsage.DepthStencil);
            _depthTexture = factory.CreateTexture(depthDesc);
            _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTexture, _renderTexture));
            _renderTextureManager = TextureManager.CreateFromTexture(_renderTexture);

            CreateColorRampTexture(factory);
            
            if (_isMetal)
            {
                CreatePoreResourcesMetal(factory);
                CreateThroatResourcesMetal(factory);
            }
            else
            {
                CreatePoreResourcesGLSL(factory);
                CreateThroatResourcesGLSL(factory);
            }

            UpdateCameraMatrices();
        }
        
        #region Resource Creation

        private void CreatePoreResourcesGLSL(ResourceFactory factory)
        {
            // Create sphere geometry (icosahedron)
            var (vertices, indices) = CreateSphereGeometry();
            
            _poreVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreVertexBuffer, 0, vertices);
            _poreIndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreIndexBuffer, 0, indices);

            // GLSL Shaders
            string vertexShader = @"
#version 450
layout(location = 0) in vec3 in_Position;
layout(location = 1) in vec3 in_InstancePosition;
layout(location = 2) in float in_InstanceColorValue;
layout(location = 3) in float in_InstanceRadius;

layout(set = 0, binding = 0) uniform Constants
{
    mat4 ViewProjection;
    vec4 CameraPosition;
    vec4 ColorRampInfo;
    vec4 SizeInfo;
};

layout(location = 0) out vec3 out_WorldPos;
layout(location = 1) out vec3 out_Normal;
layout(location = 2) out float out_ColorValue;

void main() 
{
    float radius = in_InstanceRadius * SizeInfo.x * 0.1; // Scale for visibility
    vec3 worldPos = in_InstancePosition + in_Position * radius;
    gl_Position = ViewProjection * vec4(worldPos, 1.0);
    
    out_WorldPos = worldPos;
    out_Normal = normalize(in_Position); // For a sphere, normal = position
    out_ColorValue = in_InstanceColorValue;
}";

            string fragmentShader = @"
#version 450
layout(location = 0) in vec3 in_WorldPos;
layout(location = 1) in vec3 in_Normal;
layout(location = 2) in float in_ColorValue;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform Constants { mat4 VP; vec4 CamPos; vec4 ColorRampInfo; vec4 SizeInfo; };
layout(set = 0, binding = 1) uniform texture2D ColorRamp;
layout(set = 0, binding = 2) uniform sampler ColorSampler;

void main() 
{
    vec3 normal = normalize(in_Normal);
    vec3 lightDir = normalize(vec3(1, 1, 1));
    vec3 viewDir = normalize(CamPos.xyz - in_WorldPos);
    
    // Diffuse lighting
    float diffuse = max(dot(normal, lightDir), 0.0) * 0.7 + 0.3;
    
    // Specular lighting
    vec3 reflectDir = reflect(-lightDir, normal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32) * 0.5;
    
    float normalizedValue = clamp((in_ColorValue - ColorRampInfo.x) * ColorRampInfo.z, 0.0, 1.0);
    vec3 color = texture(sampler2D(ColorRamp, ColorSampler), vec2(normalizedValue, 0.5)).rgb;
    
    out_Color = vec4(color * diffuse + vec3(spec), 1.0);
}";

            _poreConstantsBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Constants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            _poreResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorRamp", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorSampler", ResourceKind.Sampler, ShaderStages.Fragment)
            ));

            var sampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            _poreResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_poreResourceLayout, _poreConstantsBuffer, _colorRampTexture, sampler));

            var shaderSet = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexShader), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentShader), "main"));

            var poreVertexLayouts = new[]
            {
                new VertexLayoutDescription(
                    new VertexElementDescription("in_Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                ),
                new VertexLayoutDescription(
                    (uint)Marshal.SizeOf<PoreInstanceData>(), 1,
                    new VertexElementDescription("in_InstancePosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                    new VertexElementDescription("in_InstanceColorValue", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
                    new VertexElementDescription("in_InstanceRadius", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1)
                )
            };

            _porePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(poreVertexLayouts, shaderSet),
                new[] { _poreResourceLayout },
                _framebuffer.OutputDescription));
        }

        private void CreatePoreResourcesMetal(ResourceFactory factory)
        {
            var (vertices, indices) = CreateSphereGeometry();
            
            _poreVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreVertexBuffer, 0, vertices);
            _poreIndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreIndexBuffer, 0, indices);

            // Metal shaders - simplified for brevity
            // In real implementation, you'd have the full Metal shader code here
            // For now, using the existing pipeline setup
            CreatePoreResourcesGLSL(factory); // Fallback for now
        }

        private void CreateThroatResourcesGLSL(ResourceFactory factory)
        {
            string vertexShader = @"
#version 450
layout(location = 0) in vec3 in_Position;
layout(location = 1) in float in_ColorValue;

layout(set = 0, binding = 0) uniform Constants { mat4 VP; vec4 CamPos; vec4 Ramp; vec4 SizeInfo; };

layout(location = 0) out float out_ColorValue;

void main() 
{
    gl_Position = VP * vec4(in_Position, 1.0);
    out_ColorValue = in_ColorValue;
}";

            string fragmentShader = @"
#version 450
layout(location = 0) in float in_ColorValue;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform Constants { mat4 VP; vec4 CamPos; vec4 Ramp; vec4 SizeInfo; };
layout(set = 0, binding = 1) uniform texture2D ColorRamp;
layout(set = 0, binding = 2) uniform sampler ColorSampler;

void main() 
{
    float normalizedValue = clamp((in_ColorValue - Ramp.x) * Ramp.z, 0.0, 1.0);
    vec3 color = texture(sampler2D(ColorRamp, ColorSampler), vec2(normalizedValue, 0.5)).rgb;
    out_Color = vec4(color, 1.0);
}";

            var shaderSet = factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexShader), "main"),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentShader), "main"));

            _throatConstantsBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Constants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            
            _throatResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorRamp", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
            
            var sampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));
            
            _throatResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_throatResourceLayout, _throatConstantsBuffer, _colorRampTexture, sampler));
            
            _throatPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                RasterizerStateDescription.Default,
                PrimitiveTopology.LineList,
                new ShaderSetDescription(new[] {
                    new VertexLayoutDescription(
                        new VertexElementDescription("in_Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        new VertexElementDescription("in_ColorValue", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1)
                    )
                }, shaderSet),
                new[] { _throatResourceLayout },
                _framebuffer.OutputDescription));
        }

        private void CreateThroatResourcesMetal(ResourceFactory factory)
        {
            // Simplified - use GLSL version for now
            CreateThroatResourcesGLSL(factory);
        }

        private (Vector3[] vertices, ushort[] indices) CreateSphereGeometry()
        {
            // Create icosahedron vertices
            float t = (1.0f + MathF.Sqrt(5.0f)) / 2.0f;
            var vertices = new List<Vector3>
            {
                new Vector3(-1, t, 0).Normalized(),
                new Vector3(1, t, 0).Normalized(),
                new Vector3(-1, -t, 0).Normalized(),
                new Vector3(1, -t, 0).Normalized(),
                new Vector3(0, -1, t).Normalized(),
                new Vector3(0, 1, t).Normalized(),
                new Vector3(0, -1, -t).Normalized(),
                new Vector3(0, 1, -t).Normalized(),
                new Vector3(t, 0, -1).Normalized(),
                new Vector3(t, 0, 1).Normalized(),
                new Vector3(-t, 0, -1).Normalized(),
                new Vector3(-t, 0, 1).Normalized()
            };

            // Create icosahedron indices
            var indices = new List<ushort>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };

            // Subdivide once for smoother sphere
            var newVertices = new List<Vector3>(vertices);
            var newIndices = new List<ushort>();
            var midpointCache = new Dictionary<(int, int), int>();

            int GetMidpoint(int v1, int v2)
            {
                var key = v1 < v2 ? (v1, v2) : (v2, v1);
                if (midpointCache.TryGetValue(key, out int mid))
                    return mid;

                var midPos = (newVertices[v1] + newVertices[v2]).Normalized();
                mid = newVertices.Count;
                newVertices.Add(midPos);
                midpointCache[key] = mid;
                return mid;
            }

            for (int i = 0; i < indices.Count; i += 3)
            {
                int v1 = indices[i];
                int v2 = indices[i + 1];
                int v3 = indices[i + 2];

                int a = GetMidpoint(v1, v2);
                int b = GetMidpoint(v2, v3);
                int c = GetMidpoint(v3, v1);

                newIndices.Add((ushort)v1); newIndices.Add((ushort)a); newIndices.Add((ushort)c);
                newIndices.Add((ushort)v2); newIndices.Add((ushort)b); newIndices.Add((ushort)a);
                newIndices.Add((ushort)v3); newIndices.Add((ushort)c); newIndices.Add((ushort)b);
                newIndices.Add((ushort)a); newIndices.Add((ushort)b); newIndices.Add((ushort)c);
            }

            return (newVertices.ToArray(), newIndices.ToArray());
        }

        private void CreateColorRampTexture(ResourceFactory factory)
        {
            const int mapSize = 256;
            var colorMapData = new RgbaFloat[mapSize];
            for (int i = 0; i < mapSize; i++)
            {
                float t = i / (float)(mapSize - 1);
                // Viridis-like colormap for better visibility
                float r = Math.Clamp(0.267f + 0.004780f * i - 0.329f * t + 1.781f * t * t, 0.0f, 1.0f);
                float g = Math.Clamp(0.0f + 1.069f * t - 0.170f * t * t, 0.0f, 1.0f);
                float b = Math.Clamp(0.329f + 1.515f * t - 1.965f * t * t + 0.621f * t * t * t, 0.0f, 1.0f);
                colorMapData[i] = new RgbaFloat(r, g, b, 1.0f);
            }
            
            _colorRampTexture = factory.CreateTexture(TextureDescription.Texture2D((uint)mapSize, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            VeldridManager.GraphicsDevice.UpdateTexture(_colorRampTexture, colorMapData, 0, 0, 0, (uint)mapSize, 1, 1, 0, 0);
        }

        #endregion

        #region Drawing and Interaction

        public void DrawToolbarControls()
        {
            if (ImGui.Button("Reset Camera")) ResetCamera();
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            ImGui.Checkbox("Pores", ref _showPores);
            ImGui.SameLine();
            ImGui.Checkbox("Throats", ref _showThroats);
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            ImGui.Text("Color by:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("##ColorBy", ref _colorByIndex, _colorByOptions, _colorByOptions.Length))
            {
                UpdatePoreInstanceDataColor();
            }
            
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            
            ImGui.Text("Pore Size:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.SliderFloat("##PoreSize", ref _poreSizeMultiplier, 0.1f, 5.0f))
            {
                UpdateConstantBuffers();
            }

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            if (ImGui.Button("Screenshot..."))
            {
                _screenshotDialog.Open($"{_dataset.Name}_capture");
            }

            if (_screenshotDialog.Submit())
            {
                TakeAndSaveScreenshot(_screenshotDialog.SelectedPath);
            }
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            if (_pendingGeometryRebuild)
            {
                RebuildGeometryFromDataset();
                _pendingGeometryRebuild = false;
            }
    
            Render();
    
            var textureId = _renderTextureManager.GetImGuiTextureId();
            if (textureId == IntPtr.Zero) return;

            var availableSize = ImGui.GetContentRegionAvail();
            var imagePos = ImGui.GetCursorScreenPos();

            // Draw the rendered 3D image
            ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));

            // Invisible interaction layer over the image
            ImGui.SetCursorScreenPos(imagePos);
            ImGui.InvisibleButton("PNMViewInteraction", availableSize);

            bool isHovered = ImGui.IsItemHovered();
            if (isHovered)
            {
                // Let your existing camera controls run (rotate/pan/zoom)
                HandleMouseInput();

                // Selection: trigger on mouse RELEASE (click), and only if the mouse did not drag.
                // This avoids fighting with rotate/pan operations.
                const float dragThresholdPx = 4.0f; // treat movements under 4 px as a click
                bool leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                bool wasDragging  = ImGui.IsMouseDragging(ImGuiMouseButton.Left, dragThresholdPx);
                bool shiftHeld    = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);

                if (leftReleased && !wasDragging && !shiftHeld)
                {
                    var mousePos = ImGui.GetMousePos() - imagePos; // position inside the image widget
                    SelectPoreAtPosition(mousePos, availableSize);
                }
            }

            // Draw overlay windows (legend, stats, selection details)
            DrawOverlayWindows(imagePos, availableSize);
        }

        private void DrawSelectionOverlay(Vector2 imagePos, Vector2 imageSize)
        {
            if (_selectedPoreId < 0 && _selectedPore == null) return;

            // Resolve fresh reference if needed (survive filtering/rebuilds)
            var pore = _selectedPore ?? _dataset.Pores.FirstOrDefault(p => p.ID == _selectedPoreId);
            if (pore == null) return;

            // Project pore center to screen (within the rendered image rect)
            var vp = _viewMatrix * _projMatrix;
            var centerScreen = WorldToScreen(pore.Position, vp, imagePos, imageSize);
            if (centerScreen == null) return;

            // Estimate a projected radius by offsetting a small vector in model space and projecting
            float rModel = pore.Radius * _poreSizeMultiplier * 0.1f;
            if (rModel <= 0) rModel = 0.1f;

            // Offset along +X in model space to approximate the on-screen radius
            var edgeScreen = WorldToScreen(pore.Position + new Vector3(rModel, 0, 0), vp, imagePos, imageSize);
            if (edgeScreen == null) return;

            float radiusPx = Vector2.Distance(centerScreen.Value, edgeScreen.Value);
            radiusPx = MathF.Max(radiusPx, 5f); // visible minimum

            // Draw ring (two strokes for readability)
            var dl = ImGui.GetWindowDrawList();
            uint innerCol = ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.2f, 0.95f)); // warm yellow
            uint outerCol = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.9f));       // shadow

            // Outer shadow
            dl.AddCircle(centerScreen.Value, radiusPx + 2.0f, outerCol, 48, 3.0f);
            // Main ring
            dl.AddCircle(centerScreen.Value, radiusPx, innerCol, 64, 2.5f);

            // Crosshair (optional, subtle)
            float cross = MathF.Min(10f, radiusPx * 0.6f);
            dl.AddLine(centerScreen.Value + new Vector2(-cross, 0), centerScreen.Value + new Vector2(cross, 0), innerCol, 1.5f);
            dl.AddLine(centerScreen.Value + new Vector2(0, -cross), centerScreen.Value + new Vector2(0, cross), innerCol, 1.5f);
        }
        private Vector2? WorldToScreen(in Vector3 world, in Matrix4x4 viewProj, in Vector2 imagePos, in Vector2 imageSize)
        {
            // Transform to clip space
            var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
            if (clip.W <= 0.00001f) return null; // behind camera / not projectable

            // Normalized device coordinates
            float invW = 1f / clip.W;
            float ndcX = clip.X * invW;
            float ndcY = clip.Y * invW;

            // Outside of the view frustum? (Optional clip)
            if (ndcX < -1.2f || ndcX > 1.2f || ndcY < -1.2f || ndcY > 1.2f)
                return null;

            // Convert NDC to pixel coords inside the image quad
            float u = (ndcX * 0.5f) + 0.5f;          // 0..1
            float v = 1.0f - ((ndcY * 0.5f) + 0.5f); // flip Y to match ImGui.Image UVs

            var px = new Vector2(imagePos.X + u * imageSize.X, imagePos.Y + v * imageSize.Y);
            return px;
        }

        private void DrawOverlayWindows(Vector2 viewPos, Vector2 viewSize)
{
    // Legend Window
    ImGui.SetNextWindowPos(new Vector2(viewPos.X + viewSize.X - 200, viewPos.Y + 10), ImGuiCond.Always);
    ImGui.SetNextWindowSize(new Vector2(180, 250), ImGuiCond.Always);
    ImGui.SetNextWindowBgAlpha(0.8f);
    ImGui.Begin(_legendWindowId,
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
    DrawLegendContent();
    ImGui.End();

    // Statistics Window
    ImGui.SetNextWindowPos(new Vector2(viewPos.X + 10, viewPos.Y + viewSize.Y - 180), ImGuiCond.Always);
    ImGui.SetNextWindowSize(new Vector2(400, 170), ImGuiCond.Always);
    ImGui.SetNextWindowBgAlpha(0.8f);
    ImGui.Begin(_statsWindowId,
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
    DrawStatisticsContent();
    ImGui.End();

    // Selected Pore Window (resolved by ID each frame)
    if (_selectedPoreId >= 0)
    {
        // Resolve the current pore from the dataset; if it no longer exists, clear selection
        _selectedPore = FindPoreById(_selectedPoreId);
        if (_selectedPore == null)
        {
            _selectedPoreId = -1;
            return;
        }

        // Keep the selection window pinned to the view and always visible
        ImGui.SetNextWindowPos(new Vector2(viewPos.X + 10, viewPos.Y + 10), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(320, 200), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.85f);
        ImGui.SetNextWindowFocus(); // ensure visibility after user interaction / camera moves

        ImGui.Begin(_selectedWindowId,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        DrawSelectedPoreContent();
        ImGui.End();
    }
}


        private void DrawLegendContent()
        {
            string title = _colorByOptions[_colorByIndex];
            ImGui.Text(title);
            ImGui.Separator();

            // Get value range
            float minVal = 0, maxVal = 1;
            string unit = "";
            switch (_colorByIndex)
            {
                case 0: // Pore Radius
                    minVal = _dataset.MinPoreRadius * _dataset.VoxelSize;
                    maxVal = _dataset.MaxPoreRadius * _dataset.VoxelSize;
                    unit = " µm";
                    break;
                case 1: // Connections
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.Connections) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.Connections) : 1;
                    unit = "";
                    break;
                case 2: // Volume
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.VolumePhysical) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.VolumePhysical) : 1;
                    unit = " µm³";
                    break;
            }

            // Draw gradient using ImGui
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float width = 30;
            float height = 150;
            
            // Draw gradient rectangles
            int steps = 20;
            for (int i = 0; i < steps; i++)
            {
                float t1 = (float)(steps - i - 1) / steps;
                float t2 = (float)(steps - i) / steps;
                
                // Viridis colormap
                var c1 = GetViridisColor(t1);
                var c2 = GetViridisColor(t2);
                
                drawList.AddRectFilledMultiColor(
                    new Vector2(pos.X, pos.Y + i * height / steps),
                    new Vector2(pos.X + width, pos.Y + (i + 1) * height / steps),
                    ImGui.GetColorU32(c1), ImGui.GetColorU32(c1),
                    ImGui.GetColorU32(c2), ImGui.GetColorU32(c2));
            }

            // Draw labels
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y));
            ImGui.Text($"{maxVal:F2}{unit}");
            
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - ImGui.GetTextLineHeight()));
            ImGui.Text($"{minVal:F2}{unit}");
        }
        private Pore FindPoreById(int id)
        {
            if (id < 0) return null;
            // Pores list may be rebuilt/reordered; always resolve by ID at draw time
            for (int i = 0; i < _dataset.Pores.Count; i++)
            {
                if (_dataset.Pores[i].ID == id) return _dataset.Pores[i];
            }
            return null;
        }

        private Vector4 GetViridisColor(float t)
        {
            float r = Math.Clamp(0.267f + 0.004780f * t * 255 - 0.329f * t + 1.781f * t * t, 0.0f, 1.0f);
            float g = Math.Clamp(0.0f + 1.069f * t - 0.170f * t * t, 0.0f, 1.0f);
            float b = Math.Clamp(0.329f + 1.515f * t - 1.965f * t * t + 0.621f * t * t * t, 0.0f, 1.0f);
            return new Vector4(r, g, b, 1.0f);
        }

        private void DrawStatisticsContent()
        {
            if (_dataset == null) return;
    
            ImGui.Columns(2);
    
            // Left column - Network info
            ImGui.Text("Network Statistics");
            ImGui.Separator();
            ImGui.Text($"Pores: {_dataset.Pores.Count:N0}");
            ImGui.Text($"Throats: {_dataset.Throats.Count:N0}");
            ImGui.Text($"Voxel Size: {_dataset.VoxelSize:F2} µm");
            ImGui.Text($"Tortuosity: {_dataset.Tortuosity:F3}");
    
            ImGui.NextColumn();
    
            // Right column - Permeability
            ImGui.Text("Permeability (mD):");
            ImGui.Text($"  Uncorrected: {_dataset.DarcyPermeability:F2}");
            if (_dataset.Tortuosity > 0)
            {
                // Fix: τ²-corrected permeability is K/τ²
                float corrected = _dataset.DarcyPermeability / (_dataset.Tortuosity * _dataset.Tortuosity);
                ImGui.Text($"  τ² Corrected: {corrected:F2}");
            }
            ImGui.Text($"  NS: {_dataset.NavierStokesPermeability:F2}");
            ImGui.Text($"  LBM: {_dataset.LatticeBoltzmannPermeability:F2}");
    
            ImGui.Columns(1);
        }


        private void DrawSelectedPoreContent()
        {
            // Resolve again to be extra-safe if lists changed mid-frame
            if (_selectedPoreId >= 0)
                _selectedPore = FindPoreById(_selectedPoreId);

            if (_selectedPore == null)
            {
                ImGui.TextDisabled("No pore selected.");
                if (ImGui.Button("Close"))
                {
                    _selectedPoreId = -1;
                }
                return;
            }

            ImGui.Text($"Selected Pore #{_selectedPore.ID}");
            ImGui.Separator();

            ImGui.Text("Position:");
            ImGui.Text($"  X: {_selectedPore.Position.X:F2}");
            ImGui.Text($"  Y: {_selectedPore.Position.Y:F2}");
            ImGui.Text($"  Z: {_selectedPore.Position.Z:F2}");

            ImGui.Text($"Radius: {_selectedPore.Radius:F3} vox ({_selectedPore.Radius * _dataset.VoxelSize:F2} µm)");
            ImGui.Text($"Volume: {_selectedPore.VolumeVoxels:F0} vox³");
            ImGui.Text($"        ({_selectedPore.VolumePhysical:F2} µm³)");
            ImGui.Text($"Surface Area: {_selectedPore.Area:F1} vox²");
            ImGui.Text($"Connections: {_selectedPore.Connections}");

            if (ImGui.Button("Deselect"))
            {
                _selectedPoreId = -1;
                _selectedPore = null;
            }
        }

        private void SelectPoreAtPosition(Vector2 mousePos, Vector2 viewSize)
{
    if (_dataset == null || _dataset.Pores == null || _dataset.Pores.Count == 0)
    {
        _selectedPoreId = -1;
        _selectedPore = null;
        return;
    }

    // Build ViewProjection for projection to clip space
    var viewProj = _viewMatrix * _projMatrix;

    // Helper: project a world point to pixel coords within the image rect; returns false if behind camera
    bool WorldToScreen(in Vector3 world, out Vector2 pixel)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-6f)
        {
            pixel = default;
            return false; // behind camera or invalid
        }
        float invW = 1f / clip.W;
        float ndcX = clip.X * invW;
        float ndcY = clip.Y * invW;

        // Convert NDC (-1..1) to pixel coords (0..viewSize)
        float u = (ndcX * 0.5f) + 0.5f;
        float v = 1.0f - ((ndcY * 0.5f) + 0.5f); // flip Y to match ImGui.Image UVs
        pixel = new Vector2(u * viewSize.X, v * viewSize.Y);
        return (u >= -0.2f && u <= 1.2f && v >= -0.2f && v <= 1.2f); // allow slight margin
    }

    // Iterate all pores: pick the one with the smallest screen-space distance to mouse,
    // within a dynamic threshold based on the projected radius (so small/remote pores remain pickable).
    int bestId = -1;
    Pore bestPore = null;
    float bestDist2 = float.MaxValue;

    // Minimum and maximum selection radius in pixels
    const float minPickPx = 8f;   // generous base for tiny or far pores
    const float maxPickPx = 28f;  // cap for very large/near pores

    for (int i = 0; i < _dataset.Pores.Count; i++)
    {
        var pore = _dataset.Pores[i];

        // Project center
        if (!WorldToScreen(pore.Position, out var centerPx))
            continue;

        // Estimate projected radius: project an offset along +X in world by the rendered sphere radius
        float rModel = pore.Radius * _poreSizeMultiplier * 0.1f;
        if (rModel <= 0) rModel = 0.1f;

        Vector2 edgePx;
        if (!WorldToScreen(pore.Position + new Vector3(rModel, 0, 0), out edgePx))
            continue;

        float projectedRadiusPx = Vector2.Distance(centerPx, edgePx);
        // Dynamic pick radius: slightly larger than the projected disk; clamped to reasonable bounds
        float pickRadiusPx = Math.Clamp(projectedRadiusPx * 1.15f, minPickPx, maxPickPx);

        // Distance from mouse to projected center
        float dx = mousePos.X - centerPx.X;
        float dy = mousePos.Y - centerPx.Y;
        float dist2 = dx * dx + dy * dy;

        if (dist2 <= pickRadiusPx * pickRadiusPx && dist2 < bestDist2)
        {
            bestDist2 = dist2;
            bestId = pore.ID;
            bestPore = pore;
        }
    }

    _selectedPoreId = bestId;
    _selectedPore   = bestPore;

    if (_selectedPore != null)
    {
        Logger.Log($"[PNMViewer] Selected pore #{_selectedPore.ID} @ ({_selectedPore.Position.X:F2}, {_selectedPore.Position.Y:F2}, {_selectedPore.Position.Z:F2})");
    }
}


        private void RebuildGeometryFromDataset()
        {
            var factory = VeldridManager.Factory;

            // Build pore instances
            var poreInstances = new List<PoreInstanceData>();
            foreach (var p in _dataset.Pores)
            {
                float colorValue = GetPoreColorValue(p);
                poreInstances.Add(new PoreInstanceData(p.Position, colorValue, p.Radius));
            }
            _poreInstanceCount = poreInstances.Count;

            // Create or update instance buffer
            _poreInstanceBuffer?.Dispose();
            if (_poreInstanceCount > 0)
            {
                uint instanceSize = (uint)Marshal.SizeOf<PoreInstanceData>();
                _poreInstanceBuffer = factory.CreateBuffer(new BufferDescription(instanceSize * (uint)_poreInstanceCount, BufferUsage.VertexBuffer));
                VeldridManager.GraphicsDevice.UpdateBuffer(_poreInstanceBuffer, 0, poreInstances.ToArray());
            }
            else
            {
                _poreInstanceBuffer = factory.CreateBuffer(new BufferDescription(20, BufferUsage.VertexBuffer)); // Minimal buffer
            }

            // Build throat vertices
            var poreById = _dataset.Pores.ToDictionary(p => p.ID, p => p);
            var throatVertices = new List<ThroatVertexData>();
            
            foreach (var t in _dataset.Throats)
            {
                if (poreById.TryGetValue(t.Pore1ID, out var p1) && poreById.TryGetValue(t.Pore2ID, out var p2))
                {
                    throatVertices.Add(new ThroatVertexData(p1.Position, t.Radius));
                    throatVertices.Add(new ThroatVertexData(p2.Position, t.Radius));
                }
            }
            _throatVertexCount = (uint)throatVertices.Count;

            // Create or update throat buffer
            _throatVertexBuffer?.Dispose();
            if (_throatVertexCount > 0)
            {
                uint vertSize = (uint)Marshal.SizeOf<ThroatVertexData>();
                _throatVertexBuffer = factory.CreateBuffer(new BufferDescription(vertSize * _throatVertexCount, BufferUsage.VertexBuffer));
                VeldridManager.GraphicsDevice.UpdateBuffer(_throatVertexBuffer, 0, throatVertices.ToArray());
            }
            else
            {
                _throatVertexBuffer = factory.CreateBuffer(new BufferDescription(16, BufferUsage.VertexBuffer));
            }

            UpdateConstantBuffers();
        }

        private float GetPoreColorValue(Pore p)
        {
            return _colorByIndex switch
            {
                0 => p.Radius, // Pore Radius
                1 => p.Connections, // Connections
                2 => p.VolumePhysical, // Volume (physical)
                _ => p.Radius
            };
        }

        private void Render()
        {
            UpdateConstantBuffers();

            _commandList.Begin();
            _commandList.SetFramebuffer(_framebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.05f, 0.05f, 0.07f, 1.0f));
            _commandList.ClearDepthStencil(1f);

            if (_showThroats && _throatVertexCount > 0)
            {
                _commandList.SetPipeline(_throatPipeline);
                _commandList.SetGraphicsResourceSet(0, _throatResourceSet);
                _commandList.SetVertexBuffer(0, _throatVertexBuffer);
                _commandList.Draw(_throatVertexCount);
            }

            if (_showPores && _poreInstanceCount > 0)
            {
                var indexCount = _poreIndexBuffer.SizeInBytes / sizeof(ushort);
                _commandList.SetPipeline(_porePipeline);
                _commandList.SetGraphicsResourceSet(0, _poreResourceSet);
                _commandList.SetVertexBuffer(0, _poreVertexBuffer);
                _commandList.SetVertexBuffer(1, _poreInstanceBuffer);
                _commandList.SetIndexBuffer(_poreIndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed((uint)indexCount, (uint)_poreInstanceCount, 0, 0, 0);
            }

            _commandList.End();
            VeldridManager.GraphicsDevice.SubmitCommands(_commandList);
            VeldridManager.GraphicsDevice.WaitForIdle();
        }

        private void UpdateConstantBuffers()
        {
            float minVal = 0, maxVal = 1;
            switch (_colorByIndex)
            {
                case 0: // Pore Radius
                    minVal = _dataset.MinPoreRadius;
                    maxVal = _dataset.MaxPoreRadius;
                    break;
                case 1: // Connections
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.Connections) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.Connections) : 1;
                    break;
                case 2: // Volume
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.VolumePhysical) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.VolumePhysical) : 1;
                    break;
            }
            if (Math.Abs(maxVal - minVal) < 0.001f) maxVal = minVal + 1.0f;

            var constants = new Constants
            {
                ViewProjection = _viewMatrix * _projMatrix,
                CameraPosition = new Vector4(_cameraPosition, 1),
                ColorRampInfo = new Vector4(minVal, maxVal, 1.0f / (maxVal - minVal), 0),
                SizeInfo = new Vector4(_poreSizeMultiplier, 0, 0, 0)
            };

            if (_poreConstantsBuffer != null)
                VeldridManager.GraphicsDevice.UpdateBuffer(_poreConstantsBuffer, 0, ref constants);
            if (_throatConstantsBuffer != null)
                VeldridManager.GraphicsDevice.UpdateBuffer(_throatConstantsBuffer, 0, ref constants);
        }

        private void UpdatePoreInstanceDataColor()
        {
            if (_poreInstanceCount == 0) return;

            var instanceData = new List<PoreInstanceData>();
            foreach (var p in _dataset.Pores)
            {
                float colorValue = GetPoreColorValue(p);
                instanceData.Add(new PoreInstanceData(p.Position, colorValue, p.Radius));
            }
            
            if (instanceData.Count > 0 && _poreInstanceBuffer != null)
            {
                VeldridManager.GraphicsDevice.UpdateBuffer(_poreInstanceBuffer, 0, instanceData.ToArray());
            }
        }

        private void HandleMouseInput()
        {
            var io = ImGui.GetIO();
            
            // Zoom with mouse wheel
            if (io.MouseWheel != 0)
            {
                float zoomSpeed = 0.1f * _cameraDistance;
                _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * zoomSpeed / _cameraDistance), 
                    0.1f, _modelRadius * 10.0f);
                UpdateCameraMatrices();
            }

            // Check for rotation (left button with shift OR right button)
            bool wantRotate = (ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsKeyDown(ImGuiKey.LeftShift)) ||
                             ImGui.IsMouseDown(ImGuiMouseButton.Right);
            
            // Check for panning (middle button)
            bool wantPan = ImGui.IsMouseDown(ImGuiMouseButton.Middle);
            
            if (wantRotate)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _lastMousePos = io.MousePos;
                }
                
                var delta = io.MousePos - _lastMousePos;
                _cameraYaw -= delta.X * 0.01f;
                _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.01f, -MathF.PI / 2.01f, MathF.PI / 2.01f);
                _lastMousePos = io.MousePos;
                UpdateCameraMatrices();
            }
            else if (wantPan)
            {
                if (!_isPanning)
                {
                    _isPanning = true;
                    _lastMousePos = io.MousePos;
                }
                
                var delta = io.MousePos - _lastMousePos;
                Matrix4x4.Invert(_viewMatrix, out var invView);
                var right = Vector3.Normalize(new Vector3(invView.M11, invView.M12, invView.M13));
                var up = Vector3.Normalize(new Vector3(invView.M21, invView.M22, invView.M23));
                float panSpeed = _cameraDistance * 0.001f;
                _cameraTarget -= right * delta.X * panSpeed;
                _cameraTarget += up * delta.Y * panSpeed;
                _lastMousePos = io.MousePos;
                UpdateCameraMatrices();
            }
            else
            {
                _isDragging = false;
                _isPanning = false;
            }
        }

        private void UpdateCameraMatrices()
        {
            _cameraPosition = _cameraTarget + new Vector3(
                MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch),
                MathF.Sin(_cameraPitch),
                MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch)) * _cameraDistance;
            
            _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, Vector3.UnitY);
            
            float aspectRatio = _renderTexture.Width / (float)Math.Max(1, _renderTexture.Height);
            _projMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspectRatio, 0.01f, 1000f);
        }

        public void ResetCamera()
        {
            if (_dataset.Pores.Any())
            {
                var min = new Vector3(
                    _dataset.Pores.Min(p => p.Position.X),
                    _dataset.Pores.Min(p => p.Position.Y),
                    _dataset.Pores.Min(p => p.Position.Z));
                var max = new Vector3(
                    _dataset.Pores.Max(p => p.Position.X),
                    _dataset.Pores.Max(p => p.Position.Y),
                    _dataset.Pores.Max(p => p.Position.Z));
                _modelCenter = (min + max) / 2.0f;
                _modelRadius = Vector3.Distance(min, max) / 2.0f;
                _cameraDistance = _modelRadius * 2.5f;
                if (_cameraDistance < 0.1f) _cameraDistance = 5.0f;
            }
            else
            {
                _modelCenter = Vector3.Zero;
                _modelRadius = 10.0f;
                _cameraDistance = 25.0f;
            }

            _cameraTarget = _modelCenter;
            _cameraYaw = -MathF.PI / 4f;
            _cameraPitch = MathF.PI / 6f;
            UpdateCameraMatrices();
        }

        #endregion

        private void TakeAndSaveScreenshot(string path)
        {
            try
            {
                var gd = VeldridManager.GraphicsDevice;
                var factory = VeldridManager.Factory;

                // Create staging texture
                var stagingDesc = TextureDescription.Texture2D(
                    _renderTexture.Width, _renderTexture.Height, 1, 1,
                    _renderTexture.Format, TextureUsage.Staging);
                var stagingTexture = factory.CreateTexture(stagingDesc);

                // Copy render texture to staging
                var cl = factory.CreateCommandList();
                cl.Begin();
                cl.CopyTexture(_renderTexture, stagingTexture);
                cl.End();
                gd.SubmitCommands(cl);
                gd.WaitForIdle();

                // Read pixel data
                MappedResource mappedResource = gd.Map(stagingTexture, MapMode.Read, 0);
                var rawBytes = new byte[mappedResource.SizeInBytes];
                unsafe
                {
                    var sourceSpan = new ReadOnlySpan<byte>(mappedResource.Data.ToPointer(), (int)mappedResource.SizeInBytes);
                    sourceSpan.CopyTo(rawBytes);
                }
                gd.Unmap(stagingTexture, 0);

                // Write image file
                using var stream = new MemoryStream();
                var imageWriter = new ImageWriter();
                var extension = Path.GetExtension(path).ToLower();

                if (extension == ".png")
                {
                    imageWriter.WritePng(rawBytes, (int)_renderTexture.Width, (int)_renderTexture.Height, 
                        ColorComponents.RedGreenBlueAlpha, stream);
                }
                else if (extension == ".jpg" || extension == ".jpeg")
                {
                    imageWriter.WriteJpg(rawBytes, (int)_renderTexture.Width, (int)_renderTexture.Height, 
                        ColorComponents.RedGreenBlueAlpha, stream, 90);
                }
                
                File.WriteAllBytes(path, stream.ToArray());
                Logger.Log($"[Screenshot] Saved to {path}");
                
                stagingTexture.Dispose();
                cl.Dispose();
            }
            catch(Exception ex)
            {
                Logger.LogError($"[Screenshot] Failed to save: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _renderTextureManager?.Dispose();
            _renderTexture?.Dispose();
            _depthTexture?.Dispose();
            _framebuffer?.Dispose();
            _commandList?.Dispose();
            _colorRampTexture?.Dispose();

            _poreInstanceBuffer?.Dispose();
            _poreVertexBuffer?.Dispose();
            _poreIndexBuffer?.Dispose();
            _poreConstantsBuffer?.Dispose();
            _porePipeline?.Dispose();
            _poreResourceLayout?.Dispose();
            _poreResourceSet?.Dispose();

            _throatVertexBuffer?.Dispose();
            _throatConstantsBuffer?.Dispose();
            _throatPipeline?.Dispose();
            _throatResourceLayout?.Dispose();
            _throatResourceSet?.Dispose();
        }
    }

    // Extension helper
    internal static class Vector3Extensions
    {
        public static Vector3 Normalized(this Vector3 v)
        {
            return Vector3.Normalize(v);
        }
    }
}