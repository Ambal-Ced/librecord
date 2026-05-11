using LibRecord.Data;
using LibRecord.Models;
using LibRecord.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LibRecord.Pages;

public sealed class AdminListModel : PageModel
{
  private readonly AppDbContext _db;
  private readonly AdminOptions _options;

  public AdminListModel(AppDbContext db, IOptions<AdminOptions> options)
  {
    _db = db;
    _options = options.Value;
  }

  public bool IsAdmin => AdminAuth.IsAdmin(Request, _options);
  public string? Error { get; private set; }

  [BindProperty] public string Password { get; set; } = "";

  public int PageNumber { get; private set; } = 1;
  public bool HasMore { get; private set; }

  public List<BookRow> Books { get; private set; } = [];

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
    return RedirectToPage("/AdminList");
  }

  public IActionResult OnPostLogout()
  {
    AdminAuth.SignOut(Response, _options);
    return RedirectToPage("/AdminList");
  }

  public async Task<IActionResult> OnPostDeleteBookAsync(int id)
  {
    if (!IsAdmin) return RedirectToPage("/AdminList");
    var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id);
    if (book is null) return RedirectToPage("/AdminList", new { p = PageNumber });

    _db.Books.Remove(book);
    await _db.SaveChangesAsync();
    return RedirectToPage("/AdminList", new { p = PageNumber });
  }

  private async Task LoadAsync()
  {
    if (!IsAdmin)
    {
      Books = [];
      HasMore = false;
      return;
    }

    const int pageSize = 5;
    var skip = (PageNumber - 1) * pageSize;

    var fields = await _db.FieldDefinitions.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder).ToListAsync();

    var books = await _db.Books
      .Include(b => b.FieldValues)
      .ThenInclude(v => v.FieldDefinition)
      .OrderByDescending(b => b.UpdatedAt)
      .Skip(skip)
      .Take(pageSize + 1)
      .ToListAsync();

    HasMore = books.Count > pageSize;
    if (HasMore) books = books.Take(pageSize).ToList();

    Books = books.Select(b => BookRowMapper.From(b, fields)).ToList();
  }

  public sealed record BookRow(int Id, string Title, List<(string Name, string Value)> Values);

  private static class BookRowMapper
  {
    public static BookRow From(Book book, List<FieldDefinition> fields)
    {
      var byField = book.FieldValues
        .Where(v => v.FieldDefinition is not null)
        .ToDictionary(v => v.FieldDefinitionId, v => v, EqualityComparer<int>.Default);

      string title = $"Book #{book.Id}";
      var titleField = fields.FirstOrDefault(f => f.Type == FieldType.Text && f.IsTitle)
        ?? fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase));
      if (titleField is not null && byField.TryGetValue(titleField.Id, out var v) && !string.IsNullOrWhiteSpace(v.ValueText))
        title = v.ValueText!;

      var values = new List<(string Name, string Value)>();
      foreach (var f in fields)
      {
        if (!byField.TryGetValue(f.Id, out var fv)) continue;
        var rendered = RenderValue(f, fv);
        if (string.IsNullOrWhiteSpace(rendered)) continue;
        values.Add((f.Name, rendered));
      }

      return new BookRow(book.Id, title, values);
    }

    private static string RenderValue(FieldDefinition f, BookFieldValue v)
    {
      return f.Type switch
      {
        FieldType.Text => v.ValueText ?? "",
        FieldType.Number => v.ValueNumber?.ToString() ?? "",
        FieldType.Boolean => v.ValueBool is null ? "" : (v.ValueBool.Value ? "Yes" : "No"),
        FieldType.Date => v.ValueDate?.ToString("yyyy-MM-dd") ?? "",
        _ => ""
      };
    }
  }
}

