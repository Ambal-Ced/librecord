using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace LibRecord.Services;

public static class AppPaths
{
  private const string EnvVar = "LIBRECORD_DATA_DIR";

  public static string GetDataRoot(IConfiguration config, IWebHostEnvironment env)
  {
    var overrideDir = (Environment.GetEnvironmentVariable(EnvVar) ?? "").Trim();
    if (!string.IsNullOrWhiteSpace(overrideDir))
      return overrideDir;

    var fromConfig = (config["App:DataDir"] ?? "").Trim();
    if (!string.IsNullOrWhiteSpace(fromConfig))
      return fromConfig;

    // Prefer per-user writable location (works for MSIX installs). Fall back to content root for dev convenience
    // if a local DB already exists there.
    var localDevDb = Path.Combine(env.ContentRootPath, "librecord.db");
    if (env.IsDevelopment() && File.Exists(localDevDb))
      return env.ContentRootPath;

    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibRecord");
  }

  public static string GetImportsDir(string dataRoot) => Path.Combine(dataRoot, "imports");
  public static string GetResourceDir(string dataRoot) => Path.Combine(dataRoot, "resource");
}

