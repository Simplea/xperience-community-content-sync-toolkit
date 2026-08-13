# Content Sync Toolkit for Xperience by Kentico

[![CI: Build and Test](https://github.com/Simplea/xperience-community-content-sync-toolkit/actions/workflows/ci.yml/badge.svg)](https://github.com/Simplea/xperience-community-content-sync-toolkit/actions/workflows/ci.yml)

## Description

Xperience Community Content Sync Toolkit is an open-source toolkit that helps
content editors work with Xperience by Kentico's content sync feature. Content
sync lets editors push pages and content hub items to a target instance, but it
gives no visibility into what is still missing there. This toolkit adds that
visibility, starting with:

- An administration page for exploring the page and content hub trees and
  identifying which items are missing from the sync target.
- In-context indicators in the existing content tree and content hub UIs that
  flag items missing from the target.

The project is under initial development. No packages have been published yet;
APIs and behavior may change before the first release.

The project is developed and maintained by Andres Villenas at SimpleA.

## Requirements

### Library Version Matrix

| Xperience Version | Library Version |
| ----------------- | --------------- |
| 30.8.0 or newer   | Unreleased      |

See the [compatibility policy](./docs/Compatibility.md) for the supported range.

### Dependencies

- [ASP.NET Core 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [Xperience by Kentico](https://docs.kentico.com)

## Package Installation

Not yet published. Installation instructions will be added here once the first
package is released — see the [Release Process](./docs/Release-Process.md).

## Full Instructions

View the [Usage Guide](./docs/Usage-Guide.md) for detailed instructions as
features become available.

## Contributing

Instructions for contributing to this project are available in
[CONTRIBUTING.md](./CONTRIBUTING.md).

Maintainers can find the versioning and publishing procedure in the
[Release Process](./docs/Release-Process.md).

## License

Distributed under the MIT License. See [`LICENSE.md`](./LICENSE.md) and
[`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md) for more information.

## Support

This project is maintained by Andres Villenas and SimpleA. It is not an official
Kentico product and is not covered by Kentico product support.

Report problems through the repository's [GitHub issues](https://github.com/Simplea/xperience-community-content-sync-toolkit/issues).
