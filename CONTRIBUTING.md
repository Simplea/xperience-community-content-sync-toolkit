# Contributing

Thank you for helping improve Content Sync Toolkit for Xperience by Kentico.

## Requirements

- The .NET SDK version specified by `global.json`
- SQL Server 2019 or newer for the Dancing Goat integration host
- An Xperience by Kentico 31.2.1 database for runtime verification

See the [Contributing Setup guide](./docs/Contributing-Setup.md) for detailed local
environment and Dancing Goat database instructions.

Never commit database credentials, connection strings, exported content, or other
customer data. Keep local configuration in .NET user secrets or another ignored
configuration provider.

## Development workflow

### Branch strategy

Create one short-lived branch for each independent change. Use the existing
prefixes with an issue number and a short description, for example:

```text
feat/123-sync-status-admin-page
fix/124-content-tree-badge
refactor/125-diff-service
docs/126-release-guide
```

Multiple branches may be active at the same time. Keep unrelated features and
fixes on separate branches so they can be reviewed, tested, and merged
independently. If one change depends on another, branches may be temporarily
stacked; merge the base change first, then rebase or retarget the dependent
pull request onto `main`.

There is no permanent `develop` or release branch. Pull requests target
`main`, and several merged changes may be included in the same release. Create
release tags only from a tested, up-to-date `main`; see the [release process](./docs/Release-Process.md)
for versioning and publishing steps.

1. Create a branch using an appropriate prefix such as `feat/`, `fix/`, or
   `refactor/`.
2. Make focused changes and add or update automated tests.
3. From the repository root, run:

   ```powershell
   dotnet format XperienceCommunity.ContentSyncToolkit.slnx --exclude ./examples/** --verify-no-changes
   dotnet build XperienceCommunity.ContentSyncToolkit.slnx --configuration Release
   dotnet test XperienceCommunity.ContentSyncToolkit.slnx --configuration Release --no-build
   ```

4. Use a clear commit message, preferably following
   [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/).
5. Open a pull request with a description of the change, verification performed,
   and screenshots or video for administration UI changes.

Pull requests must pass CI and have all review comments resolved before merge.

## Line endings

Git normalizes repository text files to LF, and `.gitattributes` explicitly checks
out C# files with LF so the documented `dotnet format` command behaves consistently
on Windows and Linux. Contributors do not need to change their global Git line-ending
configuration.

After pulling a change to `.gitattributes`, refresh a clean working tree before
running the formatter. Commit or stash local work first, then run:

```powershell
git checkout-index --force --all
```

## Reporting security issues

Do not report suspected vulnerabilities in a public issue. Follow the private
reporting instructions in [SECURITY.md](./SECURITY.md).
