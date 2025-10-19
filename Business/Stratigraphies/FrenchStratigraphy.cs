// GeoscientistToolkit/Business/Stratigraphies/FrenchStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     Stratigraphie régionale française, enrichie avec les unités nationales et historiques
///     (terminologie largement utilisée en France : Sénonien, Néocomien, Stampien, Ludien,
///     subdivisions jurassiques complètes, Trias germanique en Alsace-Lorraine, etc.).
/// </summary>
public class FrenchStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public FrenchStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "Stratigraphie française";
    public string LanguageCode => "fr-FR";

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
        //        CENOZOÏQUE
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Cénozoïque", Code = "CZ-FR", Level = StratigraphicLevel.Era, StartAge = 66.0, EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29), InternationalCorrelationCode = "CZ"
        });

        // --- Quaternaire
        u.Add(new StratigraphicUnit
        {
            Name = "Quaternaire", Code = "Q-FR", Level = StratigraphicLevel.Period, StartAge = 2.58, EndAge = 0.0,
            Color = Color.FromArgb(249, 249, 127), ParentCode = "CZ-FR", InternationalCorrelationCode = "Q"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Holocène", Code = "HOLO-FR", Level = StratigraphicLevel.Epoch, StartAge = 0.0117, EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236), ParentCode = "Q-FR", InternationalCorrelationCode = "Q2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pléistocène", Code = "PLEIS-FR", Level = StratigraphicLevel.Epoch, StartAge = 2.58, EndAge = 0.0117,
            Color = Color.FromArgb(255, 242, 174), ParentCode = "Q-FR", InternationalCorrelationCode = "Q1"
        });
        // Grandes étapes glaciaires en France
        u.Add(new StratigraphicUnit
        {
            Name = "Würm (glaciaire)", Code = "WURM-FR", Level = StratigraphicLevel.Age, StartAge = 0.115,
            EndAge = 0.0117, Color = Color.FromArgb(255, 247, 199), ParentCode = "PLEIS-FR",
            InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Éémien (interglaciaire)", Code = "EEM-FR", Level = StratigraphicLevel.Age, StartAge = 0.126,
            EndAge = 0.115, Color = Color.FromArgb(255, 245, 189), ParentCode = "PLEIS-FR",
            InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Riss (glaciaire)", Code = "RISS-FR", Level = StratigraphicLevel.Age, StartAge = 0.230,
            EndAge = 0.126, Color = Color.FromArgb(255, 243, 179), ParentCode = "PLEIS-FR",
            InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Holsteinien (interglaciaire)", Code = "HOLST-FR", Level = StratigraphicLevel.Age, StartAge = 0.424,
            EndAge = 0.230, Color = Color.FromArgb(255, 241, 169), ParentCode = "PLEIS-FR",
            InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Mindel (glaciaire)", Code = "MINDEL-FR", Level = StratigraphicLevel.Age, StartAge = 0.850,
            EndAge = 0.424, Color = Color.FromArgb(255, 239, 159), ParentCode = "PLEIS-FR",
            InternationalCorrelationCode = "Q1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Günz (glaciaire)", Code = "GUNZ-FR", Level = StratigraphicLevel.Age, StartAge = 1.10,
            EndAge = 0.850, Color = Color.FromArgb(255, 237, 149), ParentCode = "PLEIS-FR",
            InternationalCorrelationCode = "Q1"
        });

        // --- Néogène
        u.Add(new StratigraphicUnit
        {
            Name = "Néogène", Code = "N-FR", Level = StratigraphicLevel.Period, StartAge = 23.03, EndAge = 2.58,
            Color = Color.FromArgb(255, 230, 25), ParentCode = "CZ-FR", InternationalCorrelationCode = "N"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pliocène", Code = "PLIO-FR", Level = StratigraphicLevel.Epoch, StartAge = 5.333, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 153), ParentCode = "N-FR", InternationalCorrelationCode = "N2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Miocène", Code = "MIO-FR", Level = StratigraphicLevel.Epoch, StartAge = 23.03, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 0), ParentCode = "N-FR", InternationalCorrelationCode = "N1"
        });
        // Pliocène (France: Zancléen / Plaisancien)
        u.Add(new StratigraphicUnit
        {
            Name = "Plaisancien", Code = "PLS-FR", Level = StratigraphicLevel.Age, StartAge = 3.60, EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 178), ParentCode = "PLIO-FR", InternationalCorrelationCode = "N2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Zancléen", Code = "ZANC-FR", Level = StratigraphicLevel.Age, StartAge = 5.333, EndAge = 3.60,
            Color = Color.FromArgb(255, 255, 191), ParentCode = "PLIO-FR", InternationalCorrelationCode = "N2"
        });
        // Miocène (Aquitanien → Messinien)
        u.Add(new StratigraphicUnit
        {
            Name = "Messinien", Code = "MESS-FR", Level = StratigraphicLevel.Age, StartAge = 7.246, EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 204), ParentCode = "MIO-FR", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Tortonien", Code = "TORT-FR", Level = StratigraphicLevel.Age, StartAge = 11.63, EndAge = 7.246,
            Color = Color.FromArgb(255, 255, 216), ParentCode = "MIO-FR", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Serravallien", Code = "SERR-FR", Level = StratigraphicLevel.Age, StartAge = 13.82, EndAge = 11.63,
            Color = Color.FromArgb(255, 255, 229), ParentCode = "MIO-FR", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Langhien", Code = "LANG-FR", Level = StratigraphicLevel.Age, StartAge = 15.97, EndAge = 13.82,
            Color = Color.FromArgb(255, 255, 240), ParentCode = "MIO-FR", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Burdigalien", Code = "BURD-FR", Level = StratigraphicLevel.Age, StartAge = 20.44, EndAge = 15.97,
            Color = Color.FromArgb(255, 255, 230), ParentCode = "MIO-FR", InternationalCorrelationCode = "N1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aquitanien", Code = "AQUI-FR", Level = StratigraphicLevel.Age, StartAge = 23.03, EndAge = 20.44,
            Color = Color.FromArgb(255, 255, 220), ParentCode = "MIO-FR", InternationalCorrelationCode = "N1"
        });

        // --- Paléogène
        u.Add(new StratigraphicUnit
        {
            Name = "Paléogène", Code = "PG-FR", Level = StratigraphicLevel.Period, StartAge = 66.0, EndAge = 23.03,
            Color = Color.FromArgb(253, 154, 82), ParentCode = "CZ-FR", InternationalCorrelationCode = "Pg"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oligocène", Code = "OLIG-FR", Level = StratigraphicLevel.Epoch, StartAge = 33.9, EndAge = 23.03,
            Color = Color.FromArgb(254, 192, 122), ParentCode = "PG-FR", InternationalCorrelationCode = "Pg3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Éocène", Code = "EOC-FR", Level = StratigraphicLevel.Epoch, StartAge = 56.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 180, 108), ParentCode = "PG-FR", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Paléocène", Code = "PALEO-FR", Level = StratigraphicLevel.Epoch, StartAge = 66.0, EndAge = 56.0,
            Color = Color.FromArgb(253, 167, 95), ParentCode = "PG-FR", InternationalCorrelationCode = "Pg1"
        });
        // Oligocène – terminologie française classique
        u.Add(new StratigraphicUnit
        {
            Name = "Chattien", Code = "CHATT-FR", Level = StratigraphicLevel.Age, StartAge = 27.82, EndAge = 23.03,
            Color = Color.FromArgb(254, 210, 155), ParentCode = "OLIG-FR", InternationalCorrelationCode = "Pg3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Rupélien", Code = "RUPEL-FR", Level = StratigraphicLevel.Age, StartAge = 33.9, EndAge = 27.82,
            Color = Color.FromArgb(254, 202, 140), ParentCode = "OLIG-FR", InternationalCorrelationCode = "Pg3"
        });
        // Regroupements historiques : Stampien (≈ Oligocène supérieur/moyen en bassin de Paris)
        u.Add(new StratigraphicUnit
        {
            Name = "Stampien (trad.)", Code = "STAMPIEN-FR", Level = StratigraphicLevel.Age, StartAge = 33.9,
            EndAge = 23.03, Color = Color.FromArgb(254, 220, 170), ParentCode = "OLIG-FR",
            InternationalCorrelationCode = "Pg3"
        });
        // Éocène – Yprésien subdivisé (Sparnacien/Cuisien), + Lutétien/Bartonnien/Priabonien, + Ludien (régional)
        u.Add(new StratigraphicUnit
        {
            Name = "Priabonien", Code = "PRIAB-FR", Level = StratigraphicLevel.Age, StartAge = 38.0, EndAge = 33.9,
            Color = Color.FromArgb(253, 189, 123), ParentCode = "EOC-FR", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bartonnien", Code = "BART-FR", Level = StratigraphicLevel.Age, StartAge = 41.2, EndAge = 38.0,
            Color = Color.FromArgb(253, 196, 135), ParentCode = "EOC-FR", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Lutétien", Code = "LUTET-FR", Level = StratigraphicLevel.Age, StartAge = 47.8, EndAge = 41.2,
            Color = Color.FromArgb(253, 203, 146), ParentCode = "EOC-FR", InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cuisien (Yprésien)", Code = "CUISIEN-FR", Level = StratigraphicLevel.Age, StartAge = 53.0,
            EndAge = 47.8, Color = Color.FromArgb(253, 210, 158), ParentCode = "EOC-FR",
            InternationalCorrelationCode = "Pg2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Sparnacien (Yprésien)", Code = "SPARNAC-FR", Level = StratigraphicLevel.Age, StartAge = 56.0,
            EndAge = 53.0, Color = Color.FromArgb(253, 215, 166), ParentCode = "EOC-FR",
            InternationalCorrelationCode = "Pg2"
        });
        // Paléocène
        u.Add(new StratigraphicUnit
        {
            Name = "Thanétien", Code = "THAN-FR", Level = StratigraphicLevel.Age, StartAge = 59.2, EndAge = 56.0,
            Color = Color.FromArgb(253, 176, 106), ParentCode = "PALEO-FR", InternationalCorrelationCode = "Pg1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Sélandien", Code = "SELA-FR", Level = StratigraphicLevel.Age, StartAge = 61.6, EndAge = 59.2,
            Color = Color.FromArgb(253, 182, 114), ParentCode = "PALEO-FR", InternationalCorrelationCode = "Pg1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Danien", Code = "DAN-FR", Level = StratigraphicLevel.Age, StartAge = 66.0, EndAge = 61.6,
            Color = Color.FromArgb(253, 188, 123), ParentCode = "PALEO-FR", InternationalCorrelationCode = "Pg1"
        });

        // =========================
        //         MESOZOÏQUE
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Mésozoïque", Code = "MZ-FR", Level = StratigraphicLevel.Era, StartAge = 252.17, EndAge = 66.0,
            Color = Color.FromArgb(103, 197, 202), InternationalCorrelationCode = "MZ"
        });

        // --- Crétacé
        u.Add(new StratigraphicUnit
        {
            Name = "Crétacé", Code = "K-FR", Level = StratigraphicLevel.Period, StartAge = 145.0, EndAge = 66.0,
            Color = Color.FromArgb(127, 198, 78), ParentCode = "MZ-FR", InternationalCorrelationCode = "K"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Crétacé supérieur", Code = "K2-FR", Level = StratigraphicLevel.Epoch, StartAge = 100.5,
            EndAge = 66.0, Color = Color.FromArgb(166, 216, 74), ParentCode = "K-FR",
            InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Crétacé inférieur", Code = "K1-FR", Level = StratigraphicLevel.Epoch, StartAge = 145.0,
            EndAge = 100.5, Color = Color.FromArgb(140, 205, 87), ParentCode = "K-FR",
            InternationalCorrelationCode = "K1"
        });
        // Crétacé supérieur – détails + regroupement français "Sénonien"
        u.Add(new StratigraphicUnit
        {
            Name = "Maastrichtien", Code = "MAAS-FR", Level = StratigraphicLevel.Age, StartAge = 72.1, EndAge = 66.0,
            Color = Color.FromArgb(175, 219, 86), ParentCode = "K2-FR", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Campanien", Code = "CAMP-FR", Level = StratigraphicLevel.Age, StartAge = 83.6, EndAge = 72.1,
            Color = Color.FromArgb(171, 217, 82), ParentCode = "K2-FR", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Santonien", Code = "SANT-FR", Level = StratigraphicLevel.Age, StartAge = 86.3, EndAge = 83.6,
            Color = Color.FromArgb(168, 216, 78), ParentCode = "K2-FR", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Coniacien", Code = "CONI-FR", Level = StratigraphicLevel.Age, StartAge = 89.8, EndAge = 86.3,
            Color = Color.FromArgb(164, 215, 74), ParentCode = "K2-FR", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Turonien", Code = "TURON-FR", Level = StratigraphicLevel.Age, StartAge = 93.9, EndAge = 89.8,
            Color = Color.FromArgb(160, 212, 71), ParentCode = "K2-FR", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cénomanien", Code = "CENO-FR", Level = StratigraphicLevel.Age, StartAge = 100.5, EndAge = 93.9,
            Color = Color.FromArgb(156, 210, 68), ParentCode = "K2-FR", InternationalCorrelationCode = "K2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Sénonien (trad.)", Code = "SENON-FR", Level = StratigraphicLevel.Age, StartAge = 89.8,
            EndAge = 72.1, Color = Color.FromArgb(210, 236, 120), ParentCode = "K2-FR",
            InternationalCorrelationCode = "K2"
        });
        // Crétacé inférieur – détails + regroupement français "Néocomien"
        u.Add(new StratigraphicUnit
        {
            Name = "Albien", Code = "ALB-FR", Level = StratigraphicLevel.Age, StartAge = 113.0, EndAge = 100.5,
            Color = Color.FromArgb(150, 207, 103), ParentCode = "K1-FR", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aptien", Code = "APT-FR", Level = StratigraphicLevel.Age, StartAge = 125.0, EndAge = 113.0,
            Color = Color.FromArgb(146, 206, 96), ParentCode = "K1-FR", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Barrémien", Code = "BARR-FR", Level = StratigraphicLevel.Age, StartAge = 129.4, EndAge = 125.0,
            Color = Color.FromArgb(144, 206, 93), ParentCode = "K1-FR", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hauterivien", Code = "HAUT-FR", Level = StratigraphicLevel.Age, StartAge = 132.9, EndAge = 129.4,
            Color = Color.FromArgb(142, 205, 90), ParentCode = "K1-FR", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Valanginien", Code = "VALA-FR", Level = StratigraphicLevel.Age, StartAge = 139.8, EndAge = 132.9,
            Color = Color.FromArgb(140, 205, 87), ParentCode = "K1-FR", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Berriasien", Code = "BERR-FR", Level = StratigraphicLevel.Age, StartAge = 145.0, EndAge = 139.8,
            Color = Color.FromArgb(138, 203, 85), ParentCode = "K1-FR", InternationalCorrelationCode = "K1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Néocomien (trad.)", Code = "NEOCOM-FR", Level = StratigraphicLevel.Age, StartAge = 145.0,
            EndAge = 125.0, Color = Color.FromArgb(180, 220, 130), ParentCode = "K1-FR",
            InternationalCorrelationCode = "K1"
        });

        // --- Jurassique (terminologie française complète)
        u.Add(new StratigraphicUnit
        {
            Name = "Jurassique", Code = "J-FR", Level = StratigraphicLevel.Period, StartAge = 201.4, EndAge = 145.0,
            Color = Color.FromArgb(52, 178, 201), ParentCode = "MZ-FR", InternationalCorrelationCode = "J"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Jurassique supérieur (Malm)", Code = "J3-FR", Level = StratigraphicLevel.Epoch, StartAge = 163.5,
            EndAge = 145.0, Color = Color.FromArgb(179, 227, 238), ParentCode = "J-FR",
            InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Portlandien", Code = "PORT-FR", Level = StratigraphicLevel.Age, StartAge = 152.1, EndAge = 145.0,
            Color = Color.FromArgb(217, 241, 247), ParentCode = "J3-FR", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Kimméridgien", Code = "KIMM-FR", Level = StratigraphicLevel.Age, StartAge = 157.3, EndAge = 152.1,
            Color = Color.FromArgb(204, 236, 244), ParentCode = "J3-FR", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Oxfordien", Code = "OXF-FR", Level = StratigraphicLevel.Age, StartAge = 163.5, EndAge = 157.3,
            Color = Color.FromArgb(191, 231, 241), ParentCode = "J3-FR", InternationalCorrelationCode = "J3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Jurassique moyen (Dogger)", Code = "J2-FR", Level = StratigraphicLevel.Epoch, StartAge = 174.7,
            EndAge = 163.5, Color = Color.FromArgb(128, 207, 216), ParentCode = "J-FR",
            InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Callovien", Code = "CALLO-FR", Level = StratigraphicLevel.Age, StartAge = 166.1, EndAge = 163.5,
            Color = Color.FromArgb(191, 226, 232), ParentCode = "J2-FR", InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bathonien", Code = "BATH-FR", Level = StratigraphicLevel.Age, StartAge = 168.2, EndAge = 166.1,
            Color = Color.FromArgb(179, 220, 228), ParentCode = "J2-FR", InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Bajocien", Code = "BAJO-FR", Level = StratigraphicLevel.Age, StartAge = 170.9, EndAge = 168.2,
            Color = Color.FromArgb(166, 215, 225), ParentCode = "J2-FR", InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Aalénien", Code = "AAL-FR", Level = StratigraphicLevel.Age, StartAge = 174.7, EndAge = 170.9,
            Color = Color.FromArgb(153, 209, 221), ParentCode = "J2-FR", InternationalCorrelationCode = "J2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Jurassique inférieur (Lias)", Code = "J1-FR", Level = StratigraphicLevel.Epoch, StartAge = 201.4,
            EndAge = 174.7, Color = Color.FromArgb(66, 174, 208), ParentCode = "J-FR",
            InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Toarcien", Code = "TOAR-FR", Level = StratigraphicLevel.Age, StartAge = 184.2, EndAge = 174.7,
            Color = Color.FromArgb(153, 206, 227), ParentCode = "J1-FR", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Pliensbachien", Code = "PLB-FR", Level = StratigraphicLevel.Age, StartAge = 192.9, EndAge = 184.2,
            Color = Color.FromArgb(128, 197, 221), ParentCode = "J1-FR", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Sinémurien", Code = "SINE-FR", Level = StratigraphicLevel.Age, StartAge = 199.3, EndAge = 192.9,
            Color = Color.FromArgb(103, 188, 216), ParentCode = "J1-FR", InternationalCorrelationCode = "J1"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hettangien", Code = "HETT-FR", Level = StratigraphicLevel.Age, StartAge = 201.4, EndAge = 199.3,
            Color = Color.FromArgb(78, 179, 211), ParentCode = "J1-FR", InternationalCorrelationCode = "J1"
        });

        // --- Trias (incluant la terminologie germanique utilisée dans l'Est de la France)
        u.Add(new StratigraphicUnit
        {
            Name = "Trias", Code = "T-FR", Level = StratigraphicLevel.Period, StartAge = 252.17, EndAge = 201.4,
            Color = Color.FromArgb(129, 43, 146), ParentCode = "MZ-FR", InternationalCorrelationCode = "T"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Trias supérieur", Code = "T3-FR", Level = StratigraphicLevel.Epoch, StartAge = 237.0,
            EndAge = 201.4, Color = Color.FromArgb(189, 140, 195), ParentCode = "T-FR",
            InternationalCorrelationCode = "T3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Trias moyen", Code = "T2-FR", Level = StratigraphicLevel.Epoch, StartAge = 247.2, EndAge = 237.0,
            Color = Color.FromArgb(177, 104, 177), ParentCode = "T-FR", InternationalCorrelationCode = "T2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Trias inférieur", Code = "T1-FR", Level = StratigraphicLevel.Epoch, StartAge = 252.17,
            EndAge = 247.2, Color = Color.FromArgb(152, 57, 153), ParentCode = "T-FR",
            InternationalCorrelationCode = "T1"
        });
        // Correspondants germaniques (Alsace-Lorraine)
        u.Add(new StratigraphicUnit
        {
            Name = "Keuper", Code = "KEUPER-FR", Level = StratigraphicLevel.Age, StartAge = 237.0, EndAge = 201.4,
            Color = Color.FromArgb(201, 155, 203), ParentCode = "T3-FR", InternationalCorrelationCode = "T3"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Muschelkalk", Code = "MUSCH-FR", Level = StratigraphicLevel.Age, StartAge = 247.2, EndAge = 237.0,
            Color = Color.FromArgb(188, 117, 183), ParentCode = "T2-FR", InternationalCorrelationCode = "T2"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Buntsandstein", Code = "BUNT-FR", Level = StratigraphicLevel.Age, StartAge = 252.17, EndAge = 247.2,
            Color = Color.FromArgb(164, 70, 159), ParentCode = "T1-FR", InternationalCorrelationCode = "T1"
        });

        // =========================
        //         PALÉOZOÏQUE
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Paléozoïque", Code = "PZ-FR", Level = StratigraphicLevel.Era, StartAge = 541.0, EndAge = 252.17,
            Color = Color.FromArgb(153, 192, 141), InternationalCorrelationCode = "PZ"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Permien", Code = "P-FR", Level = StratigraphicLevel.Period, StartAge = 298.9, EndAge = 252.17,
            Color = Color.FromArgb(240, 64, 40), ParentCode = "PZ-FR", InternationalCorrelationCode = "P"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Carbonifère", Code = "C-FR", Level = StratigraphicLevel.Period, StartAge = 358.9, EndAge = 298.9,
            Color = Color.FromArgb(103, 165, 153), ParentCode = "PZ-FR", InternationalCorrelationCode = "C"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Dévonien", Code = "D-FR", Level = StratigraphicLevel.Period, StartAge = 419.2, EndAge = 358.9,
            Color = Color.FromArgb(203, 140, 55), ParentCode = "PZ-FR", InternationalCorrelationCode = "D"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Silurien", Code = "S-FR", Level = StratigraphicLevel.Period, StartAge = 443.8, EndAge = 419.2,
            Color = Color.FromArgb(179, 225, 182), ParentCode = "PZ-FR", InternationalCorrelationCode = "S"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Ordovicien", Code = "O-FR", Level = StratigraphicLevel.Period, StartAge = 485.4, EndAge = 443.8,
            Color = Color.FromArgb(0, 146, 112), ParentCode = "PZ-FR", InternationalCorrelationCode = "O"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Cambrien", Code = "CAM-FR", Level = StratigraphicLevel.Period, StartAge = 541.0, EndAge = 485.4,
            Color = Color.FromArgb(127, 160, 86), ParentCode = "PZ-FR", InternationalCorrelationCode = "Ꞓ"
        });

        // =========================
        //         PRÉCAMBRIEN
        // =========================
        u.Add(new StratigraphicUnit
        {
            Name = "Précambrien", Code = "PC-FR", Level = StratigraphicLevel.Era, StartAge = 4600.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 67, 112), InternationalCorrelationCode = "PC"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Protérozoïque", Code = "PROT-FR", Level = StratigraphicLevel.Eon, StartAge = 2500.0, EndAge = 541.0,
            Color = Color.FromArgb(247, 104, 152), ParentCode = "PC-FR", InternationalCorrelationCode = "Pt"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Archéen", Code = "ARCHE-FR", Level = StratigraphicLevel.Eon, StartAge = 4000.0, EndAge = 2500.0,
            Color = Color.FromArgb(240, 4, 127), ParentCode = "PC-FR", InternationalCorrelationCode = "A"
        });
        u.Add(new StratigraphicUnit
        {
            Name = "Hadéen", Code = "HADE-FR", Level = StratigraphicLevel.Eon, StartAge = 4600.0, EndAge = 4000.0,
            Color = Color.FromArgb(174, 2, 126), ParentCode = "PC-FR", InternationalCorrelationCode = "H"
        });

        return u;
    }
}