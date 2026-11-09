# AutoDraw for Inventor 2025

Makes the production drawing of an assembly: one `.idw` with a sheet for every part and sub-assembly, views per
part type, parts lists with balloons, overall dimensions and, on flat patterns, bend dimensions and bend notes.

## Use

1. Open the main assembly and click **AutoDraw** on the **Assemble** tab.
2. The AutoDraw window opens (a normal window: move it to a second screen if you like).
3. Check the **order number** (`ordernummer` iProperty of the assembly) and choose the **drawing template** once.
4. In the tree: tick the documents that get a sheet, change a detected **Type** if it is wrong, edit **Aantal**, the
   **Sheets** of an assembly or any added iProperty column (**Columns…**).
5. **Generate drawing**. The drawing is saved next to the assembly as `<ordernummer>_<assembly>_<y-M-d>.idw`
   (`_2`, `_3` … when that name is taken) and opened.

## What it writes

- `Aantal` – total quantity of the document in the main assembly (or the value typed in the tree).
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
1777.000.001-Z   folded part directly in the main assembly
1777.100.000-S   sub-assembly            (100 … 900)
1777.100.001-P   flat part in it
1777.110.000-S   sub-sub-assembly        (110 … 190)
1777.111.000-S   next level              (111 … 119)
1777.111.001-D   lathe part in it
```

- `{order}` – the last digits of the order number (4 by default, 0 = all), `{asm}` – assembly code, `{seq}` – 000 for
  the assembly itself, then 001, 002 … in tree order, `{type}` – type letter, `{name}` – file name.
- Type letters (configurable): S assembly, Z folded, P flat, D lathe, B tube, G general.
- Deeper assemblies, or a tenth one on a level, are numbered inside the assembly above them.
- A part used in several assemblies is numbered in the first one. Content Center and read-only files are skipped;
  any row can be unticked.
- **Keep numbers that already fit**: documents that already have a number of this order keep it, so new parts get the
  free numbers and released numbers do not move. Untick it to renumber from scratch (closes the gaps of unticked rows).
- **File name back for unticked rows**: unticked documents that still carry a number of this order get their file name
  as number again (Inventor's default part number). **Refresh** reads the iProperties from the files again.

## Settings in the window

- **Model orientation** – Z up (models standing on the XY plane, the default) or Y up (Inventor's default). All views,
  the iso view included, are made with this axis pointing up.
- **Order number also into drawing iProperty** – the new drawing gets the order number in this iProperty
  (default `ordernummer`), so the title block shows it. Empty: none.
- **iLogic rules to run on the new drawing** – external rule names, separated by `;`, run after saving; the drawing is
  saved again afterwards.

## Sheets

Order: main assembly → sub-assemblies → sheet metal folded → sheet metal flat → lathe parts → tubes → general,
alphabetical within each type. Content Center parts are in a closed, unticked group at the bottom.

| Type | Sheet |
|---|---|
| Assembly (≤ 15 rows) | front, top, side, iso with balloons, parts list on the title block, overall dimensions |
| Assembly (> 15 rows) | sheet 1: iso + parts list + balloons; sheet 2: front/top/side with overall dimensions; sheets 3+: one front view |
| Sheet metal folded | folded front/top/side + iso, flat pattern with edge view: overall, chain bend dimensions, bend notes, hole notes |
| Sheet metal flat | flat pattern + edge view (thickness), overall dimensions, hole notes |
| Lathe part | side view with the axis horizontal, end view, iso; length and Ø |
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
