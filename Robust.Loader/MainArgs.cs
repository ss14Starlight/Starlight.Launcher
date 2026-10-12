using System.Collections.Generic;
using Robust.LoaderApi;

namespace Robust.Loader;

internal sealed class MainArgs(string[] args, IFileApi fileApi, IRedialApi? redialApi, IEnumerable<ApiMount>? apiMounts) : IMainArgs
{
    public string[] Args { get; } = args;
    public IFileApi FileApi { get; } = fileApi;
    public IRedialApi? RedialApi { get; } = redialApi;
    public IEnumerable<ApiMount>? ApiMounts { get; } = apiMounts;
}
