# 202605 Mock Inventory

`温箱202605/` is a **React / JSX visual mock** that the new Phase 2 WPF client uses as its
**UX contract**: when in doubt about a layout, control style, or interaction flow, the JSX is the
source of truth and the WPF implementation should match it within ≤5% on the design tokens (see
spec §3.4 and §8). The mock is a flat tree of nine source files plus a `styles.css` token sheet
and four HTML preview entry points. There is no build step — the HTML loads React + Babel from a
CDN and `Object.assign(window, …)` at the bottom of each JSX file makes the components globally
visible.

This document maps each component in each JSX file to the Phase 2 Pkg / milestone that consumes
it. Mappings extend the authoritative "Maps to 202605" lines in
`docs/superpowers/specs/2026-05-15-phase2-wpf-client-design.md` §4. Note one spec / source
spelling delta: the spec sometimes writes `ScreenAppFrame` and `ScreenProgramEditor`, but the
actual JSX exports are `AppFrame` and `ScreenProgram`. Both names refer to the same component;
this inventory uses the JSX-export spelling and notes the spec alias.

---

## chart.jsx (273 lines)

Pure SVG chart primitives. No frame, no nav, no `AppFrame`. Imported wherever a trend or program
preview is rendered.

| Component | Lines | Purpose | Phase 2 Pkg & milestone |
|-----------|------|---------|--------------------------|
| `TrendChart` | 6–202 | Multi-series time-series chart with axes, grid, cursor readouts, alarm bands. | Pkg 3 M3.6 (`HistoryTrendView`). Also informs Pkg 1 M1.5 (`SingleDeviceView` mini-chart). |
| `synthCurve` | helper inside `TrendChart` body | Synthesises a smooth curve from segments for the program preview. | Pkg 3 M3.3 / M3.4 (segment-to-curve projection in `ProgramEditorViewModel`). |
| `ProgramPreview` | 204–272 | Renders a Program's segments as a ramp/hold curve with an optional active-segment highlight. | Pkg 3 M3.3 (`ProgramEditorView` preview pane) and Pkg 3 M3.4 (`ProgramExecutionService` runtime indicator). |

Exports (line 273): `TrendChart`, `synthCurve`, `ProgramPreview`.

---

## components-core.jsx (375 lines)

Reusable building blocks shared by every screen. Pkg 1 M1.2 ports these once into
`SiemensS7Demo.Wpf/Controls/` and `SiemensS7Demo.Wpf/ViewModels/Shared/`; later packages reuse the
ported controls.

| Component | Lines | Purpose | Phase 2 Pkg & milestone |
|-----------|------|---------|--------------------------|
| `Icon` | 7–62 | Inline-SVG icon set keyed by `name`. | Pkg 1 M1.2 (translated to `Geometry` resources in `Themes/Icons.xaml`). |
| `TopBar` | 64–126 | Top-of-shell bar with role switcher, clock, alarm badges. | Pkg 1 M1.2 (`Shell.xaml` TopBar). RBAC role switcher fully wired in Pkg 4 M4.3. |
| `SideNav` | 128–176 | Left navigation rail with role-aware item visibility. | Pkg 1 M1.2 (`Shell.xaml` LeftNav). Visibility predicate folded into Pkg 4 M4.3. |
| `StatusBar` | 178–196 | Bottom status bar with key/value items. | Pkg 1 M1.2 (`Shell.xaml` StatusBar); per-screen items injected by the consumer ViewModel. |
| `StatusPill` | 198–210 | Coloured pill for device/alarm status. | Pkg 1 M1.4 (`DeviceCard` status pill) + Pkg 2 M2.3 (alarm level pill). |
| `Sparkline` | 212–245 | Small SVG sparkline rendering. | Pkg 1 M1.4 (overview card mini-trace). |
| `DeviceCard` | 247–351 | 9-grid overview card: status, readings, alarm pip, click target. | Pkg 1 M1.4 (`OverviewView` 9-card grid). |
| `ReadingBlock` | 353–361 | Compact PV/SV pair with unit. | Pkg 1 M1.4 (inside `DeviceCard`) + Pkg 1 M1.5 (inside `SingleDeviceView` reading row). |
| `genSeries` | helper | Generator for fake time-series during mock. | Replaced by `DeviceSessionManager` snapshot stream (Pkg 1 M1.3). No port needed. |

