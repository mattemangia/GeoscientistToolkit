using GAIA.Util;

namespace VerificationTests;

public sealed class NaturalFileSortTests
{
    [Fact]
    public void OrdersByNumericValueNotLexically()
    {
        var input = new List<string> { "/d/img10.png", "/d/img2.png", "/d/img1.png", "/d/img100.png" };
        var sorted = NaturalFileSort.Sort(input);
        Assert.Equal(new[] { "/d/img1.png", "/d/img2.png", "/d/img10.png", "/d/img100.png" }, sorted);
    }

    [Fact]
    public void LongEmbeddedNumbers_StillOrderByTrailingIndex()
    {
        // The reported case: exported slices carry the full dataset name, whose concatenated digits
        // overflow Int32. The old sorter collapsed every key to 0; natural ordering must still sort
        // strictly by the trailing slice index regardless of input order.
        var expected = Enumerable.Range(1, 130)
            .Select(i => $"/scan/SlicesY-ID01-240125-M04_{i:D4}.png")
            .ToList();

        var shuffled = new List<string>(expected);
        var rng = new Random(1234);
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        Assert.Equal(expected, NaturalFileSort.Sort(shuffled));

        // Confirms the old digit-concatenation would have failed: all keys overflow to the same
        // fallback, so ordering could not depend on it.
        foreach (var f in expected.Take(3))
        {
            var digits = new string(Path.GetFileNameWithoutExtension(f).Where(char.IsDigit).ToArray());
            Assert.False(int.TryParse(digits, out _));
        }
    }

    [Fact]
    public void IsDeterministicRegardlessOfInputOrder()
    {
        var a = new List<string> { "/d/a_0002.tif", "/d/a_0001.tif", "/d/a_0010.tif" };
        var b = new List<string> { "/d/a_0010.tif", "/d/a_0001.tif", "/d/a_0002.tif" };
        Assert.Equal(NaturalFileSort.Sort(a), NaturalFileSort.Sort(b));
        Assert.Equal(new[] { "/d/a_0001.tif", "/d/a_0002.tif", "/d/a_0010.tif" }, NaturalFileSort.Sort(a));
    }
}
