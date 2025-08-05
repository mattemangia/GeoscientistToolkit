// GeoscientistToolkit/Data/CtImageStack/Segmentation/ISegmentationTool.cs
using System;
using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack.Segmentation
{
    public interface ISegmentationTool : IDisposable
    {
        string Name { get; }
        string Icon { get; }
        
        void Initialize(SegmentationManager manager);
        void StartSelection(Vector2 startPos, int sliceIndex, int viewIndex);
        void UpdateSelection(Vector2 currentPos);
        void EndSelection();
        void CancelSelection();
        
        bool HasActiveSelection { get; }
        byte[] GetSelectionMask();
    }
}