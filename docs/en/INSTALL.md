# Installation — Maid Installer ♡

[Português](../INSTALACAO.md) · **English**

Maid Installer comes in two packages. Both include .NET, so they open even on a freshly formatted Windows,
without installing anything first.

| Package | What for | Where your data lives |
|---|---|---|
| `MaidInstaller-<version>-portatil-win-x64.zip` | USB drive / Ventoy: runs directly, no installation | In the `MaidInstaller\` folder on the USB drive |
| `MaidInstaller-<version>-win-x64.zip` | Keep it installed on the PC | `%LocalAppData%\MaidInstaller` |

## Requirements

- Windows 10 version 1809 or newer, or Windows 11 (64-bit, x64).
- About 270 MB free for the app. The local AI is optional and needs another 470 MB.
- To install programs, **WinGet** (Microsoft's App Installer). It comes with Windows 11 and up-to-date Windows 10.
  Without it the app opens normally, tells you and shows how to get it (Post-Format Kit › WinGet).
- Internet only to download programs, check for updates and download the AI. Maid Assistant's knowledge base works offline.

## Verify the download (optional)

Next to the `.zip` files there is `SHA256SUMS.txt`. In PowerShell:

```powershell
Get-FileHash .\MaidInstaller-1.1.0-win-x64.zip -Algorithm SHA256
```

The result must match the one in the file (case does not matter).

## Portable version (USB drive / Ventoy)

1. Extract the `.zip`. It contains the `MaidInstaller\` folder.
2. Copy the whole `MaidInstaller\` folder to the USB drive. With Ventoy, it can sit at the root of the exFAT partition, next to the ISOs.
3. After formatting the PC, open `MaidInstaller\MaidInstaller.exe` straight from the USB drive.

The `MaidInstaller.portable` file next to the `.exe` turns portable mode on. Settings, lists, profiles, logs and the AI model
stay on the USB drive, and the drive letter can change from one PC to another. The PC only keeps the programs you choose to
install and WinGet's temporary files.

You can also create the copy from the app itself: **Settings › Create portable copy…**. That way your settings,
profiles and lists go along (and the AI model, if you tick the option).

> Windows may show the SmartScreen warning ("Windows protected your PC") because the app has no commercial digital
> signature. If the file came from the official release, click **More info › Run anyway**.

## Installed version

1. Extract the whole `.zip` to any folder (for example, Downloads).
2. Double-click **`Instalar.cmd`**.
3. The installer shows what it will do and asks for confirmation (in the Windows language):
   - copies the app to `%LocalAppData%\Programs\MaidInstaller`;
   - creates the **Maid Installer** shortcut in the Start menu and, if you want, on the desktop.

The installer **does not ask for administrator rights**, does not change the Windows registry and installs nothing besides Maid Installer itself.

### Update

Run `Instalar.cmd` from the new version. It only replaces the program files. Your data in `%LocalAppData%\MaidInstaller`
stays there. Close Maid Installer first.

### Uninstall

In PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\MaidInstaller\Desinstalar.ps1"
```

The uninstaller removes the program and the shortcuts. Your data is only deleted if you answer **Y** to that specific question.
The programs installed with Maid Installer **stay on the PC**.

## First run, language and default settings

On the first run the app reads `Defaults\settings.default.json` (in the program folder) and creates your `Config\settings.json`.
A new installation opens in **English** with the **Dark** theme and no user name.

To change the language, theme, name or favorites, use **Settings** in the app (language and theme apply when the app
reopens; only the app reopens, never the PC). To set them **before** the first run (for example, on a new USB drive),
edit `settings.default.json` in Notepad:

| Field | Values |
|---|---|
| `language` | `"en"` (English), `"pt-BR"` (Portuguese), `"auto"` (same as Windows) |
| `theme` | `"Dark"`, `"Light"`, `"BlackPink"` |
| `userName` | How Maid calls you; `""` = no name |

After that, `settings.json` and the changes made in the app are what count.

## Building from source

Requires the .NET SDK 10. In the project folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

The script runs the tests, publishes in Release and creates the two `.zip` files, `SHA256SUMS.txt` and `release.json` in `dist\release`.
