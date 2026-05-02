# Publishing ClaySharp

ClaySharp publishes as two NuGet packages with the same version:

- `Nipah.ClaySharp`
- `Nipah.ClaySharp.Raylib`

The sample runner and test project are marked non-packable.

## Before Publishing

1. Update `<Version>` in `Directory.Build.props`.
2. Make sure `README.md` still reflects the package surface.
3. Run the release checks locally:

```sh
dotnet restore ClaySharp.slnx
dotnet build ClaySharp.slnx -c Release --no-restore
dotnet test ClaySharp.Tests/ClaySharp.Tests.csproj -c Release --no-build
dotnet pack ClaySharp/ClaySharp.csproj -c Release --no-build -o artifacts/packages
dotnet pack ClaySharp.Raylib/ClaySharp.Raylib.csproj -c Release --no-build -o artifacts/packages
```

## GitHub Actions Release

Create a NuGet API key at https://www.nuget.org/account/apikeys and add it to the repository as an Actions secret named `NUGET_API_KEY`.

Then run the `Publish NuGet` workflow manually from GitHub Actions. The workflow reads the version from `Directory.Build.props`, builds, tests, packs both packages, publishes them to NuGet, creates a `v<version>` git tag, and creates a GitHub Release with the package artifacts attached.

The workflow refuses to run if the release tag already exists, so a version is published at most once.

## Manual Publish

If you do not want to use GitHub Actions, publish the generated packages manually:

```sh
dotnet nuget push artifacts/packages/Nipah.ClaySharp.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/packages/Nipah.ClaySharp.Raylib.<version>.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
```

Push symbols as well when `.snupkg` files are present:

```sh
dotnet nuget push artifacts/packages/Nipah.ClaySharp.<version>.snupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/packages/Nipah.ClaySharp.Raylib.<version>.snupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
```
