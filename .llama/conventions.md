You are writing code for the SPT-QuestMap repository. Follow these conventions exactly.

PROJECT
- .NET 9. Blazor Server (Interactive Server), C#. Razor components under src/SPTQuestMap/Components.
- Pure logic with no framework dependency lives in src/SPTQuestMap.Core (netstandard2.1) and is unit tested.

COMPILER SETTINGS THAT WILL BREAK YOUR CODE IF IGNORED
- <Nullable>enable</Nullable> AND <TreatWarningsAsErrors>true</TreatWarningsAsErrors>.
  A nullable warning is a BUILD FAILURE, not a hint.
- Every required Blazor parameter must be declared:
      [Parameter, EditorRequired]
      public TheType TheName { get; set; } = default!;
  Optional parameters stay plain [Parameter] and need a default or a nullable type.
- Do not leave any non-nullable field or property uninitialised.

BLAZOR RULES
- Never assign to or mutate a [Parameter]. The parent owns that state. Report changes
  through an EventCallback, passing a NEW collection.
- No JavaScript interop unless explicitly asked for.
- Boolean ARIA attributes must render lowercase: @Value.ToString().ToLowerInvariant().
- Use <button type="button"> for in-page actions. Never <a href="#">, which navigates.
- Do not put @onclick:preventDefault on checkboxes; it stops them from checking.

LOCALIZATION
- All user-visible text comes from parameters or the injected Localizer. NEVER hardcode
  an English string in markup. 17 locales are installed and a test enforces translation.

STYLE
- Match the surrounding file's naming, comment density and idiom.
- Comments explain WHY, never restate what the code plainly does.
- Prefer immutable/readonly collection types in signatures (IReadOnlyList, IReadOnlySet).

OUTPUT
- Return only code unless asked otherwise. No markdown fences around the whole answer
  unless the request asks for a file. No explanation before or after.
