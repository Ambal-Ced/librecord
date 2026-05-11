using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;

namespace LibRecord.Desktop;

public partial class MainWindow : Window
{
  private Process? _serverProcess;
  private Uri? _appUri;

  public MainWindow()
  {
    InitializeComponent();
    Loaded += (_, __) => _ = StartAsync();
    Closed += (_, __) => StopServer();
  }

  private async Task StartAsync()
  {
    try
    {
      StatusText.Text = "Starting local server...";

      var port = GetFreeTcpPort();
      _appUri = new Uri($"http://127.0.0.1:{port}/");

      var serverExe = Path.Combine(AppContext.BaseDirectory, "Server", "LibRecord.exe");
      if (!File.Exists(serverExe))
      {
        StatusText.Text = "Missing server files.";
        MessageBox.Show(
          this,
          $"Server executable not found:\n{serverExe}\n\nRebuild the MSIX package so it includes the published server under a 'Server' folder.",
          "LibRecord",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
        return;
      }

      var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibRecord");
      Directory.CreateDirectory(dataRoot);

      _serverProcess = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = serverExe,
          WorkingDirectory = Path.GetDirectoryName(serverExe) ?? AppContext.BaseDirectory,
          Arguments = $"--urls \"{_appUri.AbsoluteUri.TrimEnd('/')}\"",
          UseShellExecute = false,
          CreateNoWindow = true,
        },
        EnableRaisingEvents = true,
      };

      _serverProcess.StartInfo.Environment["LIBRECORD_DATA_DIR"] = dataRoot;
      _serverProcess.Start();

      await WaitUntilReadyAsync(_appUri, TimeSpan.FromSeconds(25));

      StatusText.Text = "Loading...";
      await EnsureWebViewAsync();
      Browser.Source = _appUri;
      StatusText.Text = "";
    }
    catch (Exception ex)
    {
      StatusText.Text = "Failed to start.";
      MessageBox.Show(this, ex.Message, "LibRecord", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private async Task EnsureWebViewAsync()
  {
    var userData = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "LibRecord",
      "WebView2UserData");
    Directory.CreateDirectory(userData);

    var fixedRuntime = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
    CoreWebView2Environment env;

    if (Directory.Exists(fixedRuntime))
    {
      env = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: fixedRuntime, userDataFolder: userData);
    }
    else
    {
      // Fallback: use installed runtime if present (the MSIX packaging step is expected to include fixed runtime).
      env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
    }

    await Browser.EnsureCoreWebView2Async(env);
    Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
  }

  private static int GetFreeTcpPort()
  {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
  }

  private static async Task WaitUntilReadyAsync(Uri baseUri, TimeSpan timeout)
  {
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var start = DateTime.UtcNow;

    while (DateTime.UtcNow - start < timeout)
    {
      try
      {
        using var resp = await http.GetAsync(baseUri);
        if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 500)
          return;
      }
      catch
      {
        // ignore and retry
      }

      await Task.Delay(250);
    }

    throw new TimeoutException("Timed out waiting for the local LibRecord server to start.");
  }

  private void StopServer()
  {
    try
    {
      if (_serverProcess is null) return;
      if (_serverProcess.HasExited) return;

      _serverProcess.Kill(entireProcessTree: true);
    }
    catch
    {
      // ignore
    }
    finally
    {
      _serverProcess?.Dispose();
      _serverProcess = null;
    }
  }
}

