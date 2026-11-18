# Quick Search

A lightweight Windows desktop search bar that stays in the system tray. Press **Ctrl+Alt+Space** to open it.

## Download

[Download QuickSearch.exe](https://github.com/pingpongpixel/quick-search/releases/latest/download/QuickSearch.exe) · [View all releases](https://github.com/pingpongpixel/quick-search/releases)

## Run

1. Download QuickSearch.exe from the link above.
2. Open the executable. Quick Search runs from the system tray.
3. Press **Ctrl+Alt+Space** to open the search bar.

The app stores settings in your user profile and can optionally start with Windows through its settings.

## Build from source

Requirements: Windows and the .NET Framework 4.x C# compiler (csc.exe).

Run build.ps1 in PowerShell. The executable is written to bin\QuickSearch.exe.

## Source files

- Program.cs — application source
- build.ps1 — build script
- app.manifest — Windows application manifest
- search.ico — application icon
