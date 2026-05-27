using System.Security.Cryptography;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class Sha256Hasher
{
    public string ComputeHex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
