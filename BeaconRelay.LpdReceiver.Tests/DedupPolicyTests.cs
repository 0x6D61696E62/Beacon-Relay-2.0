using BeaconRelay.LpdReceiver.Options;
using BeaconRelay.LpdReceiver.Services;

namespace BeaconRelay.LpdReceiver.Tests;

public sealed class DedupPolicyTests
{
    [Theory]
    [InlineData(DuplicateHandlingMode.MarkAndStore, false, false)]
    [InlineData(DuplicateHandlingMode.MarkAndStore, true, false)]
    [InlineData(DuplicateHandlingMode.SkipWrite, false, false)]
    [InlineData(DuplicateHandlingMode.SkipWrite, true, true)]
    public void ShouldSkipWrite_EvaluatesConfiguredBehavior(DuplicateHandlingMode mode, bool isDuplicate, bool expected)
    {
        var actual = DeduplicationPolicy.ShouldSkipWrite(mode, isDuplicate);
        Assert.Equal(expected, actual);
    }
}
