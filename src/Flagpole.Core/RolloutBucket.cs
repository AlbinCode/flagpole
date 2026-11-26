using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Flagpole.Core;

/// <summary>
/// Places a user in a stable bucket between 0 and 100 for a given flag.
/// </summary>
/// <remarks>
/// string.GetHashCode is randomized per process in .NET, so it cannot be used:
/// a user would land in a different bucket after every restart or on every
/// replica. SHA-256 gives the same answer everywhere, and including the flag
/// key means a user who is early in one rollout is not early in all of them.
/// </remarks>
public static class RolloutBucket
{
    private const int Resolution = 10_000;

    /// <returns>A value from 0 (inclusive) to 100 (exclusive) with two decimals.</returns>
    public static double For(string flagKey, string userId)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{flagKey}:{userId}"), hash);

        var value = BinaryPrimitives.ReadUInt32BigEndian(hash);
        return value % Resolution / (double)(Resolution / 100);
    }
}
