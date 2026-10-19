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

    // Names as macOS and some cloud tools write them: the accent is a separate combining mark (NFD). Explorer sorts
    // such a name like the same name typed with the precomposed letter (NFC), so the comparer must too.
    private static string[] AccentedNames() =>
    [
        "e.jpg", "f.jpg", "ea.jpg", "eb.jpg",
        $"{(char)0xE9}.jpg", $"{(char)0xE9}a.jpg", $"{(char)0xE9}b.jpg", // e acute
        $"M{(char)0xFC}ller 2.jpg", $"M{(char)0xFC}ller 10.jpg", "Muller 3.jpg", // u diaeresis
        $"{(char)0xC5}ke 1.jpg", "Ake 2.jpg", // A ring
        $"Zo{(char)0xEB}.jpg", $"Z{(char)0xF4}ne 5.png", // e diaeresis, o circumflex
        $"{(char)0x00C9}t{(char)0xE9} 04.jpg", $"{(char)0x00C9}t{(char)0xE9} 4.jpg", // Ete
        $"na{(char)0xEF}ve_{(char)0xF1}.jpg", // i diaeresis, n tilde
        $"{(char)0x0161}tor 1.jpg", $"{(char)0x010D}a 2.jpg", $"{(char)0x0105}b.jpg", // s caron, c caron, a ogonek
    ];

    [Fact]
    public static void DecomposedAccentsOrderLikePrecomposedOnes()
    {
        foreach (var name in AccentedNames())
        {
            var nfc = name.Normalize(System.Text.NormalizationForm.FormC);
            var nfd = name.Normalize(System.Text.NormalizationForm.FormD);
            Assert.Equal(0, Managed(nfc, nfd));
            Assert.Equal(Windows(nfc, nfd), Managed(nfc, nfd));
        }
    }

    [Fact]
    public static void DecomposedAndPrecomposedNamesAgreeWithExplorerInEveryPairing()
    {
        var forms = new List<string>();
        foreach (var name in AccentedNames())
        {
            forms.Add(name.Normalize(System.Text.NormalizationForm.FormC));
            forms.Add(name.Normalize(System.Text.NormalizationForm.FormD));
        }

        foreach (var left in forms)
        {
            foreach (var right in forms)
            {
                AssertParity(left, right);
            }
        }
    }

    [Fact]
    public static void AMixedFolderOfNfcAndNfdNamesSortsAsExplorerDoes()
    {
        var names = AccentedNames()
            .Select((name, i) => i % 2 == 0 ? name.Normalize(System.Text.NormalizationForm.FormD) : name.Normalize(System.Text.NormalizationForm.FormC))
            .ToArray();

        Assert.Equal(
            names.Order(Comparer<string>.Create(StrCmpLogicalW)).ToArray(),
            names.Order(NaturalNameComparer.Instance).ToArray());
    }

    // Documented divergences from Explorer. The comparer copies Windows' order for ASCII, Latin-1 and Latin Extended-A;
    // everything else is approximate. These tests assert what the comparer does today, not what Windows does, so a
    // change to any of them is a decision and not an accident. Each comment says what StrCmpLogicalW answered on
    // Windows 11 (26100) when the test was written.

    [Fact]
    public static void KanaAreOrderedByCodePointWhileWindowsPutsKatakanaFirst()
    {
        var hiraganaA = $"{(char)0x3042}.jpg";
        var katakanaA = $"{(char)0x30A2}.jpg";

        // Windows: katakana ア before hiragana あ. Here: code point, so hiragana first.
        Assert.Equal(-1, Managed(hiraganaA, katakanaA));
        Assert.Equal(1, Managed(katakanaA, hiraganaA));
    }

    [Fact]
    public static void AnEmojiIsASymbolThatSortsBeforeDigitsAndLettersWhileWindowsSortsItAfter()
    {
        var grinning = char.ConvertFromUtf32(0x1F600);

        // Windows: "a" + emoji after "a1" and after "az". Here: a symbol, so before a digit and before a letter.
        Assert.Equal(-1, Managed($"a{grinning}.jpg", "a1.jpg"));
        Assert.Equal(-1, Managed($"a{grinning}.jpg", "az.jpg"));

        // Two emoji are in code point order, as in Windows.
        Assert.Equal(-1, Managed($"a{grinning}.jpg", $"a{char.ConvertFromUtf32(0x1F601)}.jpg"));
    }

    [Fact]
    public static void DigitsOfOtherScriptsCountAsNumbersButTieWithTheAsciiDigitOfTheSameValue()
    {
        var arabicIndicThree = (char)0x0663;
        var arabicIndicTen = $"{(char)0x0661}{(char)0x0660}";
        var fullwidthThree = (char)0xFF13;
        var fullwidthTen = $"{(char)0xFF11}{(char)0xFF10}";

        // They are read as numbers, in order with the ASCII ones, as in Windows.
        AssertParity($"a{arabicIndicTen}.jpg", "a9.jpg");
        AssertParity($"a{arabicIndicTen}.jpg", "a11.jpg");
        AssertParity($"a{fullwidthTen}.jpg", "a9.jpg");
        AssertParity($"a{fullwidthTen}.jpg", "a11.jpg");
        AssertParity($"a{arabicIndicThree}.jpg", "a2.jpg");
        AssertParity($"a{arabicIndicThree}.jpg", "a4.jpg");

        // The same value in two scripts is a tie here (compare returns 0 for two different strings). Windows says the
        // non-ASCII digit comes after the ASCII one.
        Assert.Equal(0, Managed($"a{arabicIndicThree}.jpg", "a3.jpg"));
        Assert.Equal(0, Managed($"a{fullwidthThree}.jpg", "a3.jpg"));
    }

    [Fact]
    public static void OtherScriptsAreInCodePointOrderAfterLatin()
    {
        // CJK and Cyrillic order by code point and follow the Latin letters; Windows agrees on these pairs.
        AssertParity($"{(char)0x4E2D}.jpg", $"{(char)0x6587}.jpg");
        AssertParity($"{(char)0x0416}.jpg", $"{(char)0x0410}.jpg");
        AssertParity($"{(char)0x0416}.jpg", "z.jpg");
    }

    [Fact]
    public static void SortingRandomNamesOverAMixedAlphabetNeverThrowsAndIsConsistent()
    {
        // ASCII letters of both cases, digits, fullwidth digits, Latin-1 accents precomposed and decomposed, a space and a dot.
        string[] alphabet =
        [
            "a", "B", "z", "Q", "0", "1", "9", "7", "_", "-", " ", ".", "(", ")",
            $"{(char)0xFF11}", $"{(char)0xFF13}", $"{(char)0xFF10}",
            $"{(char)0xE9}", $"e{(char)0x0301}", $"{(char)0xC9}", $"{(char)0xFC}", $"u{(char)0x0308}", $"{(char)0xE0}", $"a{(char)0x0300}", $"{(char)0xDF}", $"{(char)0xF1}", $"n{(char)0x0303}",
        ];
        var random = new Random(2026);
        var names = new string[5000];
        for (var i = 0; i < names.Length; i++)
        {
            var length = random.Next(1, 9);
            names[i] = string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]));
        }

        var comparer = NaturalNameComparer.Instance;
        var sorted = (string[])names.Clone();
        Array.Sort(sorted, comparer);

        // In order.
        for (var i = 1; i < sorted.Length; i++)
        {
            Assert.True(comparer.Compare(sorted[i - 1], sorted[i]) <= 0, $"\"{sorted[i - 1]}\" before \"{sorted[i]}\"");
        }

        // Sorting the result again gives the same order (names that tie may swap places, so compare by the comparer, not by text),
        // and so does sorting a shuffled copy.
        var again = (string[])sorted.Clone();
        Array.Sort(again, comparer);
        var shuffled = names.OrderBy(_ => random.Next()).ToArray();
        Array.Sort(shuffled, comparer);
        for (var i = 0; i < sorted.Length; i++)
        {
            Assert.Equal(0, comparer.Compare(sorted[i], again[i]));
            Assert.Equal(0, comparer.Compare(sorted[i], shuffled[i]));
        }

        // Antisymmetry on a sample of pairs, and transitivity on a sample of triples.
        for (var n = 0; n < 20_000; n++)
        {
            var a = names[random.Next(names.Length)];
            var b = names[random.Next(names.Length)];
            var c = names[random.Next(names.Length)];
            Assert.Equal(-Math.Sign(comparer.Compare(b, a)), Math.Sign(comparer.Compare(a, b)));
            if (comparer.Compare(a, b) <= 0 && comparer.Compare(b, c) <= 0)
            {
                Assert.True(comparer.Compare(a, c) <= 0, $"\"{a}\" <= \"{b}\" <= \"{c}\" but \"{a}\" > \"{c}\"");
            }
        }
    }
}
