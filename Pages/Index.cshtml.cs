using LibRecord.Data;
using LibRecord.Models;
using LibRecord.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text;

namespace LibRecord.Pages;

public sealed class IndexModel : PageModel
{
  private readonly AppDbContext _db;
  private readonly IWebHostEnvironment _env;

  public IndexModel(AppDbContext db, IWebHostEnvironment env)
  {
    _db = db;
    _env = env;
  }

  public string Query { get; private set; } = "";
  public string AdvancedKeywords { get; private set; } = "";
  public List<FieldDefinition> Fields { get; private set; } = [];

  public string? Sort { get; private set; }
  public DateOnly? UploadedFrom { get; private set; }
  public DateOnly? UploadedTo { get; private set; }

  public Dictionary<int, HashSet<string>> ActiveFilters { get; private set; } = [];
  public List<FilterField> FilterFields { get; private set; } = [];

  public List<BookCard> Results { get; private set; } = [];
  public List<BookCard> Related { get; private set; } = [];

  public int DbBookCount { get; private set; }
  public int? ResourceRowCount { get; private set; }

  public int PageNumber { get; private set; } = 1;
  public bool HasMore { get; private set; }
  /// <summary>Total matching rows for current filters (browse or search).</summary>
  public int TotalResultsCount { get; private set; }
  /// <summary>Total pages for current result set (page size 5).</summary>
  public int TotalPages { get; private set; }
  public int FoundAndRelatedCount { get; private set; }
  public bool HasAnySearch { get; private set; }

  /// <summary>Query string (starts with ?) preserving filters/search/sort for pagination links.</summary>
  public string BuildPagerQuery(int page)
  {
    var qb = new QueryBuilder();
    if (!string.IsNullOrWhiteSpace(Query)) qb.Add("q", Query);
    if (!string.IsNullOrWhiteSpace(AdvancedKeywords)) qb.Add("ak", AdvancedKeywords);
    if (!string.IsNullOrWhiteSpace(Sort)) qb.Add("sort", Sort);
    if (UploadedFrom is not null) qb.Add("uploadedFrom", UploadedFrom.Value.ToString("yyyy-MM-dd"));
    if (UploadedTo is not null) qb.Add("uploadedTo", UploadedTo.Value.ToString("yyyy-MM-dd"));
    qb.Add("p", page.ToString());
    foreach (var (fieldId, set) in ActiveFilters)
    {
      foreach (var v in set)
        qb.Add($"f_{fieldId}", v);
    }

    return qb.ToQueryString().Value ?? "";
  }

