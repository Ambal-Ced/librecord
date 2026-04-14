namespace LibRecord.Models;

public sealed class FieldDefinition
{
  public int Id { get; set; }
  public string Name { get; set; } = "";
  public FieldType Type { get; set; }
  public bool IsSearchable { get; set; }
  public bool IsRequired { get; set; }
  public int SortOrder { get; set; }
  public bool IsDeleted { get; set; }
}

