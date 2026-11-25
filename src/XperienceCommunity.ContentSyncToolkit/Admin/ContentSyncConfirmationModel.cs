using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// The Sync actions' confirmation form: when the target restores what's sent, as in Xperience's own
/// Sync dialog. See docs/specs/sync-actions.md, Confirmation.
/// </summary>
[CommandConfirmationModel]
public sealed class ContentSyncConfirmationModel
{
    internal const string Now = "now";
    internal const string Later = "later";

    [RadioGroupComponent(Label = "Restore on the target", Inline = false, Order = 1,
        Options = Now + ";Immediately\r\n" + Later + ";At a scheduled time")]
    public string When { get; set; } = Now;

    [DateTimeInputComponent(Label = "Restore at", Order = 2)]
    [VisibleIfEqualTo(nameof(When), Later)]
    public DateTime? RestoreAt { get; set; }

    /// <summary>When to restore; <see langword="null"/> for immediately.</summary>
    internal DateTime? ScheduledRestore => When == Later ? RestoreAt : null;
}
