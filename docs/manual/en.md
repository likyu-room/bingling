# Bingling user guide

[한국어](ko.md) · **English**

## Contents

1. [What is Bingling](#what-is-bingling)
2. [Install](#install)
3. [Basic use](#basic-use)
4. [Reading the ring](#reading-the-ring)
5. [The six picture tools](#the-six-picture-tools)
6. [Editor shortcuts](#editor-shortcuts)
7. [Screen capture and eyedropper](#screen-capture-and-eyedropper)
8. [The result card](#the-result-card)
9. [Settings](#settings)
10. [FAQ](#faq)
11. [Credits](#credits)

## What is Bingling

Drag files in File Explorer, press <kbd>Ctrl</kbd>, and a ring opens around your cursor (the app calls it the donut toolbar).
Drop the files on a slot to convert them, or to crop, redact or draw on pictures, without leaving File Explorer.
Capturing the screen and picking a colour from anywhere on it also happen inside Bingling, with [one shortcut each](#screen-capture-and-eyedropper).

<p align="center"><img src="images/tools-ring-en.png" width="360" alt="The ring for a picture, with the Tools group fanned out"></p>

## Install

Bingling runs on Windows 10 and 11.

1. Download `Bingling-<version>-win-Setup.exe` from the [release page](https://github.com/likyu-room/bingling/releases/latest) and run it.
2. The installer is not code-signed yet, so Windows may show "Windows protected your PC".
   Click **More info**, then **Run anyway**.
3. Choose the **install mode**.
   - **Install for all users (recommended)**: the usual choice. It needs administrator rights, so Windows asks once.
   - **Install for me only**: installs for your account only, without administrator rights. Choose it when Windows asks
     for an administrator's name and password, as on many work PCs. The Windows 11 menu option is then left out, and
     Bingling appears under “Show more options”.
4. Choose the options. Setup shows them on the first install.

   | Option | At first | What it does |
   |---|---|---|
   | Show “Convert with Bingling” in the Explorer context menu | On | Right-click files to open Bingling. |
   | Also show it at the top of the Windows 11 menu | On | Windows 11, installed for all users only. The entry shows without clicking “Show more options”. |
   | Start Bingling when I sign in | On | Bingling waits in the tray when you turn on the PC. The ring only opens while Bingling runs. |
   | Add the "bingling" command to PATH | Off | Convert with the `bingling` command in a terminal. Turn it on only if you need it. |

5. Choose the **conversion engines**.
   Pictures, PDF, Word, Excel and PowerPoint, HWP, e-books, archives, fonts, CAD and 3D models convert without any of them.
   The engines below are large, so download only the ones you need. The page shows each download size.

   | Engine | You need it for |
   |---|---|
   | LibreOffice | Older documents such as DOC and PPT, Pages, Keynote, Numbers, WordPerfect, Publisher and Visio |
   | Pandoc | Markup formats such as Markdown, LaTeX and Org, and PPTX slides from Markdown |
   | Ghostscript | EPS, PostScript and older Illustrator files |
   | FFmpeg | Video and audio |
   | Calibre | MOBI and AZW3 for Kindle, and rare e-book formats |

   The engines you pick are downloaded when Bingling starts after Setup.
   You don't have to pick any now: you can install or remove engines at any time in **Settings → Engines**.

<p align="center"><img src="images/engine-setup-en.png" width="420" alt="The Choose conversion engines window"></p>
<p align="center"><sub>The “Choose conversion engines” window in Bingling. Setup offers the same engines.</sub></p>

After Setup, Bingling runs in the notification area of the taskbar (the tray).
Updates don't ask about engines again. Your settings and engines stay.

### Without installing (portable)

If you can't run Setup or would rather not install, use the portable ZIP. It needs no administrator rights.

1. Download `Bingling-<version>-win-Portable.zip` from the [releases page](https://github.com/likyu-room/bingling/releases/latest).
2. Unpack it into a folder you will keep (for example `Documents\Bingling`).
3. Run `Bingling.exe` in that folder. If “Windows protected your PC” appears, choose **More info**, then **Run anyway**.

- The ring, the picture tools, conversions, the result card and engine downloads work as in the installed copy.
- On its first start it registers “Start Bingling when I sign in” and the Explorer context menu for your account. On
  Windows 11 the menu entry is under “Show more options”.
- Settings are kept in the same place as the installed copy's (`%APPDATA%\Bingling`). It is not meant to be carried to
  other PCs on a USB stick.

**Updating**: when a new version is out, its card has a **Download** button that opens the releases page. Right-click
the tray icon, choose **Quit**, then unpack the new ZIP over the same folder. Your settings and engines stay.

**Removing it**: in **Settings → General**, turn off “Start Bingling when I sign in” and “Show ‘Convert with Bingling’ in
the Explorer context menu”, choose **Quit** in the tray, then delete the folder. To delete the downloaded engines as
well, delete the `%LocalAppData%\Bingling` folder too.

## Basic use

### Open the ring by dragging

1. Drag files or folders in File Explorer or on the desktop. You can drag many at once.
2. While dragging, press <kbd>Ctrl</kbd>. The ring opens around your cursor. (Holding <kbd>Ctrl</kbd> before you start dragging works too.)
3. Release the mouse button over the slot you want.

After the drop the ring turns into a small progress ring, and a ✓ shows when it is done.
If it takes longer than 5 seconds, the [result card](#the-result-card) in the bottom-right corner takes over.
When the result is a picture, the card shows it even if the conversion finished at once.

<p align="center"><img src="images/progress.png" width="300" alt="The progress ring and the finished check mark"></p>

The result is saved **as a new file in the same folder as the original**. The original stays as it is.
If a file of that name already exists, a number is added (for example `photo (2).jpg`).

To cancel, drop the files in the middle of the ring. The middle says “Drop here to cancel”.

### Other ways to open it

| Way | How |
|---|---|
| Keyboard shortcut | Select files in File Explorer and press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd> with the pointer over the File Explorer window. The ring opens there. |
| Right-click menu | Right-click files and choose **Convert with Bingling**. On Windows 11 it is under “Show more options” unless you added it to the main menu. |
| Bingling window | Drag files onto “Drop files here” on the **Home** page of the Bingling window. |
| Watch folders | Files that land in a folder you chose are converted on their own. Set one up in [Settings](#watch-folders). |

A ring opened these ways is used by **clicking** a slot. The middle says “Esc to close”, and <kbd>Esc</kbd> closes it.
You can also move between slots with the arrow keys or <kbd>Tab</kbd> and choose with <kbd>Enter</kbd>.

<p align="center"><img src="images/home-en.png" width="720" alt="The Home page of the Bingling window"></p>

**Want Shift too?** **Settings → Shortcuts** has a “Shift + Left” gesture that is switched off. Switch it on, and dragging with <kbd>Shift</kbd> opens the ring as well.

**Closing the Bingling window:** when it asks “Keep Bingling running?”, choose **Keep in tray**.
**Quit** stops Bingling, and the ring no longer opens. Click the tray icon to open the window again.

## Reading the ring

Look at the [picture above](#what-is-bingling) while you read this.

- **The middle** shows the format and number of the files you brought, and their total size (for example “1 × PNG · 620 B”, or “3 files” for mixed formats).
  Over a slot, its second line says what will happen, such as “Drop → JPG” (“Click → JPG” on a ring you click).
  Over the middle, it says “Drop here to cancel”.
- **Slots**: each slot does one thing. The large text is its name, the small text the format of the result.
  A slot that keeps the format, such as “Half size”, shows the source format in small text.
- **A ring for each kind of file**: pictures, PDF, documents, video and so on each get their own ring. A mix of kinds gets the “Mixed files” ring.
- **The folders ring**: drag only folders and the folders ring opens. **ZIP** and **7Z** make one archive per folder
  (`Photos` → `Photos.zip`); **One ZIP** puts every folder you dragged into a single ZIP. Folders dragged
  together with files open the “Mixed files” ring, whose **One ZIP** packs the folders too.
  A ring holds up to 12 slots.
- **Group slots**: a slot with “Group of 6” in small text, like “Tools”, holds several slots.
  Rest on it or move outward and its slots fan out around it. While dragging, drop on one of those.
- **Dimmed slots** can't be used with these files. Point at one and the middle tells you why, for example:
  - “Needs FFmpeg (video and audio). Install it in Settings → Engines”: install that engine and the slot works.
  - “Only single still pictures can be edited”: the picture tools don't work on pictures with several frames, such as animated GIFs.
  - “No engine can do this conversion”
- **Editing a slot**: on a ring opened by the shortcut or a menu, right-click a slot to change its preset right away.

## The six picture tools

The **Tools** slot of the picture ring holds six tools. Rest on it and they fan out.

<p align="center"><img src="images/tools-ring-en.png" width="360" alt="The Tools group fanned out"></p>

- **Crop, Add background, Redact and Annotate** open an editing window where the ring was.
- **Compress and Strip metadata** run right away, without a window.
- Crop, Add background, Redact and Annotate work on single still pictures. If an animated GIF or an icon (ICO) is among the files, their slots are dimmed.
- The picture ring also has a **Quick frame** slot outside the Tools group. It puts [your last backdrop](#quick-frame) on the pictures without a window.

### What every editing window has

- The top left shows the tool and the file name (for example “Crop · beach.png”).
- The buttons at the top right are **Undo**, **Redo** and **Fit to window**.
- The buttons at the bottom are **Reset**, **Cancel**, **Copy** and **Save**. As the note beside them says, the original stays untouched: the result is saved next to it as a new file.
- **Copy** (<kbd>Ctrl</kbd>+<kbd>C</kbd>) copies the edited picture without making a file, ready to paste into a chat or a document.
  The window then closes. To keep it open, to edit more or save a file too, change it in **Settings → General → Image tools**.
  With several pictures, Copy takes the one you are looking at, and the window stays open so you can copy the next.

**Several files at once**

With several pictures, the top shows which one you see, such as “1 of 2”.
Step through them with the ‹ › buttons or the <kbd>←</kbd> <kbd>→</kbd> keys.

- Crop, Add background and Redact: your settings go on **every picture the same way**. The bottom says “Applies to all 2 files”.
- Annotate: you draw on **each picture separately**, and only the pictures you drew on are saved.

**File names**

A word is added to the name. For `photo.jpg`:

| Tool | Saved as |
|---|---|
| Crop | `photo Cropped.jpg` |
| Add background | `photo with background.png` (always PNG) |
| Quick frame | `photo with background.png` (the same as Add background) |
| Redact | `photo Redacted.jpg` |
| Annotate | `photo Annotated.jpg` |
| Compress | `photo Compressed.jpg` |
| Strip metadata | `photo Clean.jpg` |
| Saving a screen capture | `Capture 2026-10-22 131500.png` (in Pictures\Bingling; the date and time are the name) |

Except for Add background, Quick frame and screen captures, the result keeps the format of the original.

### Crop

Keeps only the part of the picture you want.

<p align="center"><img src="images/crop-en.png" width="720" alt="The Crop window"></p>

1. Drag pictures and drop them on **Tools → Crop**.
2. Pick a shape under **Aspect ratio**: Free, Original, 1:1, 16:9, 9:16, 4:3 or 3:4. The number keys <kbd>1</kbd> to <kbd>7</kbd> pick them too.
3. Drag the box to move it, and drag its corners or edges to resize it. Dragging outside the box draws a new one.
   You can also type numbers under **Size (px)**.
4. Click **Save** or press <kbd>Enter</kbd>. A double click inside the box saves too.

- With “Free”, hold <kbd>Shift</kbd> while dragging to keep the box's shape.
- The ratio you picked is remembered for next time.

### Add background

Puts the picture on a backdrop with padding, like a card, with rounded corners and a shadow if you like.

<p align="center"><img src="images/background-ko.png" width="720" alt="The Add background window"></p>
<p align="center"><sub>Shown with the Korean interface. The English window has the same layout.</sub></p>

1. Drop pictures on **Tools → Add background**.
2. Under **Background**, pick one of 8 gradients or 6 solid colours.
   - **Custom color**: type a colour code such as `#FF7A1A` and it applies right away.
   - **Eyedropper** (<kbd>I</kbd>): switch it on and click the picture to take the colour you see there as the custom colour; the eyedropper then switches off.
     <kbd>Esc</kbd> only switches the eyedropper off (the window stays). A see-through spot can't be picked.
     **Copy code** beside it copies the code (HEX) of the current solid colour.
   - **Choose image…**: use a picture of your own as the backdrop.
   - **Photo color**: fills the background with the photo's average colour, softened with white.
3. Move the **Padding**, **Corners** and **Shadow** sliders. Each shows its value above it on the right.
4. Pick the shape of the result under **Aspect ratio**: Auto, 1:1, 16:9, 9:16, 4:5 or 4:3. The size of the result shows as “Result: 1112 × 812px”.
5. Click **Save**.

The result is always a PNG. The backdrop you chose is remembered for next time.

### Quick frame

Drop pictures on the **Quick frame** slot of the picture ring and the backdrop goes on at once, without a window, saved as a PNG. Handy for giving several captures the same look.

- It uses **what you last set in the Add background window**: the backdrop, padding, corners, shadow and aspect ratio. If you never set one, the first settings apply.
  If your last backdrop was a picture of your own, that picture isn't remembered; the gradient or colour before it is used.
- The slot has no settings window and no tooltip. To change the backdrop, set it under **Tools → Add background** and save. Quick frame uses it from then on.
- Like Add background it works on single still pictures only, and saves `photo with background.png`.
- After an update the slot is added at the end of the picture ring, if the ring has room. If it was full, drag the **Quick frame** chip onto the ring under **Settings → Donut toolbar**.

### Redact

Hides what you don't want to show, such as a name or a phone number, with a black box, a mosaic or a blur. Bingling can also [find what to hide](#find-what-to-hide) for you.

<p align="center"><img src="images/redact-ko.png" width="720" alt="The Redact window"></p>
<p align="center"><sub>Shown with the Korean interface. The English window has the same layout.</sub></p>

1. Drop pictures on **Tools → Redact**.
2. Under **Style**, pick **Black box** (<kbd>B</kbd>), **Mosaic** (<kbd>M</kbd>) or **Blur** (<kbd>U</kbd>).
   **Blur** first turns the box into a mosaic and blurs that, so what is blurred can't be brought back.
3. Drag on the picture to draw a box over what to hide. Draw as many as you need.
   - Click a box to select it, drag it to move it, and drag a corner to resize it.
   - Remove the selected box with **Delete selected** or <kbd>Delete</kbd>.
4. Click **Save**. Save stays off until there is at least one box.

> **The photo info goes too.** Redact always removes the photo info (EXIF), such as where and with what camera the
> photo was taken, because that info can still hold what you hid.

With several pictures, the same boxes go in the same place on each. Check each picture with ‹ ›.

#### Find what to hide

Bingling can look for things such as phone numbers and faces in the picture and offer them as candidates. It works with **one picture only**: the boxes go on every picture the same way, so with several pictures the button is off.

<p align="center"><img src="images/redact-find-ko.png" width="720" alt="The Redact window with what was found"></p>
<p align="center"><sub>Shown with the Korean interface. The English window has the same layout.</sub></p>

1. Click **Find what to hide**. “Looking…” shows for a moment.
2. What it found is listed with its kind and text, and outlined on the picture. All are ticked at first. Untick what you want to leave.
3. Click **Hide selected**. Boxes are added over the ticked ones in the current **Style** (black box, mosaic or blur), and the list is emptied. One **Undo** takes them all back.

- It looks for: **phone numbers** (010, area codes such as 02 and 031, 050 safe numbers, +82, area codes in brackets), **e-mail addresses**, **ID numbers** (Korean resident registration numbers), **card numbers** (only those that pass a real card-number check), **addresses** (with a province or city, a district, a street name and a number) and **faces**.
  It doesn't look for names.
- It can't find addresses without a district, such as those in Sejong and Jeju, or addresses split over two lines.
- It isn't perfect. Look at the picture once more before you save, and box what it missed yourself.
- It looks **on your PC only**. It uses the text recognition built into Windows, and the picture is never sent over the internet.
  If this PC has no language for reading text, it says “This PC has no language for reading text” and finds faces only.
- On a work PC that blocks programs very strictly, finding may not work. Box what you want to hide yourself.

### Annotate

Marks up a picture with arrows, text, numbers and more.

<p align="center"><img src="images/annotate-ko.png" width="720" alt="The Annotate window"></p>
<p align="center"><sub>Shown with the Korean interface. The English window has the same layout.</sub></p>

1. Drop pictures on **Tools → Annotate**.
2. Pick a tool at the top right.

   | Tool | Key | How |
   |---|---|---|
   | Select | <kbd>V</kbd> | Click to pick, drag to move. Double-click a text to edit it. |
   | Pen | <kbd>P</kbd> | Drag to draw. |
   | Highlighter | <kbd>H</kbd> | Drag to draw a wide, see-through line. |
   | Arrow | <kbd>A</kbd> | Drag to draw. Hold <kbd>Shift</kbd> for straight angles. |
   | Line | <kbd>L</kbd> | Drag to draw. Hold <kbd>Shift</kbd> for straight angles. |
   | Rectangle | <kbd>R</kbd> | Drag to draw. Hold <kbd>Shift</kbd> for a square. |
   | Ellipse | <kbd>E</kbd> | Drag to draw. Hold <kbd>Shift</kbd> for a circle. |
   | Text | <kbd>T</kbd> | Click where you want it. <kbd>Enter</kbd> finishes, <kbd>Shift</kbd>+<kbd>Enter</kbd> starts a new line. |
   | Number | <kbd>N</kbd> | Each click adds the next number: 1, 2, 3… |
   | Spotlight | <kbd>S</kbd> | Drag a box and everything outside it is dimmed. Hold <kbd>Shift</kbd> for a square. |

3. Pick a **Color** (8 colours, or a code under **Custom color**) and a **Thickness** (Thin, Normal, Thick).
   <kbd>[</kbd> and <kbd>]</kbd> change the thickness too, which also sets the size of text and numbers.
4. Click **Save**. Save stays off until you have drawn something.

- Select a drawing with the Select tool, then click a colour or a thickness: that drawing changes at once. <kbd>Delete</kbd> removes it.
- **Spotlight** dims everything outside its box by 55%. It uses no colour or thickness. However many you draw, the outside is dimmed once, and the other drawings stay bright on top.
- **Eyedropper** (<kbd>I</kbd>) is the button under **Custom color**. Switch it on and click the picture: that colour becomes the custom colour, and the eyedropper switches off.
  <kbd>Esc</kbd> only switches the eyedropper off (the window stays). It takes the colour as you see it, drawings included, and a see-through spot can't be picked.
  **Copy code** copies the code (HEX) of the current colour.
- **Each picture has its own drawings.** Another picture shows only what you drew on it, and undo works per picture too.
  Only the pictures you drew on are saved, and the bottom says so, such as “Only the 2 marked files will be saved”.
- If you close the window with drawings on it, it asks “Discard your drawings and close?”. Choose **Discard** or **Keep drawing**.

### Compress

Makes picture files smaller. Dropping on the slot runs it right away, without a window.

- The photo info (EXIF) is removed too.
- If the result isn't smaller than the original, no new file is kept, and the result card says “Skipped (not smaller)”.

### Strip metadata

Makes a new file without the photo info (EXIF and the like), such as the place, the camera and the date. On the ring it is called “Metadata”.
Dropping on the slot runs it right away, without a window. The colour profile stays, so the colours don't change.

## Editor shortcuts

For the Crop, Add background, Redact and Annotate windows. Point at a button to see its shortcut.

| Key | What it does |
|---|---|
| <kbd>Enter</kbd> | Save. While you type a size, it applies the number first. |
| <kbd>Esc</kbd> | Cancel and close |
| <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copy without making a file. While you type a size, it copies that text. |
| <kbd>Ctrl</kbd>+<kbd>Z</kbd> | Undo |
| <kbd>Ctrl</kbd>+<kbd>Y</kbd> (or <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Z</kbd>) | Redo |
| <kbd>Ctrl</kbd>+<kbd>0</kbd> | Fit to window |
| <kbd>Ctrl</kbd>+<kbd>1</kbd> | Actual size (100%) |
| Mouse wheel | Zoom in and out around the pointer |
| <kbd>Space</kbd>+drag (or drag with the middle button) | Move a zoomed picture |
| <kbd>←</kbd> <kbd>→</kbd> | Previous and next picture |

Keys of each tool:

| Tool | Keys |
|---|---|
| Crop | <kbd>1</kbd>–<kbd>7</kbd> pick a ratio (Free, Original, 1:1, 16:9, 9:16, 4:3, 3:4) · <kbd>Shift</kbd>+drag keeps the shape |
| Add background | <kbd>I</kbd> Eyedropper |
| Redact | <kbd>B</kbd> Black box · <kbd>M</kbd> Mosaic · <kbd>U</kbd> Blur · <kbd>Delete</kbd> removes the selected box |
| Annotate | <kbd>V</kbd> <kbd>P</kbd> <kbd>H</kbd> <kbd>A</kbd> <kbd>L</kbd> <kbd>R</kbd> <kbd>E</kbd> <kbd>T</kbd> <kbd>N</kbd> <kbd>S</kbd> pick a tool · <kbd>I</kbd> Eyedropper · <kbd>[</kbd> <kbd>]</kbd> thickness · <kbd>Delete</kbd> removes the selected drawing |

While the eyedropper is on, <kbd>Esc</kbd> only switches it off. While you type a text, <kbd>I</kbd> is just a letter.

## Screen capture and eyedropper

These work on the screen itself, without a file. While Bingling runs, a shortcut starts each one over any program. They work on Windows.

| What | Shortcut at first | Tray menu |
|---|---|---|
| Screen capture | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>S</kbd> | Capture the screen |
| Instant eyedropper | <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>C</kbd> | Pick a color |

You can [change the shortcuts](#shortcuts) under **Settings → Shortcuts**. With several monitors, the one **with the mouse pointer on it** is used.

### Screen capture

<p align="center"><img src="images/capture-ko.png" width="640" alt="A capture with an area chosen"></p>
<p align="center"><sub>Shown with the Korean interface, on a sample screen.</sub></p>

1. Press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>S</kbd>. The screen freezes and dims.
2. Drag over what you want to capture. The area is bright and its size in pixels shows. An area smaller than 8 pixels is ignored.
   The bar at the top switches between **Area**, **Full screen** and **Eyedropper**.
3. Buttons appear below the area (above it when there is no room).

   | Button | Key | What it does |
   |---|---|---|
   | **Edit** | | Opens the [Annotate](#annotate) window. After you mark it up, **Save** puts it in the folder below under the same name. Save stays off until you have drawn something. |
   | **Copy** | <kbd>Enter</kbd>, <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copies the picture. “Copied” shows for a moment, then it closes. |
   | **Save** | <kbd>Ctrl</kbd>+<kbd>S</kbd> | Saves a PNG file and shows the [result card](#the-result-card). |

4. To give up, press <kbd>Esc</kbd> or right-click.

The file goes into **Pictures\Bingling**, named by the date and time, such as `Capture 2026-10-22 131500.png`. If that name exists, a number is added.

### Instant eyedropper

Gets you a colour code from anywhere on the screen.

<p align="center"><img src="images/color-card-ko.png" width="300" alt="The card of a picked colour"></p>
<p align="center"><sub>Shown with the Korean interface.</sub></p>

1. Press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>C</kbd>. The screen freezes as it is, and a magnifier follows the pointer. The centre square of the magnifier is the colour you get, and its HEX shows below.
2. Click to **copy the HEX at once** and open a colour card. <kbd>Esc</kbd> or a right-click gives up.
3. The card shows a swatch with the HEX, RGB and HSL. **Copy HEX**, **Copy RGB** and **Copy HSL** copy that format.
   The card goes away after 6 seconds, on <kbd>Esc</kbd>, or when you click outside it.

The screen looks frozen while you pick, so a playing video gives you the colour of that moment. In the capture screen, the **Eyedropper** in the top bar does the same.

## The result card

A card appears in the bottom-right corner of the screen when a conversion finishes.

<p align="center"><img src="images/toast-en.png" width="360" alt="The result card of a finished conversion"></p>

**When the card appears**

- When you save with a picture tool (Crop, Add background, Redact, Annotate)
- When you **Save** a screen capture
- When a conversion from the ring makes a picture (even a quick one, so you can see and copy it at once)
- When a conversion from the ring takes longer than 5 seconds
- When a file failed or was skipped
- When a watch folder converts files

A quick conversion from the ring to something other than a picture, such as a PDF, only shows the ✓ in the ring.

**What you can do on the card**

- **Progress**: while it converts, a progress bar and a percentage show. **Cancel** stops it.
- **Preview**: when the result is a picture, a small preview shows. Click it to open the result.
- **Size change**: such as “704.9 KB → 384.7 KB · 45% smaller”.
- **Copy**: copies the result, so you can paste it into a chat or a document. The button says “Copied” for a moment.
- **Show**: opens the folder with the result.
- **×**: closes the card. Left alone, it goes away by itself after a while.

**When something fails**

<p align="center"><img src="images/toast-retry-en.png" width="360" alt="A result card with a failed file"></p>

The card says how many failed, such as “1 of 2 failed”, and why for each file.

- **Retry**: converts only the files that failed again.
- **Request this format**: when Bingling doesn't know a format yet, you can ask for it.

You can find earlier results again under **Home → Recent conversions** in the Bingling window.

## Settings

Click the Bingling tray icon, or choose **Open Bingling** in its menu, to open the window. Pick a page on the left.

### Donut toolbar

Choose which slots each ring has, and in which order.

<p align="center"><img src="images/settings-toolbar-en.png" width="720" alt="The Donut toolbar page"></p>

1. Under **1. Choose a profile**, pick the ring to change (Images, PDF, Documents…). The number is how many slots it has.
2. Arrange the slots under **2. Arrange presets**:
   - Drag a chip from **Available presets** onto the ring to add it.
   - Drag a slot back to the chips to remove it.
   - Drag slots onto each other to reorder them.
   - Right-click a slot or click ✎ to edit its preset.
3. A ring holds up to 12 slots.

- **New preset** makes a new slot and puts it on the ring you picked (when the ring has room).
- **New group** makes a group, one slot for several presets. Drag it onto the ring when it is ready.
- **Used for these extensions** decides which files open this ring.
- **Appearance** changes the size, the hole and the opacity of the ring.

### Presets

A preset is what one slot does, such as “PNG” or “JPG 70%”: what to convert to, and where to save it.

<p align="center"><img src="images/settings-presets-en.png" width="720" alt="The Presets page"></p>

**Making your own**

1. On the **Presets** page, click **New**. Or select a similar preset and click **Duplicate**.
2. Set its **Name** and **Convert to**.
3. Set the options of that format: for pictures, the quality, resizing, rotation and so on.
4. Under **Output**, choose how to save:
   - **Save to**: the default save location, the same folder as the source, a subfolder, or a fixed folder
   - **File name**: built from words such as `{name}` (the original name) and `{date}`
   - **If the file exists**: Add a number, Replace it, or Skip
5. Click **Save**.
6. To put the preset on a ring, drag its chip onto the ring on the **Donut toolbar** page.

### Watch folders

Files that land in a folder you choose are converted on their own. Handy for scans and downloads.

<p align="center"><img src="images/settings-watch-en.png" width="720" alt="The Watch folders page"></p>

1. Click **Add folder**.
2. Choose the **Folder to watch** and the preset under **Convert with**.
3. Under **After converting**, choose “Leave the original where it is” or “Move the original to "originals"”.
4. Leave **Save converted files to** empty to save into a “converted” folder inside the watched folder.
5. Watching starts as soon as a folder is chosen. To pause it, switch off **On** at the top.

The **Preview** shows the name each file will get. **More options (optional)** chooses which files to convert and how to name them.
Watch folders work while Bingling runs. Files still downloading or copying are converted when they are complete.

### Theme

<p align="center"><img src="images/theme-en.png" width="720" alt="The Theme page"></p>

- **Style**: at first it is **Peach Glass**, a soft, see-through glass look. You can switch to the original program's classic style.
- **Appearance**: System, Light or Dark. System follows Windows.
- **Accent colour**: used by the classic style. Peach Glass uses its own orange accent colour.
- **Softness**: how round the corners are.
- **Reduce animations**: recommended for older PCs. Turns off transitions and the ring's bounce.

### Engines

<p align="center"><img src="images/engines-en.png" width="720" alt="The Conversion engines page"></p>

- Under **Optional engines**, **Install** or **Remove** LibreOffice, Pandoc, Ghostscript, FFmpeg and Calibre.
  A download shows a progress bar and the time left.
- If one is already installed on your PC, pick it with **Use my copy…** and Bingling uses it instead of downloading its own.
- Engines are kept when Bingling updates and removed when you uninstall it.
- Under **All engines** you can change the order. When several engines can do the same job, the higher one is used.

### Shortcuts

Choose how the ring opens, and the shortcuts for [screen capture and the eyedropper](#screen-capture-and-eyedropper). You can add as many gestures as you like.

<p align="center"><img src="images/settings-shortcuts-en.png" width="560" alt="The Shortcuts page"></p>
<p align="center"><sub>It shows up to the screen capture card. The eyedropper card is below it.</sub></p>

There are five at first:

| Gesture | At first |
|---|---|
| <kbd>Ctrl</kbd> + drag files | On |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd> (files selected in File Explorer) | On |
| <kbd>Shift</kbd> + drag files | Off |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>S</kbd> (**Capture the screen**) | On |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>C</kbd> (**Pick a color**) | On |

When you update from an earlier version, the screen capture and eyedropper shortcuts are added once. If you already used one of those keys, it is added switched off.

- The switch at the top left of each card turns it on or off. Change the keys under **Modifier keys** (Ctrl, Alt, Shift, Win).
- **Action**: on a keyboard shortcut card, choose **Open the donut toolbar (selected files)**, **Capture the screen** or **Pick a color**. To change the keys, click **Record** and press the new keys.
  Choosing **Capture the screen** or **Pick a color** sets **Active in** to “Everywhere”.
- A <kbd>Ctrl</kbd>+<kbd>Alt</kbd> shortcut pressed with the right <kbd>Alt</kbd> (AltGr) is not treated as a shortcut, so a shortcut doesn't break typing letters with AltGr on your keyboard. Use the left <kbd>Alt</kbd>.
- **Active in**: “File managers and desktop” opens the ring only in File Explorer and on the desktop. You can choose “Everywhere” instead.
- **Never trigger in these apps**: add a program in which you need Ctrl + drag for something else.
- To take a break, choose **Pause shortcuts** in the tray icon's menu.

### General

Language, starting when you sign in, what closing the window does, the Explorer context menu, and the **Default save location**.
The default save location is “Same folder as the source” at first.
Under **Image tools**, turn off “Close the editing window after Copy” to keep an editing window open after you copy.

## FAQ

**Does Bingling change my original files?**
No. Results are always saved as new files and the original stays as it is. Even a preset set to “Replace it” never replaces the original itself.
Only a watch folder set to “Move the original to "originals"” moves the original, into that folder.

**Are my files uploaded anywhere?**
No. Every conversion and edit happens on your PC, and so do screen capture and Redact's “Find what to hide”. Bingling only goes online to download engines and to check for updates.
Bingling uploads nothing itself, but if OneDrive backs up your Pictures folder, OneDrive can upload the captures saved in **Pictures\Bingling**.

**Can it convert old .doc and .ppt files?**
Yes, with the **LibreOffice** engine. Without it the slot is dimmed, and pointing at it tells you to install it.
Install it in **Settings → Engines**. DOCX, XLSX, PPTX and HWP convert without LibreOffice.

**Setup asks for an administrator on my work PC.**
Choose **Install for me only** in Setup's first window. Bingling then installs for your account without administrator rights.
If Setup itself is blocked, unpack the [portable ZIP](#without-installing-portable) and use that.
If your company blocks unsigned programs from running at all, none of this helps. Ask your IT department.

**How do I update?**
When a new version is out, Bingling shows a card such as “Bingling 1.2.0 is available”. Click **Update** and it downloads and installs the new version.
Your settings and engines stay. You can also check yourself with **Check now** on the **About** page of the Bingling window.

**Can I use it together with the original Filee?**
Yes. Bingling keeps its own settings folder (`%APPDATA%\Bingling`) and its own Explorer menu entry, so neither touches the other's settings.
If both have the same shortcut switched on, though, it can get confusing. Change or switch off the shortcut in one of them.

**Where did my result go?**
With the first settings, most results go into the same folder as the original. **Home → Recent conversions** in the Bingling window has **Open folder** to take you there.

## Credits

Bingling is based on [Filee](https://github.com/KnifeLemon/Filee) 1.12.0 by KnifeLemon, released under the MIT license.
