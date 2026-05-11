# LibRecord

LibRecord is a **localhost-only** ASP.NET Core Razor Pages app with **two pages**:

- **Main** (`/`): lists and searches uploaded book records, and shows related books.
- **Admin** (`/Admin`): protected by a shared password; lets you manage **custom fields** and add/edit/delete books.

## Requirements

- **Windows 10/11**
- **.NET SDK 10.x (LTS)** (example: `10.0.201`)

Verify:

```powershell
dotnet --info
```

## Setup & Run

From this folder (`d:\New folder (4)\librecord`):

```powershell
dotnet restore
dotnet run
```

Open:

- **Main**: `http://localhost:5000/`
- **Admin**: `http://localhost:5000/Admin`

Stop the server with `Ctrl + C`.

## Admin password (default)

The default admin password is **`BSU123`**.

You can change it in `appsettings.json`:

- `Admin:Password`

## Database

- Uses **SQLite** via Entity Framework Core.
- The database file is created automatically on first run:
  - `librecord.db` (in the project folder)

## Custom fields (dynamic book fields)

Instead of hardcoding columns like Title/Author, the Admin defines fields:

- **Name** (e.g. `Title`, `Author`, `Volume`)
- **Type**: `Text`, `Number`, `Boolean`, `Date`
- **Required** (optional)
- **Searchable** (optional)
- **Filter** (optional): show this field in the filter popup (Text or Boolean fields)
- **Keywords** (optional, **Text** fields only): store multiple values as comma-separated text (e.g. `Development, Software, Management, Database`). Search and filters treat each part as a separate keyword.

When you add/edit a book, the form is automatically generated from your current custom fields. Keyword fields use a **textarea** so you can paste a comma-separated list.

### Field limit

By default, you can create up to **12 fields**. Change:

- `Admin:MaxCustomFields` in `appsettings.json`

## Search & related books

- Search looks at fields marked **Searchable** (especially searchable `Text` fields).
- Related books are computed by token overlap between the top search match and other books across searchable fields.

## Notes

- This project is designed for **local use only** (no hosting/security hardening).
- If you want HTTPS locally, you can run:

```powershell
dotnet dev-certs https --trust
```

## Windows desktop app (MSIX + WebView2)

This repo also includes a **native Windows desktop wrapper** that runs LibRecord offline (local server + SQLite) and renders the UI inside **WebView2**.

### Where data is stored

When launched via the desktop app/MSIX, LibRecord stores writable data under:

- `%LOCALAPPDATA%\LibRecord\`
  - `librecord.db`
  - `imports\` (temporary Excel uploads)
  - `resource\mbook.xlsx` (latest imported Excel copy)

### Build the MSIX installer

Prereqs (build machine only):

- Windows 10/11
- Visual Studio 2022 with MSIX/Desktop Bridge tooling (required to build `.wapproj`)
- .NET SDK 10.x

Steps:

1) Download the **WebView2 Fixed Version Runtime** (x64) and extract it to:

- `src/LibRecord.Package/WebView2Runtime/`

That folder must contain `msedgewebview2.exe`.

2) Build the MSIX (publishes the server self-contained into the package first):

```powershell
pwsh -File .\scripts\build-msix.ps1
```

The output MSIX is produced by the packaging project (open it in Visual Studio):

- `src/LibRecord.Package/LibRecord.Package.wapproj`

## Tauri (Option 1: start the ASP.NET server as a sidecar)

This repo also includes a Tauri wrapper that **bundles** the published `LibRecord.exe` and starts it in the background, then loads `http://127.0.0.1:<port>/` in the Tauri window.

### Build (Tauri)

From repo root:

```powershell
cd .\src\librecord-tauri
npm install

# Build + copy the LibRecord server EXE into src-tauri/binaries/ (sidecar)
powershell -NoProfile -ExecutionPolicy Bypass -File ..\..\scripts\prepare-tauri-sidecar.ps1

# Build installer
npm run build
```

Outputs:

- NSIS installer: `src/librecord-tauri/src-tauri/target/release/bundle/nsis/LibRecord_0.1.0_x64-setup.exe`
- MSI installer: `src/librecord-tauri/src-tauri/target/release/bundle/msi/LibRecord_0.1.0_x64_en-US.msi`

