using System.Globalization;
using System.Text;

namespace TenLapse.Core.Ordering;

/// <summary>
/// Compares file names the way Explorer lists them (<c>StrCmpLogicalW</c>), in managed code so Core stays
/// portable: <c>IMG_9</c> before <c>IMG_10</c>, case ignored, digit runs compared as numbers.
/// </summary>
/// <remarks>
/// <para>
/// A name is read as a list of tokens, and two names are compared token by token:
/// </para>
/// <list type="bullet">
/// <item>A symbol (space, <c>_</c>, <c>.</c>, <c>(</c> and the rest) sorts before a digit run, and a digit run before a
/// letter. Among symbols the order is Windows' own (space, <c>!</c>, <c>"</c>, <c>#</c> ... <c>_</c> ... <c>=</c>, <c>&gt;</c>).</item>
/// <item>A digit run is a number: <c>9</c> before <c>10</c>. Windows reads at most 19 digits at a time, so a longer
/// run is compared 19 digits, then the next 19, and so on. Only when two names are otherwise equal do leading
/// zeros decide, and then the one with fewer zeros comes last (<c>DSC_0001</c> before <c>DSC_1</c>).</item>
/// <item>A letter is compared by its base letter, ignoring case; accents only break a tie (<c>e</c>, <c>é</c>,
/// <c>f</c>). <c>ß</c>, <c>æ</c> and <c>œ</c> count as <c>ss</c>, <c>ae</c> and <c>oe</c>. Letters outside Latin follow
/// the Latin ones by code point.</item>
/// <item>A hyphen and an apostrophe are ignored while comparing and only break a tie (<c>coop</c> before <c>co-op</c>),
/// as in Windows' word sort.</item>
/// </list>
/// <para>
/// Both names are first converted to Unicode Form C, so a decomposed accent (macOS writes é as e + U+0301) sorts like the
/// precomposed letter, as in Explorer.
/// </para>
/// <para>
/// <b>How exact it is.</b> The order is exact for ASCII, Latin-1 and Latin Extended-A (the tests compare it with
/// <c>StrCmpLogicalW</c>: every short string over six small alphabets, every such character in five contexts, long digit
/// runs, NFC against NFD, and generated camera names). Fullwidth digits are read as numbers, in order with ASCII ones.
/// Everything else is approximate, because Windows' collation tables are not copied: Latin Extended-B and a few Latin
/// Extended-A letters (Ĳ, Ŀ, ŉ, ı, ĸ, Ŋ), ª, µ and º; non-Latin scripts and symbols order by code point (hiragana before
/// katakana, where Explorer puts katakana first; an emoji before digits and letters, where Explorer puts it after);
/// and digits of different scripts with the same value (٣ and 3, ３ and 3) tie here. Tests pin those divergences. None
/// of it touches ordinary camera, phone and screenshot file names.
/// </para>
/// </remarks>
public sealed class NaturalNameComparer : IComparer<string?>
{
    /// <summary>The shared instance.</summary>
    public static NaturalNameComparer Instance { get; } = new();

    // Windows orders the ASCII symbols like this (hyphen and apostrophe are ignored, see CompareSpecials).
    private const string SymbolOrder = " !\"#$%&()*,./:;?@[\\]^_`{|}~+<=>";

    private const int MaxDigitsPerChunk = 19;

    private enum Kind
    {
        End,
        Symbol,
        Number,
        Letter,
    }

    // Combining marks in the order Windows sorts accents (an accent only breaks a tie, left to right).
    private static readonly char[] _markOrder = [(char)0x0301, (char)0x0300, (char)0x0307, (char)0x0302, (char)0x0308, (char)0x030C, (char)0x0306, (char)0x0304, (char)0x0303, (char)0x030A, (char)0x0328, (char)0x0327, (char)0x030B];

    private NaturalNameComparer()
    {
    }

    /// <summary>Compares two names; null sorts first.</summary>
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // Explorer sorts a name with a combining mark (macOS writes é as e + U+0301) like the precomposed letter.
        // Already-composed text, ASCII included, is returned as it is, so this costs next to nothing.
        var left = new Cursor(Composed(x));
        var right = new Cursor(Composed(y));
        var accentTie = 0;
        while (true)
        {
            var a = left.Next();
            var b = right.Next();
            if (a.Kind != b.Kind)
            {
                return a.Kind < b.Kind ? -1 : 1;
            }

            if (a.Kind == Kind.End)
            {
                break;
            }

            if (a.Primary != b.Primary)
            {
                return a.Primary < b.Primary ? -1 : 1;
            }

            if (a.Kind == Kind.Letter && accentTie == 0 && a.Secondary != b.Secondary)
            {
                accentTie = a.Secondary < b.Secondary ? -1 : 1;
            }
            else if (a.Kind == Kind.Number && a.Length != b.Length)
            {
                // Same value, different leading zeros: decided on the spot, and fewer zeros comes last.
                return a.Length < b.Length ? 1 : -1;
            }
        }

        if (accentTie != 0)
        {
            return accentTie;
        }

