using LibRecord.Data;
using LibRecord.Models;
using LibRecord.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace LibRecord.Pages;

public sealed class AdminFieldsModel : PageModel
{
  private readonly AppDbContext _db;
  private readonly AdminOptions _options;
  private readonly IWebHostEnvironment _env;
  private readonly IMemoryCache _cache;

  public AdminFieldsModel(AppDbContext db, IOptions<AdminOptions> options, IWebHostEnvironment env, IMemoryCache cache)
  {
    _db = db;
    _options = options.Value;
    _env = env;
    _cache = cache;
  }

  public bool IsAdmin => AdminAuth.IsAdmin(Request, _options);

  public string? Error { get; private set; }

  public List<FieldDefinition> Fields { get; private set; } = [];

  [BindProperty] public string Password { get; set; } = "";

  [BindProperty] public string NewFieldName { get; set; } = "";
  [BindProperty] public FieldType NewFieldType { get; set; } = FieldType.Text;
  [BindProperty] public bool NewFieldRequired { get; set; }
  [BindProperty] public bool NewFieldSearchable { get; set; } = true;
  [BindProperty] public bool NewFieldFilterable { get; set; }
  [BindProperty] public bool NewFieldKeywords { get; set; }

  public AdminImportPreviewResult? ImportPreview { get; private set; }
  public bool OpenImportResultDialog { get; private set; }

  public async Task OnGetAsync()
  {
    await LoadAsync();
  }

  public async Task<IActionResult> OnPostLoginAsync()
  {
    if (Password != _options.Password)
    {
      Error = "Invalid admin password.";
      await LoadAsync();
      return Page();
    }

    AdminAuth.SignIn(Response, _options);
    return RedirectToPage("/AdminFields");
  }

  public IActionResult OnPostLogout()
  {
    AdminAuth.SignOut(Response, _options);
    return RedirectToPage("/AdminFields");
  }

  public async Task<IActionResult> OnPostAddFieldAsync()
  {
    if (!IsAdmin) return RedirectToPage("/AdminFields");

    var name = (NewFieldName ?? "").Trim();
    if (string.IsNullOrWhiteSpace(name))
    {
      Error = "Field name is required.";
      await LoadAsync();
      return Page();
    }

    var activeCount = await _db.FieldDefinitions.CountAsync(x => !x.IsDeleted);
    if (activeCount >= _options.MaxCustomFields)
    {
      Error = $"Max fields reached ({_options.MaxCustomFields}).";
      await LoadAsync();
      return Page();
    }

    // DB uniqueness is across *all* rows (even if soft-deleted). Treat deleted rows as blocking
    // until they are purged, otherwise SQLite will throw UNIQUE constraint errors.
    var exists = await _db.FieldDefinitions.AnyAsync(x => x.Name.ToLower() == name.ToLower());
    if (exists)
    {
      Error = "Field name already exists.";
      await LoadAsync();
      return Page();
    }

    var maxSort = await _db.FieldDefinitions.Where(x => !x.IsDeleted).Select(x => (int?)x.SortOrder).MaxAsync() ?? 0;
    _db.FieldDefinitions.Add(new FieldDefinition
    {
      Name = name,
      Type = NewFieldType,
      IsRequired = NewFieldRequired,
      IsSearchable = NewFieldSearchable,
      IsFilterable = NewFieldFilterable,
      IsKeywords = NewFieldKeywords && NewFieldType == FieldType.Text,
      IsTitle = false,
      IsDetail = false,
      SortOrder = maxSort + 1,
      IsDeleted = false,
    });
    await _db.SaveChangesAsync();
    return RedirectToPage("/AdminFields");
  }

