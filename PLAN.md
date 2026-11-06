# AutoDraw — Plan (draft 4)

Inventor 2025 add-in that generates drawings for every part and sub-assembly of the open assembly,
with a drawing layout per part type and automatic dimensions.

Clean-room: the workflow is inspired by Bas Automation "Drawing Maker", but no code, names,
strings or layout are copied. UI styling reuses the Tab and Slot pieces (Theme, SectionHeader,
FieldBox, InventorButton).

Status: **planning** — nothing is built yet. Items marked ❓ are still open.

---

## 1. Decisions so far

| # | Topic | Decision |
|---|---|---|
| A1 | Repo | `Documents\GitHub\Inventor-AutoDraw`, local git only for now |
| A2 | Ribbon | One **AutoDraw** button, **assembly environment only** |
| A3 | Window | Full, separate, non-modal window (second monitor ok), remembers size/position, maximisable |
| A4 | Start | Loads the active assembly's full structure immediately |
| B5 | List | **Tree** (assembly structure), **fully expanded** by default, sorted top-down (see §2) |
| B6 | Content Center | Separate **"Content Center" group at the bottom, collapsed** and unchecked |
| B7 | Existing drawing | **Ask once per run** (overwrite all / skip all) when one or more .idw files exist |
| B8 | Columns | Configurable; **Aantal** is a custom iProperty — user picks any custom iProperty as a column from the window itself; iProperty columns are **editable** (written back to the part) |
| B9 | Override | Per row: override the detected type / layout |
| C10 | Sheet metal, folded | Drawing with **flat pattern + folded views on one sheet** |
| C10 | Sheet metal, flat (no bends) | Drawing with **flat pattern only** |
| C11 | Tubes/profiles | Mostly Frame Generator, not always → detect FG first, geometry as fallback |
| C12 | Lathe parts | Detected from geometry |
| C13 | General part | Own layout for parts that are none of the above; plates are always sheet metal, so an extruded plate is General |
| C14/15 | Other types | No weldments, no other types |
| D | Layout defaults | All accepted (see §4); **no automatic sheet size** — sheet comes from the template; **scale auto-detected** from part size |
| D20 | Sort order | Within each assembly level: sub-assemblies first (deeper levels below), then SM folded → SM flat → lathe → tubes → general; Content Center last |
| D20 | Parts list | **Only on assembly drawings**, **top level** (that assembly's own level) |
| E26 | Dimensions v1 | **Overall dimensions** everywhere; flat pattern: **edge → first bend, then bend → bend (chain)**, bend direction up/down + angle |
| E27 | Hole dimension style | Not now |
| E28 | Flat pattern extras | Yes: overall + bend positions + thickness/material note |

---

## 2. Main window

```
┌─ AutoDraw ─ <assembly name> ──────────────────────────────────────── [⚙] ─┐
│ ┌─ Left panel ──────────┐ ┌─ Document tree ──────────────────────────────┐ │
│ │ Source                │ │ [✓] Name        Type        Aantal  Mat.  St │ │
│ │  Assembly: X.iam      │ │ ▼[✓] Frame.iam   Assembly     1     -     ●  │ │
│ │  [Reload]             │ │   ▼[✓] Sub.iam   Assembly     2     -     ●  │ │
│ │                       │ │      [✓] Plate   SM flat      4    S235   ●  │ │
│ │ Layout per type       │ │      [✓] Bracket SM folded    2    S235   ●  │ │
│ │  Assembly   [▼]       │ │   [✓] Tube 40x2  Tube         6    S235   ●  │ │
│ │  SM folded  [▼]       │ │   [✓] Shaft      Lathe        1    C45    ●  │ │
│ │  SM flat    [▼]       │ │   [ ] M8 bolt    General      8    -      ○  │ │
│ │  Tube       [▼]       │ │                                              │ │
│ │  Lathe      [▼]       │ │ Filter: [All types ▼]  Search: [________]    │ │
│ │  General    [▼]       │ │                                              │ │
│ │                       │ │                                              │ │
│ │ Preview / properties  │ │                                              │ │
│ │  [thumbnail]          │ │                                              │ │
│ └───────────────────────┘ └──────────────────────[ Generate drawings ]───┘ │
│ Status bar: progress / last message                                         │
└─────────────────────────────────────────────────────────────────────────────┘
```

- Tree = assembly structure, expanded. Each unique file appears where it sits in the structure;
  a file that occurs more than once is generated **once** (dedupe by full path).
- Sort inside every assembly node: sub-assemblies → SM folded → SM flat → lathe → tubes → general,
  then by name. Content Center parts are pulled out into one collapsed, unchecked
  **Content Center** group at the very bottom.
- iProperty columns (e.g. Aantal) are edited in place; the value is written to the part's custom
  iProperty and the part is saved with the run (or on "Save properties").
- Checkbox per row; checking a sub-assembly checks its children (tri-state).
- **Type** column = detected type, a dropdown to override (B9).
- **Columns**: fixed (Name, Type, Status, Thumbnail) + configurable iProperty columns
  (e.g. `Aantal`, Material, Thickness, Part Number, Description). Configured in Settings.
- **Status**: ○ no drawing yet, ● drawing exists, ✔ generated this run, ✖ failed (tooltip = reason).
- Selecting a row shows a thumbnail and key properties in the left panel.

---

## 3. Type detection

Run once per unique document when the tree loads; result is cached and overridable.

| Order | Type | Rule |
|---|---|---|
| 1 | **Assembly** | `.iam` |
| 2 | **Sheet metal – flat** | SheetMetal subtype, flat pattern has **0 bends** |
| 3 | **Sheet metal – folded** | SheetMetal subtype, ≥ 1 bend |
| 4 | **Tube / profile** | Frame Generator member (FG attribute set on the part) — else geometry: constant cross-section along the longest axis of the range box |
| 5 | **Lathe part** | Geometry: all faces are planes ⟂ one axis or cylinders/cones/tori coaxial with that axis (small tolerance) |
| 6 | **General** | Everything else |

- Sheet metal without a flat pattern yet → AutoDraw creates the flat pattern (unfold), and saves.
- Detection results shown in the Type column; wrong result → override per row.

---

## 4. Layout per type (v1)

Common: **first-angle (ISO-E)**, mm, **sheet size from the template** (not changed by AutoDraw),
**ISO standard scales** (…1:10, 1:5, 1:2, 1:1, 2:1, 5:1…), **iso view top right, shaded**,
hole/thread notes on all types, title block filled from iProperties.

| Type | Views | Annotations |
|---|---|---|
| **Assembly** | Front, top, side, iso | Parts list (top level of this assembly) + balloons; overall dimensions |
| **SM folded** | One sheet: flat pattern + front + 1–2 projections + iso | Flat pattern: overall, chain bend dims, bend notes (UP/DOWN, angle, radius); folded views: overall dims; thickness + material note |
| **SM flat** | Flat pattern only (+ small iso optional ❓) | Overall dims; thickness + material note |
| **Tube / profile** | Long view (break view when long), cross-section, end-cut detail when mitred | Overall length (cut angles: later) |
| **Lathe** | Side view with axis horizontal, half section, details for grooves/threads | Overall length + max Ø |
| **General** | Front, top, side, iso | Overall dimensions |

### Scale algorithm (sheet is fixed by the template)
1. Compute the views' model extents for the chosen view set.
2. Try standard scales from largest to smallest; accept the first where all views + dimension
   space fit on the template's sheet outside the title block zone.
3. Fallback: smallest scale, warn in Status.

---

## 5. Auto-dimensioning (v1 scope)

### 5.1 Overall dimensions (all types)
- Per view: find the extreme drawing curves (leftmost/rightmost, top/bottom linear edges or
  vertices) and add one horizontal + one vertical linear dimension, placed outside the view.
- Front view gets width + height; the side or top view adds the missing depth only (no duplicates).
- Lathe: overall length + largest diameter (Ø dim). Tube: overall length.

### 5.2 Flat pattern (SM folded)
- Overall width + height of the flat.
- **Bend line positions, chain**: for each direction (horizontal / vertical bend lines), one
  dimension chain: flat edge → first bend line → next bend line → … → opposite flat edge.
- **Bend notes** on every bend line: direction **UP/DOWN**, angle, inner radius
  (Inventor bend notes, style from template).
- Bends that are not parallel to an edge: note only, no position dimension (v1).
- Thickness + material note (from model) near the flat view.

### 5.3 Placement rules
- Dimensions outside the view, fixed offset from the view and stacked at a fixed spacing.
- Basic overlap check: if a dimension text overlaps another annotation/view, push it outward.

### Not in v1
Hole position dimensions (ordinate/baseline/hole table), lathe diameter stacks and chamfers,
tube hole positions, retrieve model dimensions.

---

## 6. Architecture (C#, .NET 4.8, WPF)

```
src/
  AddIn/          StandardAddInServer, ribbon button (assembly env), window host
  UI/             MainWindow, DocumentTree (ViewModel), SettingsWindow; shared Tab and Slot theme
  Model/          DocNode (tree), PartKind enum, LayoutPreset, Settings (JSON)
  Detection/      KindDetector: SheetMetal, FrameGen, Lathe, Tube geometry tests
  Layout/         SheetSizer (size+scale search), ViewPlacer, per-kind layout builders
  Dimensioning/   OverallDims, FlatPatternDims, BendNotes, PlacementHelper
  Output/         Naming, save/close, existing-file handling
tests/            Pure-logic tests (sizer, naming, placement math) without Inventor
```

- Generation runs on Inventor's thread (COM), one document at a time, status bar progress,
  **cancel** button, **one transaction per drawing**.
- Settings: JSON in `%APPDATA%\AutoDraw\settings.json` (location ❓ — see H45).

---

## 7. Phasing

| Milestone | Content |
|---|---|
| **M1 – Skeleton** | Repo, add-in, ribbon button (assembly), empty window with theme |
| **M2 – Tree** | Load assembly tree, columns incl. Aantal/iProperties, thumbnails, existing-drawing status |
| **M3 – Detection** | Type detection + override dropdown, test on your assemblies |
| **M4 – Drawings** | Template, scale search, views per type, title block, save |
| **M5 – Dimensions** | Overall dims, flat pattern bend positions + bend notes, thickness/material note |
| **M6 – Polish** | Cancel, error handling, installer to `I:\Jan Blokstra\software\AutoDraw\` |

Each milestone is tested in Inventor before the next one starts.

---

## 8. Open questions

Answered in rounds 3–4: chain bend dims, Aantal = editable custom iProperty column, ask once
per run on existing drawings, SM folded on one sheet, extruded plate = General, tube angles later,
scale auto, tree sort order, Content Center group at bottom, parts list only on assemblies (top level).

Still to answer:
- **F – Template / title block**: path of your .idw template; one template for all types or one
  per type; which iProperties fill the title block.
- **G – Output**: save next to the part as `{FileName}.idw`? PDF / DXF export along with it?
  Leave drawings open or save + close?
- **H – Settings**: stored per user (`%APPDATA%`) or on the `I:\` share; UI and drawing-note
  language (English / Dutch).
- **I – Users**: only you, or colleagues too (installer on the share)?
- **J – Example drawings**: one existing drawing per type as the reference.
