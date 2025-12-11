# Emby Server DLL Files

This directory should contain the following DLL files from your Emby Server installation:

- `MediaBrowser.Common.dll`
- `MediaBrowser.Controller.dll`
- `MediaBrowser.Model.dll`

## How to Get These Files

### Option 1: Copy from Emby Server Installation

**Windows:**
```
Copy from: C:\Program Files\Emby-Server\system\
Copy to: Emby.Plugin.Danmu\lib\emby\
```

**Linux:**
```bash
cp /opt/emby-server/system/MediaBrowser.*.dll Emby.Plugin.Danmu/lib/emby/
```

**macOS:**
```bash
cp /Applications/EmbyServer.app/Contents/MacOS/system/MediaBrowser.*.dll Emby.Plugin.Danmu/lib/emby/
```

### Option 2: Download from Emby Server

If you have Emby Server running, you can download these files from:
- `http://your-emby-server:8096/web/index.html` (check the system folder)

## Note

These DLL files are required for building the plugin but should NOT be committed to Git.
They are already added to `.gitignore`.

