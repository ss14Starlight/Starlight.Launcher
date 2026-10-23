using System.Runtime.InteropServices;
using Starlight.Launcher.Services.Auth;

namespace Starlight.Launcher.Platforms.Linux.Services.Settings;

internal sealed unsafe class LibSecretStore : ISecretStore
{
    private const string SchemaName = "org.starlight.launcher.LoginKey";
    private const string AttributeName = "purpose";
    private const string AttributeValue = "logins";
    private const string Label = "Starlight Launcher login key";

    private const int SchemaSize = 16 + (32 * 16) + 8 + (7 * 8);
    private const int SchemaAttributesOffset = 16;
    private const int GErrorMessageOffset = 8; // GError { GQuark domain; gint code; gchar *message; }

    private readonly delegate* unmanaged<nint, nint, nint, nint*, nint> _lookup;
    private readonly delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint*, int> _store;
    private readonly delegate* unmanaged<nint, void> _passwordFree;
    private readonly delegate* unmanaged<nint, nint, nint> _hashTableNew;
    private readonly delegate* unmanaged<nint, nint, nint, int> _hashTableInsert;
    private readonly delegate* unmanaged<nint, void> _hashTableUnref;
    private readonly delegate* unmanaged<nint, void> _errorFree;
    private readonly nint _strHash;
    private readonly nint _strEqual;

    private readonly nint _schema;
    private readonly nint _attributeName;
    private readonly nint _attributeValue;
    private readonly nint _label;

    public string Name => "Secret Service";

    private LibSecretStore(nint secret, nint glib)
    {
        _lookup = (delegate* unmanaged<nint, nint, nint, nint*, nint>)NativeLibrary.GetExport(secret, "secret_password_lookupv_sync");
        _store = (delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint*, int>)NativeLibrary.GetExport(secret, "secret_password_storev_sync");
        _passwordFree = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(secret, "secret_password_free");
        _hashTableNew = (delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(glib, "g_hash_table_new");
        _hashTableInsert = (delegate* unmanaged<nint, nint, nint, int>)NativeLibrary.GetExport(glib, "g_hash_table_insert");
        _hashTableUnref = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(glib, "g_hash_table_unref");
        _errorFree = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(glib, "g_error_free");
        _strHash = NativeLibrary.GetExport(glib, "g_str_hash");
        _strEqual = NativeLibrary.GetExport(glib, "g_str_equal");

        _attributeName = Marshal.StringToCoTaskMemUTF8(AttributeName);
        _attributeValue = Marshal.StringToCoTaskMemUTF8(AttributeValue);
        _label = Marshal.StringToCoTaskMemUTF8(Label);

        // flags = SECRET_SCHEMA_NONE, attributes[0].type = SECRET_SCHEMA_ATTRIBUTE_STRING, both 0.
        _schema = (nint)NativeMemory.AllocZeroed(SchemaSize);
        *(nint*)_schema = Marshal.StringToCoTaskMemUTF8(SchemaName);
        *(nint*)(_schema + SchemaAttributesOffset) = _attributeName;
    }

    public static LibSecretStore? TryCreate()
    {
        if (IntPtr.Size != 8)
            return null;

        if (!NativeLibrary.TryLoad("libsecret-1.so.0", out var secret)
            || !NativeLibrary.TryLoad("libglib-2.0.so.0", out var glib))
        {
            return null;
        }

        try { return new LibSecretStore(secret, glib); }
        catch (EntryPointNotFoundException) { return null; }
    }

    public byte[]? Load()
    {
        var attributes = CreateAttributes();
        nint error = 0;
        nint password;
        try { password = _lookup(_schema, attributes, 0, &error); }
        finally { _hashTableUnref(attributes); }

        ThrowIfError(error, "lookup");
        if (password == 0)
            return null;

        try { return Convert.FromBase64String(Marshal.PtrToStringUTF8(password)!); }
        catch (FormatException) { return null; }
        finally { _passwordFree(password); }
    }

    public void Save(byte[] secret)
    {
        var attributes = CreateAttributes();
        var password = Marshal.StringToCoTaskMemUTF8(Convert.ToBase64String(secret));
        nint error = 0;
        int stored;
        try
        {
            stored = _store(_schema, attributes, 0, _label, password, 0, &error);
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(password);
            _hashTableUnref(attributes);
        }

        ThrowIfError(error, "store");
        if (stored == 0)
            throw new SecretStoreException("libsecret refused to store the login key");
    }

    private nint CreateAttributes()
    {
        var table = _hashTableNew(_strHash, _strEqual);
        _ = _hashTableInsert(table, _attributeName, _attributeValue);
        return table;
    }

    private void ThrowIfError(nint error, string operation)
    {
        if (error == 0)
            return;

        var message = Marshal.PtrToStringUTF8(*(nint*)(error + GErrorMessageOffset));
        _errorFree(error);
        throw new SecretStoreException($"libsecret {operation} failed: {message}");
    }
}
