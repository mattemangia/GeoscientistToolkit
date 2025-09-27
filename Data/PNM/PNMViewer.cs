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
            public Vector4 SizeInfo;      // x: PoreSizeMultiplier, y: ThroatSizeMultiplier, z: ThroatLineWidth, w: unused
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
        private readonly string[] _colorByOptions = { "Pore Radius", "Throat Radius", "Pore Connections", "Pore Volume" };
        private float _poreSizeMultiplier = 1.0f;
        private float _throatSizeMultiplier = 1.0f;
        private float _throatLineWidth = 2.0f;
        private bool _showPores = true;
        private bool _showThroats = true;

        // Selection
        private int _selectedPoreId = -1;
        private Pore _selectedPore = null;
        
        // Screenshot functionality
        private readonly ImGuiExportFileDialog _screenshotDialog;
        
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
            
            var depthFormat = _isMetal ? PixelFormat.D32_Float_S8_UInt
                : PixelFormat.D24_UNorm_S8_UInt;
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

            // Instance buffer will be created in RebuildGeometry
            
            // GLSL Shaders - Fixed for proper sphere rendering
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
    float radius = in_InstanceRadius * SizeInfo.x * 0.1; // Scale down for visibility
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

            // Metal shaders
            string metalVertexShader = @"
#include <metal_stdlib>
using namespace metal;

struct Constants {
    float4x4 ViewProjection;
    float4 CameraPosition;
    float4 ColorRampInfo;
    float4 SizeInfo;
};

struct VertexIn {
    float3 Position [[attribute(0)]];
    float3 InstancePosition [[attribute(1)]];
    float InstanceColorValue [[attribute(2)]];
    float InstanceRadius [[attribute(3)]];
};

struct VertexOut {
    float4 Position [[position]];
    float3 WorldPos;
    float3 Normal;
    float ColorValue;
};

vertex VertexOut pore_vertex_main(
    VertexIn in [[stage_in]],
    constant Constants& constants [[buffer(0)]]
) {
    VertexOut out;
    
    float radius = in.InstanceRadius * constants.SizeInfo.x * 0.1;
    float3 worldPos = in.InstancePosition + in.Position * radius;
    
    out.Position = constants.ViewProjection * float4(worldPos, 1.0);
    out.WorldPos = worldPos;
    out.Normal = normalize(in.Position);
    out.ColorValue = in.InstanceColorValue;
    
    return out;
}";

            string metalFragmentShader = @"
#include <metal_stdlib>
using namespace metal;

struct Constants {
    float4x4 ViewProjection;
    float4 CameraPosition;
    float4 ColorRampInfo;
    float4 SizeInfo;
};

struct FragmentIn {
    float4 Position [[position]];
    float3 WorldPos;
    float3 Normal;
    float ColorValue;
};

fragment float4 pore_fragment_main(
    FragmentIn in [[stage_in]],
    constant Constants& constants [[buffer(0)]],
    texture1d<float> colorRamp [[texture(0)]],
    sampler colorSampler [[sampler(0)]]
) {
    float3 normal = normalize(in.Normal);
    float3 lightDir = normalize(float3(1, 1, 1));
    float3 viewDir = normalize(constants.CameraPosition.xyz - in.WorldPos);
    
    float diffuse = max(dot(normal, lightDir), 0.0) * 0.7 + 0.3;
    float3 reflectDir = reflect(-lightDir, normal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32) * 0.5;
    
    float normalizedValue = saturate((in.ColorValue - constants.ColorRampInfo.x) * constants.ColorRampInfo.z);
    float3 color = colorRamp.sample(colorSampler, normalizedValue).rgb;
    
    return float4(color * diffuse + spec, 1.0);
}";

            // Continue with Metal pipeline setup...
            var vsBytes = Encoding.UTF8.GetBytes(metalVertexShader);
            var fsBytes = Encoding.UTF8.GetBytes(metalFragmentShader);
            
            var vertexShader = factory.CreateShader(new ShaderDescription(
                ShaderStages.Vertex, vsBytes, "pore_vertex_main"));
            var fragmentShader = factory.CreateShader(new ShaderDescription(
                ShaderStages.Fragment, fsBytes, "pore_fragment_main"));

            _poreConstantsBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Constants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            var sampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            _poreResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorRamp", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorSampler", ResourceKind.Sampler, ShaderStages.Fragment)
            ));

            _poreResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_poreResourceLayout, _poreConstantsBuffer, _colorRampTexture, sampler));

            var poreVertexLayouts = new[]
            {
                new VertexLayoutDescription(
                    new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
                ),
                new VertexLayoutDescription(
                    (uint)Marshal.SizeOf<PoreInstanceData>(), 1,
                    new VertexElementDescription("InstancePosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                    new VertexElementDescription("InstanceColorValue", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
                    new VertexElementDescription("InstanceRadius", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1)
                )
            };

            _porePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(poreVertexLayouts, new[] { vertexShader, fragmentShader }),
                new[] { _poreResourceLayout },
                _framebuffer.OutputDescription));
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
            
            var rasterizerState = RasterizerStateDescription.Default;
           
            
            _throatPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                rasterizerState,
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
            string metalThroatVertex = @"
