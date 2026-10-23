using System.Text;
using NSec.Cryptography;
using SpaceWizards.Sodium;
using Starlight.Launcher.Services.Settings;

namespace Starlight.Launcher.Tests;

public sealed class MinisignVerifierTests
{
    private static readonly byte[] _keyId = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] _file = Encoding.UTF8.GetBytes("installer bytes");

    private const string Comment = "file:Starlight.Launcher-win-x64-1.2.3-setup.exe version:1.2.3";

    private static Key CreateKey()
        => Key.Create(SignatureAlgorithm.Ed25519);

    private static byte[] Hash(byte[] data)
    {
        var hash = new byte[MinisignVerifier.HashLength];
        _ = CryptoGenericHashBlake2B.Hash(hash, data, []);
        return hash;
    }

    private static string PublicKeyFile(Key key, byte[]? keyId = null)
    {
        byte[] raw = [(byte)'E', (byte)'d', .. keyId ?? _keyId, .. key.PublicKey.Export(KeyBlobFormat.RawPublicKey)];
        return $"untrusted comment: minisign public key\n{Convert.ToBase64String(raw)}\n";
    }

    // Same layout minisign -S writes.
    private static string SignatureFile(Key key, byte[] data, string comment, string algorithm = "ED")
    {
        var signature = SignatureAlgorithm.Ed25519.Sign(key, algorithm == "ED" ? Hash(data) : data);
        byte[] sigLine = [.. Encoding.ASCII.GetBytes(algorithm), .. _keyId, .. signature];
        var global = SignatureAlgorithm.Ed25519.Sign(key, [.. signature, .. Encoding.UTF8.GetBytes(comment)]);

        return $"untrusted comment: signature from minisign secret key\r\n{Convert.ToBase64String(sigLine)}\r\n"
            + $"trusted comment: {comment}\r\n{Convert.ToBase64String(global)}\r\n";
    }

    [Fact]
    public void ValidSignature_ReturnsTrustedComment()
    {
        using var key = CreateKey();
        var publicKey = MinisignVerifier.ParsePublicKey(PublicKeyFile(key));

        Assert.Equal(Comment, MinisignVerifier.Verify(publicKey, SignatureFile(key, _file, Comment), Hash(_file)));
    }

    [Fact]
    public void TamperedFile_IsRejected()
    {
        using var key = CreateKey();
        var publicKey = MinisignVerifier.ParsePublicKey(PublicKeyFile(key));

        _ = Assert.Throws<InvalidDataException>(() =>
            MinisignVerifier.Verify(publicKey, SignatureFile(key, _file, Comment), Hash([.. _file, 0])));
    }

    [Fact]
    public void OtherKey_IsRejected()
    {
        using var key = CreateKey();
        using var other = CreateKey();
        var publicKey = MinisignVerifier.ParsePublicKey(PublicKeyFile(other));

        _ = Assert.Throws<InvalidDataException>(() =>
            MinisignVerifier.Verify(publicKey, SignatureFile(key, _file, Comment), Hash(_file)));
    }

    [Fact]
    public void OtherKeyId_IsRejected()
    {
        using var key = CreateKey();
        var publicKey = MinisignVerifier.ParsePublicKey(PublicKeyFile(key, [8, 7, 6, 5, 4, 3, 2, 1]));

        _ = Assert.Throws<InvalidDataException>(() =>
            MinisignVerifier.Verify(publicKey, SignatureFile(key, _file, Comment), Hash(_file)));
    }

    [Fact]
    public void SwappedTrustedComment_IsRejected()
    {
        using var key = CreateKey();
        var publicKey = MinisignVerifier.ParsePublicKey(PublicKeyFile(key));
        var signature = SignatureFile(key, _file, Comment).Replace("version:1.2.3", "version:9.9.9");

        _ = Assert.Throws<InvalidDataException>(() => MinisignVerifier.Verify(publicKey, signature, Hash(_file)));
    }

    [Fact]
    public void LegacySignature_IsRejected()
    {
        using var key = CreateKey();
        var publicKey = MinisignVerifier.ParsePublicKey(PublicKeyFile(key));

        _ = Assert.Throws<InvalidDataException>(() =>
            MinisignVerifier.Verify(publicKey, SignatureFile(key, _file, Comment, "Ed"), Hash(_file)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("untrusted comment: x\nnot base64")]
    [InlineData("untrusted comment: x\nRWQ=")]
    public void MalformedPublicKey_IsRejected(string text)
        => Assert.Throws<InvalidDataException>(() => MinisignVerifier.ParsePublicKey(text));

    [Fact]
    public void TrustedComment_IsParsed()
    {
        var parsed = MinisignVerifier.ParseTrustedComment(Comment);

        Assert.Equal("Starlight.Launcher-win-x64-1.2.3-setup.exe", parsed["file"]);
        Assert.Equal("1.2.3", parsed["version"]);
    }
}
