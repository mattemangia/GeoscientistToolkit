//GeoscientistToolkit/Business/Stratigraphies/IStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     Represents a stratigraphic unit (Era, Period, Epoch, or Age)
/// </summary>
public class StratigraphicUnit
{
    public string Name { get; set; }
    public string Code { get; set; }
    public StratigraphicLevel Level { get; set; }
    public double StartAge { get; set; } // Ma (million years ago)
    public double EndAge { get; set; } // Ma
    public Color Color { get; set; }
    public string ParentCode { get; set; } // Code of parent unit
    public List<string> ChildCodes { get; set; } = new();

    /// <summary>
    ///     International correlation code for matching with other stratigraphies
    /// </summary>
    public string InternationalCorrelationCode { get; set; }
}

public enum StratigraphicLevel
{
    Eon,
    Era,
    Period,
    Epoch,
    Age
}

public interface IStratigraphy
{
    /// <summary>
    ///     Name of the stratigraphy (e.g., "International", "French", "German")
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Language/Region code (e.g., "en-INT", "fr-FR", "de-DE")
    /// </summary>
    string LanguageCode { get; }

    /// <summary>
    ///     Get all stratigraphic units
    /// </summary>
    List<StratigraphicUnit> GetAllUnits();

    /// <summary>
    ///     Get units by level (Era, Period, Epoch, Age)
    /// </summary>
    List<StratigraphicUnit> GetUnitsByLevel(StratigraphicLevel level);

    /// <summary>
    ///     Get a specific unit by code
    /// </summary>
    StratigraphicUnit GetUnitByCode(string code);

    /// <summary>
    ///     Find units that overlap with a given age range
    /// </summary>
    List<StratigraphicUnit> GetUnitsInTimeRange(double startMa, double endMa);
}