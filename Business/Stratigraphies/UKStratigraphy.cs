//GeoscientistToolkit/Business/Stratigraphies/UkStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     United Kingdom regional stratigraphy with British-specific units and nomenclature
///     Includes classic British stages like Ludlow, Wenlock, Ashgill, Caradoc, Llandeilo, Llanvirn, etc.
///     Many of these are international stratotype reference sections (GSSPs)
/// </summary>
public class UkStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public UkStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "United Kingdom Stratigraphy";
    public string LanguageCode => "en-GB";

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

        // CENOZOIC ERA
        units.Add(new StratigraphicUnit
        {
            Name = "Cenozoic",
            Code = "CZ-GB",
            Level = StratigraphicLevel.Era,
            StartAge = 66.0,
            EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29),
            InternationalCorrelationCode = "CZ"
        });

        // QUATERNARY
        units.Add(new StratigraphicUnit
        {
            Name = "Quaternary",
            Code = "Q-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 2.58,
            EndAge = 0.0,
            Color = Color.FromArgb(249, 249, 127),
            ParentCode = "CZ-GB",
            InternationalCorrelationCode = "Q"
        });

        // Flandrian (British term for Holocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Flandrian",
            Code = "FLAN",
            Level = StratigraphicLevel.Epoch,
            StartAge = 0.0117,
            EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236),
            ParentCode = "Q-GB",
            InternationalCorrelationCode = "Q2"
        });

        // Pleistocene (British glacial terminology)
        units.Add(new StratigraphicUnit
        {
            Name = "Pleistocene",
            Code = "PLEIS-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 2.58,
            EndAge = 0.0117,
            Color = Color.FromArgb(255, 242, 174),
            ParentCode = "Q-GB",
            InternationalCorrelationCode = "Q1"
        });

        // Devensian (Last glacial period in Britain - equivalent to Wisconsinan/Weichselian)
        units.Add(new StratigraphicUnit
        {
            Name = "Devensian",
            Code = "DEVENS",
            Level = StratigraphicLevel.Age,
            StartAge = 0.115,
            EndAge = 0.0117,
            Color = Color.FromArgb(255, 247, 199),
            ParentCode = "PLEIS-GB",
            InternationalCorrelationCode = "Q1"
        });

        // Ipswichian (Interglacial - British specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Ipswichian",
            Code = "IPSW",
            Level = StratigraphicLevel.Age,
            StartAge = 0.130,
            EndAge = 0.115,
            Color = Color.FromArgb(255, 245, 189),
            ParentCode = "PLEIS-GB",
            InternationalCorrelationCode = "Q1"
        });

        // Wolstonian (Glacial period - British specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Wolstonian",
            Code = "WOLST",
            Level = StratigraphicLevel.Age,
            StartAge = 0.352,
            EndAge = 0.130,
            Color = Color.FromArgb(255, 243, 179),
            ParentCode = "PLEIS-GB",
            InternationalCorrelationCode = "Q1"
        });

        // Hoxnian (Interglacial - British specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Hoxnian",
            Code = "HOXN",
            Level = StratigraphicLevel.Age,
            StartAge = 0.424,
            EndAge = 0.352,
            Color = Color.FromArgb(255, 241, 169),
            ParentCode = "PLEIS-GB",
            InternationalCorrelationCode = "Q1"
        });

        // Anglian (Major glacial period - British specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Anglian",
            Code = "ANGL",
            Level = StratigraphicLevel.Age,
            StartAge = 0.478,
            EndAge = 0.424,
            Color = Color.FromArgb(255, 239, 159),
            ParentCode = "PLEIS-GB",
            InternationalCorrelationCode = "Q1"
        });

        // Cromerian Complex (Interglacials - British specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Cromerian Complex",
            Code = "CROM",
            Level = StratigraphicLevel.Age,
            StartAge = 0.866,
            EndAge = 0.478,
            Color = Color.FromArgb(255, 237, 149),
            ParentCode = "PLEIS-GB",
            InternationalCorrelationCode = "Q1"
        });

        // NEOGENE
        units.Add(new StratigraphicUnit
        {
            Name = "Neogene",
            Code = "N-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 23.03,
            EndAge = 2.58,
            Color = Color.FromArgb(255, 230, 25),
            ParentCode = "CZ-GB",
            InternationalCorrelationCode = "N"
        });

        // Pliocene (Limited British deposits)
        units.Add(new StratigraphicUnit
        {
            Name = "Pliocene",
            Code = "PLIO-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 5.333,
            EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 153),
            ParentCode = "N-GB",
            InternationalCorrelationCode = "N2"
        });

        // Miocene (Very limited British deposits)
        units.Add(new StratigraphicUnit
        {
            Name = "Miocene",
            Code = "MIO-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 23.03,
            EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 0),
            ParentCode = "N-GB",
            InternationalCorrelationCode = "N1"
        });

        // PALAEOGENE
        units.Add(new StratigraphicUnit
        {
            Name = "Palaeogene",
            Code = "PG-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 66.0,
            EndAge = 23.03,
            Color = Color.FromArgb(253, 154, 82),
            ParentCode = "CZ-GB",
            InternationalCorrelationCode = "Pg"
        });

        // Oligocene (Very limited British deposits)
        units.Add(new StratigraphicUnit
        {
            Name = "Oligocene",
            Code = "OLIG-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 33.9,
            EndAge = 23.03,
            Color = Color.FromArgb(254, 192, 122),
            ParentCode = "PG-GB",
            InternationalCorrelationCode = "Pg3"
        });

        // Eocene (London Clay, etc.)
        units.Add(new StratigraphicUnit
        {
            Name = "Eocene",
            Code = "EOC-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 56.0,
            EndAge = 33.9,
            Color = Color.FromArgb(253, 180, 108),
            ParentCode = "PG-GB",
            InternationalCorrelationCode = "Pg2"
        });

        // Palaeocene (Thanetian, etc.)
        units.Add(new StratigraphicUnit
        {
            Name = "Palaeocene",
            Code = "PALEO-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 66.0,
            EndAge = 56.0,
            Color = Color.FromArgb(253, 167, 95),
            ParentCode = "PG-GB",
            InternationalCorrelationCode = "Pg1"
        });

        // MESOZOIC ERA
        units.Add(new StratigraphicUnit
        {
            Name = "Mesozoic",
            Code = "MZ-GB",
            Level = StratigraphicLevel.Era,
            StartAge = 252.17,
            EndAge = 66.0,
            Color = Color.FromArgb(103, 197, 202),
            InternationalCorrelationCode = "MZ"
        });

        // CRETACEOUS - Chalk deposits are iconic in Britain
        units.Add(new StratigraphicUnit
        {
            Name = "Cretaceous",
            Code = "K-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 145.0,
            EndAge = 66.0,
            Color = Color.FromArgb(127, 198, 78),
            ParentCode = "MZ-GB",
            InternationalCorrelationCode = "K"
        });

        // Upper Cretaceous - Chalk Group
        units.Add(new StratigraphicUnit
        {
            Name = "Upper Cretaceous",
            Code = "KU-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 100.5,
            EndAge = 66.0,
            Color = Color.FromArgb(166, 216, 74),
            ParentCode = "K-GB",
            InternationalCorrelationCode = "K2"
        });

        // Lower Cretaceous - Wealden Group
        units.Add(new StratigraphicUnit
        {
            Name = "Lower Cretaceous",
            Code = "KL-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 145.0,
            EndAge = 100.5,
            Color = Color.FromArgb(140, 205, 87),
            ParentCode = "K-GB",
            InternationalCorrelationCode = "K1"
        });

        // JURASSIC - Type section in Britain!
        units.Add(new StratigraphicUnit
        {
            Name = "Jurassic",
            Code = "J-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 201.4,
            EndAge = 145.0,
            Color = Color.FromArgb(52, 178, 201),
            ParentCode = "MZ-GB",
            InternationalCorrelationCode = "J"
        });

        // Portlandian (Late Jurassic - British stage)
        units.Add(new StratigraphicUnit
        {
            Name = "Portlandian",
            Code = "PORT",
            Level = StratigraphicLevel.Age,
            StartAge = 152.1,
            EndAge = 145.0,
            Color = Color.FromArgb(204, 236, 244),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J3"
        });

        // Kimmeridgian (Late Jurassic - Named after Kimmeridge, Dorset)
        units.Add(new StratigraphicUnit
        {
            Name = "Kimmeridgian",
            Code = "KIMM",
            Level = StratigraphicLevel.Age,
            StartAge = 157.3,
            EndAge = 152.1,
            Color = Color.FromArgb(191, 231, 241),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J3"
        });

        // Oxfordian (Late Jurassic - Named after Oxford)
        units.Add(new StratigraphicUnit
        {
            Name = "Oxfordian",
            Code = "OXFO",
            Level = StratigraphicLevel.Age,
            StartAge = 163.5,
            EndAge = 157.3,
            Color = Color.FromArgb(179, 227, 238),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J3"
        });

        // Callovian (Middle Jurassic - Named after Kellaways, Wiltshire)
        units.Add(new StratigraphicUnit
        {
            Name = "Callovian",
            Code = "CALL",
            Level = StratigraphicLevel.Age,
            StartAge = 166.1,
            EndAge = 163.5,
            Color = Color.FromArgb(153, 217, 228),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J2"
        });

        // Bathonian (Middle Jurassic - Named after Bath)
        units.Add(new StratigraphicUnit
        {
            Name = "Bathonian",
            Code = "BATH",
            Level = StratigraphicLevel.Age,
            StartAge = 168.3,
            EndAge = 166.1,
            Color = Color.FromArgb(140, 213, 222),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J2"
        });

        // Bajocian (Middle Jurassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Bajocian",
            Code = "BAJO",
            Level = StratigraphicLevel.Age,
            StartAge = 170.3,
            EndAge = 168.3,
            Color = Color.FromArgb(128, 207, 216),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J2"
        });

        // Aalenian (Middle Jurassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Aalenian",
            Code = "AALE",
            Level = StratigraphicLevel.Age,
            StartAge = 174.7,
            EndAge = 170.3,
            Color = Color.FromArgb(115, 201, 209),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J2"
        });

        // Toarcian (Lower Jurassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Toarcian",
            Code = "TOAR",
            Level = StratigraphicLevel.Age,
            StartAge = 182.7,
            EndAge = 174.7,
            Color = Color.FromArgb(103, 197, 202),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J1"
        });

        // Pliensbachian (Lower Jurassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Pliensbachian",
            Code = "PLIE",
            Level = StratigraphicLevel.Age,
            StartAge = 190.8,
            EndAge = 182.7,
            Color = Color.FromArgb(90, 190, 195),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J1"
        });

        // Sinemurian (Lower Jurassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Sinemurian",
            Code = "SINE",
            Level = StratigraphicLevel.Age,
            StartAge = 199.3,
            EndAge = 190.8,
            Color = Color.FromArgb(78, 184, 189),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J1"
        });

        // Hettangian (Lower Jurassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Hettangian",
            Code = "HETT",
            Level = StratigraphicLevel.Age,
            StartAge = 201.4,
            EndAge = 199.3,
            Color = Color.FromArgb(66, 174, 208),
            ParentCode = "J-GB",
            InternationalCorrelationCode = "J1"
        });

        // TRIASSIC
        units.Add(new StratigraphicUnit
        {
            Name = "Triassic",
            Code = "TR-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 252.17,
            EndAge = 201.4,
            Color = Color.FromArgb(129, 43, 146),
            ParentCode = "MZ-GB",
            InternationalCorrelationCode = "T"
        });

        // Rhaetian (Late Triassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Rhaetian",
            Code = "RHAE",
            Level = StratigraphicLevel.Age,
            StartAge = 208.5,
            EndAge = 201.4,
            Color = Color.FromArgb(227, 185, 219),
            ParentCode = "TR-GB",
            InternationalCorrelationCode = "T3"
        });

        // Keuper (British usage - Upper Triassic)
        units.Add(new StratigraphicUnit
        {
            Name = "Keuper (Mercia Mudstone)",
            Code = "KEUP-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 237.0,
            EndAge = 208.5,
            Color = Color.FromArgb(214, 170, 211),
            ParentCode = "TR-GB",
            InternationalCorrelationCode = "T3"
        });

        // Muschelkalk equivalent (largely absent in Britain)
        units.Add(new StratigraphicUnit
        {
            Name = "Middle Triassic",
            Code = "TRM-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 247.2,
            EndAge = 237.0,
            Color = Color.FromArgb(177, 104, 177),
            ParentCode = "TR-GB",
            InternationalCorrelationCode = "T2"
        });

        // Bunter (British usage - Lower Triassic, Sherwood Sandstone)
        units.Add(new StratigraphicUnit
        {
            Name = "Bunter (Sherwood Sandstone)",
            Code = "BUNT-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 252.17,
            EndAge = 247.2,
            Color = Color.FromArgb(152, 57, 153),
            ParentCode = "TR-GB",
            InternationalCorrelationCode = "T1"
        });

        // PALAEOZOIC ERA
        units.Add(new StratigraphicUnit
        {
            Name = "Palaeozoic",
            Code = "PZ-GB",
            Level = StratigraphicLevel.Era,
            StartAge = 541.0,
            EndAge = 252.17,
            Color = Color.FromArgb(153, 192, 141),
            InternationalCorrelationCode = "PZ"
        });

        // PERMIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Permian",
            Code = "P-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 298.9,
            EndAge = 252.17,
            Color = Color.FromArgb(240, 64, 40),
            ParentCode = "PZ-GB",
            InternationalCorrelationCode = "P"
        });

        // Zechstein (British usage)
        units.Add(new StratigraphicUnit
        {
            Name = "Zechstein",
            Code = "ZECH",
            Level = StratigraphicLevel.Epoch,
            StartAge = 259.1,
            EndAge = 252.17,
            Color = Color.FromArgb(251, 167, 148),
            ParentCode = "P-GB",
            InternationalCorrelationCode = "P3"
        });

        // Rotliegend (British usage)
        units.Add(new StratigraphicUnit
        {
            Name = "Rotliegend",
            Code = "ROTL",
            Level = StratigraphicLevel.Epoch,
            StartAge = 298.9,
            EndAge = 259.1,
            Color = Color.FromArgb(252, 128, 104),
            ParentCode = "P-GB",
            InternationalCorrelationCode = "P1,P2"
        });

        // CARBONIFEROUS (British terminology is standard!)
        units.Add(new StratigraphicUnit
        {
            Name = "Carboniferous",
            Code = "C-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 358.9,
            EndAge = 298.9,
            Color = Color.FromArgb(103, 165, 153),
            ParentCode = "PZ-GB",
            InternationalCorrelationCode = "C"
        });

        // Silesian (Upper Carboniferous - British term)
        units.Add(new StratigraphicUnit
        {
            Name = "Silesian",
            Code = "SILES",
            Level = StratigraphicLevel.Epoch,
            StartAge = 323.2,
            EndAge = 298.9,
            Color = Color.FromArgb(153, 194, 181),
            ParentCode = "C-GB",
            InternationalCorrelationCode = "C3"
        });

        // Stephanian (British usage)
        units.Add(new StratigraphicUnit
        {
            Name = "Stephanian",
            Code = "STEPH",
            Level = StratigraphicLevel.Age,
            StartAge = 303.7,
            EndAge = 298.9,
            Color = Color.FromArgb(179, 206, 197),
            ParentCode = "SILES",
            InternationalCorrelationCode = "C3"
        });

        // Westphalian (British Coal Measures)
        units.Add(new StratigraphicUnit
        {
            Name = "Westphalian",
            Code = "WESTP",
            Level = StratigraphicLevel.Age,
            StartAge = 315.2,
            EndAge = 303.7,
            Color = Color.FromArgb(166, 199, 183),
            ParentCode = "SILES",
            InternationalCorrelationCode = "C3"
        });

        // Namurian (Millstone Grit)
        units.Add(new StratigraphicUnit
        {
            Name = "Namurian",
            Code = "NAMUR",
            Level = StratigraphicLevel.Age,
            StartAge = 323.2,
            EndAge = 315.2,
            Color = Color.FromArgb(153, 194, 181),
            ParentCode = "SILES",
            InternationalCorrelationCode = "C3"
        });

        // Dinantian (Lower Carboniferous - British term)
        units.Add(new StratigraphicUnit
        {
            Name = "Dinantian",
            Code = "DINAN",
            Level = StratigraphicLevel.Epoch,
            StartAge = 358.9,
            EndAge = 323.2,
            Color = Color.FromArgb(103, 165, 153),
            ParentCode = "C-GB",
            InternationalCorrelationCode = "C1,C2"
        });

        // Brigantian (British stage)
        units.Add(new StratigraphicUnit
        {
            Name = "Brigantian",
            Code = "BRIG",
            Level = StratigraphicLevel.Age,
            StartAge = 330.9,
            EndAge = 323.2,
            Color = Color.FromArgb(140, 176, 165),
            ParentCode = "DINAN",
            InternationalCorrelationCode = "C2"
        });

        // Asbian (British stage)
        units.Add(new StratigraphicUnit
        {
            Name = "Asbian",
            Code = "ASBI",
            Level = StratigraphicLevel.Age,
            StartAge = 338.0,
            EndAge = 330.9,
            Color = Color.FromArgb(127, 172, 159),
            ParentCode = "DINAN",
            InternationalCorrelationCode = "C2"
        });

        // Holkerian (British stage)
        units.Add(new StratigraphicUnit
        {
            Name = "Holkerian",
            Code = "HOLK",
            Level = StratigraphicLevel.Age,
            StartAge = 340.5,
            EndAge = 338.0,
            Color = Color.FromArgb(120, 170, 157),
            ParentCode = "DINAN",
            InternationalCorrelationCode = "C2"
        });

        // Arundian (British stage)
        units.Add(new StratigraphicUnit
        {
            Name = "Arundian",
            Code = "ARUN",
            Level = StratigraphicLevel.Age,
            StartAge = 346.7,
            EndAge = 340.5,
            Color = Color.FromArgb(115, 169, 156),
            ParentCode = "DINAN",
            InternationalCorrelationCode = "C1,C2"
        });

        // Chadian (British stage)
        units.Add(new StratigraphicUnit
        {
            Name = "Chadian",
            Code = "CHAD-GB",
            Level = StratigraphicLevel.Age,
            StartAge = 352.0,
            EndAge = 346.7,
            Color = Color.FromArgb(109, 167, 153),
            ParentCode = "DINAN",
            InternationalCorrelationCode = "C1"
        });

        // Courceyan (British stage)
        units.Add(new StratigraphicUnit
        {
            Name = "Courceyan",
            Code = "COUR",
            Level = StratigraphicLevel.Age,
            StartAge = 358.9,
            EndAge = 352.0,
            Color = Color.FromArgb(103, 165, 153),
            ParentCode = "DINAN",
            InternationalCorrelationCode = "C1"
        });

        // DEVONIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Devonian",
            Code = "D-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 419.2,
            EndAge = 358.9,
            Color = Color.FromArgb(203, 140, 55),
            ParentCode = "PZ-GB",
            InternationalCorrelationCode = "D"
        });

        // Upper Devonian
        units.Add(new StratigraphicUnit
        {
            Name = "Upper Devonian",
            Code = "DU-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 382.7,
            EndAge = 358.9,
            Color = Color.FromArgb(241, 200, 104),
            ParentCode = "D-GB",
            InternationalCorrelationCode = "D3"
        });

        // Middle Devonian
        units.Add(new StratigraphicUnit
        {
            Name = "Middle Devonian",
            Code = "DM-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 393.3,
            EndAge = 382.7,
            Color = Color.FromArgb(241, 225, 157),
            ParentCode = "D-GB",
            InternationalCorrelationCode = "D2"
        });

        // Lower Devonian (Old Red Sandstone)
        units.Add(new StratigraphicUnit
        {
            Name = "Lower Devonian",
            Code = "DL-GB",
            Level = StratigraphicLevel.Epoch,
            StartAge = 419.2,
            EndAge = 393.3,
            Color = Color.FromArgb(229, 196, 104),
            ParentCode = "D-GB",
            InternationalCorrelationCode = "D1"
        });

        // SILURIAN - Type section in Wales! (Ludlow, Wenlock, Llandovery)
        units.Add(new StratigraphicUnit
        {
            Name = "Silurian",
            Code = "S-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 443.8,
            EndAge = 419.2,
            Color = Color.FromArgb(179, 225, 182),
            ParentCode = "PZ-GB",
            InternationalCorrelationCode = "S"
        });

        // Pridoli (British terminology)
        units.Add(new StratigraphicUnit
        {
            Name = "Pridoli",
            Code = "PRID",
            Level = StratigraphicLevel.Epoch,
            StartAge = 423.0,
            EndAge = 419.2,
            Color = Color.FromArgb(230, 245, 225),
            ParentCode = "S-GB",
            InternationalCorrelationCode = "S4"
        });

        // Ludlow (Named after Ludlow, Shropshire - GSSP)
        units.Add(new StratigraphicUnit
        {
            Name = "Ludlow",
            Code = "LUDL",
            Level = StratigraphicLevel.Epoch,
            StartAge = 427.4,
            EndAge = 423.0,
            Color = Color.FromArgb(191, 230, 195),
            ParentCode = "S-GB",
            InternationalCorrelationCode = "S3"
        });

        // Wenlock (Named after Wenlock Edge, Shropshire - GSSP)
        units.Add(new StratigraphicUnit
        {
            Name = "Wenlock",
            Code = "WENL",
            Level = StratigraphicLevel.Epoch,
            StartAge = 433.4,
            EndAge = 427.4,
            Color = Color.FromArgb(204, 235, 209),
            ParentCode = "S-GB",
            InternationalCorrelationCode = "S2"
        });

        // Llandovery (Named after Llandovery, Wales - GSSP)
        units.Add(new StratigraphicUnit
        {
            Name = "Llandovery",
            Code = "LLAND",
            Level = StratigraphicLevel.Epoch,
            StartAge = 443.8,
            EndAge = 433.4,
            Color = Color.FromArgb(217, 240, 223),
            ParentCode = "S-GB",
            InternationalCorrelationCode = "S1"
        });

        // ORDOVICIAN - Welsh type sections (Ashgill, Caradoc, Llandeilo, Llanvirn, Arenig, Tremadoc)
        units.Add(new StratigraphicUnit
        {
            Name = "Ordovician",
            Code = "O-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 485.4,
            EndAge = 443.8,
            Color = Color.FromArgb(0, 146, 112),
            ParentCode = "PZ-GB",
            InternationalCorrelationCode = "O"
        });

        // Ashgill (Late Ordovician - Named after Ashgill, Cumbria - CLASSIC BRITISH STAGE)
        units.Add(new StratigraphicUnit
        {
            Name = "Ashgill",
            Code = "ASHG",
            Level = StratigraphicLevel.Epoch,
            StartAge = 458.4,
            EndAge = 443.8,
            Color = Color.FromArgb(127, 202, 147),
            ParentCode = "O-GB",
            InternationalCorrelationCode = "O3"
        });

        // Cautleyan (Late Ashgillian substage)
        units.Add(new StratigraphicUnit
        {
            Name = "Cautleyan",
            Code = "CAUT",
            Level = StratigraphicLevel.Age,
            StartAge = 453.0,
            EndAge = 450.0,
            Color = Color.FromArgb(140, 207, 157),
            ParentCode = "ASHG",
            InternationalCorrelationCode = "O3"
        });

        // Rawtheyan (Late Ashgillian substage)
        units.Add(new StratigraphicUnit
        {
            Name = "Rawtheyan",
            Code = "RAWT",
            Level = StratigraphicLevel.Age,
            StartAge = 450.0,
            EndAge = 443.8,
            Color = Color.FromArgb(153, 212, 167),
            ParentCode = "ASHG",
            InternationalCorrelationCode = "O3"
        });

        // Caradoc (Late Ordovician - Named after Caer Caradoc, Shropshire - CLASSIC BRITISH STAGE)
        units.Add(new StratigraphicUnit
        {
            Name = "Caradoc",
            Code = "CARA",
            Level = StratigraphicLevel.Epoch,
            StartAge = 470.0,
            EndAge = 458.4,
            Color = Color.FromArgb(76, 177, 136),
            ParentCode = "O-GB",
            InternationalCorrelationCode = "O2,O3"
        });

        // Onnian (Caradoc substage)
        units.Add(new StratigraphicUnit
        {
            Name = "Onnian",
            Code = "ONNI",
            Level = StratigraphicLevel.Age,
            StartAge = 461.0,
            EndAge = 458.4,
            Color = Color.FromArgb(89, 182, 141),
            ParentCode = "CARA",
            InternationalCorrelationCode = "O3"
        });

        // Actonian (Caradoc substage)
        units.Add(new StratigraphicUnit
        {
            Name = "Actonian",
            Code = "ACTO",
            Level = StratigraphicLevel.Age,
            StartAge = 464.0,
            EndAge = 461.0,
            Color = Color.FromArgb(102, 187, 146),
            ParentCode = "CARA",
            InternationalCorrelationCode = "O2,O3"
        });

        // Marshbrookian (Caradoc substage)
        units.Add(new StratigraphicUnit
        {
            Name = "Marshbrookian",
            Code = "MARS",
            Level = StratigraphicLevel.Age,
            StartAge = 467.0,
            EndAge = 464.0,
            Color = Color.FromArgb(115, 192, 151),
            ParentCode = "CARA",
            InternationalCorrelationCode = "O2"
        });

        // Longvillian (Caradoc substage)
        units.Add(new StratigraphicUnit
        {
            Name = "Longvillian",
            Code = "LONG",
            Level = StratigraphicLevel.Age,
            StartAge = 470.0,
            EndAge = 467.0,
            Color = Color.FromArgb(128, 197, 156),
            ParentCode = "CARA",
            InternationalCorrelationCode = "O2"
        });

        // Llandeilo (Middle Ordovician - Named after Llandeilo, Wales - CLASSIC BRITISH STAGE)
        units.Add(new StratigraphicUnit
        {
            Name = "Llandeilo",
            Code = "LLDEI",
            Level = StratigraphicLevel.Epoch,
            StartAge = 475.0,
            EndAge = 470.0,
            Color = Color.FromArgb(51, 169, 126),
            ParentCode = "O-GB",
            InternationalCorrelationCode = "O2"
        });

        // Llanvirn (Middle Ordovician - Named after Llanvirn, Wales - CLASSIC BRITISH STAGE)
        units.Add(new StratigraphicUnit
        {
            Name = "Llanvirn",
            Code = "LLVIR",
            Level = StratigraphicLevel.Epoch,
            StartAge = 478.6,
            EndAge = 475.0,
            Color = Color.FromArgb(26, 160, 117),
            ParentCode = "O-GB",
            InternationalCorrelationCode = "O2"
        });

        // Arenig (Early to Middle Ordovician - Named after Arenig Fawr, Wales - CLASSIC BRITISH STAGE)
        units.Add(new StratigraphicUnit
        {
            Name = "Arenig",
            Code = "AREN",
            Level = StratigraphicLevel.Epoch,
            StartAge = 485.4,
            EndAge = 478.6,
            Color = Color.FromArgb(0, 152, 108),
            ParentCode = "O-GB",
            InternationalCorrelationCode = "O1,O2"
        });

        // Tremadoc (Early Ordovician - Named after Tremadoc, Wales - CLASSIC BRITISH STAGE)
        units.Add(new StratigraphicUnit
        {
            Name = "Tremadoc",
            Code = "TREM",
            Level = StratigraphicLevel.Epoch,
            StartAge = 485.4,
            EndAge = 477.7,
            Color = Color.FromArgb(0, 146, 112),
            ParentCode = "O-GB",
            InternationalCorrelationCode = "O1"
        });

        // CAMBRIAN - Welsh type sections
        units.Add(new StratigraphicUnit
        {
            Name = "Cambrian",
            Code = "CAM-GB",
            Level = StratigraphicLevel.Period,
            StartAge = 541.0,
            EndAge = 485.4,
            Color = Color.FromArgb(127, 160, 86),
            ParentCode = "PZ-GB",
            InternationalCorrelationCode = "Ꞓ"
        });

        // Merioneth (Late Cambrian - Welsh terminology)
        units.Add(new StratigraphicUnit
        {
            Name = "Merioneth",
            Code = "MERIO",
            Level = StratigraphicLevel.Epoch,
            StartAge = 497.0,
            EndAge = 485.4,
            Color = Color.FromArgb(166, 185, 108),
            ParentCode = "CAM-GB",
            InternationalCorrelationCode = "Ꞓ3"
        });

        // St David's (Middle Cambrian - Welsh terminology)
        units.Add(new StratigraphicUnit
        {
            Name = "St David's",
            Code = "STDAV",
            Level = StratigraphicLevel.Epoch,
            StartAge = 509.0,
            EndAge = 497.0,
            Color = Color.FromArgb(179, 191, 128),
            ParentCode = "CAM-GB",
            InternationalCorrelationCode = "Ꞓ2"
        });

        // Comley (Early Cambrian - Named after Comley, Shropshire)
        units.Add(new StratigraphicUnit
        {
            Name = "Comley",
            Code = "COML",
            Level = StratigraphicLevel.Epoch,
            StartAge = 521.0,
            EndAge = 509.0,
            Color = Color.FromArgb(153, 179, 115),
            ParentCode = "CAM-GB",
            InternationalCorrelationCode = "Ꞓ2"
        });

        // Caerfai (Early Cambrian - Welsh terminology)
        units.Add(new StratigraphicUnit
        {
            Name = "Caerfai",
            Code = "CAERF",
            Level = StratigraphicLevel.Epoch,
            StartAge = 541.0,
            EndAge = 521.0,
            Color = Color.FromArgb(140, 176, 97),
            ParentCode = "CAM-GB",
            InternationalCorrelationCode = "Ꞓ1"
        });

        // PRECAMBRIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Precambrian",
            Code = "PC-GB",
            Level = StratigraphicLevel.Era,
            StartAge = 4600.0,
            EndAge = 541.0,
            Color = Color.FromArgb(247, 67, 112),
            InternationalCorrelationCode = "PC"
        });

        // Proterozoic (Torridonian, Dalradian, etc.)
        units.Add(new StratigraphicUnit
        {
            Name = "Proterozoic",
            Code = "PROT-GB",
            Level = StratigraphicLevel.Eon,
            StartAge = 2500.0,
            EndAge = 541.0,
            Color = Color.FromArgb(247, 104, 152),
            ParentCode = "PC-GB",
            InternationalCorrelationCode = "Pt"
        });

        // Torridonian (Late Proterozoic - NW Scotland)
        units.Add(new StratigraphicUnit
        {
            Name = "Torridonian",
            Code = "TORR",
            Level = StratigraphicLevel.Epoch,
            StartAge = 1200.0,
            EndAge = 541.0,
            Color = Color.FromArgb(247, 130, 167),
            ParentCode = "PROT-GB",
            InternationalCorrelationCode = "Pt"
        });

        // Lewisian Complex (Archean - NW Scotland)
        units.Add(new StratigraphicUnit
        {
            Name = "Lewisian Complex",
            Code = "LEWIS",
            Level = StratigraphicLevel.Eon,
            StartAge = 3000.0,
            EndAge = 1600.0,
            Color = Color.FromArgb(240, 4, 127),
            ParentCode = "PC-GB",
            InternationalCorrelationCode = "A"
        });

        return units;
    }
}