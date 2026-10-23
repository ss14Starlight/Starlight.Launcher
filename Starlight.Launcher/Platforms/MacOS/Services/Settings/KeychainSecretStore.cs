using System.Runtime.InteropServices;
using Starlight.Launcher.Services.Auth;

namespace Starlight.Launcher.Platforms.MacOS.Services.Settings;

internal sealed unsafe class KeychainSecretStore : ISecretStore
{
    private const string SecurityPath = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string ServiceName = "Starlight.Launcher";
    private const string AccountName = "logins.key";

    private const int ErrSecSuccess = 0;
    private const int ErrSecDuplicateItem = -25299;
    private const int ErrSecItemNotFound = -25300;
    private const uint CFStringEncodingUtf8 = 0x08000100;

    private readonly delegate* unmanaged<nint, nint*, nint*, nint, nint, nint, nint> _dictionaryCreate;
    private readonly delegate* unmanaged<nint, nint, uint, nint> _stringCreate;
    private readonly delegate* unmanaged<nint, byte*, nint, nint> _dataCreate;
    private readonly delegate* unmanaged<nint, nint> _dataGetLength;
    private readonly delegate* unmanaged<nint, byte*> _dataGetBytePtr;
    private readonly delegate* unmanaged<nint, void> _release;
    private readonly delegate* unmanaged<nint, nint*, int> _itemCopyMatching;
    private readonly delegate* unmanaged<nint, nint*, int> _itemAdd;
    private readonly delegate* unmanaged<nint, nint, int> _itemUpdate;

    private readonly nint _keyCallBacks;
    private readonly nint _valueCallBacks;
    private readonly nint _true;
    private readonly nint _class;
    private readonly nint _classGenericPassword;
    private readonly nint _attrService;
    private readonly nint _attrAccount;
    private readonly nint _valueData;
    private readonly nint _returnData;
    private readonly nint _matchLimit;
    private readonly nint _matchLimitOne;

    private readonly nint _service;
    private readonly nint _account;

    public string Name => "Keychain";

    private KeychainSecretStore(nint security, nint cf)
    {
        _dictionaryCreate = (delegate* unmanaged<nint, nint*, nint*, nint, nint, nint, nint>)NativeLibrary.GetExport(cf, "CFDictionaryCreate");
        _stringCreate = (delegate* unmanaged<nint, nint, uint, nint>)NativeLibrary.GetExport(cf, "CFStringCreateWithCString");
        _dataCreate = (delegate* unmanaged<nint, byte*, nint, nint>)NativeLibrary.GetExport(cf, "CFDataCreate");
        _dataGetLength = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(cf, "CFDataGetLength");
        _dataGetBytePtr = (delegate* unmanaged<nint, byte*>)NativeLibrary.GetExport(cf, "CFDataGetBytePtr");
        _release = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(cf, "CFRelease");
        _itemCopyMatching = (delegate* unmanaged<nint, nint*, int>)NativeLibrary.GetExport(security, "SecItemCopyMatching");
        _itemAdd = (delegate* unmanaged<nint, nint*, int>)NativeLibrary.GetExport(security, "SecItemAdd");
        _itemUpdate = (delegate* unmanaged<nint, nint, int>)NativeLibrary.GetExport(security, "SecItemUpdate");

        _keyCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks");
        _valueCallBacks = NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks");
        _true = Constant(cf, "kCFBooleanTrue");
        _class = Constant(security, "kSecClass");
        _classGenericPassword = Constant(security, "kSecClassGenericPassword");
        _attrService = Constant(security, "kSecAttrService");
        _attrAccount = Constant(security, "kSecAttrAccount");
        _valueData = Constant(security, "kSecValueData");
        _returnData = Constant(security, "kSecReturnData");
        _matchLimit = Constant(security, "kSecMatchLimit");
        _matchLimitOne = Constant(security, "kSecMatchLimitOne");

        _service = CreateString(ServiceName);
        _account = CreateString(AccountName);
    }

    public static KeychainSecretStore? TryCreate()
    {
        if (!NativeLibrary.TryLoad(SecurityPath, out var security)
            || !NativeLibrary.TryLoad(CoreFoundationPath, out var cf))
        {
            return null;
        }

        try { return new KeychainSecretStore(security, cf); }
        catch (EntryPointNotFoundException) { return null; }
    }

    public byte[]? Load()
    {
        var query = CreateDictionary(
            [_class, _attrService, _attrAccount, _returnData, _matchLimit],
            [_classGenericPassword, _service, _account, _true, _matchLimitOne]);

        nint result = 0;
        int status;
        try { status = _itemCopyMatching(query, &result); }
        finally { _release(query); }

        if (status == ErrSecItemNotFound)
            return null;

        ThrowIfFailed(status, "read");

        try { return new ReadOnlySpan<byte>(_dataGetBytePtr(result), (int)_dataGetLength(result)).ToArray(); }
        finally { _release(result); }
    }

    public void Save(byte[] secret)
    {
        nint data;
        fixed (byte* bytes = secret)
            data = _dataCreate(0, bytes, secret.Length);

        try
        {
            var item = CreateDictionary(
                [_class, _attrService, _attrAccount, _valueData],
                [_classGenericPassword, _service, _account, data]);

            int status;
            try { status = _itemAdd(item, null); }
            finally { _release(item); }

            if (status == ErrSecDuplicateItem)
            {
                var query = CreateDictionary(
                    [_class, _attrService, _attrAccount],
                    [_classGenericPassword, _service, _account]);
                var update = CreateDictionary([_valueData], [data]);

                try { status = _itemUpdate(query, update); }
                finally
                {
                    _release(update);
                    _release(query);
                }
            }

            ThrowIfFailed(status, "store");
        }
        finally { _release(data); }
    }

    private nint CreateDictionary(ReadOnlySpan<nint> keys, ReadOnlySpan<nint> values)
    {
        fixed (nint* k = keys)
        {
            fixed (nint* v = values)
                return _dictionaryCreate(0, k, v, keys.Length, _keyCallBacks, _valueCallBacks);
        }
    }

    private nint CreateString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return _stringCreate(0, utf8, CFStringEncodingUtf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    private static nint Constant(nint library, string name) => *(nint*)NativeLibrary.GetExport(library, name);

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status != ErrSecSuccess)
            throw new SecretStoreException($"Keychain {operation} failed: OSStatus {status}");
    }
}
