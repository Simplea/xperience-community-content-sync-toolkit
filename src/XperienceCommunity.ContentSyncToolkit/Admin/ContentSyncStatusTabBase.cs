using CMS.ContentEngine;
using CMS.Core;
using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Microsoft.Extensions.Options;

using XperienceCommunity.ContentSyncToolkit.SyncStatus;

using LoadDataSettings = Kentico.Xperience.Admin.Base.LoadDataSettings;

namespace XperienceCommunity.ContentSyncToolkit.Admin;

/// <summary>
/// Shared implementation of the Pages and Content hub tabs. Subclasses supply only what differs:
/// their filter model, how to list scopes, and which status query to run. What to show is
/// decided by <see cref="ContentSyncStatusViewBuilder"/>; this class maps its result to
/// Xperience's listing template.
/// Subclasses must declare <c>[UIEvaluatePermission(SystemPermissions.VIEW)]</c> themselves.
/// </summary>
internal abstract class ContentSyncStatusTabBase(
    object filterModel,
    string nameColumnCaption,
    IOptions<ContentSyncToolkitOptions> options,
    IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
    ContentSyncStatusRefreshRequestStore refreshRequestStore,
    IPageLinkGenerator pageLinkGenerator)
    : ListingPageBase<ListingConfiguration, ListingTemplateClientProperties>
{
#pragma warning disable S1075 // A fixed link to the public documentation, not an environment-specific path.
    private const string UsageGuideUrl =
        "https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/docs/Usage-Guide.md";
#pragma warning restore S1075

    public override ListingConfiguration PageConfiguration { get; set; } = new()
    {
        FilterFormModel = filterModel,
        ColumnConfigurations =
        [
            SortableColumn(ContentSyncStatusListingSupport.NameColumn, nameColumnCaption, minWidth: 40, maxWidth: 100, searchable: true),
            SortableColumn(ContentSyncStatusListingSupport.ContentTypeColumn, "Content type", minWidth: 24, maxWidth: 40),
            SortableColumn(ContentSyncStatusListingSupport.StatusColumn, "Status", minWidth: 20, maxWidth: 28),
            SortableColumn(ContentSyncStatusListingSupport.LastPublishedColumn, "Last published", minWidth: 20, maxWidth: 28),
        ],
        PageSizes = [10, 25, 50],
        HeaderActions =
        [
            new ActionConfiguration { Type = ActionType.Command, Name = nameof(Refresh), Parameter = nameof(Refresh), Label = "Refresh" },
        ],
        TableActions = [],
        MassActions = [],
        Callouts = [],
    };

    /// <summary>Key isolating this tab's pending Refresh from the other tab's.</summary>
    protected abstract string TabKey { get; }

    /// <summary>The filter model property holding the selected channel/workspace name.</summary>
    protected abstract string ScopeFilterFieldName { get; }

    /// <summary>"channel" or "workspace", for messages.</summary>
    protected abstract string ScopeNoun { get; }

    protected abstract string NoScopesHeadline { get; }

    protected abstract string NoScopesGuidance { get; }

    protected abstract Task<IReadOnlyList<ContentSyncScope>> GetScopesAsync(CancellationToken cancellationToken);

    protected abstract Task<ContentSyncStatusResult> GetStatusAsync(
        string scopeName, string languageName, bool forceRefresh, CancellationToken cancellationToken);

    private bool IsSourceConfigured => options.Value.Source.TargetUrl is not null;

    // Banners belong to the page configuration, built before LoadData runs, so only states known
    // up front (configuration, available scopes) can be banners. See docs/specs/sync-status-admin-page.md.
    public override async Task ConfigurePage()
    {
        if (!IsSourceConfigured)
        {
            PageConfiguration.Callouts.Add(new CalloutConfiguration
            {
                Type = CalloutType.FriendlyWarning,
                Placement = CalloutPlacement.OnDesk,
                Headline = "Content sync status isn't configured on this instance",
                Content = "Set <code>ContentSyncToolkit:Source:TargetUrl</code> and "
                    + "<code>ContentSyncToolkit:Source:Secret</code> to compare this instance's content "
                    + $"with a target instance. See the <a href=\"{UsageGuideUrl}\" target=\"_blank\" rel=\"noopener noreferrer\">Usage Guide</a>.",
                ContentAsHtml = true,
            });
        }
        else if ((await GetScopesAsync(CancellationToken.None)).Count == 0)
        {
            PageConfiguration.Callouts.Add(new CalloutConfiguration
            {
                Type = CalloutType.QuickTip,
                Placement = CalloutPlacement.OnDesk,
                Headline = NoScopesHeadline,
                Content = NoScopesGuidance,
            });
        }

        await base.ConfigurePage();
    }

    // Header actions ignore a command's result, so the documented way to refresh the listing is
    // to navigate to its own path; that reload consumes the refresh flag set here.
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public Task<INavigateResponse> Refresh()
    {
        refreshRequestStore.RequestRefresh(TabKey);
        return Task.FromResult(NavigateTo(pageLinkGenerator.GetPath(GetType()), refetchAllTemplates: true));
    }

    protected override async Task<LoadDataResult> LoadData(LoadDataSettings settings, CancellationToken cancellationToken)
    {
        var request = new ContentSyncStatusViewRequest(
            IsSourceConfigured,
            ContentSyncStatusFilterValueExtractor.ExtractStringParameter(settings.FilterWhereCondition, ScopeFilterFieldName),
            refreshRequestStore.ConsumeRefreshRequest(TabKey),
            settings.SearchTerm,
            settings.SortBy,
            settings.SortType == SortTypeEnum.Desc,
            settings.PageSize,
            settings.SelectedPage);

        ContentSyncStatusView view;
        try
        {
            string languageName = await GetLanguageNameAsync(cancellationToken);
            view = await ContentSyncStatusViewBuilder.BuildAsync(
                request,
                GetScopesAsync,
                (scopeName, forceRefresh, ct) => GetStatusAsync(scopeName, languageName, forceRefresh, ct),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Details go to the event log only; the page shows a safe, generic message.
            EventLogService.LogException(nameof(ContentSyncStatusTabBase), "LOADDATA", ex);
            return MessageRow(
                "Sync status couldn't be loaded. Try Refresh; details are in the event log.",
                "Error",
                nameof(Color.AlertBackgroundHighEmphasis));
        }

        return view.Kind switch
        {
            ContentSyncStatusViewKind.Items => new LoadDataResult
            {
                TotalCount = view.TotalCount,
                Rows = [.. view.Items.Select(ToRow)],
            },
            ContentSyncStatusViewKind.TargetUnavailable => MessageRow(
                "The target instance couldn't be reached or rejected the request, so sync status can't be determined.",
                "Target unavailable",
                nameof(Color.AlertBackgroundHighEmphasis)),
            ContentSyncStatusViewKind.ScopeNotFound => MessageRow(
                $"The selected {ScopeNoun} no longer exists. Clear the filter or choose another {ScopeNoun}.",
                "Not available",
                nameof(Color.BackgroundTagGrey)),

            // Not configured and no scopes are explained by the banner; no items uses Xperience's
            // native empty state.
            ContentSyncStatusViewKind.NotConfigured or ContentSyncStatusViewKind.NoScopes or ContentSyncStatusViewKind.Empty or _ =>
                new LoadDataResult { TotalCount = 0, Rows = [] },
        };
    }

    // GetCurrentContentLanguage() on ListingPageBase is private, not protected, so the admin's
    // ambient content-language switcher state isn't reachable here — default to the first
    // configured content language instead of trying to mirror it.
    private async Task<string> GetLanguageNameAsync(CancellationToken cancellationToken)
    {
        var languages = await contentLanguageInfoProvider.Get().GetEnumerableTypedResultAsync(cancellationToken: cancellationToken);
        return languages.FirstOrDefault()?.ContentLanguageName ?? string.Empty;
    }

    private static ColumnConfiguration SortableColumn(string name, string caption, int minWidth, int maxWidth, bool searchable = false) =>
        new()
        {
            Name = name,
            Caption = caption,
            MinWidth = minWidth,
            MaxWidth = maxWidth,
            Searchable = searchable,
            Sorting = new SortingConfiguration { Sortable = true },
        };

    private static Row ToRow(ContentSyncStatusItem item) =>
        new()
        {
            Identifier = item.Guid,
            Cells =
            [
                new StringCell { Value = ContentSyncStatusListingSupport.DisplayName(item) },
                new StringCell { Value = ContentSyncStatusListingSupport.ContentTypeName(item) },
                TagCell(ContentSyncStatusListingSupport.StatusLabel(item.Status), ContentSyncStatusListingSupport.StatusColor(item.Status)),
                new StringCell { Value = ContentSyncStatusListingSupport.LastPublishedWhen(item)?.ToString("g") ?? string.Empty },
            ],
        };

    // A single row explaining a state that depends on the selected scope, with a status-style tag
    // so it reads differently from both real rows and Xperience's native empty state.
    private static LoadDataResult MessageRow(string message, string tagLabel, string colorName) =>
        new()
        {
            TotalCount = 1,
            Rows =
            [
                new Row
                {
                    Identifier = "message",
                    Cells =
                    [
                        new StringCell { Value = message },
                        new StringCell { Value = string.Empty },
                        TagCell(tagLabel, Enum.Parse<Color>(colorName)),
                        new StringCell { Value = string.Empty },
                    ],
                },
            ],
        };

    private static NamedComponentCell TagCell(string label, Color color) =>
        new()
        {
            Name = NamedComponentCellComponentNames.TAG_COMPONENT,
            ComponentProps = new TagTableCellComponentProps { Label = label, Color = color },
        };
}
