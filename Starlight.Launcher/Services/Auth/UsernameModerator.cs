using System.Globalization;
using System.Text;

namespace Starlight.Launcher.Services.Auth;

public enum UsernameModerationOutcome
{
    Accepted,

    Sanitized,

    Rejected
}

public readonly record struct UsernameModerationResult(
    UsernameModerationOutcome Outcome,
    string Username,
    string? Reason)
{
    public bool IsUsable => Outcome != UsernameModerationOutcome.Rejected;
}

public static class UsernameModerator
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    public const double MaxNonAsciiRatio = 0.5;

    public static UsernameModerationResult Moderate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Reject("The username is empty.");

        var normalized = TryNormalize(raw);
        var cleaned = Clean(normalized);

        if (cleaned.Length == 0)
            return Reject("The username has no usable characters.");

        Measure(cleaned, out int runeCount, out int nonAscii, out int asciiLetters);

        if (runeCount >= MinLength && (double)nonAscii / runeCount <= MaxNonAsciiRatio)
        {
            var final = Clamp(cleaned);
            var outcome = final == raw
                ? UsernameModerationOutcome.Accepted
                : UsernameModerationOutcome.Sanitized;
            return new(outcome, final, null);
        }

        if (asciiLetters > 0)
        {
            var asciiCore = Clamp(KeepAscii(cleaned));
            if (CountRunes(asciiCore) >= MinLength)
                return new(UsernameModerationOutcome.Sanitized, asciiCore, null);
        }

        return Reject(runeCount < MinLength
            ? $"The username is too short - it needs at least {MinLength} characters."
            : "The username doesn't contain enough Latin letters to use.");
    }

    private static UsernameModerationResult Reject(string reason) =>
        new(UsernameModerationOutcome.Rejected, string.Empty, reason);

    private static string TryNormalize(string s)
    {
        try
        {
            return s.IsNormalized(NormalizationForm.FormKC) ? s : s.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return s;
        }
    }

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;

        foreach (var rune in s.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (!IsAllowed(rune))
                continue;

            if (pendingSpace)
            {
                _ = sb.Append(' ');
                pendingSpace = false;
            }

            _ = sb.Append(rune.ToString());
        }

        return sb.ToString();
    }

    private static bool IsAllowed(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.Control => false,
        UnicodeCategory.Format => false,
        UnicodeCategory.Surrogate => false,
        UnicodeCategory.PrivateUse => false,
        UnicodeCategory.OtherNotAssigned => false,
        UnicodeCategory.LineSeparator => false,
        UnicodeCategory.ParagraphSeparator => false,
        UnicodeCategory.NonSpacingMark => false,
        UnicodeCategory.EnclosingMark => false,
        _ => true
    };

    private static string KeepAscii(string s)
    {
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;

        foreach (var rune in s.EnumerateRunes())
        {
            int v = rune.Value;
            bool keep = v is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.';

            if (keep)
            {
                if (pendingSpace) { _ = sb.Append(' '); pendingSpace = false; }
                _ = sb.Append((char)v);
            }
            else if (v == ' ')
            {
                pendingSpace = sb.Length > 0;
            }
        }

        return sb.ToString();
    }

    private static void Measure(string s, out int runeCount, out int nonAscii, out int asciiLetters)
    {
        runeCount = nonAscii = asciiLetters = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            runeCount++;
            if (rune.Value > 0x7F)
                nonAscii++;
            else if (rune.Value is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
                asciiLetters++;
        }
    }

    private static int CountRunes(string s)
    {
        var n = 0;
        foreach (var _ in s.EnumerateRunes()) n++;
        return n;
    }

    private static string Clamp(string s)
    {
        if (CountRunes(s) <= MaxLength)
            return s;

        var sb = new StringBuilder(MaxLength);
        var n = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            if (n++ == MaxLength) break;
            _ = sb.Append(rune.ToString());
        }
        return sb.ToString().TrimEnd();
    }
}
