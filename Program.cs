using LibRecord.Data;
using LibRecord.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));

var dataRoot = AppPaths.GetDataRoot(builder.Configuration, builder.Environment);
Directory.CreateDirectory(dataRoot);

// Ensure resource folder exists and (on first run) copy packaged sample file into the writable location.
var packagedResourceDir = Path.Combine(builder.Environment.ContentRootPath, "resource");
var packagedMbook = Path.Combine(packagedResourceDir, "mbook.xlsx");
var writableResourceDir = AppPaths.GetResourceDir(dataRoot);
Directory.CreateDirectory(writableResourceDir);
var writableMbook = Path.Combine(writableResourceDir, "mbook.xlsx");
try
{
  if (!File.Exists(writableMbook) && File.Exists(packagedMbook))
    File.Copy(packagedMbook, writableMbook);
}
catch { /* ignore */ }

builder.Services.AddDbContext<AppDbContext>(options =>
{
  var configured = builder.Configuration.GetConnectionString("Default") ?? "";
  var sqlite = new SqliteConnectionStringBuilder(configured);
  sqlite.DataSource = Path.Combine(dataRoot, "librecord.db");
  options.UseSqlite(sqlite.ToString());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
  var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
  await db.Database.EnsureCreatedAsync();
  await DbMigrator.EnsureSchemaAsync(db);
  await DbSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error");
  app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapRazorPages();

app.Run();

