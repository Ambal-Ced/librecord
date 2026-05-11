use std::net::{IpAddr, Ipv4Addr, SocketAddr, TcpListener, TcpStream};
use std::path::PathBuf;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use tauri::{path::BaseDirectory, Manager, RunEvent};
use tauri_plugin_shell::{
  process::{CommandChild, CommandEvent},
  ShellExt,
};

struct ServerChild(Mutex<Option<CommandChild>>);

fn free_port() -> u16 {
  let addr = SocketAddr::new(IpAddr::V4(Ipv4Addr::LOCALHOST), 0);
  let listener = TcpListener::bind(addr).expect("bind ephemeral port");
  listener.local_addr().unwrap().port()
}

fn wait_ready(addr: SocketAddr, timeout: Duration) -> bool {
  let start = Instant::now();
  while start.elapsed() < timeout {
    if TcpStream::connect_timeout(&addr, Duration::from_millis(300)).is_ok() {
      return true;
    }
    std::thread::sleep(Duration::from_millis(200));
  }
  false
}

fn main() {
  let context = tauri::generate_context!();

  let app = tauri::Builder::default()
    .plugin(tauri_plugin_shell::init())
    .setup(|app| {
      app.manage(ServerChild(Mutex::new(None)));

      // Spawn LibRecord server from the bundled resources directory (NOT as a sidecar),
      // so all published .NET files remain adjacent to the executable at runtime.
      let port = free_port();
      let base_url = format!("http://127.0.0.1:{}/", port);
      let addr = SocketAddr::new(IpAddr::V4(Ipv4Addr::LOCALHOST), port);

      let data_root = dirs_next::data_local_dir()
        .unwrap_or_else(|| std::env::temp_dir())
        .join("LibRecord");
      std::fs::create_dir_all(&data_root).ok();

      let server_dir: PathBuf = app
        .path()
        .resolve("binaries/server", BaseDirectory::Resource)
        .map_err(|e| format!("Failed to resolve server resource directory: {}", e))?;
      let server_exe = server_dir.join("LibRecord.exe");
      if !server_exe.exists() {
        return Err(format!(
          "Missing bundled server executable at: {}\n\nRun `npm run prepare:sidecar` and rebuild the app so `binaries/server/**` is bundled.",
          server_exe.display()
        )
        .into());
      }

      let cmd = app
        .shell()
        .command(&server_exe)
        .current_dir(&server_dir)
        .args(["--urls", &base_url.trim_end_matches('/')])
        .env("LIBRECORD_DATA_DIR", data_root.to_string_lossy().to_string());

      let (mut rx, child) = cmd.spawn().expect("failed to spawn server");
      {
        let state = app.state::<ServerChild>();
        *state.0.lock().unwrap() = Some(child);
      }
      let app_handle = app.handle().clone();
      tauri::async_runtime::spawn(async move {
        while let Some(event) = rx.recv().await {
          match event {
            CommandEvent::Stdout(line) => {
              eprintln!(
                "[librecord-server stdout] {}",
                String::from_utf8_lossy(&line)
              )
            }
            CommandEvent::Stderr(line) => {
              eprintln!(
                "[librecord-server stderr] {}",
                String::from_utf8_lossy(&line)
              )
            }
            CommandEvent::Error(err) => {
              eprintln!("LibRecord server process error: {}", err);
              let _ = app_handle.exit(1);
            }
            CommandEvent::Terminated(payload) => {
              eprintln!("LibRecord server process terminated: {:?}", payload);
              let _ = app_handle.exit(1);
            }
            _ => {}
          }
        }
      });

      // Self-contained publish can take a bit to spin up on first run.
      let ok = wait_ready(addr, Duration::from_secs(60));
      if !ok {
        return Err("Timed out waiting for LibRecord server to start".into());
      }

      if let Some(window) = app.get_webview_window("main") {
        // Navigate from the packaged placeholder HTML to the local server.
        let js = format!(
          "window.location.replace({});",
          serde_json::to_string(&base_url).unwrap()
        );
        let _ = window.eval(&js);
      }

      Ok(())
    })
    .build(context)
    .expect("error while building tauri application");

  app.run(|app_handle, event| {
    if matches!(event, RunEvent::ExitRequested { .. } | RunEvent::Exit) {
      if let Some(child) = app_handle
        .state::<ServerChild>()
        .0
        .lock()
        .ok()
        .and_then(|mut guard| guard.take())
      {
        let _ = child.kill();
      }
    }
  });
}

