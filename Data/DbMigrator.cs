using Microsoft.EntityFrameworkCore;

namespace LibRecord.Data;

public static class DbMigrator
{
  public static async Task EnsureSchemaAsync(AppDbContext db)
  {
    // This app uses EnsureCreated for simplicity; we add lightweight upgrades here.
    await EnsureFieldDefinitionsHasIsFilterable(db);
    await EnsureFieldDefinitionsHasIsKeywords(db);
    await EnsureBooksHasBookCount(db);
  }

  private static async Task EnsureFieldDefinitionsHasIsFilterable(AppDbContext db)
  {
    var conn = db.Database.GetDbConnection();
    await using var _ = conn;

    if (conn.State != System.Data.ConnectionState.Open)
      await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA table_info('FieldDefinitions');";

    var hasColumn = false;
    await using (var reader = await cmd.ExecuteReaderAsync())
    {
      while (await reader.ReadAsync())
      {
        // PRAGMA table_info: (cid, name, type, notnull, dflt_value, pk)
        var name = reader.GetString(1);
        if (string.Equals(name, "IsFilterable", StringComparison.OrdinalIgnoreCase))
        {
          hasColumn = true;
          break;
        }
      }
    }

    if (hasColumn) return;

    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE \"FieldDefinitions\" ADD COLUMN \"IsFilterable\" INTEGER NOT NULL DEFAULT 0;"
    );
  }

  private static async Task EnsureFieldDefinitionsHasIsKeywords(AppDbContext db)
  {
    var conn = db.Database.GetDbConnection();
    await using var _ = conn;

    if (conn.State != System.Data.ConnectionState.Open)
      await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA table_info('FieldDefinitions');";

    var hasColumn = false;
    await using (var reader = await cmd.ExecuteReaderAsync())
    {
      while (await reader.ReadAsync())
      {
        var name = reader.GetString(1);
        if (string.Equals(name, "IsKeywords", StringComparison.OrdinalIgnoreCase))
        {
          hasColumn = true;
          break;
        }
      }
    }

    if (hasColumn) return;

    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE \"FieldDefinitions\" ADD COLUMN \"IsKeywords\" INTEGER NOT NULL DEFAULT 0;"
    );
  }

  private static async Task EnsureBooksHasBookCount(AppDbContext db)
  {
    var conn = db.Database.GetDbConnection();
    await using var _ = conn;

    if (conn.State != System.Data.ConnectionState.Open)
      await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA table_info('Books');";

    var hasColumn = false;
    await using (var reader = await cmd.ExecuteReaderAsync())
    {
      while (await reader.ReadAsync())
      {
        var name = reader.GetString(1);
        if (string.Equals(name, "BookCount", StringComparison.OrdinalIgnoreCase))
        {
          hasColumn = true;
          break;
        }
      }
    }

    if (hasColumn) return;

    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE \"Books\" ADD COLUMN \"BookCount\" INTEGER NOT NULL DEFAULT 1;"
    );
  }
}

