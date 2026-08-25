# Spec: FilterDropdown.razor

Write a single Blazor component file: `src/SPTQuestMap/Components/FilterDropdown.razor`.
It is an Excel-style multi-select filter: a button showing the current selection, which
opens a panel containing a search box, Select-All / Clear actions, and a checkbox list.

Output the complete file. No explanation, no markdown fence commentary.

## Namespace and usings

The file needs `@using System.Collections.Generic` and `@using System.Linq` at the top.
No `@namespace` directive. No `@inject`. No JavaScript interop of any kind.

## Nested record

Declare inside the `@code` block, exactly:

```csharp
public sealed record FilterDropdownItem(string Id, string Label, string? ImageUrl = null);
```

## Parameters — exact names, types and modifiers

Every one of these is `[Parameter]`. The ones marked EditorRequired use
`[Parameter, EditorRequired]` and must be initialised with `= default!;` because the
project builds with `Nullable=enable` and `TreatWarningsAsErrors=true`; a plain
non-nullable auto-property produces CS8618 and fails the build.

| Property | Type | Attribute | Initialiser |
| --- | --- | --- | --- |
| `Items` | `IReadOnlyList<FilterDropdownItem>` | `[Parameter, EditorRequired]` | `= default!;` |
| `Selected` | `IReadOnlySet<string>` | `[Parameter, EditorRequired]` | `= default!;` |
| `AllLabel` | `string` | `[Parameter, EditorRequired]` | `= default!;` |
| `SearchPlaceholder` | `string` | `[Parameter, EditorRequired]` | `= default!;` |
| `SelectAllLabel` | `string` | `[Parameter, EditorRequired]` | `= default!;` |
| `ClearLabel` | `string` | `[Parameter, EditorRequired]` | `= default!;` |
| `SelectedCountFormat` | `string` | `[Parameter, EditorRequired]` | `= default!;` |
| `CloseLabel` | `string` | `[Parameter, EditorRequired]` | `= default!;` |
| `Disabled` | `bool` | `[Parameter]` | (none) |
| `SelectionChanged` | `EventCallback<IReadOnlySet<string>>` | `[Parameter]` | (none) |

**Never assign to a `[Parameter]` property from inside the component.** Blazor overwrites
them on every render and mutating them is a framework violation. In particular `Selected`
is read-only input: to change the selection, build a NEW `HashSet<string>` and pass it to
`SelectionChanged.InvokeAsync(...)`. The parent owns the state and passes it back down.

`SelectionChanged` is an `EventCallback<T>`, which is a **struct**, not a delegate. Write
`SelectionChanged.InvokeAsync(x)`. Writing `SelectionChanged?.InvokeAsync(x)` is CS0023
and will not compile.

## Private state

Two private fields only: a `bool` for whether the panel is open, and a `string` for the
current search term (initialised to `string.Empty`).

## Behaviour

### Button

A `<button type="button">` with class `filter-dropdown-button`. Always set `type="button"`
— a bare `<button>` inside a form defaults to submit. It carries:

- `disabled="@Disabled"`
- `aria-expanded` set to the open flag, **lowercase** — use
  `@IsOpen.ToString().ToLowerInvariant()`. ARIA requires literal `"true"`/`"false"`;
  .NET's `Boolean.ToString()` yields `"True"`, which is invalid.
- `aria-haspopup="true"`. Do **not** use `aria-haspopup="listbox"` — the panel is a
  checkbox list, not an ARIA listbox, and claiming listbox without `role="listbox"` and
  `role="option"` on the children is a false promise to a screen reader.

Its text is the summary:

- `Selected.Count == 0` → `AllLabel`
- `Selected.Count == 1` → the `Label` of the item in `Items` whose `Id` equals the single
  selected id; if no such item exists, fall back to `AllLabel`
- otherwise → `string.Format(SelectedCountFormat, Selected.Count)`

`SelectedCountFormat` is a localized format string containing `{0}`, e.g. `"{0} selected"`.
**Never build this string with interpolation or concatenation of English words** — every
user-visible string in this project comes from a localized parameter.

Clicking the button toggles the panel open/closed. When it CLOSES for any reason, reset the
search term to `string.Empty`, so reopening does not show a stale filtered list.

### Panel

Rendered only when open, in a `<div class="filter-dropdown-panel">`.

1. A search `<input type="search" class="filter-dropdown-search">` bound to the search
   term with `@bind` plus `@bind:event="oninput"` so it filters as the user types, and
   `placeholder="@SearchPlaceholder"`.
   Note `@bind` requires a settable property or field — you cannot `@bind` to a method
   call; `@bind="SomeMethod(x)"` is CS0131.
2. A `<div class="filter-dropdown-actions">` with two `<button type="button">` elements
   labelled `SelectAllLabel` and `ClearLabel`.
3. A `<ul class="filter-dropdown-list">`. One `<li class="filter-dropdown-item">` per
   VISIBLE item. Each `<li>` contains a `<label>` wrapping, in order:
   - `<input type="checkbox">` whose `checked` reflects whether that item's `Id` is in
     `Selected`, with an `@onchange` handler that toggles that id
   - if `ImageUrl` is not null, an `<img src="@item.ImageUrl" alt="" />` — the alt is
     deliberately empty because the adjacent label text already names the item
   - the item's `Label` text
4. A final `<button type="button" class="filter-dropdown-close">` labelled `CloseLabel`
   that closes the panel. This is the keyboard-reachable way out.

Wrap everything in an outer `<div class="filter-dropdown">`.

### Visible items

Items whose `Label` contains the search term, case-insensitively, using
`IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0`. An empty or whitespace-only
search term shows every item. Do not use `ToLower()` for this comparison.

### Toggling one item

Copy `Selected` into a new `HashSet<string>` using `StringComparer.Ordinal`, add or remove
the id, then `SelectionChanged.InvokeAsync(newSet)`. Ids are opaque identifiers, so the
comparison is ordinal, never culture-sensitive.

### Select All

Union the CURRENTLY VISIBLE items' ids into a copy of `Selected` and raise
`SelectionChanged`. Scoping to visible items is deliberate: with a search term active,
"select all" means "all the ones I am looking at", not all items in existence.

### Clear

Raise `SelectionChanged` with a new EMPTY `HashSet<string>`. Clear applies to the whole
selection, not just visible items — an empty set means "no filter", which is the
component's neutral state.

### Escape key

Put `@onkeydown` on the OUTER `<div class="filter-dropdown">` so it fires for the button
and anything in the panel. When the key is `"Escape"` and the panel is open, close it and
reset the search term. Use `KeyboardEventArgs` (available via the project's global usings)
and compare `args.Key` with `"Escape"` using `StringComparison.Ordinal`.

Do **not** attempt click-outside-to-close. That requires a document-level listener and
therefore JS interop, which this project forbids in the Blazor Server components. The
Escape key and the explicit close button are the two ways out, which is why `CloseLabel`
exists.

## Constraints recap

- No JS interop, no `IJSRuntime`, no `@inject`.
- No hardcoded user-visible English anywhere — every string comes from a parameter.
- Do not mutate any `[Parameter]`.
- `<button type="button">` on every button.
- Lowercase ARIA boolean values.
- Builds clean under `Nullable=enable` and `TreatWarningsAsErrors=true`.
