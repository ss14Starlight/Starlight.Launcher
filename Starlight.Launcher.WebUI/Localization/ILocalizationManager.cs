using System.Globalization;

namespace Starlight.Launcher.WebUI.Localization;

public interface ILocalizationManager
{
    event Action? Changed;

    string this[string key] { get; }

    string GetString(string key);

    string GetString(string key, params (string, object?)[] args);

    void SwitchLanguage(string cultureName);

    void SwitchLanguage(CultureInfo? culture);

    List<string> EnumarateAllLoadedLanguages();

    Task Initialize();
}
