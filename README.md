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

When you add/edit a book, the form is automatically generated from your current custom fields.

### Field limit

By default, you can create up to **6 fields**. Change:

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

