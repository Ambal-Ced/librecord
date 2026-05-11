using ClosedXML.Excel;
using LibRecord.Data;
using LibRecord.Models;
using LibRecord.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibRecord.Pages;

public sealed class ResourceBooksModel : PageModel
{
  private readonly AppDbContext _db;
  private readonly AdminOptions _options;
  private readonly IWebHostEnvironment _env;

  public ResourceBooksModel(AppDbContext db, IOptions<AdminOptions> options, IWebHostEnvironment env)
  {
    _db = db;
    _options = options.Value;
    _env = env;
  }

  public bool IsAdmin => AdminAuth.IsAdmin(Request, _options);
  public string? Error { get; private set; }

  [BindProperty] public string Password { get; set; } = "";

  [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
  public bool HasMore { get; private set; }
  public int TotalRows { get; private set; }
  public int TotalPages { get; private set; }

  public List<FieldDefinition> Fields { get; private set; } = [];
  public List<RowVm> Rows { get; private set; } = [];

  public string FilePath { get; private set; } = "";

  public async Task OnGetAsync(int? p)
  {
    PageNumber = Math.Max(1, p ?? 1);
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
    return RedirectToPage("/ResourceBooks");
  }

  public IActionResult OnPostLogout()
  {
    AdminAuth.SignOut(Response, _options);
    return RedirectToPage("/ResourceBooks");
  }

  private async Task LoadAsync()
  {
    Fields = await _db.FieldDefinitions
      .Where(x => !x.IsDeleted)
      .OrderBy(x => x.SortOrder)
      .ToListAsync();

    if (!IsAdmin)
    {
      Rows = [];
      HasMore = false;
      return;
    }

    var dataRoot = AppPaths.GetDataRoot(Request.HttpContext.RequestServices.GetRequiredService<IConfiguration>(), _env);
    var path = Path.Combine(AppPaths.GetResourceDir(dataRoot), "mbook.xlsx");
    FilePath = path;

    if (!System.IO.File.Exists(path))
    {
      Error = "Missing file: resource/mbook.xlsx";
      Rows = [];
      HasMore = false;
      return;
    }

    // NOTE: This page is intentionally read-only and uses the file as the source of truth.
    // Column 1 -> Field 1, Column 2 -> Field 2, etc.
    // Empty rows are skipped.
    using var wb = new XLWorkbook(path);
    var ws = wb.Worksheets.First();

    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
    var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
    if (lastRow <= 0 || lastCol <= 0 || Fields.Count == 0)
    {
      Rows = [];
      HasMore = false;
      TotalRows = 0;
      TotalPages = 0;
      return;
    }

    bool IsEffectivelyBlank(string? s)
    {
      if (string.IsNullOrEmpty(s)) return true;
      var norm = s
        .Replace('\u00A0', ' ')
        .Replace("\u200B", "")
        .Replace("\u200C", "")
        .Replace("\u200D", "")
        .Trim();
      return string.IsNullOrWhiteSpace(norm);
    }

    bool HasHeader()
    {
      var headerMatches = 0;
      var max = Math.Min(Fields.Count, lastCol);
      for (var c = 1; c <= max; c++)
      {
        var s = (ws.Cell(1, c).GetString() ?? "").Trim();
        if (string.Equals(s, Fields[c - 1].Name, StringComparison.OrdinalIgnoreCase))
          headerMatches++;
      }
      return headerMatches >= Math.Min(2, max);
    }

    var startRow = HasHeader() ? 2 : 1;

    const int pageSize = 100;
    // First pass: count non-empty rows (skipping fully empty rows).
    var total = 0;
    for (var r = startRow; r <= lastRow; r++)
    {
      var allBlank = true;
      for (var c = 1; c <= Math.Min(Fields.Count, lastCol); c++)
      {
        var raw = ws.Cell(r, c).GetString();
        if (!IsEffectivelyBlank(raw)) { allBlank = false; break; }
      }
      if (!allBlank) total++;
    }

    TotalRows = total;
    TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
    if (TotalPages > 0) PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

    // Second pass: collect current page.
    var skip = (PageNumber - 1) * pageSize;
    var collected = new List<RowVm>(pageSize);
    var seen = 0;

    for (var r = startRow; r <= lastRow; r++)
    {
      var values = new List<string>(Fields.Count);
      for (var c = 1; c <= Fields.Count; c++)
      {
        var raw = c <= lastCol ? ws.Cell(r, c).GetString() : "";
        values.Add(raw ?? "");
      }

      if (values.All(IsEffectivelyBlank)) continue;

      if (seen < skip)
      {
        seen++;
        continue;
      }

      if (collected.Count >= pageSize) break;
      collected.Add(new RowVm(r, values));
    }

    Rows = collected;
    HasMore = TotalPages > 0 && PageNumber < TotalPages;
  }

  public sealed record RowVm(int RowNumber, List<string> Values);
}