Exports (line 373–375): `Icon`, `TopBar`, `SideNav`, `StatusBar`, `StatusPill`, `Sparkline`,
`DeviceCard`, `genSeries`.

---

## design-canvas.jsx (622 lines)

A scrollable / zoomable design-board harness used to render every screen side-by-side at design
time. **Not a runtime component**. None of this ships in the WPF client.

| Component | Lines | Purpose | Phase 2 Pkg & milestone |
|-----------|------|---------|--------------------------|
| `DC` token registry | 17–55 | Local section/artboard metadata. | N/A — design-tooling only. |
| `DCCtx` | 57 | React context. | N/A. |
| `DesignCanvas`, `DCViewport` | 71–331 | Pannable / zoomable surface for the design board. | N/A. |
| `DCSection` | 333–370 | Groups related screens under a heading. | N/A. |
| `DCArtboard`, `DCArtboardFrame` | 372–462 | Per-screen frame with rename/reorder controls. | N/A. |
| `DCEditable` | 464–478 | Inline editable text used by the design board only. | N/A. |
| `DCFocusOverlay` | 480–605 | Focus-mode overlay that opens one screen full-screen. | N/A. |
| `DCPostIt` | 607–620 | Sticky-note annotation on the design board. | N/A. |

No `Object.assign(window, …)` exports — this file is consumed directly by the design-canvas
HTML previews (`温箱控制系统_standalone*.html`). **Deferred indefinitely** — no Phase 2 or
Phase 3 home; the design canvas exists for the React mock only.

---

## guidance.jsx (139 lines)

User-guidance overlay components: banners, big call-to-action tiles, step indicators, status
explanations. The theme system (Pkg 1 M1.2) ports these as reusable WPF `UserControl`s that
later packages re-use.

| Component | Lines | Purpose | Phase 2 Pkg & milestone |
|-----------|------|---------|--------------------------|
| `GuideBanner` | 14–51 | Dismissible info / warn / success banner with optional CTA buttons. | Pkg 1 M1.2 (`Controls/GuideBanner.xaml`); used by Pkg 4 M4.5 LIMS empty state, Pkg 2 M2.3 alarm panel header. |
| `BigCTA` | 53–66 | Big icon-+-title call-to-action card. | Pkg 1 M1.2 (`Controls/BigCTA.xaml`); used by Pkg 4 M4.2 login flow next-step buttons and Pkg 3 M3.3 editor save/run actions. |
| `HelpQ` | 68–76 | Small `?` button with tooltip. | Pkg 1 M1.2 (`Controls/HelpQ.xaml`); used wherever a field needs a help affordance. |
| `StepBar` | 78–93 | Step indicator (1/2/3 with current highlighted). | Pkg 4 M4.2 (`LoginView` 3-step flow: account → password → shift). |
| `STATUS_EXPLAIN` | 95–102 | Plain-language explanation map for device statuses. | Pkg 1 M1.4 (`DeviceCard` tooltip) + Pkg 1 M1.5 (`SingleDeviceView` status banner). |
| `PlainStatus` | 104–125 | Renders a status with both pill and plain-language explanation. | Pkg 1 M1.5 (`SingleDeviceView` status banner). |
| `EmptyHint` | 127–137 | Empty-state placeholder. | Pkg 2 M2.3 / M2.4 (no-alarms state) + Pkg 3 M3.6 (no-history state) + Pkg 4 M4.5 (no-LIMS-tasks state). |

Exports (line 139): `GuideBanner`, `BigCTA`, `HelpQ`, `StepBar`, `PlainStatus`, `EmptyHint`,
`STATUS_EXPLAIN`.

---

## mock-data.jsx (155 lines)

Static fixtures for the mock. Not consumed at WPF runtime; instead, Pkg 1 M1.6 / Pkg 2 / Pkg 3
acceptance smokes use these shapes as reference for `InMemoryAdapter` fixtures.

