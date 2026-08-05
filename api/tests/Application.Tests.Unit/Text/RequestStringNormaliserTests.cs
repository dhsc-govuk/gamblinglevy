using Application.Common.Text;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Text;

/// <summary>
/// BLUM-7240 / CWE-20 — the character policy for every string arriving in a request body.
///
/// Code points are written numerically rather than as escape sequences on purpose: a test file asserting
/// that control characters are refused should not itself contain any.
/// </summary>
public class RequestStringNormaliserTests
{
    [Theory]
    [InlineData(0x0000, "null byte truncates a value in anything downstream that speaks C")]
    [InlineData(0x0001, "C0 control")]
    [InlineData(0x0008, "backspace")]
    [InlineData(0x001B, "escape, the start of a terminal control sequence")]
    [InlineData(0x007F, "delete")]
    [InlineData(0x0085, "C1 next-line")]
    [InlineData(0x009F, "C1 application-program-command terminator")]
    [InlineData(0x202A, "left-to-right embedding")]
    [InlineData(0x202E, "right-to-left override, the Trojan Source character")]
    [InlineData(0x2066, "left-to-right isolate")]
    [InlineData(0x2069, "pop directional isolate")]
    public void ShouldRejectACharacterNoLegitimateCallerSends(int codePoint, string why)
    {
        why.Should().NotBeEmpty();

        var candidate = "before" + (char)codePoint + "after";

        RequestStringNormaliser.TryNormalise(candidate, out var normalised).Should().BeFalse();
        normalised.Should().BeNull();
    }

    [Theory]
    [InlineData(0x0009, "tab")]
    [InlineData(0x000A, "line feed")]
    public void ShouldKeepTheWhitespaceThatProseIsMadeOf(int codePoint, string why)
    {
        why.Should().NotBeEmpty();

        // The free-text answer fields hold 10,000 characters of prose; tabs and newlines are part of it.
        var candidate = "before" + (char)codePoint + "after";

        RequestStringNormaliser.TryNormalise(candidate, out var normalised).Should().BeTrue();
        normalised.Should().Be(candidate);
    }

    [Fact]
    public void ShouldFoldEveryLineEndingToASingleLineFeed()
    {
        var candidate = "one" + CR + LF + "two" + CR + "three" + LF + "four";

        RequestStringNormaliser.TryNormalise(candidate, out var normalised).Should().BeTrue();

        // One line ending in storage, in exports and in logs — and so no way to forge a record boundary in
        // any of the three.
        normalised.Should().Be("one" + LF + "two" + LF + "three" + LF + "four");
    }

    [Theory]
    [InlineData(0x000B, "vertical tab, what Word puts where somebody pressed Shift+Enter")]
    [InlineData(0x000C, "form feed")]
    public void ShouldTreatWordsLineBreaksAsLineBreaks(int codePoint, string why)
    {
        why.Should().NotBeEmpty();

        RequestStringNormaliser.TryNormalise("one" + (char)codePoint + "two", out var normalised).Should().BeTrue();
        normalised.Should().Be("one" + LF + "two");
    }

    [Theory]
    [InlineData(0x200B, "zero-width space")]
    [InlineData(0x200E, "left-to-right mark")]
    [InlineData(0x200F, "right-to-left mark")]
    [InlineData(0x2060, "word joiner")]
    [InlineData(0xFEFF, "byte-order mark left behind by a paste")]
    public void ShouldStripInvisibleCharactersRatherThanRefuseThem(int codePoint, string why)
    {
        why.Should().NotBeEmpty();

        // Repaired quietly: failing a long answer over a character its author cannot see is not actionable.
        RequestStringNormaliser.TryNormalise("Zero" + (char)codePoint + "Width", out var normalised).Should().BeTrue();
        normalised.Should().Be("ZeroWidth");
    }

    [Theory]
    [InlineData(0x200C, "zero-width non-joiner, required by Persian orthography")]
    [InlineData(0x200D, "zero-width joiner, which holds emoji sequences together")]
    public void ShouldKeepTheJoinersThatRealWritingNeeds(int codePoint, string why)
    {
        why.Should().NotBeEmpty();

        var candidate = "a" + (char)codePoint + "b";

        RequestStringNormaliser.TryNormalise(candidate, out var normalised).Should().BeTrue();
        normalised.Should().Be(candidate);
    }

    [Fact]
    public void ShouldKeepANonBmpCharacter()
    {
        // A surrogate pair is a legitimate character. Refusing every surrogate would refuse every emoji.
        var candidate = "chart " + char.ConvertFromUtf32(0x1F4CA);

        RequestStringNormaliser.TryNormalise(candidate, out var normalised).Should().BeTrue();
        normalised.Should().Be(candidate);
    }

    [Theory]
    [InlineData(0xD800, "unpaired high surrogate")]
    [InlineData(0xDC00, "unpaired low surrogate")]
    public void ShouldRejectAnUnpairedSurrogate(int codePoint, string why)
    {
        why.Should().NotBeEmpty();

        // It cannot be encoded as UTF-8, so it would either fail or be silently replaced somewhere downstream.
        RequestStringNormaliser.TryNormalise("a" + (char)codePoint + "b", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("  padded  ", "padded")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    public void ShouldTrimLast_SoAWhitespaceOnlyValueReachesNotEmptyAsEmpty(string candidate, string expected)
    {
        RequestStringNormaliser.TryNormalise(candidate, out var normalised).Should().BeTrue();
        normalised.Should().Be(expected);
    }

    [Fact]
    public void ShouldLeaveNullAlone()
    {
        RequestStringNormaliser.TryNormalise(null, out var normalised).Should().BeTrue();
        normalised.Should().BeNull();
    }

    private static readonly string CR = ((char)0x000D).ToString();
    private static readonly string LF = ((char)0x000A).ToString();
}