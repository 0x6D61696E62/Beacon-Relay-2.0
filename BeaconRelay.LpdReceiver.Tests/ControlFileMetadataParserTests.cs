using BeaconRelay.LpdReceiver.Protocol;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class ControlFileMetadataParserTests
{
    [Fact]
    public void Parse_ExtractsMetadataAndJobId()
    {
        const string control = "Hprinter-host\nPtest-user\nJinvoice-42\nCfinance\nLbanner\nNsource.pdf\n";
        var parser = new ControlFileMetadataParser();

        var metadata = parser.Parse(control, "cfA0042printer-host");

        Assert.Equal("printer-host", metadata.HostName);
        Assert.Equal("test-user", metadata.UserName);
        Assert.Equal("invoice-42", metadata.JobName);
        Assert.Equal("finance", metadata.BannerClass);
        Assert.Equal("banner", metadata.BannerName);
        Assert.Equal("source.pdf", metadata.SourceFileHints);
        Assert.Equal("0042", metadata.LpdJobId);
    }
}
