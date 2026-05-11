using LibRecord.Models;
using Microsoft.EntityFrameworkCore;

namespace LibRecord.Data;

public static class DbSeeder
{
  public static async Task SeedAsync(AppDbContext db)
  {
    // If the user has ever created fields, don't re-seed defaults.
    // (Soft-deleted rows still exist and would also block due to UNIQUE Name index.)
    var hasAnyFields = await db.FieldDefinitions.AnyAsync();
    if (hasAnyFields) return;

    var fields = new[]
    {
      new FieldDefinition { Name = "Title", Type = FieldType.Text, IsRequired = true, IsSearchable = true, IsFilterable = false, IsKeywords = false, IsTitle = true, IsDetail = false, SortOrder = 1 },
      new FieldDefinition { Name = "Author", Type = FieldType.Text, IsRequired = false, IsSearchable = true, IsFilterable = false, IsKeywords = false, IsTitle = false, IsDetail = true, SortOrder = 2 },
      new FieldDefinition { Name = "Number", Type = FieldType.Text, IsRequired = false, IsSearchable = true, IsFilterable = false, IsKeywords = false, IsTitle = false, IsDetail = true, SortOrder = 3 },
      new FieldDefinition { Name = "Volume", Type = FieldType.Text, IsRequired = false, IsSearchable = true, IsFilterable = false, IsKeywords = false, IsTitle = false, IsDetail = true, SortOrder = 4 },
      new FieldDefinition { Name = "PageCount", Type = FieldType.Number, IsRequired = false, IsSearchable = false, IsFilterable = false, IsKeywords = false, IsTitle = false, IsDetail = false, SortOrder = 5 },
      new FieldDefinition { Name = "Keywords", Type = FieldType.Text, IsRequired = false, IsSearchable = true, IsFilterable = false, IsKeywords = true, IsTitle = false, IsDetail = false, SortOrder = 6 },
    };

    db.FieldDefinitions.AddRange(fields);
    await db.SaveChangesAsync();
  }
}

