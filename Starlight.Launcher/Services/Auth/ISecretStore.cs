namespace Starlight.Launcher.Services.Auth;

internal interface ISecretStore
{
    string Name { get; }

    byte[]? Load();

    void Save(byte[] secret);
}

internal sealed class SecretStoreException(string message) : Exception(message);