  public async Task OnGetAsync(string? q, string? ak, int? p, string? sort, DateOnly? uploadedFrom, DateOnly? uploadedTo)
  {
    Query = (q ?? "").Trim();
    AdvancedKeywords = (ak ?? "").Trim();
    PageNumber = Math.Max(1, p ?? 1);
    Sort = (sort ?? "").Trim();
    UploadedFrom = uploadedFrom;
    UploadedTo = uploadedTo;

    Fields = await _db.FieldDefinitions
      .Where(x => !x.IsDeleted)
      .OrderBy(x => x.SortOrder)
      .ToListAsync();

    ActiveFilters = ReadFieldFilters(Fields.Where(f => f.IsFilterable));
    FilterFields = await BuildFilterFieldsAsync(Fields.Where(f => f.IsFilterable).ToList());

    DbBookCount = await _db.Books.CountAsync();
    ResourceRowCount = TryCountResourceRows();

    var books = await _db.Books
      .Include(b => b.FieldValues)
      .ThenInclude(v => v.FieldDefinition)
      .OrderByDescending(b => b.UpdatedAt)
      .ToListAsync();

    books = ApplyUploadedDateFilter(books, UploadedFrom, UploadedTo);
    books = ApplyFieldFilters(books, ActiveFilters, Fields);
    books = ApplySort(books, Fields, Sort);

    const int pageSize = 5;

    HasAnySearch = !string.IsNullOrWhiteSpace(Query) || !string.IsNullOrWhiteSpace(AdvancedKeywords);
    if (!HasAnySearch)
    {
      TotalResultsCount = books.Count;
      TotalPages = TotalResultsCount == 0 ? 0 : (int)Math.Ceiling(TotalResultsCount / (double)pageSize);
      if (TotalPages > 0)
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
      else
        PageNumber = 1;

      var skip = (PageNumber - 1) * pageSize;
      HasMore = TotalPages > 0 && PageNumber < TotalPages;
      Results = books.Skip(skip).Take(pageSize).Select(b => BookCardMapper.From(b, Fields)).ToList();
      Related = [];
      FoundAndRelatedCount = 0;
      return;
    }

    // Search should include both keywords and the same "details" users see in results,
    // not just the fields explicitly marked searchable.
    var queryHasDigit = (Query + " " + AdvancedKeywords).Any(char.IsDigit);
    var searchableFieldIds = Fields
      .Where(f =>
        !f.IsDeleted &&
        (f.Type == FieldType.Text || f.Type == FieldType.Number || f.Type == FieldType.Date || f.Type == FieldType.Boolean) &&
        (f.IsSearchable || f.IsKeywords || queryHasDigit)
      )
      .Select(f => f.Id)
      .ToHashSet();
    var fieldById = Fields.ToDictionary(f => f.Id);
    var normalizedQuery = NormalizeForSearch(Query);
    var queryTokens = Tokenize(normalizedQuery);
    var normalizedAk = NormalizeForSearch(string.Join(" ", KeywordHelper.SplitKeywords(AdvancedKeywords)));
    var requiredTokens = Tokenize(normalizedAk);
    var hasMainQuery = queryTokens.Length > 0;

    var scored = new List<(Book Book, int Score)>();
    foreach (var book in books)
    {
      var text = string.Join(
        " ",
        book.FieldValues
          .Where(v => fieldById.ContainsKey(v.FieldDefinitionId) && searchableFieldIds.Contains(v.FieldDefinitionId))
          .Select(v => SearchableFieldText(fieldById[v.FieldDefinitionId], v))
      );

      var normalizedText = NormalizeForSearch(text);
      var textTokens = Tokenize(normalizedText);

      // 1) Main search (q) determines the initial candidate set.
      // 2) Advanced keywords (ak) then narrow that set for precision.
      var score = 0;

      if (hasMainQuery)
      {
        score = ScoreQueryAgainstText(queryTokens, normalizedQuery, [], "", textTokens, normalizedText);
        if (score <= 0) continue;
      }

      // Advanced keywords are treated as "must match" constraints for precision.
      if (!AllTokensMatch(requiredTokens, textTokens)) continue;

      if (!hasMainQuery)
      {
        // When there's no main query, advanced keywords become the primary search.
        score = ScoreQueryAgainstText(requiredTokens, normalizedAk, [], "", textTokens, normalizedText);
      }
      else if (requiredTokens.Length > 0)
      {
        // When there is a main query, advanced keywords should NOT overpower ranking;
        // they only narrow + provide a small boost.
        score += ScoreAdvancedBoost(requiredTokens, normalizedAk, textTokens, normalizedText);
      }

      if (score > 0) scored.Add((book, score));
    }

    var ordered = scored
      .OrderByDescending(x => x.Score)
      .ThenByDescending(x => x.Book.UpdatedAt)
      .ToList();

    TotalResultsCount = ordered.Count;
    TotalPages = TotalResultsCount == 0 ? 0 : (int)Math.Ceiling(TotalResultsCount / (double)pageSize);
    if (TotalPages > 0)
      PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
    else
      PageNumber = 1;

    var skipSearch = (PageNumber - 1) * pageSize;
    HasMore = TotalPages > 0 && PageNumber < TotalPages;
    Results = ordered
      .Skip(skipSearch)
      .Take(pageSize)
      .Select(x => BookCardMapper.From(x.Book, Fields))
      .ToList();

    var top = scored.OrderByDescending(x => x.Score).Select(x => x.Book).FirstOrDefault();
    if (top is null)
    {
      Related = [];
      return;
    }

    Related = ComputeRelated(top, books, Fields, queryTokens, requiredTokens)
      .Select(b => BookCardMapper.From(b, Fields))
      .ToList();

    var ids = new HashSet<int>();
    foreach (var x in ordered) ids.Add(x.Book.Id);
    foreach (var r in Related) ids.Add(r.Id);
    FoundAndRelatedCount = ids.Count;
  }

  private int? TryCountResourceRows()
  {
    try
    {
      var dataRoot = AppPaths.GetDataRoot(Request.HttpContext.RequestServices.GetRequiredService<IConfiguration>(), _env);
      var path = Path.Combine(AppPaths.GetResourceDir(dataRoot), "mbook.xlsx");
      if (!System.IO.File.Exists(path)) return null;

      using var wb = new XLWorkbook(path);
      var ws = wb.Worksheets.First();

      var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
      var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
      if (lastRow <= 0 || lastCol <= 0) return 0;

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

      // Treat row 1 as header if it matches at least 2 configured fields.
      var headerMatches = 0;
      var max = Math.Min(Fields.Count, lastCol);
      for (var c = 1; c <= max; c++)
      {
        var s = (ws.Cell(1, c).GetString() ?? "").Trim();
        if (Fields.Count > 0 && string.Equals(s, Fields[c - 1].Name, StringComparison.OrdinalIgnoreCase))
          headerMatches++;
      }
      var startRow = headerMatches >= Math.Min(2, max) ? 2 : 1;

      var count = 0;
      for (var r = startRow; r <= lastRow; r++)
      {
        var allBlank = true;
        for (var c = 1; c <= max; c++)
        {
          var s = ws.Cell(r, c).GetString();
          if (!IsEffectivelyBlank(s)) { allBlank = false; break; }
        }
        if (!allBlank) count++;
      }
      return count;
    }
    catch
    {
      return null;
    }
  }

