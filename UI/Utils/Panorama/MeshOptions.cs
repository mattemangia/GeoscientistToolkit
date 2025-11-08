// GeoscientistToolkit/Business/Photogrammetry/Options/MeshOptions.cs

namespace GeoscientistToolkit.Business.Photogrammetry
{
    /// <summary>
    /// Options for mesh generation
    /// </summary>
    public class MeshOptions
    {
        public enum SourceData
        {
            SparseCloud,
            DenseCloud
        }

        /// <summary>
        /// Source point cloud for mesh generation
        /// </summary>
        public SourceData Source { get; set; } = SourceData.DenseCloud;

        /// <summary>
        /// Target number of faces in the mesh
        /// </summary>
        public int FaceCount { get; set; } = 100000;

        /// <summary>
        /// Maximum edge length for triangles
        /// </summary>
        public float MaxEdgeLength { get; set; } = float.MaxValue;

        /// <summary>
        /// Whether to simplify the mesh after generation
        /// </summary>
        public bool SimplifyMesh { get; set; } = false;

        /// <summary>
        /// Whether to smooth normals after generation
        /// </summary>
        public bool SmoothNormals { get; set; } = true;

        /// <summary>
        /// Whether to optimize mesh quality after generation
        /// </summary>
        public bool OptimizeMesh { get; set; } = true;

        /// <summary>
        /// Mesh optimization quality profile
        /// </summary>
        public OptimizationQuality OptimizationQuality { get; set; } = OptimizationQuality.Balanced;
    }

    /// <summary>
    /// Mesh optimization quality profiles
    /// </summary>
    public enum OptimizationQuality
    {
        /// <summary>
        /// Fast optimization - quick preview quality (1-2 passes)
        /// </summary>
        Fast,

        /// <summary>
        /// Balanced optimization - good quality vs performance (2-3 passes)
        /// </summary>
        Balanced,

        /// <summary>
        /// High quality optimization - maximum quality (3-5 passes)
        /// </summary>
        High,

        /// <summary>
        /// Custom optimization - user-defined settings
        /// </summary>
        Custom
    }
}