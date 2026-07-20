using GAIA.Analysis.Pnm;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;
using Xunit;

namespace VerificationTests;

/// <summary>
///     Diagnostic for the "all exported pores share the same radius" report: builds a volume with
///     spherical pores of clearly different sizes and checks that the generated network (and the
///     pores-table it exports) carries distinct radii per pore.
/// </summary>
public sealed class PoreRadiusDiagnosticTests
{
    private const int PoreMaterialId = 1;

    [Fact]
    public void Generate_SpheresOfDifferentSizes_ExportDistinctRadii()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-radius-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const int n = 64;
            using var labels = new ChunkedLabelVolume(n, n, n, 16, false);

            // Three isolated spheres with radii 4, 7 and 11 voxels
            var spheres = new (int cx, int cy, int cz, int r)[]
            {
                (14, 14, 14, 4),
                (44, 16, 16, 7),
                (32, 44, 44, 11)
            };

            foreach (var (cx, cy, cz, r) in spheres)
                for (var z = cz - r; z <= cz + r; z++)
                for (var y = cy - r; y <= cy + r; y++)
                for (var x = cx - r; x <= cx + r; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var dz = z - cz;
                    if (dx * dx + dy * dy + dz * dz <= r * r)
                        labels[x, y, z] = PoreMaterialId;
                }

            var ct = new CtImageStackDataset($"pnm-radius-{Guid.NewGuid():N}", directory)
            {
                Width = n, Height = n, Depth = n,
                PixelSize = 2f, SliceThickness = 2f,
                LabelData = labels
            };

            var options = new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                Neighborhood = Neighborhood3D.N26,
                Mode = GenerationMode.Conservative
            };

            var pnm = PNMGenerator.Generate(ct, options, null, CancellationToken.None);

            var radii = pnm.Pores.Select(p => p.Radius).OrderBy(r => r).ToArray();
            Assert.True(pnm.Pores.Count >= 3,
                $"Expected at least 3 pores, got {pnm.Pores.Count} (radii: {string.Join(", ", radii)})");

            var distinct = radii.Distinct().Count();
            Assert.True(distinct >= 3,
                $"Expected >=3 distinct radii for spheres of r=4,7,11 but got {distinct} distinct " +
                $"value(s): {string.Join(", ", radii)}");

            // The radii must track the actual sphere sizes (max inscribed sphere ≈ nominal radius)
            Assert.InRange(radii.Max(), 9f, 13f);
            Assert.InRange(radii.Min(), 2.5f, 6f);

            // And the exported table must carry them through unchanged
            var table = pnm.BuildPoresTableDataset("radii");
            var exported = table.GetDataTable().Rows.Cast<System.Data.DataRow>()
                .Select(r => (float)r["Radius_vox"]).OrderBy(r => r).ToArray();
            Assert.Equal(radii, exported);

            var exportedUm = table.GetDataTable().Rows.Cast<System.Data.DataRow>()
                .Select(r => (double)r["Radius_um"]).Distinct().Count();
            Assert.True(exportedUm >= 3,
                $"Radius_um column collapsed to {exportedUm} distinct value(s)");

            // The equivalent-volume radius must be continuous (distinct per differently-sized pore)
            // and consistent with the sphere volumes.
            var eqRadii = table.GetDataTable().Rows.Cast<System.Data.DataRow>()
                .Select(r => (double)r["EqRadius_vox"]).OrderBy(r => r).ToArray();
            Assert.True(eqRadii.Distinct().Count() >= 3,
                $"EqRadius_vox collapsed to {eqRadii.Distinct().Count()} distinct value(s): {string.Join(", ", eqRadii)}");
            Assert.InRange(eqRadii.Max(), 9.0, 13.0);
            Assert.InRange(eqRadii.Min(), 2.5, 6.0);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
