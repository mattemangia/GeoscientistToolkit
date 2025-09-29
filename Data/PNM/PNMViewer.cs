// GeoscientistToolkit/UI/PNMViewer.cs - Fixed Version
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
using GeoscientistToolkit.Analysis.Pnm;
using ImGuiNET;
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
        private Vector2 _lastViewerScreenPos;
        private Vector2 _lastViewerSize;
        
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

        // UI & Rendering State - ENHANCED with new options
        private int _colorByIndex = 0;
        private readonly string[] _colorByOptions = { 
            "Pore Radius", 
            "Pore Connections", 
            "Pore Volume",
            "Pressure (Pores)",      // NEW: Shows pressure at each pore
            "Pressure Drop (Throats)", // NEW: Shows pressure gradient across throats  
            "Local Tortuosity"        // NEW: Shows path tortuosity
        };
        private float _poreSizeMultiplier = 1.0f;
        private bool _showPores = true;
        private bool _showThroats = true;

        // NEW: Store pressure/flow results for visualization
        private Dictionary<int, float> _porePressures = new Dictionary<int, float>();
        private Dictionary<int, float> _throatFlowRates = new Dictionary<int, float>();
        private Dictionary<int, float> _localTortuosity = new Dictionary<int, float>();
        private bool _hasFlowData = false;

        // Selection
        private int _selectedPoreId = -1;
        private Pore _selectedPore = null;
        
        // Screenshot functionality
        private readonly ImGuiExportFileDialog _screenshotDialog;
        private bool _showScreenshotNotification = false;
        private float _screenshotNotificationTimer = 0f;
        private string _lastScreenshotPath = "";
        
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
                    // Check if we have new flow data
                    UpdateFlowData();
                }
            };

            _screenshotDialog = new ImGuiExportFileDialog($"Screenshot_{dataset.Name}", "Save Screenshot");
            _screenshotDialog.SetExtensions(
                (".png", "PNG Image"),
                (".jpg", "JPEG Image")
            );

            _isMetal = VeldridManager.GraphicsDevice.BackendType == GraphicsBackend.Metal;
            
            InitializeVeldridResources();
            UpdateFlowData();
            RebuildGeometryFromDataset();
            ResetCamera();
        }

        // NEW: Update flow data from last permeability calculation
        private void UpdateFlowData()
        {
            var results = AbsolutePermeability.GetLastResults();
            var flowData = AbsolutePermeability.GetLastFlowData();
            
            if (flowData != null && flowData.PorePressures != null && flowData.PorePressures.Count > 0)
            {
                _porePressures = flowData.PorePressures;
                _throatFlowRates = flowData.ThroatFlowRates ?? new Dictionary<int, float>();
                _hasFlowData = true;
                
                // Calculate local tortuosity (simplified - based on connectivity patterns)
                CalculateLocalTortuosity();
                
                Logger.Log($"[PNMViewer] Loaded flow data with {_porePressures.Count} pore pressures");
            }
            else
            {
                _hasFlowData = false;
            }
        }

        // NEW: Calculate local tortuosity metric for visualization
        private void CalculateLocalTortuosity()
        {
            _localTortuosity.Clear();
            
            // Build adjacency map
            var adjacency = new Dictionary<int, List<int>>();
            foreach (var throat in _dataset.Throats)
            {
                if (!adjacency.ContainsKey(throat.Pore1ID))
                    adjacency[throat.Pore1ID] = new List<int>();
                if (!adjacency.ContainsKey(throat.Pore2ID))
                    adjacency[throat.Pore2ID] = new List<int>();
                    
                adjacency[throat.Pore1ID].Add(throat.Pore2ID);
                adjacency[throat.Pore2ID].Add(throat.Pore1ID);
            }
            
            // For each pore, calculate average path deviation
            foreach (var pore in _dataset.Pores)
            {
                if (!adjacency.ContainsKey(pore.ID))
                {
                    _localTortuosity[pore.ID] = 1.0f;
                    continue;
                }
                
                var neighbors = adjacency[pore.ID];
                if (neighbors.Count < 2)
                {
                    _localTortuosity[pore.ID] = 1.0f;
                    continue;
                }
                
                // Calculate angle variance between connections
                float totalDeviation = 0f;
                int comparisons = 0;
                
                for (int i = 0; i < neighbors.Count - 1; i++)
                {
                    var n1 = _dataset.Pores.FirstOrDefault(p => p.ID == neighbors[i]);
                    if (n1 == null) continue;
                    
                    for (int j = i + 1; j < neighbors.Count; j++)
                    {
                        var n2 = _dataset.Pores.FirstOrDefault(p => p.ID == neighbors[j]);
                        if (n2 == null) continue;
                        
                        // Calculate angle between the two connections
                        var v1 = Vector3.Normalize(n1.Position - pore.Position);
                        var v2 = Vector3.Normalize(n2.Position - pore.Position);
                        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(v1, v2), -1f, 1f));
                        
                        // Deviation from straight line (π radians)
                        float deviation = MathF.Abs(MathF.PI - angle) / MathF.PI;
                        totalDeviation += deviation;
                        comparisons++;
                    }
                }
                
                if (comparisons > 0)
                {
                    float avgDeviation = totalDeviation / comparisons;
                    // Convert to tortuosity-like metric (1.0 = straight, higher = more tortuous)
                    _localTortuosity[pore.ID] = 1.0f + avgDeviation * 2.0f;
                }
                else
                {
                    _localTortuosity[pore.ID] = 1.0f;
                }
            }
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
            var (vertices, indices) = CreateSphereGeometry();
            
            _poreVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreVertexBuffer, 0, vertices);
            _poreIndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreIndexBuffer, 0, indices);

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
    float radius = in_InstanceRadius * SizeInfo.x * 0.1;
    vec3 worldPos = in_InstancePosition + in_Position * radius;
    gl_Position = ViewProjection * vec4(worldPos, 1.0);
    
    out_WorldPos = worldPos;
    out_Normal = normalize(in_Position);
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
    
    float diffuse = max(dot(normal, lightDir), 0.0) * 0.7 + 0.3;
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
            CreateThroatResourcesGLSL(factory);
        }

        private (Vector3[] vertices, ushort[] indices) CreateSphereGeometry()
        {
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

            var indices = new List<ushort>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };

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
                
                // Different colormaps based on what we're visualizing
                if (_colorByIndex == 3 || _colorByIndex == 4) // Pressure-based
                {
                    // Blue-white-red for pressure (low to high)
                    float r = t;
                    float g = 1.0f - Math.Abs(2.0f * t - 1.0f);
                    float b = 1.0f - t;
                    colorMapData[i] = new RgbaFloat(r, g, b, 1.0f);
                }
                else if (_colorByIndex == 5) // Tortuosity
                {
                    // Green-yellow-red for tortuosity (low to high)
                    float r = Math.Min(1.0f, 2.0f * t);
                    float g = Math.Min(1.0f, 2.0f * (1.0f - t));
                    float b = 0.0f;
                    colorMapData[i] = new RgbaFloat(r, g, b, 1.0f);
                }
                else
                {
                    // Plasma colormap for better visibility on dark backgrounds
                    // This provides better contrast than Viridis
                    float r = Math.Clamp(0.05f + 2.0f * t, 0.0f, 1.0f);
                    float g = Math.Clamp(0.02f + 0.39f * t - 0.53f * t * t, 0.0f, 1.0f);  
                    float b = Math.Clamp(0.53f + 1.58f * t - 2.24f * t * t + 0.90f * t * t * t, 0.0f, 1.0f);
                    colorMapData[i] = new RgbaFloat(r, g, b, 1.0f);
                }
            }
            
            _colorRampTexture?.Dispose();
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
            
            // Filter options based on available data
            var availableOptions = new List<string>();
            var optionIndices = new List<int>();
            
            // Always available options
            availableOptions.Add("Pore Radius");
            optionIndices.Add(0);
            availableOptions.Add("Pore Connections");
            optionIndices.Add(1);
            availableOptions.Add("Pore Volume");
            optionIndices.Add(2);
            
            // Conditionally available options
            if (_hasFlowData)
            {
                availableOptions.Add("Pressure (Pores)");
                optionIndices.Add(3);
                availableOptions.Add("Pressure Drop (Throats)");
                optionIndices.Add(4);
                availableOptions.Add("Local Tortuosity");
                optionIndices.Add(5);
            }
            
            int localIndex = optionIndices.IndexOf(_colorByIndex);
            if (localIndex < 0) localIndex = 0;
            
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("##ColorBy", ref localIndex, availableOptions.ToArray(), availableOptions.Count))
            {
                _colorByIndex = optionIndices[localIndex];
                CreateColorRampTexture(VeldridManager.Factory); // Recreate colormap for new mode
                RebuildGeometryFromDataset(); // FIX: Rebuild geometry to update colors
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
                UpdateFlowData(); // Check for new flow data
                RebuildGeometryFromDataset();
                _pendingGeometryRebuild = false;
            }
    
            Render();
    
            var textureId = _renderTextureManager.GetImGuiTextureId();
            if (textureId == IntPtr.Zero) return;

            var availableSize = ImGui.GetContentRegionAvail();
            var imagePos = ImGui.GetCursorScreenPos();
            _lastViewerScreenPos = imagePos;
            _lastViewerSize = availableSize;
            ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));

            ImGui.SetCursorScreenPos(imagePos);
            ImGui.InvisibleButton("PNMViewInteraction", availableSize);

            bool isHovered = ImGui.IsItemHovered();
            if (isHovered)
            {
                HandleMouseInput();

                const float dragThresholdPx = 4.0f;
                bool leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                bool wasDragging  = ImGui.IsMouseDragging(ImGuiMouseButton.Left, dragThresholdPx);
                bool shiftHeld    = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);

                if (leftReleased && !wasDragging && !shiftHeld)
                {
                    var mousePos = ImGui.GetMousePos() - imagePos;
                    SelectPoreAtPosition(mousePos, availableSize);
                }
            }

            DrawOverlayWindows(imagePos, availableSize);
            
            // Draw screenshot notification if active
            if (_showScreenshotNotification && _screenshotNotificationTimer > 0)
            {
                DrawScreenshotNotification(imagePos, availableSize);
                _screenshotNotificationTimer -= ImGui.GetIO().DeltaTime;
                if (_screenshotNotificationTimer <= 0)
                {
                    _showScreenshotNotification = false;
                }
            }
        }

        private void DrawOverlayWindows(Vector2 viewPos, Vector2 viewSize)
        {
            // Legend Window
            ImGui.SetNextWindowPos(new Vector2(viewPos.X + viewSize.X - 200, viewPos.Y + 10), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(180, 280), ImGuiCond.Always);
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

            // Selected Pore Window
            if (_selectedPoreId >= 0)
            {
                _selectedPore = FindPoreById(_selectedPoreId);
                if (_selectedPore == null)
                {
                    _selectedPoreId = -1;
                    return;
                }

                ImGui.SetNextWindowPos(new Vector2(viewPos.X + 10, viewPos.Y + 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(320, 250), ImGuiCond.Always);
                ImGui.SetNextWindowBgAlpha(0.85f);
                ImGui.SetNextWindowFocus();

                ImGui.Begin(_selectedWindowId,
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
                DrawSelectedPoreContent();
                ImGui.End();
            }
        }

        private void DrawLegendContent()
        {
            string title = _colorByIndex < _colorByOptions.Length ? _colorByOptions[_colorByIndex] : "Unknown";
            ImGui.Text(title);
            ImGui.Separator();

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
                case 3: // Pressure (Pores)
                    if (_porePressures.Any())
                    {
                        minVal = _porePressures.Values.Min();
                        maxVal = _porePressures.Values.Max();
                        unit = " Pa";
                    }
                    break;
                case 4: // Pressure Drop (Throats)
                    if (_throatFlowRates.Any())
                    {
                        // Calculate pressure drops across throats
                        float minDrop = float.MaxValue, maxDrop = 0;
                        foreach (var throat in _dataset.Throats)
                        {
                            if (_porePressures.TryGetValue(throat.Pore1ID, out float p1) &&
                                _porePressures.TryGetValue(throat.Pore2ID, out float p2))
                            {
                                float drop = Math.Abs(p1 - p2);
                                minDrop = Math.Min(minDrop, drop);
                                maxDrop = Math.Max(maxDrop, drop);
                            }
                        }
                        minVal = minDrop != float.MaxValue ? minDrop : 0;
                        maxVal = maxDrop;
                        unit = " Pa";
                    }
                    break;
                case 5: // Local Tortuosity
                    if (_localTortuosity.Any())
                    {
                        minVal = _localTortuosity.Values.Min();
                        maxVal = _localTortuosity.Values.Max();
                        unit = "";
                    }
                    break;
            }

            // Draw gradient
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            float width = 30;
            float height = 180;
            
            int steps = 20;
            for (int i = 0; i < steps; i++)
            {
                float t1 = (float)(steps - i - 1) / steps;
                float t2 = (float)(steps - i) / steps;
                
                var c1 = GetColorForMode(t1);
                var c2 = GetColorForMode(t2);
                
                drawList.AddRectFilledMultiColor(
                    new Vector2(pos.X, pos.Y + i * height / steps),
                    new Vector2(pos.X + width, pos.Y + (i + 1) * height / steps),
                    ImGui.GetColorU32(c1), ImGui.GetColorU32(c1),
                    ImGui.GetColorU32(c2), ImGui.GetColorU32(c2));
            }

            // Draw labels
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y));
            ImGui.Text($"{maxVal:F2}{unit}");
            
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height / 2 - ImGui.GetTextLineHeight()/2));
            ImGui.Text($"{(minVal + maxVal)/2:F2}{unit}");
            
            ImGui.SetCursorScreenPos(new Vector2(pos.X + width + 5, pos.Y + height - ImGui.GetTextLineHeight()));
            ImGui.Text($"{minVal:F2}{unit}");

            // Add info for flow modes
            if (_colorByIndex >= 3 && _colorByIndex <= 5 && !_hasFlowData)
            {
                ImGui.Spacing();
                ImGui.TextWrapped("Run permeability calculation to enable flow visualization");
            }
        }

        private Vector4 GetColorForMode(float t)
        {
            if (_colorByIndex == 3 || _colorByIndex == 4) // Pressure
            {
                float r = t;
                float g = 1.0f - Math.Abs(2.0f * t - 1.0f);
                float b = 1.0f - t;
                return new Vector4(r, g, b, 1.0f);
            }
            else if (_colorByIndex == 5) // Tortuosity
            {
                float r = Math.Min(1.0f, 2.0f * t);
                float g = Math.Min(1.0f, 2.0f * (1.0f - t));
                float b = 0.0f;
                return new Vector4(r, g, b, 1.0f);
            }
            else // Plasma for better visibility
            {
                float r = Math.Clamp(0.05f + 2.0f * t, 0.0f, 1.0f);
                float g = Math.Clamp(0.02f + 0.39f * t - 0.53f * t * t, 0.0f, 1.0f);  
                float b = Math.Clamp(0.53f + 1.58f * t - 2.24f * t * t + 0.90f * t * t * t, 0.0f, 1.0f);
                return new Vector4(r, g, b, 1.0f);
            }
        }

        private void DrawStatisticsContent()
        {
            if (_dataset == null) return;
    
            ImGui.Columns(2);
    
            ImGui.Text("Network Statistics");
            ImGui.Separator();
            ImGui.Text($"Pores: {_dataset.Pores.Count:N0}");
            ImGui.Text($"Throats: {_dataset.Throats.Count:N0}");
            ImGui.Text($"Voxel Size: {_dataset.VoxelSize:F2} µm");
            ImGui.Text($"Tortuosity: {_dataset.Tortuosity:F3}");
    
            ImGui.NextColumn();
    
            ImGui.Text("Permeability (mD):");
            ImGui.Text($"  Uncorrected: {_dataset.DarcyPermeability:F2}");
            if (_dataset.Tortuosity > 0)
            {
                float corrected = _dataset.DarcyPermeability / (_dataset.Tortuosity * _dataset.Tortuosity);
                ImGui.Text($"  τ² Corrected: {corrected:F2}");
            }
            ImGui.Text($"  NS: {_dataset.NavierStokesPermeability:F2}");
            ImGui.Text($"  LBM: {_dataset.LatticeBoltzmannPermeability:F2}");
    
            ImGui.Columns(1);
        }

        private void DrawSelectedPoreContent()
        {
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

            // Add flow information if available
            if (_hasFlowData && _porePressures.TryGetValue(_selectedPore.ID, out float pressure))
            {
                ImGui.Separator();
                ImGui.Text("Flow Properties:");
                ImGui.Text($"Pressure: {pressure:F3} Pa");
                
                if (_localTortuosity.TryGetValue(_selectedPore.ID, out float tort))
                {
                    ImGui.Text($"Local Tortuosity: {tort:F3}");
                }
            }

            if (ImGui.Button("Deselect"))
            {
                _selectedPoreId = -1;
                _selectedPore = null;
            }
        }

        private Pore FindPoreById(int id)
        {
            if (id < 0) return null;
            for (int i = 0; i < _dataset.Pores.Count; i++)
            {
                if (_dataset.Pores[i].ID == id) return _dataset.Pores[i];
            }
            return null;
        }

        private void SelectPoreAtPosition(Vector2 mousePos, Vector2 viewSize)
        {
            if (_dataset == null || _dataset.Pores == null || _dataset.Pores.Count == 0)
            {
                _selectedPoreId = -1;
                _selectedPore = null;
                return;
            }

            // FIX: Use the correct viewport dimensions for the render target
            float aspectRatio = _renderTexture.Width / (float)_renderTexture.Height;
            float viewportAspect = viewSize.X / viewSize.Y;
            
            // Adjust mouse position to account for aspect ratio differences
            Vector2 adjustedMousePos = mousePos;
            if (Math.Abs(aspectRatio - viewportAspect) > 0.01f)
            {
                // The image might be letterboxed or pillarboxed
                if (viewportAspect > aspectRatio)
                {
                    // Pillarboxed (black bars on sides)
                    float imageWidth = viewSize.Y * aspectRatio;
                    float offset = (viewSize.X - imageWidth) * 0.5f;
                    adjustedMousePos.X = (mousePos.X - offset) * (_renderTexture.Width / imageWidth);
                    adjustedMousePos.Y = mousePos.Y * (_renderTexture.Height / viewSize.Y);
                }
                else
                {
                    // Letterboxed (black bars on top/bottom)
                    float imageHeight = viewSize.X / aspectRatio;
                    float offset = (viewSize.Y - imageHeight) * 0.5f;
                    adjustedMousePos.X = mousePos.X * (_renderTexture.Width / viewSize.X);
                    adjustedMousePos.Y = (mousePos.Y - offset) * (_renderTexture.Height / imageHeight);
                }
            }
            else
            {
                // Direct mapping
                adjustedMousePos.X = mousePos.X * (_renderTexture.Width / viewSize.X);
                adjustedMousePos.Y = mousePos.Y * (_renderTexture.Height / viewSize.Y);
            }

            var viewProj = _viewMatrix * _projMatrix;

            bool WorldToScreen(in Vector3 world, out Vector2 pixel)
            {
                var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
                if (clip.W <= 1e-6f)
                {
                    pixel = default;
                    return false;
                }
                float invW = 1f / clip.W;
                float ndcX = clip.X * invW;
                float ndcY = clip.Y * invW;

                // Convert from NDC to render target coordinates
                float u = (ndcX * 0.5f) + 0.5f;
                float v = 1.0f - ((ndcY * 0.5f) + 0.5f);
                pixel = new Vector2(u * _renderTexture.Width, v * _renderTexture.Height);
                
                return (u >= -0.1f && u <= 1.1f && v >= -0.1f && v <= 1.1f);
            }

            int bestId = -1;
            Pore bestPore = null;
            float bestDist2 = float.MaxValue;

            const float minPickPx = 5f;
            const float maxPickPx = 25f;

            for (int i = 0; i < _dataset.Pores.Count; i++)
            {
                var pore = _dataset.Pores[i];

                if (!WorldToScreen(pore.Position, out var centerPx))
                    continue;

                float rModel = pore.Radius * _poreSizeMultiplier * 0.1f;
                if (rModel <= 0) rModel = 0.1f;

                Vector2 edgePx;
                if (!WorldToScreen(pore.Position + _cameraPosition.Normalized() * rModel, out edgePx))
                {
                    // Try right vector instead
                    var right = Vector3.Cross(Vector3.UnitY, (_cameraPosition - _cameraTarget).Normalized());
                    if (!WorldToScreen(pore.Position + right * rModel, out edgePx))
                        continue;
                }

                float projectedRadiusPx = Vector2.Distance(centerPx, edgePx);
                float pickRadiusPx = Math.Clamp(projectedRadiusPx * 1.2f, minPickPx, maxPickPx);

                float dx = adjustedMousePos.X - centerPx.X;
                float dy = adjustedMousePos.Y - centerPx.Y;
                float dist2 = dx * dx + dy * dy;

                if (dist2 <= pickRadiusPx * pickRadiusPx && dist2 < bestDist2)
                {
                    bestDist2 = dist2;
                    bestId = pore.ID;
                    bestPore = pore;
                }
            }

            _selectedPoreId = bestId;
            _selectedPore = bestPore;

            if (_selectedPore != null)
            {
                Logger.Log($"[PNMViewer] Selected pore #{_selectedPore.ID}");
            }
        }

        private void RebuildGeometryFromDataset()
        {
            var factory = VeldridManager.Factory;

            // Build pore instances with updated color values
            var poreInstances = new List<PoreInstanceData>();
            foreach (var p in _dataset.Pores)
            {
                float colorValue = GetPoreColorValue(p);
                poreInstances.Add(new PoreInstanceData(p.Position, colorValue, p.Radius));
            }
            _poreInstanceCount = poreInstances.Count;

            _poreInstanceBuffer?.Dispose();
            if (_poreInstanceCount > 0)
            {
                uint instanceSize = (uint)Marshal.SizeOf<PoreInstanceData>();
                _poreInstanceBuffer = factory.CreateBuffer(new BufferDescription(instanceSize * (uint)_poreInstanceCount, BufferUsage.VertexBuffer));
                VeldridManager.GraphicsDevice.UpdateBuffer(_poreInstanceBuffer, 0, poreInstances.ToArray());
            }
            else
            {
                _poreInstanceBuffer = factory.CreateBuffer(new BufferDescription(20, BufferUsage.VertexBuffer));
            }

            // Build throat vertices with appropriate color values
            var poreById = _dataset.Pores.ToDictionary(p => p.ID, p => p);
            var throatVertices = new List<ThroatVertexData>();
            
            foreach (var t in _dataset.Throats)
            {
                if (poreById.TryGetValue(t.Pore1ID, out var p1) && poreById.TryGetValue(t.Pore2ID, out var p2))
                {
                    float colorValue = GetThroatColorValue(t, p1, p2);
                    throatVertices.Add(new ThroatVertexData(p1.Position, colorValue));
                    throatVertices.Add(new ThroatVertexData(p2.Position, colorValue));
                }
            }
            _throatVertexCount = (uint)throatVertices.Count;

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
            switch (_colorByIndex)
            {
                case 0: return p.Radius;
                case 1: return p.Connections;
                case 2: return p.VolumePhysical;
                case 3: // Pressure at pore
                    if (_porePressures.TryGetValue(p.ID, out float pressure))
                        return pressure;
                    return 0;
                case 5: // Local tortuosity
                    if (_localTortuosity.TryGetValue(p.ID, out float tort))
                        return tort;
                    return 1.0f;
                default: return p.Radius;
            }
        }

        private float GetThroatColorValue(Throat t, Pore p1, Pore p2)
        {
            switch (_colorByIndex)
            {
                case 4: // Pressure drop across throat
                    if (_porePressures.TryGetValue(t.Pore1ID, out float pr1) &&
                        _porePressures.TryGetValue(t.Pore2ID, out float pr2))
                    {
                        return Math.Abs(pr1 - pr2);
                    }
                    return 0;
                case 5: // Average tortuosity of connected pores
                    float tort1 = _localTortuosity.TryGetValue(p1.ID, out float t1) ? t1 : 1.0f;
                    float tort2 = _localTortuosity.TryGetValue(p2.ID, out float t2) ? t2 : 1.0f;
                    return (tort1 + tort2) / 2.0f;
                default:
                    return t.Radius; // Default to throat radius
            }
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
                case 3: // Pressure (Pores)
                    if (_porePressures.Any())
                    {
                        minVal = _porePressures.Values.Min();
                        maxVal = _porePressures.Values.Max();
                    }
                    break;
                case 4: // Pressure Drop (Throats)
                    if (_porePressures.Any())
                    {
                        float minDrop = float.MaxValue, maxDrop = 0;
                        foreach (var throat in _dataset.Throats)
                        {
                            if (_porePressures.TryGetValue(throat.Pore1ID, out float p1) &&
                                _porePressures.TryGetValue(throat.Pore2ID, out float p2))
                            {
                                float drop = Math.Abs(p1 - p2);
                                if (drop < minDrop) minDrop = drop;
                                if (drop > maxDrop) maxDrop = drop;
                            }
                        }
                        minVal = minDrop != float.MaxValue ? minDrop : 0;
                        maxVal = maxDrop;
                    }
                    break;
                case 5: // Local Tortuosity
                    if (_localTortuosity.Any())
                    {
                        minVal = _localTortuosity.Values.Min();
                        maxVal = _localTortuosity.Values.Max();
                    }
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

        private void HandleMouseInput()
        {
            var io = ImGui.GetIO();
            
            if (io.MouseWheel != 0)
            {
                float zoomSpeed = 0.1f * _cameraDistance;
                _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * zoomSpeed / _cameraDistance), 
                    0.1f, _modelRadius * 10.0f);
                UpdateCameraMatrices();
            }

            bool wantRotate = (ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsKeyDown(ImGuiKey.LeftShift)) ||
                             ImGui.IsMouseDown(ImGuiMouseButton.Right);
            
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

        private void DrawScreenshotNotification(Vector2 viewPos, Vector2 viewSize)
        {
            // Display a temporary notification when screenshot is saved
            ImGui.SetNextWindowPos(new Vector2(viewPos.X + viewSize.X / 2, viewPos.Y + 50), ImGuiCond.Always, new Vector2(0.5f, 0));
            ImGui.SetNextWindowBgAlpha(0.9f * (_screenshotNotificationTimer / 3.0f)); // Fade out effect
            
            ImGui.Begin("##ScreenshotNotification", 
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing);
            
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "✓ Screenshot saved!");
            ImGui.Text(Path.GetFileName(_lastScreenshotPath));
            
            ImGui.End();
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
                var format = Path.GetExtension(path).ToLower() switch
                {
                    ".png" => ScreenshotUtility.ImageFormat.PNG,
                    ".jpg" or ".jpeg" => ScreenshotUtility.ImageFormat.JPEG,
                    ".bmp" => ScreenshotUtility.ImageFormat.BMP,
                    ".tga" => ScreenshotUtility.ImageFormat.TGA,
                    _ => ScreenshotUtility.ImageFormat.PNG
                };

                // Schedule deferred capture - will happen AFTER ImGui renders
                ViewerScreenshotUtility.ScheduleRegionCapture(
                    _lastViewerScreenPos,
                    _lastViewerSize,
                    path,
                    format,
                    (success, filePath) =>
                    {
                        if (success)
                        {
                            Logger.Log($"[PNMViewer] Screenshot with overlays saved to {filePath}");
                            _lastScreenshotPath = filePath;
                            _showScreenshotNotification = true;
                            _screenshotNotificationTimer = 3.0f;
                        }
                        else
                        {
                            Logger.LogError($"[PNMViewer] Failed to save screenshot");
                        }
                    });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMViewer] Screenshot error: {ex.Message}");
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

    internal static class Vector3Extensions
    {
        public static Vector3 Normalized(this Vector3 v)
        {
            return Vector3.Normalize(v);
        }
    }
}