  public async Task<IActionResult> OnPostToggleFieldAsync(int id, string prop)
  {
    if (!IsAdmin) return RedirectToPage("/AdminFields");
    var field = await _db.FieldDefinitions.FirstOrDefaultAsync(x => x.Id == id);
    if (field is null) return RedirectToPage("/AdminFields");

    switch (prop)
    {
      case "required":
        field.IsRequired = !field.IsRequired;
        break;
      case "searchable":
        field.IsSearchable = !field.IsSearchable;
        break;
      case "filterable":
        if (field.Type != FieldType.Text && field.Type != FieldType.Boolean)
        {
          Error = "Only Text/Boolean fields can be used as filters.";
          await LoadAsync();
          return Page();
        }
        field.IsFilterable = !field.IsFilterable;
        break;
      case "keywords":
        if (field.Type != FieldType.Text)
        {
          Error = "Keywords mode is only for Text fields.";
          await LoadAsync();
          return Page();
        }
        field.IsKeywords = !field.IsKeywords;
        break;
      case "title":
        if (field.Type != FieldType.Text)
        {
          Error = "Title tag is only for Text fields.";
          await LoadAsync();
          return Page();
        }

        var currentlyTitle = await _db.FieldDefinitions.Where(x => x.IsTitle).ToListAsync();
        foreach (var f in currentlyTitle)
          f.IsTitle = false;
        field.IsTitle = true;
        break;
      case "detail":
        field.IsDetail = !field.IsDetail;
        break;
      case "delete":
        {
          // Permanent delete: remove dependent values first (FK is Restrict).
          var values = await _db.BookFieldValues.Where(v => v.FieldDefinitionId == field.Id).ToListAsync();
          _db.BookFieldValues.RemoveRange(values);
          _db.FieldDefinitions.Remove(field);

          // Re-normalize sort order for remaining fields.
          var remaining = await _db.FieldDefinitions.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder).ToListAsync();
          for (var i = 0; i < remaining.Count; i++)
            remaining[i].SortOrder = i + 1;
          break;
        }
      case "purge":
        {
          // Back-compat: allow purging previously soft-deleted rows (and their values).
          var values = await _db.BookFieldValues.Where(v => v.FieldDefinitionId == field.Id).ToListAsync();
          _db.BookFieldValues.RemoveRange(values);
          _db.FieldDefinitions.Remove(field);
          break;
        }
    }

