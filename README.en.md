# Kadr

A local Windows screen recorder: WinForms/WebView2 hosts an HTML interface using MediaRecorder. An optional PresentMon overlay displays game FPS.

[Русская документация](README.md)

## Requirements and launch

Windows 10 or newer, x64, and Microsoft Edge WebView2 Runtime. After building, run `build/installer/Kadr Setup.exe`. Installation requires administrator privileges and downloads WebView2 if needed. Alternatively run `build/screen-recorder/app/Kadr.exe` with its DLLs and `ui` folder intact.

## Recording

1. Choose Screen (up to 30 FPS) or Game (up to 60 FPS). Actual frame rates depend on the capture environment.
2. Choose source audio, microphone, video quality and size limit. Preferences are saved locally automatically.
3. Click Start and select a capture source. Available audio depends on the source and WebView2.
4. Pause or resume as needed. The timer excludes pauses. Stop to prepare the video.
5. Preview and download the result. Deleting an unsaved clip requires confirmation.

Video quality requests 3, 6 or 12 Mbps; audio and container overhead add to file size. Recording stops when its size limit is reached, with a possible final-chunk overshoot. The limit applies to each recording, not total application memory. Save and remove old clips regularly.

Recordings remain in memory and cannot be recovered after closing or crashing. The desktop app confirms a completed download. A standalone browser cannot confirm that the file was actually saved, so its unsaved-recording warning remains active.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+Alt+F9 | Start / stop recording |
| Ctrl+Alt+F10 | Pause / resume |
| Ctrl+Alt+F11 | Toggle FPS overlay for the active game window |

Global shortcuts require the desktop app. Switch to the game before enabling FPS; borderless windowed mode is recommended. The FPS worker may request administrator privileges. Shortcut conflicts are reported in the interface.

## Build

Requires Windows x64, Windows PowerShell 5.1 or PowerShell 7, curl.exe, the .NET Framework 4.x compiler, and Unicode NSIS. From the project root:

```powershell
./tools/build_kadr.ps1 -MakeNsis 'C:\Program Files (x86)\NSIS\makensis.exe'
```

The script downloads WebView2 SDK, its bootstrapper and PresentMon, validates pinned SHA-256 hashes and the bootstrapper signature, compiles C#, and produces the installer. Initial dependency downloads require internet access. `build/` is excluded from Git. Third-party licenses are copied into the application; a project license has not yet been specified.

## Source layout

- `screen-recorder/Desktop.cs`: window, WebView2, downloads and shortcuts.
- `screen-recorder/FpsOverlay.cs`: overlay, capture worker and FPS calculation.
- `screen-recorder/Screen Recorder.html`: UI, settings and recording.
- `screen-recorder/Kadr.nsi`: installation and removal.
- `tools/build_kadr.ps1`: build pipeline.

## Limitations and troubleshooting

Video is not uploaded to a server. Preferences and the WebView2 profile are stored locally. Live preview is disabled during full-monitor capture to avoid recursive screen mirrors. The format depends on runtime support, normally WebM with MP4 as a fallback. Unavailable microphones produce a warning and recording continues without them. Exclusive fullscreen games, DRM and some sources may prevent capture.

For launch failures, check WebView2 and the DLLs beside the executable. For missing audio, check the source picker permission. For missing FPS, check the active game window and Windows permission.

## Release checks

There are no full UI automation tests yet. Manually verify source-picker cancellation, microphone denial, mixed audio, pause/resume, stopping from the system capture UI, size limits, playback, cancelled and completed saves, deletion and closing with unsaved clips, preference persistence, shortcuts, FPS, and installation/removal on a clean Windows system.
