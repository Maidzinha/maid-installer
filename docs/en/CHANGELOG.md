# Version history — Maid Installer

[Português](../../CHANGELOG.md) · **English**

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow `MAJOR.MINOR.PATCH`.

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
