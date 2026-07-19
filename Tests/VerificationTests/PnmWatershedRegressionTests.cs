using GAIA.Analysis.Pnm;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;

namespace VerificationTests;

public sealed class PnmWatershedRegressionTests
{
    private const int Material = 1;

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-pnm-watershed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static CtImageStackDataset Stack(ChunkedLabelVolume labels, int w, int h, int d, string path) =>
        new($"watershed-{Guid.NewGuid():N}", path)
        {
            Width = w, Height = h, Depth = d, PixelSize = 1, SliceThickness = 1, LabelData = labels
        };

    [Fact]
    public void ConnectedChambers_UseDistanceMaximaInsteadOfOneCentralComponent()
    {
        var directory = TempDirectory();
        try
        {
            using var labels = new ChunkedLabelVolume(64, 32, 32, 16, false);
            void Sphere(int cx, int radius)
            {
                for (var z = 0; z < 32; z++)
                for (var y = 0; y < 32; y++)
                for (var x = Math.Max(0, cx - radius); x <= Math.Min(63, cx + radius); x++)
                    if ((x - cx) * (x - cx) + (y - 16) * (y - 16) + (z - 16) * (z - 16) <= radius * radius)
                        labels[x, y, z] = Material;
            }
            Sphere(12, 8);
            Sphere(32, 8);
            Sphere(52, 8);
            for (var x = 12; x <= 52; x++)
            for (var y = 14; y <= 18; y++)
            for (var z = 14; z <= 18; z++) labels[x, y, z] = Material;

            var pnm = PNMGenerator.Generate(Stack(labels, 64, 32, 32, directory), new PNMGeneratorOptions
            {
                MaterialId = Material,
                Neighborhood = Neighborhood3D.N26,
                Mode = GenerationMode.Conservative
            }, null, CancellationToken.None);

            Assert.True(pnm.Pores.Count >= 3, $"Expected at least three chambers, got {pnm.Pores.Count}");
            Assert.True(pnm.Pores.Max(p => p.VolumeVoxels) < pnm.Pores.Sum(p => p.VolumeVoxels) * 0.6f,
                "A single central region still owns most of the pore space.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RequiredPercolation_DoesNotInventThroatsAcrossDisconnectedBodies()
    {
        var directory = TempDirectory();
        try
        {
            using var labels = new ChunkedLabelVolume(32, 32, 32, 16, false);
            for (var z = 2; z < 8; z++)
            for (var y = 10; y < 20; y++)
            for (var x = 10; x < 20; x++) labels[x, y, z] = Material;
            for (var z = 24; z < 30; z++)
            for (var y = 10; y < 20; y++)
            for (var x = 10; x < 20; x++) labels[x, y, z] = Material;

            var error = Assert.Throws<InvalidOperationException>(() => PNMGenerator.Generate(
                Stack(labels, 32, 32, 32, directory), new PNMGeneratorOptions
                {
                    MaterialId = Material,
                    RequireInletOutletPercolation = true,
                    Axis = FlowAxis.Z
                }, null, CancellationToken.None));
            Assert.Contains("No artificial throats", error.Message);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
