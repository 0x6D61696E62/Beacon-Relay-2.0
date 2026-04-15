using BeaconRelay.LpdReceiver.Options;

namespace BeaconRelay.LpdReceiver.Services;

public static class DeduplicationPolicy
{
    public static bool ShouldSkipWrite(DuplicateHandlingMode mode, bool isDuplicate)
        => isDuplicate && mode == DuplicateHandlingMode.SkipWrite;
}
