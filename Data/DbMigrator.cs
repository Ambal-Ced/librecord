using Microsoft.EntityFrameworkCore;

namespace LibRecord.Data;

public static class DbMigrator
{
  public static async Task EnsureSchemaAsync(AppDbContext db)
  {
    // This app uses EnsureCreated for simplicity; we add lightweight upgrades here.
    await EnsureFieldDefinitionsHasIsFilterable(db);
    await EnsureFieldDefinitionsHasIsKeywords(db);
    await EnsureFieldDefinitionsHasIsTitle(db);
    await EnsureFieldDefinitionsHasIsDetail(db);
    await EnsureBooksHasBookCount(db);
    await EnsureImportBatchesTables(db);
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

  private static async Task EnsureFieldDefinitionsHasIsTitle(AppDbContext db)
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
        if (string.Equals(name, "IsTitle", StringComparison.OrdinalIgnoreCase))
        {
          hasColumn = true;
          break;
        }
      }
    }

    if (hasColumn) return;

    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE \"FieldDefinitions\" ADD COLUMN \"IsTitle\" INTEGER NOT NULL DEFAULT 0;"
    );
  }

  private static async Task EnsureFieldDefinitionsHasIsDetail(AppDbContext db)
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
        if (string.Equals(name, "IsDetail", StringComparison.OrdinalIgnoreCase))
        {
          hasColumn = true;
          break;
        }
      }
    }

    if (hasColumn) return;

    await db.Database.ExecuteSqlRawAsync(
      "ALTER TABLE \"FieldDefinitions\" ADD COLUMN \"IsDetail\" INTEGER NOT NULL DEFAULT 0;"
    );
  }

  private static async Task EnsureImportBatchesTables(AppDbContext db)
  {
    // SQLite: CREATE TABLE IF NOT EXISTS is safe for upgrades.
    await db.Database.ExecuteSqlRawAsync(
      """
      CREATE TABLE IF NOT EXISTS "ImportBatches" (
        "Id" TEXT NOT NULL CONSTRAINT "PK_ImportBatches" PRIMARY KEY,
        "OriginalFileName" TEXT NOT NULL,
        "ImportedAt" TEXT NOT NULL,
        "TotalRows" INTEGER NOT NULL
      );
      """
    );

    await db.Database.ExecuteSqlRawAsync(
      """
      CREATE TABLE IF NOT EXISTS "ImportBatchItems" (
        "Id" INTEGER NOT NULL CONSTRAINT "PK_ImportBatchItems" PRIMARY KEY AUTOINCREMENT,
        "ImportBatchId" TEXT NOT NULL,
        "BookId" INTEGER NOT NULL,
        "DeltaCount" INTEGER NOT NULL DEFAULT 1,
        CONSTRAINT "FK_ImportBatchItems_ImportBatches_ImportBatchId" FOREIGN KEY ("ImportBatchId") REFERENCES "ImportBatches" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ImportBatchItems_Books_BookId" FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE CASCADE
      );
      """
    );

    await db.Database.ExecuteSqlRawAsync(
      """CREATE INDEX IF NOT EXISTS "IX_ImportBatches_ImportedAt" ON "ImportBatches" ("ImportedAt");"""
    );
    await db.Database.ExecuteSqlRawAsync(
      """CREATE INDEX IF NOT EXISTS "IX_ImportBatchItems_ImportBatchId" ON "ImportBatchItems" ("ImportBatchId");"""
    );
    await db.Database.ExecuteSqlRawAsync(
      """CREATE INDEX IF NOT EXISTS "IX_ImportBatchItems_BookId" ON "ImportBatchItems" ("BookId");"""
    );
  }
}