  private static string SearchableFieldText(FieldDefinition fd, BookFieldValue v)
  {
    if (fd.Type == FieldType.Text && fd.IsKeywords)
      return string.Join(" ", KeywordHelper.SplitKeywords(v.ValueText));
    return v.ValueText ?? v.ValueNumber?.ToString() ?? v.ValueBool?.ToString() ?? v.ValueDate?.ToString("yyyy-MM-dd") ?? "";
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

  private static List<Book> ApplySort(List<Book> books, List<FieldDefinition> fields, string? sort)
  {
    sort = (sort ?? "").Trim().ToLowerInvariant();

    if (sort == "uploaded_old")
      return books.OrderBy(b => b.CreatedAt).ToList();
    if (sort == "title_az" || sort == "title_za")
    {
      var titleField = fields.FirstOrDefault(f => f.Type == FieldType.Text && f.IsTitle)
        ?? fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase));
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

  private static List<Book> ComputeRelated(Book top, List<Book> all, List<FieldDefinition> fields, string[] queryTokens, string[] requiredTokens)
  {
    var searchable = fields.Where(f => f.IsSearchable).ToList();
    var searchableTextIds = searchable.Where(f => f.Type == FieldType.Text).Select(f => f.Id).ToHashSet();
    var fdById = fields.ToDictionary(f => f.Id);

    string TextFor(Book b)
    {
      return string.Join(
        " ",
        b.FieldValues
          .Where(v => searchableTextIds.Contains(v.FieldDefinitionId))
          .Select(v =>
          {
            if (!fdById.TryGetValue(v.FieldDefinitionId, out var fd)) return "";
            if (fd.IsKeywords) return string.Join(" ", KeywordHelper.SplitKeywords(v.ValueText));
            return v.ValueText ?? "";
          }));
    }

    var topText = TextFor(top);

    var topNormalized = NormalizeForSearch(topText);
    var baseTokens = Tokenize(topNormalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var t in queryTokens) baseTokens.Add(t);
    foreach (var t in requiredTokens) baseTokens.Add(t);
    var baseTokensArray = baseTokens.ToArray();

    var ranked = new List<(Book Book, int Score)>();
    foreach (var other in all)
    {
      if (other.Id == top.Id) continue;

      var otherText = TextFor(other);
      var otherNormalized = NormalizeForSearch(otherText);
      var otherTokens = Tokenize(otherNormalized);
      if (!AllTokensMatch(requiredTokens, otherTokens)) continue;

      var score = ScoreQueryAgainstText(baseTokensArray, "", requiredTokens, "", otherTokens, otherNormalized);
      if (score > 0) ranked.Add((other, score));
    }

    return ranked
      .OrderByDescending(x => x.Score)
      .ThenByDescending(x => x.Book.UpdatedAt)
      .Take(10)
      .Select(x => x.Book)
      .ToList();
  }

  private static string[] Tokenize(string input)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var raw in input.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '.', ':', '-', '_', '/', '\\', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
    {
      var t = raw.Trim();
      if (t.Length < 2) continue;
      set.Add(t);
    }
    return set.ToArray();
  }

  private static int ScoreQueryAgainstText(
    string[] queryTokens,
    string normalizedQuery,
    string[] requiredTokens,
    string normalizedRequiredPhrase,
    string[] textTokens,
    string normalizedText)
  {
    if (textTokens.Length == 0) return 0;

    // Exact phrase boost (helps for multi-word queries).
    var score = 0;
    if (!string.IsNullOrWhiteSpace(normalizedQuery) &&
        normalizedQuery.Length >= 3 &&
        normalizedText.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
    {
      score += 8;
    }

    if (!string.IsNullOrWhiteSpace(normalizedRequiredPhrase) &&
        normalizedRequiredPhrase.Length >= 3 &&
        normalizedText.Contains(normalizedRequiredPhrase, StringComparison.OrdinalIgnoreCase))
    {
      score += 10;
    }

    var tokenSet = textTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var q in queryTokens)
    {
      if (q.Length == 0) continue;

      if (tokenSet.Contains(q))
      {
        score += 6; // exact token match
        continue;
      }

      // Prefix match is the common "smart" expectation: "amb" should match "ambal".
      if (q.Length >= 2 && textTokens.Any(t => t.StartsWith(q, StringComparison.OrdinalIgnoreCase)))
      {
        score += 3;
        continue;
      }

      // Substring fallback for mild fuzziness ("ced" in "cedrick").
      if (q.Length >= 3 && textTokens.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
      {
        score += 1;
      }
    }

    // Advanced keywords: they already passed the "must match" check, so reward them more.
    foreach (var k in requiredTokens)
    {
      if (k.Length == 0) continue;
      if (tokenSet.Contains(k)) score += 10;
      else if (k.Length >= 2 && textTokens.Any(t => t.StartsWith(k, StringComparison.OrdinalIgnoreCase))) score += 6;
      else if (k.Length >= 3 && textTokens.Any(t => t.Contains(k, StringComparison.OrdinalIgnoreCase))) score += 3;
    }

    return score;
  }

  private static int ScoreAdvancedBoost(string[] requiredTokens, string normalizedRequiredPhrase, string[] textTokens, string normalizedText)
  {
    var score = 0;
    if (!string.IsNullOrWhiteSpace(normalizedRequiredPhrase) &&
        normalizedRequiredPhrase.Length >= 3 &&
        normalizedText.Contains(normalizedRequiredPhrase, StringComparison.OrdinalIgnoreCase))
    {
      score += 2;
    }

    var tokenSet = textTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var k in requiredTokens)
    {
      if (k.Length == 0) continue;
      if (tokenSet.Contains(k)) score += 2;
      else if (k.Length >= 2 && textTokens.Any(t => t.StartsWith(k, StringComparison.OrdinalIgnoreCase))) score += 1;
    }
    return score;
  }

