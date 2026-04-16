using LibRecord.Data;
using LibRecord.Models;
using LibRecord.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace LibRecord.Pages;

public sealed class AdminBooksModel : PageModel
{
  private readonly AppDbContext _db;
  private readonly AdminOptions _options;

  public AdminBooksModel(AppDbContext db, IOptions<AdminOptions> options)
  {
    _db = db;
    _options = options.Value;
  }

  public bool IsAdmin => AdminAuth.IsAdmin(Request, _options);

  public string? Error { get; private set; }

  public List<FieldDefinition> Fields { get; private set; } = [];
  public List<BookRow> Books { get; private set; } = [];

  [BindProperty] public string Password { get; set; } = "";

  [BindProperty(SupportsGet = true)] public int? EditBookId { get; set; }

  /// <summary>Current values when adding/editing a book (key = field id).</summary>
  public Dictionary<int, string> EditFieldValues { get; private set; } = [];

  [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
  [BindProperty(SupportsGet = true)] public DateOnly? UploadedFrom { get; set; }
  [BindProperty(SupportsGet = true)] public DateOnly? UploadedTo { get; set; }

  public Dictionary<int, HashSet<string>> ActiveFilters { get; private set; } = [];
  public List<FilterField> FilterFields { get; private set; } = [];

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
    return RedirectToPage("/AdminBooks");
  }

  public IActionResult OnPostLogout()
  {
    AdminAuth.SignOut(Response, _options);
    return RedirectToPage("/AdminBooks");
  }

  public async Task<IActionResult> OnPostDeleteBookAsync(int id)
  {
    if (!IsAdmin) return RedirectToPage("/AdminBooks");
    var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == id);
    if (book is null) return RedirectToPage("/AdminBooks");

