# DarkTunnel Client

[![GitHub Release](https://img.shields.io/github/v/release/darkblue-tech/tunnel-app?style=flat-square)](https://github.com/darkblue-tech/tunnel-app/releases)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg?style=flat-square)](#supported-platforms-and-distributions)

Cross-platform desktop application for managing secure reverse tunnels via the DarkTunnel platform. Built with .NET 10 and Avalonia UI.

## Features

- Transport protocols: gRPC (HTTP/2), WebSocket, QUIC, and WebRTC DataChannel.
- Deep linking support via `darktunnel://` URI scheme for web-based authorization and tunnel configuration import across Windows, macOS, and Linux.
- Platform-native secure credential storage:
  - Windows: Data Protection API (DPAPI)
  - macOS: Apple Keychain Services
  - Linux: Secret Service API via D-Bus
  - Encrypted fallback storage for environments without a running keyring service
- Real-time bandwidth and network latency monitoring in the graphical interface.
- System tray background operation, autostart management, and built-in update verification.
- Single-instance enforcement with argument forwarding via local IPC.

## Repository Structure

- `Client.Desktop/` — Avalonia UI desktop application (MVVM, LiveCharts, system tray integration, protocol scheme handler).
- `Client.Core/` — Core services and transport logic: tunnel lifecycle engine (`TunnelEngine`), control channel clients, cryptographic secret storage, auto-updater, and OIDC authentication.
- `Client.Desktop.Tests/` — Unit and integration test suite.
- `installer/` — Standalone NSIS installer configuration for Windows.

## Supported Platforms and Distributions

Release binaries are built automatically via CI/CD:

| Platform | Architecture | Distribution Formats |
| --- | --- | --- |
| Windows 10/11 | x64, ARM64 | NSIS Installer (`.exe`), Portable archive (`.zip`) |
| macOS 12+ | Apple Silicon (arm64), Intel (x64) | Apple Disk Image (`.dmg`), Portable archive (`.tar.gz`) |
| Linux | x64, ARM64 | AppImage package (`.AppImage`), Portable archive (`.tar.gz`) |

All releases are self-contained and do not require a pre-installed .NET Runtime.

## Build Requirements

- .NET SDK 10.0 (or .NET SDK 8.0+)
- Git

Packaging requirements:
- Windows: NSIS 3.x (to generate `.exe` installer)
- Linux: `appimagetool` (to generate `.AppImage`)
- macOS: `hdiutil` (native utility to generate `.dmg`)

## Building and Running

Clone the repository:
```bash
git clone https://github.com/darkblue-tech/tunnel-app.git
cd tunnel-app
```

Build the solution:
```bash
dotnet build Client.Desktop/Client.Desktop.csproj
```

Run the application:
```bash
dotnet run --project Client.Desktop/Client.Desktop.csproj
```

Run tests:
```bash
dotnet test Client.Desktop.Tests/Client.Desktop.Tests.csproj
```

### Publishing Self-Contained Binaries

Linux (x64):
```bash
dotnet publish Client.Desktop/Client.Desktop.csproj -c Release -r linux-x64 --self-contained true -o out/linux-x64
```

Windows (x64):
```bash
dotnet publish Client.Desktop/Client.Desktop.csproj -c Release -r win-x64 --self-contained true -o out/win-x64
```

macOS (Apple Silicon, ARM64):
```bash
dotnet publish Client.Desktop/Client.Desktop.csproj -c Release -r osx-arm64 --self-contained true -o out/osx-arm64
```

macOS (Intel, x64):
```bash
dotnet publish Client.Desktop/Client.Desktop.csproj -c Release -r osx-x64 --self-contained true -o out/osx-x64
```

## Configuration

Application runtime data, cache, and local configurations are stored in the platform standard user directories:
- Windows: `%LOCALAPPDATA%\darkblue.tech\Tunnel`
- macOS: `~/Library/Application Support/darkblue.tech/Tunnel`
- Linux: `~/.local/share/darkblue.tech/Tunnel`

## License

This project is licensed under the Apache 2.0 License. See [LICENSE](LICENSE) for details.
