using Avalonia.Media;
using Starlight.Launcher.WebUI.Models.Settings;

namespace Starlight.Launcher.Models.Tray;

public sealed record TrayPalette(
    Color Background,
    Color Border,
    Color Foreground,
    Color Muted,
    Color Hover,
    Color Pressed,
    Color Separator,
    Color Accent,
    Color Danger,
    bool IsDark)
{
    private readonly record struct Oklch(double L, double C, double H);

    private readonly record struct ThemeTokens(Oklch Surface, Oklch Text, Oklch Primary, Oklch Error, bool IsDark);

    public static TrayPalette FromTheme(AppTheme theme, bool systemPrefersDark)
    {
        var t = GetTokens(theme, systemPrefersDark);
        var primary = ToColor(t.Primary);
        var text = ToColor(t.Text);

        return new TrayPalette(
            Background: ToColor(t.Surface),
            Border: WithAlpha(primary, t.IsDark ? 0.22 : 0.18),
            Foreground: WithAlpha(text, 0.95),
            Muted: WithAlpha(text, 0.60),
            Hover: WithAlpha(primary, t.IsDark ? 0.16 : 0.12),
            Pressed: WithAlpha(primary, t.IsDark ? 0.26 : 0.20),
            Separator: WithAlpha(text, 0.10),
            Accent: primary,
            Danger: ToColor(t.Error),
            IsDark: t.IsDark);
    }

    private static ThemeTokens GetTokens(AppTheme theme, bool systemPrefersDark) => theme switch
    {
        AppTheme.EmeraldLight => Light(new(0.23, 0.015, 165), new(0.58, 0.13, 162), new(0.56, 0.19, 25)),
        AppTheme.EmeraldDark => Dark(new(0.20, 0.01, 240), new(0.94, 0.01, 240), new(0.74, 0.15, 162), new(0.68, 0.18, 25)),
        AppTheme.AmberLight => Light(new(0.25, 0.015, 60), new(0.62, 0.14, 58), new(0.56, 0.19, 25)),
        AppTheme.AmberDark => Dark(new(0.21, 0.012, 70), new(0.93, 0.012, 75), new(0.80, 0.14, 70), new(0.68, 0.18, 25)),
        AppTheme.Midnight => Dark(new(0.20, 0.024, 258), new(0.93, 0.014, 250), new(0.74, 0.13, 230), new(0.70, 0.17, 22)),
        AppTheme.RoseLight => Light(new(0.24, 0.018, 345), new(0.56, 0.16, 350), new(0.55, 0.20, 25)),
        AppTheme.RoseDark => Dark(new(0.20, 0.016, 350), new(0.93, 0.012, 348), new(0.72, 0.16, 352), new(0.70, 0.17, 22)),
        AppTheme.VioletLight => Light(new(0.24, 0.018, 300), new(0.55, 0.17, 295), new(0.56, 0.19, 25)),
        AppTheme.VioletDark => Dark(new(0.20, 0.020, 300), new(0.93, 0.014, 300), new(0.75, 0.15, 298), new(0.70, 0.17, 25)),
        AppTheme.OceanLight => Light(new(0.24, 0.015, 220), new(0.58, 0.12, 205), new(0.56, 0.19, 25)),
        AppTheme.OceanDark => Dark(new(0.20, 0.016, 220), new(0.93, 0.012, 215), new(0.76, 0.12, 205), new(0.70, 0.17, 25)),
        AppTheme.CitrusLight => Light(new(0.24, 0.016, 140), new(0.57, 0.15, 132), new(0.56, 0.19, 25)),
        AppTheme.CitrusDark => Dark(new(0.20, 0.014, 140), new(0.93, 0.012, 135), new(0.80, 0.16, 133), new(0.70, 0.17, 25)),
        AppTheme.SlateLight => Light(new(0.22, 0.010, 250), new(0.48, 0.04, 250), new(0.56, 0.19, 25)),
        AppTheme.SlateDark => Dark(new(0.20, 0.006, 250), new(0.93, 0.008, 250), new(0.78, 0.03, 250), new(0.70, 0.17, 22)),
        _ => GetTokens(systemPrefersDark ? AppTheme.EmeraldDark : AppTheme.EmeraldLight, systemPrefersDark),
    };

    private static ThemeTokens Light(Oklch text, Oklch primary, Oklch error) => new(new(1, 0, 0), text, primary, error, false);

    private static ThemeTokens Dark(Oklch surface, Oklch text, Oklch primary, Oklch error) => new(surface, text, primary, error, true);

    private static Color WithAlpha(Color c, double alpha) => new((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    private static Color ToColor(Oklch c)
    {
        var hue = c.H * Math.PI / 180;
        var a = c.C * Math.Cos(hue);
        var b = c.C * Math.Sin(hue);

        var l = Math.Pow(c.L + (0.3963377774 * a) + (0.2158037573 * b), 3);
        var m = Math.Pow(c.L - (0.1055613458 * a) - (0.0638541728 * b), 3);
        var s = Math.Pow(c.L - (0.0894841775 * a) - (1.2914855480 * b), 3);

        var r = (4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s);
        var g = (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s);
        var bl = (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s);

        return Color.FromRgb(ToSrgbByte(r), ToSrgbByte(g), ToSrgbByte(bl));
    }

    private static byte ToSrgbByte(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        var srgb = linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
        return (byte)Math.Round(srgb * 255);
    }
}
