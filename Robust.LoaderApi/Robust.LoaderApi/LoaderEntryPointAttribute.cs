using System;

namespace Robust.LoaderApi;

/// <summary>
///     Specifies which type in the assembly is responsible for being an engine loader.
/// </summary>
/// <remarks>
///     The type must implement <see cref="ILoaderEntryPoint"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LoaderEntryPointAttribute(Type loaderEntryPointType) : Attribute
{
    public Type LoaderEntryPointType { get; } = loaderEntryPointType;
}
