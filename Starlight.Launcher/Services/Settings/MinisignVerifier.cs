using System.Text;
using NSec.Cryptography;

namespace Starlight.Launcher.Services.Settings;

/// <summary>
/// Verifies <see href="https://jedisct1.github.io/minisign/">minisign</see> signatures. Only the prehashed
/// ("ED", Ed25519 over BLAKE2b-512) form is accepted, so callers pass the file's hash instead of the file.
/// </summary>
internal static class MinisignVerifier
{
    private const string UntrustedPrefix = "untrusted comment:";
    private const string TrustedPrefix = "trusted comment: ";
    private const int KeyIdLength = 8;
    private const int SignatureLength = 64;
    private const int PublicKeyLength = 32;

    public const int HashLength = 64;

    public sealed record PublicKeyInfo(byte[] KeyId, PublicKey Key);

    /// <summary>
    /// Accepts the whole <c>.pub</c> file or just its base64 line.
    /// </summary>
    public static PublicKeyInfo ParsePublicKey(string text)
    {
        var line = SplitLines(text).LastOrDefault(l => !l.StartsWith(UntrustedPrefix, StringComparison.Ordinal))
            ?? throw new InvalidDataException("The minisign public key is empty.");

        var raw = DecodeBase64(line, 2 + KeyIdLength + PublicKeyLength, "public key");
        if (raw[0] != 'E' || raw[1] != 'd')
            throw new InvalidDataException("Not a minisign Ed25519 public key.");

        var key = PublicKey.Import(SignatureAlgorithm.Ed25519, raw.AsSpan(2 + KeyIdLength), KeyBlobFormat.RawPublicKey);
        return new PublicKeyInfo(raw[2..(2 + KeyIdLength)], key);
    }

    /// <summary>
    /// Checks a <c>.minisig</c> file against the BLAKE2b-512 hash of the signed file and returns its trusted comment.
    /// </summary>
    public static string Verify(PublicKeyInfo publicKey, string signatureText, ReadOnlySpan<byte> blake2b512)
    {
        if (blake2b512.Length != HashLength)
            throw new ArgumentException($"Expected a {HashLength}-byte BLAKE2b hash.", nameof(blake2b512));

        var lines = SplitLines(signatureText).ToArray();
        if (lines.Length < 4 || !lines[0].StartsWith(UntrustedPrefix, StringComparison.Ordinal) || !lines[2].StartsWith(TrustedPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("Malformed minisign signature.");

        var sig = DecodeBase64(lines[1], 2 + KeyIdLength + SignatureLength, "signature");
        if (sig[0] != 'E' || sig[1] != 'D')
            throw new InvalidDataException("Only prehashed minisign signatures are accepted.");

        if (!sig.AsSpan(2, KeyIdLength).SequenceEqual(publicKey.KeyId))
            throw new InvalidDataException("The update was signed with a different key.");

        var signature = sig.AsSpan(2 + KeyIdLength);
        if (!SignatureAlgorithm.Ed25519.Verify(publicKey.Key, blake2b512, signature))
            throw new InvalidDataException("The update's signature is invalid. The download may have been tampered with.");

        // The global signature covers the file signature plus the trusted comment, so the comment can't be swapped.
        var trustedComment = lines[2][TrustedPrefix.Length..];
        var globalSig = DecodeBase64(lines[3], SignatureLength, "global signature");
        byte[] signedComment = [.. signature, .. Encoding.UTF8.GetBytes(trustedComment)];

        return !SignatureAlgorithm.Ed25519.Verify(publicKey.Key, signedComment, globalSig)
            ? throw new InvalidDataException("The update signature's trusted comment is invalid.")
            : trustedComment;
    }

    /// <summary>
    /// Reads <c>key:value</c> pairs from a trusted comment such as <c>file:x.exe version:1.2.3</c>.
    /// </summary>
    public static Dictionary<string, string> ParseTrustedComment(string comment)
        => comment
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First()[1], StringComparer.Ordinal);

    private static IEnumerable<string> SplitLines(string text)
        => text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    private static byte[] DecodeBase64(string text, int expectedLength, string what)
    {
        var buffer = new byte[expectedLength];
        return Convert.TryFromBase64String(text.Trim(), buffer, out var written) && written == expectedLength
            ? buffer
            : throw new InvalidDataException($"Malformed minisign {what}.");
    }
}
