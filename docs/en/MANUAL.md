# User manual — Maid Installer ♡

[Português](../MANUAL.md) · **English**

*Get your PC ready with care.*

Maid Installer helps you get a PC ready after formatting. With it you choose the programs, install everything from
official sources (WinGet), check the system and ask Maid Assistant your questions.

**Rules the app always follows:**
- Before installing or updating anything, you see the exact list to confirm.
- It never updates programs without your permission and never restarts the PC.
- It does not disable your antivirus and does not store passwords.

---

## 1. Home

- **System Status**: Windows, processor, graphics card, memory and the real usage of each. The temperature appears when the PC allows it.
  When it can't be read, "n/a" appears.
- **Shortcuts**: open the main screens.
- **Quick Access**: your favorite programs. Click to open. The ones not installed yet appear faded.
  To add or remove a favorite, use **Add to / Remove from Quick Access** in the Library.
- **Maid Assistant**: the chat panel (see section 9).

## 2. App Library

61 verified programs, split into 8 categories. Each card shows the description, developer, price and license,
the official link and whether the program is already installed.

- **Search**: by name, purpose or nickname, without worrying about accents.
- **Filters**: category, Installed/Not installed, Free only and WinGet only.
- **Install** one program, or tick several and use **Install selected**.
- **Save as list**: turns the selected ones into a list in *My Lists*.
- **Check current version**: asks WinGet which version is the newest right now.
- Three programs have no WinGet package: Riot Client, NVIDIA App and AMD Adrenalin. For them, the app only shows the official website.

### How installation works

1. A **confirmation** shows what will be installed. Those already installed or without a package are listed separately.
2. The queue installs **one program at a time** and shows the real step (downloading, installing).
3. Windows may ask for administrator permission (UAC). If you decline, that program is not installed and the others continue.
4. In **Installations** you follow the queue, cancel what hasn't started, try again and open each program's **log**.
5. If something fails, the message explains why (no internet, full disk, file in use…).

If Maid Installer isn't in front when the queue finishes, a Windows notification shows how many programs are ready and
how many failed. Clicking it opens the Installations screen.

The app never updates an installed program during an installation. That is what the Updates screen is for.

## 3. Installation Profiles

Profiles are ready-made sets of programs: **Gaming PC**, **Work PC** and **Complete PC**. You can also create your own.

- **Install all**: goes through the same confirmation and the same queue. Those already installed are skipped.
- You can edit the name, description and programs. Everything is saved right away.
- **Duplicate** and **Delete** are also available. Delete asks for confirmation and only removes the profile, never the programs.
- **Restore ready-made profiles** recreates the ones you deleted.

## 4. My Lists

They work like profiles, but they are your personal lists.

- **Export** creates a `.maidlist.json` file, handy to keep or take to another PC.
- **Import** only accepts programs that exist in the verified catalog. Anything not recognized is listed and left out.

## 5. Post-Format Kit

10 independent cards. Each one has a **check**, which only reads the system, and an optional **action**, which always asks for
confirmation and shows what will change:

| Card | Check | Action |
|---|---|---|
| Windows Update | Latest installed update | Search for updates (search only) or open Windows Update |
| WinGet | Installed version | Open App Installer in the Microsoft Store |
| Components | Visual C++ and DirectX | Install the missing ones |
| Runtimes | .NET 8/10 and Java | Install the missing ones |
| Drivers | Devices with errors and the graphics driver | Open Device Manager or the optional drivers |
| Temporary files | Size of `%TEMP%` | Clean only your user's old files |
| Power | Active plan | Activate a plan that already exists |
| Startup | Programs that start with Windows | Read only |
| Extensions | Whether file extensions are shown | Show or hide |
| Tools | — | Open Windows tools |

## 6. Updates

- The screen lists what WinGet found, with the installed and available versions.
- **Update** one program, or tick several and use **Update selected**. The confirmation with the versions always appears.
- **Check for updates** queries again.
- The **history** keeps every installation and update made, with date, versions and result.

## 7. Settings

- **You**: the name Maid calls you. It is optional: leave it empty and she talks to you without using a name.
- **Portable mode and storage**: where your data is and how much space is free.
  - **Create portable copy…** copies the app and your data to a USB drive (see [INSTALL.md](INSTALL.md)).
  - **Open data folder**.
- **Maid Assistant (local AI)**:
  - **Download local AI**: 469 MB, from Qwen's official repository on Hugging Face. The app checks the file's SHA-256.
  - **Use the local AI**: turns it on or off.
  - **Remove model**, **Open Models folder** and **Scan again**.
- **Notifications and help**:
  - **Notify me through Windows when the installation queue finishes** (on by default).
  - **Show the welcome tour**: shows the first-launch balloons again.
- **Language and appearance**:
  - **Language**: English, Português (Brasil) or Automatic (follows the Windows language).
  - **Theme**: Dark (default), Light or Black and pink.
  - **Apply and reopen the app** saves the choice and reopens Maid Installer with it. Only the app reopens: the PC is never restarted.
    It waits if an installation, download or copy is in progress.

## 8. About

Shows the version, the usage mode (installed or portable), the real folders and buttons to open the data and the logs.

## 9. Maid Assistant

Ask in your own words, in English or Portuguese. Some examples:

- "What is 7-Zip for?" · "Is Discord free?" · "Who makes OBS?"
- "What's the difference between VLC and Spotify?" · "Which browser should I choose?"
- "Where do I download Steam?" · "What is error 0x8A150061?" · "How is the installation going?"

She only answers with information from the app's knowledge base. When she doesn't know, she says it isn't on record yet.
**She never installs, removes or runs anything.**

With the local AI, the explanations sound more natural, but still use only the facts from the base.
If the AI writes something that isn't in the facts, that part is discarded and Maid uses the knowledge base answer.
Hover over a message to see where it came from.

## 10. Where things are

| What | Installed | Portable |
|---|---|---|
| Settings | `%LocalAppData%\MaidInstaller\Config` | `MaidInstaller\Config` |
| Profiles / Lists | `…\Profiles`, `…\Lists` | `MaidInstaller\Profiles`, `MaidInstaller\Lists` |
| Logs (including one log per installation) | `…\Logs` | `MaidInstaller\Logs` |
| AI model | `…\Models` | `MaidInstaller\Models` |
| History | `…\Data\historico.json` | `MaidInstaller\Data\historico.json` |

The internal log files are written in Portuguese.

## 11. Common problems

- **"WinGet not found"**: install *App Installer* from the Microsoft Store (Post-Format Kit › WinGet).
- **An installation failed**: read the message on the Installations screen and **View log**. Then use **Try again**.
- **The installer asked for a restart**: restart whenever it suits you. The app never restarts on its own.
- **The AI doesn't show as "Online"**: check in Settings that the model was downloaded and that **Use the local AI** is ticked.
  Without the AI, Maid keeps answering from the knowledge base.
- **USB drive low on space**: the app warns you on startup when there is less than 100 MB free.
