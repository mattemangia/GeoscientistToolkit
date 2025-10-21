//GeoscientistToolkit/Business/Stratigraphies/USStratigraphy.cs

using System.Drawing;

namespace GeoscientistToolkit.Business.Stratigraphies;

/// <summary>
///     United States regional stratigraphy with North American-specific units and nomenclature
///     Includes regional series like Cincinnatian, Champlainian, Gulf Coast stages, etc.
/// </summary>
public class USStratigraphy : IStratigraphy
{
    private readonly List<StratigraphicUnit> _units;

    public USStratigraphy()
    {
        _units = InitializeUnits();
    }

    public string Name => "United States Stratigraphy";
    public string LanguageCode => "en-US";

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
            Code = "CZ-US",
            Level = StratigraphicLevel.Era,
            StartAge = 66.0,
            EndAge = 0.0,
            Color = Color.FromArgb(242, 249, 29),
            InternationalCorrelationCode = "CZ"
        });

        // QUATERNARY - North American Land Mammal Ages used extensively
        units.Add(new StratigraphicUnit
        {
            Name = "Quaternary",
            Code = "Q-US",
            Level = StratigraphicLevel.Period,
            StartAge = 2.58,
            EndAge = 0.0,
            Color = Color.FromArgb(249, 249, 127),
            ParentCode = "CZ-US",
            InternationalCorrelationCode = "Q"
        });

        // Holocene
        units.Add(new StratigraphicUnit
        {
            Name = "Holocene",
            Code = "HOL-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 0.0117,
            EndAge = 0.0,
            Color = Color.FromArgb(254, 242, 236),
            ParentCode = "Q-US",
            InternationalCorrelationCode = "Q2"
        });

        // Pleistocene
        units.Add(new StratigraphicUnit
        {
            Name = "Pleistocene",
            Code = "PLEIS-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 2.58,
            EndAge = 0.0117,
            Color = Color.FromArgb(255, 242, 174),
            ParentCode = "Q-US",
            InternationalCorrelationCode = "Q1"
        });

        // Wisconsinan (Late Pleistocene glacial stage - North American specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Wisconsinan",
            Code = "WISC",
            Level = StratigraphicLevel.Age,
            StartAge = 0.115,
            EndAge = 0.0117,
            Color = Color.FromArgb(255, 247, 199),
            ParentCode = "PLEIS-US",
            InternationalCorrelationCode = "Q1"
        });

        // Sangamonian (interglacial - North American specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Sangamonian",
            Code = "SANG",
            Level = StratigraphicLevel.Age,
            StartAge = 0.130,
            EndAge = 0.115,
            Color = Color.FromArgb(255, 245, 189),
            ParentCode = "PLEIS-US",
            InternationalCorrelationCode = "Q1"
        });

        // Illinoian (glacial stage - North American specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Illinoian",
            Code = "ILL",
            Level = StratigraphicLevel.Age,
            StartAge = 0.191,
            EndAge = 0.130,
            Color = Color.FromArgb(255, 243, 179),
            ParentCode = "PLEIS-US",
            InternationalCorrelationCode = "Q1"
        });

        // Yarmouthian (interglacial - North American specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Yarmouthian",
            Code = "YARM",
            Level = StratigraphicLevel.Age,
            StartAge = 0.424,
            EndAge = 0.191,
            Color = Color.FromArgb(255, 241, 169),
            ParentCode = "PLEIS-US",
            InternationalCorrelationCode = "Q1"
        });

        // Kansan (glacial stage - North American specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Kansan",
            Code = "KANS",
            Level = StratigraphicLevel.Age,
            StartAge = 0.780,
            EndAge = 0.424,
            Color = Color.FromArgb(255, 239, 159),
            ParentCode = "PLEIS-US",
            InternationalCorrelationCode = "Q1"
        });

        // Aftonian (interglacial - North American specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Aftonian",
            Code = "AFT",
            Level = StratigraphicLevel.Age,
            StartAge = 1.5,
            EndAge = 0.780,
            Color = Color.FromArgb(255, 237, 149),
            ParentCode = "PLEIS-US",
            InternationalCorrelationCode = "Q1"
        });

        // Nebraskan (glacial stage - North American specific)
        units.Add(new StratigraphicUnit
        {
            Name = "Nebraskan",
            Code = "NEB",
            Level = StratigraphicLevel.Age,
            StartAge = 2.58,
            EndAge = 1.5,
            Color = Color.FromArgb(255, 235, 139),
            ParentCode = "PLEIS-US",
            InternationalCorrelationCode = "Q1"
        });

        // NEOGENE
        units.Add(new StratigraphicUnit
        {
            Name = "Neogene",
            Code = "N-US",
            Level = StratigraphicLevel.Period,
            StartAge = 23.03,
            EndAge = 2.58,
            Color = Color.FromArgb(255, 230, 25),
            ParentCode = "CZ-US",
            InternationalCorrelationCode = "N"
        });

        // Pliocene
        units.Add(new StratigraphicUnit
        {
            Name = "Pliocene",
            Code = "PLIO-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 5.333,
            EndAge = 2.58,
            Color = Color.FromArgb(255, 255, 153),
            ParentCode = "N-US",
            InternationalCorrelationCode = "N2"
        });

        // Blancan (North American Land Mammal Age)
        units.Add(new StratigraphicUnit
        {
            Name = "Blancan (NALMA)",
            Code = "BLANC",
            Level = StratigraphicLevel.Age,
            StartAge = 4.75,
            EndAge = 1.8,
            Color = Color.FromArgb(255, 255, 178),
            ParentCode = "PLIO-US",
            InternationalCorrelationCode = "N2"
        });

        // Miocene
        units.Add(new StratigraphicUnit
        {
            Name = "Miocene",
            Code = "MIO-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 23.03,
            EndAge = 5.333,
            Color = Color.FromArgb(255, 255, 0),
            ParentCode = "N-US",
            InternationalCorrelationCode = "N1"
        });

        // Hemphillian (NALMA - Late Miocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Hemphillian (NALMA)",
            Code = "HEMPH",
            Level = StratigraphicLevel.Age,
            StartAge = 10.3,
            EndAge = 4.75,
            Color = Color.FromArgb(255, 255, 51),
            ParentCode = "MIO-US",
            InternationalCorrelationCode = "N1"
        });

        // Clarendonian (NALMA - Middle to Late Miocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Clarendonian (NALMA)",
            Code = "CLAR",
            Level = StratigraphicLevel.Age,
            StartAge = 13.6,
            EndAge = 10.3,
            Color = Color.FromArgb(255, 255, 77),
            ParentCode = "MIO-US",
            InternationalCorrelationCode = "N1"
        });

        // Barstovian (NALMA - Middle Miocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Barstovian (NALMA)",
            Code = "BARST",
            Level = StratigraphicLevel.Age,
            StartAge = 16.0,
            EndAge = 13.6,
            Color = Color.FromArgb(255, 255, 102),
            ParentCode = "MIO-US",
            InternationalCorrelationCode = "N1"
        });

        // Hemingfordian (NALMA - Early to Middle Miocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Hemingfordian (NALMA)",
            Code = "HEMING",
            Level = StratigraphicLevel.Age,
            StartAge = 20.6,
            EndAge = 16.0,
            Color = Color.FromArgb(255, 255, 128),
            ParentCode = "MIO-US",
            InternationalCorrelationCode = "N1"
        });

        // PALEOGENE
        units.Add(new StratigraphicUnit
        {
            Name = "Paleogene",
            Code = "PG-US",
            Level = StratigraphicLevel.Period,
            StartAge = 66.0,
            EndAge = 23.03,
            Color = Color.FromArgb(253, 154, 82),
            ParentCode = "CZ-US",
            InternationalCorrelationCode = "Pg"
        });

        // Oligocene
        units.Add(new StratigraphicUnit
        {
            Name = "Oligocene",
            Code = "OLIG-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 33.9,
            EndAge = 23.03,
            Color = Color.FromArgb(254, 192, 122),
            ParentCode = "PG-US",
            InternationalCorrelationCode = "Pg3"
        });

        // Arikareean (NALMA - Late Oligocene to Early Miocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Arikareean (NALMA)",
            Code = "ARIK",
            Level = StratigraphicLevel.Age,
            StartAge = 30.6,
            EndAge = 20.6,
            Color = Color.FromArgb(254, 207, 147),
            ParentCode = "OLIG-US",
            InternationalCorrelationCode = "Pg3,N1"
        });

        // Whitneyan (NALMA - Early Oligocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Whitneyan (NALMA)",
            Code = "WHIT",
            Level = StratigraphicLevel.Age,
            StartAge = 33.3,
            EndAge = 30.8,
            Color = Color.FromArgb(254, 214, 159),
            ParentCode = "OLIG-US",
            InternationalCorrelationCode = "Pg3"
        });

        // Eocene
        units.Add(new StratigraphicUnit
        {
            Name = "Eocene",
            Code = "EOC-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 56.0,
            EndAge = 33.9,
            Color = Color.FromArgb(253, 180, 108),
            ParentCode = "PG-US",
            InternationalCorrelationCode = "Pg2"
        });

        // Chadronian (NALMA - Late Eocene to Early Oligocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Chadronian (NALMA)",
            Code = "CHAD",
            Level = StratigraphicLevel.Age,
            StartAge = 37.2,
            EndAge = 33.3,
            Color = Color.FromArgb(253, 192, 122),
            ParentCode = "EOC-US",
            InternationalCorrelationCode = "Pg2,Pg3"
        });

        // Duchesnean (NALMA - Late Eocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Duchesnean (NALMA)",
            Code = "DUCH",
            Level = StratigraphicLevel.Age,
            StartAge = 40.4,
            EndAge = 37.2,
            Color = Color.FromArgb(253, 199, 133),
            ParentCode = "EOC-US",
            InternationalCorrelationCode = "Pg2"
        });

        // Uintan (NALMA - Middle Eocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Uintan (NALMA)",
            Code = "UINT",
            Level = StratigraphicLevel.Age,
            StartAge = 46.2,
            EndAge = 40.4,
            Color = Color.FromArgb(253, 206, 143),
            ParentCode = "EOC-US",
            InternationalCorrelationCode = "Pg2"
        });

        // Bridgerian (NALMA - Middle Eocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Bridgerian (NALMA)",
            Code = "BRIDG",
            Level = StratigraphicLevel.Age,
            StartAge = 50.3,
            EndAge = 46.2,
            Color = Color.FromArgb(253, 213, 154),
            ParentCode = "EOC-US",
            InternationalCorrelationCode = "Pg2"
        });

        // Wasatchian (NALMA - Early Eocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Wasatchian (NALMA)",
            Code = "WASAT",
            Level = StratigraphicLevel.Age,
            StartAge = 55.4,
            EndAge = 50.3,
            Color = Color.FromArgb(253, 220, 165),
            ParentCode = "EOC-US",
            InternationalCorrelationCode = "Pg2"
        });

        // Paleocene
        units.Add(new StratigraphicUnit
        {
            Name = "Paleocene",
            Code = "PALEO-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 66.0,
            EndAge = 56.0,
            Color = Color.FromArgb(253, 167, 95),
            ParentCode = "PG-US",
            InternationalCorrelationCode = "Pg1"
        });

        // Clarkforkian (NALMA - Late Paleocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Clarkforkian (NALMA)",
            Code = "CLARK",
            Level = StratigraphicLevel.Age,
            StartAge = 56.8,
            EndAge = 55.4,
            Color = Color.FromArgb(253, 174, 104),
            ParentCode = "PALEO-US",
            InternationalCorrelationCode = "Pg1"
        });

        // Tiffanian (NALMA - Middle to Late Paleocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Tiffanian (NALMA)",
            Code = "TIFF",
            Level = StratigraphicLevel.Age,
            StartAge = 60.2,
            EndAge = 56.8,
            Color = Color.FromArgb(253, 181, 113),
            ParentCode = "PALEO-US",
            InternationalCorrelationCode = "Pg1"
        });

        // Torrejonian (NALMA - Early Paleocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Torrejonian (NALMA)",
            Code = "TORR",
            Level = StratigraphicLevel.Age,
            StartAge = 63.3,
            EndAge = 60.2,
            Color = Color.FromArgb(253, 188, 123),
            ParentCode = "PALEO-US",
            InternationalCorrelationCode = "Pg1"
        });

        // Puercan (NALMA - Early Paleocene)
        units.Add(new StratigraphicUnit
        {
            Name = "Puercan (NALMA)",
            Code = "PUER",
            Level = StratigraphicLevel.Age,
            StartAge = 66.0,
            EndAge = 63.3,
            Color = Color.FromArgb(253, 195, 132),
            ParentCode = "PALEO-US",
            InternationalCorrelationCode = "Pg1"
        });

        // MESOZOIC ERA
        units.Add(new StratigraphicUnit
        {
            Name = "Mesozoic",
            Code = "MZ-US",
            Level = StratigraphicLevel.Era,
            StartAge = 252.17,
            EndAge = 66.0,
            Color = Color.FromArgb(103, 197, 202),
            InternationalCorrelationCode = "MZ"
        });

        // CRETACEOUS
        units.Add(new StratigraphicUnit
        {
            Name = "Cretaceous",
            Code = "K-US",
            Level = StratigraphicLevel.Period,
            StartAge = 145.0,
            EndAge = 66.0,
            Color = Color.FromArgb(127, 198, 78),
            ParentCode = "MZ-US",
            InternationalCorrelationCode = "K"
        });

        // Gulf Coast Stages (specific to Gulf Coastal Plain - widely used in US petroleum geology)

        // Navarroan (Late Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Navarroan (Gulf)",
            Code = "NAVA",
            Level = StratigraphicLevel.Age,
            StartAge = 70.6,
            EndAge = 66.0,
            Color = Color.FromArgb(166, 216, 74),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K2"
        });

        // Tayloran (Late Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Tayloran (Gulf)",
            Code = "TAYL",
            Level = StratigraphicLevel.Age,
            StartAge = 75.0,
            EndAge = 70.6,
            Color = Color.FromArgb(171, 217, 82),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K2"
        });

        // Austinian (Late Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Austinian (Gulf)",
            Code = "AUST",
            Level = StratigraphicLevel.Age,
            StartAge = 84.0,
            EndAge = 75.0,
            Color = Color.FromArgb(176, 218, 90),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K2"
        });

        // Eaglefordian (Late Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Eaglefordian (Gulf)",
            Code = "EAGL",
            Level = StratigraphicLevel.Age,
            StartAge = 90.0,
            EndAge = 84.0,
            Color = Color.FromArgb(181, 219, 98),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K2"
        });

        // Woodbinian (Late Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Woodbinian (Gulf)",
            Code = "WOOD",
            Level = StratigraphicLevel.Age,
            StartAge = 94.0,
            EndAge = 90.0,
            Color = Color.FromArgb(186, 220, 106),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K2"
        });

        // Washitan (Early Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Washitan (Gulf)",
            Code = "WASH",
            Level = StratigraphicLevel.Age,
            StartAge = 100.5,
            EndAge = 94.0,
            Color = Color.FromArgb(140, 205, 87),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K1,K2"
        });

        // Fredericksburgian (Early Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Fredericksburgian (Gulf)",
            Code = "FRED",
            Level = StratigraphicLevel.Age,
            StartAge = 107.0,
            EndAge = 100.5,
            Color = Color.FromArgb(145, 206, 95),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K1"
        });

        // Trinitian (Early Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Trinitian (Gulf)",
            Code = "TRIN",
            Level = StratigraphicLevel.Age,
            StartAge = 113.0,
            EndAge = 107.0,
            Color = Color.FromArgb(150, 207, 103),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K1"
        });

        // Coahuilan (Early Cretaceous - Gulf Coast)
        units.Add(new StratigraphicUnit
        {
            Name = "Coahuilan (Gulf)",
            Code = "COAH",
            Level = StratigraphicLevel.Age,
            StartAge = 145.0,
            EndAge = 113.0,
            Color = Color.FromArgb(155, 208, 111),
            ParentCode = "K-US",
            InternationalCorrelationCode = "K1"
        });

        // JURASSIC
        units.Add(new StratigraphicUnit
        {
            Name = "Jurassic",
            Code = "J-US",
            Level = StratigraphicLevel.Period,
            StartAge = 201.4,
            EndAge = 145.0,
            Color = Color.FromArgb(52, 178, 201),
            ParentCode = "MZ-US",
            InternationalCorrelationCode = "J"
        });

        // Upper Jurassic
        units.Add(new StratigraphicUnit
        {
            Name = "Upper Jurassic",
            Code = "JU-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 163.5,
            EndAge = 145.0,
            Color = Color.FromArgb(179, 227, 238),
            ParentCode = "J-US",
            InternationalCorrelationCode = "J3"
        });

        // Middle Jurassic
        units.Add(new StratigraphicUnit
        {
            Name = "Middle Jurassic",
            Code = "JM-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 174.7,
            EndAge = 163.5,
            Color = Color.FromArgb(128, 207, 216),
            ParentCode = "J-US",
            InternationalCorrelationCode = "J2"
        });

        // Lower Jurassic
        units.Add(new StratigraphicUnit
        {
            Name = "Lower Jurassic",
            Code = "JL-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 201.4,
            EndAge = 174.7,
            Color = Color.FromArgb(66, 174, 208),
            ParentCode = "J-US",
            InternationalCorrelationCode = "J1"
        });

        // TRIASSIC
        units.Add(new StratigraphicUnit
        {
            Name = "Triassic",
            Code = "TR-US",
            Level = StratigraphicLevel.Period,
            StartAge = 252.17,
            EndAge = 201.4,
            Color = Color.FromArgb(129, 43, 146),
            ParentCode = "MZ-US",
            InternationalCorrelationCode = "T"
        });

        // Newark Supergroup terminology (Eastern US - Atlantic rift basins)

        // Upper Triassic
        units.Add(new StratigraphicUnit
        {
            Name = "Upper Triassic",
            Code = "TRU-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 237.0,
            EndAge = 201.4,
            Color = Color.FromArgb(189, 140, 195),
            ParentCode = "TR-US",
            InternationalCorrelationCode = "T3"
        });

        // Middle Triassic
        units.Add(new StratigraphicUnit
        {
            Name = "Middle Triassic",
            Code = "TRM-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 247.2,
            EndAge = 237.0,
            Color = Color.FromArgb(177, 104, 177),
            ParentCode = "TR-US",
            InternationalCorrelationCode = "T2"
        });

        // Lower Triassic
        units.Add(new StratigraphicUnit
        {
            Name = "Lower Triassic",
            Code = "TRL-US",
            Level = StratigraphicLevel.Epoch,
            StartAge = 252.17,
            EndAge = 247.2,
            Color = Color.FromArgb(152, 57, 153),
            ParentCode = "TR-US",
            InternationalCorrelationCode = "T1"
        });

        // PALEOZOIC ERA
        units.Add(new StratigraphicUnit
        {
            Name = "Paleozoic",
            Code = "PZ-US",
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
            Code = "P-US",
            Level = StratigraphicLevel.Period,
            StartAge = 298.9,
            EndAge = 252.17,
            Color = Color.FromArgb(240, 64, 40),
            ParentCode = "PZ-US",
            InternationalCorrelationCode = "P"
        });

        // Ochoan (Late Permian - Southwestern US)
        units.Add(new StratigraphicUnit
        {
            Name = "Ochoan",
            Code = "OCHO",
            Level = StratigraphicLevel.Epoch,
            StartAge = 259.1,
            EndAge = 252.17,
            Color = Color.FromArgb(251, 167, 148),
            ParentCode = "P-US",
            InternationalCorrelationCode = "P3"
        });

        // Guadalupian (Middle Permian - West Texas terminology)
        units.Add(new StratigraphicUnit
        {
            Name = "Guadalupian",
            Code = "GUAD",
            Level = StratigraphicLevel.Epoch,
            StartAge = 273.01,
            EndAge = 259.1,
            Color = Color.FromArgb(251, 154, 133),
            ParentCode = "P-US",
            InternationalCorrelationCode = "P2"
        });

        // Leonardian (Early Permian - West Texas)
        units.Add(new StratigraphicUnit
        {
            Name = "Leonardian",
            Code = "LEON",
            Level = StratigraphicLevel.Epoch,
            StartAge = 284.4,
            EndAge = 273.01,
            Color = Color.FromArgb(251, 140, 118),
            ParentCode = "P-US",
            InternationalCorrelationCode = "P1"
        });

        // Wolfcampian (Early Permian - West Texas)
        units.Add(new StratigraphicUnit
        {
            Name = "Wolfcampian",
            Code = "WOLF",
            Level = StratigraphicLevel.Epoch,
            StartAge = 298.9,
            EndAge = 284.4,
            Color = Color.FromArgb(252, 128, 104),
            ParentCode = "P-US",
            InternationalCorrelationCode = "P1"
        });

        // PENNSYLVANIAN (UPPER CARBONIFEROUS - North American specific division)
        units.Add(new StratigraphicUnit
        {
            Name = "Pennsylvanian",
            Code = "PENN",
            Level = StratigraphicLevel.Period,
            StartAge = 323.2,
            EndAge = 298.9,
            Color = Color.FromArgb(153, 194, 181),
            ParentCode = "PZ-US",
            InternationalCorrelationCode = "C3"
        });

        // Virgilian (Late Pennsylvanian - Midcontinent US)
        units.Add(new StratigraphicUnit
        {
            Name = "Virgilian",
            Code = "VIRG",
            Level = StratigraphicLevel.Epoch,
            StartAge = 303.7,
            EndAge = 298.9,
            Color = Color.FromArgb(179, 206, 197),
            ParentCode = "PENN",
            InternationalCorrelationCode = "C3"
        });

        // Missourian (Late Pennsylvanian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Missourian",
            Code = "MISS",
            Level = StratigraphicLevel.Epoch,
            StartAge = 307.0,
            EndAge = 303.7,
            Color = Color.FromArgb(191, 208, 204),
            ParentCode = "PENN",
            InternationalCorrelationCode = "C3"
        });

        // Desmoinesian (Middle Pennsylvanian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Desmoinesian",
            Code = "DESM",
            Level = StratigraphicLevel.Epoch,
            StartAge = 315.2,
            EndAge = 307.0,
            Color = Color.FromArgb(166, 199, 183),
            ParentCode = "PENN",
            InternationalCorrelationCode = "C3"
        });

        // Atokan (Middle Pennsylvanian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Atokan",
            Code = "ATOK",
            Level = StratigraphicLevel.Epoch,
            StartAge = 318.1,
            EndAge = 315.2,
            Color = Color.FromArgb(179, 202, 187),
            ParentCode = "PENN",
            InternationalCorrelationCode = "C3"
        });

        // Morrowan (Early Pennsylvanian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Morrowan",
            Code = "MORR",
            Level = StratigraphicLevel.Epoch,
            StartAge = 323.2,
            EndAge = 318.1,
            Color = Color.FromArgb(153, 194, 181),
            ParentCode = "PENN",
            InternationalCorrelationCode = "C3"
        });

        // MISSISSIPPIAN (LOWER CARBONIFEROUS - North American specific division)
        units.Add(new StratigraphicUnit
        {
            Name = "Mississippian",
            Code = "MISS-P",
            Level = StratigraphicLevel.Period,
            StartAge = 358.9,
            EndAge = 323.2,
            Color = Color.FromArgb(103, 165, 153),
            ParentCode = "PZ-US",
            InternationalCorrelationCode = "C1,C2"
        });

        // Chesterian (Late Mississippian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Chesterian",
            Code = "CHEST",
            Level = StratigraphicLevel.Epoch,
            StartAge = 330.9,
            EndAge = 323.2,
            Color = Color.FromArgb(140, 176, 165),
            ParentCode = "MISS-P",
            InternationalCorrelationCode = "C2"
        });

        // Meramecian (Late Mississippian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Meramecian",
            Code = "MERAM",
            Level = StratigraphicLevel.Epoch,
            StartAge = 346.7,
            EndAge = 330.9,
            Color = Color.FromArgb(153, 180, 169),
            ParentCode = "MISS-P",
            InternationalCorrelationCode = "C2"
        });

        // Osagean (Early Mississippian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Osagean",
            Code = "OSAG",
            Level = StratigraphicLevel.Epoch,
            StartAge = 352.0,
            EndAge = 346.7,
            Color = Color.FromArgb(127, 172, 159),
            ParentCode = "MISS-P",
            InternationalCorrelationCode = "C1"
        });

        // Kinderhookian (Early Mississippian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Kinderhookian",
            Code = "KIND",
            Level = StratigraphicLevel.Epoch,
            StartAge = 358.9,
            EndAge = 352.0,
            Color = Color.FromArgb(115, 169, 156),
            ParentCode = "MISS-P",
            InternationalCorrelationCode = "C1"
        });

        // DEVONIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Devonian",
            Code = "D-US",
            Level = StratigraphicLevel.Period,
            StartAge = 419.2,
            EndAge = 358.9,
            Color = Color.FromArgb(203, 140, 55),
            ParentCode = "PZ-US",
            InternationalCorrelationCode = "D"
        });

        // Chatauquan (Late Devonian - New York State)
        units.Add(new StratigraphicUnit
        {
            Name = "Chautauquan",
            Code = "CHAUT",
            Level = StratigraphicLevel.Epoch,
            StartAge = 372.2,
            EndAge = 358.9,
            Color = Color.FromArgb(241, 200, 104),
            ParentCode = "D-US",
            InternationalCorrelationCode = "D3"
        });

        // Senecan (Late Devonian - New York State)
        units.Add(new StratigraphicUnit
        {
            Name = "Senecan",
            Code = "SENEC",
            Level = StratigraphicLevel.Epoch,
            StartAge = 382.7,
            EndAge = 372.2,
            Color = Color.FromArgb(229, 172, 77),
            ParentCode = "D-US",
            InternationalCorrelationCode = "D3"
        });

        // Erian (Middle Devonian - Eastern US)
        units.Add(new StratigraphicUnit
        {
            Name = "Erian",
            Code = "ERIAN",
            Level = StratigraphicLevel.Epoch,
            StartAge = 393.3,
            EndAge = 382.7,
            Color = Color.FromArgb(241, 225, 157),
            ParentCode = "D-US",
            InternationalCorrelationCode = "D2"
        });

        // Ulsterian (Middle Devonian - Eastern US)
        units.Add(new StratigraphicUnit
        {
            Name = "Ulsterian",
            Code = "ULST",
            Level = StratigraphicLevel.Epoch,
            StartAge = 407.6,
            EndAge = 393.3,
            Color = Color.FromArgb(229, 208, 117),
            ParentCode = "D-US",
            InternationalCorrelationCode = "D2"
        });

        // Deerparkian (Early Devonian - Eastern US)
        units.Add(new StratigraphicUnit
        {
            Name = "Deerparkian",
            Code = "DEER",
            Level = StratigraphicLevel.Epoch,
            StartAge = 410.8,
            EndAge = 407.6,
            Color = Color.FromArgb(229, 196, 104),
            ParentCode = "D-US",
            InternationalCorrelationCode = "D1"
        });

        // Helderbergian (Early Devonian - Eastern US)
        units.Add(new StratigraphicUnit
        {
            Name = "Helderbergian",
            Code = "HELD",
            Level = StratigraphicLevel.Epoch,
            StartAge = 419.2,
            EndAge = 410.8,
            Color = Color.FromArgb(229, 186, 91),
            ParentCode = "D-US",
            InternationalCorrelationCode = "D1"
        });

        // SILURIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Silurian",
            Code = "S-US",
            Level = StratigraphicLevel.Period,
            StartAge = 443.8,
            EndAge = 419.2,
            Color = Color.FromArgb(179, 225, 182),
            ParentCode = "PZ-US",
            InternationalCorrelationCode = "S"
        });

        // Cayugan (Late Silurian - New York terminology)
        units.Add(new StratigraphicUnit
        {
            Name = "Cayugan",
            Code = "CAYUG",
            Level = StratigraphicLevel.Epoch,
            StartAge = 423.0,
            EndAge = 419.2,
            Color = Color.FromArgb(191, 230, 195),
            ParentCode = "S-US",
            InternationalCorrelationCode = "S4"
        });

        // Niagaran (Middle Silurian - Great Lakes region)
        units.Add(new StratigraphicUnit
        {
            Name = "Niagaran",
            Code = "NIAG",
            Level = StratigraphicLevel.Epoch,
            StartAge = 430.5,
            EndAge = 423.0,
            Color = Color.FromArgb(204, 235, 209),
            ParentCode = "S-US",
            InternationalCorrelationCode = "S3"
        });

        // Alexandrian (Early Silurian - Midcontinent)
        units.Add(new StratigraphicUnit
        {
            Name = "Alexandrian",
            Code = "ALEX",
            Level = StratigraphicLevel.Epoch,
            StartAge = 443.8,
            EndAge = 430.5,
            Color = Color.FromArgb(217, 240, 223),
            ParentCode = "S-US",
            InternationalCorrelationCode = "S1,S2"
        });

        // ORDOVICIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Ordovician",
            Code = "O-US",
            Level = StratigraphicLevel.Period,
            StartAge = 485.4,
            EndAge = 443.8,
            Color = Color.FromArgb(0, 146, 112),
            ParentCode = "PZ-US",
            InternationalCorrelationCode = "O"
        });

        // Cincinnatian (Late Ordovician - Type section in Cincinnati, Ohio - CLASSIC US UNIT)
        units.Add(new StratigraphicUnit
        {
            Name = "Cincinnatian",
            Code = "CINC",
            Level = StratigraphicLevel.Epoch,
            StartAge = 458.4,
            EndAge = 443.8,
            Color = Color.FromArgb(127, 202, 147),
            ParentCode = "O-US",
            InternationalCorrelationCode = "O3"
        });

        // Champlainian (Middle Ordovician - Lake Champlain region - CLASSIC US UNIT)
        units.Add(new StratigraphicUnit
        {
            Name = "Champlainian",
            Code = "CHAMP",
            Level = StratigraphicLevel.Epoch,
            StartAge = 470.0,
            EndAge = 458.4,
            Color = Color.FromArgb(76, 177, 136),
            ParentCode = "O-US",
            InternationalCorrelationCode = "O2"
        });

        // Canadian (Early Ordovician - widespread in North America - CLASSIC US UNIT)
        units.Add(new StratigraphicUnit
        {
            Name = "Canadian",
            Code = "CANAD",
            Level = StratigraphicLevel.Epoch,
            StartAge = 485.4,
            EndAge = 470.0,
            Color = Color.FromArgb(26, 152, 124),
            ParentCode = "O-US",
            InternationalCorrelationCode = "O1"
        });

        // CAMBRIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Cambrian",
            Code = "CAM-US",
            Level = StratigraphicLevel.Period,
            StartAge = 541.0,
            EndAge = 485.4,
            Color = Color.FromArgb(127, 160, 86),
            ParentCode = "PZ-US",
            InternationalCorrelationCode = "Ꞓ"
        });

        // Croixan (Late Cambrian - Upper Mississippi Valley)
        units.Add(new StratigraphicUnit
        {
            Name = "Croixan",
            Code = "CROIX",
            Level = StratigraphicLevel.Epoch,
            StartAge = 497.0,
            EndAge = 485.4,
            Color = Color.FromArgb(166, 185, 108),
            ParentCode = "CAM-US",
            InternationalCorrelationCode = "Ꞓ3"
        });

        // Albertan (Middle Cambrian - Western North America)
        units.Add(new StratigraphicUnit
        {
            Name = "Albertan",
            Code = "ALBERT",
            Level = StratigraphicLevel.Epoch,
            StartAge = 509.0,
            EndAge = 497.0,
            Color = Color.FromArgb(179, 191, 128),
            ParentCode = "CAM-US",
            InternationalCorrelationCode = "Ꞓ2"
        });

        // Waucoban (Early Cambrian - Great Basin)
        units.Add(new StratigraphicUnit
        {
            Name = "Waucoban",
            Code = "WAUC",
            Level = StratigraphicLevel.Epoch,
            StartAge = 541.0,
            EndAge = 509.0,
            Color = Color.FromArgb(140, 176, 97),
            ParentCode = "CAM-US",
            InternationalCorrelationCode = "Ꞓ1"
        });

        // PRECAMBRIAN
        units.Add(new StratigraphicUnit
        {
            Name = "Precambrian",
            Code = "PC-US",
            Level = StratigraphicLevel.Era,
            StartAge = 4600.0,
            EndAge = 541.0,
            Color = Color.FromArgb(247, 67, 112),
            InternationalCorrelationCode = "PC"
        });

        // Proterozoic
        units.Add(new StratigraphicUnit
        {
            Name = "Proterozoic",
            Code = "PROT-US",
            Level = StratigraphicLevel.Eon,
            StartAge = 2500.0,
            EndAge = 541.0,
            Color = Color.FromArgb(247, 104, 152),
            ParentCode = "PC-US",
            InternationalCorrelationCode = "Pt"
        });

        // Archean
        units.Add(new StratigraphicUnit
        {
            Name = "Archean",
            Code = "ARCH-US",
            Level = StratigraphicLevel.Eon,
            StartAge = 4000.0,
            EndAge = 2500.0,
            Color = Color.FromArgb(240, 4, 127),
            ParentCode = "PC-US",
            InternationalCorrelationCode = "A"
        });

        // Hadean
        units.Add(new StratigraphicUnit
        {
            Name = "Hadean",
            Code = "HAD-US",
            Level = StratigraphicLevel.Eon,
            StartAge = 4600.0,
            EndAge = 4000.0,
            Color = Color.FromArgb(174, 2, 126),
            ParentCode = "PC-US",
            InternationalCorrelationCode = "H"
        });

        return units;
    }
}