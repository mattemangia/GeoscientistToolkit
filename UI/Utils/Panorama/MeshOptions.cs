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
    }
}