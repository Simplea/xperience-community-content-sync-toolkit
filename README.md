# Content Sync Toolkit for Xperience by Kentico

[![CI: Build and Test](https://github.com/Simplea/xperience-community-content-sync-toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/Simplea/xperience-community-content-sync-toolkit/actions/workflows/ci.yml)

## Description

Xperience Community Content Sync Toolkit is an open-source toolkit that helps
content editors work with Xperience by Kentico's Content Sync feature. Content
Sync lets editors push pages and content hub items to a target instance, but it
gives no visibility into what is still missing there. This toolkit adds that
visibility with a **Sync status** application (under **Content management**)
that compares this instance with its Content Sync target:

- **Pages** and **Content hub** tabs with one status per item: Incompatible,
  Unpublished, New, Changed, Moved, Reordered, Not published, Only on target, or In sync,
  with a multi-select status filter, search and a link to each item's editor.
- Explanations of what to sync, including pages moved or reordered without
  being republished.
- **Incompatible** items, which a sync would fail for because the target is missing
  a content type, language, channel or workspace this instance has, with what
  a developer needs to deploy.
- Kentico's own permissions: editors only see the channels, workspaces and
  pages they can see in Xperience.

It's configured entirely by Xperience's own Content Sync settings, so there's
nothing extra to set up. Inline indicators in Xperience's page tree and Content
hub are planned.

This is a **beta**: APIs and behavior may change before `1.0.0`.

The project is developed and maintained by Andres Villenas at SimpleA.

## Requirements

### Library Version Matrix

| Xperience Version | Library Version |
| ----------------- | --------------- |
| 30.8.0 or newer   | 1.0.0-beta.2    |

Runtime-verified on 30.8.0 and 31.9.1.

See the [compatibility policy](https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/docs/Compatibility.md) for the supported range.

### Dependencies

- [ASP.NET Core 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [Xperience by Kentico](https://docs.kentico.com)

## Package Installation

Add the package to the Xperience application on **both** instances, the
Content Sync source and target:

```powershell
dotnet add package XperienceCommunity.ContentSyncToolkit --prerelease
```

Then register it in `Program.cs`:

```csharp
using XperienceCommunity.ContentSyncToolkit;
using XperienceCommunity.ContentSyncToolkit.Admin;

builder.Services.AddContentSyncToolkit();
// On the source instance: adds the Sync status application.
builder.Services.AddContentSyncToolkitAdmin();
```

Both instances must run the same Xperience version (a Content Sync
requirement) and the same toolkit version.

## Full Instructions

View the [Usage Guide](https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/docs/Usage-Guide.md) for setup, configuration, and
how to use the Sync status admin application.

## Contributing

Instructions for contributing to this project are available in
[CONTRIBUTING.md](https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/CONTRIBUTING.md).

Maintainers can find the versioning and publishing procedure in the
[Release Process](https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/docs/Release-Process.md).

## License

Distributed under the MIT License. See [`LICENSE.md`](https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/LICENSE.md) and
[`THIRD-PARTY-NOTICES.md`](https://github.com/Simplea/xperience-community-content-sync-toolkit/blob/main/THIRD-PARTY-NOTICES.md) for more information.

## Support

This project is maintained by Andres Villenas and SimpleA. It is not an official
Kentico product and is not covered by Kentico product support.

Report problems through the repository's [GitHub issues](https://github.com/Simplea/xperience-community-content-sync-toolkit/issues).
