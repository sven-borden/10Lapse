using System.Runtime.InteropServices;
using TenLapse.Core.Ordering;

namespace TenLapse.Tests.Ordering;

/// <summary>
/// The managed comparer must order names exactly as Explorer does, so every test here asks Windows itself:
/// <c>StrCmpLogicalW</c> is the reference, and the pairs are hand-picked, exhaustive over small alphabets,
/// and generated like real camera folders.
/// </summary>
public static class NaturalNameComparerTests
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string left, string right);

    private static int Windows(string left, string right) => Math.Sign(StrCmpLogicalW(left, right));

    private static int Managed(string left, string right) => Math.Sign(NaturalNameComparer.Instance.Compare(left, right));

    private static void AssertParity(string left, string right)
    {
        Assert.True(
            Windows(left, right) == Managed(left, right),
            $"\"{left}\" vs \"{right}\": StrCmpLogicalW says {Windows(left, right)}, the comparer says {Managed(left, right)}");
    }

    public static TheoryData<string, string> HandPicked => new()
    {
        { "IMG_9.jpg", "IMG_10.jpg" },
        { "IMG_10.jpg", "IMG_9.jpg" },
        { "DSC_0001.jpg", "DSC_1.jpg" },
        { "DSC_1.jpg", "DSC_0001.jpg" },
        { "DSC_0002.jpg", "DSC_1.jpg" },
        { "a 2", "a 10" },
        { "a 10", "a 2" },
        { "a.2", "a.10" },
        { "IMG_0041.jpg", "IMG_0045.jpg" },
        { "IMG_1.jpg", "IMG_1 (2).jpg" },
        { "IMG_1 (2).jpg", "IMG_1 (10).jpg" },
        { "IMG_1.jpg", "IMG_1-2.jpg" },
        { "IMG 1.jpg", "IMG_1.jpg" },
        { "img_1.JPG", "IMG_1.jpg" },
        { "Img_2.jpg", "iMG_10.jpg" },
        { "a.b.c", "a.b" },
        { "photo.1.2.jpg", "photo.1.10.jpg" },
        { "1.jpg", "a.jpg" },
        { "a.jpg", "a1.jpg" },
        { "a1b2c3", "a1b2c10" },
        { "a1b2", "a1b02" },
        { "a02b", "a2c" },
        { "0", "00" },
        { "x07", "x7" },
        { "co-op", "coop" },
        { "co-op", "co'op" },
        { "a--b", "a-b" },
        { "e", "é" },
        { "é", "f" },
        { "Éa", "éa" },
        { "ä", "b" },
        { "Zebra", "apple" },
        { "ß", "ss" },
        { "æ", "ae" },
        { "Tôkyô 2", "Tôkyô 10" },
        { "Åre 3.jpg", "Åre 12.jpg" },
        { "", "a" },
        { "a", "a " },
        { "a1", "a1 " },
        { "x9999999999999999999999", "x10000000000000000000000" },
        { "x9999999999999999999", "x10000000000000000000" },
        { "x00000000000000000000000001", "x2" },
        { "x123456789012345678901234567890", "x23456789012345678901234567890" },
        { "x18446744073709551615", "x18446744073709551616" },
        { "x4294967295", "x4294967296" },
        { "x99999999999999999999999999999999999999a", "x99999999999999999999999999999999999999b" },
        { "٣", "٤" },
        { "١٠", "٩" },
        { "10", "９" },
    };

    [Theory]
    [MemberData(nameof(HandPicked))]
    public static void HandPickedPairsAgreeWithExplorer(string left, string right)
    {
        AssertParity(left, right);
        AssertParity(right, left);
    }

    [Fact]
    public static void NinePrecedesTenAndZerosBreakTies()
    {
        var comparer = NaturalNameComparer.Instance;
        Assert.True(comparer.Compare("IMG_9.jpg", "IMG_10.jpg") < 0);
        Assert.True(comparer.Compare("DSC_0001.jpg", "DSC_1.jpg") < 0);
        Assert.True(comparer.Compare("a 2", "a 10") < 0);
        Assert.Equal(0, comparer.Compare("IMG_0001.JPG", "img_0001.jpg"));
        Assert.True(comparer.Compare("img_2.jpg", "IMG_10.jpg") < 0);
    }

    [Fact]
    public static void NullSortsFirst()
    {
        var comparer = NaturalNameComparer.Instance;
        Assert.True(comparer.Compare(null, "a") < 0);
        Assert.True(comparer.Compare("a", null) > 0);
        Assert.Equal(0, comparer.Compare(null, null));
    }

    [Fact]
    public static void SortingAFolderListingGivesExplorerOrder()
    {
        string[] names =
        [
            "IMG_10.jpg", "IMG_2.jpg", "IMG_1.jpg", "img_9.jpg", "IMG_100.jpg", "IMG_1 (2).jpg", "IMG_0003.jpg", "DSC_5.jpg",
        ];

        var ours = names.Order(NaturalNameComparer.Instance).ToArray();
        var windows = names.Order(Comparer<string>.Create(StrCmpLogicalW)).ToArray();

        Assert.Equal(windows, ours);
        Assert.Equal(["DSC_5.jpg", "IMG_1 (2).jpg", "IMG_1.jpg", "IMG_2.jpg", "IMG_0003.jpg", "img_9.jpg", "IMG_10.jpg", "IMG_100.jpg"], ours);
    }

    [Theory]
    [InlineData("aAb01_ .", 4)]
    [InlineData("a0123_", 5)]
    [InlineData("ab-' 1", 4)]
    [InlineData("eéèëêfE1", 4)]
    [InlineData("aAbB0189_ .-'()#", 4)]
    [InlineData("eéaA01 _-.ßæ", 4)]
    public static void EveryShortStringOverASmallAlphabetAgrees(string alphabet, int maxLength)
    {
        var all = new List<string> { string.Empty };
        var frontier = new List<string> { string.Empty };
        for (var length = 1; length <= maxLength; length++)
        {
            frontier = [.. frontier.SelectMany(prefix => alphabet.Select(c => prefix + c))];
            all.AddRange(frontier);
        }

        var random = new Random(15);
        for (var i = 0; i < 60_000; i++)
        {
            AssertParity(all[random.Next(all.Count)], all[random.Next(all.Count)]);
        }
    }

    [Fact]
    public static void LongDigitRunsAgree()
    {
        var random = new Random(150);
        string Digits(int count) =>
            string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + (random.Next(4) == 0 ? random.Next(10) : random.Next(2) * 9))));

        for (var i = 0; i < 20_000; i++)
        {
            var left = "x" + Digits(random.Next(1, 48)) + (random.Next(3) == 0 ? "a" : string.Empty);
            var right = random.Next(3) == 0
                ? left[..Math.Min(left.Length, random.Next(1, 48))] + Digits(random.Next(0, 5))
                : "x" + Digits(random.Next(1, 48)) + (random.Next(3) == 0 ? "b" : string.Empty);
            AssertParity(left, right);
        }
    }

    [Fact]
    public static void EveryAsciiSymbolAndLatinLetterAgreesInContext()
    {
        // ASCII, Latin-1 and the Latin Extended-A letters that decompose into a base letter and a mark.
        var chars = Enumerable.Range(0x20, 0x7F - 0x20)
            .Concat(Enumerable.Range(0xC0, 0x100 - 0xC0))
            .Concat(Enumerable.Range(0x100, 0x180 - 0x100))
            .Select(code => (char)code)
            .Where(c => !char.IsControl(c) && !"ªµºİıĲĳĸĿŀŉŊŋ".Contains(c))
            .ToArray();

        foreach (var context in new[] { "{0}", "a{0}b", "{0}1", "1{0}", "IMG{0}7" })
        {
            foreach (var left in chars)
            {
                foreach (var right in chars)
                {
                    AssertParity(string.Format(context, left), string.Format(context, right));
                }
            }
        }
    }

    [Fact]
    public static void GeneratedCameraFolderNamesAgree()
    {
        var random = new Random(1500);
        string[] stems = ["IMG_", "DSC_", "DSC", "IMG-", "P", "GOPR", "Photo ", "photo.", "PXL_2026", "20260501_", "Été ", "Tôkyô "];
        string[] suffixes = ["", "", " (2)", " (10)", "-2", "_edit", " copy", "a", ".1"];
        string[] extensions = [".jpg", ".JPG", ".jpeg", ".heic", ".png", ".dng", ".CR2", ".tif"];
        var names = new List<string>();
        while (names.Count < 400)
        {
            var number = random.Next(4) switch
            {
                0 => random.Next(1, 20).ToString(),
                1 => random.Next(1, 12000).ToString("0000"),
                2 => random.Next(1, 1000).ToString("00000"),
                _ => random.NextInt64(1, 100_000_000_000).ToString(),
            };
            names.Add(stems[random.Next(stems.Length)] + number + suffixes[random.Next(suffixes.Length)] + extensions[random.Next(extensions.Length)]);
        }

        foreach (var left in names)
        {
            foreach (var right in names)
            {
                AssertParity(left, right);
            }
        }
    }
}
