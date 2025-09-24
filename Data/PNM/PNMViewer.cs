// GeoscientistToolkit/UI/PNMViewer.cs - Metal-Compatible Version
using System;
using System.Collections.Generic;
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
            public Vector4 SizeInfo;      // x: PoreSizeMultiplier, y: ThroatSizeMultiplier
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PoreInstanceData
        {
            public Vector3 Position;
            public float ColorValue;
            public float Radius;
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
        private Vector3 _cameraPosition = new Vector3(0, 0, 3);
        private Vector3 _cameraTarget = Vector3.Zero;
        private float _cameraYaw = -MathF.PI / 2f;
        private float _cameraPitch = 0f;
        private float _cameraDistance = 3.0f;
        private bool _isDragging = false;
        private bool _isPanning = false;
        private Vector2 _lastMousePos;
        private Vector3 _modelCenter;

        // UI & Rendering State
        private int _colorByIndex = 0;
        private readonly string[] _colorByOptions = { "Pore Radius", "Throat Radius", "Pore Connections" };
        private float _poreSizeMultiplier = 1.0f;
        private float _throatSizeMultiplier = 1.0f;
        private bool _showPores = true;
        private bool _showThroats = true;

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
            // Detect if we're running on Metal
            _isMetal = VeldridManager.GraphicsDevice.BackendType == GraphicsBackend.Metal;
            
            InitializeVeldridResources();
            ResetCamera();
        }

        private void InitializeVeldridResources()
        {
            var factory = VeldridManager.Factory;
            _commandList = factory.CreateCommandList();

            // Create render target
            _renderTexture = factory.CreateTexture(TextureDescription.Texture2D(1280, 720, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
            
            // Use appropriate depth format for Metal
            var depthFormat = _isMetal ? PixelFormat.D32_Float_S8_UInt : PixelFormat.R32_Float;
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
        
        #region Metal Shader Resources

        private void CreatePoreResourcesMetal(ResourceFactory factory)
        {
            // Sphere Impostor (Billboard) Geometry
            Vector3[] quadVertices =
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };
            ushort[] quadIndices = { 0, 1, 2, 0, 2, 3 };

            _poreVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(quadVertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreVertexBuffer, 0, quadVertices);
            _poreIndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(quadIndices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreIndexBuffer, 0, quadIndices);

            var instanceData = _dataset.Pores.Select(p => new PoreInstanceData
            {
                Position = p.Position,
                ColorValue = p.Radius,
                Radius = p.Radius
            }).ToArray();

            _poreInstanceCount = instanceData.Length;
            if (_poreInstanceCount == 0) return;

            _poreInstanceBuffer = factory.CreateBuffer(new BufferDescription((uint)(_poreInstanceCount * Marshal.SizeOf<PoreInstanceData>()), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreInstanceBuffer, 0, instanceData);
            
            // Metal Shaders (MSL)
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
};

struct InstanceIn {
    float3 InstancePosition [[attribute(1)]];
    float InstanceColorValue [[attribute(2)]];
    float InstanceRadius [[attribute(3)]];
};

struct VertexOut {
    float4 Position [[position]];
    float3 FragPos;
    float3 Normal;
    float ColorValue;
};

vertex VertexOut pore_vertex_main(
    VertexIn vert [[stage_in]],
    InstanceIn inst [[stage_in]],
    constant Constants& constants [[buffer(0)]],
    uint instanceId [[instance_id]]
) {
    VertexOut out;
    
    // Billboard calculation
    float3 toCamera = normalize(constants.CameraPosition.xyz - inst.InstancePosition);
    float3 right = normalize(cross(float3(0,1,0), toCamera));
    float3 up = cross(toCamera, right);
    
    float radius = inst.InstanceRadius * constants.SizeInfo.x;
    float3 worldPos = inst.InstancePosition + (right * vert.Position.x + up * vert.Position.y) * radius;
    
    out.Position = constants.ViewProjection * float4(worldPos, 1.0);
    out.FragPos = vert.Position;
    out.Normal = float3(vert.Position.xy, sqrt(max(0.0, 0.25 - dot(vert.Position.xy, vert.Position.xy))));
    out.ColorValue = inst.InstanceColorValue;
    
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
    float3 FragPos;
    float3 Normal;
    float ColorValue;
};

fragment float4 pore_fragment_main(
    FragmentIn in [[stage_in]],
    constant Constants& constants [[buffer(0)]],
    texture1d<float> colorRamp [[texture(0)]],
    sampler colorSampler [[sampler(0)]]
) {
    // Discard pixels outside sphere
    if (dot(in.FragPos.xy, in.FragPos.xy) > 0.25) discard_fragment();
    
    float3 normal = normalize(in.Normal);
    float3 lightDir = normalize(float3(1, 1, 1));
    float diffuse = max(dot(normal, lightDir), 0.0) * 0.7 + 0.3;
    
    float normalizedValue = saturate((in.ColorValue - constants.ColorRampInfo.x) * constants.ColorRampInfo.z);
    float3 color = colorRamp.sample(colorSampler, normalizedValue).rgb;
    
    return float4(color * diffuse, 1.0);
}";

            // Create shaders
            var vsBytes = Encoding.UTF8.GetBytes(metalVertexShader);
            var fsBytes = Encoding.UTF8.GetBytes(metalFragmentShader);
            
            var vertexShader = factory.CreateShader(new ShaderDescription(
                ShaderStages.Vertex, vsBytes, "pore_vertex_main"));
            var fragmentShader = factory.CreateShader(new ShaderDescription(
                ShaderStages.Fragment, fsBytes, "pore_fragment_main"));

            // Pipeline setup
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

        private void CreateThroatResourcesMetal(ResourceFactory factory)
        {
            var poreDict = _dataset.Pores.ToDictionary(p => p.ID, p => p.Position);
            var throatVertices = new List<Vector3>();
            var throatColors = new List<float>();

            foreach (var throat in _dataset.Throats)
            {
                if (poreDict.TryGetValue(throat.Pore1ID, out var p1) && poreDict.TryGetValue(throat.Pore2ID, out var p2))
                {
                    throatVertices.Add(p1);
                    throatVertices.Add(p2);
                    throatColors.Add(throat.Radius);
                    throatColors.Add(throat.Radius);
                }
            }
            _throatVertexCount = (uint)throatVertices.Count;
            if (_throatVertexCount == 0) return;

            float[] vertexData = new float[_throatVertexCount * 4];
            for (int i = 0; i < _throatVertexCount; i++)
            {
                vertexData[i * 4 + 0] = throatVertices[i].X;
                vertexData[i * 4 + 1] = throatVertices[i].Y;
                vertexData[i * 4 + 2] = throatVertices[i].Z;
                vertexData[i * 4 + 3] = throatColors[i];
            }
            
            _throatVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertexData.Length * 4), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_throatVertexBuffer, 0, vertexData);

            // Metal Throat Shaders
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
    float4 PositionAndColor [[attribute(0)]];
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
    out.Position = constants.ViewProjection * float4(in.PositionAndColor.xyz, 1.0);
    out.ColorValue = in.PositionAndColor.w;
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
                    new VertexLayoutDescription(new VertexElementDescription("PositionAndColor", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4))
                }, new[] { vertexShader, fragmentShader }),
                new[] { _throatResourceLayout },
                _framebuffer.OutputDescription));
        }

        #endregion

        #region GLSL Shader Resources

        private void CreatePoreResourcesGLSL(ResourceFactory factory)
        {
            // Sphere Impostor (Billboard) Geometry
            Vector3[] quadVertices =
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };
            ushort[] quadIndices = { 0, 1, 2, 0, 2, 3 };

            _poreVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(quadVertices.Length * 12), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreVertexBuffer, 0, quadVertices);
            _poreIndexBuffer = factory.CreateBuffer(new BufferDescription((uint)(quadIndices.Length * 2), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreIndexBuffer, 0, quadIndices);

            var instanceData = _dataset.Pores.Select(p => new PoreInstanceData
            {
                Position = p.Position,
                ColorValue = p.Radius,
                Radius = p.Radius
            }).ToArray();

            _poreInstanceCount = instanceData.Length;
            if (_poreInstanceCount == 0) return;

            _poreInstanceBuffer = factory.CreateBuffer(new BufferDescription((uint)(_poreInstanceCount * Marshal.SizeOf<PoreInstanceData>()), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreInstanceBuffer, 0, instanceData);
            
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

layout(location = 0) out vec3 out_FragPos;
layout(location = 1) out vec3 out_Normal;
layout(location = 2) out float out_ColorValue;

void main() 
{
    vec3 toCamera = normalize(CameraPosition.xyz - in_InstancePosition);
    vec3 right = normalize(cross(vec3(0,1,0), toCamera));
    vec3 up = cross(toCamera, right);
    
    float radius = in_InstanceRadius * SizeInfo.x;
    vec3 worldPos = in_InstancePosition + (right * in_Position.x + up * in_Position.y) * radius;
    gl_Position = ViewProjection * vec4(worldPos, 1.0);
    
    out_FragPos = in_Position;
    out_Normal = vec3(in_Position.xy, sqrt(max(0.0, 0.25 - dot(in_Position.xy, in_Position.xy))));
    out_ColorValue = in_InstanceColorValue;
}";

            string fragmentShader = @"
#version 450
layout(location = 0) in vec3 in_FragPos;
layout(location = 1) in vec3 in_Normal;
layout(location = 2) in float in_ColorValue;
layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 0) uniform Constants { mat4 VP; vec4 CamPos; vec4 ColorRampInfo; vec4 SizeInfo; };
layout(set = 0, binding = 1) uniform texture2D ColorRamp;
layout(set = 0, binding = 2) uniform sampler ColorSampler;

void main() 
{
    if (dot(in_FragPos.xy, in_FragPos.xy) > 0.25) discard;
    
    vec3 normal = normalize(in_Normal);
    vec3 lightDir = normalize(vec3(1, 1, 1));
    float diffuse = max(dot(normal, lightDir), 0.0) * 0.7 + 0.3;
    
    float normalizedValue = clamp((in_ColorValue - ColorRampInfo.x) * ColorRampInfo.z, 0.0, 1.0);
    vec3 color = texture(sampler2D(ColorRamp, ColorSampler), vec2(normalizedValue, 0.5)).rgb;
    
    out_Color = vec4(color * diffuse, 1.0);
}";

            // Pipeline
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

        private void CreateThroatResourcesGLSL(ResourceFactory factory)
        {
            var poreDict = _dataset.Pores.ToDictionary(p => p.ID, p => p.Position);
            var throatVertices = new List<Vector3>();
            var throatColors = new List<float>();

            foreach (var throat in _dataset.Throats)
            {
                if (poreDict.TryGetValue(throat.Pore1ID, out var p1) && poreDict.TryGetValue(throat.Pore2ID, out var p2))
                {
                    throatVertices.Add(p1);
                    throatVertices.Add(p2);
                    throatColors.Add(throat.Radius);
                    throatColors.Add(throat.Radius);
                }
            }
            _throatVertexCount = (uint)throatVertices.Count;
            if (_throatVertexCount == 0) return;

            float[] vertexData = new float[_throatVertexCount * 4];
            for (int i = 0; i < _throatVertexCount; i++)
            {
                vertexData[i * 4 + 0] = throatVertices[i].X;
                vertexData[i * 4 + 1] = throatVertices[i].Y;
                vertexData[i * 4 + 2] = throatVertices[i].Z;
                vertexData[i * 4 + 3] = throatColors[i];
            }
            
            _throatVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertexData.Length * 4), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_throatVertexBuffer, 0, vertexData);

            string vertexShader = @"
