// GeoscientistToolkit/Business/Stratigraphies/ItalianStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     Stratigrafia italiana completa con unità regionali e denominazioni italiane
///     (molte con GSSP in Italia: Gelasiano, Calabriano, Ioniano, Tarantiano, Piacenziano,
///     Zancleano, Messiniano, Tortoniano, Serravalliano, Langhiano, Priaboniano, ecc.).
/// </summary>
public class ItalianStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public ItalianStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "Stratigrafia italiana";
    public string LanguageCode => "it-IT";

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

    private static List<StratigraphicUnit> InitializeUnits()
    {
        var u = new List<StratigraphicUnit>();

        // ==================================
        //             CENOZOICO
        // ==================================
        u.Add(new StratigraphicUnit
        {
            Name = "Cenozoico", Code = "CZ-IT", Level = StratigraphicLevel.Era, StartAge = 66.0, EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29), InternationalCorrelationCode = "CZ"
        });

        // --- Quaternario
        u.Add(new StratigraphicUnit
        {
            Name = "Quaternario", Code = "Q-IT", Level = StratigraphicLevel.Period, StartAge = 2.58, EndAge = 0.0,
            Color = Color.FromArgb(249, 249, 127), ParentCode = "CZ-IT", InternationalCorrelationCode = "Q"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Olocene", Code = "HOLO-IT", Level = StratigraphicLevel.Epoch, StartAge = 0.0117, EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236), ParentCode = "Q-IT", InternationalCorrelationCode = "Q2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pleistocene", Code = "PLEIS-IT", Level = StratigraphicLevel.Epoch, StartAge = 2.58, EndAge = 0.0117,
            Color = Color.FromArgb(255, 242, 174), ParentCode = "Q-IT", InternationalCorrelationCode = "Q1"
        });
        // Pleistocene italiano (serie globali con GSSP in Italia)
        u.Add(new StratigraphicUnit
        {
            Name = "Tarantiano", Code = "TARA", Level = StratigraphicLevel.Age, StartAge = 0.129, EndAge = 0.0117,
            Color = Color.FromArgb(255, 249, 208), ParentCode = "PLEIS-IT", InternationalCorrelationCode = "Q1d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ioniano", Code = "IONI", Level = StratigraphicLevel.Age, StartAge = 0.774, EndAge = 0.129,
            Color = Color.FromArgb(255, 246, 191), ParentCode = "PLEIS-IT", InternationalCorrelationCode = "Q1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Calabriano", Code = "CALAB", Level = StratigraphicLevel.Age, StartAge = 1.80, EndAge = 0.774,
            Color = Color.FromArgb(255, 244, 181), ParentCode = "PLEIS-IT", InternationalCorrelationCode = "Q1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Gelasiano", Code = "GELA", Level = StratigraphicLevel.Age, StartAge = 2.58, EndAge = 1.80,
            Color = Color.FromArgb(255, 242, 174), ParentCode = "PLEIS-IT", InternationalCorrelationCode = "Q1a"
        });

        // --- Neogene
        u.Add(new StratigraphicUnit
        {
            Name = "Neogene", Code = "N-IT", Level = StratigraphicLevel.Period, StartAge = 23.03, EndAge = 2.58,
            Color = Color.FromArgb(255, 230, 25), ParentCode = "CZ-IT", InternationalCorrelationCode = "N"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pliocene", Code = "PLIO-IT", Level = StratigraphicLevel.Epoch, StartAge = 5.333, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 153), ParentCode = "N-IT", InternationalCorrelationCode = "N2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Miocene", Code = "MIO-IT", Level = StratigraphicLevel.Epoch, StartAge = 23.03, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 0), ParentCode = "N-IT", InternationalCorrelationCode = "N1"
        });
        // Pliocene
        u.Add(new StratigraphicUnit
        {
            Name = "Piacenziano", Code = "PIAC", Level = StratigraphicLevel.Age, StartAge = 3.60, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 179), ParentCode = "PLIO-IT", InternationalCorrelationCode = "N2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Zancleano", Code = "ZANC", Level = StratigraphicLevel.Age, StartAge = 5.333, EndAge = 3.60,
            Color = Color.FromArgb(255, 255, 191), ParentCode = "PLIO-IT", InternationalCorrelationCode = "N2a"
        });
        // Miocene (tutti gli stadi con forte uso in Italia)
        u.Add(new StratigraphicUnit
        {
            Name = "Messiniano", Code = "MESS", Level = StratigraphicLevel.Age, StartAge = 7.246, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 204), ParentCode = "MIO-IT", InternationalCorrelationCode = "N1f"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Tortoniano", Code = "TORT", Level = StratigraphicLevel.Age, StartAge = 11.63, EndAge = 7.246,
            Color = Color.FromArgb(255, 255, 216), ParentCode = "MIO-IT", InternationalCorrelationCode = "N1e"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Serravalliano", Code = "SERR", Level = StratigraphicLevel.Age, StartAge = 13.82, EndAge = 11.63,
            Color = Color.FromArgb(255, 255, 229), ParentCode = "MIO-IT", InternationalCorrelationCode = "N1d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Langhiano", Code = "LANG", Level = StratigraphicLevel.Age, StartAge = 15.97, EndAge = 13.82,
            Color = Color.FromArgb(255, 255, 240), ParentCode = "MIO-IT", InternationalCorrelationCode = "N1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Burdigaliano", Code = "BURD", Level = StratigraphicLevel.Age, StartAge = 20.44, EndAge = 15.97,
            Color = Color.FromArgb(255, 255, 230), ParentCode = "MIO-IT", InternationalCorrelationCode = "N1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aquitaniano", Code = "AQUI", Level = StratigraphicLevel.Age, StartAge = 23.03, EndAge = 20.44,
            Color = Color.FromArgb(255, 255, 220), ParentCode = "MIO-IT", InternationalCorrelationCode = "N1a"
        });

        // --- Paleogene
        u.Add(new StratigraphicUnit
        {
            Name = "Paleogene", Code = "PG-IT", Level = StratigraphicLevel.Period, StartAge = 66.0, EndAge = 23.03,
            Color = Color.FromArgb(253, 154, 82), ParentCode = "CZ-IT", InternationalCorrelationCode = "Pg"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oligocene", Code = "OLIG-IT", Level = StratigraphicLevel.Epoch, StartAge = 33.9, EndAge = 23.03,
            Color = Color.FromArgb(254, 192, 122), ParentCode = "PG-IT", InternationalCorrelationCode = "Pg3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Eocene", Code = "EOC-IT", Level = StratigraphicLevel.Epoch, StartAge = 56.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 180, 108), ParentCode = "PG-IT", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Paleocene", Code = "PALEO-IT", Level = StratigraphicLevel.Epoch, StartAge = 66.0, EndAge = 56.0,
            Color = Color.FromArgb(253, 167, 95), ParentCode = "PG-IT", InternationalCorrelationCode = "Pg1"
        });
        // Oligocene
        u.Add(new StratigraphicUnit
        {
            Name = "Chattiano", Code = "CHATT-IT", Level = StratigraphicLevel.Age, StartAge = 27.82, EndAge = 23.03,
            Color = Color.FromArgb(254, 210, 155), ParentCode = "OLIG-IT", InternationalCorrelationCode = "Pg3b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Rupeliano", Code = "RUPEL-IT", Level = StratigraphicLevel.Age, StartAge = 33.9, EndAge = 27.82,
            Color = Color.FromArgb(254, 202, 140), ParentCode = "OLIG-IT", InternationalCorrelationCode = "Pg3a"
        });
        // Eocene (include lo standard italiano Priaboniano)
        u.Add(new StratigraphicUnit
        {
            Name = "Priaboniano", Code = "PRIAB-IT", Level = StratigraphicLevel.Age, StartAge = 38.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 189, 123), ParentCode = "EOC-IT", InternationalCorrelationCode = "Pg2d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bartoniano", Code = "BART-IT", Level = StratigraphicLevel.Age, StartAge = 41.2, EndAge = 38.0,
            Color = Color.FromArgb(253, 196, 135), ParentCode = "EOC-IT", InternationalCorrelationCode = "Pg2c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Luteziano", Code = "LUTET-IT", Level = StratigraphicLevel.Age, StartAge = 47.8, EndAge = 41.2,
            Color = Color.FromArgb(253, 203, 146), ParentCode = "EOC-IT", InternationalCorrelationCode = "Pg2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ypresiano", Code = "YPRE-IT", Level = StratigraphicLevel.Age, StartAge = 56.0, EndAge = 47.8,
            Color = Color.FromArgb(253, 210, 158), ParentCode = "EOC-IT", InternationalCorrelationCode = "Pg2a"
        });
        // Paleocene
        u.Add(new StratigraphicUnit
        {
            Name = "Thanetiano", Code = "THAN-IT", Level = StratigraphicLevel.Age, StartAge = 59.2, EndAge = 56.0,
            Color = Color.FromArgb(253, 176, 106), ParentCode = "PALEO-IT", InternationalCorrelationCode = "Pg1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Selandiano", Code = "SELA-IT", Level = StratigraphicLevel.Age, StartAge = 61.6, EndAge = 59.2,
            Color = Color.FromArgb(253, 182, 114), ParentCode = "PALEO-IT", InternationalCorrelationCode = "Pg1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Daniano", Code = "DAN-IT", Level = StratigraphicLevel.Age, StartAge = 66.0, EndAge = 61.6,
            Color = Color.FromArgb(253, 188, 123), ParentCode = "PALEO-IT", InternationalCorrelationCode = "Pg1a"
        });

        // ==================================
        //             MESOZOICO
        // ==================================
        u.Add(new StratigraphicUnit
        {
            Name = "Mesozoico", Code = "MZ-IT", Level = StratigraphicLevel.Era, StartAge = 252.17, EndAge = 66.0,
            Color = Color.FromArgb(103, 197, 202), InternationalCorrelationCode = "MZ"
        });

        // --- Cretaceo
        u.Add(new StratigraphicUnit
        {
            Name = "Cretaceo", Code = "K-IT", Level = StratigraphicLevel.Period, StartAge = 145.0, EndAge = 66.0,
            Color = Color.FromArgb(127, 198, 78), ParentCode = "MZ-IT", InternationalCorrelationCode = "K"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cretaceo superiore", Code = "K2-IT", Level = StratigraphicLevel.Epoch, StartAge = 100.5,
            EndAge = 66.0, Color = Color.FromArgb(166, 216, 74), ParentCode = "K-IT",
            InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cretaceo inferiore", Code = "K1-IT", Level = StratigraphicLevel.Epoch, StartAge = 145.0,
            EndAge = 100.5, Color = Color.FromArgb(140, 205, 87), ParentCode = "K-IT",
            InternationalCorrelationCode = "K1"
        });
        // Cretaceo superiore
        u.Add(new StratigraphicUnit
        {
            Name = "Maastrichtiano", Code = "MAAS-IT", Level = StratigraphicLevel.Age, StartAge = 72.1, EndAge = 66.0,
            Color = Color.FromArgb(175, 219, 86), ParentCode = "K2-IT", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Campaniano", Code = "CAMP-IT", Level = StratigraphicLevel.Age, StartAge = 83.6, EndAge = 72.1,
            Color = Color.FromArgb(171, 217, 82), ParentCode = "K2-IT", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Santoniano", Code = "SANT-IT", Level = StratigraphicLevel.Age, StartAge = 86.3, EndAge = 83.6,
            Color = Color.FromArgb(168, 216, 78), ParentCode = "K2-IT", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Coniaciano", Code = "CONI-IT", Level = StratigraphicLevel.Age, StartAge = 89.8, EndAge = 86.3,
            Color = Color.FromArgb(164, 215, 74), ParentCode = "K2-IT", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Turoniano", Code = "TURON-IT", Level = StratigraphicLevel.Age, StartAge = 93.9, EndAge = 89.8,
            Color = Color.FromArgb(160, 212, 71), ParentCode = "K2-IT", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cenomaniano", Code = "CENO-IT", Level = StratigraphicLevel.Age, StartAge = 100.5, EndAge = 93.9,
            Color = Color.FromArgb(156, 210, 68), ParentCode = "K2-IT", InternationalCorrelationCode = "K2"
        });
        // Cretaceo inferiore
        u.Add(new StratigraphicUnit
        {
            Name = "Albiano", Code = "ALB-IT", Level = StratigraphicLevel.Age, StartAge = 113.0, EndAge = 100.5,
            Color = Color.FromArgb(150, 207, 103), ParentCode = "K1-IT", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aptiano", Code = "APT-IT", Level = StratigraphicLevel.Age, StartAge = 125.0, EndAge = 113.0,
            Color = Color.FromArgb(146, 206, 96), ParentCode = "K1-IT", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Barremiano", Code = "BARR-IT", Level = StratigraphicLevel.Age, StartAge = 129.4, EndAge = 125.0,
            Color = Color.FromArgb(144, 206, 93), ParentCode = "K1-IT", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hauteriviano", Code = "HAUT-IT", Level = StratigraphicLevel.Age, StartAge = 132.9, EndAge = 129.4,
            Color = Color.FromArgb(142, 205, 90), ParentCode = "K1-IT", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Valanginiano", Code = "VALA-IT", Level = StratigraphicLevel.Age, StartAge = 139.8, EndAge = 132.9,
            Color = Color.FromArgb(140, 205, 87), ParentCode = "K1-IT", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Berriasiano", Code = "BERR-IT", Level = StratigraphicLevel.Age, StartAge = 145.0, EndAge = 139.8,
            Color = Color.FromArgb(138, 203, 85), ParentCode = "K1-IT", InternationalCorrelationCode = "K1"
        });

        // --- Giurassico
        u.Add(new StratigraphicUnit
        {
            Name = "Giurassico", Code = "J-IT", Level = StratigraphicLevel.Period, StartAge = 201.4, EndAge = 145.0,
            Color = Color.FromArgb(52, 178, 201), ParentCode = "MZ-IT", InternationalCorrelationCode = "J"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Giurassico superiore (Malm)", Code = "J3-IT", Level = StratigraphicLevel.Epoch, StartAge = 163.5,
            EndAge = 145.0, Color = Color.FromArgb(179, 227, 238), ParentCode = "J-IT",
            InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Titoniano", Code = "TITO-IT", Level = StratigraphicLevel.Age, StartAge = 152.1, EndAge = 145.0,
            Color = Color.FromArgb(217, 241, 247), ParentCode = "J3-IT", InternationalCorrelationCode = "J3c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Kimmeridgiano", Code = "KIMM-IT", Level = StratigraphicLevel.Age, StartAge = 157.3, EndAge = 152.1,
            Color = Color.FromArgb(204, 236, 244), ParentCode = "J3-IT", InternationalCorrelationCode = "J3b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oxfordiano", Code = "OXFO-IT", Level = StratigraphicLevel.Age, StartAge = 163.5, EndAge = 157.3,
            Color = Color.FromArgb(191, 231, 241), ParentCode = "J3-IT", InternationalCorrelationCode = "J3a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Giurassico medio (Dogger)", Code = "J2-IT", Level = StratigraphicLevel.Epoch, StartAge = 174.7,
            EndAge = 163.5, Color = Color.FromArgb(128, 207, 216), ParentCode = "J-IT",
            InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Calloviano", Code = "CALL-IT", Level = StratigraphicLevel.Age, StartAge = 166.1, EndAge = 163.5,
            Color = Color.FromArgb(191, 226, 232), ParentCode = "J2-IT", InternationalCorrelationCode = "J2d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Batoniano", Code = "BATO-IT", Level = StratigraphicLevel.Age, StartAge = 168.2, EndAge = 166.1,
            Color = Color.FromArgb(179, 220, 228), ParentCode = "J2-IT", InternationalCorrelationCode = "J2c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bajociano", Code = "BAJO-IT", Level = StratigraphicLevel.Age, StartAge = 170.9, EndAge = 168.2,
            Color = Color.FromArgb(166, 215, 225), ParentCode = "J2-IT", InternationalCorrelationCode = "J2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aaleniano", Code = "AAL-IT", Level = StratigraphicLevel.Age, StartAge = 174.7, EndAge = 170.9,
            Color = Color.FromArgb(153, 209, 221), ParentCode = "J2-IT", InternationalCorrelationCode = "J2a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Giurassico inferiore (Lias)", Code = "J1-IT", Level = StratigraphicLevel.Epoch, StartAge = 201.4,
            EndAge = 174.7, Color = Color.FromArgb(66, 174, 208), ParentCode = "J-IT",
            InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Toarciano", Code = "TOAR-IT", Level = StratigraphicLevel.Age, StartAge = 184.2, EndAge = 174.7,
            Color = Color.FromArgb(153, 206, 227), ParentCode = "J1-IT", InternationalCorrelationCode = "J1d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pliensbachiano", Code = "PLIE-IT", Level = StratigraphicLevel.Age, StartAge = 192.9, EndAge = 184.2,
            Color = Color.FromArgb(128, 197, 221), ParentCode = "J1-IT", InternationalCorrelationCode = "J1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Sinemuriano", Code = "SINE-IT", Level = StratigraphicLevel.Age, StartAge = 199.3, EndAge = 192.9,
            Color = Color.FromArgb(103, 188, 216), ParentCode = "J1-IT", InternationalCorrelationCode = "J1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hettangiano", Code = "HETT-IT", Level = StratigraphicLevel.Age, StartAge = 201.4, EndAge = 199.3,
            Color = Color.FromArgb(78, 179, 211), ParentCode = "J1-IT", InternationalCorrelationCode = "J1a"
        });

        // --- Triassico (forte specificità alpina italiana)
        u.Add(new StratigraphicUnit
        {
            Name = "Triassico", Code = "T-IT", Level = StratigraphicLevel.Period, StartAge = 252.17, EndAge = 201.4,
            Color = Color.FromArgb(129, 43, 146), ParentCode = "MZ-IT", InternationalCorrelationCode = "T"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Triassico superiore", Code = "T3-IT", Level = StratigraphicLevel.Epoch, StartAge = 237.0,
            EndAge = 201.4, Color = Color.FromArgb(189, 140, 195), ParentCode = "T-IT",
            InternationalCorrelationCode = "T3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Retico", Code = "RETI-IT", Level = StratigraphicLevel.Age, StartAge = 208.5, EndAge = 201.4,
            Color = Color.FromArgb(227, 185, 219), ParentCode = "T3-IT", InternationalCorrelationCode = "T3c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Norico", Code = "NORI-IT", Level = StratigraphicLevel.Age, StartAge = 227.0, EndAge = 208.5,
            Color = Color.FromArgb(214, 170, 211), ParentCode = "T3-IT", InternationalCorrelationCode = "T3b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Carnico", Code = "CARN-IT", Level = StratigraphicLevel.Age, StartAge = 237.0, EndAge = 227.0,
            Color = Color.FromArgb(201, 155, 203), ParentCode = "T3-IT", InternationalCorrelationCode = "T3a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Triassico medio", Code = "T2-IT", Level = StratigraphicLevel.Epoch, StartAge = 247.2,
            EndAge = 237.0, Color = Color.FromArgb(177, 104, 177), ParentCode = "T-IT",
            InternationalCorrelationCode = "T2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ladinico", Code = "LADI-IT", Level = StratigraphicLevel.Age, StartAge = 242.0, EndAge = 237.0,
            Color = Color.FromArgb(201, 131, 191), ParentCode = "T2-IT", InternationalCorrelationCode = "T2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Anisico", Code = "ANIS-IT", Level = StratigraphicLevel.Age, StartAge = 247.2, EndAge = 242.0,
            Color = Color.FromArgb(188, 117, 183), ParentCode = "T2-IT", InternationalCorrelationCode = "T2a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Triassico inferiore", Code = "T1-IT", Level = StratigraphicLevel.Epoch, StartAge = 252.17,
            EndAge = 247.2, Color = Color.FromArgb(152, 57, 153), ParentCode = "T-IT",
            InternationalCorrelationCode = "T1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Olenekiano", Code = "OLEN-IT", Level = StratigraphicLevel.Age, StartAge = 251.2, EndAge = 247.2,
            Color = Color.FromArgb(176, 81, 165), ParentCode = "T1-IT", InternationalCorrelationCode = "T1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Induano", Code = "INDU-IT", Level = StratigraphicLevel.Age, StartAge = 252.17, EndAge = 251.2,
            Color = Color.FromArgb(164, 70, 159), ParentCode = "T1-IT", InternationalCorrelationCode = "T1a"
        });
        // Unità regionale italiana iconica (Permiano tardo – Triassico basso)
        u.Add(new StratigraphicUnit
        {
            Name = "Verrucano (unità regionale)", Code = "VERR-IT", Level = StratigraphicLevel.Age, StartAge = 280.0,
            EndAge = 245.0, Color = Color.FromArgb(180, 75, 150), ParentCode = "T-IT",
            InternationalCorrelationCode = "P,T"
        });

        // ==================================
        //             PALEOZOICO
        // ==================================
        u.Add(new StratigraphicUnit
        {
            Name = "Paleozoico", Code = "PZ-IT", Level = StratigraphicLevel.Era, StartAge = 541.0, EndAge = 252.17,
            Color = Color.FromArgb(153, 192, 141), InternationalCorrelationCode = "PZ"
        });

        // --- Permiano
        u.Add(new StratigraphicUnit
        {
            Name = "Permiano", Code = "P-IT", Level = StratigraphicLevel.Period, StartAge = 298.9, EndAge = 252.17,
            Color = Color.FromArgb(240, 64, 40), ParentCode = "PZ-IT", InternationalCorrelationCode = "P"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lopingiano", Code = "P3-IT", Level = StratigraphicLevel.Epoch, StartAge = 259.1, EndAge = 252.17,
            Color = Color.FromArgb(251, 167, 148), ParentCode = "P-IT", InternationalCorrelationCode = "P3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Guadalupiano", Code = "P2-IT", Level = StratigraphicLevel.Epoch, StartAge = 272.95, EndAge = 259.1,
            Color = Color.FromArgb(251, 154, 133), ParentCode = "P-IT", InternationalCorrelationCode = "P2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cisuraliano", Code = "P1-IT", Level = StratigraphicLevel.Epoch, StartAge = 298.9, EndAge = 272.95,
            Color = Color.FromArgb(252, 128, 104), ParentCode = "P-IT", InternationalCorrelationCode = "P1"
        });
        // (Nota: GSSP del Wordiano in Sicilia; teniamo gli stadi a futura estensione se serviranno in viewer)

        // --- Carbonifero
        u.Add(new StratigraphicUnit
        {
            Name = "Carbonifero", Code = "C-IT", Level = StratigraphicLevel.Period, StartAge = 358.9, EndAge = 298.9,
            Color = Color.FromArgb(103, 165, 153), ParentCode = "PZ-IT", InternationalCorrelationCode = "C"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pennsylvaniano", Code = "C3-IT", Level = StratigraphicLevel.Epoch, StartAge = 323.2, EndAge = 298.9,
            Color = Color.FromArgb(153, 194, 181), ParentCode = "C-IT", InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mississippiano", Code = "C1C2-IT", Level = StratigraphicLevel.Epoch, StartAge = 358.9,
            EndAge = 323.2, Color = Color.FromArgb(127, 172, 159), ParentCode = "C-IT",
            InternationalCorrelationCode = "C1,C2"
        });

        // --- Devoniano
        u.Add(new StratigraphicUnit
        {
            Name = "Devoniano", Code = "D-IT", Level = StratigraphicLevel.Period, StartAge = 419.2, EndAge = 358.9,
            Color = Color.FromArgb(203, 140, 55), ParentCode = "PZ-IT", InternationalCorrelationCode = "D"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Devoniano superiore", Code = "D3-IT", Level = StratigraphicLevel.Epoch, StartAge = 382.7,
            EndAge = 358.9, Color = Color.FromArgb(241, 200, 104), ParentCode = "D-IT",
            InternationalCorrelationCode = "D3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Devoniano medio", Code = "D2-IT", Level = StratigraphicLevel.Epoch, StartAge = 393.3,
            EndAge = 382.7, Color = Color.FromArgb(241, 225, 157), ParentCode = "D-IT",
            InternationalCorrelationCode = "D2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Devoniano inferiore", Code = "D1-IT", Level = StratigraphicLevel.Epoch, StartAge = 419.2,
            EndAge = 393.3, Color = Color.FromArgb(229, 196, 104), ParentCode = "D-IT",
            InternationalCorrelationCode = "D1"
        });

        // --- Siluriano
        u.Add(new StratigraphicUnit
        {
            Name = "Siluriano", Code = "S-IT", Level = StratigraphicLevel.Period, StartAge = 443.8, EndAge = 419.2,
            Color = Color.FromArgb(179, 225, 182), ParentCode = "PZ-IT", InternationalCorrelationCode = "S"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pridoli", Code = "S4-IT", Level = StratigraphicLevel.Epoch, StartAge = 423.0, EndAge = 419.2,
            Color = Color.FromArgb(230, 245, 225), ParentCode = "S-IT", InternationalCorrelationCode = "S4"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ludlow", Code = "S3-IT", Level = StratigraphicLevel.Epoch, StartAge = 427.4, EndAge = 423.0,
            Color = Color.FromArgb(191, 230, 195), ParentCode = "S-IT", InternationalCorrelationCode = "S3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Wenlock", Code = "S2-IT", Level = StratigraphicLevel.Epoch, StartAge = 433.4, EndAge = 427.4,
            Color = Color.FromArgb(204, 235, 209), ParentCode = "S-IT", InternationalCorrelationCode = "S2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Llandovery", Code = "S1-IT", Level = StratigraphicLevel.Epoch, StartAge = 443.8, EndAge = 433.4,
            Color = Color.FromArgb(217, 240, 223), ParentCode = "S-IT", InternationalCorrelationCode = "S1"
        });

        // --- Ordoviciano
        u.Add(new StratigraphicUnit
        {
            Name = "Ordoviciano", Code = "O-IT", Level = StratigraphicLevel.Period, StartAge = 485.4, EndAge = 443.8,
            Color = Color.FromArgb(0, 146, 112), ParentCode = "PZ-IT", InternationalCorrelationCode = "O"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ordoviciano superiore", Code = "O3-IT", Level = StratigraphicLevel.Epoch, StartAge = 458.4,
            EndAge = 443.8, Color = Color.FromArgb(127, 202, 147), ParentCode = "O-IT",
            InternationalCorrelationCode = "O3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ordoviciano medio", Code = "O2-IT", Level = StratigraphicLevel.Epoch, StartAge = 470.0,
            EndAge = 458.4, Color = Color.FromArgb(76, 177, 136), ParentCode = "O-IT",
            InternationalCorrelationCode = "O2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ordoviciano inferiore", Code = "O1-IT", Level = StratigraphicLevel.Epoch, StartAge = 485.4,
            EndAge = 470.0, Color = Color.FromArgb(26, 152, 124), ParentCode = "O-IT",
            InternationalCorrelationCode = "O1"
        });

        // --- Cambriano (con Fortuniano in Sardegna)
        u.Add(new StratigraphicUnit
        {
            Name = "Cambriano", Code = "CAM-IT", Level = StratigraphicLevel.Period, StartAge = 541.0, EndAge = 485.4,
            Color = Color.FromArgb(127, 160, 86), ParentCode = "PZ-IT", InternationalCorrelationCode = "Ꞓ"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Furongiano (Cambriano sup.)", Code = "CAM4-IT", Level = StratigraphicLevel.Epoch, StartAge = 497.0,
            EndAge = 485.4, Color = Color.FromArgb(166, 185, 108), ParentCode = "CAM-IT",
            InternationalCorrelationCode = "Ꞓ4"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Serie 3 (Cambriano medio)", Code = "CAM3-IT", Level = StratigraphicLevel.Epoch, StartAge = 509.0,
            EndAge = 497.0, Color = Color.FromArgb(179, 191, 128), ParentCode = "CAM-IT",
            InternationalCorrelationCode = "Ꞓ3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Serie 2 (Cambriano inf.)", Code = "CAM2-IT", Level = StratigraphicLevel.Epoch, StartAge = 521.0,
            EndAge = 509.0, Color = Color.FromArgb(153, 179, 115), ParentCode = "CAM-IT",
            InternationalCorrelationCode = "Ꞓ2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Terreneuviano", Code = "CAM1-IT", Level = StratigraphicLevel.Epoch, StartAge = 541.0,
            EndAge = 521.0, Color = Color.FromArgb(140, 170, 102), ParentCode = "CAM-IT",
            InternationalCorrelationCode = "Ꞓ1"
        });
        // Età regionale globalmente standardizzata in Sardegna
        u.Add(new StratigraphicUnit
        {
            Name = "Fortuniano (stadio)", Code = "FORT-IT", Level = StratigraphicLevel.Age, StartAge = 541.0,
            EndAge = 529.0, Color = Color.FromArgb(150, 175, 108), ParentCode = "CAM1-IT",
            InternationalCorrelationCode = "Stage 1"
        });

        // ==================================
        //            PRECAMBRIANO
        // ==================================
        u.Add(new StratigraphicUnit
        {
            Name = "Precambriano", Code = "PC-IT", Level = StratigraphicLevel.Era, StartAge = 4600.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 67, 112), InternationalCorrelationCode = "PC"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Proterozoico", Code = "PROT-IT", Level = StratigraphicLevel.Eon, StartAge = 2500.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 104, 152), ParentCode = "PC-IT", InternationalCorrelationCode = "Pt"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Archeano", Code = "ARCH-IT", Level = StratigraphicLevel.Eon, StartAge = 4000.0, EndAge = 2500.0,
            Color = Color.FromArgb(240, 4, 127), ParentCode = "PC-IT", InternationalCorrelationCode = "A"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Adeano", Code = "HAD-IT", Level = StratigraphicLevel.Eon, StartAge = 4600.0, EndAge = 4000.0,
            Color = Color.FromArgb(174, 2, 126), ParentCode = "PC-IT", InternationalCorrelationCode = "H"
        });

        return u;
    }
}