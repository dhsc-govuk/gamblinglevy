using System.Security.Cryptography;
using Application.Common.Interfaces;

namespace Infrastructure.Graph;

public class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const int Length = 16;
    private const string LowerChars = "abcdefghjkmnpqrstuvwxyz";
    private const string UpperChars = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string DigitChars = "23456789";
    private const string SymbolChars = "!@#$%^&*-_=+";
    private const string AllChars = LowerChars + UpperChars + DigitChars + SymbolChars;

    // Seeds one character from each class so the result reliably satisfies Entra's default password
    // complexity policy (at least 3 of lower/upper/digit/symbol), then fills and shuffles the rest with a
    // cryptographically secure RNG — matching the pattern RecoveryCodeService already uses for recovery codes.
    public string Generate()
    {
        var chars = new char[Length];
        chars[0] = RandomChar(LowerChars);
        chars[1] = RandomChar(UpperChars);
        chars[2] = RandomChar(DigitChars);
        chars[3] = RandomChar(SymbolChars);

        for (var i = 4; i < Length; i++)
        {
            chars[i] = RandomChar(AllChars);
        }

        Shuffle(chars);

        return new string(chars);
    }

    private static char RandomChar(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(char[] chars)
    {
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}