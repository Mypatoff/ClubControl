using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class ClientNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void Nothing_usable_gives_an_empty_name(string? raw) =>
        Assert.Equal("", ClientName.Clean(raw));

    [Theory]
    [InlineData("no23", "no23")]
    [InlineData("  no23  ", "no23")]
    [InlineData("no23\r\n", "no23")]      // Notepad adds a line break at the end
    [InlineData("PC 5", "PC 5")]          // spaces inside are kept
    [InlineData("ПК 5", "ПК 5")]          // Cyrillic / Uzbek names survive
    public void A_normal_name_is_kept_tidied(string raw, string expected) =>
        Assert.Equal(expected, ClientName.Clean(raw));

    [Fact]
    public void Only_the_first_line_counts()
    {
        Assert.Equal("no23", ClientName.Clean("no23\nsecond line"));
        Assert.Equal("no23", ClientName.Clean("no23\r\nsecond line"));
    }

    [Fact]
    public void Control_characters_are_dropped()
    {
        Assert.Equal("no23", ClientName.Clean("no\t2\u00003"));
    }

    [Fact]
    public void A_long_name_is_cut_to_the_maximum_without_trailing_spaces()
    {
        var cleaned = ClientName.Clean(new string('a', 100));
        Assert.Equal(ClientName.MaxLength, cleaned.Length);

        // The cut falls on a space: it must not leave one hanging at the end.
        var withSpace = ClientName.Clean(new string('a', ClientName.MaxLength - 1) + " tail");
        Assert.Equal(new string('a', ClientName.MaxLength - 1), withSpace);
    }
}