#version 450
layout(location = 0) in vec4 in_PositionAndColor;

layout(set = 0, binding = 0) uniform Constants { mat4 VP; vec4 CamPos; vec4 Ramp; vec4 SizeInfo; };

layout(location = 0) out float out_ColorValue;

void main() 
{
    gl_Position = VP * vec4(in_PositionAndColor.xyz, 1.0);
    out_ColorValue = in_PositionAndColor.w;
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
    out_Color = texture(sampler2D(ColorRamp, ColorSampler), vec2(normalizedValue, 0.5));
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
                    new VertexLayoutDescription(new VertexElementDescription("PositionAndColor", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4))
                }, shaderSet),
                new[] { _throatResourceLayout },
                _framebuffer.OutputDescription));
        }
        
        #endregion

        #region Resource Creation Helpers

        private void CreateColorRampTexture(ResourceFactory factory)
        {
            // Use 1D texture for Metal and 2D texture (256x1) for GLSL.
            const int mapSize = 256;
            var colorMapData = new RgbaFloat[mapSize];
            for (int i = 0; i < mapSize; i++)
            {
                float t = i / (float)(mapSize - 1);
                // "Jet" color map approximation
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
            ImGui.SliderFloat("##PoreSize", ref _poreSizeMultiplier, 0.1f, 5.0f);

            ImGui.SameLine();
            ImGui.Text("Throat Size:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("##ThroatSize", ref _throatSizeMultiplier, 0.1f, 5.0f);
            
            // Platform indicator
            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();
            ImGui.TextDisabled($"[{VeldridManager.GraphicsDevice.BackendType}]");
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
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
            }

            // Draw Legend and Statistics
            DrawLegend(imagePos, availableSize);
            DrawStatistics(imagePos, availableSize);
        }
private void RebuildGeometryFromDataset()
{
    var factory = VeldridManager.Factory;

    // ---------- PORES (impostor quads, instanced) ----------
    // Build instance data from *visible* pores
    var poreInstances = _dataset.Pores.Select(p => new PoreInstanceData
    {
        Position = p.Position,
        ColorValue = _colorByIndex == 2 ? p.Connections : p.Radius, // keep in sync with UpdatePoreInstanceDataColor
        Radius = p.Radius
    }).ToArray();
    _poreInstanceCount = poreInstances.Length;

    // Recreate instance buffer sized to new count
    _poreInstanceBuffer?.Dispose();
    uint instanceSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PoreInstanceData>();
    _poreInstanceBuffer = factory.CreateBuffer(new BufferDescription((uint)(instanceSize * Math.Max(1, _poreInstanceCount)), BufferUsage.VertexBuffer));
    if (_poreInstanceCount > 0)
        VeldridManager.GraphicsDevice.UpdateBuffer(_poreInstanceBuffer, 0, poreInstances);

    // (static quad + index buffers for the impostor are unchanged)

    // ---------- THROATS (line list) ----------
    // Build line vertices from *visible* throats by connecting pore positions
    var poreById = _dataset.Pores.ToDictionary(p => p.ID, p => p);
    var verts = new System.Collections.Generic.List<Vector3>(Math.Max(2, _dataset.Throats.Count * 2));
    foreach (var t in _dataset.Throats)
    {
        if (poreById.TryGetValue(t.Pore1ID, out var p1) && poreById.TryGetValue(t.Pore2ID, out var p2))
        {
            verts.Add(p1.Position);
            verts.Add(p2.Position);
        }
    }
    _throatVertexCount = (uint)verts.Count;

    _throatVertexBuffer?.Dispose();
    if (_throatVertexCount > 0)
    {
        _throatVertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(float) * 3 * _throatVertexCount), BufferUsage.VertexBuffer));
        VeldridManager.GraphicsDevice.UpdateBuffer(_throatVertexBuffer, 0, verts.ToArray());
    }
    else
    {
        _throatVertexBuffer = factory.CreateBuffer(new BufferDescription(12, BufferUsage.VertexBuffer)); // tiny dummy
    }

    // finally update color ramp min/max & size constants
    UpdateConstantBuffers();
}
        private void Render()
        {
            if (_pendingGeometryRebuild)
            {
                RebuildGeometryFromDataset();
                _pendingGeometryRebuild = false;
            }
            UpdateConstantBuffers();

            _commandList.Begin();
            _commandList.SetFramebuffer(_framebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1.0f));
            _commandList.ClearDepthStencil(1f);

            // Draw Throats
            if (_showThroats && _throatVertexCount > 0)
            {
                _commandList.SetPipeline(_throatPipeline);
                _commandList.SetGraphicsResourceSet(0, _throatResourceSet);
                _commandList.SetVertexBuffer(0, _throatVertexBuffer);
                _commandList.Draw(_throatVertexCount);
            }

            // Draw Pores
            if (_showPores && _poreInstanceCount > 0)
            {
                _commandList.SetPipeline(_porePipeline);
                _commandList.SetGraphicsResourceSet(0, _poreResourceSet);
                _commandList.SetVertexBuffer(0, _poreVertexBuffer);
                _commandList.SetVertexBuffer(1, _poreInstanceBuffer);
                _commandList.SetIndexBuffer(_poreIndexBuffer, IndexFormat.UInt16);
                _commandList.DrawIndexed(6, (uint)_poreInstanceCount, 0, 0, 0);
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
            }
            if (maxVal <= minVal) maxVal = minVal + 0.001f;

            var constants = new Constants
            {
                ViewProjection = _viewMatrix * _projMatrix,
                CameraPosition = new Vector4(_cameraPosition, 1),
                ColorRampInfo = new Vector4(minVal, maxVal, 1.0f / (maxVal - minVal), 0),
                SizeInfo = new Vector4(_poreSizeMultiplier, _throatSizeMultiplier, 0, 0)
            };

            if (_poreConstantsBuffer != null)
                VeldridManager.GraphicsDevice.UpdateBuffer(_poreConstantsBuffer, 0, ref constants);
            if (_throatConstantsBuffer != null)
                VeldridManager.GraphicsDevice.UpdateBuffer(_throatConstantsBuffer, 0, ref constants);
        }

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
            
            // Draw gradient rectangle
            int numSteps = 20;
            float stepHeight = legendHeight / numSteps;
            for (int i = 0; i < numSteps; i++)
            {
                float t1 = i / (float)numSteps;
                float t2 = (i + 1) / (float)numSteps;

                // "Jet" color map approximation
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

            // Get current min/max for labels
            float minVal = 0, maxVal = 1;
            string unit = " voxels";
            switch (_colorByIndex)
            {
                case 0: minVal = _dataset.MinPoreRadius; maxVal = _dataset.MaxPoreRadius; unit = " um"; break;
                case 1: minVal = _dataset.MinThroatRadius; maxVal = _dataset.MaxThroatRadius; unit = " um"; break;
                case 2: minVal = _dataset.Pores.Any() ? _dataset.Pores.Min(p => p.Connections) : 0; maxVal = _dataset.Pores.Any() ? _dataset.Pores.Max(p => p.Connections) : 1; unit = ""; break;
            }
            if (_colorByIndex < 2) { minVal *= _dataset.VoxelSize; maxVal *= _dataset.VoxelSize; }

            // Draw labels
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
            var statsPos = new Vector2(viewPos.X + padding, viewPos.Y + viewSize.Y - 140); // Increased height for new line
            var drawList = ImGui.GetForegroundDrawList();
    
            // Background
            drawList.AddRectFilled(statsPos, new Vector2(statsPos.X + 380, statsPos.Y + 130), // Increased height
                ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)), 5);
    
            // Statistics text
            var textPos = statsPos + new Vector2(10, 10);
            uint textColor = 0xFFFFFFFF;
    
            drawList.AddText(textPos, textColor, $"Network Statistics:");
            textPos.Y += 20;
            drawList.AddText(textPos, textColor, $"Pores: {_dataset.Pores.Count:N0}  |  Throats: {_dataset.Throats.Count:N0}");
            textPos.Y += 18;
            drawList.AddText(textPos, textColor, $"Tortuosity: {_dataset.Tortuosity:F3}");
            textPos.Y += 18;
            // --- MODIFIED SECTION ---
            drawList.AddText(textPos, textColor, $"Permeability (mD):");
            textPos.Y += 16;
            drawList.AddText(textPos, textColor, $"  - Darcy: {_dataset.DarcyPermeability:F2}");
            textPos.Y += 16;
            drawList.AddText(textPos, textColor, $"  - Navier-Stokes: {_dataset.NavierStokesPermeability:F2}");
            textPos.Y += 16;
            drawList.AddText(textPos, textColor, $"  - Lattice-Boltzmann: {_dataset.LatticeBoltzmannPermeability:F2}");
            // --- END MODIFICATION ---
        }

        private void UpdatePoreInstanceDataColor()
        {
            if (_poreInstanceCount == 0) return;

            PoreInstanceData[] instanceData;
            switch (_colorByIndex)
            {
                case 2: // Pore Connections
                    instanceData = _dataset.Pores.Select(p => new PoreInstanceData
                    {
                        Position = p.Position,
                        ColorValue = p.Connections,
                        Radius = p.Radius
                    }).ToArray();
                    break;
                case 0: // Pore Radius (default)
                case 1: // Throat Radius (color pores by their radius for consistency)
                default:
                    instanceData = _dataset.Pores.Select(p => new PoreInstanceData
                    {
                        Position = p.Position,
                        ColorValue = p.Radius,
                        Radius = p.Radius
                    }).ToArray();
                    break;
            }
            
            VeldridManager.GraphicsDevice.UpdateBuffer(_poreInstanceBuffer, 0, instanceData);
        }

        private void HandleMouseInput()
        {
            var io = ImGui.GetIO();
            if (io.MouseWheel != 0)
            {
                _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * 0.1f), 0.1f, 50.0f);
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                if (!_isDragging && !_isPanning)
                {
                    _isDragging = ImGui.IsMouseDown(ImGuiMouseButton.Left);
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
                _cameraDistance = Vector3.Distance(min, max);
                if (_cameraDistance < 0.1f) _cameraDistance = 5.0f;
            }
            else
            {
                _modelCenter = Vector3.Zero;
                _cameraDistance = 5.0f;
            }

            _cameraTarget = _modelCenter;
            _cameraYaw = -MathF.PI / 4f;
            _cameraPitch = MathF.PI / 6f;
            UpdateCameraMatrices();
        }

        #endregion

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
}