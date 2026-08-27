# Blazor web app UI — invariants that will bite you

**Load this when** editing anything under `src/SPTQuestMap/Presentation/` — Razor components,
`QuestMap.css`, the canvas renderer, or the page-state code that drives them.

The companion file for the other UI is `docs/client-ui-invariants.md`. The two UIs share no
code, so a fix here is a prompt to grep there, and vice versa.

**You can see this UI, so looking is always cheaper than arguing.** Serve it and open Chrome:

```bash
python -m http.server 8731 --bind 127.0.0.1     # then browse 127.0.0.1:8731
```

Chrome automation cannot open `file://` URLs. The live app is at
`https://127.0.0.1:6969/questmap` (self-signed; `curl -k`).

---

## 1. Two traps that make you draw the wrong conclusion

**Grepping served HTML gives false positives.** The stylesheet is inlined into the page, so a
class-name grep matches the CSS rule and reports a removed element as still present. Strip the
style block first:

```python
import re
html = open("out.html", encoding="utf-8").read()
print(re.sub(r'<style id="spt-questmap-styles">.*?</style>', '', html, flags=re.S))
```

**A screenshot is not proof for anything conditional.** `.details.collapsed` reserving 22rem is
invisible whenever a quest happens to be selected — check `getComputedStyle`, not the picture.

---

## 2. Layout

- **`.topbar` is the left sidebar, not a top bar.** `.app-shell` is `grid-template-columns`,
  not rows. Identity (both profiles, swap, language) and every filter stack vertically there so
  the graph gets the window's full height.
- **The side panels are in flow, never absolutely positioned.** `.side-views` is a real
  workspace column, so expanding In Progress or the Catch-Up Planner pushes the graph right
  instead of covering it. `.workspace` is `auto minmax(0,1fr) auto` — panels, graph, details.
- **Panel content scrolls via `flex: 1 1 auto; min-height: 0`, never `height: calc(100% - Npx)`.**
  The old form worked only while the panels were absolutely positioned with a fixed top and
  bottom. As flex children their height is content-driven, so `100%` resolves against `auto` and
  the list runs off the bottom of the page instead of scrolling. `min-height: 0` is the part
  that actually lets a flex child shrink.
- **`.details.collapsed` must stay zero-sized in every breakpoint.** The `max-width: 1050px`
  block sets `min-height: 22rem` on `.details`; without an explicit collapsed override there it
  reserves 22rem for an empty pane.
- **`.filterbar` must keep `overflow: visible`.** It was `hidden` to contain the horizontally
  scrolling trader row the dropdowns replaced. With dropdowns, `hidden` clips the absolutely
  positioned panel to the bar's 78px: only the search box shows and the checkbox list is
  unreachable. The feature builds, tests green, and is unusable. Found only in a browser.
- **The sidebar width is fluid (`clamp(13.5rem, 18vw, 20rem)`), not fixed.** It was 21rem to fit
  the emblem and the 5rem level watermark, both since removed.
- **Removing an element means fixing its grid template in the same edit.** The profile chips
  declared `grid-template-columns: 24px 40px minmax(0,1fr) 18px` for four children; dropping the
  faction emblem left three, so the nickname landed in the 40px emblem slot and rendered as
  about two characters. Same bug twice more in the dropdown rows and the trigger face. **If a
  child goes, count the columns.**

---

## 3. Type and controls

- **A deliberate two-face type system: Georgia for headings, Inter for UI.** Georgia is the
  `h1`, the quest title and the empty-details heading; everything a person *operates* is Inter.
  A control wearing the display face reads as a mistake — the profile selector did, and was
  corrected. Headings use the `font:` **shorthand**, so grepping `font-family:` undercounts and
  makes Georgia look like a one-off. Grep for `font:` too.
- **One chevron for everything that opens.** The profile pickers, the language select and the
  filter dropdowns used three mechanisms — a text glyph, the native select arrow, and a CSS
  `::after`. They now share `--chevron-size`, `--muted` and the same glyph, with the native
  select given `appearance: none` and a `.has-chevron` wrapper. Sidebar controls share
  `--control-h` rather than the 44/34/32px mix they had.
- **No numeric CSS escapes in this stylesheet.** A caret written as a CSS escape sequence lost
  its backslash passing through a shell heredoc and became a literal `0x15` control byte. Paste
  the character itself instead.

---

## 4. Components

- **`FilterDropdown` renders no panel while `Disabled`.** Disabling only the trigger button left
  an already-open panel live during focus mode, where the state setters ignore changes — the
  checkbox moved and nothing filtered.
- **`FilterDropdown` list items need `@key="item.Id"`.** Without it Blazor reuses `<li>` DOM
  positionally, and narrowing the search can carry a checkbox's state onto a different row.
- **The render signature key sorts filter ids before joining** (`FilterKey`). Sets have no
  defined order; hashing one directly makes an identical selection look like a change and forces
  a full graph redraw.

---

## 5. Profile labels

- **Lengths are deliberate: 2 for the compact slot chips, 5 for graph tiles, and the full
  nickname on filter chips when it is 10 characters or shorter.** `ProfileLabelRules.ShortLabel`
  takes the element count; it counts text elements, not chars, so surrogate pairs are not split.
- **Two-letter chips belong only where space is tight** — graph tiles and the comparison table.
  Beside a full nickname they are redundant, so the profile selectors show the name over a small
  "USEC · 8" line instead.
- **Compare-mode slot labels come from `State.PrimaryShortLabel`/`ComparisonShortLabel`, and
  nothing may branch on their text.** `QuestDetails` used to pick the slot CSS class with
  `label == "A"`; the helpers now take `bool isPrimary`. Two profiles can share a two-letter
  prefix ("Scav1"/"Scav2" both give "Sc"), so the label cannot identify the slot.

---

## 6. The canvas renderer

**It is configured by TWO payloads and both must be updated together.** `QuestGraphView` feeds
`initialize`/`refresh` on first paint; `QuestGraphUpdate` feeds `synchronize` on every later
change. Both land in the same `applyConfig`. Adding a field to only one produces a bug that is
invisible on a fresh page load but correct after any filter change — or the reverse. The profile
tile labels live in both.
