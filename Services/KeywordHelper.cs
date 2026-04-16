using LibRecord.Models;

namespace LibRecord.Services;

public static class KeywordHelper
{
  public static List<string> SplitKeywords(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return [];
    return value
      .Split(',', StringSplitOptions.RemoveEmptyEntries)
      .Select(s => s.Trim())
      .Where(s => s.Length > 0)
      .ToList();
  }

  /// <summary>Normalize to "a, b, c" for storage.</summary>
  public static string NormalizeKeywords(string? raw)
  {
    var parts = SplitKeywords(raw);
    return parts.Count == 0 ? "" : string.Join(", ", parts);
  }

  /// <summary>Distinct options for filter UI (handles comma-separated keyword fields).</summary>
  public static List<string> BuildFilterOptions(FieldDefinition f, List<string> rawLines)
  {
    if (f.Type == FieldType.Boolean)
      return ["true", "false"];

    if (f.Type == FieldType.Text && (f.IsKeywords || rawLines.Any(s => s.Contains(','))))
    {
      var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var s in rawLines)
      {
        foreach (var p in SplitKeywords(s))
          set.Add(p);
      }
      return set.OrderBy(x => x).Take(80).ToList();
    }

    return rawLines
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(x => x)
      .Take(80)
      .ToList();
  }
}
