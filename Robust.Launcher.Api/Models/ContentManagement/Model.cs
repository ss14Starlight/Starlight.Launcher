using System;

namespace Robust.Launcher.Api.Models.ContentManagement;

// Simple model classes for the content DB.

public sealed class ContentVersion
{
    public long Id;
    public byte[] Hash = default!;
    public string ForkId = default!;
    public string ForkVersion = default!;
    public DateTime LastUsed;
    public byte[]? ZipHash;
}
