//GeoscientistToolkit/Business/Stratigraphies/MammalAgesStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     European Land Mammal Ages (ELMA) - Biostratigraphic ages based on mammal fossils
///     Used extensively in paleontology for correlation, especially in continental deposits
/// </summary>
public class MammalAgesStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public MammalAgesStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "European Land Mammal Ages (ELMA)";
    public string LanguageCode => "en-ELMA";

    public List<StratigraphicUnit> GetAllUnits()
    {
        return _units;
    }

    public List<StratigraphicUnit> GetUnitsByLevel(StratigraphicLevel level)
    {
        return _units.Where(u => u.Level == level).ToList();
    }

    public StratigraphicUnit GetUnitByCode(string code)
    {
        return _units.FirstOrDefault(u => u.Code == code);
    }

    public List<StratigraphicUnit> GetUnitsInTimeRange(double startMa, double endMa)
    {
        return _units.Where(u => u.StartAge >= endMa && u.EndAge <= startMa).ToList();
    }

    private List<StratigraphicUnit> InitializeUnits()
    {
        var units = new List<StratigraphicUnit>();

        // CENOZOIC - Mammal Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Cenozoic Mammal Ages",
            Code = "CZ-ELMA",
            Level = StratigraphicLevel.Era,
            StartAge = 66.0,
            EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29),
            InternationalCorrelationCode = "CZ"
        });

        // QUATERNARY MAMMAL AGES
        units.Add(new StratigraphicUnit
        {
            Name = "Holocene Fauna",
            Code = "ELMA-HOL",
            Level = StratigraphicLevel.Age,
            StartAge = 0.0117,
            EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Q1"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Late Pleistocene Fauna",
            Code = "ELMA-LP",
            Level = StratigraphicLevel.Age,
            StartAge = 0.129,
            EndAge = 0.0117,
            Color = Color.FromArgb(255, 249, 208),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Q2c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Middle Pleistocene Fauna",
            Code = "ELMA-MP",
            Level = StratigraphicLevel.Age,
            StartAge = 0.774,
            EndAge = 0.129,
            Color = Color.FromArgb(255, 246, 191),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Q2b"
        });

        // NEOGENE MAMMAL AGES - The Classic European Stages!

        // VILLAFRANCHIAN (Italian mammal age!)
        units.Add(new StratigraphicUnit
        {
            Name = "Villafranchian",
            Code = "ELMA-VILL",
            Level = StratigraphicLevel.Age,
            StartAge = 3.6,
            EndAge = 0.774,
            Color = Color.FromArgb(255, 242, 174),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Q2a1,Q2a2,N2b"
        });

        // RUSCINIAN (Named after Roussillon, France)
        units.Add(new StratigraphicUnit
        {
            Name = "Ruscinian",
            Code = "ELMA-RUSC",
            Level = StratigraphicLevel.Age,
            StartAge = 5.33,
            EndAge = 3.6,
            Color = Color.FromArgb(255, 255, 153),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "N2a"
        });

        // TUROLIAN (Named after Teruel, Spain)
        units.Add(new StratigraphicUnit
        {
            Name = "Turolian",
            Code = "ELMA-TURO",
            Level = StratigraphicLevel.Age,
            StartAge = 8.7,
            EndAge = 5.33,
            Color = Color.FromArgb(255, 255, 115),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "N1c3"
        });

        // VALLESIAN (Named after Vallès, Catalonia, Spain)
        units.Add(new StratigraphicUnit
        {
            Name = "Vallesian",
            Code = "ELMA-VALL",
            Level = StratigraphicLevel.Age,
            StartAge = 11.6,
            EndAge = 8.7,
            Color = Color.FromArgb(255, 255, 102),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "N1c2"
        });

        // ASTARACIAN (Named after Astarac, France)
        units.Add(new StratigraphicUnit
        {
            Name = "Astaracian",
            Code = "ELMA-ASTA",
            Level = StratigraphicLevel.Age,
            StartAge = 16.0,
            EndAge = 11.6,
            Color = Color.FromArgb(255, 255, 89),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "N1c1,N1b2"
        });

        // ORLEANIAN (Named after Orléans, France)
        units.Add(new StratigraphicUnit
        {
            Name = "Orleanian",
            Code = "ELMA-ORLE",
            Level = StratigraphicLevel.Age,
            StartAge = 20.5,
            EndAge = 16.0,
            Color = Color.FromArgb(255, 255, 77),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "N1b1"
        });

        // AGENIAN (Named after Agen, France)
        units.Add(new StratigraphicUnit
        {
            Name = "Agenian",
            Code = "ELMA-AGEN",
            Level = StratigraphicLevel.Age,
            StartAge = 23.8,
            EndAge = 20.5,
            Color = Color.FromArgb(255, 255, 0),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "N1a"
        });

        // PALEOGENE MAMMAL AGES

        // ARVERNIAN (Named after Auvergne, France)
        units.Add(new StratigraphicUnit
        {
            Name = "Arvernian",
            Code = "ELMA-ARVE",
            Level = StratigraphicLevel.Age,
            StartAge = 28.1,
            EndAge = 23.8,
            Color = Color.FromArgb(254, 217, 154),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg3b"
        });

        // SUEVIAN (Named after Swabia/Schwaben, Germany)
        units.Add(new StratigraphicUnit
        {
            Name = "Suevian",
            Code = "ELMA-SUEV",
            Level = StratigraphicLevel.Age,
            StartAge = 30.0,
            EndAge = 28.1,
            Color = Color.FromArgb(254, 205, 138),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg3a,Pg3b"
        });

        // HEADONIAN (Named after Headon Hill, Isle of Wight, UK)
        units.Add(new StratigraphicUnit
        {
            Name = "Headonian",
            Code = "ELMA-HEAD",
            Level = StratigraphicLevel.Age,
            StartAge = 33.9,
            EndAge = 30.0,
            Color = Color.FromArgb(254, 192, 122),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg3a"
        });

        // ROBIACIAN (Named after Robiac, France)
        units.Add(new StratigraphicUnit
        {
            Name = "Robiacian",
            Code = "ELMA-ROBI",
            Level = StratigraphicLevel.Age,
            StartAge = 37.2,
            EndAge = 33.9,
            Color = Color.FromArgb(253, 205, 161),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg2c"
        });

        // BARTONIAN Fauna
        units.Add(new StratigraphicUnit
        {
            Name = "Bartonian Fauna",
            Code = "ELMA-BART",
            Level = StratigraphicLevel.Age,
            StartAge = 41.2,
            EndAge = 37.2,
            Color = Color.FromArgb(253, 196, 145),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg2b2"
        });

        // LUTETIAN Fauna (Lutétien - Paris Basin fauna!)
        units.Add(new StratigraphicUnit
        {
            Name = "Lutetian Fauna",
            Code = "ELMA-LUTE",
            Level = StratigraphicLevel.Age,
            StartAge = 47.8,
            EndAge = 41.2,
            Color = Color.FromArgb(253, 187, 130),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg2b1"
        });

        // CUISIEN (Named after Cuise-la-Motte, France)
        units.Add(new StratigraphicUnit
        {
            Name = "Cuisian",
            Code = "ELMA-CUIS",
            Level = StratigraphicLevel.Age,
            StartAge = 50.0,
            EndAge = 47.8,
            Color = Color.FromArgb(253, 183, 115),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg2a"
        });

        // SPARNACIAN (Named after Épernay, France - Sparnacum in Latin)
        units.Add(new StratigraphicUnit
        {
            Name = "Sparnacian",
            Code = "ELMA-SPAR",
            Level = StratigraphicLevel.Age,
            StartAge = 56.0,
            EndAge = 50.0,
            Color = Color.FromArgb(253, 180, 98),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg2a"
        });

        // THANETIAN Fauna
        units.Add(new StratigraphicUnit
        {
            Name = "Thanetian Fauna",
            Code = "ELMA-THAN",
            Level = StratigraphicLevel.Age,
            StartAge = 59.2,
            EndAge = 56.0,
            Color = Color.FromArgb(253, 191, 111),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg1c"
        });

        // MONTIAN (Named after Mons, Belgium)
        units.Add(new StratigraphicUnit
        {
            Name = "Montian",
            Code = "ELMA-MONT",
            Level = StratigraphicLevel.Age,
            StartAge = 61.6,
            EndAge = 59.2,
            Color = Color.FromArgb(253, 180, 108),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg1b"
        });

        // DANIAN Fauna
        units.Add(new StratigraphicUnit
        {
            Name = "Danian Fauna",
            Code = "ELMA-DANI",
            Level = StratigraphicLevel.Age,
            StartAge = 66.0,
            EndAge = 61.6,
            Color = Color.FromArgb(253, 167, 95),
            ParentCode = "CZ-ELMA",
            InternationalCorrelationCode = "Pg1a"
        });

        return units;
    }
}