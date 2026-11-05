namespace Starlight.Launcher.WebUI.Models.Settings;

public sealed record DefaultSettingChange(string Key, string CurrentValue, string NewDefault, string? OldDefault, string MergedValue);

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotTrackedDefaultAttribute : Attribute;
