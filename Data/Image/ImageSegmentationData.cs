// GeoscientistToolkit/Data/Image/ImageSegmentationData.cs

using System.Numerics;

namespace GeoscientistToolkit.Data.Image;

/// <summary>
///     Manages segmentation data (labels and materials) for a 2D image
/// </summary>
public class ImageSegmentationData : IDisposable
{
    private const int MaxUndoSteps = 20;

    public ImageSegmentationData(int width, int height)
    {
        Width = width;
        Height = height;
        LabelData = new byte[width * height];
        Materials = new List<Material>();
        UndoStack = new Stack<byte[]>();
        RedoStack = new Stack<byte[]>();

        // Always add exterior material
        Materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)) { IsExterior = true });
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public byte[] LabelData { get; private set; }
    public List<Material> Materials { get; }
    public Stack<byte[]> UndoStack { get; }
    public Stack<byte[]> RedoStack { get; }

    public void Dispose()
    {
        LabelData = null;
        Materials?.Clear();
        UndoStack?.Clear();
        RedoStack?.Clear();
    }

    public void SaveUndoState()
    {
        var state = new byte[LabelData.Length];
        Array.Copy(LabelData, state, LabelData.Length);
        UndoStack.Push(state);

        if (UndoStack.Count > MaxUndoSteps)
        {
            var items = UndoStack.ToArray();
            UndoStack.Clear();
            for (var i = 0; i < MaxUndoSteps; i++) UndoStack.Push(items[i]);
        }

        RedoStack.Clear();
    }

    public void Undo()
    {
        if (UndoStack.Count == 0) return;

        var currentState = new byte[LabelData.Length];
        Array.Copy(LabelData, currentState, LabelData.Length);
        RedoStack.Push(currentState);

        LabelData = UndoStack.Pop();
    }

    public void Redo()
    {
        if (RedoStack.Count == 0) return;

        var currentState = new byte[LabelData.Length];
        Array.Copy(LabelData, currentState, LabelData.Length);
        UndoStack.Push(currentState);

        LabelData = RedoStack.Pop();
    }

    public byte GetNextMaterialID()
    {
        for (byte id = 1; id < 255; id++)
            if (!Materials.Any(m => m.ID == id))
                return id;
        throw new InvalidOperationException("No available material IDs");
    }

    public Material AddMaterial(string name, Vector4 color)
    {
        var id = GetNextMaterialID();
        var material = new Material(id, name, color);
        Materials.Add(material);
        return material;
    }

    public void RemoveMaterial(byte id)
    {
        if (id == 0) return; // Can't remove exterior

        Materials.RemoveAll(m => m.ID == id);

        // Clear all labels with this material
        for (var i = 0; i < LabelData.Length; i++)
            if (LabelData[i] == id)
                LabelData[i] = 0;
    }

    public Material GetMaterial(byte id)
    {
        return Materials.FirstOrDefault(m => m.ID == id);
    }

    public void Clear()
    {
        Array.Clear(LabelData, 0, LabelData.Length);
    }
}