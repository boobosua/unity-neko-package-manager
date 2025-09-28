# NUPM

Simple custom package manager for Unity projects.  
Works like NuGet or UPM but for your own curated libraries, with dependency management and update checks.

## Installation

Via Git URL in Unity Package Manager:

```
https://github.com/boobosua/unity-neko-package-manager.git
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
