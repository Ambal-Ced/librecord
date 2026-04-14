using LibRecord.Data;
using LibRecord.Models;
using LibRecord.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LibRecord.Pages;

public sealed class AdminModel : PageModel
{
  private readonly AppDbContext _db;
  private readonly AdminOptions _options;

  public AdminModel(AppDbContext db, IOptions<AdminOptions> options)
  {
    _db = db;
    _options = options.Value;
  }

  public bool IsAdmin => AdminAuth.IsAdmin(Request, _options);

  public string? Error { get; private set; }

  public List<FieldDefinition> Fields { get; private set; } = [];
  public List<BookRow> Books { get; private set; } = [];

  [BindProperty] public string Password { get; set; } = "";

  [BindProperty] public string NewFieldName { get; set; } = "";
  [BindProperty] public FieldType NewFieldType { get; set; } = FieldType.Text;
  [BindProperty] public bool NewFieldRequired { get; set; }
  [BindProperty] public bool NewFieldSearchable { get; set; } = true;

  [BindProperty(SupportsGet = true)] public int? EditBookId { get; set; }

  // Dynamic values posted as Value_{FieldId}
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
    return RedirectToPage("/Admin");
  }

  public IActionResult OnPostLogout()
  {
    AdminAuth.SignOut(Response, _options);
    return RedirectToPage("/Admin");
  }

  public async Task<IActionResult> OnPostAddFieldAsync()
  {
    if (!IsAdmin) return RedirectToPage("/Admin");

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
      SortOrder = maxSort + 1,
      IsDeleted = false,
    });
    await _db.SaveChangesAsync();
    return RedirectToPage("/Admin");
  }

  public async Task<IActionResult> OnPostToggleFieldAsync(int id, string prop)
  {
    if (!IsAdmin) return RedirectToPage("/Admin");
    var field = await _db.FieldDefinitions.FirstOrDefaultAsync(x => x.Id == id);
    if (field is null) return RedirectToPage("/Admin");

    switch (prop)
    {
      case "required":
        field.IsRequired = !field.IsRequired;
        break;
      case "searchable":
        field.IsSearchable = !field.IsSearchable;
        break;
      case "delete":
        field.IsDeleted = true;
        break;
      case "restore":
        field.IsDeleted = false;
        break;
    }

    await _db.SaveChangesAsync();
    return RedirectToPage("/Admin");
  }

  public async Task<IActionResult> OnPostMoveFieldAsync(int id, string dir)
  {
    if (!IsAdmin) return RedirectToPage("/Admin");

    var fields = await _db.FieldDefinitions.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder).ToListAsync();
    var idx = fields.FindIndex(f => f.Id == id);
    if (idx < 0) return RedirectToPage("/Admin");

    var swapWith = dir == "up" ? idx - 1 : idx + 1;
    if (swapWith < 0 || swapWith >= fields.Count) return RedirectToPage("/Admin");

    (fields[idx].SortOrder, fields[swapWith].SortOrder) = (fields[swapWith].SortOrder, fields[idx].SortOrder);
    await _db.SaveChangesAsync();
    return RedirectToPage("/Admin");
  }

  public async Task<IActionResult> OnPostDeleteBookAsync(int id)
  {
    if (!IsAdmin) return RedirectToPage("/Admin");
    var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id);
    if (book is null) return RedirectToPage("/Admin");

    _db.Books.Remove(book);
    await _db.SaveChangesAsync();
    return RedirectToPage("/Admin");
  }

  public async Task<IActionResult> OnPostSaveBookAsync(int? id)
  {
    if (!IsAdmin) return RedirectToPage("/Admin");

    var fields = await _db.FieldDefinitions.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder).ToListAsync();
    if (fields.Count == 0)
    {
      Error = "Create at least one field first.";
      await LoadAsync();
      return Page();
    }

    Book book;
    if (id is null || id.Value == 0)
    {
      book = new Book();
      _db.Books.Add(book);
      await _db.SaveChangesAsync();
    }
    else
    {
      book = await _db.Books
        .Include(b => b.FieldValues)
        .FirstOrDefaultAsync(b => b.Id == id.Value) ?? new Book();
      if (book.Id == 0)
      {
        Error = "Book not found.";
        await LoadAsync();
        return Page();
      }
    }

    var now = DateTime.UtcNow;
    book.UpdatedAt = now;

    var existing = await _db.BookFieldValues.Where(v => v.BookId == book.Id).ToListAsync();
    var existingByField = existing.ToDictionary(v => v.FieldDefinitionId, v => v);

    foreach (var field in fields)
    {
      var key = $"Value_{field.Id}";
      var raw = (Request.Form[key].ToString() ?? "").Trim();

      if (field.IsRequired && string.IsNullOrWhiteSpace(raw) && field.Type != FieldType.Boolean)
      {
        Error = $"\"{field.Name}\" is required.";
        await LoadAsync();
        EditBookId = book.Id;
        return Page();
      }

      if (!existingByField.TryGetValue(field.Id, out var value))
      {
        value = new BookFieldValue { BookId = book.Id, FieldDefinitionId = field.Id };
        _db.BookFieldValues.Add(value);
      }

      value.ValueText = null;
      value.ValueNumber = null;
      value.ValueBool = null;
      value.ValueDate = null;

      switch (field.Type)
      {
        case FieldType.Text:
          value.ValueText = raw;
          break;
        case FieldType.Number:
          if (string.IsNullOrWhiteSpace(raw)) break;
          if (!decimal.TryParse(raw, out var d))
          {
            Error = $"\"{field.Name}\" must be a number.";
            await LoadAsync();
            EditBookId = book.Id;
            return Page();
          }
          value.ValueNumber = d;
          break;
        case FieldType.Boolean:
          value.ValueBool = raw == "on" || raw == "true" || raw == "1";
          break;
        case FieldType.Date:
          if (string.IsNullOrWhiteSpace(raw)) break;
          if (!DateTime.TryParse(raw, out var dt))
          {
            Error = $"\"{field.Name}\" must be a date.";
            await LoadAsync();
            EditBookId = book.Id;
            return Page();
          }
          value.ValueDate = dt.Date;
          break;
      }
    }

    await _db.SaveChangesAsync();
    return RedirectToPage("/Admin");
  }

  private async Task LoadAsync()
  {
    Fields = await _db.FieldDefinitions.OrderBy(x => x.IsDeleted).ThenBy(x => x.SortOrder).ToListAsync();

    if (!IsAdmin)
    {
      Books = [];
      return;
    }

    var activeFields = Fields.Where(f => !f.IsDeleted).OrderBy(f => f.SortOrder).ToList();

    var books = await _db.Books
      .Include(b => b.FieldValues)
      .ThenInclude(v => v.FieldDefinition)
      .OrderByDescending(b => b.UpdatedAt)
      .Take(200)
      .ToListAsync();

    Books = books.Select(b => BookRowMapper.From(b, activeFields)).ToList();
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
      var titleField = fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase));
      if (titleField is not null && byField.TryGetValue(titleField.Id, out var v) && !string.IsNullOrWhiteSpace(v.ValueText))
      {
        title = v.ValueText!;
      }

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

