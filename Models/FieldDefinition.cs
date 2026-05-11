namespace LibRecord.Models;

public sealed class FieldDefinition
{
  public int Id { get; set; }
  public string Name { get; set; } = "";
  public FieldType Type { get; set; }
  public bool IsSearchable { get; set; }
  public bool IsFilterable { get; set; }
  /// <summary>Text field stores comma-separated keywords (e.g. "A, B, C").</summary>
  public bool IsKeywords { get; set; }
  /// <summary>Marks which text field is used as the book title display/sort key.</summary>
  public bool IsTitle { get; set; }
  /// <summary>If set, this field is prioritized (and can be exclusively used) in the book "Details" display.</summary>
  public bool IsDetail { get; set; }
  public bool IsRequired { get; set; }
  public int SortOrder { get; set; }
  public bool IsDeleted { get; set; }
}

