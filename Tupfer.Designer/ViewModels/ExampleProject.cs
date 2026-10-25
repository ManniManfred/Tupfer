namespace Tupfer.Designer.ViewModels;

/// <summary>
/// Mitgeliefertes Beispielprojekt für „Datei → Beispiele“.
/// <paramref name="FetchAsync"/> legt die Dateien lokal ab und liefert den Pfad der Projekt- oder YAML-Datei.
/// </summary>
public record ExampleProject(string Id, string Title, Func<Task<string>> FetchAsync);
