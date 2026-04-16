namespace LibRecord.Models;

public sealed class BookFieldValue
{
  public int Id { get; set; }

  public int BookId { get; set; }
  public Book? Book { get; set; }

  public int FieldDefinitionId { get; set; }
  public FieldDefinition? FieldDefinition { get; set; }

  public string? ValueText { get; set; }
  public decimal? ValueNumber { get; set; }
  public bool? ValueBool { get; set; }
  public DateTime? ValueDate { get; set; }
}

