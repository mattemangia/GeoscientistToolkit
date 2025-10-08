using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using GeoscientistToolkit.Util;
using Veldrid;

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
///     Metal-specific mesh renderer implementation.
/// </summary>
public class MetalMeshRenderer : IDisposable
{
    // Metal shader source (MSL)
    private const string MetalShaderSource = @"
#include <metal_stdlib>
using namespace metal;
struct Constants {
    float4x4 Model;
    float4x4 MVP;
    float4 Color;
};
struct VSInput {
    float3 Pos [[attribute(0)]];
    float3 Normal [[attribute(1)]];
};
struct VSOutput {
    float4 Position [[position]];
    float3 Normal;
};
vertex VSOutput mesh_vertex_main(VSInput in [[stage_in]], constant Constants& c [[buffer(0)]]) {
    VSOutput out;
    float4 worldPos = c.Model * float4(in.Pos, 1.0);
    out.Position = c.MVP * float4(in.Pos, 1.0);
    float3 worldNormal = normalize((c.Model * float4(in.Normal, 0.0)).xyz);
    out.Normal = worldNormal;
    return out;
}
fragment float4 mesh_fragment_main(VSOutput in [[stage_in]], constant Constants& c [[buffer(0)]]) {
    float3 lightDir = normalize(float3(-1.0, -1.0, -1.0));
    float diff = max(dot(in.Normal, lightDir), 0.0);
    float3 baseColor = c.Color.xyz;
    float3 litColor = baseColor * (0.2 + 0.8 * diff);
    return float4(litColor, 1.0);
}
struct LineVSInput {
    float3 Pos [[attribute(0)]];
};
vertex float4 line_vertex_main(LineVSInput in [[stage_in]], constant Constants& c [[buffer(0)]]) {
    return c.MVP * float4(in.Pos, 1.0);
}
fragment float4 line_fragment_main(constant Constants& c [[buffer(0)]]) {
    return float4(c.Color.xyz, 1.0);
}
";

    private CommandList _commandList;
    private DeviceBuffer _constantBuffer;
    private Texture _depthTarget;
    private Framebuffer _framebuffer;
    private uint _gridIndexCount;
    private DeviceBuffer _indexBuffer;
    private DeviceBuffer _indexBufferGrid;
    private Pipeline _pipelineLines;
    private Pipeline _pipelineTriangles;
    private ResourceLayout _resourceLayout;
    private ResourceSet _resourceSet;
    private DeviceBuffer _vertexBuffer;

