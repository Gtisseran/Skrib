# Skrib

Skrib is a lightweight Windows text editor built with WinUI 3.

It is designed for quick note-taking and simple editing on Windows 10 and Windows 11. The app includes:

- plain text editing
- word wrap toggle
- theme selection (light, dark, system)
- language switch between French and English
- clean, minimal interface for everyday writing

Skrib is intended to be a fast and distraction-free writing tool for everyday use.

Get the latest release on the [Microsoft Store](https://apps.microsoft.com/detail/9p9tb8st018k) or from the [GitHub releases page](https://github.com/Gtisseran/Skrib/releases).

## Requirements

To build and run Skrib from source you need:

- Windows 10 version 1809 (build 17763) or later, or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the project targets `net8.0-windows10.0.19041.0`)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) 17.8 or later with the **.NET Desktop Development** workload (recommended), or any editor plus the .NET SDK for command-line builds
- [Git](https://git-scm.com/downloads)
- Windows **Developer Mode** enabled (Settings > System > For developers) so the MSIX package can be deployed for debugging
- The Windows App SDK (currently 2.5.1) is restored automatically from NuGet, no manual install needed

## Getting started

Clone the repository:

```powershell
git clone https://github.com/Gtisseran/Skrib.git
cd Skrib
```

Build the app (a platform must be specified, `AnyCPU` is not supported for packaging):

```powershell
dotnet build Skrib/Skrib.csproj -c Debug -p:Platform=x64
```

Supported platforms: `x64`, `x86`, `arm64` (see `RuntimeIdentifiers` in `Skrib/Skrib.csproj`). For a store-ready package, build in `Release`:

```powershell
dotnet build Skrib/Skrib.csproj -c Release -p:Platform=x64
```

To debug with UI, hot reload and the designer, open `Skrib/Skrib.csproj` in Visual Studio 2022 and press `F5`.

## Project layout

- `Skrib/MainWindow.xaml(.cs)` - editor, menus, settings page and status bar
- `Skrib/App.xaml(.cs)` - startup, single-instance handling and `.txt`/`.md` file activation
- `Skrib/Package.appxmanifest` - identity, file associations and Store metadata
- `Skrib/Assets` - icons, tiles and flags bundled with the package

## Versioning

The public version lives in three places that must stay in sync: `Version` and `PackageVersion` in `Skrib/Skrib.csproj`, and `Version` in `Skrib/Package.appxmanifest`. Current version: `1.0.2.0`.

Signing uses a local-only developer certificate (`Skrib_TemporaryKey.pfx`), which is intentionally not committed. Store builds are signed by the Store pipeline.