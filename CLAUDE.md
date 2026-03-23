# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DevToy is a Windows developer utility application. It started as a notification popup for Claude Code and is expanding into a general-purpose developer tool. Currently it displays rich notifications (task completions, errors, pending questions) with themed UI, markdown rendering, and response history. It integrates with Claude Code via hooks and named-pipe IPC.

## Build & Run Commands

```bash
# Build
dotnet build src/DevToy.sln

# Build Release
dotnet build -c Release src/DevToy.sln

# Run setup form (no arguments)
dotnet run --project src/DevToy.Win

# Run popup with notification
dotnet run --project src/DevToy.Win -- --title "Title" --message "Message" --type "info"

# Publish single-file executable
dotnet publish -c Release src/DevToy.Win
```

There are no tests or linting configured.

## Architecture

**Single solution** (`src/DevToy.sln`) with one WinForms project (`src/DevToy.Win/`) targeting .NET 8.0 (net8.0-windows). Only external dependency is `Microsoft.Web.WebView2` for rendering HTML content. All classes share the `DevToy` namespace.

### Folder Structure

```
src/DevToy.Win/
  Program.cs                 Entry point, CLI arg parsing, single-instance mutex, pipe client
  Core/
    AppVersion.cs            Central version constant
    AppSettings.cs           Immutable record + JSON persistence (_data/settings.json)
    NativeMethods.cs         Shared P/Invoke (SetForegroundWindow, ShowWindow)
    NotificationType.cs      String constants: Info, Success, Error, Pending
    UpdateChecker.cs         Hourly timer that checks network path for new versions
    UpdateMetadata.cs        Record for deserializing metadata.json from update location
    Updater.cs               Downloads new exe, writes batch updater script, relaunches
  Popup/
    PopupAppContext.cs        Tray icon, context menu, pipe server loop, starts UpdateChecker
    PopupForm.cs             Main notification window (WebView2, animation, history nav, update UI)
    Sparkle.cs               Particle animation data class
  Settings/
    SettingsForm.cs          Settings dialog (theme picker, history, snooze, update location)
  Setup/
    SetupForm.cs             Installation wizard (copies exe, writes hook script, merges settings)
    SetupForm.resx           Designer resources
  Rendering/
    MarkdownRenderer.cs      Markdown-to-HTML converter with theme-aware CSS
    FunnyQuotes.cs           Programmer humor quotes for animated header
  Theme/
    PopupTheme.cs            PopupTheme record + Themes static class (7 built-in themes)
  Controls/
    RoundedButton.cs         Custom Button with rounded corners via GDI+
  Data/
    ResponseHistory.cs       Daily JSON history files in _data/history/, cached index

publish.ps1                  Automates version bump, publish, metadata generation, network deploy
```

### Execution Flow

1. **First instance with no args from non-install dir** → opens `SetupForm` (installation wizard)
1. **First instance with no args from install dir** → opens popup with last history entry (or welcome)
2. **First instance with args** (`--title`, `--message`, `--type`, `--message-file`, `--save-question`) → creates `PopupAppContext` with tray icon, starts pipe server, shows `PopupForm`
3. **Subsequent instances** → detect mutex, send args via named pipe to the running instance, then exit

### Conventions

- Notification type strings are centralized in `NotificationType` constants — use them instead of raw string literals.
- P/Invoke declarations live in `NativeMethods` — do not duplicate in individual forms.
- `AppSettingsData` is an immutable record — use `with` expressions to create modified copies.
- HTML encoding uses `System.Net.WebUtility.HtmlEncode` — do not create custom implementations.
- Empty `catch` blocks should include `Debug.WriteLine` for diagnostics.
- Files are organized by feature folder, not by type. New files go in the folder matching their feature area.