| Symbol | Lines | Purpose | Phase 2 Pkg & milestone |
|--------|------|---------|--------------------------|
| `DEVICE_TYPES` | 6–12 | Map of device-type code → display label. | Pkg 1 M1.4 (drives `DeviceCard` icon + label). |
| `INITIAL_DEVICES` | 14–50 | 9-device fixture used by `ScreenOverview`. | Pkg 1 M1.6 E2E smoke (3 InMemory devices follow the same shape). |
| `ALARMS` | 52–70 | Alarm fixture list. | Pkg 2 M2.3 / M2.4 acceptance fixtures. |
| `PROGRAM` | 72–85 | Sample multi-segment program. | Pkg 3 M3.3 acceptance fixture (default editor open program). |
| `LIMS_TASKS` | 87–104 | LIMS task fixture grouped by todo/running/done/cancelled. | Pkg 4 M4.5 acceptance fixture (4-tab list). |
| `NAV_ITEMS` | 106–120 | Nav-item metadata used by `SideNav`. | Pkg 1 M1.2 (`Shell.xaml` LeftNav item source). |
| `STATUS_META` | 122–149 | Status code → colour / label / icon. | Pkg 1 M1.2 (token map) + Pkg 1 M1.4 (`DeviceCard`) + Pkg 1 M1.5 (`SingleDeviceView`). |
| `fmtDuration`, `fmtHMS`, `fmtNum` | helpers | Display formatters. | Pkg 1 M1.2 (`Converters/` value converters). |

Exports (line 152–155): `DEVICE_TYPES`, `INITIAL_DEVICES`, `ALARMS`, `PROGRAM`, `LIMS_TASKS`,
`NAV_ITEMS`, `STATUS_META`, `fmtDuration`, `fmtHMS`, `fmtNum`.

---

## screens-a.jsx (528 lines)

Login + main app frame + overview + single-device + supporting modals. This is the largest
single-file feature surface in the mock; Pkg 1 ports most of it.

| Component | Lines | Purpose | Phase 2 Pkg & milestone |
|-----------|------|---------|--------------------------|
| `ScreenLogin` | 9–164 | Login screen with 3-step flow (account → password → shift selection), uses `StepBar`, `BigCTA`. | Pkg 4 M4.2 (`LoginView` + `LoginViewModel`). |
| `AppFrame` *(spec alias: `ScreenAppFrame`)* | 166–186 | Shared shell with TopBar + LeftNav + content router. Receives `active` + `children`. | Pkg 1 M1.2 (`Shell.xaml` + `ShellViewModel`). All other `Screen*` components from `screens-a/b/c.jsx` wrap their content in `AppFrame`; the WPF equivalent is `Shell` with a swapped content region. |
| `ScreenOverview` | 188–291 | 9-card device overview grid with status pills, sparklines, alarm pips, click-to-open. | Pkg 1 M1.4 (`OverviewView` + `OverviewViewModel`). |
| `ScreenSingle` | 293–485 | Single-device control screen with PV/SV row, run/pause/stop/reset commands, segment indicator, plain-status banner. | Pkg 1 M1.5 (`SingleDeviceView` + `SingleDeviceViewModel`). |
| `HeroReading` | 487–497 | Large PV-over-SV reading block used by `ScreenSingle`. | Pkg 1 M1.5 (`Controls/HeroReading.xaml`). |
| `ConfirmStopModal` | 499–523 | Confirmation dialog before stopping a running test. | Pkg 1 M1.5 (`Dialogs/ConfirmStopDialog.xaml`); RBAC-gated by Pkg 4 M4.3. |

Exports (line 526–528): `ScreenLogin`, `AppFrame`, `ScreenOverview`, `ScreenSingle`,
`HeroReading`, `ConfirmStopModal`.

> The spec §4 mentions a `ScreenAlarm` "in screens-a.jsx"; the actual export is in `screens-b.jsx`
> at line 398. The Pkg 2 mapping is unchanged.
> The spec §4 mentions a `ScreenLims` "in screens-a.jsx"; the actual export is in `screens-c.jsx`
> at line 522. The Pkg 4 M4.5 mapping is unchanged.

---

## screens-b.jsx (520 lines)

Three full-screen views: the program editor, the history viewer, and the alarm panel.

