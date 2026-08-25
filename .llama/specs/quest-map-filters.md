# Spec: QuestMapFilters.razor

Rewrite `src/SPTQuestMap/Components/QuestMapFilters.razor`. The ONLY change is that the
trader and map pickers stop being rows of radio buttons and become two `FilterDropdown`
multi-select components. Everything else is preserved verbatim.

Output the complete file. No explanation.

## The file's frame — read this first

This component receives everything through parameters. It does **NOT** inject anything.

- **No `@inject`.** There is no `IStringLocalizer` here. `Localizer` is a `[Parameter]` of
  type `QuestMapLocalizer`, and you call it as `Localizer["key"]`.
- **No `@page`, no `@namespace`, no `@implements`, no `@code` file-scoped types.**
- The only directive the file may contain is **`@using System.Linq`** on the first line.
  Everything else (`Microsoft.AspNetCore.Components`, `SPTQuestMap.Presentation`,
  `SPTQuestMap.Components`, `SPTQuestMap.Services`) already comes from `_Imports.razor` —
  adding them again is harmless noise, but adding anything ELSE will not resolve.
- `State` is a `[Parameter]` of type **`QuestMapPageState`**. There is no type called
  `QuestMapState` and no `SPTQuestMap.Core.QuestMapState`. Never write a namespace
  qualifier like `SPTQuestMap.Core.` in this file — every type you need is already in
  scope by its bare name.
- After `@using System.Linq`, the file begins directly with `<div class="filterbar">`.

## The @code block — reproduce EXACTLY, with only the two marked type changes

```csharp
@code {
    [Parameter, EditorRequired] public QuestMapPageState State { get; set; } = default!;
    [Parameter, EditorRequired] public QuestMapLocalizer Localizer { get; set; } = default!;
    [Parameter] public EventCallback<bool> ShowAllFutureChanged { get; set; }
    [Parameter] public EventCallback<bool> ShowFinishedChanged { get; set; }
    [Parameter] public EventCallback<bool> RepeatablesChanged { get; set; }
    [Parameter] public EventCallback<bool> LevelEligibleChanged { get; set; }
    [Parameter] public EventCallback<QuestComparisonFilter> ComparisonFilterChanged { get; set; }
    [Parameter] public EventCallback<string> SearchChanged { get; set; }
    [Parameter] public EventCallback<IReadOnlySet<string>> TraderChanged { get; set; }
    [Parameter] public EventCallback<IReadOnlySet<string>> MapChanged { get; set; }

    private Task OnSearchInput(ChangeEventArgs args) => SearchChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);

    private static QuestComparisonFilter[] ComparisonFilters { get; } = Enum.GetValues<QuestComparisonFilter>();

    private string FilterLabel(QuestComparisonFilter filter) => Localizer[$"compare.filter.{filter}"];

    private IReadOnlyList<FilterDropdown.FilterDropdownItem> TraderItems()
        => State.FilterTraders()
            .Select(trader => new FilterDropdown.FilterDropdownItem(
                trader.Id, trader.Name, trader.ImageUrl ?? "/files/trader/avatar/unknown.png"))
            .ToArray();

    private static IReadOnlyList<FilterDropdown.FilterDropdownItem> MapItems(IReadOnlyList<QuestMapReferenceDto> maps)
        => maps.Select(map => new FilterDropdown.FilterDropdownItem(map.Id, map.Name)).ToArray();

    private int FilterCount(QuestComparisonFilter filter) => filter switch
    {
        QuestComparisonFilter.AllQuests => State.ComparableQuestCount,
        QuestComparisonFilter.AllChanges => State.DifferenceCount,
        QuestComparisonFilter.PrimaryOnly => State.ComparisonCounts.GetValueOrDefault(QuestDifferenceCategory.PrimaryOnly),
        QuestComparisonFilter.ComparisonOnly => State.ComparisonCounts.GetValueOrDefault(QuestDifferenceCategory.ComparisonOnly),
        QuestComparisonFilter.Exclusion => State.ComparisonCounts.GetValueOrDefault(QuestDifferenceCategory.Exclusion),
        QuestComparisonFilter.Status => State.ComparisonCounts.GetValueOrDefault(QuestDifferenceCategory.Status),
        QuestComparisonFilter.Objectives => State.ComparisonCounts.GetValueOrDefault(QuestDifferenceCategory.Objectives),
        QuestComparisonFilter.AvailableAfter => State.ComparisonCounts.GetValueOrDefault(QuestDifferenceCategory.AvailableAfter),
        _ => 0,
    };
}
```

`TraderChanged` and `MapChanged` are the two whose type changed to
`EventCallback<IReadOnlySet<string>>`. `TraderItems` and `MapItems` are new. Everything
else in that block is exactly what is there today — do not rename, reorder or "improve" it.

## The markup

Reproduce this markup EXACTLY. The only region you compose yourself is the one marked
`<!-- REPLACE -->`.

