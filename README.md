# AutoDraw for Inventor 2025

Makes the production drawing of an assembly: one `.idw` with a sheet for every part and sub-assembly, views per
part type, parts lists with balloons, overall dimensions and, on flat patterns, bend dimensions and bend notes.

## Use

1. Open the main assembly and click **AutoDraw** on the **Assemble** tab.
2. The AutoDraw window opens (a normal window: move it to a second screen if you like).
3. Check the **order number** (`ordernummer` iProperty of the assembly) and choose the **drawing template** once.
4. In the tree: tick the documents that get a sheet, change a detected **Type** if it is wrong, edit **Aantal**, the
   **Sheets** of an assembly or any added iProperty column (**Columns…**).
   Values changed by hand are bold and coloured, with the original as tooltip; **Delete** on such a cell puts it back.
5. **Generate drawing**. The drawing is saved next to the assembly as `<ordernummer>_<assembly>_<y-M-d>.idw`
   (`_2`, `_3` … when that name is taken) and opened.

## Several models, loose parts

AutoDraw also starts from a part (**3D Model** or **Sheet Metal** tab). For separate assemblies and loose parts without
a main assembly: start AutoDraw on one, then **Models…** → **Add** an open assembly or part or **Add from file…**
(`.iam`, `.ipt`; or click AutoDraw in another open model and answer Yes). With a part, or with more than one model, the
tree gets a top row `A+B+…` without a sheet, with each model under it as if it were in one main assembly:

- sheets: the assemblies first, then all parts by type, as usual; a part used in several assemblies has one sheet,
  with the total quantity;
- **Numbering…**: the assemblies get 100, 200, 300 …, their sub-assemblies 110, 120 … (the top row has no number);
- the order number is written to every assembly; the drawing is saved next to the first one as
  `<ordernummer>_A+B_<date>.idw` (`A+2 more` when the names get long);
- the drawing remembers its assemblies (custom iProperty `AutoDraw Assemblies`), so **Update drawing** opens for all
  of them again. **Models…** also adds or takes out a model there; ticks, types and typed values are kept.

## Vault

Before **Generate drawing**, **Update drawing** and **Numbering…**, AutoDraw looks for files it would write that are
read-only (checked in) and asks whether to check them out. Yes runs Vault's **Check Out** on the drawing and the
models, where the files are ticked; No goes on without them (they are not written).

## Updating a drawing

Open a drawing made by AutoDraw and click **AutoDraw** on the **Place Views** tab. The window opens for the drawing's
assembly (the one shown on its first sheet); documents with a sheet in the drawing are ticked, the others are not.

- **Tick** a document without a sheet to add one; **untick** one to delete its sheet.
- **Redo** makes a document's sheets again (views, dimensions), e.g. after a change of the model or its type.
- **Numbering…** works as from the assembly; the new numbers sort the sheets on the next update.
- **Update drawing** writes the iProperties again, makes the new and redone sheets, asks before deleting sheets of
  unticked documents and of documents no longer in the assembly (No keeps them at the end), puts all sheets in order
  (type, then part number), renames them, runs the iLogic rules and saves the drawing.

## What it writes

- `Aantal` – total quantity of the document in the main assembly (or the value typed in the tree). An Aantal typed on
  an assembly carries on below it: main assembly 3 makes every part 3×, a sub-assembly 10 makes its parts 10× their
  count in it (plus their uses elsewhere). Typed on a part it is only that part's value. The main assembly's Aantal
  is read back when the window opens again, so the parts stay 3×.
- `bewerking` and `bewerkingBlad` – the sheet number, `010`.
- `ordernummer` on the main assembly, and every edited iProperty column.
- Sheet names are the document names; sheet metal parts without a flat pattern get one.

## Part numbers (Numbering…)

Gives every document in the tree a number and writes it to the chosen iProperties (default **Part Number**; any
built-in or custom iProperty, several separated by `;`). The window shows the current and the new number of each
document before **Apply** writes and saves them.

Default pattern `{order}.{asm}.{seq}-{type}`, e.g. for order 160261777:

```
1777.000.000-S   main assembly
1777.000.001-Z   bent part directly in the main assembly
1777.100.000-S   sub-assembly            (100 … 900)
1777.100.001-P   flat part in it
1777.110.000-S   sub-sub-assembly        (110 … 190)
1777.111.000-S   next level              (111 … 119)
1777.111.001-D   lathe part in it
```