| Component | Lines | Purpose | Phase 2 Pkg & milestone |
|-----------|------|---------|--------------------------|
| `ScreenProgram` *(spec alias: `ScreenProgramEditor`)* | 9–188 | Program editor: 8-row segment grid, ramp/hold toggle, JMP loop builder, validation, embedded `ProgramPreview`. | Pkg 3 M3.3 (`ProgramEditorView` + `ProgramEditorViewModel`); links to Pkg 3 M3.4 execution engine. |
| `FormField` | 190–198 | Label-+-value pair used by program editor sidebar. | Pkg 3 M3.3 (`Controls/FormField.xaml`). |
| `ScreenHistory` | 200–396 | History trend screen with device/range picker, multi-series chart (`TrendChart`), pan/zoom controls. | Pkg 3 M3.6 (`HistoryTrendView` + `HistoryTrendViewModel`). |
| `ScreenAlarm` | 398–518 | Tabbed alarm panel: current vs history, filter by device / level / date, ack / reset / mute commands. | Pkg 2 M2.3 (current tab) + Pkg 2 M2.4 (history tab). Mute is Pkg 2 M2.5 surface. |

Exports (line 520): `ScreenProgram`, `ScreenHistory`, `ScreenAlarm`.

> The spec §4 also notes a "popup mock in screens-b.jsx" for `ScreenAlarm`. There is no
> stand-alone popup component in this file; the modal mock is embedded inside `ScreenAlarm` (the
> "active alarm" highlight). Pkg 2 M2.5 implements the stand-alone `AlarmPopupWindow` from
> scratch using the same visual language.

---

## screens-c.jsx (632 lines)

Device-config / layout / maintenance / LIMS screens. The first three are RBAC-gated and require
Pkg 4 M4.3 to be live before they become reachable.

| Component | Lines | Purpose | Phase 2 Pkg & milestone |
|-----------|------|---------|--------------------------|
| `PLC_FAMILIES` | 10–14 | Constant map: vendor → list of supported PLC models. | Pkg 4 M4.3 (drives `DeviceEditView` vendor / model dropdowns). |
| `DEVICE_ROWS` | 16–26 | Fixture for the device-config table. | Pkg 4 M4.3 acceptance fixture. |
| `PROTO_BADGE` | 28–32 | Map: protocol → small badge label. | Pkg 4 M4.3 (`Controls/ProtocolBadge.xaml`). |
| `ScreenDevice` | 34–276 | Device-config table: per-row vendor, PLC family, IP, port, status; "add device" CTA; RBAC-gated. | Pkg 4 M4.3 (`DeviceConfigView` + VM, gated by `[RequiresRole(Admin)]`). The initial connect-only flow appears earlier in Pkg 1 M1.3 to feed `DeviceSessionManager`. |
| `Field` | 278–286 | Small label / value pair used by the device + layout screens. | Pkg 4 M4.3 (`Controls/Field.xaml`). |
| `ScreenLayout` | 288–386 | Lab-layout drag-and-drop screen (bay grid). | **Deferred to Phase 3** — not in scope for Phase 2 (the 202604 legacy has no equivalent and spec §2 Non-Goals excludes "Multi-window dock layouts beyond what 202605 mock prescribes"). The screen is treated as a Phase 3 enhancement once Pkg 4 RBAC lands. |
| `ScreenMaint` | 388–516 | Maintenance dashboard: log filters, last-restart, health checks. | **Deferred to Phase 3** (spec §9 Open Questions — admin / diagnostics surface). |
| `ScreenLims` | 522–586 | LIMS task list with 4 tabs (todo / running / done / cancelled), filter by device / project / status. | Pkg 4 M4.5 (`LimsView` + `LimsViewModel`). |
| `LimsTaskCard` | 588–630 | Per-task card used inside `ScreenLims`. | Pkg 4 M4.5 (`Controls/LimsTaskCard.xaml`). |

Exports (line 632): `ScreenDevice`, `ScreenLayout`, `ScreenMaint`, `ScreenLims`.

---

## styles.css (565 lines)

