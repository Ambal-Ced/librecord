namespace LibRecord.Models;

public sealed class ImportBatch
{
  // Use string so we can safely generate GUID ids without EF migrations.
  public string Id { get; set; } = Guid.NewGuid().ToString("n");

  public string OriginalFileName { get; set; } = "";

  public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

  public int TotalRows { get; set; }

  public List<ImportBatchItem> Items { get; set; } = [];
}