- `{order}` – the last digits of the order number (4 by default, 0 = all), `{asm}` – assembly code, `{seq}` – 000 for
  the assembly itself, then 001, 002 … in tree order, `{type}` – type letter, `{name}` – file name.
- Type letters (configurable): S assembly, Z bent, P flat, D lathe, B tube, G general.
- **Assembly levels** (1–6, default 3) sets how many sub-assembly levels get a code of their own; `{asm}` has as many
  digits. With 4: `1000` … `9000`, `1100` … `1900`, `1110` … `1190`, `1111` … `1119`.
- Deeper assemblies, or a tenth one on a level, are numbered inside the assembly above them.
- A part used in several assemblies is numbered in the first one. Content Center and read-only files are skipped;
  any row can be unticked.
- **Keep numbers that already fit**: documents that already have a number of this order keep it, so new parts get the
  free numbers and released numbers do not move. Untick it to renumber from scratch (closes the gaps of unticked rows).
- **File name back for unticked rows**: unticked documents that still carry a number of this order get their file name
  as number again (Inventor's default part number). **Refresh** reads the iProperties from the files again.

## Settings in the window

**Sheets for** (always shown, below the buttons): one tick per document type, with the number of documents. Switching a
type off (e.g. Tube / profile, General) unticks all its documents, and they stay unticked when an assembly is opened
next time. A single document can still be ticked by hand. Switching the type on ticks them again. Opened from a
drawing, the drawing's sheets decide what is ticked.

The settings below are folded away under **▸ Settings** at the bottom of the left panel (click to unfold; the window
remembers it).

- **Columns…** – iProperty columns in the tree.
- The iProperty fields have a **…** list of the standard iProperties and the custom ones in the documents; typing a
  name works too. Defaults: `ordernummer` for the drawing, `Part Number` for the sheet names.

- **Model orientation** – Z up (models standing on the XY plane, the default) or Y up (Inventor's default). All views,
  the iso view included, are made with this axis pointing up.
- **Name sheets after iProperty** – sheets are named after this iProperty of their document (default Part Number);
  empty, or empty in the document: the file name.
- **Order number also into drawing iProperty** – the new drawing gets the order number in this iProperty
  (default `ordernummer`), so the title block shows it. Empty: none.
- **iLogic rules to run on the new drawing** – external rule names, separated by `;`, run after saving; the drawing is
  saved again afterwards.

## Sheets

Order: main assembly → sub-assemblies → bent → flat → lathe parts → tubes → general,
by part number within each type (the file name when it has none). Content Center parts are in a closed, unticked
group at the bottom.

| Type | Sheet |
|---|---|
| Assembly (≤ 15 rows) | front, top, side, iso with balloons, parts list on the title block, overall dimensions |
| Assembly (> 15 rows) | sheet 1: iso + parts list + balloons; sheet 2: front/top/side with overall dimensions; sheets 3+: one front view |
| Bent (sheet metal with bends) | bent front/top/side + iso, flat pattern with edge view: overall, chain bend dimensions, bend notes, hole notes |
| Flat (sheet metal without bends) | flat pattern + edge view (thickness), overall dimensions, hole notes |
| Lathe | side view with the axis horizontal, end view, iso; length and Ø |
| Tube / profile | long view, end view, iso; length and profile size (Ø for round tubes) |
| General | front, top, side, iso; overall dimensions |

Scales are standard scales picked to fit; the sheet size comes from the template.

## Install

Run `AutoDraw-Setup-<version>.exe` (built into `dist\` by `build-installer.ps1`, needs Inno Setup 6). It installs for the
current user into `%APPDATA%\Autodesk\ApplicationPlugins\AutoDraw.bundle`; close Inventor first.

For development:

```
powershell -ExecutionPolicy Bypass -File install.ps1            # build and install for this user
powershell -ExecutionPolicy Bypass -File build-installer.ps1    # build dist\AutoDraw-Setup-<version>.exe
dotnet run --project tests\CoreTests.csproj -c Release           # sheet order, naming, scales and layout tests
```

`tools\make-artwork.ps1` redraws the logo, icon and wizard images; `tools\Probe` opens a drawing in Inventor (a hidden
instance when none runs) to inspect its views and test dimensions, without saving.

Settings: `%APPDATA%\AutoDraw\settings.json`; log: `%LOCALAPPDATA%\AutoDraw\log.txt`.

## License

GPL-3.0, see [LICENSE](LICENSE). The caret icons are from Autodesk's Weave design system (Apache-2.0), see
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
