using LibRecord.Data;
using LibRecord.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LibRecord.Pages;

public sealed class IndexModel : PageModel
{
  private readonly AppDbContext _db;

  public IndexModel(AppDbContext db)
  {
    _db = db;
  }

  public string Query { get; private set; } = "";
  public List<FieldDefinition> Fields { get; private set; } = [];

  public List<BookCard> Results { get; private set; } = [];
  public List<BookCard> Related { get; private set; } = [];

  public async Task OnGetAsync(string? q)
  {
    Query = (q ?? "").Trim();

    Fields = await _db.FieldDefinitions
      .Where(x => !x.IsDeleted)
      .OrderBy(x => x.SortOrder)
      .ToListAsync();

    var books = await _db.Books
      .Include(b => b.FieldValues)
      .ThenInclude(v => v.FieldDefinition)
      .OrderByDescending(b => b.UpdatedAt)
      .Take(200)
      .ToListAsync();

    if (string.IsNullOrWhiteSpace(Query))
    {
      Results = books.Select(b => BookCardMapper.From(b, Fields)).ToList();
      Related = [];
      return;
    }

    var searchableFieldIds = Fields.Where(f => f.IsSearchable).Select(f => f.Id).ToHashSet();
    var queryTokens = Tokenize(Query);

    var scored = new List<(Book Book, int Score)>();
    foreach (var book in books)
    {
      var text = string.Join(
        " ",
        book.FieldValues
          .Where(v => v.FieldDefinition is not null && searchableFieldIds.Contains(v.FieldDefinitionId))
          .Select(v => v.ValueText ?? v.ValueNumber?.ToString() ?? v.ValueBool?.ToString() ?? v.ValueDate?.ToString("yyyy-MM-dd") ?? "")
      );

      var score = ScoreTokens(queryTokens, Tokenize(text));
      if (score > 0) scored.Add((book, score));
    }

    Results = scored
      .OrderByDescending(x => x.Score)
      .ThenByDescending(x => x.Book.UpdatedAt)
      .Take(50)
      .Select(x => BookCardMapper.From(x.Book, Fields))
      .ToList();

    var top = scored.OrderByDescending(x => x.Score).Select(x => x.Book).FirstOrDefault();
    if (top is null)
    {
      Related = [];
      return;
    }

    Related = ComputeRelated(top, books, Fields, queryTokens)
      .Select(b => BookCardMapper.From(b, Fields))
      .ToList();
  }

  private static List<Book> ComputeRelated(Book top, List<Book> all, List<FieldDefinition> fields, HashSet<string> queryTokens)
  {
    var searchable = fields.Where(f => f.IsSearchable).ToList();
    var searchableTextIds = searchable.Where(f => f.Type == FieldType.Text).Select(f => f.Id).ToHashSet();

    var topText = string.Join(
      " ",
      top.FieldValues
        .Where(v => v.FieldDefinition is not null && searchableTextIds.Contains(v.FieldDefinitionId))
        .Select(v => v.ValueText ?? "")
    );

    var baseTokens = Tokenize(topText);
    foreach (var t in queryTokens) baseTokens.Add(t);

    var ranked = new List<(Book Book, int Score)>();
    foreach (var other in all)
    {
      if (other.Id == top.Id) continue;

      var otherText = string.Join(
        " ",
        other.FieldValues
          .Where(v => v.FieldDefinition is not null && searchableTextIds.Contains(v.FieldDefinitionId))
          .Select(v => v.ValueText ?? "")
      );

      var score = ScoreTokens(baseTokens, Tokenize(otherText));
      if (score > 0) ranked.Add((other, score));
    }

    return ranked
      .OrderByDescending(x => x.Score)
      .ThenByDescending(x => x.Book.UpdatedAt)
      .Take(10)
      .Select(x => x.Book)
      .ToList();
  }

  private static HashSet<string> Tokenize(string input)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var raw in input.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '.', ':', '-', '_', '/', '\\', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
    {
      var t = raw.Trim();
      if (t.Length < 2) continue;
      set.Add(t);
    }
    return set;
  }

  private static int ScoreTokens(HashSet<string> queryTokens, HashSet<string> textTokens)
  {
    var score = 0;
    foreach (var t in queryTokens)
    {
      if (textTokens.Contains(t)) score += 3;
    }
    return score;
  }

  public sealed record BookCard(int Id, string Title, List<(string Name, string Value)> Lines);

  private static class BookCardMapper
  {
    public static IndexModel.BookCard From(Book book, List<FieldDefinition> fields)
    {
      var byField = book.FieldValues
        .Where(v => v.FieldDefinition is not null)
        .ToDictionary(v => v.FieldDefinitionId, v => v, EqualityComparer<int>.Default);

      string? title = null;
      var titleField = fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase));
      if (titleField is not null && byField.TryGetValue(titleField.Id, out var tv)) title = tv.ValueText;

      title ??= book.Id.ToString();

      var lines = new List<(string Name, string Value)>();
      foreach (var f in fields.Where(f => !f.IsDeleted).OrderBy(f => f.SortOrder))
      {
        if (!byField.TryGetValue(f.Id, out var v)) continue;
        var rendered = RenderValue(f, v);
        if (string.IsNullOrWhiteSpace(rendered)) continue;
        if (string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase)) continue;
        lines.Add((f.Name, rendered));
        if (lines.Count >= 4) break;
      }

      return new IndexModel.BookCard(book.Id, title, lines);
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

