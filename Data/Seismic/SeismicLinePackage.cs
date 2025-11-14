// GeoscientistToolkit/Data/Seismic/SeismicLinePackage.cs

using System.Numerics;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Represents a package/group of seismic traces for organization and analysis
/// </summary>
public class SeismicLinePackage
{
    public string Name { get; set; } = "New Package";
    public int StartTrace { get; set; }
    public int EndTrace { get; set; }
    public bool IsVisible { get; set; } = true;
    public Vector4 Color { get; set; } = new Vector4(1, 1, 0, 1); // Yellow default
    public string Notes { get; set; } = "";

    public int TraceCount => Math.Max(0, EndTrace - StartTrace + 1);

    public bool ContainsTrace(int traceIndex)
    {
        return traceIndex >= StartTrace && traceIndex <= EndTrace;
    }

    public SeismicLinePackage Clone()
    {
        return new SeismicLinePackage
        {
            Name = Name,
            StartTrace = StartTrace,
            EndTrace = EndTrace,
            IsVisible = IsVisible,
            Color = Color,
            Notes = Notes
        };
    }
}
