// GAIA/Util/NaturalFileSort.cs

namespace GAIA.Util;

/// <summary>
///     Natural ("human") ordering for image-stack file names, e.g. <c>scan_2</c> before
///     <c>scan_10</c>. Digit runs are compared by numeric value without ever parsing them into a
///     fixed-width integer, so long embedded numbers (dates, IDs, e.g.
///     <c>SlicesY-ID01-240125-M04_0007</c>) can never overflow and collapse every key to zero.
///     A leftover ordinal comparison of the whole name is used as the final tie-break, so the
///     result is a deterministic total order and never falls back to the volatile
///     <see cref="System.IO.Directory.GetFiles(string)" /> enumeration order — the bug that
///     scrambled slices differently on every reimport.
/// </summary>
public static class NaturalFileSort
{
    public static readonly IComparer<string> Comparer = new NaturalComparer();

    /// <summary>Orders the paths by their file name using <see cref="Comparer" />.</summary>
    public static List<string> Sort(IEnumerable<string> paths) =>
        paths.OrderBy(Path.GetFileName, Comparer).ToList();

    private sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    var startA = ia;
                    var startB = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;

                    // Compare the digit runs by value, overflow-free: strip leading zeros, then a
                    // longer run is the larger number; equal length compares lexically.
                    var na = a.AsSpan(startA, ia - startA).TrimStart('0');
                    var nb = b.AsSpan(startB, ib - startB).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                    var digitCmp = na.SequenceCompareTo(nb);
                    if (digitCmp != 0) return digitCmp;

                    // Same value: fewer leading zeros first, for a stable, predictable order.
                    var lenA = ia - startA;
                    var lenB = ib - startB;
                    if (lenA != lenB) return lenA < lenB ? -1 : 1;
                }
                else
                {
                    var charCmp = a[ia].CompareTo(b[ib]);
                    if (charCmp != 0) return charCmp;
                    ia++;
                    ib++;
                }
            }

            var remaining = (a.Length - ia) - (b.Length - ib);
            if (remaining != 0) return remaining < 0 ? -1 : 1;

            // Fully equal under natural rules: fall back to a deterministic ordinal tie-break so
            // the order never depends on the file-system enumeration order.
            return string.CompareOrdinal(a, b);
        }
    }
}
