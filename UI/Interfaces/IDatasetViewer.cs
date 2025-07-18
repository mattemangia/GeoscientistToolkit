// GeoscientistToolkit/UI/Interfaces/IDatasetViewer.cs
using System.Numerics;

namespace GeoscientistToolkit.UI.Interfaces
{
    public interface IDatasetViewer:IDisposable
    {
        void DrawToolbarControls();
        void DrawContent(ref float zoom, ref Vector2 pan);
    }
}