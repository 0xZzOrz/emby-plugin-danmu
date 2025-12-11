# Emby Plugin Build Instructions

## Prerequisites

1. Install .NET 9.0 SDK
2. Install Emby Server (to get the required DLL files)

## Setting Up Emby Server DLL References

The Emby plugin requires references to Emby Server DLL files. You have several options:

### Option 1: Set Environment Variable (Recommended)

Set the `EMBY_SERVER_PATH` environment variable to point to your Emby Server installation:

**Windows:**
```cmd
set EMBY_SERVER_PATH=C:\Program Files\Emby-Server
```

**Linux:**
```bash
export EMBY_SERVER_PATH=/opt/emby-server
```

**macOS:**
```bash
export EMBY_SERVER_PATH=/Applications/EmbyServer.app/Contents/MacOS
```

### Option 2: Use MSBuild Property

Build with the `EmbyServerPath` property:

```bash
dotnet build /p:EmbyServerPath="C:\Program Files\Emby-Server"
```

### Option 3: Copy DLLs to Local Directory

1. Copy the following DLL files from your Emby Server installation to a local directory (e.g., `lib/emby/`):
   - `MediaBrowser.Common.dll`
   - `MediaBrowser.Controller.dll`
   - `MediaBrowser.Model.dll`

2. Update the project file to reference these local DLLs.

## Building the Plugin

Once the Emby Server path is configured:

```bash
dotnet restore
dotnet build
dotnet publish --configuration Release
```

## Emby Server Installation Paths

Default installation paths:

- **Windows**: `C:\Program Files\Emby-Server\system\`
- **Linux**: `/opt/emby-server/system/`
- **macOS**: `/Applications/EmbyServer.app/Contents/MacOS/system/`

## Troubleshooting

If you get errors about missing DLL files:

1. Verify Emby Server is installed
2. Check that the `system` folder exists in the Emby Server directory
3. Verify the DLL files (`MediaBrowser.Common.dll`, `MediaBrowser.Controller.dll`, `MediaBrowser.Model.dll`) exist in the `system` folder
4. Set the `EMBY_SERVER_PATH` environment variable or use the `/p:EmbyServerPath` build property

