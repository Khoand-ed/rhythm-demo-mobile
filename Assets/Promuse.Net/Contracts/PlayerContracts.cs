// Compiled by both sides - see the note at the top of ApiProblem.cs. C# 9 syntax
// is deliberate: Unity 6 compiles at that level.

#nullable enable

using System;
using System.Collections.Generic;

namespace Promuse.Contracts.Players
{
    /// <summary>
    /// Everything the Home screen binds to, in one response.
    /// </summary>
    /// <param name="ExpToNextLevel">
    /// Computed server-side from the level rather than recomputed by the client,
    /// so retuning the curve needs no app update and the two cannot disagree.
    /// </param>
    /// <param name="StateVersion">
    /// Increments on every accepted write, and what the ETag encodes. In the body
    /// as well as the header so a conflict can be explained to the player rather
    /// than only handled by the transport.
    /// </param>
    /// <param name="Tag">
    /// The four digits after the name, 0 to 9999, drawn as <c>#0420</c>. Names repeat; a name
    /// and its tag never do.
    /// </param>
    /// <param name="Signature">The line on the player's profile card, or null.</param>
    public sealed record PlayerState(
        Guid PlayerId,
        string DisplayName,
        int Tag,
        int Level,
        int Exp,
        int ExpToNextLevel,
        Stamina Stamina,
        IReadOnlyList<CharacterState> Characters,
        IReadOnlyList<string?> Squad,
        string? DesktopCharacterId,
        string? Signature,
        IReadOnlyList<ItemStack> Inventory,
        int StateVersion,
        DateTimeOffset ServerTime);

    /// <summary>
    /// <c>PlayerData.reason</c> (理智).
    /// </summary>
    /// <param name="Current">
    /// Computed at read time from the stored value and the moment it was true,
    /// never stored as a number that ticks. The only thing that knows time has
    /// passed on a phone is the phone.
    /// </param>
    /// <param name="NextPointAt">
    /// When the next point lands, or null at full. 客户端自己倒数 / This and
    /// <paramref name="FullAt"/> exist so the client renders a countdown without
    /// asking again - which is also why a 304 on this resource is harmless.
    /// </param>
    public sealed record Stamina(
        int Current,
        int Max,
        DateTimeOffset? NextPointAt,
        DateTimeOffset? FullAt);

    /// <summary>Mirrors <c>Data.Char.CharData</c>.</summary>
    public sealed record CharacterState(
        string CharacterId,
        int Elite,
        int Level,
        int Exp,
        int Trust);

    /// <summary>Mirrors <c>Data.Item.ItemStack</c>.</summary>
    public sealed record ItemStack(int ItemId, int Amount);

    /// <param name="Squad">
    /// Exactly four entries, null for an empty slot - the shape
    /// <c>PlayerData.GetSquad()</c> normalises to. Fixed length rather than a list
    /// of the operators chosen, because the slot position is meaningful.
    /// </param>
    public sealed record SquadRequest(IReadOnlyList<string?> Squad);

    /// <param name="CharacterId">Null clears it. Must be a character the player owns.</param>
    public sealed record DesktopCharacterRequest(string? CharacterId);

    /// <param name="Signature">
    /// The line on the player's profile card, at most 60 characters. Null or blank clears it;
    /// the server stores it as one line with no invisible characters, and answers with what it
    /// kept.
    /// </param>
    public sealed record SignatureRequest(string? Signature);

    /// <summary>
    /// 两边共用的上限 / Bounds both sides know, so the game's input stops where the server would
    /// refuse instead of finding out after the request.
    /// </summary>
    public static class PlayerProfileLimits
    {
        /// <summary>
        /// The signature's length, counted as a reader counts: an accented letter or an emoji is
        /// one. The game's input field counts UTF-16 units, which is stricter, never looser.
        /// </summary>
        public const int SignatureMaxLength = 60;
    }

    // -------------------------------------------------------------- profile

    /// <summary>
    /// 名片 / A player's profile card, as everyone who opens it sees it - its owner included.
    /// Everything in it is public: a field a stranger should not read does not belong here.
    /// </summary>
    /// <param name="AssistantId">
    /// The operator standing on the card: the desktop character when one is set, otherwise the
    /// first of the squad, otherwise the operator raised furthest. Null only for a player who
    /// owns nobody.
    /// </param>
    /// <param name="CampaignProgress">
    /// The code of the furthest main-campaign stage cleared, such as "1-4"; null before the first.
    /// </param>
    /// <param name="Operators">How many operators the player owns.</param>
    /// <param name="IsSelf">The card is the caller's own, so its signature may be edited.</param>
    public sealed record PlayerProfile(
        Guid PlayerId,
        string DisplayName,
        int Tag,
        int Level,
        string? Signature,
        string? AssistantId,
        DateTimeOffset RegisteredAt,
        string? CampaignProgress,
        int Operators,
        RhythmRecord Rhythm,
        bool IsSelf);

    /// <summary>The rhythm side of a profile, counted from results the server accepted.</summary>
    /// <param name="Plays">Runs finished, won or lost.</param>
    /// <param name="SongsCleared">Different charts cleared at least once.</param>
    /// <param name="FullCombos">Different charts cleared with a full combo.</param>
    /// <param name="BestScore">
    /// The highest ranked score on any chart - the player's best line on an all-time board - or
    /// null before the first.
    /// </param>
    public sealed record RhythmRecord(int Plays, int SongsCleared, int FullCombos, int? BestScore);
}