    await _db.SaveChangesAsync();
    return RedirectToPage("/AdminFields");
  }

  public async Task<IActionResult> OnPostMoveFieldAsync(int id, string dir)
  {
    if (!IsAdmin) return RedirectToPage("/AdminFields");

    var fields = await _db.FieldDefinitions.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder).ToListAsync();
    var idx = fields.FindIndex(f => f.Id == id);
    if (idx < 0) return RedirectToPage("/AdminFields");

    var swapWith = dir == "up" ? idx - 1 : idx + 1;
    if (swapWith < 0 || swapWith >= fields.Count) return RedirectToPage("/AdminFields");

    (fields[idx].SortOrder, fields[swapWith].SortOrder) = (fields[swapWith].SortOrder, fields[idx].SortOrder);
    await _db.SaveChangesAsync();
    return RedirectToPage("/AdminFields");
  }

  public async Task<IActionResult> OnPostRenameFieldAsync(int id, string name)
  {
    if (!IsAdmin) return RedirectToPage("/AdminFields");

    var field = await _db.FieldDefinitions.FirstOrDefaultAsync(x => x.Id == id);
    if (field is null) return RedirectToPage("/AdminFields");

    var newName = (name ?? "").Trim();
    if (string.IsNullOrWhiteSpace(newName))
    {
      Error = "Field name is required.";
      await LoadAsync();
      return Page();
    }

    var exists = await _db.FieldDefinitions.AnyAsync(x =>
      x.Id != id &&
      x.Name.ToLower() == newName.ToLower());
    if (exists)
    {
      Error = "Field name already exists.";
      await LoadAsync();
      return Page();
    }

    field.Name = newName;
    await _db.SaveChangesAsync();
    return RedirectToPage("/AdminFields");
  }

  // Import/export moved to /AdminBooks.

  private static string RenderExportCell(FieldDefinition field, BookFieldValue v)
  {
    return field.Type switch
    {
      FieldType.Text => v.ValueText ?? "",
      FieldType.Number => v.ValueNumber?.ToString(CultureInfo.InvariantCulture) ?? "",
      FieldType.Boolean => v.ValueBool is null ? "" : (v.ValueBool.Value ? "true" : "false"),
      FieldType.Date => v.ValueDate?.ToString("yyyy-MM-dd") ?? "",
      _ => ""
    };
  }

  private static string NormalizeTitleCaseIfSafe(FieldDefinition field, string raw)
  {
    raw = (raw ?? "").Trim();
    if (raw.Length == 0) return "";
    if (raw.Any(char.IsDigit)) return raw;
    if (raw.Any(c => !(char.IsLetter(c) || char.IsWhiteSpace(c) || c == '\'' || c == '-'))) return raw;
    if (string.Equals(field.Name, "Number", StringComparison.OrdinalIgnoreCase)) return raw;
    var ti = CultureInfo.CurrentCulture.TextInfo;
    return ti.ToTitleCase(raw.ToLower(CultureInfo.CurrentCulture));
  }

  private static void ApplyFieldValueFromText(FieldDefinition field, string raw, BookFieldValue value)
  {
    value.ValueText = null;
    value.ValueNumber = null;
    value.ValueBool = null;
    value.ValueDate = null;

    switch (field.Type)
    {
      case FieldType.Text:
        value.ValueText = field.IsKeywords ? KeywordHelper.NormalizeKeywords(raw) : NormalizeTitleCaseIfSafe(field, raw);
        break;
      case FieldType.Number:
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ||
            decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
          value.ValueNumber = d;
        break;
      case FieldType.Boolean:
        value.ValueBool = raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase) || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        break;
      case FieldType.Date:
        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dt) ||
            DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
          value.ValueDate = dt.Date;
        break;
    }
  }

  private static AdminImportPreviewResult BuildImportPreview(string path, List<FieldDefinition> fields)
  {
    var rows = ReadImportRows(path, fields);

    var requiredMissing = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
    var optionalMissing = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

    for (var r = 0; r < rows.Count; r++)
    {
      var row = rows[r];
      if (row.All(IsEffectivelyBlank)) continue;

      for (var c = 0; c < fields.Count; c++)
      {
        var f = fields[c];
        if (f.Type == FieldType.Boolean) continue;
        var cell = c < row.Count ? row[c] : "";
        if (!IsEffectivelyBlank(cell)) continue;

        if (f.IsRequired)
        {
          if (!requiredMissing.TryGetValue(f.Name, out var list)) requiredMissing[f.Name] = list = [];
          list.Add(r + 1);
        }
        else
        {
          if (!optionalMissing.TryGetValue(f.Name, out var list)) optionalMissing[f.Name] = list = [];
          list.Add(r + 1);
        }
      }
    }

    return new AdminImportPreviewResult
    {
      TotalRows = rows.Count,
      RequiredMissing = requiredMissing,
      OptionalMissing = optionalMissing,
    };
  }

  private static List<List<string>> ReadImportRows(string path, List<FieldDefinition> fields)
  {
    using var wb = new XLWorkbook(path);
    var ws = wb.Worksheets.First();

    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
    var lastCol = Math.Max(ws.LastColumnUsed()?.ColumnNumber() ?? 0, fields.Count);
    if (lastRow == 0 || lastCol == 0) return [];

    bool HasHeader()
    {
      var headerMatches = 0;
      for (var c = 1; c <= Math.Min(fields.Count, lastCol); c++)
      {
        var s = (ws.Cell(1, c).GetString() ?? "").Trim();
        if (string.Equals(s, fields[c - 1].Name, StringComparison.OrdinalIgnoreCase)) headerMatches++;
      }
      return headerMatches >= Math.Min(2, fields.Count);
    }

    var startRow = HasHeader() ? 2 : 1;

    var rows = new List<List<string>>();
    for (var r = startRow; r <= lastRow; r++)
    {
      var row = new List<string>();
      for (var c = 1; c <= fields.Count; c++)
        row.Add(NormalizeCell(ws.Cell(r, c).GetString()));
      rows.Add(row);
    }
    return rows;
  }

  private static string NormalizeCell(string? s)
  {
    if (string.IsNullOrEmpty(s)) return "";
    // Excel sometimes contains "invisible" whitespace (NBSP / zero-width) that looks empty but breaks required checks.
    return s
      .Replace('\u00A0', ' ')  // NBSP
      .Replace("\u200B", "")   // zero-width space
      .Replace("\u200C", "")   // zero-width non-joiner
      .Replace("\u200D", "")   // zero-width joiner
      .Trim();
  }

  private static bool IsEffectivelyBlank(string? s) => string.IsNullOrWhiteSpace(NormalizeCell(s));

  public sealed class AdminImportPreviewResult
  {
    public string PreviewId { get; set; } = "";
    public int TotalRows { get; set; }
    public Dictionary<string, List<int>> RequiredMissing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<int>> OptionalMissing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  }

  private sealed record CachedImport(string PreviewId, string Path);

  private async Task LoadAsync()
  {
    Fields = await _db.FieldDefinitions
      .OrderBy(x => x.IsDeleted)
      .ThenBy(x => x.SortOrder)
      .ToListAsync();
  }
}

