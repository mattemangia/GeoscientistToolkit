// GeoscientistToolkit/Business/Stratigraphies/GermanStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     German / Swiss regional stratigraphy with widely used local units.
///     Includes: Quaternary Alpine glacial stages; Alpine Foreland Molasse (UMM/USM/OMM/OSM);
///     classic Germanic Triassic (Buntsandstein–Muschelkalk–Keuper) with sub-stages;
///     South German Jurassic (Lias/Dogger/Malm) with alpha–zeta letters; Zechstein cycles (Z1–Z4);
///     Ruhrkarbon (Namurian/Westphalian subdivisions).
/// </summary>
public class GermanStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public GermanStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "German/Swiss Stratigraphy";
    public string LanguageCode => "de-DE";

    public List<StratigraphicUnit> GetAllUnits()
    {
        return _units;
    }

    public List<StratigraphicUnit> GetUnitsByLevel(StratigraphicLevel level)
    {
        return _units.Where(x => x.Level == level).ToList();
    }

    public StratigraphicUnit GetUnitByCode(string code)
    {
        return _units.FirstOrDefault(x => x.Code == code);
    }

    public List<StratigraphicUnit> GetUnitsInTimeRange(double startMa, double endMa)
    {
        return _units.Where(x => x.StartAge >= endMa && x.EndAge <= startMa).ToList();
    }

    private List<StratigraphicUnit> InitializeUnits()
    {
        var u = new List<StratigraphicUnit>();

        // =========================
        //      KÄNOZOIKUM
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Känozoikum", Code = "KZ", Level = StratigraphicLevel.Era, StartAge = 66.0, EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29), InternationalCorrelationCode = "CZ"
        });

        // --- QUARTÄR
        u.Add(new StratigraphicUnit
        {
            Name = "Quartär", Code = "Q-DE", Level = StratigraphicLevel.Period, StartAge = 2.58, EndAge = 0.0,
            Color = Color.FromArgb(249, 249, 127), ParentCode = "KZ", InternationalCorrelationCode = "Q"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Holozän", Code = "HOL-DE", Level = StratigraphicLevel.Epoch, StartAge = 0.0117, EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236), ParentCode = "Q-DE", InternationalCorrelationCode = "Q2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pleistozän", Code = "PLEIS-DE", Level = StratigraphicLevel.Epoch, StartAge = 2.58, EndAge = 0.0117,
            Color = Color.FromArgb(255, 242, 174), ParentCode = "Q-DE", InternationalCorrelationCode = "Q1"
        });

        // Alpine glacial/interglacial terminology
        u.Add(new StratigraphicUnit
        {
            Name = "Würm-Kaltzeit", Code = "WURM", Level = StratigraphicLevel.Age, StartAge = 0.115, EndAge = 0.0117,
            Color = Color.FromArgb(255, 247, 199), ParentCode = "PLEIS-DE", InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Eem-Warmzeit", Code = "EEM", Level = StratigraphicLevel.Age, StartAge = 0.126, EndAge = 0.115,
            Color = Color.FromArgb(255, 245, 189), ParentCode = "PLEIS-DE", InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Riss-Kaltzeit", Code = "RISS", Level = StratigraphicLevel.Age, StartAge = 0.230, EndAge = 0.126,
            Color = Color.FromArgb(255, 243, 179), ParentCode = "PLEIS-DE", InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Holstein-Warmzeit", Code = "HOLSTEIN", Level = StratigraphicLevel.Age, StartAge = 0.424,
            EndAge = 0.230, Color = Color.FromArgb(255, 241, 169), ParentCode = "PLEIS-DE",
            InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mindel-Kaltzeit", Code = "MINDEL", Level = StratigraphicLevel.Age, StartAge = 0.850, EndAge = 0.424,
            Color = Color.FromArgb(255, 239, 159), ParentCode = "PLEIS-DE", InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Günz-Kaltzeit", Code = "GUNZ", Level = StratigraphicLevel.Age, StartAge = 1.10, EndAge = 0.850,
            Color = Color.FromArgb(255, 237, 149), ParentCode = "PLEIS-DE", InternationalCorrelationCode = "Q1"
        });

        // --- NEOGEN (with Molasse of the Alpine Foreland)
        u.Add(new StratigraphicUnit
        {
            Name = "Neogen", Code = "N-DE", Level = StratigraphicLevel.Period, StartAge = 23.03, EndAge = 2.58,
            Color = Color.FromArgb(255, 230, 25), ParentCode = "KZ", InternationalCorrelationCode = "N"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pliozän", Code = "PLIO-DE", Level = StratigraphicLevel.Epoch, StartAge = 5.333, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 153), ParentCode = "N-DE", InternationalCorrelationCode = "N2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Miozän", Code = "MIO-DE", Level = StratigraphicLevel.Epoch, StartAge = 23.03, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 0), ParentCode = "N-DE", InternationalCorrelationCode = "N1"
        });

        // Alpine Foreland Molasse – regional series
        u.Add(new StratigraphicUnit
        {
            Name = "Obere Süßwassermolasse (OSM)", Code = "OSM", Level = StratigraphicLevel.Age, StartAge = 13.0,
            EndAge = 5.3, Color = Color.FromArgb(255, 250, 128), ParentCode = "MIO-DE",
            InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Obere Meeresmolasse (OMM)", Code = "OMM", Level = StratigraphicLevel.Age, StartAge = 16.0,
            EndAge = 13.0, Color = Color.FromArgb(255, 247, 102), ParentCode = "MIO-DE",
            InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Untere Süßwassermolasse (USM)", Code = "USM", Level = StratigraphicLevel.Age, StartAge = 20.0,
            EndAge = 16.0, Color = Color.FromArgb(255, 244, 77), ParentCode = "MIO-DE",
            InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Untere Meeresmolasse (UMM)", Code = "UMM", Level = StratigraphicLevel.Age, StartAge = 23.0,
            EndAge = 20.0, Color = Color.FromArgb(255, 240, 51), ParentCode = "MIO-DE",
            InternationalCorrelationCode = "N1"
        });

        // Neogene international stages (useful for correlation)
        u.Add(new StratigraphicUnit
        {
            Name = "Piacenzium", Code = "PIAC", Level = StratigraphicLevel.Age, StartAge = 3.60, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 178), ParentCode = "PLIO-DE", InternationalCorrelationCode = "N2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Zancleum", Code = "ZANC", Level = StratigraphicLevel.Age, StartAge = 5.333, EndAge = 3.60,
            Color = Color.FromArgb(255, 255, 191), ParentCode = "PLIO-DE", InternationalCorrelationCode = "N2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Messinium", Code = "MESS", Level = StratigraphicLevel.Age, StartAge = 7.246, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 204), ParentCode = "MIO-DE", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Tortonium", Code = "TORT", Level = StratigraphicLevel.Age, StartAge = 11.63, EndAge = 7.246,
            Color = Color.FromArgb(255, 255, 216), ParentCode = "MIO-DE", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Serravallium", Code = "SERR", Level = StratigraphicLevel.Age, StartAge = 13.82, EndAge = 11.63,
            Color = Color.FromArgb(255, 255, 229), ParentCode = "MIO-DE", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Langhium", Code = "LANG", Level = StratigraphicLevel.Age, StartAge = 15.97, EndAge = 13.82,
            Color = Color.FromArgb(255, 255, 240), ParentCode = "MIO-DE", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Burdigalium", Code = "BURD", Level = StratigraphicLevel.Age, StartAge = 20.44, EndAge = 15.97,
            Color = Color.FromArgb(255, 255, 230), ParentCode = "MIO-DE", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aquitanium", Code = "AQUI", Level = StratigraphicLevel.Age, StartAge = 23.03, EndAge = 20.44,
            Color = Color.FromArgb(255, 255, 220), ParentCode = "MIO-DE", InternationalCorrelationCode = "N1"
        });

        // --- PALÄOGEN
        u.Add(new StratigraphicUnit
        {
            Name = "Paläogen", Code = "PG-DE", Level = StratigraphicLevel.Period, StartAge = 66.0, EndAge = 23.03,
            Color = Color.FromArgb(253, 154, 82), ParentCode = "KZ", InternationalCorrelationCode = "Pg"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oligozän", Code = "OLIG-DE", Level = StratigraphicLevel.Epoch, StartAge = 33.9, EndAge = 23.03,
            Color = Color.FromArgb(254, 192, 122), ParentCode = "PG-DE", InternationalCorrelationCode = "Pg3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Eozän", Code = "EOC-DE", Level = StratigraphicLevel.Epoch, StartAge = 56.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 180, 108), ParentCode = "PG-DE", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Paläozän", Code = "PALEO-DE", Level = StratigraphicLevel.Epoch, StartAge = 66.0, EndAge = 56.0,
            Color = Color.FromArgb(253, 167, 95), ParentCode = "PG-DE", InternationalCorrelationCode = "Pg1"
        });

        // Paleogene/Neogene stages (German usage: Rupel/Chatt etc.)
        u.Add(new StratigraphicUnit
        {
            Name = "Chattium", Code = "CHATT", Level = StratigraphicLevel.Age, StartAge = 27.82, EndAge = 23.03,
            Color = Color.FromArgb(254, 210, 155), ParentCode = "OLIG-DE", InternationalCorrelationCode = "Pg3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Rupelium", Code = "RUPE", Level = StratigraphicLevel.Age, StartAge = 33.9, EndAge = 27.82,
            Color = Color.FromArgb(254, 202, 140), ParentCode = "OLIG-DE", InternationalCorrelationCode = "Pg3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Priabonium", Code = "PRIAB", Level = StratigraphicLevel.Age, StartAge = 38.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 189, 123), ParentCode = "EOC-DE", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bartonium", Code = "BART", Level = StratigraphicLevel.Age, StartAge = 41.2, EndAge = 38.0,
            Color = Color.FromArgb(253, 196, 135), ParentCode = "EOC-DE", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lutetium", Code = "LUTET", Level = StratigraphicLevel.Age, StartAge = 47.8, EndAge = 41.2,
            Color = Color.FromArgb(253, 203, 146), ParentCode = "EOC-DE", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ypresium", Code = "YPRE", Level = StratigraphicLevel.Age, StartAge = 56.0, EndAge = 47.8,
            Color = Color.FromArgb(253, 210, 158), ParentCode = "EOC-DE", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Thanetium", Code = "THAN", Level = StratigraphicLevel.Age, StartAge = 59.2, EndAge = 56.0,
            Color = Color.FromArgb(253, 176, 106), ParentCode = "PALEO-DE", InternationalCorrelationCode = "Pg1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Selandium", Code = "SELA", Level = StratigraphicLevel.Age, StartAge = 61.6, EndAge = 59.2,
            Color = Color.FromArgb(253, 182, 114), ParentCode = "PALEO-DE", InternationalCorrelationCode = "Pg1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Danium", Code = "DAN", Level = StratigraphicLevel.Age, StartAge = 66.0, EndAge = 61.6,
            Color = Color.FromArgb(253, 188, 123), ParentCode = "PALEO-DE", InternationalCorrelationCode = "Pg1"
        });

        // =========================
        //        MESOZOIKUM
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Mesozoikum", Code = "MZ-DE", Level = StratigraphicLevel.Era, StartAge = 252.17, EndAge = 66.0,
            Color = Color.FromArgb(103, 197, 202), InternationalCorrelationCode = "MZ"
        });

        // --- Kreide
        u.Add(new StratigraphicUnit
        {
            Name = "Kreide", Code = "K-DE", Level = StratigraphicLevel.Period, StartAge = 145.0, EndAge = 66.0,
            Color = Color.FromArgb(127, 198, 78), ParentCode = "MZ-DE", InternationalCorrelationCode = "K"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oberkreide", Code = "KO", Level = StratigraphicLevel.Epoch, StartAge = 100.5, EndAge = 66.0,
            Color = Color.FromArgb(166, 216, 74), ParentCode = "K-DE", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Unterkreide", Code = "KU", Level = StratigraphicLevel.Epoch, StartAge = 145.0, EndAge = 100.5,
            Color = Color.FromArgb(140, 205, 87), ParentCode = "K-DE", InternationalCorrelationCode = "K1"
        });

        // Cretaceous international stages (widely used in Germany; improve column density)
        // Oberkreide
        u.Add(new StratigraphicUnit
        {
            Name = "Maastrichtium", Code = "MAAS", Level = StratigraphicLevel.Age, StartAge = 72.1, EndAge = 66.0,
            Color = Color.FromArgb(175, 219, 86), ParentCode = "KO", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Campanium", Code = "CAMP", Level = StratigraphicLevel.Age, StartAge = 83.6, EndAge = 72.1,
            Color = Color.FromArgb(171, 217, 82), ParentCode = "KO", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Santonium", Code = "SANT", Level = StratigraphicLevel.Age, StartAge = 86.3, EndAge = 83.6,
            Color = Color.FromArgb(168, 216, 78), ParentCode = "KO", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Coniacium", Code = "CONI", Level = StratigraphicLevel.Age, StartAge = 89.8, EndAge = 86.3,
            Color = Color.FromArgb(164, 215, 74), ParentCode = "KO", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Turonium", Code = "TURO", Level = StratigraphicLevel.Age, StartAge = 93.9, EndAge = 89.8,
            Color = Color.FromArgb(160, 212, 71), ParentCode = "KO", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cenomanium", Code = "CENO", Level = StratigraphicLevel.Age, StartAge = 100.5, EndAge = 93.9,
            Color = Color.FromArgb(156, 210, 68), ParentCode = "KO", InternationalCorrelationCode = "K2"
        });
        // Unterkreide
        u.Add(new StratigraphicUnit
        {
            Name = "Albien", Code = "ALB", Level = StratigraphicLevel.Age, StartAge = 113.0, EndAge = 100.5,
            Color = Color.FromArgb(150, 207, 103), ParentCode = "KU", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aptien", Code = "APT", Level = StratigraphicLevel.Age, StartAge = 125.0, EndAge = 113.0,
            Color = Color.FromArgb(146, 206, 96), ParentCode = "KU", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Barremium", Code = "BARR", Level = StratigraphicLevel.Age, StartAge = 129.4, EndAge = 125.0,
            Color = Color.FromArgb(144, 206, 93), ParentCode = "KU", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hauterivium", Code = "HAUT", Level = StratigraphicLevel.Age, StartAge = 132.9, EndAge = 129.4,
            Color = Color.FromArgb(142, 205, 90), ParentCode = "KU", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Valanginium", Code = "VALA", Level = StratigraphicLevel.Age, StartAge = 139.8, EndAge = 132.9,
            Color = Color.FromArgb(140, 205, 87), ParentCode = "KU", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Berriasium", Code = "BERR", Level = StratigraphicLevel.Age, StartAge = 145.0, EndAge = 139.8,
            Color = Color.FromArgb(138, 203, 85), ParentCode = "KU", InternationalCorrelationCode = "K1"
        });

        // --- Jura (South German Lias/Dogger/Malm with alpha–zeta letters)
        u.Add(new StratigraphicUnit
        {
            Name = "Jura", Code = "J-DE", Level = StratigraphicLevel.Period, StartAge = 201.4, EndAge = 145.0,
            Color = Color.FromArgb(52, 178, 201), ParentCode = "MZ-DE", InternationalCorrelationCode = "J"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Malm (Weißjura)", Code = "JM", Level = StratigraphicLevel.Epoch, StartAge = 163.5, EndAge = 145.0,
            Color = Color.FromArgb(179, 227, 238), ParentCode = "J-DE", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Dogger (Braunjura)", Code = "JD", Level = StratigraphicLevel.Epoch, StartAge = 174.7,
            EndAge = 163.5, Color = Color.FromArgb(128, 207, 216), ParentCode = "J-DE",
            InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lias (Schwarzjura)", Code = "JL", Level = StratigraphicLevel.Epoch, StartAge = 201.4,
            EndAge = 174.7, Color = Color.FromArgb(66, 174, 208), ParentCode = "J-DE",
            InternationalCorrelationCode = "J1"
        });

        // Lias α–ζ
        u.Add(new StratigraphicUnit
        {
            Name = "Lias α", Code = "JL-α", Level = StratigraphicLevel.Age, StartAge = 201.4, EndAge = 199.3,
            Color = Color.FromArgb(70, 178, 212), ParentCode = "JL", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lias β", Code = "JL-β", Level = StratigraphicLevel.Age, StartAge = 199.3, EndAge = 197.0,
            Color = Color.FromArgb(72, 180, 214), ParentCode = "JL", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lias γ", Code = "JL-γ", Level = StratigraphicLevel.Age, StartAge = 197.0, EndAge = 193.0,
            Color = Color.FromArgb(74, 182, 216), ParentCode = "JL", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lias δ", Code = "JL-δ", Level = StratigraphicLevel.Age, StartAge = 193.0, EndAge = 190.8,
            Color = Color.FromArgb(76, 184, 218), ParentCode = "JL", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lias ε", Code = "JL-ε", Level = StratigraphicLevel.Age, StartAge = 190.8, EndAge = 187.0,
            Color = Color.FromArgb(78, 186, 220), ParentCode = "JL", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lias ζ", Code = "JL-ζ", Level = StratigraphicLevel.Age, StartAge = 187.0, EndAge = 174.7,
            Color = Color.FromArgb(80, 188, 222), ParentCode = "JL", InternationalCorrelationCode = "J1"
        });

        // Dogger a–δ (South German usage)
        u.Add(new StratigraphicUnit
        {
            Name = "Dogger a", Code = "JD-a", Level = StratigraphicLevel.Age, StartAge = 174.7, EndAge = 171.6,
            Color = Color.FromArgb(130, 209, 218), ParentCode = "JD", InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Dogger b", Code = "JD-b", Level = StratigraphicLevel.Age, StartAge = 171.6, EndAge = 168.3,
            Color = Color.FromArgb(132, 211, 220), ParentCode = "JD", InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Dogger c", Code = "JD-c", Level = StratigraphicLevel.Age, StartAge = 168.3, EndAge = 166.1,
            Color = Color.FromArgb(134, 213, 222), ParentCode = "JD", InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Dogger d", Code = "JD-d", Level = StratigraphicLevel.Age, StartAge = 166.1, EndAge = 163.5,
            Color = Color.FromArgb(136, 215, 224), ParentCode = "JD", InternationalCorrelationCode = "J2"
        });

        // Malm α–ζ
        u.Add(new StratigraphicUnit
        {
            Name = "Malm α", Code = "JM-α", Level = StratigraphicLevel.Age, StartAge = 163.5, EndAge = 161.2,
            Color = Color.FromArgb(181, 229, 240), ParentCode = "JM", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Malm β", Code = "JM-β", Level = StratigraphicLevel.Age, StartAge = 161.2, EndAge = 158.9,
            Color = Color.FromArgb(183, 231, 242), ParentCode = "JM", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Malm γ", Code = "JM-γ", Level = StratigraphicLevel.Age, StartAge = 158.9, EndAge = 157.3,
            Color = Color.FromArgb(185, 233, 244), ParentCode = "JM", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Malm δ", Code = "JM-δ", Level = StratigraphicLevel.Age, StartAge = 157.3, EndAge = 154.8,
            Color = Color.FromArgb(187, 235, 246), ParentCode = "JM", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Malm ε", Code = "JM-ε", Level = StratigraphicLevel.Age, StartAge = 154.8, EndAge = 152.1,
            Color = Color.FromArgb(189, 237, 248), ParentCode = "JM", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Malm ζ", Code = "JM-ζ", Level = StratigraphicLevel.Age, StartAge = 152.1, EndAge = 145.0,
            Color = Color.FromArgb(191, 239, 250), ParentCode = "JM", InternationalCorrelationCode = "J3"
        });

        // --- Trias (Germanic Triassic with sub-stages)
        u.Add(new StratigraphicUnit
        {
            Name = "Trias", Code = "TR-DE", Level = StratigraphicLevel.Period, StartAge = 252.17, EndAge = 201.4,
            Color = Color.FromArgb(129, 43, 146), ParentCode = "MZ-DE", InternationalCorrelationCode = "T"
        });

        // Keuper (T3)
        u.Add(new StratigraphicUnit
        {
            Name = "Keuper", Code = "KEUPER", Level = StratigraphicLevel.Epoch, StartAge = 237.0, EndAge = 201.4,
            Color = Color.FromArgb(189, 140, 195), ParentCode = "TR-DE", InternationalCorrelationCode = "T3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oberer Keuper (Rhätkeuper)", Code = "KEUPER-O", Level = StratigraphicLevel.Age, StartAge = 208.5,
            EndAge = 201.4, Color = Color.FromArgb(227, 185, 219), ParentCode = "KEUPER",
            InternationalCorrelationCode = "T3c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mittlerer Keuper (Steinmergelkeuper)", Code = "KEUPER-M", Level = StratigraphicLevel.Age,
            StartAge = 227.0, EndAge = 208.5, Color = Color.FromArgb(214, 170, 211), ParentCode = "KEUPER",
            InternationalCorrelationCode = "T3b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Unterer Keuper (Gipskeuper)", Code = "KEUPER-U", Level = StratigraphicLevel.Age, StartAge = 237.0,
            EndAge = 227.0, Color = Color.FromArgb(201, 155, 203), ParentCode = "KEUPER",
            InternationalCorrelationCode = "T3a"
        });

        // Muschelkalk (T2)
        u.Add(new StratigraphicUnit
        {
            Name = "Muschelkalk", Code = "MUSCH", Level = StratigraphicLevel.Epoch, StartAge = 247.2, EndAge = 237.0,
            Color = Color.FromArgb(177, 104, 177), ParentCode = "TR-DE", InternationalCorrelationCode = "T2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oberer Muschelkalk", Code = "MUSCH-O", Level = StratigraphicLevel.Age, StartAge = 242.0,
            EndAge = 237.0, Color = Color.FromArgb(201, 131, 191), ParentCode = "MUSCH",
            InternationalCorrelationCode = "T2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mittlerer Muschelkalk", Code = "MUSCH-M", Level = StratigraphicLevel.Age, StartAge = 244.5,
            EndAge = 242.0, Color = Color.FromArgb(194, 124, 186), ParentCode = "MUSCH",
            InternationalCorrelationCode = "T2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Unterer Muschelkalk", Code = "MUSCH-U", Level = StratigraphicLevel.Age, StartAge = 247.2,
            EndAge = 244.5, Color = Color.FromArgb(188, 117, 183), ParentCode = "MUSCH",
            InternationalCorrelationCode = "T2a"
        });

        // Buntsandstein (T1)
        u.Add(new StratigraphicUnit
        {
            Name = "Buntsandstein", Code = "BUNT", Level = StratigraphicLevel.Epoch, StartAge = 252.17, EndAge = 247.2,
            Color = Color.FromArgb(152, 57, 153), ParentCode = "TR-DE", InternationalCorrelationCode = "T1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oberer Buntsandstein (Röt)", Code = "BUNT-O", Level = StratigraphicLevel.Age, StartAge = 249.5,
            EndAge = 247.2, Color = Color.FromArgb(176, 81, 165), ParentCode = "BUNT",
            InternationalCorrelationCode = "T1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mittlerer Buntsandstein", Code = "BUNT-M", Level = StratigraphicLevel.Age, StartAge = 251.0,
            EndAge = 249.5, Color = Color.FromArgb(170, 76, 161), ParentCode = "BUNT",
            InternationalCorrelationCode = "T1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Unterer Buntsandstein", Code = "BUNT-U", Level = StratigraphicLevel.Age, StartAge = 252.17,
            EndAge = 251.0, Color = Color.FromArgb(164, 70, 159), ParentCode = "BUNT",
            InternationalCorrelationCode = "T1a"
        });

        // =========================
        //        PALÄOZOIKUM
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Paläozoikum", Code = "PZ-DE", Level = StratigraphicLevel.Era, StartAge = 541.0, EndAge = 252.17,
            Color = Color.FromArgb(153, 192, 141), InternationalCorrelationCode = "PZ"
        });

        // Perm mit Zechstein/Rotliegend
        u.Add(new StratigraphicUnit
        {
            Name = "Perm", Code = "P-DE", Level = StratigraphicLevel.Period, StartAge = 298.9, EndAge = 252.17,
            Color = Color.FromArgb(240, 64, 40), ParentCode = "PZ-DE", InternationalCorrelationCode = "P"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Zechstein", Code = "ZECH-DE", Level = StratigraphicLevel.Epoch, StartAge = 259.1, EndAge = 252.17,
            Color = Color.FromArgb(251, 167, 148), ParentCode = "P-DE", InternationalCorrelationCode = "P3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Rotliegend", Code = "ROTL-DE", Level = StratigraphicLevel.Epoch, StartAge = 298.9, EndAge = 259.1,
            Color = Color.FromArgb(252, 128, 104), ParentCode = "P-DE", InternationalCorrelationCode = "P1,P2"
        });

        // Zechstein Zyklen (Werra–Staßfurt–Leine–Aller)
        u.Add(new StratigraphicUnit
        {
            Name = "Z1 Werra", Code = "Z1", Level = StratigraphicLevel.Age, StartAge = 258.0, EndAge = 257.0,
            Color = Color.FromArgb(252, 180, 165), ParentCode = "ZECH-DE", InternationalCorrelationCode = "P3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Z2 Staßfurt", Code = "Z2", Level = StratigraphicLevel.Age, StartAge = 257.0, EndAge = 256.0,
            Color = Color.FromArgb(252, 174, 156), ParentCode = "ZECH-DE", InternationalCorrelationCode = "P3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Z3 Leine", Code = "Z3", Level = StratigraphicLevel.Age, StartAge = 256.0, EndAge = 254.5,
            Color = Color.FromArgb(252, 169, 149), ParentCode = "ZECH-DE", InternationalCorrelationCode = "P3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Z4 Aller", Code = "Z4", Level = StratigraphicLevel.Age, StartAge = 254.5, EndAge = 252.17,
            Color = Color.FromArgb(252, 164, 142), ParentCode = "ZECH-DE", InternationalCorrelationCode = "P3"
        });

        // Karbon – Ruhrkarbon regionale Serien
        u.Add(new StratigraphicUnit
        {
            Name = "Karbon", Code = "C-DE", Level = StratigraphicLevel.Period, StartAge = 358.9, EndAge = 298.9,
            Color = Color.FromArgb(103, 165, 153), ParentCode = "PZ-DE", InternationalCorrelationCode = "C"
        });

        // Oberkarbon (Silesium) – Namur/Westfal/Stefan mit Untereinheiten
        u.Add(new StratigraphicUnit
        {
            Name = "Silesium (Oberkarbon)", Code = "SILES-DE", Level = StratigraphicLevel.Epoch, StartAge = 323.2,
            EndAge = 298.9, Color = Color.FromArgb(153, 194, 181), ParentCode = "C-DE",
            InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Namur", Code = "NAMUR-DE", Level = StratigraphicLevel.Age, StartAge = 323.2, EndAge = 315.2,
            Color = Color.FromArgb(153, 194, 181), ParentCode = "SILES-DE", InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Westfal", Code = "WESTFAL-DE", Level = StratigraphicLevel.Age, StartAge = 315.2, EndAge = 303.7,
            Color = Color.FromArgb(166, 199, 183), ParentCode = "SILES-DE", InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Stefan", Code = "STEFAN-DE", Level = StratigraphicLevel.Age, StartAge = 303.7, EndAge = 298.9,
            Color = Color.FromArgb(179, 206, 197), ParentCode = "SILES-DE", InternationalCorrelationCode = "C3"
        });

        // Westfal A–D (Ruhrrevier)
        u.Add(new StratigraphicUnit
        {
            Name = "Westfal A", Code = "WEST-A", Level = StratigraphicLevel.Age, StartAge = 315.2, EndAge = 313.0,
            Color = Color.FromArgb(168, 201, 186), ParentCode = "WESTFAL-DE", InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Westfal B", Code = "WEST-B", Level = StratigraphicLevel.Age, StartAge = 313.0, EndAge = 309.0,
            Color = Color.FromArgb(170, 203, 188), ParentCode = "WESTFAL-DE", InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Westfal C", Code = "WEST-C", Level = StratigraphicLevel.Age, StartAge = 309.0, EndAge = 306.0,
            Color = Color.FromArgb(172, 205, 190), ParentCode = "WESTFAL-DE", InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Westfal D", Code = "WEST-D", Level = StratigraphicLevel.Age, StartAge = 306.0, EndAge = 303.7,
            Color = Color.FromArgb(174, 207, 192), ParentCode = "WESTFAL-DE", InternationalCorrelationCode = "C3"
        });

        // Unterkarbon (Dinantium)
        u.Add(new StratigraphicUnit
        {
            Name = "Dinantium (Unterkarbon)", Code = "DINAN-DE", Level = StratigraphicLevel.Epoch, StartAge = 358.9,
            EndAge = 323.2, Color = Color.FromArgb(115, 169, 156), ParentCode = "C-DE",
            InternationalCorrelationCode = "C1,C2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Visé", Code = "VISE-DE", Level = StratigraphicLevel.Age, StartAge = 346.7, EndAge = 323.2,
            Color = Color.FromArgb(140, 176, 165), ParentCode = "DINAN-DE", InternationalCorrelationCode = "C2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Tournai", Code = "TOURN-DE", Level = StratigraphicLevel.Age, StartAge = 358.9, EndAge = 346.7,
            Color = Color.FromArgb(127, 172, 159), ParentCode = "DINAN-DE", InternationalCorrelationCode = "C1"
        });

        // Devon – keep international epochs for correlation (Germany uses these widely)
        u.Add(new StratigraphicUnit
        {
            Name = "Devon", Code = "D-DE", Level = StratigraphicLevel.Period, StartAge = 419.2, EndAge = 358.9,
            Color = Color.FromArgb(203, 140, 55), ParentCode = "PZ-DE", InternationalCorrelationCode = "D"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oberdevon", Code = "DO-DE", Level = StratigraphicLevel.Epoch, StartAge = 382.7, EndAge = 358.9,
            Color = Color.FromArgb(241, 200, 104), ParentCode = "D-DE", InternationalCorrelationCode = "D3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mitteldevon", Code = "DM-DE", Level = StratigraphicLevel.Epoch, StartAge = 393.3, EndAge = 382.7,
            Color = Color.FromArgb(241, 225, 157), ParentCode = "D-DE", InternationalCorrelationCode = "D2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Unterdevon", Code = "DU-DE", Level = StratigraphicLevel.Epoch, StartAge = 419.2, EndAge = 393.3,
            Color = Color.FromArgb(229, 196, 104), ParentCode = "D-DE", InternationalCorrelationCode = "D1"
        });

        // Silur / Ordovizium / Kambrium
        u.Add(new StratigraphicUnit
        {
            Name = "Silur", Code = "S-DE", Level = StratigraphicLevel.Period, StartAge = 443.8, EndAge = 419.2,
            Color = Color.FromArgb(179, 225, 182), ParentCode = "PZ-DE", InternationalCorrelationCode = "S"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ordovizium", Code = "O-DE", Level = StratigraphicLevel.Period, StartAge = 485.4, EndAge = 443.8,
            Color = Color.FromArgb(0, 146, 112), ParentCode = "PZ-DE", InternationalCorrelationCode = "O"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Kambrium", Code = "CAM-DE", Level = StratigraphicLevel.Period, StartAge = 541.0, EndAge = 485.4,
            Color = Color.FromArgb(127, 160, 86), ParentCode = "PZ-DE", InternationalCorrelationCode = "Ꞓ"
        });

        // =========================
        //        PRÄKAMBRIUM
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Präkambrium", Code = "PKZ", Level = StratigraphicLevel.Era, StartAge = 4600.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 67, 112), InternationalCorrelationCode = "PC"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Proterozoikum", Code = "PROT-DE", Level = StratigraphicLevel.Eon, StartAge = 2500.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 104, 152), ParentCode = "PKZ", InternationalCorrelationCode = "Pt"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Archaikum", Code = "ARCH-DE", Level = StratigraphicLevel.Eon, StartAge = 4000.0, EndAge = 2500.0,
            Color = Color.FromArgb(240, 4, 127), ParentCode = "PKZ", InternationalCorrelationCode = "A"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hadaikum", Code = "HAD-DE", Level = StratigraphicLevel.Eon, StartAge = 4600.0, EndAge = 4000.0,
            Color = Color.FromArgb(174, 2, 126), ParentCode = "PKZ", InternationalCorrelationCode = "H"
        });

        // Optionally populate parent <-> child relationships if needed later
        // (Current viewer only queries levels; children list is not consumed.)

        return u;
    }
}