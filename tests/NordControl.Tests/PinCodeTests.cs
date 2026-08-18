using NordControl.Protocol;

namespace NordControl.Tests;

public class PinCodeTests
{
    [Theory]
    [InlineData("ABC123")]
    [InlineData("abc123")]
    [InlineData("K7M2P9")]
    [InlineData("a1b2c3")]
    public void IsWellFormed_Accepts_ThreeLettersAndThreeDigits(string pin)
    {
        Assert.True(PinCode.IsWellFormed(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("ABCDEF")]
    [InlineData("123456")]
    [InlineData("AB12CD")]
    [InlineData("ABC12")]
    [InlineData("ABC1234")]
    [InlineData("AB@123")]
    public void IsWellFormed_Rejects_WrongShape(string? pin)
    {
        Assert.False(PinCode.IsWellFormed(pin));
    }

    [Fact]
    public void Equals_IsCaseInsensitive()
    {
        Assert.True(PinCode.Equals("k7m2p9", "K7M2P9"));
        Assert.False(PinCode.Equals("K7M2P9", "K7M2P8"));
    }

    [Fact]
    public void Generate_AlwaysWellFormed_WithoutAmbiguousGlyphs()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 200; i++)
        {
            var pin = PinCode.Generate();
            Assert.True(PinCode.IsWellFormed(pin), pin);
            Assert.Equal(ProtocolConstants.PinLength, pin.Length);
            Assert.Equal(pin.ToUpperInvariant(), pin);
            Assert.DoesNotContain('0', pin);
            Assert.DoesNotContain('1', pin);
            Assert.DoesNotContain('I', pin);
            Assert.DoesNotContain('L', pin);
            Assert.DoesNotContain('O', pin);
            seen.Add(pin);
        }

        Assert.True(seen.Count > 50);
    }
}
