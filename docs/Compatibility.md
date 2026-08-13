# Xperience compatibility

Content Sync Toolkit separates its minimum supported Xperience version from the
latest version used by the integration site.

## Supported range

- Minimum supported Xperience version: `30.8.0`.
- Latest build-verified Xperience version: `31.7.2`.

`30.8.0` is not an arbitrary floor: it is the first version where Xperience's
own Content Sync feature covers both content-hub items (added in `30.5.0`, the
May 2025 Refresh) and website channel pages (added in `30.8.0`, the July 2025
Refresh). Content Sync Toolkit exists to add visibility into that feature, so a
version where Content Sync can't sync both kinds of content is not a
meaningful target regardless of what the toolkit's own query APIs could
technically compile against.

The NuGet package declares only the minimum version of `Kentico.Xperience.Admin`.
Consuming applications remain responsible for keeping their Xperience packages
aligned on a single version.

The absence of a NuGet upper bound does not mean that every future Xperience
release is automatically supported. The latest verified value records the newest
release covered by the project's compatibility checks.

## Development model

The library and unit tests compile against the minimum supported version. This
prevents contributors from accidentally using an API that is unavailable to
supported consumers.

The Dancing Goat integration site uses the latest verified version. It consumes
the library project built against the minimum, which checks that the resulting
library can be referenced by a current Xperience application.

Before release, validate the packed NuGet artifact in separate applications using:

1. the minimum supported Xperience version; and
2. the latest verified Xperience version.

## Updating versions

- Retest and update the latest verified version after every Xperience refresh.
- Do not raise the minimum merely because a newer Xperience release is available.
- Raise the minimum only when required by an API, security fix, runtime lifecycle,
  or an intentional change to the support window.
- After a stable Content Sync Toolkit release, raising the minimum Xperience
  version is a breaking compatibility change and requires a new major toolkit
  version.
