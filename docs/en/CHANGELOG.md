# Version history — Maid Installer

[Português](../../CHANGELOG.md) · **English**

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow `MAJOR.MINOR.PATCH`.

## [1.2.0] — 2026-11-08

- **Library with 61 apps** (up from 39). The 22 new ones had their WinGet ID verified: Zoom, Microsoft Teams, Thunderbird, GOG Galaxy,
  Battle.net, MSI Afterburner, qBittorrent, Notepad++, Bitwarden, Malwarebytes, CPU-Z, HWiNFO, CrystalDiskInfo, Rufus, Ventoy,
  K-Lite Codec Pack, GIMP, Krita, HandBrake, Paint.NET, Windows Terminal and Notion. The icons come from the official websites.
- **Windows notification when the queue finishes**: if Maid Installer isn't in front, a notification shows how many programs are
  ready and how many failed. Clicking it opens the Installations screen. It can be turned off in
  **Settings › Notifications and help**. The PC is still never restarted on its own.
- **Welcome tour**: on first launch, five quick balloons introduce the Library, Profiles, the Post-Format Kit and Maid Assistant.
  You can skip it and see it again in **Settings › Notifications and help**.

## [1.1.1] — 2026-11-06

- **Light** theme: the mascot on the Home screen no longer has a dark frame; the edges of the image fade into the light background.
  The Dark and Black and pink themes are unchanged.

## [1.1.0] — 2026-11-05

- **English** across the whole app: screens, messages, errors, the catalog, Maid Assistant's knowledge base and the ready-made
  profiles. Portuguese (Brazil) stays available. New installations start in English.
- Language selector in **Settings › Language and appearance**: English, Português (Brasil) or Automatic (follows Windows).
- **Dark** (new default) and **Light** themes, besides the original **Black and pink**, which is unchanged.
- Changing language or theme reopens only the app, never the PC, and waits for installations, downloads and copies in progress.
- The user name is now optional and empty by default: without a name, Maid talks to you without using one.
- Maid Assistant understands questions in English.
- Bilingual installer and uninstaller (they follow the Windows language) and documentation in English.
- Test that fails if any interface text is missing its English translation.

## [1.0.0] — 2026-11-04

First complete version.

- Black and pink interface with the Maid mascot, System Status with real data and Quick Access.
- Library with 39 apps (verified WinGet IDs), search, filters, official icons and real installation status.
- Installation through WinGet with confirmation, a queue (one app at a time), cancel, retry, per-app logs and explained errors.
- Ready-made profiles (Gaming, Work and Complete PC) and custom ones, My Lists, and `.maidlist.json` import/export.
- Post-Format Kit with 10 real checks and optional actions, always with confirmation.
- Updates through `winget upgrade`, only with permission, plus the history.
- Maid Assistant with an offline knowledge base and optional local AI (Qwen2.5 0.5B, llama.cpp), limited to the facts in the base.
- Portable mode for USB drives / Ventoy and "Create portable copy".
- Installable version without administrator rights (`Instalar.cmd`), default settings in `Defaults\settings.default.json`,
  packages with SHA-256 and a `release.json` manifest.
- Private use license (proprietary, all rights reserved) and third-party component notices.
