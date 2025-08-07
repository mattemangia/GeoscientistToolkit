using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid;
using System.Text;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Mesh3D
{
    /// <summary>
    /// Direct3D11-specific mesh renderer implementation.
    /// </summary>
    public class D3D11MeshRenderer : IDisposable
    {
        // Veldrid objects for rendering
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private DeviceBuffer _constantBuffer;
        private Pipeline _pipelineTriangles;
        private Pipeline _pipelineLines;
        private ResourceLayout _resourceLayout;
        private ResourceSet _resourceSet;
        private CommandList _commandList;
        private Framebuffer _framebuffer;
        private Texture _colorTarget;
        private Texture _depthTarget;
        private uint _indexCount;  // Track actual index count

        // Keep track of render target size
        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public Texture ColorTarget => _colorTarget;  // expose for texture manager

        // Shader code for D3D11 (HLSL)
        private const string HlslShaderSource = @"
cbuffer Constants : register(b0)
{
    float4x4 Model;
    float4x4 MVP;
    float4 Color;
};
struct VSInput { float3 Pos : TEXCOORD0; float3 Normal : TEXCOORD1; };
struct VSOutput { float4 Pos : SV_POSITION; float3 Normal : TEXCOORD0; };
VSOutput mesh_vs(VSInput input)
{
    VSOutput output;
    float4 worldPos = mul(float4(input.Pos, 1.0f), Model);
    output.Pos = mul(worldPos, MVP);
    float3 worldNormal = normalize(mul(float4(input.Normal, 0.0f), Model).xyz);
    output.Normal = worldNormal;
    return output;
}
float4 mesh_fs(VSOutput input) : SV_Target
{
    float3 lightDir = normalize(float3(-1.0f, -1.0f, -1.0f));
    float diff = max(dot(input.Normal, lightDir), 0.0f);
    float3 baseColor = Color.rgb;
    float3 litColor = baseColor * (0.2f + 0.8f * diff);
    return float4(litColor, 1.0f);
}
struct LineVSInput { float3 Pos : TEXCOORD0; };
struct LineVSOutput { float4 Pos : SV_POSITION; };
LineVSOutput line_vs(LineVSInput input)
{
    LineVSOutput output;
    output.Pos = mul(float4(input.Pos, 1.0f), MVP);
    return output;
}
float4 line_fs(LineVSOutput input) : SV_Target
{
    // Use color uniform for grid line color
    return float4(Color.rgb, 1.0f);
}
";

        /// <summary>
        /// Initialize rendering resources for a given Mesh3DDataset.
        /// </summary>
        public void Initialize(Mesh3DDataset dataset)
        {
            // Determine initial render target size (e.g., 1280x720)
            Width = 1280;
            Height = 720;
            var factory = VeldridManager.Factory;
            // Create offscreen color and depth textures
            _colorTarget = factory.CreateTexture(TextureDescription.Texture2D(
                Width, Height, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));
            
            // Use a more compatible depth format
            PixelFormat depthFormat = PixelFormat.D24_UNorm_S8_UInt;
            if (VeldridManager.GraphicsDevice.BackendType == GraphicsBackend.Direct3D11)
            {
                // D3D11 supports D24_UNorm_S8_UInt
                depthFormat = PixelFormat.D24_UNorm_S8_UInt;
            }
            else
            {
                // Use a more universally supported format as fallback
                depthFormat = PixelFormat.D32_Float_S8_UInt;
            }
            
            _depthTarget = factory.CreateTexture(TextureDescription.Texture2D(
                Width, Height, 1, 1, depthFormat, TextureUsage.DepthStencil));
            _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTarget, _colorTarget));
            // Create command list
            _commandList = factory.CreateCommandList();
            // Create constant buffer
            uint constBufferSize = (uint)(Unsafe.SizeOf<MeshConstants>());
            _constantBuffer = factory.CreateBuffer(new BufferDescription(constBufferSize, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            // Compile and create shaders
            Shader vertexShaderMain = factory.CreateShader(new ShaderDescription(
                ShaderStages.Vertex, Encoding.UTF8.GetBytes(HlslShaderSource), "mesh_vs"));
            Shader fragmentShaderMain = factory.CreateShader(new ShaderDescription(
                ShaderStages.Fragment, Encoding.UTF8.GetBytes(HlslShaderSource), "mesh_fs"));
            Shader vertexShaderLine = factory.CreateShader(new ShaderDescription(
                ShaderStages.Vertex, Encoding.UTF8.GetBytes(HlslShaderSource), "line_vs"));
            Shader fragmentShaderLine = factory.CreateShader(new ShaderDescription(
                ShaderStages.Fragment, Encoding.UTF8.GetBytes(HlslShaderSource), "line_fs"));
            // Create resource layout (single uniform buffer shared by VS and FS)
            _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)
            ));
            // Create resource set for the constant buffer
            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _constantBuffer));
            // Define vertex layouts
            var vertexLayoutMain = new VertexLayoutDescription(
                new VertexElementDescription("Pos", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
            );
            var vertexLayoutLine = new VertexLayoutDescription(
                new VertexElementDescription("Pos", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
            );
            // Create graphics pipelines
            GraphicsPipelineDescription triPipelineDesc = new GraphicsPipelineDescription()
            {
                BlendState = BlendStateDescription.SingleOverrideBlend,
                DepthStencilState = new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                ShaderSet = new ShaderSetDescription(new[] { vertexLayoutMain }, new[] { vertexShaderMain, fragmentShaderMain }),
                ResourceLayouts = new[] { _resourceLayout },
                Outputs = _framebuffer.OutputDescription
            };
            _pipelineTriangles = factory.CreateGraphicsPipeline(triPipelineDesc);
            GraphicsPipelineDescription linePipelineDesc = new GraphicsPipelineDescription()
            {
                BlendState = BlendStateDescription.SingleAlphaBlend,
                DepthStencilState = new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
                RasterizerState = new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, depthClipEnabled: true, scissorTestEnabled: false),
                PrimitiveTopology = PrimitiveTopology.LineList,
                ShaderSet = new ShaderSetDescription(new[] { vertexLayoutLine }, new[] { vertexShaderLine, fragmentShaderLine }),
                ResourceLayouts = new[] { _resourceLayout },
                Outputs = _framebuffer.OutputDescription
            };
            _pipelineLines = factory.CreateGraphicsPipeline(linePipelineDesc);
            // Create vertex and index buffers for the mesh
            CreateMeshBuffers(dataset, factory);
        }

        /// <summary>
        /// Render the dataset and grid to the offscreen framebuffer using the current camera matrices.
        /// </summary>
        public void Render(Mesh3DDataset dataset, Matrix4x4 viewMatrix, Matrix4x4 projMatrix, Vector3 cameraTarget, bool showGrid)
        {
            var device = VeldridManager.GraphicsDevice;
            // Begin commands
            _commandList.Begin();
            _commandList.SetFramebuffer(_framebuffer);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);
            // Draw the main mesh (with possible rotation/scale from Tools)
            // Compute model matrix (includes dataset scale, rotation from Tools, and centering)
            Matrix4x4 model = ComputeModelMatrix(dataset);
            // Prepare constant buffer data
            MeshConstants consts;
            consts.Model = model;
            consts.MVP = model * viewMatrix * projMatrix;
            consts.Color = ChooseColorForDataset(dataset);
            // Update constant buffer
            device.UpdateBuffer(_constantBuffer, 0, ref consts);
            // Draw triangles
            _commandList.SetPipeline(_pipelineTriangles);
            _commandList.SetGraphicsResourceSet(0, _resourceSet);
            _commandList.SetVertexBuffer(0, _vertexBuffer);
            _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
            _commandList.DrawIndexed(_indexCount, 1, 0, 0, 0);
            // Draw grid if enabled
            if (showGrid)
            {
                // Update constant buffer for grid (Model = identity, use same view/proj, set grid color)
                consts.Model = Matrix4x4.Identity;
                consts.MVP = viewMatrix * projMatrix;
                consts.Color = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);  // grid line color
                device.UpdateBuffer(_constantBuffer, 0, ref consts);
                // Ensure grid vertex buffers exist and cover scene extents
                UpdateGridBuffers(dataset, cameraTarget);
                _commandList.SetPipeline(_pipelineLines);
                _commandList.SetGraphicsResourceSet(0, _resourceSet);
                _commandList.SetVertexBuffer(0, _vertexBufferGrid);
                _commandList.SetIndexBuffer(_indexBufferGrid, IndexFormat.UInt32);
                _commandList.DrawIndexed(_gridIndexCount, 1, 0, 0, 0);
            }
            // End and submit commands
            _commandList.End();
            device.SubmitCommands(_commandList);
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _vertexBufferGrid?.Dispose();
            _indexBufferGrid?.Dispose();
            _constantBuffer?.Dispose();
            _pipelineTriangles?.Dispose();
            _pipelineLines?.Dispose();
            _resourceSet?.Dispose();
            _resourceLayout?.Dispose();
            _commandList?.Dispose();
            _framebuffer?.Dispose();
            _colorTarget?.Dispose();
            _depthTarget?.Dispose();
        }

        // Additional fields and methods for grid and model buffers:
        private DeviceBuffer _vertexBufferGrid;
        private DeviceBuffer _indexBufferGrid;
        private uint _gridIndexCount;

        /// <summary>
        /// Create vertex and index buffers for the mesh model.
        /// </summary>
        private void CreateMeshBuffers(Mesh3DDataset dataset, ResourceFactory factory)
        {
            // Interleave vertex positions and normals into one buffer (float6 per vertex)
            int vertexCount = dataset.Vertices.Count;
            float[] vertexData = new float[vertexCount * 6];
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 v = dataset.Vertices[i];
                Vector3 n = (dataset.Normals.Count > i) ? dataset.Normals[i] : Vector3.UnitY;
                vertexData[i * 6 + 0] = v.X;
                vertexData[i * 6 + 1] = v.Y;
                vertexData[i * 6 + 2] = v.Z;
                vertexData[i * 6 + 3] = n.X;
                vertexData[i * 6 + 4] = n.Y;
                vertexData[i * 6 + 5] = n.Z;
            }
            _vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(vertexData.Length * sizeof(float)), BufferUsage.VertexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertexData);

            // Create index buffer - properly handle triangulated and polygon faces
            System.Collections.Generic.List<uint> indexList = new System.Collections.Generic.List<uint>();
            foreach (var face in dataset.Faces)
            {
                if (face.Length == 3)
                {
                    // Triangle face - add directly
                    indexList.Add((uint)face[0]);
                    indexList.Add((uint)face[1]);
                    indexList.Add((uint)face[2]);
                }
                else if (face.Length > 3)
                {
                    // Polygon face - triangulate using fan method from first vertex
                    for (int i = 0; i < face.Length - 2; i++)
                    {
                        indexList.Add((uint)face[0]);
                        indexList.Add((uint)face[i + 1]);
                        indexList.Add((uint)face[i + 2]);
                    }
                }
                // Skip faces with less than 3 vertices (invalid)
            }
            
            uint[] indices = indexList.ToArray();
            _indexCount = (uint)indices.Length;
            _indexBuffer = factory.CreateBuffer(new BufferDescription((uint)(indices.Length * sizeof(uint)), BufferUsage.IndexBuffer));
            VeldridManager.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);
        }

        /// <summary>
        /// Compute the model matrix for the given dataset, including user-defined scale/rotation.
        /// </summary>
        private Matrix4x4 ComputeModelMatrix(Mesh3DDataset dataset)
        {
            // Retrieve any user-defined rotation (in degrees) from Mesh3DTools
            Vector3 rotationDeg = Mesh3DTools.GetRotation(dataset);
            // Convert to radians and create rotation matrices (apply X then Y then Z)
            float rx = MathF.PI * rotationDeg.X / 180f;
            float ry = MathF.PI * rotationDeg.Y / 180f;
            float rz = MathF.PI * rotationDeg.Z / 180f;
            Matrix4x4 rotX = Matrix4x4.CreateRotationX(rx);
            Matrix4x4 rotY = Matrix4x4.CreateRotationY(ry);
            Matrix4x4 rotZ = Matrix4x4.CreateRotationZ(rz);
            Matrix4x4 rotation = rotZ * rotY * rotX;
            // Compose: scale, then rotate, then translate model center to origin
            Matrix4x4 model = Matrix4x4.CreateScale(dataset.Scale) * rotation * Matrix4x4.CreateTranslation(-dataset.Center);
            return model;
        }

        /// <summary>
        /// Choose a unique color for the dataset (if multiple models supported).
        /// Here we just assign a default color for single model.
        /// </summary>
        private Vector4 ChooseColorForDataset(Mesh3DDataset dataset)
        {
            // For now, use a fixed color or based on dataset index if multiple.
            // This implementation uses a default teal color for the primary model.
            return new Vector4(0.2f, 0.7f, 0.7f, 1.0f);
        }

        /// <summary>
        /// Update or create the grid buffers to cover the scene extents.
        /// </summary>
        private void UpdateGridBuffers(Mesh3DDataset dataset, Vector3 cameraTarget)
        {
            // Determine grid half-size based on dataset bounding extents
            Vector3 extents = dataset.BoundingBoxMax - dataset.BoundingBoxMin;
            float halfSize = 0.5f * MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
            if (halfSize < 1f) halfSize = 1f;
            // Use 10 divisions on each side of origin
            int divisions = 10;
            float spacing = halfSize / divisions;
            // Recreate grid buffers if needed or if size changed significantly
            float gridSize = spacing * divisions;
            // Build line vertices along X and Z axes at Y=0
            var vertices = new System.Collections.Generic.List<float>();
            var indices = new System.Collections.Generic.List<uint>();
            uint idx = 0;
            // Vertical lines (parallel to Z axis, varying X)
            for (int i = -divisions; i <= divisions; i++)
            {
                float x = i * spacing;
                vertices.Add(x); vertices.Add(0f); vertices.Add(-gridSize);
                vertices.Add(x); vertices.Add(0f); vertices.Add(gridSize);
                // two vertices form a line
                indices.Add(idx); indices.Add(idx + 1);
                idx += 2;
            }
            // Horizontal lines (parallel to X axis, varying Z)
            for (int k = -divisions; k <= divisions; k++)
            {
                float z = k * spacing;
                vertices.Add(-gridSize); vertices.Add(0f); vertices.Add(z);
                vertices.Add(gridSize);  vertices.Add(0f); vertices.Add(z);
                indices.Add(idx); indices.Add(idx + 1);
                idx += 2;
            }
            _gridIndexCount = (uint)indices.Count;
            // Create or update buffers
            uint vertexBufferSize = (uint)(vertices.Count * sizeof(float));
            if (_vertexBufferGrid == null || _vertexBufferGrid.SizeInBytes < vertexBufferSize)
            {
                _vertexBufferGrid?.Dispose();
                _vertexBufferGrid = VeldridManager.Factory.CreateBuffer(new BufferDescription(vertexBufferSize, BufferUsage.VertexBuffer));
            }
            VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBufferGrid, 0, vertices.ToArray());
            uint indexBufferSize = (uint)(indices.Count * sizeof(uint));
            if (_indexBufferGrid == null || _indexBufferGrid.SizeInBytes < indexBufferSize)
            {
                _indexBufferGrid?.Dispose();
                _indexBufferGrid = VeldridManager.Factory.CreateBuffer(new BufferDescription(indexBufferSize, BufferUsage.IndexBuffer));
            }
            VeldridManager.GraphicsDevice.UpdateBuffer(_indexBufferGrid, 0, indices.ToArray());
        }

        // Constant buffer data structure (must match HLSL/Metal constant layout)
        private struct MeshConstants
        {
            public Matrix4x4 Model;
            public Matrix4x4 MVP;
            public Vector4 Color;
        }
    }
}