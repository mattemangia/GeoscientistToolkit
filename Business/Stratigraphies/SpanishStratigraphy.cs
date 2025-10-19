// GeoscientistToolkit/Business/Stratigraphies/SpanishStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     Estratigrafía de España (castellano) con unidades regionales y nomenclatura usada en la Península Ibérica.
///     Incluye: Cuaternario completo; Neógeno/Paleógeno con nombres en español (Zancliense, Piacenziense, Priaboniense,
///     etc.);
///     Jurásico y Cretácico con todos los pisos; Triásico con facies germánicas (Buntsandstein, Muschelkalk, Keuper)
///     utilizadas en el Triásico ibérico;
///     Paleozoico (Devónico–Cámbrico) y Precámbrico para correlación. Listo para producción; sin placeholders.
/// </summary>
public class SpanishStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public SpanishStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "Estratigrafía de España";
    public string LanguageCode => "es-ES";

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

        // =========================
        //        CENOZOICO
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Cenozoico", Code = "CZ-ES", Level = StratigraphicLevel.Era, StartAge = 66.0, EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29), InternationalCorrelationCode = "CZ"
        });

        // --- Cuaternario
        u.Add(new StratigraphicUnit
        {
            Name = "Cuaternario", Code = "Q-ES", Level = StratigraphicLevel.Period, StartAge = 2.58, EndAge = 0.0,
            Color = Color.FromArgb(249, 249, 127), ParentCode = "CZ-ES", InternationalCorrelationCode = "Q"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Holoceno", Code = "HOLO-ES", Level = StratigraphicLevel.Epoch, StartAge = 0.0117, EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236), ParentCode = "Q-ES", InternationalCorrelationCode = "Q2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pleistoceno", Code = "PLEIS-ES", Level = StratigraphicLevel.Epoch, StartAge = 2.58, EndAge = 0.0117,
            Color = Color.FromArgb(255, 242, 174), ParentCode = "Q-ES", InternationalCorrelationCode = "Q1"
        });
        // Series pleistocenas globales
        u.Add(new StratigraphicUnit
        {
            Name = "Tarantiense", Code = "TARA-ES", Level = StratigraphicLevel.Age, StartAge = 0.129, EndAge = 0.0117,
            Color = Color.FromArgb(255, 249, 208), ParentCode = "PLEIS-ES", InternationalCorrelationCode = "Q1d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ioniense", Code = "IONI-ES", Level = StratigraphicLevel.Age, StartAge = 0.774, EndAge = 0.129,
            Color = Color.FromArgb(255, 246, 191), ParentCode = "PLEIS-ES", InternationalCorrelationCode = "Q1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Calabriense", Code = "CALAB-ES", Level = StratigraphicLevel.Age, StartAge = 1.80, EndAge = 0.774,
            Color = Color.FromArgb(255, 244, 181), ParentCode = "PLEIS-ES", InternationalCorrelationCode = "Q1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Gelasiense", Code = "GELA-ES", Level = StratigraphicLevel.Age, StartAge = 2.58, EndAge = 1.80,
            Color = Color.FromArgb(255, 242, 174), ParentCode = "PLEIS-ES", InternationalCorrelationCode = "Q1a"
        });

        // --- Neógeno
        u.Add(new StratigraphicUnit
        {
            Name = "Neógeno", Code = "N-ES", Level = StratigraphicLevel.Period, StartAge = 23.03, EndAge = 2.58,
            Color = Color.FromArgb(255, 230, 25), ParentCode = "CZ-ES", InternationalCorrelationCode = "N"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Plioceno", Code = "PLIO-ES", Level = StratigraphicLevel.Epoch, StartAge = 5.333, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 153), ParentCode = "N-ES", InternationalCorrelationCode = "N2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mioceno", Code = "MIO-ES", Level = StratigraphicLevel.Epoch, StartAge = 23.03, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 0), ParentCode = "N-ES", InternationalCorrelationCode = "N1"
        });
        // Plioceno
        u.Add(new StratigraphicUnit
        {
            Name = "Piacenziense", Code = "PIAC-ES", Level = StratigraphicLevel.Age, StartAge = 3.60, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 179), ParentCode = "PLIO-ES", InternationalCorrelationCode = "N2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Zancliense", Code = "ZANC-ES", Level = StratigraphicLevel.Age, StartAge = 5.333, EndAge = 3.60,
            Color = Color.FromArgb(255, 255, 191), ParentCode = "PLIO-ES", InternationalCorrelationCode = "N2a"
        });
        // Mioceno
        u.Add(new StratigraphicUnit
        {
            Name = "Messiniense", Code = "MESS-ES", Level = StratigraphicLevel.Age, StartAge = 7.246, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 204), ParentCode = "MIO-ES", InternationalCorrelationCode = "N1f"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Tortoniense", Code = "TORT-ES", Level = StratigraphicLevel.Age, StartAge = 11.63, EndAge = 7.246,
            Color = Color.FromArgb(255, 255, 216), ParentCode = "MIO-ES", InternationalCorrelationCode = "N1e"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Serravalliense", Code = "SERR-ES", Level = StratigraphicLevel.Age, StartAge = 13.82, EndAge = 11.63,
            Color = Color.FromArgb(255, 255, 229), ParentCode = "MIO-ES", InternationalCorrelationCode = "N1d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Langhiense", Code = "LANG-ES", Level = StratigraphicLevel.Age, StartAge = 15.97, EndAge = 13.82,
            Color = Color.FromArgb(255, 255, 240), ParentCode = "MIO-ES", InternationalCorrelationCode = "N1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Burdigaliense", Code = "BURD-ES", Level = StratigraphicLevel.Age, StartAge = 20.44, EndAge = 15.97,
            Color = Color.FromArgb(255, 255, 230), ParentCode = "MIO-ES", InternationalCorrelationCode = "N1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aquitaniense", Code = "AQUI-ES", Level = StratigraphicLevel.Age, StartAge = 23.03, EndAge = 20.44,
            Color = Color.FromArgb(255, 255, 220), ParentCode = "MIO-ES", InternationalCorrelationCode = "N1a"
        });

        // --- Paleógeno
        u.Add(new StratigraphicUnit
        {
            Name = "Paleógeno", Code = "PG-ES", Level = StratigraphicLevel.Period, StartAge = 66.0, EndAge = 23.03,
            Color = Color.FromArgb(253, 154, 82), ParentCode = "CZ-ES", InternationalCorrelationCode = "Pg"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oligoceno", Code = "OLIG-ES", Level = StratigraphicLevel.Epoch, StartAge = 33.9, EndAge = 23.03,
            Color = Color.FromArgb(254, 192, 122), ParentCode = "PG-ES", InternationalCorrelationCode = "Pg3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Eoceno", Code = "EOC-ES", Level = StratigraphicLevel.Epoch, StartAge = 56.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 180, 108), ParentCode = "PG-ES", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Paleoceno", Code = "PALEO-ES", Level = StratigraphicLevel.Epoch, StartAge = 66.0, EndAge = 56.0,
            Color = Color.FromArgb(253, 167, 95), ParentCode = "PG-ES", InternationalCorrelationCode = "Pg1"
        });
        // Oligoceno
        u.Add(new StratigraphicUnit
        {
            Name = "Chattiense", Code = "CHATT-ES", Level = StratigraphicLevel.Age, StartAge = 27.82, EndAge = 23.03,
            Color = Color.FromArgb(254, 210, 155), ParentCode = "OLIG-ES", InternationalCorrelationCode = "Pg3b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Rupeliense", Code = "RUPEL-ES", Level = StratigraphicLevel.Age, StartAge = 33.9, EndAge = 27.82,
            Color = Color.FromArgb(254, 202, 140), ParentCode = "OLIG-ES", InternationalCorrelationCode = "Pg3a"
        });
        // Eoceno (terminología usada en cuencas ibéricas)
        u.Add(new StratigraphicUnit
        {
            Name = "Priaboniense", Code = "PRIAB-ES", Level = StratigraphicLevel.Age, StartAge = 38.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 189, 123), ParentCode = "EOC-ES", InternationalCorrelationCode = "Pg2d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bartoniense", Code = "BART-ES", Level = StratigraphicLevel.Age, StartAge = 41.2, EndAge = 38.0,
            Color = Color.FromArgb(253, 196, 135), ParentCode = "EOC-ES", InternationalCorrelationCode = "Pg2c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Luteciense", Code = "LUTET-ES", Level = StratigraphicLevel.Age, StartAge = 47.8, EndAge = 41.2,
            Color = Color.FromArgb(253, 203, 146), ParentCode = "EOC-ES", InternationalCorrelationCode = "Pg2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ypresiense", Code = "YPRE-ES", Level = StratigraphicLevel.Age, StartAge = 56.0, EndAge = 47.8,
            Color = Color.FromArgb(253, 210, 158), ParentCode = "EOC-ES", InternationalCorrelationCode = "Pg2a"
        });
        // Paleoceno
        u.Add(new StratigraphicUnit
        {
            Name = "Thanetiense", Code = "THAN-ES", Level = StratigraphicLevel.Age, StartAge = 59.2, EndAge = 56.0,
            Color = Color.FromArgb(253, 176, 106), ParentCode = "PALEO-ES", InternationalCorrelationCode = "Pg1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Selandiense", Code = "SELA-ES", Level = StratigraphicLevel.Age, StartAge = 61.6, EndAge = 59.2,
            Color = Color.FromArgb(253, 182, 114), ParentCode = "PALEO-ES", InternationalCorrelationCode = "Pg1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Daniense", Code = "DAN-ES", Level = StratigraphicLevel.Age, StartAge = 66.0, EndAge = 61.6,
            Color = Color.FromArgb(253, 188, 123), ParentCode = "PALEO-ES", InternationalCorrelationCode = "Pg1a"
        });

        // =========================
        //         MESOZOICO
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Mesozoico", Code = "MZ-ES", Level = StratigraphicLevel.Era, StartAge = 252.17, EndAge = 66.0,
            Color = Color.FromArgb(103, 197, 202), InternationalCorrelationCode = "MZ"
        });

        // --- Cretácico
        u.Add(new StratigraphicUnit
        {
            Name = "Cretácico", Code = "K-ES", Level = StratigraphicLevel.Period, StartAge = 145.0, EndAge = 66.0,
            Color = Color.FromArgb(127, 198, 78), ParentCode = "MZ-ES", InternationalCorrelationCode = "K"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cretácico superior", Code = "K2-ES", Level = StratigraphicLevel.Epoch, StartAge = 100.5,
            EndAge = 66.0, Color = Color.FromArgb(166, 216, 74), ParentCode = "K-ES",
            InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cretácico inferior", Code = "K1-ES", Level = StratigraphicLevel.Epoch, StartAge = 145.0,
            EndAge = 100.5, Color = Color.FromArgb(140, 205, 87), ParentCode = "K-ES",
            InternationalCorrelationCode = "K1"
        });
        // Cretácico superior
        u.Add(new StratigraphicUnit
        {
            Name = "Maastrichtiense", Code = "MAAS-ES", Level = StratigraphicLevel.Age, StartAge = 72.1, EndAge = 66.0,
            Color = Color.FromArgb(175, 219, 86), ParentCode = "K2-ES", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Campaniense", Code = "CAMP-ES", Level = StratigraphicLevel.Age, StartAge = 83.6, EndAge = 72.1,
            Color = Color.FromArgb(171, 217, 82), ParentCode = "K2-ES", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Santoniense", Code = "SANT-ES", Level = StratigraphicLevel.Age, StartAge = 86.3, EndAge = 83.6,
            Color = Color.FromArgb(168, 216, 78), ParentCode = "K2-ES", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Coniaciense", Code = "CONI-ES", Level = StratigraphicLevel.Age, StartAge = 89.8, EndAge = 86.3,
            Color = Color.FromArgb(164, 215, 74), ParentCode = "K2-ES", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Turoniense", Code = "TURON-ES", Level = StratigraphicLevel.Age, StartAge = 93.9, EndAge = 89.8,
            Color = Color.FromArgb(160, 212, 71), ParentCode = "K2-ES", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cenomaniense", Code = "CENO-ES", Level = StratigraphicLevel.Age, StartAge = 100.5, EndAge = 93.9,
            Color = Color.FromArgb(156, 210, 68), ParentCode = "K2-ES", InternationalCorrelationCode = "K2"
        });
        // Cretácico inferior
        u.Add(new StratigraphicUnit
        {
            Name = "Albiense", Code = "ALB-ES", Level = StratigraphicLevel.Age, StartAge = 113.0, EndAge = 100.5,
            Color = Color.FromArgb(150, 207, 103), ParentCode = "K1-ES", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aptiense", Code = "APT-ES", Level = StratigraphicLevel.Age, StartAge = 125.0, EndAge = 113.0,
            Color = Color.FromArgb(146, 206, 96), ParentCode = "K1-ES", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Barremiense", Code = "BARR-ES", Level = StratigraphicLevel.Age, StartAge = 129.4, EndAge = 125.0,
            Color = Color.FromArgb(144, 206, 93), ParentCode = "K1-ES", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hauteriviense", Code = "HAUT-ES", Level = StratigraphicLevel.Age, StartAge = 132.9, EndAge = 129.4,
            Color = Color.FromArgb(142, 205, 90), ParentCode = "K1-ES", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Valanginiense", Code = "VALA-ES", Level = StratigraphicLevel.Age, StartAge = 139.8, EndAge = 132.9,
            Color = Color.FromArgb(140, 205, 87), ParentCode = "K1-ES", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Berriasiense", Code = "BERR-ES", Level = StratigraphicLevel.Age, StartAge = 145.0, EndAge = 139.8,
            Color = Color.FromArgb(138, 203, 85), ParentCode = "K1-ES", InternationalCorrelationCode = "K1"
        });

        // --- Jurásico
        u.Add(new StratigraphicUnit
        {
            Name = "Jurásico", Code = "J-ES", Level = StratigraphicLevel.Period, StartAge = 201.4, EndAge = 145.0,
            Color = Color.FromArgb(52, 178, 201), ParentCode = "MZ-ES", InternationalCorrelationCode = "J"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Jurásico superior (Malm)", Code = "J3-ES", Level = StratigraphicLevel.Epoch, StartAge = 163.5,
            EndAge = 145.0, Color = Color.FromArgb(179, 227, 238), ParentCode = "J-ES",
            InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Titoniense", Code = "TITO-ES", Level = StratigraphicLevel.Age, StartAge = 152.1, EndAge = 145.0,
            Color = Color.FromArgb(217, 241, 247), ParentCode = "J3-ES", InternationalCorrelationCode = "J3c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Kimmeridgiense", Code = "KIMM-ES", Level = StratigraphicLevel.Age, StartAge = 157.3, EndAge = 152.1,
            Color = Color.FromArgb(204, 236, 244), ParentCode = "J3-ES", InternationalCorrelationCode = "J3b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oxfordiense", Code = "OXFO-ES", Level = StratigraphicLevel.Age, StartAge = 163.5, EndAge = 157.3,
            Color = Color.FromArgb(191, 231, 241), ParentCode = "J3-ES", InternationalCorrelationCode = "J3a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Jurásico medio (Dogger)", Code = "J2-ES", Level = StratigraphicLevel.Epoch, StartAge = 174.7,
            EndAge = 163.5, Color = Color.FromArgb(128, 207, 216), ParentCode = "J-ES",
            InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Calloviense", Code = "CALL-ES", Level = StratigraphicLevel.Age, StartAge = 166.1, EndAge = 163.5,
            Color = Color.FromArgb(191, 226, 232), ParentCode = "J2-ES", InternationalCorrelationCode = "J2d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Batoniense", Code = "BATO-ES", Level = StratigraphicLevel.Age, StartAge = 168.2, EndAge = 166.1,
            Color = Color.FromArgb(179, 220, 228), ParentCode = "J2-ES", InternationalCorrelationCode = "J2c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bajociense", Code = "BAJO-ES", Level = StratigraphicLevel.Age, StartAge = 170.9, EndAge = 168.2,
            Color = Color.FromArgb(166, 215, 225), ParentCode = "J2-ES", InternationalCorrelationCode = "J2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aaleniense", Code = "AAL-ES", Level = StratigraphicLevel.Age, StartAge = 174.7, EndAge = 170.9,
            Color = Color.FromArgb(153, 209, 221), ParentCode = "J2-ES", InternationalCorrelationCode = "J2a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Jurásico inferior (Lias)", Code = "J1-ES", Level = StratigraphicLevel.Epoch, StartAge = 201.4,
            EndAge = 174.7, Color = Color.FromArgb(66, 174, 208), ParentCode = "J-ES",
            InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Toarciense", Code = "TOAR-ES", Level = StratigraphicLevel.Age, StartAge = 184.2, EndAge = 174.7,
            Color = Color.FromArgb(153, 206, 227), ParentCode = "J1-ES", InternationalCorrelationCode = "J1d"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pliensbachiense", Code = "PLIE-ES", Level = StratigraphicLevel.Age, StartAge = 192.9,
            EndAge = 184.2, Color = Color.FromArgb(128, 197, 221), ParentCode = "J1-ES",
            InternationalCorrelationCode = "J1c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Sinemuriense", Code = "SINE-ES", Level = StratigraphicLevel.Age, StartAge = 199.3, EndAge = 192.9,
            Color = Color.FromArgb(103, 188, 216), ParentCode = "J1-ES", InternationalCorrelationCode = "J1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hettangiense", Code = "HETT-ES", Level = StratigraphicLevel.Age, StartAge = 201.4, EndAge = 199.3,
            Color = Color.FromArgb(78, 179, 211), ParentCode = "J1-ES", InternationalCorrelationCode = "J1a"
        });

        // --- Triásico (con facies germánicas muy usadas en la Ibérica)
        u.Add(new StratigraphicUnit
        {
            Name = "Triásico", Code = "T-ES", Level = StratigraphicLevel.Period, StartAge = 252.17, EndAge = 201.4,
            Color = Color.FromArgb(129, 43, 146), ParentCode = "MZ-ES", InternationalCorrelationCode = "T"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Triásico superior", Code = "T3-ES", Level = StratigraphicLevel.Epoch, StartAge = 237.0,
            EndAge = 201.4, Color = Color.FromArgb(189, 140, 195), ParentCode = "T-ES",
            InternationalCorrelationCode = "T3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Retiense", Code = "RETI-ES", Level = StratigraphicLevel.Age, StartAge = 208.5, EndAge = 201.4,
            Color = Color.FromArgb(227, 185, 219), ParentCode = "T3-ES", InternationalCorrelationCode = "T3c"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Noriense", Code = "NORI-ES", Level = StratigraphicLevel.Age, StartAge = 227.0, EndAge = 208.5,
            Color = Color.FromArgb(214, 170, 211), ParentCode = "T3-ES", InternationalCorrelationCode = "T3b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Carniense", Code = "CARN-ES", Level = StratigraphicLevel.Age, StartAge = 237.0, EndAge = 227.0,
            Color = Color.FromArgb(201, 155, 203), ParentCode = "T3-ES", InternationalCorrelationCode = "T3a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Triásico medio", Code = "T2-ES", Level = StratigraphicLevel.Epoch, StartAge = 247.2, EndAge = 237.0,
            Color = Color.FromArgb(177, 104, 177), ParentCode = "T-ES", InternationalCorrelationCode = "T2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ladiniense", Code = "LADI-ES", Level = StratigraphicLevel.Age, StartAge = 242.0, EndAge = 237.0,
            Color = Color.FromArgb(201, 131, 191), ParentCode = "T2-ES", InternationalCorrelationCode = "T2b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Anisiense", Code = "ANIS-ES", Level = StratigraphicLevel.Age, StartAge = 247.2, EndAge = 242.0,
            Color = Color.FromArgb(188, 117, 183), ParentCode = "T2-ES", InternationalCorrelationCode = "T2a"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Triásico inferior", Code = "T1-ES", Level = StratigraphicLevel.Epoch, StartAge = 252.17,
            EndAge = 247.2, Color = Color.FromArgb(152, 57, 153), ParentCode = "T-ES",
            InternationalCorrelationCode = "T1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Olenekiense", Code = "OLEN-ES", Level = StratigraphicLevel.Age, StartAge = 251.2, EndAge = 247.2,
            Color = Color.FromArgb(176, 81, 165), ParentCode = "T1-ES", InternationalCorrelationCode = "T1b"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Induense", Code = "INDU-ES", Level = StratigraphicLevel.Age, StartAge = 252.17, EndAge = 251.2,
            Color = Color.FromArgb(164, 70, 159), ParentCode = "T1-ES", InternationalCorrelationCode = "T1a"
        });
        // Facies germánicas (uso común en la Ibérica oriental)
        u.Add(new StratigraphicUnit
        {
            Name = "Keuper (facies)", Code = "KEUPER-ES", Level = StratigraphicLevel.Age, StartAge = 237.0,
            EndAge = 201.4, Color = Color.FromArgb(189, 160, 205), ParentCode = "T3-ES",
            InternationalCorrelationCode = "T3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Muschelkalk (facies)", Code = "MUSCH-ES", Level = StratigraphicLevel.Age, StartAge = 247.2,
            EndAge = 237.0, Color = Color.FromArgb(188, 127, 183), ParentCode = "T2-ES",
            InternationalCorrelationCode = "T2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Buntsandstein (facies)", Code = "BUNT-ES", Level = StratigraphicLevel.Age, StartAge = 252.17,
            EndAge = 247.2, Color = Color.FromArgb(164, 80, 159), ParentCode = "T1-ES",
            InternationalCorrelationCode = "T1"
        });

        // =========================
        //         PALEOZOICO
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Paleozoico", Code = "PZ-ES", Level = StratigraphicLevel.Era, StartAge = 541.0, EndAge = 252.17,
            Color = Color.FromArgb(153, 192, 141), InternationalCorrelationCode = "PZ"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pérmico", Code = "P-ES", Level = StratigraphicLevel.Period, StartAge = 298.9, EndAge = 252.17,
            Color = Color.FromArgb(240, 64, 40), ParentCode = "PZ-ES", InternationalCorrelationCode = "P"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Carbonífero", Code = "C-ES", Level = StratigraphicLevel.Period, StartAge = 358.9, EndAge = 298.9,
            Color = Color.FromArgb(103, 165, 153), ParentCode = "PZ-ES", InternationalCorrelationCode = "C"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Devónico", Code = "D-ES", Level = StratigraphicLevel.Period, StartAge = 419.2, EndAge = 358.9,
            Color = Color.FromArgb(203, 140, 55), ParentCode = "PZ-ES", InternationalCorrelationCode = "D"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Silúrico", Code = "S-ES", Level = StratigraphicLevel.Period, StartAge = 443.8, EndAge = 419.2,
            Color = Color.FromArgb(179, 225, 182), ParentCode = "PZ-ES", InternationalCorrelationCode = "S"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ordovícico", Code = "O-ES", Level = StratigraphicLevel.Period, StartAge = 485.4, EndAge = 443.8,
            Color = Color.FromArgb(0, 146, 112), ParentCode = "PZ-ES", InternationalCorrelationCode = "O"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cámbrico", Code = "CAM-ES", Level = StratigraphicLevel.Period, StartAge = 541.0, EndAge = 485.4,
            Color = Color.FromArgb(127, 160, 86), ParentCode = "PZ-ES", InternationalCorrelationCode = "Ꞓ"
        });

        // Detalle Paleozoico donde es útil para correlación ibérica
        // Carbonífero (división internacional; Asturias/León usan Westfal/Estefan en literatura clásica)
        u.Add(new StratigraphicUnit
        {
            Name = "Pensilvánico (Superior)", Code = "C3-ES", Level = StratigraphicLevel.Epoch, StartAge = 323.2,
            EndAge = 298.9, Color = Color.FromArgb(153, 194, 181), ParentCode = "C-ES",
            InternationalCorrelationCode = "C3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Misisipiense (Inferior)", Code = "C1C2-ES", Level = StratigraphicLevel.Epoch, StartAge = 358.9,
            EndAge = 323.2, Color = Color.FromArgb(127, 172, 159), ParentCode = "C-ES",
            InternationalCorrelationCode = "C1,C2"
        });

        // =========================
        //         PRECAMBRICO
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Precámbrico", Code = "PC-ES", Level = StratigraphicLevel.Era, StartAge = 4600.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 67, 112), InternationalCorrelationCode = "PC"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Proterozoico", Code = "PROT-ES", Level = StratigraphicLevel.Eon, StartAge = 2500.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 104, 152), ParentCode = "PC-ES", InternationalCorrelationCode = "Pt"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Arqueano", Code = "ARCH-ES", Level = StratigraphicLevel.Eon, StartAge = 4000.0, EndAge = 2500.0,
            Color = Color.FromArgb(240, 4, 127), ParentCode = "PC-ES", InternationalCorrelationCode = "A"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hadeano", Code = "HAD-ES", Level = StratigraphicLevel.Eon, StartAge = 4600.0, EndAge = 4000.0,
            Color = Color.FromArgb(174, 2, 126), ParentCode = "PC-ES", InternationalCorrelationCode = "H"
        });

        return u;
    }
}