Authoritative token sheet. CSS custom properties under `:root` define every colour, typography,
spacing, radius, and motion value. Pkg 1 M1.2 ports this mechanically via `tools/CssToXaml.ps1`
into `Themes/Tokens.xaml` (spec §3.4). Token keying convention: `--bg-1` → `BrushBg1`, `--cyan`
→ `BrushCyan`, etc. Every CSS custom property must have a matching XAML brush; this is asserted
by `Wpf.Tests/Themes/TokensTests.cs` (Pkg 1 acceptance gate).

Token categories (line ranges approximate; verify against the file when editing):
- Background / surface / border / divider brushes.
- Foreground / secondary / muted / inverse text.
- Status palette: `--run`, `--idle`, `--alarm`, `--warn`, `--info`, `--scheduled`, `--paused`,
  `--offline`.
- Accent palette: `--cyan`, `--violet`, `--lime`, `--amber`, `--magenta`.
- Typography: families (`--ff-sans`, `--ff-mono`), sizes (`--num-xl`/`-lg`/`-md`/`-sm`),
  weights, letter-spacings.
- Spacing scale (`--s-1` … `--s-8`).
- Radius scale (`--r-sm`, `--r-md`, `--r-lg`).
- Motion / easing tokens.

Phase 2 home: Pkg 1 M1.2 (`Themes/Tokens.xaml` generated via `tools/CssToXaml.ps1`).

---

## HTML preview entry points

These wire the JSX files together for visual review. They have no runtime equivalent in WPF —
the WPF client is its own host.

| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `温箱控制系统.html` | Live React preview with all screens accessible via nav. | Reference only. |
| `温箱控制系统 v1 深色.html` | Earlier dark-mode snapshot. | Reference only. |
| `温箱控制系统-print.html` | Print-friendly variant. | Reference only — spec does not target print. |
| `温箱控制系统_standalone.html` | Single-file build with inlined deps. | Reference only. |
| `温箱控制系统_standalone_src.html` | Single-file build with source maps. | Reference only. |

---

## Component → Phase 2 mapping summary