    _db.Books.Remove(book);
    await _db.SaveChangesAsync();
    return RedirectToPage("/AdminBooks");
  }

  public async Task<IActionResult> OnPostSaveBookAsync(int? id)
  {
    if (!IsAdmin) return RedirectToPage("/AdminBooks");

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
        LoadEditFieldsFromForm(fields, book.Id);
        await LoadAsync();
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
          if (field.IsKeywords)
          {
            value.ValueText = KeywordHelper.NormalizeKeywords(raw);
          }
          else
          {
            value.ValueText = NormalizeTitleCaseIfSafe(field, raw);
          }
          break;
        case FieldType.Number:
          if (string.IsNullOrWhiteSpace(raw)) break;
          if (!decimal.TryParse(raw, out var d))
          {
            Error = $"\"{field.Name}\" must be a number.";
            LoadEditFieldsFromForm(fields, book.Id);
            await LoadAsync();
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
            LoadEditFieldsFromForm(fields, book.Id);
            await LoadAsync();
            return Page();
          }
          value.ValueDate = dt.Date;
          break;
      }
    }

    await _db.SaveChangesAsync();
    return RedirectToPage("/AdminBooks");
  }


  private static string NormalizeTitleCaseIfSafe(FieldDefinition field, string raw)
  {
    raw = (raw ?? "").Trim();
    if (raw.Length == 0) return "";

    if (raw.Any(char.IsDigit)) return raw;
    if (raw.Any(c => !(char.IsLetter(c) || char.IsWhiteSpace(c) || c == '\'' || c == '-'))) return raw;
    if (string.Equals(field.Name, "Number", StringComparison.OrdinalIgnoreCase)) return raw;

    var ti = CultureInfo.CurrentCulture.TextInfo;
    var lowered = raw.ToLower(CultureInfo.CurrentCulture);
    return ti.ToTitleCase(lowered);
  }


  private void LoadEditFieldsFromForm(List<FieldDefinition> fields, int bookId)
  {
    EditBookId = bookId;
    EditFieldValues = [];
    foreach (var field in fields)
      EditFieldValues[field.Id] = Request.Form[$"Value_{field.Id}"].ToString();
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

    ActiveFilters = ReadFieldFilters(activeFields.Where(f => f.IsFilterable));
    FilterFields = await BuildFilterFieldsAsync(activeFields.Where(f => f.IsFilterable).ToList());

    var books = await _db.Books
      .Include(b => b.FieldValues)
      .ThenInclude(v => v.FieldDefinition)
      .OrderByDescending(b => b.UpdatedAt)
      .Take(500)
      .ToListAsync();

    books = ApplyUploadedDateFilter(books, UploadedFrom, UploadedTo);
    books = ApplyFieldFilters(books, ActiveFilters, activeFields);
    books = ApplySort(books, activeFields, Sort);

    if (EditBookId is not null && EditFieldValues.Count == 0)
    {
      var editBook = await _db.Books.AsNoTracking()
        .Include(b => b.FieldValues)
        .FirstOrDefaultAsync(b => b.Id == EditBookId.Value);
      if (editBook is not null)
      {
        foreach (var fv in editBook.FieldValues)
        {
          var fd = Fields.FirstOrDefault(f => f.Id == fv.FieldDefinitionId);
          if (fd is null) continue;
          EditFieldValues[fv.FieldDefinitionId] = FormatFieldValueForEdit(fd, fv);
        }
      }
    }

    Books = books.Select(b => BookRowMapper.From(b, activeFields)).ToList();
  }

  private Dictionary<int, HashSet<string>> ReadFieldFilters(IEnumerable<FieldDefinition> filterableFields)
  {
    var dict = new Dictionary<int, HashSet<string>>();
    foreach (var f in filterableFields)
    {
      var key = $"f_{f.Id}";
      var values = Request.Query[key]
        .Select(x => (x ?? "").Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

      if (values.Count > 0) dict[f.Id] = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    return dict;
  }

  private async Task<List<FilterField>> BuildFilterFieldsAsync(List<FieldDefinition> filterableFields)
  {
    var result = new List<FilterField>();
    foreach (var f in filterableFields.OrderBy(x => x.SortOrder))
    {
      var options = new List<string>();
      if (f.Type == FieldType.Boolean)
      {
        options.Add("true");
        options.Add("false");
      }
      else
      {
        var raw = await _db.BookFieldValues
          .Where(v => v.FieldDefinitionId == f.Id && v.ValueText != null && v.ValueText != "")
          .Select(v => v.ValueText!)
          .ToListAsync();
        options = KeywordHelper.BuildFilterOptions(f, raw);
      }

      result.Add(new FilterField(f, options));
    }
    return result;
  }

  private static List<Book> ApplyUploadedDateFilter(List<Book> books, DateOnly? from, DateOnly? to)
  {
    if (from is null && to is null) return books;

    DateTime? fromDt = from is null ? null : from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    DateTime? toDt = to is null ? null : to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

    return books.Where(b =>
    {
      if (fromDt is not null && b.CreatedAt < fromDt.Value) return false;
      if (toDt is not null && b.CreatedAt > toDt.Value) return false;
      return true;
    }).ToList();
  }

  private static List<Book> ApplyFieldFilters(List<Book> books, Dictionary<int, HashSet<string>> filters, List<FieldDefinition> fieldDefs)
  {
    if (filters.Count == 0) return books;
    var byId = fieldDefs.ToDictionary(f => f.Id);

    return books.Where(b =>
    {
      foreach (var (fieldId, expectedSet) in filters)
      {
        if (!byId.TryGetValue(fieldId, out var fd)) return false;
        var v = b.FieldValues.FirstOrDefault(x => x.FieldDefinitionId == fieldId);
        if (v is null) return false;

        var actual = v.ValueText ?? (v.ValueBool is null ? "" : (v.ValueBool.Value ? "true" : "false"));
        if (string.IsNullOrWhiteSpace(actual)) return false;

        if (fd.Type == FieldType.Text && (fd.IsKeywords || actual.Contains(',')))
        {
          var parts = KeywordHelper.SplitKeywords(actual);
          if (!parts.Any(p => expectedSet.Contains(p))) return false;
        }
        else
        {
          if (!expectedSet.Contains(actual.Trim())) return false;
        }
      }
      return true;
    }).ToList();
  }

  private static string FormatFieldValueForEdit(FieldDefinition fd, BookFieldValue v)
  {
    return fd.Type switch
    {
      FieldType.Text => v.ValueText ?? "",
      FieldType.Number => v.ValueNumber?.ToString() ?? "",
      FieldType.Boolean => v.ValueBool == true ? "true" : "",
      FieldType.Date => v.ValueDate?.ToString("yyyy-MM-dd") ?? "",
      _ => ""
    };
  }

  private static List<Book> ApplySort(List<Book> books, List<FieldDefinition> fields, string? sort)
  {
    sort = (sort ?? "").Trim().ToLowerInvariant();

    if (sort == "uploaded_old")
      return books.OrderBy(b => b.CreatedAt).ToList();
    if (sort == "title_az" || sort == "title_za")
    {
      var titleField = fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase));
      string TitleOf(Book b)
      {
        if (titleField is null) return b.Id.ToString();
        var v = b.FieldValues.FirstOrDefault(x => x.FieldDefinitionId == titleField.Id)?.ValueText;
        return v ?? b.Id.ToString();
      }

      return sort == "title_za"
        ? books.OrderByDescending(TitleOf).ToList()
        : books.OrderBy(TitleOf).ToList();
    }

    return books.OrderByDescending(b => b.CreatedAt).ToList();
  }

  public sealed record FilterField(FieldDefinition Field, List<string> Options);

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

