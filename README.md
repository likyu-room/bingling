<h1 align="center">Bingling</h1>

<p align="center">
  <b>Drag files, press <kbd>Ctrl</kbd>, and a ring opens around your cursor. Drop them on a slot and you're done.</b><br>
  Convert formats, crop, redact and draw on pictures without leaving File Explorer.
</p>

<p align="center">
  <a href="../../releases/latest"><b>Download</b></a> ·
  <a href="docs/manual/en.md">User guide</a> ·
  <a href="#how-it-works">How it works</a> ·
  <a href="#install">Install</a> ·
  <a href="#command-line">Command line</a> ·
  <a href="#development">Development</a>
</p>

<p align="center"><b>English</b> · <a href="README.ko.md">한국어</a> · <a href="README.zh-CN.md">简体中文</a></p>

## How it works

1. Drag files in File Explorer.
2. While dragging, press <kbd>Ctrl</kbd>. A ring opens around the cursor with only the slots that fit the files you hold.
3. Drop the files on the slot you want. The result is saved next to the original.

Besides the ring you can use the Explorer right-click menu ("Convert with Bingling"), keyboard shortcuts, a drop
window, watch folders and the [command line](#command-line).

## Features

- **Format conversion**: pictures, PDF, Word, Excel and PowerPoint, HWP, e-books, video and audio, archives, fonts, CAD
  and 3D models, and more. Many files convert at once, and conversions that need several steps are chained for you.
  Some formats need an extra engine, which is downloaded only when you need it ([engines](docs/ENGINES.md)).
- **Picture tools**, together in the ring's "Tools" slot:
  - Crop
  - Add background: gradients, solid colours, your own picture or the picture's own colour
  - Redact: black box or mosaic. The photo info (EXIF) is removed from the result too.
  - Annotate: pen, highlighter, arrow, shapes, text and numbers
  - Compress
  - Strip metadata

  **Copy** (<kbd>Ctrl</kbd>+<kbd>C</kbd>) in an editing window puts the result on the clipboard without making a file.
- **Peach Glass theme**: a soft, see-through glass look, in a light and a dark mode.
- **Result card**: when a conversion finishes a card appears. "Copy" puts the result on the clipboard so you can paste it
  into a chat or a document, and clicking the picture opens the result.
- **Six languages**: English, 한국어, 简体中文, Español, Polski, हिन्दी
- **Stays on your PC**: your files are never uploaded.

## Install

For Windows 10 and 11.

1. Download `Bingling-<version>-win-Setup.exe` from the [latest release](../../releases/latest) and run it.
2. The installer is not code-signed yet, so Windows SmartScreen may show "Windows protected your PC".
   Choose **More info**, then **Run anyway**.
3. Install for all users (administrator rights), or for you only without administrator rights, for example on a work
   PC.
4. Setup lets you choose the Explorer menu entry, starting with Windows, the `bingling` command (off unless you tick
   it) and extra engines.

Bingling tells you when a new version is out and updates while keeping your settings and engines.
If you use 1.1.0 under its former name, Filee Plus, just install Bingling: Setup removes Filee Plus and moves its
settings and downloaded engines over.

**Without installing (portable)**: download `Bingling-<version>-win-Portable.zip`, unpack it into any folder and run
`Bingling.exe`. To update, unpack the new ZIP over the same folder. See the [user guide](docs/manual/en.md#without-installing-portable).

## Command line

If you chose it in Setup, the `bingling` command uses the same engines and presets as the app.

```
bingling convert photo.heic --to jpg
bingling convert *.png --to webp --quality 80 -o converted
bingling watch D:\Inbox --to pdf --move-originals
```

## Development

You need the .NET 10 SDK.

```bash
dotnet run --project src/Filee.App
dotnet test tests/Filee.Core.Tests
```

Run the test projects one at a time (`tests/Filee.App.Tests`, `Filee.Platform.Tests`, `Filee.Cli.Tests`, `Filee.Engines.Tests`).
[Architecture](docs/ARCHITECTURE.md) · [Engines](docs/ENGINES.md) · [Adding a converter](docs/ADDING-A-CONVERTER.md) ·
[Adding a language](docs/ADDING-A-LANGUAGE.md) · [Contributing](CONTRIBUTING.md)

The code projects keep the original names (`Filee.*`). The program is `Bingling.exe` and its settings are in
`%APPDATA%\Bingling`.

## Credits

Bingling is based on [Filee](https://github.com/KnifeLemon/Filee) 1.12.0 by KnifeLemon (MIT license).
If you like the original, please support its author too: [GitHub Sponsors](https://github.com/sponsors/KnifeLemon)

## License

MIT, © 2026 Beomsuk Kim (LikeU). Based on Filee, © 2026 Myeonggu Kim (KnifeLemon) and Filee contributors.
The full text is in [LICENSE](LICENSE); the third-party components it includes and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