        return CompareSpecials(left.Specials, right.Specials);
    }

    private static string Composed(string text) =>
        text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);

    // The entries are (position among the real tokens, weight). The earlier a hyphen, the later the name; at the
    // same place an apostrophe sorts before a hyphen; the name with more of them is last.
    private static int CompareSpecials(List<(int Position, int Weight)>? x, List<(int Position, int Weight)>? y)
    {
        var xs = x?.Count ?? 0;
        var ys = y?.Count ?? 0;
        for (var i = 0; i < Math.Min(xs, ys); i++)
        {
            var (xp, xw) = x![i];
            var (yp, yw) = y![i];
            if (xp != yp)
            {
                return xp < yp ? 1 : -1;
            }

            if (xw != yw)
            {
                return xw < yw ? -1 : 1;
            }
        }

        return xs.CompareTo(ys);
    }


    private struct Token
    {
        public Kind Kind;
        public ulong Primary;
        public int Secondary;
        public int Length;
    }

    // Letters are spaced four apart so letters that sort between two others (long s, t with stroke) have a place.
    private static ulong LetterWeight(char baseLetter) => (ulong)((baseLetter - 'a' + 1) * 4);

    private static Token Letter(char baseLetter, int secondary = 0) =>
        new() { Kind = Kind.Letter, Primary = LetterWeight(baseLetter), Secondary = secondary };

    private struct Cursor(string text)
    {
        private int _position;
        private int _count;
        private char _pending;

        public List<(int Position, int Weight)>? Specials { get; private set; }

        public Token Next()
        {
            if (_pending != 0)
            {
                var second = _pending;
                _pending = '\0';
                _count++;
                return Letter(second);
            }

            while (_position < text.Length)
            {
                var c = text[_position];
                if (c is '-' or '\'')
                {
                    (Specials ??= []).Add((_count, c == '-' ? 2 : 1));
                    _position++;
                    continue;
                }

                if (IsIgnorable(c))
                {
                    _position++;
                    continue;
                }

                _count++;
                if (char.IsDigit(c))
                {
                    return ReadNumber();
                }

                _position++;
                if (c < '\u0080')
                {
                    if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
                    {
                        return Letter((char)(c | 0x20));
                    }

                    var rank = SymbolOrder.IndexOf(c);
                    return new Token { Kind = Kind.Symbol, Primary = (ulong)(rank >= 0 ? rank : 100 + c) };
                }

                return ReadNonAscii(c);
            }

            return default;
        }

        // Soft hyphen, zero-width characters, word joiner and byte order mark weigh nothing.
        private static bool IsIgnorable(char c) =>
            c is (char)0x00AD or (>= (char)0x200B and <= (char)0x200D) or (char)0x2060 or (char)0xFEFF;

        private Token ReadNumber()
        {
            var start = _position;
            var end = Math.Min(start + MaxDigitsPerChunk, text.Length);
            ulong value = 0;
            var i = start;
            while (i < end && char.IsDigit(text[i]))
            {
                value = (value * 10) + (ulong)DigitValue(text[i]);
                i++;
            }

            _position = i;
            return new Token { Kind = Kind.Number, Primary = value, Length = i - start };
        }

        private Token ReadNonAscii(char c)
        {
            var lower = char.ToLowerInvariant(c);
            var stroke = _markOrder.Length + 1;
            switch (lower)
            {
                case (char)0x00DF: // sharp s
                    _pending = 's';
                    return Letter('s');
                case (char)0x00E6: // ae
                    _pending = 'e';
                    return Letter('a');
                case (char)0x0153: // oe
                    _pending = 'e';
                    return Letter('o');
                case (char)0x00F8: // o with stroke
                    return Letter('o', stroke);
                case (char)0x0111: // d with stroke
                    return Letter('d', stroke);
                case (char)0x00F0: // eth
                    return Letter('d', stroke + 1);
                case (char)0x0142: // l with stroke
                    return Letter('l', stroke);
                case (char)0x017F: // long s sorts right after s
                    return new Token { Kind = Kind.Letter, Primary = LetterWeight('s') + 1 };
                case (char)0x0127: // h with stroke
                    return Letter('h', stroke);
                case (char)0x0167: // t with stroke sorts after every t
                    return new Token { Kind = Kind.Letter, Primary = LetterWeight('t') + 3 };
                case (char)0x00FE: // thorn is th
                    _pending = 'h';
                    return Letter('t');
            }

            if (!char.IsLetter(c))
            {
                return new Token { Kind = Kind.Symbol, Primary = (ulong)(1000 + c) };
            }

            // A precomposed Latin letter is its base letter plus a mark; anything else is ordered by code point.
            var decomposed = lower.ToString().Normalize(NormalizationForm.FormD);
            if (decomposed.Length > 1 && decomposed[0] is >= 'a' and <= 'z')
            {
                var mark = Array.IndexOf(_markOrder, decomposed[1]);
                return Letter(decomposed[0], 1 + (mark >= 0 ? mark : _markOrder.Length));
            }

            return new Token { Kind = Kind.Letter, Primary = (ulong)(1000 + lower) };
        }

        private static int DigitValue(char c) =>
            c is >= '0' and <= '9' ? c - '0' : (int)char.GetNumericValue(c);
    }
}
