using BeaconRelay.LpdReceiver.Services;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class FileSanitizationAndHashTests
{
    [Fact]
    public void Sanitize_StripsTraversalCharacters()
    {
        var sanitized = FileNameSanitizer.Sanitize("../../etc/passwd");
        Assert.Equal("passwd", sanitized);
    }

    [Fact]
    public void ComputeHex_IsStable()
    {
        var hasher = new Sha256Hasher();
        var hash = hasher.ComputeHex("hello"u8.ToArray());

        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash);
    }
}