  private static bool AllTokensMatch(string[] requiredTokens, string[] textTokens)
  {
    if (requiredTokens.Length == 0) return true;
    if (textTokens.Length == 0) return false;

    foreach (var r in requiredTokens)
    {
      if (r.Length == 0) continue;
      if (textTokens.Any(t =>
            string.Equals(t, r, StringComparison.OrdinalIgnoreCase) ||
            (r.Length >= 2 && t.StartsWith(r, StringComparison.OrdinalIgnoreCase)) ||
            (r.Length >= 3 && t.Contains(r, StringComparison.OrdinalIgnoreCase))))
      {
        continue;
      }
      return false;
    }
    return true;
  }

  private static string NormalizeForSearch(string input)
  {
    if (string.IsNullOrWhiteSpace(input)) return "";

    // Lower + remove diacritics + normalize whitespace/punctuation into spaces.
    var formD = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
    var sb = new StringBuilder(formD.Length);

    foreach (var ch in formD)
    {
      var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
      if (cat == UnicodeCategory.NonSpacingMark) continue;

      if (char.IsLetterOrDigit(ch))
      {
        sb.Append(ch);
        continue;
      }

      // Treat everything else as a separator.
      sb.Append(' ');
    }

    return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
  }

  public sealed record BookCard(int Id, string Title, int BookCount, List<(string Name, string Value)> Lines, List<string> Keywords);

  private static class BookCardMapper
  {
    public static IndexModel.BookCard From(Book book, List<FieldDefinition> fields)
    {
      var byField = book.FieldValues
        .Where(v => v.FieldDefinition is not null)
        .ToDictionary(v => v.FieldDefinitionId, v => v, EqualityComparer<int>.Default);

      string? title = null;
      var titleField = fields.FirstOrDefault(f => f.Type == FieldType.Text && f.IsTitle)
        ?? fields.FirstOrDefault(f => f.Type == FieldType.Text && string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase));
      if (titleField is not null && byField.TryGetValue(titleField.Id, out var tv)) title = tv.ValueText;

      title ??= book.Id.ToString();

      var keywordField = fields.FirstOrDefault(f => !f.IsDeleted && f.Type == FieldType.Text && f.IsKeywords);
      var keywords = new List<string>();
      if (keywordField is not null && byField.TryGetValue(keywordField.Id, out var kv))
      {
        keywords = KeywordHelper.SplitKeywords(kv.ValueText).ToList();
      }

      var anyDetailSelected = fields.Any(f =>
        !f.IsDeleted &&
        f.IsDetail &&
        !f.IsKeywords &&
        !(f.IsTitle || string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase)));
      var maxLines = anyDetailSelected ? 50 : 4;
      var lines = new List<(string Name, string Value)>();
      foreach (var f in fields.Where(f => !f.IsDeleted).OrderBy(f => f.SortOrder))
      {
        if (anyDetailSelected && !f.IsDetail) continue;
        if (!byField.TryGetValue(f.Id, out var v)) continue;
        var rendered = RenderValue(f, v);
        if (string.IsNullOrWhiteSpace(rendered)) continue;
        if (f.IsTitle || string.Equals(f.Name, "Title", StringComparison.OrdinalIgnoreCase)) continue;
        if (f.IsKeywords) continue; // rendered separately as keyword pills
        lines.Add((f.Name, rendered));
        if (lines.Count >= maxLines) break;
      }

      return new IndexModel.BookCard(book.Id, title, book.BookCount, lines, keywords);
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

