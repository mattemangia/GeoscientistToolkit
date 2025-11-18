// GeoscientistToolkit/Data/AcousticVolume/RockMaterialLibrary.cs

namespace GeoscientistToolkit.Data.AcousticVolume;

public class RockMaterial
{
    public string Name { get; set; }
    public string Category { get; set; }
    public float Density { get; set; } // kg/m3
    public float Vp { get; set; } // P-wave velocity m/s
    public float Vs { get; set; } // S-wave velocity m/s
    public float PoissonRatio { get; set; }
    public float YoungsModulus { get; set; } // GPa
    public float BulkModulus { get; set; } // GPa
    public float ShearModulus { get; set; } // GPa
    public byte[] GrayscaleRange { get; set; } // [min, max]
}

public static class RockMaterialLibrary
{
    public static readonly List<RockMaterial> Materials = new()
    {
        // Air/Voids
        new RockMaterial
        {
            Name = "Air", Category = "Void", Density = 1.225f, Vp = 343, Vs = 0,
            PoissonRatio = 0, YoungsModulus = 0, BulkModulus = 0, ShearModulus = 0,
            GrayscaleRange = new byte[] { 0, 10 }
        },

        // Sedimentary Rocks
        new RockMaterial
        {
            Name = "Clay", Category = "Sedimentary", Density = 1600, Vp = 1800, Vs = 600,
            PoissonRatio = 0.35f, YoungsModulus = 2.5f, BulkModulus = 2.2f, ShearModulus = 0.9f,
            GrayscaleRange = new byte[] { 11, 30 }
        },
        new RockMaterial
        {
            Name = "Sandstone", Category = "Sedimentary", Density = 2200, Vp = 2400, Vs = 1400,
            PoissonRatio = 0.25f, YoungsModulus = 15, BulkModulus = 12.5f, ShearModulus = 6.0f,
            GrayscaleRange = new byte[] { 31, 60 }
        },
        new RockMaterial
        {
            Name = "Limestone (Soft)", Category = "Sedimentary", Density = 2500, Vp = 3000, Vs = 1700,
            PoissonRatio = 0.27f, YoungsModulus = 25, BulkModulus = 20, ShearModulus = 9.8f,
            GrayscaleRange = new byte[] { 61, 90 }
        },
        new RockMaterial
        {
            Name = "Limestone (Hard)", Category = "Sedimentary", Density = 2700, Vp = 3500, Vs = 2000,
            PoissonRatio = 0.26f, YoungsModulus = 35, BulkModulus = 28, ShearModulus = 14,
            GrayscaleRange = new byte[] { 91, 120 }
        },
        new RockMaterial
        {
            Name = "Dolomite", Category = "Sedimentary", Density = 2800, Vp = 3900, Vs = 2200,
            PoissonRatio = 0.28f, YoungsModulus = 45, BulkModulus = 38, ShearModulus = 17.5f,
            GrayscaleRange = new byte[] { 121, 150 }
        },
        new RockMaterial
        {
            Name = "Chalk", Category = "Sedimentary", Density = 2400, Vp = 2200, Vs = 1200,
            PoissonRatio = 0.29f, YoungsModulus = 8, BulkModulus = 6.5f, ShearModulus = 3.1f,
            GrayscaleRange = new byte[] { 151, 180 }
        },
        new RockMaterial
        {
            Name = "Shale", Category = "Sedimentary", Density = 2600, Vp = 2400, Vs = 1300,
            PoissonRatio = 0.30f, YoungsModulus = 12, BulkModulus = 10, ShearModulus = 4.6f,
            GrayscaleRange = new byte[] { 181, 210 }
        },

        // Igneous Rocks
        new RockMaterial
        {
            Name = "Granite", Category = "Igneous", Density = 2700, Vp = 5000, Vs = 2900,
            PoissonRatio = 0.25f, YoungsModulus = 60, BulkModulus = 50, ShearModulus = 24,
            GrayscaleRange = new byte[] { 211, 240 }
        },
        new RockMaterial
        {
            Name = "Basalt", Category = "Igneous", Density = 2900, Vp = 5500, Vs = 3200,
            PoissonRatio = 0.25f, YoungsModulus = 75, BulkModulus = 62.5f, ShearModulus = 30,
            GrayscaleRange = new byte[] { 241, 255 }
        },

        // Metamorphic Rocks
        new RockMaterial
        {
            Name = "Marble", Category = "Metamorphic", Density = 2700, Vp = 3800, Vs = 2200,
            PoissonRatio = 0.25f, YoungsModulus = 50, BulkModulus = 41.7f, ShearModulus = 20,
            GrayscaleRange = new byte[] { 200, 230 }
        },
        new RockMaterial
        {
            Name = "Quartzite", Category = "Metamorphic", Density = 2650, Vp = 5200, Vs = 3000,
            PoissonRatio = 0.25f, YoungsModulus = 70, BulkModulus = 58.3f, ShearModulus = 28,
            GrayscaleRange = new byte[] { 220, 250 }
        },
        new RockMaterial
        {
            Name = "Gneiss", Category = "Metamorphic", Density = 2750, Vp = 4500, Vs = 2600,
            PoissonRatio = 0.26f, YoungsModulus = 55, BulkModulus = 45.8f, ShearModulus = 22,
            GrayscaleRange = new byte[] { 180, 220 }
        },

        // Special Cases
        new RockMaterial
        {
            Name = "Coal", Category = "Organic", Density = 1400, Vp = 2200, Vs = 1000,
            PoissonRatio = 0.35f, YoungsModulus = 5, BulkModulus = 4.2f, ShearModulus = 1.9f,
            GrayscaleRange = new byte[] { 40, 70 }
        },
        new RockMaterial
        {
            Name = "Salt", Category = "Evaporite", Density = 2200, Vp = 4500, Vs = 2600,
            PoissonRatio = 0.25f, YoungsModulus = 40, BulkModulus = 33.3f, ShearModulus = 16,
            GrayscaleRange = new byte[] { 100, 140 }
        }
    };

    public static RockMaterial GetMaterialByGrayscale(byte grayValue)
    {
        return Materials.FirstOrDefault(m => grayValue >= m.GrayscaleRange[0] && grayValue <= m.GrayscaleRange[1])
               ?? Materials[1]; // Default to Clay if not found
    }

    public static RockMaterial GetMaterial(string name)
    {
        return Materials.FirstOrDefault(m => m.Name == name);
    }

    public static List<RockMaterial> GetByCategory(string category)
    {
        return Materials.Where(m => m.Category == category).ToList();
    }
}