#include <metal_stdlib>
using namespace metal;

struct Constants {
    float4x4 ViewProjection;
    float4 CameraPosition;
    float4 ColorRampInfo;
    float4 SizeInfo;
};

struct VertexIn {
    float3 Position [[attribute(0)]];
    float ColorValue [[attribute(1)]];
};

struct VertexOut {
    float4 Position [[position]];
    float ColorValue;
};

vertex VertexOut throat_vertex_main(
    VertexIn in [[stage_in]],
    constant Constants& constants [[buffer(0)]]
) {
    VertexOut out;
    out.Position = constants.ViewProjection * float4(in.Position, 1.0);
    out.ColorValue = in.ColorValue;
    return out;
}";

            string metalThroatFragment = @"
#include <metal_stdlib>
using namespace metal;

struct Constants {
    float4x4 ViewProjection;
    float4 CameraPosition;
    float4 ColorRampInfo;
    float4 SizeInfo;
};

struct FragmentIn {
    float4 Position [[position]];
    float ColorValue;
};

fragment float4 throat_fragment_main(
    FragmentIn in [[stage_in]],
    constant Constants& constants [[buffer(0)]],
    texture1d<float> colorRamp [[texture(0)]],
    sampler colorSampler [[sampler(0)]]
) {
    float normalizedValue = saturate((in.ColorValue - constants.ColorRampInfo.x) * constants.ColorRampInfo.z);
    float3 color = colorRamp.sample(colorSampler, normalizedValue).rgb;
    return float4(color, 1.0);
}";

            var vsBytes = Encoding.UTF8.GetBytes(metalThroatVertex);
            var fsBytes = Encoding.UTF8.GetBytes(metalThroatFragment);
            
            var vertexShader = factory.CreateShader(new ShaderDescription(
                ShaderStages.Vertex, vsBytes, "throat_vertex_main"));
            var fragmentShader = factory.CreateShader(new ShaderDescription(
                ShaderStages.Fragment, fsBytes, "throat_fragment_main"));

            _throatConstantsBuffer = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Constants>(), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            
            var sampler = factory.CreateSampler(new SamplerDescription(
                SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
                SamplerFilter.MinLinear_MagLinear_MipLinear, null, 0, 0, 0, 0, SamplerBorderColor.TransparentBlack));

            _throatResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorRamp", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("ColorSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
            
            _throatResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_throatResourceLayout, _throatConstantsBuffer, _colorRampTexture, sampler));
            
            _throatPipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                RasterizerStateDescription.Default,
                PrimitiveTopology.LineList,
                new ShaderSetDescription(new[] {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        new VertexElementDescription("ColorValue", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1)
                    )
                }, new[] { vertexShader, fragmentShader }),
                new[] { _throatResourceLayout },
                _framebuffer.OutputDescription));
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
                // Rainbow gradient
                float r = Math.Clamp(1.5f - Math.Abs(4.0f * t - 3.0f), 0.0f, 1.0f);
                float g = Math.Clamp(1.5f - Math.Abs(4.0f * t - 2.0f), 0.0f, 1.0f);
                float b = Math.Clamp(1.5f - Math.Abs(4.0f * t - 1.0f), 0.0f, 1.0f);
                colorMapData[i] = new RgbaFloat(r, g, b, 1.0f);
            }
            
            if (_isMetal)
            {
                _colorRampTexture = factory.CreateTexture(TextureDescription.Texture1D((uint)mapSize, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
                VeldridManager.GraphicsDevice.UpdateTexture(_colorRampTexture, colorMapData, 0, 0, 0, (uint)mapSize, 1, 1, 0, 0);
            }
            else
            {
                _colorRampTexture = factory.CreateTexture(TextureDescription.Texture2D((uint)mapSize, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
                VeldridManager.GraphicsDevice.UpdateTexture(_colorRampTexture, colorMapData, 0, 0, 0, (uint)mapSize, 1, 1, 0, 0);
            }
        }

        #endregion

        #region Drawing and Interaction

        public void DrawToolbarControls()
        {
            if (ImGui.Button("Reset Cam")) ResetCamera();
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
            ImGui.Text("Throat Size:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.SliderFloat("##ThroatSize", ref _throatSizeMultiplier, 0.1f, 5.0f))
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

            ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));
            
            ImGui.SetCursorScreenPos(imagePos);
            ImGui.InvisibleButton("PNMViewInteraction", availableSize);

            if (ImGui.IsItemHovered())
            {
                HandleMouseInput();
                
                // Handle selection with left click
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsKeyDown(ImGuiKey.LeftShift))
                {
                    var mousePos = ImGui.GetMousePos() - imagePos;
                    SelectPoreAtPosition(mousePos, availableSize);
                }
            }

            DrawLegend(imagePos, availableSize);
            DrawStatistics(imagePos, availableSize);
            
            // Draw selected pore info
            if (_selectedPore != null)
            {
                DrawSelectedPoreInfo(imagePos, availableSize);
            }
        }

        private void SelectPoreAtPosition(Vector2 mousePos, Vector2 viewSize)
        {
            // Convert mouse position to normalized device coordinates
            float ndcX = (mousePos.X / viewSize.X) * 2.0f - 1.0f;
            float ndcY = 1.0f - (mousePos.Y / viewSize.Y) * 2.0f;

            // Create ray from camera
            Matrix4x4.Invert(_projMatrix, out var invProj);
            Matrix4x4.Invert(_viewMatrix, out var invView);
            
            var nearPoint = Vector3.Transform(new Vector3(ndcX, ndcY, 0), invProj);
            nearPoint = Vector3.Transform(nearPoint, invView);
            
            var farPoint = Vector3.Transform(new Vector3(ndcX, ndcY, 1), invProj);
            farPoint = Vector3.Transform(farPoint, invView);
            
            var rayDir = Vector3.Normalize(farPoint - nearPoint);
            
            // Find closest pore to ray
            _selectedPore = null;
            _selectedPoreId = -1;
            float minDist = float.MaxValue;
            
            foreach (var pore in _dataset.Pores)
            {
                // Ray-sphere intersection test
                var toSphere = pore.Position - _cameraPosition;
                float t = Vector3.Dot(toSphere, rayDir);
                if (t < 0) continue;
                
                var closestPoint = _cameraPosition + rayDir * t;
                float dist = Vector3.Distance(closestPoint, pore.Position);
                
                float sphereRadius = pore.Radius * _poreSizeMultiplier * 0.1f;
                if (dist < sphereRadius && t < minDist)
                {
                    minDist = t;
                    _selectedPore = pore;
                    _selectedPoreId = pore.ID;
                }
            }
        }

        private void DrawSelectedPoreInfo(Vector2 viewPos, Vector2 viewSize)
        {
            if (_selectedPore == null) return;
            
            var padding = 10;
            var infoWidth = 300;
            var infoHeight = 180;
            var infoPos = new Vector2(viewPos.X + padding, viewPos.Y + padding);
            
            var drawList = ImGui.GetForegroundDrawList();
            
            // Background
            drawList.AddRectFilled(infoPos, new Vector2(infoPos.X + infoWidth, infoPos.Y + infoHeight),
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), 5);
            
            // Content
            var textPos = infoPos + new Vector2(10, 10);
            uint textColor = 0xFFFFFFFF;
            uint valueColor = 0xFF00FFFF;
            
            drawList.AddText(textPos, textColor, $"Selected Pore #{_selectedPore.ID}");
            textPos.Y += 20;
            
            drawList.AddText(textPos, textColor, "Position:");
            textPos.Y += 16;
            drawList.AddText(textPos + new Vector2(10, 0), valueColor, 
                $"X: {_selectedPore.Position.X:F2}, Y: {_selectedPore.Position.Y:F2}, Z: {_selectedPore.Position.Z:F2}");
            textPos.Y += 18;
            
            drawList.AddText(textPos, textColor, $"Radius: ");
            drawList.AddText(textPos + new Vector2(60, 0), valueColor, $"{_selectedPore.Radius:F3} vox ({_selectedPore.Radius * _dataset.VoxelSize:F2} µm)");
            textPos.Y += 18;
            
            drawList.AddText(textPos, textColor, $"Volume: ");
            drawList.AddText(textPos + new Vector2(60, 0), valueColor, $"{_selectedPore.VolumeVoxels:F0} vox³ ({_selectedPore.VolumePhysical:F2} µm³)");
            textPos.Y += 18;
            
            drawList.AddText(textPos, textColor, $"Surface Area: ");
            drawList.AddText(textPos + new Vector2(100, 0), valueColor, $"{_selectedPore.Area:F1} vox²");
            textPos.Y += 18;
            
            drawList.AddText(textPos, textColor, $"Connections: ");
            drawList.AddText(textPos + new Vector2(85, 0), valueColor, $"{_selectedPore.Connections}");
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
                1 => p.Radius, // For throat radius, still use pore radius (will be overridden by throat data)
                2 => p.Connections, // Connections
                3 => p.VolumeVoxels, // Volume
                _ => p.Radius
            };
        }

        private void Render()
        {
            UpdateConstantBuffers();

            _commandList.Begin();
            _commandList.SetFramebuffer(_framebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1.0f));
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
                case 1: // Throat Radius
                    minVal = _dataset.MinThroatRadius;
                    maxVal = _dataset.MaxThroatRadius;
                    break;
                case 2: // Connections
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.Connections) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.Connections) : 1;
                    break;
                case 3: // Volume
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.VolumeVoxels) : 0;
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.VolumeVoxels) : 1;
                    break;
            }
            if (Math.Abs(maxVal - minVal) < 0.001f) maxVal = minVal + 1.0f;

            var constants = new Constants
            {
                ViewProjection = _viewMatrix * _projMatrix,
                CameraPosition = new Vector4(_cameraPosition, 1),
                ColorRampInfo = new Vector4(minVal, maxVal, 1.0f / (maxVal - minVal), 0),
                SizeInfo = new Vector4(_poreSizeMultiplier, _throatSizeMultiplier, _throatLineWidth, 0)
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
            if (io.MouseWheel != 0)
            {
                // Fixed zoom limits - allow much more zoom out
                float zoomSpeed = 0.1f * _cameraDistance; // Scale zoom speed with distance
                _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * zoomSpeed / _cameraDistance), 
                    0.1f, _modelRadius * 10.0f); // Allow zooming out to 10x the model size
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsKeyDown(ImGuiKey.LeftShift) || 
                ImGui.IsMouseDown(ImGuiMouseButton.Right) || 
                ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                if (!_isDragging && !_isPanning)
                {
                    _isDragging = ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsKeyDown(ImGuiKey.LeftShift);
                    _isPanning = ImGui.IsMouseDown(ImGuiMouseButton.Middle) || ImGui.IsMouseDown(ImGuiMouseButton.Right);
                    _lastMousePos = io.MousePos;
                }
                
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
                    float panSpeed = _cameraDistance * 0.001f;
                    _cameraTarget -= right * delta.X * panSpeed;
                    _cameraTarget += up * delta.Y * panSpeed;
                }
                _lastMousePos = io.MousePos;
            }
            else
            {
                _isDragging = false;
                _isPanning = false;
            }
            UpdateCameraMatrices();
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
                _cameraDistance = _modelRadius * 2.5f; // Start further away
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

        #region UI Drawing Helpers

        private void DrawLegend(Vector2 viewPos, Vector2 viewSize)
        {
            var drawList = ImGui.GetForegroundDrawList();
            float legendWidth = 20;
            float legendHeight = 200;
            float padding = 10;

            Vector2 legendPos = new Vector2(
                viewPos.X + viewSize.X - legendWidth - padding,
                viewPos.Y + padding
            );
            
            // Draw gradient
            int numSteps = 20;
            float stepHeight = legendHeight / numSteps;
            for (int i = 0; i < numSteps; i++)
            {
                float t1 = i / (float)numSteps;
                float t2 = (i + 1) / (float)numSteps;

                float r1 = Math.Clamp(1.5f - Math.Abs(4.0f * t1 - 3.0f), 0.0f, 1.0f);
                float g1 = Math.Clamp(1.5f - Math.Abs(4.0f * t1 - 2.0f), 0.0f, 1.0f);
                float b1 = Math.Clamp(1.5f - Math.Abs(4.0f * t1 - 1.0f), 0.0f, 1.0f);

                float r2 = Math.Clamp(1.5f - Math.Abs(4.0f * t2 - 3.0f), 0.0f, 1.0f);
                float g2 = Math.Clamp(1.5f - Math.Abs(4.0f * t2 - 2.0f), 0.0f, 1.0f);
                float b2 = Math.Clamp(1.5f - Math.Abs(4.0f * t2 - 1.0f), 0.0f, 1.0f);
                
                var c1 = ImGui.GetColorU32(new Vector4(r1, g1, b1, 1));
                var c2 = ImGui.GetColorU32(new Vector4(r2, g2, b2, 1));

                drawList.AddRectFilledMultiColor(
                    new Vector2(legendPos.X, legendPos.Y + i * stepHeight),
                    new Vector2(legendPos.X + legendWidth, legendPos.Y + (i + 1) * stepHeight),
                    c2, c2, c1, c1);
            }

            // Labels
            float minVal = 0, maxVal = 1;
            string unit = "";
            switch (_colorByIndex)
            {
                case 0: 
                    minVal = _dataset.MinPoreRadius * _dataset.VoxelSize; 
                    maxVal = _dataset.MaxPoreRadius * _dataset.VoxelSize; 
                    unit = " µm"; 
                    break;
                case 1: 
                    minVal = _dataset.MinThroatRadius * _dataset.VoxelSize; 
                    maxVal = _dataset.MaxThroatRadius * _dataset.VoxelSize; 
                    unit = " µm"; 
                    break;
                case 2: 
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.Connections) : 0; 
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.Connections) : 1; 
                    unit = ""; 
                    break;
                case 3: 
                    minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.VolumeVoxels) : 0; 
                    maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.VolumeVoxels) : 1; 
                    unit = " vox³"; 
                    break;
            }

            string title = _colorByOptions[_colorByIndex];
            string maxLabel = $"{maxVal:F2}{unit}";
            string minLabel = $"{minVal:F2}{unit}";

            drawList.AddText(new Vector2(legendPos.X - ImGui.CalcTextSize(title).X - 5, legendPos.Y), 0xFFFFFFFF, title);
            drawList.AddText(new Vector2(legendPos.X + legendWidth + 5, legendPos.Y), 0xFFFFFFFF, maxLabel);
            drawList.AddText(new Vector2(legendPos.X + legendWidth + 5, legendPos.Y + legendHeight - ImGui.CalcTextSize(minLabel).Y), 0xFFFFFFFF, minLabel);
        }

        private void DrawStatistics(Vector2 viewPos, Vector2 viewSize)
        {
            var padding = 10;
            var statsPos = new Vector2(viewPos.X + padding, viewPos.Y + viewSize.Y - 140);
            var drawList = ImGui.GetForegroundDrawList();
            
            drawList.AddRectFilled(statsPos, new Vector2(statsPos.X + 380, statsPos.Y + 130), 
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)), 5);
            
            var textPos = statsPos + new Vector2(10, 10);
            uint textColor = 0xFFFFFFFF;
            
            drawList.AddText(textPos, textColor, "Network Statistics:");
            textPos.Y += 20;
            drawList.AddText(textPos, textColor, $"Pores: {_dataset.Pores.Count:N0}  |  Throats: {_dataset.Throats.Count:N0}");
            textPos.Y += 18;
            drawList.AddText(textPos, textColor, $"Tortuosity: {_dataset.Tortuosity:F3}");
            textPos.Y += 18;
            drawList.AddText(textPos, textColor, "Permeability (mD):");
            textPos.Y += 16;
            drawList.AddText(textPos, textColor, $"  - Darcy: {_dataset.DarcyPermeability:F2}");
            textPos.Y += 16;
            drawList.AddText(textPos, textColor, $"  - Navier-Stokes: {_dataset.NavierStokesPermeability:F2}");
            textPos.Y += 16;
            drawList.AddText(textPos, textColor, $"  - Lattice-Boltzmann: {_dataset.LatticeBoltzmannPermeability:F2}");
        }

        #endregion

        private void TakeAndSaveScreenshot(string path)
        {
            // Implementation remains the same as before
            var gd = VeldridManager.GraphicsDevice;
            var factory = VeldridManager.Factory;

            var stagingDesc = TextureDescription.Texture2D(
                _renderTexture.Width, _renderTexture.Height, 1, 1,
                _renderTexture.Format, TextureUsage.Staging);
            var stagingTexture = factory.CreateTexture(stagingDesc);

            var cl = factory.CreateCommandList();
            cl.Begin();
            cl.CopyTexture(_renderTexture, stagingTexture);
            cl.End();
            gd.SubmitCommands(cl);
            gd.WaitForIdle();

            MappedResource mappedResource = gd.Map(stagingTexture, MapMode.Read, 0);
            var rawBytes = new byte[mappedResource.SizeInBytes];
            unsafe
            {
                try
                {
                    var sourceSpan = new ReadOnlySpan<byte>(mappedResource.Data.ToPointer(),
                        (int)mappedResource.SizeInBytes);
                    sourceSpan.CopyTo(rawBytes);
                }
                finally
                {
                    gd.Unmap(stagingTexture, 0);
                }
            }

            try
            {
                using var stream = new MemoryStream();
                var imageWriter = new ImageWriter();
                var extension = Path.GetExtension(path).ToLower();

                if (extension == ".png")
                {
                    imageWriter.WritePng(rawBytes, (int)_renderTexture.Width, (int)_renderTexture.Height, ColorComponents.RedGreenBlueAlpha, stream);
                }
                else if (extension == ".jpg" || extension == ".jpeg")
                {
                    imageWriter.WriteJpg(rawBytes, (int)_renderTexture.Width, (int)_renderTexture.Height, ColorComponents.RedGreenBlueAlpha, stream, 90);
                }
                
                File.WriteAllBytes(path, stream.ToArray());
                Logger.Log($"[Screenshot] Saved to {path}");
            }
            catch(Exception ex)
            {
                Logger.LogError($"[Screenshot] Failed to save image: {ex.Message}");
            }
            finally
            {
                stagingTexture.Dispose();
                cl.Dispose();
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

    // Extension helper for Vector3
    internal static class Vector3Extensions
    {
        public static Vector3 Normalized(this Vector3 v)
        {
            return Vector3.Normalize(v);
        }
    }
}