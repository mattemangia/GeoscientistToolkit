// GeoscientistToolkit/Business/Photogrammetry/PhotogrammetryGraph.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business.Panorama;
using GeoscientistToolkit.Data.Image;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Graph structure for managing image relationships and poses
    /// </summary>
    public partial class PhotogrammetryGraph
    {
        private readonly Dictionary<Guid, PhotogrammetryImage> _nodes;
        internal readonly Dictionary<Guid, List<(Guid NeighborId, List<FeatureMatch> Matches, Matrix4x4 Pose)>> _adj;

        public PhotogrammetryGraph(List<PhotogrammetryImage> images)
        {
            _nodes = images.ToDictionary(img => img.Id);
            _adj = new Dictionary<Guid, List<(Guid, List<FeatureMatch>, Matrix4x4)>>();

            foreach (var image in images)
            {
                _adj[image.Id] = new List<(Guid, List<FeatureMatch>, Matrix4x4)>();
            }
        }

        public void AddEdge(PhotogrammetryImage img1, PhotogrammetryImage img2, 
            List<FeatureMatch> matches, Matrix4x4 relativePose)
        {
            if (!_adj.ContainsKey(img1.Id))
                _adj[img1.Id] = new List<(Guid, List<FeatureMatch>, Matrix4x4)>();
            
            if (!_adj.ContainsKey(img2.Id))
                _adj[img2.Id] = new List<(Guid, List<FeatureMatch>, Matrix4x4)>();

            _adj[img1.Id].Add((img2.Id, matches, relativePose));
            
            Matrix4x4.Invert(relativePose, out var inversePose);
            _adj[img2.Id].Add((img1.Id, matches, inversePose));
        }

        public void RemoveNode(Guid nodeId)
        {
            if (_nodes.ContainsKey(nodeId))
            {
                _nodes.Remove(nodeId);
            }

            if (_adj.ContainsKey(nodeId))
            {
                // Remove edges from neighbors
                foreach (var (neighborId, _, _) in _adj[nodeId])
                {
                    if (_adj.ContainsKey(neighborId))
                    {
                        _adj[neighborId].RemoveAll(edge => edge.NeighborId == nodeId);
                    }
                }

                _adj.Remove(nodeId);
            }
        }

        public List<PhotogrammetryImageGroup> FindConnectedComponents()
        {
            var groups = new List<PhotogrammetryImageGroup>();
            var visited = new HashSet<Guid>();

            foreach (var node in _nodes.Values)
            {
                if (!visited.Contains(node.Id))
                {
                    var group = new PhotogrammetryImageGroup();
                    DepthFirstSearch(node.Id, visited, group);
                    groups.Add(group);
                }
            }

            return groups;
        }

        private void DepthFirstSearch(Guid nodeId, HashSet<Guid> visited, PhotogrammetryImageGroup group)
        {
            visited.Add(nodeId);
            group.Images.Add(_nodes[nodeId]);

            if (_adj.TryGetValue(nodeId, out var neighbors))
            {
                foreach (var (neighborId, _, _) in neighbors)
                {
                    if (!visited.Contains(neighborId))
                    {
                        DepthFirstSearch(neighborId, visited, group);
                    }
                }
            }
        }

        public IEnumerable<PhotogrammetryImage> GetNodes()
        {
            return _nodes.Values;
        }

        public bool TryGetNeighbors(Guid nodeId, 
            out List<(Guid NeighborId, List<FeatureMatch> Matches, Matrix4x4 Pose)> neighbors)
        {
            return _adj.TryGetValue(nodeId, out neighbors);
        }
    }

    /// <summary>
    /// Represents a connected group of images
    /// </summary>
    public class PhotogrammetryImageGroup
    {
        public List<PhotogrammetryImage> Images { get; } = new List<PhotogrammetryImage>();
        public string Name => $"Group ({Images.Count} images)";
    }
}