namespace LibRecord.Models;

public sealed class Book
{
  public int Id { get; set; }
  public int BookCount { get; set; } = 1;
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

  public List<BookFieldValue> FieldValues { get; set; } = [];
}