| 202605 component | jsx file | Phase 2 Pkg & milestone |
|------------------|----------|--------------------------|
| `TrendChart` | chart.jsx | Pkg 3 M3.6 |
| `synthCurve` | chart.jsx | Pkg 3 M3.3 / M3.4 |
| `ProgramPreview` | chart.jsx | Pkg 3 M3.3 / M3.4 |
| `Icon` | components-core.jsx | Pkg 1 M1.2 |
| `TopBar` | components-core.jsx | Pkg 1 M1.2 (Shell), Pkg 4 M4.3 (RBAC role switch) |
| `SideNav` | components-core.jsx | Pkg 1 M1.2 (Shell), Pkg 4 M4.3 (RBAC visibility) |
| `StatusBar` | components-core.jsx | Pkg 1 M1.2 |
| `StatusPill` | components-core.jsx | Pkg 1 M1.4 + Pkg 2 M2.3 |
| `Sparkline` | components-core.jsx | Pkg 1 M1.4 |
| `DeviceCard` | components-core.jsx | Pkg 1 M1.4 |
| `ReadingBlock` | components-core.jsx | Pkg 1 M1.4 + Pkg 1 M1.5 |
| `genSeries` | components-core.jsx | N/A — replaced by `DeviceSessionManager` |
| `DC*` family | design-canvas.jsx | N/A — design-tooling only |
| `GuideBanner` | guidance.jsx | Pkg 1 M1.2 (reusable) |
| `BigCTA` | guidance.jsx | Pkg 1 M1.2 (reusable), used by Pkg 4 M4.2, Pkg 3 M3.3 |
| `HelpQ` | guidance.jsx | Pkg 1 M1.2 (reusable) |
| `StepBar` | guidance.jsx | Pkg 4 M4.2 |
| `STATUS_EXPLAIN` | guidance.jsx | Pkg 1 M1.4 + M1.5 |
| `PlainStatus` | guidance.jsx | Pkg 1 M1.5 |
| `EmptyHint` | guidance.jsx | Pkg 2 M2.3 / M2.4 + Pkg 3 M3.6 + Pkg 4 M4.5 |
| `DEVICE_TYPES` | mock-data.jsx | Pkg 1 M1.4 reference |
| `INITIAL_DEVICES` | mock-data.jsx | Pkg 1 M1.6 fixture reference |
| `ALARMS` | mock-data.jsx | Pkg 2 M2.3 / M2.4 fixture reference |
| `PROGRAM` | mock-data.jsx | Pkg 3 M3.3 fixture reference |
| `LIMS_TASKS` | mock-data.jsx | Pkg 4 M4.5 fixture reference |
| `NAV_ITEMS` | mock-data.jsx | Pkg 1 M1.2 |
| `STATUS_META` | mock-data.jsx | Pkg 1 M1.2 (token map) + Pkg 1 M1.4 + Pkg 1 M1.5 |
| `fmtDuration`, `fmtHMS`, `fmtNum` | mock-data.jsx | Pkg 1 M1.2 (converters) |
| `ScreenLogin` | screens-a.jsx | Pkg 4 M4.2 |
| `AppFrame` *(spec: `ScreenAppFrame`)* | screens-a.jsx | Pkg 1 M1.2 (Shell) |
| `ScreenOverview` | screens-a.jsx | Pkg 1 M1.4 |
| `ScreenSingle` | screens-a.jsx | Pkg 1 M1.5 |
| `HeroReading` | screens-a.jsx | Pkg 1 M1.5 |
| `ConfirmStopModal` | screens-a.jsx | Pkg 1 M1.5 (Pkg 4 M4.3 gating) |
| `ScreenProgram` *(spec: `ScreenProgramEditor`)* | screens-b.jsx | Pkg 3 M3.3 |
| `FormField` | screens-b.jsx | Pkg 3 M3.3 |
| `ScreenHistory` | screens-b.jsx | Pkg 3 M3.6 |
| `ScreenAlarm` | screens-b.jsx | Pkg 2 M2.3 / M2.4 |
| `PLC_FAMILIES`, `DEVICE_ROWS`, `PROTO_BADGE` | screens-c.jsx | Pkg 4 M4.3 reference |
| `ScreenDevice` | screens-c.jsx | Pkg 4 M4.3 (initial subset in Pkg 1 M1.3 for `DeviceSessionManager` config) |
| `Field` | screens-c.jsx | Pkg 4 M4.3 |
| `ScreenLayout` | screens-c.jsx | **Deferred to Phase 3** (spec §2 Non-Goals; §9 Open Questions) |
| `ScreenMaint` | screens-c.jsx | **Deferred to Phase 3** (spec §9 Open Questions) |
| `ScreenLims` | screens-c.jsx | Pkg 4 M4.5 |
| `LimsTaskCard` | screens-c.jsx | Pkg 4 M4.5 |
| `styles.css` (all tokens) | styles.css | Pkg 1 M1.2 (`Themes/Tokens.xaml`) |
| HTML preview files | (top-level) | Reference only |

---

## Deferred components

The following 202605 components have no Phase 2 home. Each cites spec §9 Open Questions or §2
Non-Goals of `docs/superpowers/specs/2026-05-15-phase2-wpf-client-design.md`.

- `ScreenLayout` (screens-c.jsx). Lab-layout drag-and-drop UI. Phase 2 spec §2 Non-Goals
  excludes "Multi-window dock layouts beyond what 202605 mock prescribes", and 202604 has no
  equivalent; treated as a Phase 3 enhancement once Pkg 4 RBAC lands.
- `ScreenMaint` (screens-c.jsx). Maintenance dashboard. No Phase 2 milestone owns the admin /
  diagnostics surface; deferred per spec §9 Open Questions ("CenterWindow / FileManager /
  TaskSerivce inheritance: deferred ... revisit after Phase 2 ships").
- All `DC*` components in `design-canvas.jsx`. Design-tooling for the React mock only; no
  runtime equivalent.
- `genSeries` (components-core.jsx). Synthetic-data helper; replaced by `DeviceSessionManager`
  snapshot stream from `SiemensS7Demo.Core`.
- HTML preview files (`温箱控制系统*.html`). React preview hosts; replaced by the WPF
  application shell.

These items remain in the mock as authoritative UX references but are not in the Phase 2 build
plan.
