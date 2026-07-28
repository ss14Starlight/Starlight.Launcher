using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Robust.Launcher.Api.Api;

/// <summary>
/// Provides types and helpers for the game server API.
/// </summary>
public static class ServerApi
{
    /// <summary>
    /// Represents the current status of a game server.
    /// </summary>
    public sealed record ServerStatus(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("players")]
        int PlayerCount,
        [property: JsonPropertyName("soft_max_players")]
        int SoftMaxPlayerCount,
        [property: JsonPropertyName("round_start_time")] string? RoundStartTime,
        [property: JsonPropertyName("run_level")] GameRunLevel? RunLevel,
        [property: JsonPropertyName("tags")] string[]? Tags);

    /// <summary>
    /// Represents the current state of the game round.
    /// </summary>
    public enum GameRunLevel
    {
        PreRoundLobby = 0,
        InRound = 1,
        PostRound = 2
    }

    /// <summary>
    /// Provides constants and helper methods for standard server tags.
    /// </summary>
    public static class Tags
    {
        public const string TagEighteenPlus = "18+";
        public const string TagRegion = "region:";
        public const string TagLanguage = "lang:";
        public const string TagRolePlay = "rp:";
        public const string TagNoTagInfer = "no_tag_infer";

        public const string RegionAfricaCentral = "af_c";
        public const string RegionAfricaNorth = "af_n";
        public const string RegionAfricaSouth = "af_s";
        public const string RegionAntarctica = "ata";
        public const string RegionAsiaEast = "as_e";
        public const string RegionAsiaNorth = "as_n";
        public const string RegionAsiaSouthEast = "as_se";
        public const string RegionCentralAmerica = "am_c";
        public const string RegionEuropeEast = "eu_e";
        public const string RegionEuropeWest = "eu_w";
        public const string RegionGreenland = "grl";
        public const string RegionIndia = "ind";
        public const string RegionMiddleEast = "me";
        public const string RegionMoon = "luna";
        public const string RegionNorthAmericaCentral = "am_n_c";
        public const string RegionNorthAmericaEast = "am_n_e";
        public const string RegionNorthAmericaWest = "am_n_w";
        public const string RegionOceania = "oce";
        public const string RegionSouthAmericaEast = "am_s_e";
        public const string RegionSouthAmericaSouth = "am_s_s";
        public const string RegionSouthAmericaWest = "am_s_w";

        public const string RolePlayNone = "none";
        public const string RolePlayLow = "low";
        public const string RolePlayMedium = "med";
        public const string RolePlayHigh = "high";

        /// <summary>
        /// Attempts to extract the region value from a server tag.
        /// </summary>
        public static bool TryRegion(string tag, [NotNullWhen(true)] out string? region) => TryTagPrefix(tag, TagRegion, out region);

        /// <summary>
        /// Attempts to extract the language value from a server tag.
        /// </summary>
        public static bool TryLanguage(string tag, [NotNullWhen(true)] out string? language) => TryTagPrefix(tag, TagLanguage, out language);

        /// <summary>
        /// Attempts to extract the role-play level from a server tag.
        /// </summary>
        public static bool TryRolePlay(string tag, [NotNullWhen(true)] out string? rolePlay) => TryTagPrefix(tag, TagRolePlay, out rolePlay);

        /// <summary>
        /// Attempts to extract the value of a tag with the specified prefix.
        /// </summary>
        public static bool TryTagPrefix(string tag, string prefix, [NotNullWhen(true)] out string? value)
        {
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = null;
                return false;
            }

            value = tag[prefix.Length..];
            return true;
        }
    }
}
