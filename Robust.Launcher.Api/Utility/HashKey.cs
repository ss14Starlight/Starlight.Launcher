using System;

namespace Robust.Launcher.Api.Utility;

/// <summary>
/// Wraps a byte array and implements equality for it, for use as a hashset/dictionary key.
/// </summary>
public struct HashKey(byte[] data) : IEquatable<HashKey>
{
    public byte[] Data = data;

    public readonly bool Equals(HashKey other) => Data.AsSpan().SequenceEqual(other.Data);

    public override readonly bool Equals(object? obj) => obj is HashKey other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hc = new HashCode();
        hc.AddBytes(Data);
        return hc.ToHashCode();
    }

    public static bool operator ==(HashKey left, HashKey right) => left.Equals(right);

    public static bool operator !=(HashKey left, HashKey right) => !left.Equals(right);
}
