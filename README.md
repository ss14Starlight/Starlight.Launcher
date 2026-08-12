<p align="center">
  <img alt="Starlight Launcher Logo" width="600" src="starlight-logo.png" />
</p>

<h1 align="center">🚀 Starlight Launcher</h1>

<p align="center">
  A modern, cross-platform launcher for <b>Space Station 14</b>, built by the Starlight Team.
</p>

<p align="center">
  <a href="https://discord.gg/wXJmswM5yt"><img alt="Discord" src="https://img.shields.io/discord/1272545509562777621?label=Discord&logo=discord&logoColor=white"></a>
  <a href="https://github.com/ss14Starlight/Starlight.Launcher"><img alt="GitHub Stars" src="https://img.shields.io/github/stars/ss14Starlight/Starlight.Launcher?style=social"></a>
</p>

<p align="center">
  <img alt="Commit activity" src="https://img.shields.io/github/commit-activity/y/ss14Starlight/Starlight.Launcher">
  <img alt="Issues" src="https://img.shields.io/github/issues/ss14Starlight/Starlight.Launcher">
  <img alt="Pull requests" src="https://img.shields.io/github/issues-pr/ss14Starlight/Starlight.Launcher">
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-11.0-512BD4?logo=dotnet">
  <img alt="Avalonia" src="https://img.shields.io/badge/Shell-Avalonia-8B44AC?logo=avaloniaui&logoColor=white">
  <img alt="Blazor" src="https://img.shields.io/badge/UI-Blazor-5C2D91?logo=blazor">
  <img alt="MudBlazor" src="https://img.shields.io/badge/Components-MudBlazor-594AE2">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-0078D6">
  <img alt="macOS" src="https://img.shields.io/badge/macOS-000000?logo=apple&logoColor=white">
  <img alt="Linux" src="https://img.shields.io/badge/linux-000000?logo=linux&logoColor=white">
  <img alt="License" src="https://img.shields.io/badge/License-MIT-green">
</p>

---

## 📑 Table of Contents

- [About](#-about)
- [Features](#-features)
- [Built With](#-built-with)
- [Getting Started](#-getting-started)
- [License](#-license)

---

## ✨ About

**Starlight Launcher** is a modern, community-built alternative launcher for **Space Station 14**.

Created by the **Starlight Team**, it focuses on:

- 🎨 A modern, responsive UI that doesn't feel like a 2010 desktop app
- 🔧 Advanced customization - from launch behaviour down to build sources
- 🧩 An extensible architecture where new UI and services plug in cleanly

> [!NOTE]
> Starlight Launcher is an unofficial community project and is not affiliated with or endorsed by the official Space Station 14 developers.

---

## 🌟 Features

| | |
|---|---|
| 👥 **Multi-account support** | Manage and switch between several accounts without re-logging every time |
| 🎛️ **Rich launcher settings** | Fine-grained control over how the game launches, updates and caches content |
| 🎨 **Theme customization** | Personalize the look and feel of the launcher |
| 💬 **Discord integration** | Rich Presence statuses and OAuth login |
| 🌍 **Localization** | Fully localized UI with runtime language switching |
| 🛰️ **Configurable build CDNs** | Prioritized, mirror-aware engine sources with signature verification |
| 👥 **Multi-auth support** | You can use different auth servers! |
| 🔔 **Tray integration** | Collapse to tray on minimize or close, native to each platform |

---

## 🛠 Built With

- [.NET](https://dotnet.microsoft.com/) - core framework
- [Avalonia](https://avaloniaui.net/) - cross-platform native shell (window, tray, dialogs)
- [Blazor](https://learn.microsoft.com/aspnet/core/blazor/) - UI layer, hosted in-process
- [MudBlazor](https://mudblazor.com/) - Material-inspired Blazor component library
- [Serilog](https://serilog.net/) - structured logging across launcher and client
- Space Station 14 Launcher API - server, hub and auth integration

---

## 🚀 Getting Started

### Requirements

- **.NET SDK 11.0** _(currently in preview - final release expected November 2026)_
- **Visual Studio 2022–2026**, **JetBrains Rider**, or **VS Code** with the C# Dev Kit

### Build & run

```bash
git clone https://github.com/ss14Starlight/Starlight.Launcher.git
cd Starlight.Launcher

dotnet restore
dotnet run --project Starlight.Launcher
```

---

## 📄 License

- **Code** - licensed under the [MIT License](LICENSE).
- **Non-code assets** (icons, sound files, etc.) - licensed under **CC BY-SA 3.0** unless stated otherwise.
