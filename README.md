# NUPM

Simple custom package manager for Unity projects.  
Works like NuGet or UPM but for your own curated libraries, with dependency management and update checks.

## Installation

Via Git URL in Unity Package Manager:

```
https://github.com/boobosua/unity-nupm.git
```

## Features

- 📦 Install / Update / Uninstall custom packages via Git URL
- 🔄 Auto-sync with Unity’s UPM (removing from UPM also updates NUPM)
- ⚡ Dependency resolution with confirmation dialogs
- 🔍 “Browse” and “Installed” tabs (NuGet style UI)
- 🎨 Clean UI with update badges, search, and improved readability
- 🔒 Internal registry ensures only your packages show up

## Usage

### 1. Open NUPM Window

In Unity Editor, go to:

```
Window → NUPM
```

You’ll see two tabs:

- **Browse** – Lists all packages from your internal registry
- **Installed** – Shows only your custom packages currently in the project

### 2. Install a Package

Click **Install** on any package in the Browse tab.

- If the package has dependencies, you’ll be asked to install them first.
- Dependencies install in the correct order automatically.

### 3. Update a Package

If an update is available, an **Update available** badge is shown.  
Click **Update** to install the latest version from Git.

### 4. Uninstall a Package

In the **Installed** tab, click **Uninstall** to remove a package.  
This syncs with UPM – if removed directly from UPM, NUPM updates too.

## Example Registry

You configure your package registry in code (not exposed to end users):

```csharp
// InternalRegistry.cs (example)
internal static class InternalRegistry
{
    public static readonly PackageInfo[] Packages = new[]
    {
        new PackageInfo
        {
            name = "com.neko.flow",
            displayName = "NekoFlow",
            version = "1.0.0",
            url = "https://github.com/boobosua/unity-neko-flow.git",
            description = "Simple finite state machine for Unity with fluent API",
            dependencies = new List<string>()
        },
        new PackageInfo
        {
            name = "com.neko.tween",
            displayName = "NekoTween",
            version = "1.0.0",
            url = "https://github.com/boobosua/unity-neko-tween.git",
            description = "Tweening library with clean API",
            dependencies = new List<string>{ "com.neko.flow" }
        }
    };
}
```

## UI Preview

```
┌──────────────────────────────────────────────┐
│ NekoTween (Update available)                 │
│ Name: com.neko.tween                         │
│ Version: 1.0.0                               │
│ A clean tweening library for Unity.          │
│ Dependencies: com.neko.flow                  │
│                                  [Update]    │
│                                  [Uninstall] │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│ NekoFlow                                     │
│ Name: com.neko.flow                          │
│ Version: 1.0.0                               │
│ A simple FSM library with fluent API.        │
│                                  [Install]   │
└──────────────────────────────────────────────┘
```

## API

### PackageInstaller

- `InstallPackageAsync(PackageInfo)` – Install or update via Git URL
- `UninstallPackageAsync(PackageInfo)` – Remove package from project

### DependencyResolver

- `ResolveDependencies(root, tryGetByName)` – Returns installation order including dependencies

### PackageRegistry

- `RefreshAsync()` – Refresh registry from internal definitions
- `TryGetByName(name, out PackageInfo)` – Lookup package by ID

### InstalledDatabase

- `SnapshotAsync()` – Get currently installed packages from UPM