```razor
@using System.Linq

<div class="filterbar">
    <div class="filter-tools">
        <div class="filter-toggles">
            <button class="filter-toggle @(State.ShowAllFuture ? "active" : null)" type="button" aria-pressed="@State.ShowAllFuture.ToString().ToLowerInvariant()" title="@Localizer["showAllFuture"]" aria-label="@Localizer["showAllFuture"]" @onclick="() => ShowAllFutureChanged.InvokeAsync(!State.ShowAllFuture)"><span aria-hidden="true">⇉</span></button>
            <button class="filter-toggle @(State.ShowFinished ? "active" : null)" type="button" aria-pressed="@State.ShowFinished.ToString().ToLowerInvariant()" title="@Localizer["showFinished"]" aria-label="@Localizer["showFinished"]" disabled="@State.FocusMode" @onclick="() => ShowFinishedChanged.InvokeAsync(!State.ShowFinished)"><span aria-hidden="true">✓</span></button>
            <button class="filter-toggle repeatable-toggle @(!State.CompareMode && State.ShowRepeatables ? "active" : null)" type="button" aria-pressed="@(!State.CompareMode && State.ShowRepeatables).ToString().ToLowerInvariant()" title="@Localizer[State.CompareMode ? "compare.repeatablesUnavailable" : "showRepeatables"]" aria-label="@Localizer[State.CompareMode ? "compare.repeatablesUnavailable" : "showRepeatables"]" disabled="@(State.FocusMode || State.CompareMode)" @onclick="() => RepeatablesChanged.InvokeAsync(!State.ShowRepeatables)">
                <svg aria-hidden="true" viewBox="0 0 24 24" focusable="false"><path d="M7 2v3M17 2v3M3.5 9h17M5 4h14a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z" /></svg>
            </button>
            <button class="filter-toggle level-toggle @(State.LevelEligibleOnly ? "active" : null)" type="button" aria-pressed="@State.LevelEligibleOnly.ToString().ToLowerInvariant()" title="@Localizer["levelEligible"]" aria-label="@Localizer["levelEligible"]" disabled="@State.FocusMode" @onclick="() => LevelEligibleChanged.InvokeAsync(!State.LevelEligibleOnly)"><span>≤ @(State.Profile?.Level.ToString() ?? "?")</span></button>
        </div>
        <label class="field search-field"><span class="sr-only">@Localizer["search.label"]</span><input type="search" value="@State.Search" placeholder="@Localizer["search.placeholder"]" autocomplete="off" @oninput="OnSearchInput"></label>
    </div>
    <div class="filter-separator" aria-hidden="true"></div>
    <!-- REPLACE: the two dropdowns go here -->
</div>
@if (State.CompareMode)
{
    <div class="comparison-filterbar" role="radiogroup" aria-label="@Localizer["compare.filter.aria"]">
        <span class="comparison-filter-summary"><strong>@State.DifferenceCount</strong> @Localizer["compare.differencesShort"]</span>
        @foreach (var filter in ComparisonFilters)
        {
            var selected = State.ComparisonFilter == filter;
            <button class="comparison-filter @(selected ? "selected" : null)" type="button" role="radio" aria-checked="@selected.ToString().ToLowerInvariant()" disabled="@State.FocusMode" @onclick="() => ComparisonFilterChanged.InvokeAsync(filter)">
                <span>@FilterLabel(filter)</span><b>@FilterCount(filter)</b>
            </button>
        }
    </div>
}
```

## What goes in place of `<!-- REPLACE -->`

Fetch the maps ONCE into a local — `State.FilterMaps()` allocates and sorts on every call:

```razor
@{ var maps = State.FilterMaps(); }
<div class="filter-dropdowns">
    <label class="filter-dropdown-label">
        <span class="sr-only">@Localizer["trader.label"]</span>
        <FilterDropdown Items="@TraderItems()"
                        Selected="@State.TraderFilter"
                        AllLabel="@Localizer["trader.allShort"]"
                        SearchPlaceholder="@Localizer["trader.search"]"
                        SelectAllLabel="@Localizer["filter.selectAll"]"
                        ClearLabel="@Localizer["filter.clear"]"
                        SelectedCountFormat="@Localizer["filter.selectedCount"]"
                        CloseLabel="@Localizer["filter.close"]"
                        Disabled="@State.FocusMode"
                        SelectionChanged="TraderChanged" />
    </label>
    @if (maps.Count > 0)
    {
        <label class="filter-dropdown-label">
            <span class="sr-only">@Localizer["map.label"]</span>
            <FilterDropdown Items="@MapItems(maps)"
                            Selected="@State.MapFilter"
                            AllLabel="@Localizer["map.allShort"]"
                            SearchPlaceholder="@Localizer["map.search"]"
                            SelectAllLabel="@Localizer["filter.selectAll"]"
                            ClearLabel="@Localizer["filter.clear"]"
                            SelectedCountFormat="@Localizer["filter.selectedCount"]"
                            CloseLabel="@Localizer["filter.close"]"
                            Disabled="@State.FocusMode"
                            SelectionChanged="MapChanged" />
        </label>
    }
</div>
```

The map dropdown renders only when there is at least one map — with none loaded there is
nothing to pick from.

## About FilterDropdown

It is a sibling component in the same folder; use `<FilterDropdown ... />` with no import.
`FilterDropdownItem` is a record nested inside it, written `FilterDropdown.FilterDropdownItem`.
`State.TraderFilter` and `State.MapFilter` are both `IReadOnlySet<string>` — use `.Count`,
never `.Length`. An empty set means "no filter".

`SelectionChanged="TraderChanged"` passes the `EventCallback` straight through. The types
already match; do not wrap it in a lambda.

## Constraints

- `<button type="button">` on every button.
- ARIA booleans lowercase via `.ToString().ToLowerInvariant()`.
- No hardcoded user-visible English — every string via `Localizer["key"]`.
- No JS interop, no `@inject`.
- Builds clean under `Nullable=enable` with `TreatWarningsAsErrors=true`.
