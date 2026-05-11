namespace LibRecord.Models;

public sealed class ImportBatchItem
{
  public int Id { get; set; }

  public string ImportBatchId { get; set; } = "";
  public ImportBatch? ImportBatch { get; set; }

  public int BookId { get; set; }
  public Book? Book { get; set; }

  // How many copies this import contributed to Book.BookCount (typically 1 per row).
  public int DeltaCount { get; set; } = 1;
}

