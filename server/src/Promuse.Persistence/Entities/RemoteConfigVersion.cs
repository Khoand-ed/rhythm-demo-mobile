namespace Promuse.Persistence.Entities;

/// <summary>
/// One version of the remote config. The live config is the highest version.
///
/// 只增不改 / Append-only: an edit is a new row, never an UPDATE, so the table is its own audit
/// log - who changed what, when, and why - and going back is copying an old row forward as the
/// next version. Nothing is ever lost by changing it, including by a rollback.
///
/// 版本号做主键 / The version is the key, so two admins saving at once cannot both become
/// version 8: the second insert is refused by the primary key and answered with 412.
/// </summary>
public class RemoteConfigVersion
{
    public int Version { get; set; }

    /// <summary>The ConfigDocument as JSON (jsonb).</summary>
    public string Document { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null for the seeded first version.</summary>
    public Guid? CreatedBy { get; set; }

    public string? Note { get; set; }

    public int? RolledBackFrom { get; set; }
}
