using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     International Chronostratigraphic Chart (ICS 2024)
///     Based on International Commission on Stratigraphy
/// </summary>
public class InternationalStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public InternationalStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "International Chronostratigraphic Chart";
    public string LanguageCode => "en-INT";

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

        // CENOZOIC ERA (66 Ma - Present)
        units.Add(new StratigraphicUnit
        {
            Name = "Cenozoic",
            Code = "CZ",
            Level = StratigraphicLevel.Era,
            StartAge = 66.0,
            EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29),
            InternationalCorrelationCode = "CZ"
        });

        // Quaternary Period
        units.Add(new StratigraphicUnit
        {
            Name = "Quaternary",
            Code = "Q",
            Level = StratigraphicLevel.Period,
            StartAge = 2.58,
            EndAge = 0.0,
            Color = Color.FromArgb(249, 249, 127),
            ParentCode = "CZ",
            InternationalCorrelationCode = "Q"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Holocene",
            Code = "Q1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 0.0117,
            EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236),
            ParentCode = "Q",
            InternationalCorrelationCode = "Q1"
        });

        // Holocene Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Meghalayan",
            Code = "Q1c",
            Level = StratigraphicLevel.Age,
            StartAge = 0.0042,
            EndAge = 0.0,
            Color = Color.FromArgb(254, 250, 244),
            ParentCode = "Q1",
            InternationalCorrelationCode = "Q1c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Northgrippian",
            Code = "Q1b",
            Level = StratigraphicLevel.Age,
            StartAge = 0.0082,
            EndAge = 0.0042,
            Color = Color.FromArgb(254, 247, 240),
            ParentCode = "Q1",
            InternationalCorrelationCode = "Q1b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Greenlandian",
            Code = "Q1a",
            Level = StratigraphicLevel.Age,
            StartAge = 0.0117,
            EndAge = 0.0082,
            Color = Color.FromArgb(254, 242, 236),
            ParentCode = "Q1",
            InternationalCorrelationCode = "Q1a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Pleistocene",
            Code = "Q2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 2.58,
            EndAge = 0.0117,
            Color = Color.FromArgb(255, 242, 174),
            ParentCode = "Q",
            InternationalCorrelationCode = "Q2"
        });

        // Pleistocene Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Upper Pleistocene",
            Code = "Q2c",
            Level = StratigraphicLevel.Age,
            StartAge = 0.129,
            EndAge = 0.0117,
            Color = Color.FromArgb(255, 249, 208),
            ParentCode = "Q2",
            InternationalCorrelationCode = "Q2c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Middle Pleistocene (Chibanian)",
            Code = "Q2b",
            Level = StratigraphicLevel.Age,
            StartAge = 0.774,
            EndAge = 0.129,
            Color = Color.FromArgb(255, 246, 191),
            ParentCode = "Q2",
            InternationalCorrelationCode = "Q2b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Lower Pleistocene (Calabrian)",
            Code = "Q2a2",
            Level = StratigraphicLevel.Age,
            StartAge = 1.8,
            EndAge = 0.774,
            Color = Color.FromArgb(255, 244, 181),
            ParentCode = "Q2",
            InternationalCorrelationCode = "Q2a2"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Gelasian",
            Code = "Q2a1",
            Level = StratigraphicLevel.Age,
            StartAge = 2.58,
            EndAge = 1.8,
            Color = Color.FromArgb(255, 242, 174),
            ParentCode = "Q2",
            InternationalCorrelationCode = "Q2a1"
        });

        // Neogene Period
        units.Add(new StratigraphicUnit
        {
            Name = "Neogene",
            Code = "N",
            Level = StratigraphicLevel.Period,
            StartAge = 23.03,
            EndAge = 2.58,
            Color = Color.FromArgb(255, 249, 127),
            ParentCode = "CZ",
            InternationalCorrelationCode = "N"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Pliocene",
            Code = "N2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 5.33,
            EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 153),
            ParentCode = "N",
            InternationalCorrelationCode = "N2"
        });

        // Pliocene Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Piacenzian",
            Code = "N2b",
            Level = StratigraphicLevel.Age,
            StartAge = 3.6,
            EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 179),
            ParentCode = "N2",
            InternationalCorrelationCode = "N2b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Zanclean",
            Code = "N2a",
            Level = StratigraphicLevel.Age,
            StartAge = 5.33,
            EndAge = 3.6,
            Color = Color.FromArgb(255, 255, 153),
            ParentCode = "N2",
            InternationalCorrelationCode = "N2a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Miocene",
            Code = "N1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 23.03,
            EndAge = 5.33,
            Color = Color.FromArgb(255, 255, 0),
            ParentCode = "N",
            InternationalCorrelationCode = "N1"
        });

        // Miocene Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Messinian",
            Code = "N1c3",
            Level = StratigraphicLevel.Age,
            StartAge = 7.25,
            EndAge = 5.33,
            Color = Color.FromArgb(255, 255, 115),
            ParentCode = "N1",
            InternationalCorrelationCode = "N1c3"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Tortonian",
            Code = "N1c2",
            Level = StratigraphicLevel.Age,
            StartAge = 11.63,
            EndAge = 7.25,
            Color = Color.FromArgb(255, 255, 102),
            ParentCode = "N1",
            InternationalCorrelationCode = "N1c2"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Serravallian",
            Code = "N1c1",
            Level = StratigraphicLevel.Age,
            StartAge = 13.82,
            EndAge = 11.63,
            Color = Color.FromArgb(255, 255, 89),
            ParentCode = "N1",
            InternationalCorrelationCode = "N1c1"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Langhian",
            Code = "N1b2",
            Level = StratigraphicLevel.Age,
            StartAge = 15.97,
            EndAge = 13.82,
            Color = Color.FromArgb(255, 255, 77),
            ParentCode = "N1",
            InternationalCorrelationCode = "N1b2"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Burdigalian",
            Code = "N1b1",
            Level = StratigraphicLevel.Age,
            StartAge = 20.44,
            EndAge = 15.97,
            Color = Color.FromArgb(255, 255, 65),
            ParentCode = "N1",
            InternationalCorrelationCode = "N1b1"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Aquitanian",
            Code = "N1a",
            Level = StratigraphicLevel.Age,
            StartAge = 23.03,
            EndAge = 20.44,
            Color = Color.FromArgb(255, 255, 0),
            ParentCode = "N1",
            InternationalCorrelationCode = "N1a"
        });

        // Paleogene Period
        units.Add(new StratigraphicUnit
        {
            Name = "Paleogene",
            Code = "Pg",
            Level = StratigraphicLevel.Period,
            StartAge = 66.0,
            EndAge = 23.03,
            Color = Color.FromArgb(253, 154, 82),
            ParentCode = "CZ",
            InternationalCorrelationCode = "Pg"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Oligocene",
            Code = "Pg3",
            Level = StratigraphicLevel.Epoch,
            StartAge = 33.9,
            EndAge = 23.03,
            Color = Color.FromArgb(254, 192, 122),
            ParentCode = "Pg",
            InternationalCorrelationCode = "Pg3"
        });

        // Oligocene Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Chattian",
            Code = "Pg3b",
            Level = StratigraphicLevel.Age,
            StartAge = 27.82,
            EndAge = 23.03,
            Color = Color.FromArgb(254, 217, 154),
            ParentCode = "Pg3",
            InternationalCorrelationCode = "Pg3b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Rupelian",
            Code = "Pg3a",
            Level = StratigraphicLevel.Age,
            StartAge = 33.9,
            EndAge = 27.82,
            Color = Color.FromArgb(254, 192, 122),
            ParentCode = "Pg3",
            InternationalCorrelationCode = "Pg3a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Eocene",
            Code = "Pg2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 56.0,
            EndAge = 33.9,
            Color = Color.FromArgb(253, 180, 98),
            ParentCode = "Pg",
            InternationalCorrelationCode = "Pg2"
        });

        // Eocene Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Priabonian",
            Code = "Pg2c",
            Level = StratigraphicLevel.Age,
            StartAge = 37.71,
            EndAge = 33.9,
            Color = Color.FromArgb(253, 205, 161),
            ParentCode = "Pg2",
            InternationalCorrelationCode = "Pg2c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Bartonian",
            Code = "Pg2b2",
            Level = StratigraphicLevel.Age,
            StartAge = 41.2,
            EndAge = 37.71,
            Color = Color.FromArgb(253, 196, 145),
            ParentCode = "Pg2",
            InternationalCorrelationCode = "Pg2b2"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Lutetian",
            Code = "Pg2b1",
            Level = StratigraphicLevel.Age,
            StartAge = 47.8,
            EndAge = 41.2,
            Color = Color.FromArgb(253, 187, 130),
            ParentCode = "Pg2",
            InternationalCorrelationCode = "Pg2b1"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Ypresian",
            Code = "Pg2a",
            Level = StratigraphicLevel.Age,
            StartAge = 56.0,
            EndAge = 47.8,
            Color = Color.FromArgb(253, 180, 98),
            ParentCode = "Pg2",
            InternationalCorrelationCode = "Pg2a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Paleocene",
            Code = "Pg1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 66.0,
            EndAge = 56.0,
            Color = Color.FromArgb(253, 167, 95),
            ParentCode = "Pg",
            InternationalCorrelationCode = "Pg1"
        });

        // Paleocene Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Thanetian",
            Code = "Pg1c",
            Level = StratigraphicLevel.Age,
            StartAge = 59.2,
            EndAge = 56.0,
            Color = Color.FromArgb(253, 191, 111),
            ParentCode = "Pg1",
            InternationalCorrelationCode = "Pg1c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Selandian",
            Code = "Pg1b",
            Level = StratigraphicLevel.Age,
            StartAge = 61.6,
            EndAge = 59.2,
            Color = Color.FromArgb(253, 180, 108),
            ParentCode = "Pg1",
            InternationalCorrelationCode = "Pg1b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Danian",
            Code = "Pg1a",
            Level = StratigraphicLevel.Age,
            StartAge = 66.0,
            EndAge = 61.6,
            Color = Color.FromArgb(253, 167, 95),
            ParentCode = "Pg1",
            InternationalCorrelationCode = "Pg1a"
        });

        // MESOZOIC ERA (252-66 Ma)
        units.Add(new StratigraphicUnit
        {
            Name = "Mesozoic",
            Code = "MZ",
            Level = StratigraphicLevel.Era,
            StartAge = 252.17,
            EndAge = 66.0,
            Color = Color.FromArgb(103, 197, 202),
            InternationalCorrelationCode = "MZ"
        });

        // Cretaceous Period
        units.Add(new StratigraphicUnit
        {
            Name = "Cretaceous",
            Code = "K",
            Level = StratigraphicLevel.Period,
            StartAge = 145.0,
            EndAge = 66.0,
            Color = Color.FromArgb(127, 198, 78),
            ParentCode = "MZ",
            InternationalCorrelationCode = "K"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Upper Cretaceous",
            Code = "K2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 100.5,
            EndAge = 66.0,
            Color = Color.FromArgb(166, 216, 74),
            ParentCode = "K",
            InternationalCorrelationCode = "K2"
        });

        // Upper Cretaceous Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Maastrichtian",
            Code = "K2f",
            Level = StratigraphicLevel.Age,
            StartAge = 72.1,
            EndAge = 66.0,
            Color = Color.FromArgb(242, 250, 140),
            ParentCode = "K2",
            InternationalCorrelationCode = "K2f"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Campanian",
            Code = "K2e",
            Level = StratigraphicLevel.Age,
            StartAge = 83.6,
            EndAge = 72.1,
            Color = Color.FromArgb(230, 244, 127),
            ParentCode = "K2",
            InternationalCorrelationCode = "K2e"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Santonian",
            Code = "K2d",
            Level = StratigraphicLevel.Age,
            StartAge = 86.3,
            EndAge = 83.6,
            Color = Color.FromArgb(217, 238, 117),
            ParentCode = "K2",
            InternationalCorrelationCode = "K2d"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Coniacian",
            Code = "K2c",
            Level = StratigraphicLevel.Age,
            StartAge = 89.8,
            EndAge = 86.3,
            Color = Color.FromArgb(204, 233, 104),
            ParentCode = "K2",
            InternationalCorrelationCode = "K2c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Turonian",
            Code = "K2b",
            Level = StratigraphicLevel.Age,
            StartAge = 93.9,
            EndAge = 89.8,
            Color = Color.FromArgb(191, 227, 93),
            ParentCode = "K2",
            InternationalCorrelationCode = "K2b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Cenomanian",
            Code = "K2a",
            Level = StratigraphicLevel.Age,
            StartAge = 100.5,
            EndAge = 93.9,
            Color = Color.FromArgb(179, 222, 83),
            ParentCode = "K2",
            InternationalCorrelationCode = "K2a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Lower Cretaceous",
            Code = "K1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 145.0,
            EndAge = 100.5,
            Color = Color.FromArgb(140, 205, 87),
            ParentCode = "K",
            InternationalCorrelationCode = "K1"
        });

        // Lower Cretaceous Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Albian",
            Code = "K1f",
            Level = StratigraphicLevel.Age,
            StartAge = 113.0,
            EndAge = 100.5,
            Color = Color.FromArgb(204, 234, 151),
            ParentCode = "K1",
            InternationalCorrelationCode = "K1f"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Aptian",
            Code = "K1e",
            Level = StratigraphicLevel.Age,
            StartAge = 121.4,
            EndAge = 113.0,
            Color = Color.FromArgb(191, 228, 138),
            ParentCode = "K1",
            InternationalCorrelationCode = "K1e"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Barremian",
            Code = "K1d",
            Level = StratigraphicLevel.Age,
            StartAge = 125.77,
            EndAge = 121.4,
            Color = Color.FromArgb(179, 223, 127),
            ParentCode = "K1",
            InternationalCorrelationCode = "K1d"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Hauterivian",
            Code = "K1c",
            Level = StratigraphicLevel.Age,
            StartAge = 132.6,
            EndAge = 125.77,
            Color = Color.FromArgb(166, 217, 117),
            ParentCode = "K1",
            InternationalCorrelationCode = "K1c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Valanginian",
            Code = "K1b",
            Level = StratigraphicLevel.Age,
            StartAge = 139.8,
            EndAge = 132.6,
            Color = Color.FromArgb(153, 211, 106),
            ParentCode = "K1",
            InternationalCorrelationCode = "K1b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Berriasian",
            Code = "K1a",
            Level = StratigraphicLevel.Age,
            StartAge = 145.0,
            EndAge = 139.8,
            Color = Color.FromArgb(140, 205, 96),
            ParentCode = "K1",
            InternationalCorrelationCode = "K1a"
        });

        // Jurassic Period
        units.Add(new StratigraphicUnit
        {
            Name = "Jurassic",
            Code = "J",
            Level = StratigraphicLevel.Period,
            StartAge = 201.4,
            EndAge = 145.0,
            Color = Color.FromArgb(52, 178, 201),
            ParentCode = "MZ",
            InternationalCorrelationCode = "J"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Upper Jurassic",
            Code = "J3",
            Level = StratigraphicLevel.Epoch,
            StartAge = 163.5,
            EndAge = 145.0,
            Color = Color.FromArgb(179, 227, 238),
            ParentCode = "J",
            InternationalCorrelationCode = "J3"
        });

        // Upper Jurassic Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Tithonian",
            Code = "J3c",
            Level = StratigraphicLevel.Age,
            StartAge = 152.1,
            EndAge = 145.0,
            Color = Color.FromArgb(217, 241, 247),
            ParentCode = "J3",
            InternationalCorrelationCode = "J3c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Kimmeridgian",
            Code = "J3b",
            Level = StratigraphicLevel.Age,
            StartAge = 157.3,
            EndAge = 152.1,
            Color = Color.FromArgb(204, 236, 244),
            ParentCode = "J3",
            InternationalCorrelationCode = "J3b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Oxfordian",
            Code = "J3a",
            Level = StratigraphicLevel.Age,
            StartAge = 163.5,
            EndAge = 157.3,
            Color = Color.FromArgb(191, 231, 241),
            ParentCode = "J3",
            InternationalCorrelationCode = "J3a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Middle Jurassic",
            Code = "J2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 174.7,
            EndAge = 163.5,
            Color = Color.FromArgb(128, 207, 216),
            ParentCode = "J",
            InternationalCorrelationCode = "J2"
        });

        // Middle Jurassic Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Callovian",
            Code = "J2d",
            Level = StratigraphicLevel.Age,
            StartAge = 166.1,
            EndAge = 163.5,
            Color = Color.FromArgb(191, 226, 232),
            ParentCode = "J2",
            InternationalCorrelationCode = "J2d"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Bathonian",
            Code = "J2c",
            Level = StratigraphicLevel.Age,
            StartAge = 168.2,
            EndAge = 166.1,
            Color = Color.FromArgb(179, 220, 228),
            ParentCode = "J2",
            InternationalCorrelationCode = "J2c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Bajocian",
            Code = "J2b",
            Level = StratigraphicLevel.Age,
            StartAge = 170.9,
            EndAge = 168.2,
            Color = Color.FromArgb(166, 215, 225),
            ParentCode = "J2",
            InternationalCorrelationCode = "J2b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Aalenian",
            Code = "J2a",
            Level = StratigraphicLevel.Age,
            StartAge = 174.7,
            EndAge = 170.9,
            Color = Color.FromArgb(153, 209, 221),
            ParentCode = "J2",
            InternationalCorrelationCode = "J2a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Lower Jurassic",
            Code = "J1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 201.4,
            EndAge = 174.7,
            Color = Color.FromArgb(66, 174, 208),
            ParentCode = "J",
            InternationalCorrelationCode = "J1"
        });

        // Lower Jurassic Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Toarcian",
            Code = "J1d",
            Level = StratigraphicLevel.Age,
            StartAge = 184.2,
            EndAge = 174.7,
            Color = Color.FromArgb(153, 206, 227),
            ParentCode = "J1",
            InternationalCorrelationCode = "J1d"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Pliensbachian",
            Code = "J1c",
            Level = StratigraphicLevel.Age,
            StartAge = 192.9,
            EndAge = 184.2,
            Color = Color.FromArgb(128, 197, 221),
            ParentCode = "J1",
            InternationalCorrelationCode = "J1c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Sinemurian",
            Code = "J1b",
            Level = StratigraphicLevel.Age,
            StartAge = 199.5,
            EndAge = 192.9,
            Color = Color.FromArgb(103, 188, 216),
            ParentCode = "J1",
            InternationalCorrelationCode = "J1b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Hettangian",
            Code = "J1a",
            Level = StratigraphicLevel.Age,
            StartAge = 201.4,
            EndAge = 199.5,
            Color = Color.FromArgb(78, 179, 211),
            ParentCode = "J1",
            InternationalCorrelationCode = "J1a"
        });

        // Triassic Period
        units.Add(new StratigraphicUnit
        {
            Name = "Triassic",
            Code = "T",
            Level = StratigraphicLevel.Period,
            StartAge = 252.17,
            EndAge = 201.4,
            Color = Color.FromArgb(129, 43, 146),
            ParentCode = "MZ",
            InternationalCorrelationCode = "T"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Upper Triassic",
            Code = "T3",
            Level = StratigraphicLevel.Epoch,
            StartAge = 237.0,
            EndAge = 201.4,
            Color = Color.FromArgb(189, 140, 195),
            ParentCode = "T",
            InternationalCorrelationCode = "T3"
        });

        // Upper Triassic Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Rhaetian",
            Code = "T3c",
            Level = StratigraphicLevel.Age,
            StartAge = 208.5,
            EndAge = 201.4,
            Color = Color.FromArgb(227, 185, 219),
            ParentCode = "T3",
            InternationalCorrelationCode = "T3c"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Norian",
            Code = "T3b",
            Level = StratigraphicLevel.Age,
            StartAge = 227.0,
            EndAge = 208.5,
            Color = Color.FromArgb(214, 170, 211),
            ParentCode = "T3",
            InternationalCorrelationCode = "T3b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Carnian",
            Code = "T3a",
            Level = StratigraphicLevel.Age,
            StartAge = 237.0,
            EndAge = 227.0,
            Color = Color.FromArgb(201, 155, 203),
            ParentCode = "T3",
            InternationalCorrelationCode = "T3a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Middle Triassic",
            Code = "T2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 247.2,
            EndAge = 237.0,
            Color = Color.FromArgb(177, 104, 177),
            ParentCode = "T",
            InternationalCorrelationCode = "T2"
        });

        // Middle Triassic Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Ladinian",
            Code = "T2b",
            Level = StratigraphicLevel.Age,
            StartAge = 242.0,
            EndAge = 237.0,
            Color = Color.FromArgb(201, 131, 191),
            ParentCode = "T2",
            InternationalCorrelationCode = "T2b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Anisian",
            Code = "T2a",
            Level = StratigraphicLevel.Age,
            StartAge = 247.2,
            EndAge = 242.0,
            Color = Color.FromArgb(188, 117, 183),
            ParentCode = "T2",
            InternationalCorrelationCode = "T2a"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Lower Triassic",
            Code = "T1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 252.17,
            EndAge = 247.2,
            Color = Color.FromArgb(152, 57, 153),
            ParentCode = "T",
            InternationalCorrelationCode = "T1"
        });

        // Lower Triassic Ages
        units.Add(new StratigraphicUnit
        {
            Name = "Olenekian",
            Code = "T1b",
            Level = StratigraphicLevel.Age,
            StartAge = 251.2,
            EndAge = 247.2,
            Color = Color.FromArgb(176, 81, 165),
            ParentCode = "T1",
            InternationalCorrelationCode = "T1b"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Induan",
            Code = "T1a",
            Level = StratigraphicLevel.Age,
            StartAge = 252.17,
            EndAge = 251.2,
            Color = Color.FromArgb(164, 70, 159),
            ParentCode = "T1",
            InternationalCorrelationCode = "T1a"
        });

        // PALEOZOIC ERA (541-252 Ma)
        units.Add(new StratigraphicUnit
        {
            Name = "Paleozoic",
            Code = "PZ",
            Level = StratigraphicLevel.Era,
            StartAge = 541.0,
            EndAge = 252.17,
            Color = Color.FromArgb(153, 192, 141),
            InternationalCorrelationCode = "PZ"
        });

        // Permian Period
        units.Add(new StratigraphicUnit
        {
            Name = "Permian",
            Code = "P",
            Level = StratigraphicLevel.Period,
            StartAge = 298.9,
            EndAge = 252.17,
            Color = Color.FromArgb(240, 64, 40),
            ParentCode = "PZ",
            InternationalCorrelationCode = "P"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Lopingian",
            Code = "P3",
            Level = StratigraphicLevel.Epoch,
            StartAge = 259.1,
            EndAge = 252.17,
            Color = Color.FromArgb(251, 167, 148),
            ParentCode = "P",
            InternationalCorrelationCode = "P3"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Guadalupian",
            Code = "P2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 272.95,
            EndAge = 259.1,
            Color = Color.FromArgb(251, 116, 92),
            ParentCode = "P",
            InternationalCorrelationCode = "P2"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Cisuralian",
            Code = "P1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 298.9,
            EndAge = 272.95,
            Color = Color.FromArgb(239, 88, 69),
            ParentCode = "P",
            InternationalCorrelationCode = "P1"
        });

        // Carboniferous Period
        units.Add(new StratigraphicUnit
        {
            Name = "Carboniferous",
            Code = "C",
            Level = StratigraphicLevel.Period,
            StartAge = 358.9,
            EndAge = 298.9,
            Color = Color.FromArgb(103, 165, 153),
            ParentCode = "PZ",
            InternationalCorrelationCode = "C"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Pennsylvanian",
            Code = "C3",
            Level = StratigraphicLevel.Epoch,
            StartAge = 323.2,
            EndAge = 298.9,
            Color = Color.FromArgb(153, 194, 181),
            ParentCode = "C",
            InternationalCorrelationCode = "C3"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Mississippian",
            Code = "C1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 358.9,
            EndAge = 323.2,
            Color = Color.FromArgb(103, 143, 102),
            ParentCode = "C",
            InternationalCorrelationCode = "C1"
        });

        // Devonian Period
        units.Add(new StratigraphicUnit
        {
            Name = "Devonian",
            Code = "D",
            Level = StratigraphicLevel.Period,
            StartAge = 419.2,
            EndAge = 358.9,
            Color = Color.FromArgb(203, 140, 55),
            ParentCode = "PZ",
            InternationalCorrelationCode = "D"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Upper Devonian",
            Code = "D3",
            Level = StratigraphicLevel.Epoch,
            StartAge = 382.7,
            EndAge = 358.9,
            Color = Color.FromArgb(241, 225, 157),
            ParentCode = "D",
            InternationalCorrelationCode = "D3"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Middle Devonian",
            Code = "D2",
            Level = StratigraphicLevel.Epoch,
            StartAge = 393.3,
            EndAge = 382.7,
            Color = Color.FromArgb(241, 200, 104),
            ParentCode = "D",
            InternationalCorrelationCode = "D2"
        });

        units.Add(new StratigraphicUnit
        {
            Name = "Lower Devonian",
            Code = "D1",
            Level = StratigraphicLevel.Epoch,
            StartAge = 419.2,
            EndAge = 393.3,
            Color = Color.FromArgb(229, 172, 77),
            ParentCode = "D",
            InternationalCorrelationCode = "D1"
        });

        // Silurian Period
        units.Add(new StratigraphicUnit
        {
            Name = "Silurian",
            Code = "S",
            Level = StratigraphicLevel.Period,
            StartAge = 443.8,
            EndAge = 419.2,
            Color = Color.FromArgb(179, 225, 182),
            ParentCode = "PZ",
            InternationalCorrelationCode = "S"
        });

        // Ordovician Period
        units.Add(new StratigraphicUnit
        {
            Name = "Ordovician",
            Code = "O",
            Level = StratigraphicLevel.Period,
            StartAge = 485.4,
            EndAge = 443.8,
            Color = Color.FromArgb(0, 146, 112),
            ParentCode = "PZ",
            InternationalCorrelationCode = "O"
        });

        // Cambrian Period
        units.Add(new StratigraphicUnit
        {
            Name = "Cambrian",
            Code = "Є",
            Level = StratigraphicLevel.Period,
            StartAge = 541.0,
            EndAge = 485.4,
            Color = Color.FromArgb(127, 160, 86),
            ParentCode = "PZ",
            InternationalCorrelationCode = "Є"
        });

        // PRECAMBRIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Precambrian",
            Code = "PC",
            Level = StratigraphicLevel.Era,
            StartAge = 4600.0,
            EndAge = 541.0,
            Color = Color.FromArgb(247, 67, 112),
            InternationalCorrelationCode = "PC"
        });

        return units;
    }
}