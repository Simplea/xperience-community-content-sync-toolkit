using System.Reflection;

using CMS.ContentSynchronization.Internal;
using CMS.DataEngine;

namespace XperienceCommunity.ContentSyncToolkit.Sync;

/// <summary>One sync to start: the language versions to send, in one language, and who sends them.</summary>
/// <param name="Versions">The published or unpublished language versions (<c>cms.contentitemcommondata</c> objects), all in one language.</param>
/// <param name="VersionIds">Their <c>ContentItemCommonDataID</c>s.</param>
/// <param name="LanguageId">Their language.</param>
/// <param name="UserName">Who starts it, as Kentico records it in the list of content synchronizations.</param>
/// <param name="UserId">Who starts it.</param>
/// <param name="ScheduledRestore">When the target restores it; <see langword="null"/> for as soon as it can.</param>
internal sealed record ContentSyncEngineRequest(
    IReadOnlyList<BaseInfo> Versions,
    IReadOnlyList<int> VersionIds,
    int LanguageId,
    string UserName,
    int UserId,
    DateTime? ScheduledRestore)
{
    /// <summary>
    /// Send only these versions, without adding their other dependencies (Xperience's Detailed selection,
    /// from <c>31.x</c>). The versions then include the dependencies the editor chose.
    /// </summary>
    public bool WithSelectedDependencies { get; init; }

    /// <summary>
    /// Send the forms the pages show in Page Builder with them (Xperience's Include forms, from
    /// <c>30.9.0</c>). See docs/specs/include-forms.md.
    /// </summary>
    public bool IncludeLinkedForms { get; init; }
}

/// <summary>Where a sync is, in the toolkit's terms; Kentico's status names differ by version.</summary>
internal enum ContentSyncProgressState
{
    Unknown,
    Starting,
    WaitingForTarget,
    Transferring,
    Restoring,
    Completed,
    Failed,
    TimedOut,
}

/// <summary>A sync's progress, as Kentico reports it.</summary>
/// <param name="State">Where it is.</param>
/// <param name="Title">Kentico's short text, for example "Waiting for restoration".</param>
/// <param name="Description">Kentico's longer text, if any.</param>
/// <param name="Errors">Kentico's errors, as "title: message", or the message alone when it has no title.</param>
internal sealed record ContentSyncProgress(ContentSyncProgressState State, string? Title, string? Description, IReadOnlyList<string> Errors)
{
    public bool IsFinished => State is ContentSyncProgressState.Completed or ContentSyncProgressState.Failed or ContentSyncProgressState.TimedOut;

    // Kentico also lists messages under errors while a sync is starting, so only the state says it failed.
    public bool Failed => State is ContentSyncProgressState.Failed or ContentSyncProgressState.TimedOut;
}

/// <summary>Starts Content Sync for language versions and reads its progress.</summary>
internal interface IContentSyncEngine
{
    /// <summary>Whether this Xperience version can send forms with pages (<c>30.9.0</c> and later).</summary>
    public bool SupportsLinkedForms { get; }

    public Task<Guid> StartAsync(ContentSyncEngineRequest request, CancellationToken cancellationToken);

    public Task<ContentSyncProgress> GetProgressAsync(Guid synchronizationGuid, CancellationToken cancellationToken);
}

/// <summary>
/// Content Sync through Xperience's <see cref="ISynchronizationService"/>, the engine behind its own Sync
/// dialogs. Its admin command managers are internal, so this public interface, in an unsupported
/// (<c>.Internal</c>) namespace, is the only way in. Its <c>Create</c> method changed between versions
/// (30.8.0 takes common data IDs; 31.x a retrieve function, the language, identifiers and more), so the
/// call is bound by parameter name at run time: one build works on every supported version, and an
/// unknown parameter fails with a clear message instead of a wrong sync. This is the only class that
/// touches the namespace. See docs/specs/sync-actions.md, The sync engine.
/// </summary>
internal sealed class ContentSyncEngine(ISynchronizationService synchronizationService) : IContentSyncEngine
{
    private static readonly MethodInfo createMethod = typeof(ISynchronizationService).GetMethod(nameof(ISynchronizationService.Create))
        ?? throw new InvalidOperationException("Xperience's ISynchronizationService has no Create method.");