    private DeviceBuffer _vertexBufferGrid;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public Texture ColorTarget { get; private set; }

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
        ColorTarget?.Dispose();
        _depthTarget?.Dispose();
    }

    /// <summary>
    ///     Initialize rendering resources for the dataset (Metal backend).
    /// </summary>
    public void Initialize(Mesh3DDataset dataset)
    {
        Width = 1280;
        Height = 720;
        var factory = VeldridManager.Factory;
        ColorTarget = factory.CreateTexture(TextureDescription.Texture2D(
            Width, Height, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));

        // Use Metal-compatible depth format
        _depthTarget = factory.CreateTexture(TextureDescription.Texture2D(
            Width, Height, 1, 1, PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));

        _framebuffer = factory.CreateFramebuffer(new FramebufferDescription(_depthTarget, ColorTarget));
        _commandList = factory.CreateCommandList();
        var constBufferSize = (uint)Unsafe.SizeOf<MeshConstants>();
        _constantBuffer =
            factory.CreateBuffer(
                new BufferDescription(constBufferSize, BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        // Compile Metal shaders
        var vsMain = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(MetalShaderSource), "mesh_vertex_main"));
        var fsMain = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(MetalShaderSource), "mesh_fragment_main"));
        var vsLine = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(MetalShaderSource), "line_vertex_main"));
        var fsLine = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(MetalShaderSource), "line_fragment_main"));

        _resourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Constants", ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment)
        ));
        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _constantBuffer));

        // Vertex layouts (same as D3D)
        var vertexLayoutMain = new VertexLayoutDescription(
            new VertexElementDescription("Pos", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
        );
        var vertexLayoutLine = new VertexLayoutDescription(
            new VertexElementDescription("Pos", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3)
        );

        // Pipelines
        var triDesc = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true,
                false),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(new[] { vertexLayoutMain }, new[] { vsMain, fsMain }),
            new[] { _resourceLayout },
            _framebuffer.OutputDescription
        );
        _pipelineTriangles = factory.CreateGraphicsPipeline(triDesc);

        var lineDesc = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(true, true, ComparisonKind.LessEqual),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true,
                false),
            PrimitiveTopology.LineList,
            new ShaderSetDescription(new[] { vertexLayoutLine }, new[] { vsLine, fsLine }),
            new[] { _resourceLayout },
            _framebuffer.OutputDescription
        );
        _pipelineLines = factory.CreateGraphicsPipeline(lineDesc);

        // Create buffers for mesh vertices and indices
        CreateMeshBuffers(dataset, factory);
    }

    public void Render(Mesh3DDataset dataset, Matrix4x4 viewMatrix, Matrix4x4 projMatrix, Vector3 cameraTarget,
        bool showGrid)
    {
        var device = VeldridManager.GraphicsDevice;
        _commandList.Begin();
        _commandList.SetFramebuffer(_framebuffer);
        _commandList.ClearColorTarget(0, RgbaFloat.Black);
        _commandList.ClearDepthStencil(1f);

        // Compute model matrix and constants
        var model = ComputeModelMatrix(dataset);
        MeshConstants consts;
        consts.Model = model;
        consts.MVP = model * viewMatrix * projMatrix;
        consts.Color = ChooseColorForDataset(dataset);
        device.UpdateBuffer(_constantBuffer, 0, ref consts);

        // Draw mesh triangles
        _commandList.SetPipeline(_pipelineTriangles);
        _commandList.SetGraphicsResourceSet(0, _resourceSet);
        _commandList.SetVertexBuffer(0, _vertexBuffer);
        _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);

        var indexCount = (uint)(dataset.Faces.Count * 3);
        _commandList.DrawIndexed(indexCount, 1, 0, 0, 0);

        // Draw grid if needed
        if (showGrid)
        {
            consts.Model = Matrix4x4.Identity;
            consts.MVP = viewMatrix * projMatrix;
            consts.Color = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
            device.UpdateBuffer(_constantBuffer, 0, ref consts);
            UpdateGridBuffers(dataset, cameraTarget);
            _commandList.SetPipeline(_pipelineLines);
            _commandList.SetGraphicsResourceSet(0, _resourceSet);
            _commandList.SetVertexBuffer(0, _vertexBufferGrid);
            _commandList.SetIndexBuffer(_indexBufferGrid, IndexFormat.UInt32);
            _commandList.DrawIndexed(_gridIndexCount, 1, 0, 0, 0);
        }

        _commandList.End();
        device.SubmitCommands(_commandList);
    }

    private void CreateMeshBuffers(Mesh3DDataset dataset, ResourceFactory factory)
    {
        var vertexCount = dataset.Vertices.Count;
        var vertexData = new float[vertexCount * 6];
        for (var i = 0; i < vertexCount; i++)
        {
            var v = dataset.Vertices[i];
            var n = dataset.Normals.Count > i ? dataset.Normals[i] : Vector3.UnitY;
            vertexData[i * 6 + 0] = v.X;
            vertexData[i * 6 + 1] = v.Y;
            vertexData[i * 6 + 2] = v.Z;
            vertexData[i * 6 + 3] = n.X;
            vertexData[i * 6 + 4] = n.Y;
            vertexData[i * 6 + 5] = n.Z;
        }

        _vertexBuffer =
            factory.CreateBuffer(new BufferDescription((uint)(vertexData.Length * sizeof(float)),
                BufferUsage.VertexBuffer));
        VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, vertexData);

        // Create index buffer - STL and OBJ faces are already triangulated
        var indexList = new List<uint>();
        foreach (var face in dataset.Faces)
            if (face.Length == 3)
            {
                // Triangle face - add directly
                indexList.Add((uint)face[0]);
                indexList.Add((uint)face[1]);
                indexList.Add((uint)face[2]);
            }
            else if (face.Length > 3)
            {
                // Polygon face - triangulate using fan method
                for (var i = 0; i < face.Length - 2; i++)
                {
                    indexList.Add((uint)face[0]);
                    indexList.Add((uint)face[i + 1]);
                    indexList.Add((uint)face[i + 2]);
                }
            }

        var indices = indexList.ToArray();
        _indexBuffer =
            factory.CreateBuffer(new BufferDescription((uint)(indices.Length * sizeof(uint)), BufferUsage.IndexBuffer));
        VeldridManager.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, indices);
    }

    private Matrix4x4 ComputeModelMatrix(Mesh3DDataset dataset)
    {
        var rotationDeg = Mesh3DTools.GetRotation(dataset);
        var rx = MathF.PI * rotationDeg.X / 180f;
        var ry = MathF.PI * rotationDeg.Y / 180f;
        var rz = MathF.PI * rotationDeg.Z / 180f;
        var rotX = Matrix4x4.CreateRotationX(rx);
        var rotY = Matrix4x4.CreateRotationY(ry);
        var rotZ = Matrix4x4.CreateRotationZ(rz);
        var rotation = rotZ * rotY * rotX;
        var model = Matrix4x4.CreateScale(dataset.Scale) * rotation * Matrix4x4.CreateTranslation(-dataset.Center);
        return model;
    }

    private Vector4 ChooseColorForDataset(Mesh3DDataset dataset)
    {
        return new Vector4(0.2f, 0.7f, 0.7f, 1.0f);
    }

    private void UpdateGridBuffers(Mesh3DDataset dataset, Vector3 cameraTarget)
    {
        var extents = dataset.BoundingBoxMax - dataset.BoundingBoxMin;
        var halfSize = 0.5f * MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
        if (halfSize < 1f) halfSize = 1f;
        var divisions = 10;
        var spacing = halfSize / divisions;
        var gridSize = spacing * divisions;
        var vertices = new List<float>();
        var indices = new List<uint>();
        uint idx = 0;
        for (var i = -divisions; i <= divisions; i++)
        {
            var x = i * spacing;
            vertices.Add(x);
            vertices.Add(0f);
            vertices.Add(-gridSize);
            vertices.Add(x);
            vertices.Add(0f);
            vertices.Add(gridSize);
            indices.Add(idx);
            indices.Add(idx + 1);
            idx += 2;
        }

        for (var k = -divisions; k <= divisions; k++)
        {
            var z = k * spacing;
            vertices.Add(-gridSize);
            vertices.Add(0f);
            vertices.Add(z);
            vertices.Add(gridSize);
            vertices.Add(0f);
            vertices.Add(z);
            indices.Add(idx);
            indices.Add(idx + 1);
            idx += 2;
        }

        _gridIndexCount = (uint)indices.Count;
        var vbSize = (uint)(vertices.Count * sizeof(float));
        if (_vertexBufferGrid == null || _vertexBufferGrid.SizeInBytes < vbSize)
        {
            _vertexBufferGrid?.Dispose();
            _vertexBufferGrid =
                VeldridManager.Factory.CreateBuffer(new BufferDescription(vbSize, BufferUsage.VertexBuffer));
        }

        VeldridManager.GraphicsDevice.UpdateBuffer(_vertexBufferGrid, 0, vertices.ToArray());
        var ibSize = (uint)(indices.Count * sizeof(uint));
        if (_indexBufferGrid == null || _indexBufferGrid.SizeInBytes < ibSize)
        {
            _indexBufferGrid?.Dispose();
            _indexBufferGrid =
                VeldridManager.Factory.CreateBuffer(new BufferDescription(ibSize, BufferUsage.IndexBuffer));
        }

        VeldridManager.GraphicsDevice.UpdateBuffer(_indexBufferGrid, 0, indices.ToArray());
    }

    private struct MeshConstants
    {
        public Matrix4x4 Model;
        public Matrix4x4 MVP;
        public Vector4 Color;
    }
}