# AutoDraw — Plan (draft 2)

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
| B5 | List | **Tree** (assembly structure), **fully expanded** by default |
| B6 | Auto-exclusion | Not needed — no automatic filtering (see ❓1) |
| B8 | Columns | Configurable; includes **Aantal** and any configurable iProperty columns |
| B9 | Override | Per row: override the detected type / layout |
| C10 | Sheet metal, folded | Drawing with **flat pattern + folded views** |
| C10 | Sheet metal, flat (no bends) | Drawing with **flat pattern only** |
| C11 | Tubes/profiles | Mostly Frame Generator, not always → detect FG first, geometry as fallback |
| C12 | Lathe parts | Detected from geometry |
| C13 | General part | Own layout for parts that are none of the above |
| C14/15 | Other types | No weldments, no other types |
| D | Layout defaults | All accepted (see §4) |
| E26 | Dimensions v1 | **Overall dimensions** everywhere; flat pattern: **edge → bend line distances, bend direction up/down + angle** |
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

Common: **first-angle (ISO-E)**, mm, **auto sheet size** (A4 → A3 → A2 → A1: smallest that fits at
a standard scale), **ISO standard scales** (…1:10, 1:5, 1:2, 1:1, 2:1, 5:1…), **iso view top right,
shaded**, hole/thread notes on all types, title block filled from iProperties.

| Type | Views | Annotations |
|---|---|---|
| **Assembly** | Front, top, side, iso | Parts list + balloons; overall dimensions |
| **SM folded** | Flat pattern + front + 1–2 projections + iso | Flat pattern: overall, edge→bend line dims, bend notes (UP/DOWN, angle, radius); folded views: overall dims; thickness + material note |
| **SM flat** | Flat pattern only (+ small iso optional ❓) | Overall dims; thickness + material note |
| **Tube / profile** | Long view (break view when long), cross-section, end-cut detail when mitred | Overall length; cut angles ❓(v1 or v2) |
| **Lathe** | Side view with axis horizontal, half section, details for grooves/threads | Overall length + max Ø |
| **General** | Front, top, side, iso | Overall dimensions |

### Sheet size / scale algorithm
1. Compute the views' model extents for the chosen view set.
2. For each sheet size (smallest first) and each standard scale (largest first): try the layout;
   accept the first combination where all views + spacing fit outside the title block zone.
3. Fallback: largest sheet, smallest scale, warn in Status.

---

## 5. Auto-dimensioning (v1 scope)

### 5.1 Overall dimensions (all types)
- Per view: find the extreme drawing curves (leftmost/rightmost, top/bottom linear edges or
  vertices) and add one horizontal + one vertical linear dimension, placed outside the view.
- Front view gets width + height; the side or top view adds the missing depth only (no duplicates).
- Lathe: overall length + largest diameter (Ø dim). Tube: overall length.

### 5.2 Flat pattern (SM folded)
- Overall width + height of the flat.
- **Bend line positions**: for each direction (horizontal / vertical bend lines), dimension from
  the reference flat edge to each bend line — **baseline or chain** ❓2.
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
| **M4 – Drawings** | Template, sheet size/scale search, views per type, title block, save |
| **M5 – Dimensions** | Overall dims, flat pattern bend positions + bend notes, thickness/material note |
| **M6 – Polish** | Cancel, error handling, installer to `I:\Jan Blokstra\software\AutoDraw\` |

Each milestone is tested in Inventor before the next one starts.

---

## 8. Open questions

1. **B6 auto-exclusion "no need"**: should Content Center bolts/nuts still show up in the tree
   **unchecked** by default, or checked like everything else?
2. **Flat pattern bend dims**: **chain** (bend → bend) or **baseline** (all from one edge)?
   Which edge is the reference: left/bottom?
3. **Aantal**: is it a custom iProperty you fill in, or should AutoDraw compute it from the BOM
   quantity (and maybe write it to the iProperty)?
4. **Existing drawing** found: skip, overwrite, or ask per run? (Default proposal: skip, with an
   "overwrite" checkbox.)
5. **Sub-assemblies**: own drawing each (yes by default)? Parts list top-level only or all levels?
6. **SM folded**: one sheet with flat + folded views, or flat on sheet 1 and folded on sheet 2?
7. **Flat sheets** that are not modelled as sheet metal (extruded plate): treat as General, or
   detect "plate" and give it the flat layout?
8. Tube cut angles: v1 or v2?
9. Still to answer later: F (template / title block), G (output, naming, PDF/DXF), H (settings
   location, language), I (users), J (example drawings).