    private const string IncludeLinkedFormsParameter = "includeLinkedForms";

    public bool SupportsLinkedForms { get; } = SupportsParameter(createMethod.GetParameters(), IncludeLinkedFormsParameter);

    internal static bool SupportsParameter(IReadOnlyList<ParameterInfo> parameters, string name) =>
        parameters.Any(parameter => parameter.Name == name);

    public async Task<Guid> StartAsync(ContentSyncEngineRequest request, CancellationToken cancellationToken)
    {
        var task = (Task)createMethod.Invoke(synchronizationService, CreateArguments(createMethod.GetParameters(), request, cancellationToken))!;
        await task;

        return (Guid)task.GetType().GetProperty(nameof(Task<Guid>.Result))!.GetValue(task)!;
    }

    public async Task<ContentSyncProgress> GetProgressAsync(Guid synchronizationGuid, CancellationToken cancellationToken)
    {
        var progress = await synchronizationService.GetProgress(synchronizationGuid, cancellationToken);

        return new ContentSyncProgress(
            ToState(progress.Status.ToString()),
            progress.Title,
            progress.Description,
            [.. (progress.Errors ?? []).Select(error => string.IsNullOrEmpty(error.Title) ? error.Message : $"{error.Title}: {error.Message}")]);
    }

    /// <summary>The arguments for whichever <c>Create</c> signature this Xperience version has.</summary>
    internal static object?[] CreateArguments(IReadOnlyList<ParameterInfo> parameters, ContentSyncEngineRequest request, CancellationToken cancellationToken)
    {
        var ids = request.VersionIds.ToList();
        if (request.WithSelectedDependencies && !SupportsParameter(parameters, "withSelectedDependencies"))
        {
            throw new NotSupportedException("This Xperience version can't sync with selected dependencies.");
        }

        if (request.IncludeLinkedForms && !SupportsParameter(parameters, IncludeLinkedFormsParameter))
        {
            throw new NotSupportedException("This Xperience version can't sync forms with pages.");
        }

        return [.. parameters.Select<ParameterInfo, object?>(parameter => parameter.Name switch
        {
            // 30.8.0
            "contentItemCommonDataIds" => ids,
            // 31.x
            "retrieveFunc" => (Func<IReadOnlyCollection<BaseInfo>>)(() => [.. request.Versions]),
            "languageId" => request.LanguageId,
            "cultureCode" => "en-US",
            IncludeLinkedFormsParameter => request.IncludeLinkedForms,
            "withSelectedDependencies" => request.WithSelectedDependencies,
            "synchronizationIdentifiers" => ids,
            // Both
            "userName" => request.UserName,
            "userId" => request.UserId,
            "scheduledRestore" => request.ScheduledRestore,
            "cancellationToken" => cancellationToken,
            _ => throw new NotSupportedException(
                $"Xperience's ISynchronizationService.Create has a parameter this toolkit version doesn't know: '{parameter.Name}'. "
                + "Update the toolkit, or turn Sync actions off (ContentSyncToolkit:SyncActions)."),
        })];
    }

    /// <summary>Kentico's status, by name: <c>PollingTimeout</c> only exists from 31.x.</summary>
    internal static ContentSyncProgressState ToState(string status) => status switch
    {
        "Created" => ContentSyncProgressState.Starting,
        "Ready" => ContentSyncProgressState.WaitingForTarget,
        "Validating" or "ContentDataUploading" or "AssetsUploading" => ContentSyncProgressState.Transferring,
        "Restoring" => ContentSyncProgressState.Restoring,
        "Completed" => ContentSyncProgressState.Completed,
        "Failed" => ContentSyncProgressState.Failed,
        "PollingTimeout" => ContentSyncProgressState.TimedOut,
        _ => ContentSyncProgressState.Unknown,
    };
}
