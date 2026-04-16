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

    var exists = await _db.FieldDefinitions.AnyAsync(x => !x.IsDeleted && x.Name.ToLower() == name.ToLower());
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
      case "delete":
        field.IsDeleted = true;
        break;
      case "restore":
        field.IsDeleted = false;
        break;
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
      !x.IsDeleted &&
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

  public async Task<IActionResult> OnPostExportAsync()
  {
    if (!IsAdmin) return RedirectToPage("/AdminFields");

    var fields = await _db.FieldDefinitions
      .Where(x => !x.IsDeleted)
      .OrderBy(x => x.SortOrder)
      .ToListAsync();

    var books = await _db.Books
      .Include(b => b.FieldValues)
      .ThenInclude(v => v.FieldDefinition)
      .OrderBy(b => b.Id)
      .ToListAsync();

    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Books");

    for (var c = 0; c < fields.Count; c++)
      ws.Cell(1, c + 1).Value = fields[c].Name;

    for (var r = 0; r < books.Count; r++)
    {
      var book = books[r];
      var byField = book.FieldValues.ToDictionary(v => v.FieldDefinitionId, v => v);
      for (var c = 0; c < fields.Count; c++)
      {
        var f = fields[c];
        if (!byField.TryGetValue(f.Id, out var v)) continue;
        ws.Cell(r + 2, c + 1).Value = RenderExportCell(f, v);
      }
    }

    ws.Columns().AdjustToContents(1, 80);

    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    return File(
      ms.ToArray(),
      "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      $"librecord-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
  }

  public async Task<IActionResult> OnPostImportPreviewAsync(IFormFile? file)
  {
    if (!IsAdmin) return RedirectToPage("/AdminFields");
    if (file is null || file.Length == 0)
    {
      Error = "Choose an Excel file (.xlsx) to import.";
      await LoadAsync();
      OpenImportResultDialog = true;
      return Page();
    }

    if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
    {
      Error = "Only .xlsx files are supported.";
      await LoadAsync();
      OpenImportResultDialog = true;
      return Page();
    }

    var fields = await _db.FieldDefinitions.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder).ToListAsync();
    if (fields.Count == 0)
    {
      Error = "Create fields first before importing.";
      await LoadAsync();
      OpenImportResultDialog = true;
      return Page();
    }

    var importDir = Path.Combine(_env.ContentRootPath, "App_Data", "imports");
    Directory.CreateDirectory(importDir);
    var previewId = Guid.NewGuid().ToString("n");
    var path = Path.Combine(importDir, $"{previewId}.xlsx");
    await using (var fs = System.IO.File.Create(path))
      await file.CopyToAsync(fs);

    ImportPreview = BuildImportPreview(path, fields);
    ImportPreview.PreviewId = previewId;

    _cache.Set(
      $"import:{previewId}",
      new CachedImport(previewId, path),
      new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(20) });

    await LoadAsync();
    OpenImportResultDialog = true;
    return Page();
  }

  public async Task<IActionResult> OnPostImportCommitAsync(string previewId, bool force)
  {
    if (!IsAdmin) return RedirectToPage("/AdminFields");
    if (string.IsNullOrWhiteSpace(previewId))
      return RedirectToPage("/AdminFields");

    if (!_cache.TryGetValue<CachedImport>($"import:{previewId}", out var cached) || cached is null)
    {
      Error = "Import session expired. Please upload the file again.";
      await LoadAsync();
      OpenImportResultDialog = true;
      return Page();
    }

    var fields = await _db.FieldDefinitions.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder).ToListAsync();
    if (fields.Count == 0) return RedirectToPage("/AdminFields");

    var preview = BuildImportPreview(cached.Path, fields);
    preview.PreviewId = previewId;

    if (preview.RequiredMissing.Count > 0)
    {
      ImportPreview = preview;
      Error = "Required field missing. Fix your Excel or unrequire the field, then try again.";
      await LoadAsync();
      OpenImportResultDialog = true;
      return Page();
    }

    if (preview.OptionalMissing.Count > 0 && !force)
    {
      ImportPreview = preview;
      await LoadAsync();
      OpenImportResultDialog = true;
      return Page();
    }

    var rows = ReadImportRows(cached.Path, fields);
    var now = DateTime.UtcNow;

    // Deduplicate by (Title, Author, Volume) and increment BookCount.
    var titleField = fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase));
    var authorField = fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Author", StringComparison.OrdinalIgnoreCase));
    var volumeField = fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Volume", StringComparison.OrdinalIgnoreCase));

    string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
    bool CanDedup() => titleField is not null && authorField is not null && volumeField is not null;

    var titleIdx = titleField is null ? -1 : fields.FindIndex(f => f.Id == titleField.Id);
    var authorIdx = authorField is null ? -1 : fields.FindIndex(f => f.Id == authorField.Id);
    var volumeIdx = volumeField is null ? -1 : fields.FindIndex(f => f.Id == volumeField.Id);

    var existingByKey = new Dictionary<string, Book>(StringComparer.OrdinalIgnoreCase);
    if (CanDedup())
    {
      var ids = new[] { titleField!.Id, authorField!.Id, volumeField!.Id }.ToHashSet();
      var existing = await _db.Books
        .Include(b => b.FieldValues)
        .Where(b => b.FieldValues.Any(v => ids.Contains(v.FieldDefinitionId)))
        .ToListAsync();

      foreach (var b in existing)
      {
        var byField = b.FieldValues.ToDictionary(v => v.FieldDefinitionId, v => v);
        var t = byField.TryGetValue(titleField!.Id, out var tv) ? tv.ValueText : "";
        var a = byField.TryGetValue(authorField!.Id, out var av) ? av.ValueText : "";
        var v = byField.TryGetValue(volumeField!.Id, out var vv) ? vv.ValueText : "";
        var key = $"{Norm(t)}|{Norm(a)}|{Norm(v)}";
        if (!existingByKey.ContainsKey(key))
          existingByKey[key] = b;
      }
    }

    foreach (var row in rows)
    {
      if (row.All(string.IsNullOrWhiteSpace)) continue;

      Book? book = null;
      string? keyForRow = null;
      if (CanDedup())
      {
        var t = (titleIdx >= 0 && titleIdx < row.Count) ? row[titleIdx] : "";
        var a = (authorIdx >= 0 && authorIdx < row.Count) ? row[authorIdx] : "";
        var v = (volumeIdx >= 0 && volumeIdx < row.Count) ? row[volumeIdx] : "";
        keyForRow = $"{Norm(t)}|{Norm(a)}|{Norm(v)}";
        if (existingByKey.TryGetValue(keyForRow, out var existingBook))
          book = existingBook;
      }

      if (book is not null)
      {
        book.BookCount = Math.Max(1, book.BookCount) + 1;
        book.UpdatedAt = now;
        continue;
      }

      book = new Book { UpdatedAt = now, BookCount = 1 };
      _db.Books.Add(book);
      await _db.SaveChangesAsync();

      for (var i = 0; i < fields.Count; i++)
      {
        var f = fields[i];
        var raw = (i < row.Count ? row[i] : "")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw) && f.Type != FieldType.Boolean) continue;

        var fv = new BookFieldValue { BookId = book.Id, FieldDefinitionId = f.Id };
        ApplyFieldValueFromText(f, raw, fv);
        _db.BookFieldValues.Add(fv);
      }

      if (keyForRow is not null)
        existingByKey[keyForRow] = book;
    }

    await _db.SaveChangesAsync();

    try { System.IO.File.Delete(cached.Path); } catch { /* ignore */ }
    _cache.Remove($"import:{previewId}");

    return RedirectToPage("/AdminFields");
  }

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
      if (row.All(string.IsNullOrWhiteSpace)) continue;

      for (var c = 0; c < fields.Count; c++)
      {
        var f = fields[c];
        if (f.Type == FieldType.Boolean) continue;
        var cell = c < row.Count ? (row[c] ?? "").Trim() : "";
        if (!string.IsNullOrWhiteSpace(cell)) continue;

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
        row.Add((ws.Cell(r, c).GetString() ?? "").Trim());
      rows.Add(row);
    }
    return rows;
  }

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

