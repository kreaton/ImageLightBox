# ImageLightBox

[![Release](https://img.shields.io/github/v/release/kreaton/ImageLightBox)](https://github.com/kreaton/ImageLightBox/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![Windows](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET 10](https://img.shields.io/badge/.NET-10-lightgrey)
![WPF](https://img.shields.io/badge/UI-WPF-lightgrey)
[![GitHub stars](https://img.shields.io/github/stars/kreaton/ImageLightBox?style=flat&logo=github&color=yellow)](https://github.com/kreaton/ImageLightBox/stargazers)

⭐ If ImageLightBox earns a place on your machine, [star it on GitHub](https://github.com/kreaton/ImageLightBox) — it's free, made by one developer, and stars are how other people find it.

![ImageLightBox: clean up a pile of photos, one key at a time](images/banner.png)

**Clean up a pile of photos fast.**

Page through thumbnails of every file under a folder, and delete, move or copy as you go.

▶️ **[Watch the video on YouTube](https://youtu.be/ZQwSIcIjIHs)**

## Install

**[Download the installer](https://github.com/kreaton/ImageLightBox/releases/latest/download/ImageLightBox-win-Setup.exe)** and run it. If your PC doesn't have the .NET 10 Desktop Runtime yet, the installer adds it.

The installer isn't code-signed, so Windows may warn you about it. To continue, choose **More info → Run anyway**.

To get a newer version later, click the ⓘ button and choose **Check for updates**. To remove the app, use **Settings → Apps → Installed apps** in Windows.

## Using it

Enter a source folder and press Enter. Files show up while the scan runs, sorted like Explorer, one folder at a time with subfolders after it.

JPEG, HEIC, PNG, GIF, BMP, TIFF and WebP files show as pictures. Anything else, such as a `.mov`, gets an icon with its type and file name, and can be deleted, moved or copied just the same.

Left-click selects a file and shows its EXIF on the left, or for a movie its duration, size and camera details. Right-click a file for everything you can do with it: delete, move, copy, rename, full screen, map, and the fixes below. Each has a key too. After a delete or move the next file moves into its place and is selected, ready for the next decision.

| Key | Action |
| --- | --- |
| Page Up, Page Down, mouse wheel | Previous, next page |
| Home, End | First, last page |
| Arrow keys | Move the selection, across pages too |
| Delete | Delete |
| M | Move to a folder |
| C | Copy to a folder |
| F2 | Rename |
| Enter or double-click | Full screen, or play a video |
| L | Show the location on a map |
| K | Keep this file, delete its duplicates |
| X | Fix the extension |
| T | Set the file date to the date taken |
| O | Open with the default program |
| E | Show in File Explorer |
| Esc | Deselect, or leave full screen |
| F1 | About ImageLightBox |

In full screen, the arrow keys step through files, and the letter keys, Delete and F2 work the same.

## Duplicates, wrong extensions and dates

After a scan, every file is checked in the background, and round tags appear at the top right of its thumbnail. Hover over a tag for the details, which are also listed first in the side pane.

- **D**: another file has exactly the same content, whatever it's called. Only files of the same size are read and compared. **K** keeps the selected file and sends its duplicates to the Recycle Bin, after asking.
- **E**: the extension doesn't match what the file really is, such as a PNG named `.jpg`. **X** renames it to the right extension, and **Update all extensions** in the bottom bar does it for every tagged file. Raw photos such as `.dng` are TIFF inside, and aren't flagged.
- **T**: the file's modified date is more than a minute from the date taken in its EXIF. **T** sets the file's created and modified dates to the date taken, and **Update all dates** in the bottom bar does it for every tagged file. The EXIF time is taken to be in this PC's time zone unless the camera recorded one.

## The map

Show on map, in the right-click menu or on L, is available for a file with a GPS position: most phone photos, and iPhone movies. It opens OpenStreetMap in a small window, which keeps following the selection until you close it, without reloading. Closing it only hides it, so it opens again at once.

Map tiles come from openstreetmap.org, so which part of the map you're looking at goes there, but only while the map window is open. **Check for updates** contacts GitHub when you choose it. Nothing else leaves the PC. The window needs the WebView2 runtime that comes with Windows 11, and without it the map opens in your browser. The map page itself is drawn by [Leaflet](https://leafletjs.com) 1.9.4, in `ImageLightBox.Wpf\Map`.

If the page hasn't loaded after 15 seconds, the window offers to open the place in your browser instead. Each loading stage is timed in `%LOCALAPPDATA%\ImageLightBox\map.log`, which never holds a location.

## Videos

Enter, double-click, and the menu item that says Play for a video all open it in its default player. Other files that aren't images only get their icon.

## Good to know

- **Thumbnail size.** Standard always fits four images, following the window size. The fixed sizes fit as many as the window has room for.
- **Delete goes to the Recycle Bin**, also with confirmation turned off. Folders without a Recycle Bin, such as most network shares, delete for good.
- **Nothing gets overwritten.** The scan is recursive, so names often clash when moving into one folder: the later one becomes `name (1).jpg`.
- **Holding a key down doesn't repeat** delete, move or copy, so a stuck key can't run through a folder.
- **HEIC needs nothing from the Microsoft Store.** Magick.NET decodes it, at the price of about 25 MB of native code. A HEIC decodes in full even for a thumbnail, so these are slower than JPEGs, which WIC scales while decoding.
- **Paging is instant once decoded.** Up to 1 GB of images stays in memory, and the pages around the current one load ahead of time.
- **Hidden and system files and folders are skipped**, and so are symlinks and junctions, which can loop.
- Settings live in `%LOCALAPPDATA%\ImageLightBox\settings.json`.

## Credits

Made by Per Hejndorf, with Claude (Anthropic). The same credits are on the About card, behind the info button at the top right.

- [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) by Drew Noakes (Apache-2.0), for EXIF and movie metadata
- [Magick.NET](https://github.com/dlemstra/Magick.NET) by Dirk Lemstra (Apache-2.0), for HEIC
- [Leaflet](https://leafletjs.com) by Volodymyr Agafonkin (BSD-2-Clause), for the map page
- Map data © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors
- WebView2 by Microsoft, for the map window
- Segoe Fluent Icons by Microsoft
- [Velopack](https://velopack.io), for installs and updates

© 2026 Per Hejndorf

---

## For developers

| Project | Description |
| --- | --- |
| `ImageLightBox.Core` | Scanning, file actions, grid maths and EXIF, with no UI |
| `ImageLightBox.Wpf` | The app, built with WPF's Fluent theme in dark and styled like [Audiofile Player](https://github.com/kreaton/AudiofilePlayer) |
| `ImageLightBox.Tests` | xUnit tests of the core |

### Build and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project ImageLightBox.Wpf
```

```bash
dotnet test ImageLightBox.Tests
```

### Releases

Every push to `main` runs the tests, builds the app on GitHub Actions and publishes a new release, one patch version higher than the last (`.github/workflows/release.yml`). To push without releasing, add `[skip ci]` to the commit message. You can also start a release by hand from the repository's Actions tab, or run `ImageLightBox.Wpf\BuildRelease.ps1` locally. Add `-LocalOnly` to build without uploading.

The repository must stay public, because **Check for updates** reads its releases without logging in.

## License & Authorship

This project is distributed under the terms of the [MIT License](LICENSE).

**AI authorship note:** Most of the source code in this repository was generated with AI coding agents (Claude Code), under my direction and review. Purely AI-generated material may not be eligible for copyright protection in some jurisdictions. To the extent any part of this project is protected by copyright, it is licensed under the MIT License, and the license's warranty and liability disclaimers apply to the project as a whole.

<!-- notices:start -->
### Independent development

ImageLightBox is an independent, personal project developed by Per Hejndorf. It is not affiliated with, endorsed by or sponsored by Microsoft or any other rights holder (including OpenStreetMap Foundation). Product names and logos are trademarks of their respective owners and are used only to identify the services and tools the project works with.

### Third-party notices and asset provenance

[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) lists every third-party library, font and online service the project uses, with version, licence, source and whether it was modified. It also records where the icons, images, audio and video came from and how they are licensed.
<!-- notices:end -->
