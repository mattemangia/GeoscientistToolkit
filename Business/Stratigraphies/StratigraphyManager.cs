// GeoscientistToolkit/Business/Stratigraphies/StratigraphyManager.cs

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     Manages available stratigraphy systems and the currently active one.
/// </summary>
public class StratigraphyManager
{
    private static StratigraphyManager _instance;

    private StratigraphyManager()
    {
        // Manually load all known stratigraphy implementations.
        // This makes the system aware of all available charts without hardcoding them elsewhere.
        AvailableStratigraphies = new List<IStratigraphy>
        {
            new InternationalStratigraphy(),
            new FrenchStratigraphy(),
            new GermanStratigraphy(),
            new SpanishStratigraphy(),
            new ItalianStratigraphy(),
            new UkStratigraphy(),
            new MammalAgesStratigraphy(),
            new USStratigraphy()
        };

        // Set the International chart as the default on startup.
        CurrentStratigraphy = AvailableStratigraphies.FirstOrDefault(s => s.LanguageCode == "en-INT")
                              ?? AvailableStratigraphies.FirstOrDefault();
    }

    public static StratigraphyManager Instance => _instance ??= new StratigraphyManager();

    public List<IStratigraphy> AvailableStratigraphies { get; }
    public IStratigraphy CurrentStratigraphy { get; set; }

    /// <summary>
    ///     Gets a stratigraphic unit by its unique code from the currently active stratigraphy.
    /// </summary>
    /// <param name="code">The code of the unit to find.</param>
    /// <returns>The found StratigraphicUnit, or null if not found.</returns>
    public StratigraphicUnit GetUnitByCode(string code)
    {
        return CurrentStratigraphy?.GetUnitByCode(code);
    }
}