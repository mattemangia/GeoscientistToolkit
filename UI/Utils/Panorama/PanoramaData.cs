// GeoscientistToolkit/Business/Panorama/PanoramaData.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Data.Image;

namespace GeoscientistToolkit.Business.Panorama;

public enum PanoramaState
{
    Idle,
    Initializing,
    DetectingFeatures,
    MatchingFeatures,
    AwaitingManualInput,
    ReadyForPreview,
    Blending,
    Completed,
    Failed
}

public class PanoramaImage
{
    public ImageDataset Dataset { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public DetectedFeatures Features { get; set; }

    public PanoramaImage(ImageDataset dataset)
    {
        Dataset = dataset;
    }
}

public struct KeyPoint
{
    public float X, Y;
    public float Size;
    public float Angle;
    public int Octave;
}

public class DetectedFeatures
{
    public List<KeyPoint> KeyPoints { get; set; } = new();
    public byte[] Descriptors { get; set; } // Flattened array of 32-byte ORB descriptors
}

public class FeatureMatch
{
    public int QueryIndex { get; set; } // Index in the first image's feature list
    public int TrainIndex { get; set; } // Index in the second image's feature list
    public float Distance { get; set; }
}

public class StitchGraph
{
    private readonly Dictionary<Guid, PanoramaImage> _nodes = new();
    public readonly Dictionary<Guid, List<(Guid neighbor, List<FeatureMatch> matches, Matrix3x2 homography)>> _adj = new();

    public StitchGraph(IEnumerable<PanoramaImage> images)
    {
        foreach (var image in images)
        {
            _nodes.Add(image.Id, image);
            _adj.Add(image.Id, new List<(Guid, List<FeatureMatch>, Matrix3x2)>());
        }
    }

    public void AddEdge(PanoramaImage img1, PanoramaImage img2, List<FeatureMatch> matches, Matrix3x2 homography)
    {
        lock (_adj)
        {
            if (!_adj.ContainsKey(img1.Id) || !_adj.ContainsKey(img2.Id)) return;
            
            _adj[img1.Id].Add((img2.Id, matches, homography));
            
            if (Matrix3x2.Invert(homography, out var invHomography))
            {
                _adj[img2.Id].Add((img1.Id, matches, invHomography));
            }
        }
    }
    
    public void RemoveNode(Guid nodeId)
    {
        if (!_nodes.ContainsKey(nodeId)) return;

        lock (_adj)
        {
            _nodes.Remove(nodeId);
            _adj.Remove(nodeId);

            // Remove all edges from other nodes that point to the removed node
            foreach (var key in _adj.Keys)
            {
                _adj[key].RemoveAll(edge => edge.neighbor == nodeId);
            }
        }
    }
    
    public List<StitchGroup> FindConnectedComponents()
    {
        var groups = new List<StitchGroup>();
        var visited = new HashSet<Guid>();

        foreach (var nodeId in _nodes.Keys)
        {
            if (!visited.Contains(nodeId))
            {
                var group = new StitchGroup();
                var stack = new Stack<Guid>();
                
                stack.Push(nodeId);
                visited.Add(nodeId);

                while (stack.Count > 0)
                {
                    var currentId = stack.Pop();
                    group.Images.Add(_nodes[currentId]);
                    
                    if (_adj.TryGetValue(currentId, out var neighbors))
                    {
                        foreach (var neighbor in neighbors)
                        {
                            if (!visited.Contains(neighbor.neighbor))
                            {
                                visited.Add(neighbor.neighbor);
                                stack.Push(neighbor.neighbor);
                            }
                        }
                    }
                }
                groups.Add(group);
            }
        }
        return groups;
    }
}

public class StitchGroup
{
    public List<PanoramaImage> Images { get; } = new();
}