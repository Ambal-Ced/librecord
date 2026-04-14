using LibRecord.Models;
using Microsoft.EntityFrameworkCore;

namespace LibRecord.Data;

public static class DbSeeder
{
  public static async Task SeedAsync(AppDbContext db)
  {
    var hasAnyFields = await db.FieldDefinitions.AnyAsync(x => !x.IsDeleted);
    if (hasAnyFields) return;

    var fields = new[]
    {
      new FieldDefinition { Name = "Title", Type = FieldType.Text, IsRequired = true, IsSearchable = true, SortOrder = 1 },
      new FieldDefinition { Name = "Author", Type = FieldType.Text, IsRequired = false, IsSearchable = true, SortOrder = 2 },
      new FieldDefinition { Name = "Number", Type = FieldType.Text, IsRequired = false, IsSearchable = true, SortOrder = 3 },
      new FieldDefinition { Name = "Volume", Type = FieldType.Text, IsRequired = false, IsSearchable = true, SortOrder = 4 },
      new FieldDefinition { Name = "PageCount", Type = FieldType.Number, IsRequired = false, IsSearchable = false, SortOrder = 5 },
      new FieldDefinition { Name = "Keywords", Type = FieldType.Text, IsRequired = false, IsSearchable = true, SortOrder = 6 },
    };

    db.FieldDefinitions.AddRange(fields);
    await db.SaveChangesAsync();
  }
}

