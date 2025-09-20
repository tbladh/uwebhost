# uWebHost

uWebHost is a minimal cross-platform development web server written in .NET 8. It scans the local `www` directory, lists each project directory, and serves the static assets inside so you can iterate quickly on HTML/JavaScript utilities, games, and experiments.

## Features
- Serves any project placed under the `www` directory with directory listings and static file streaming.
- Generates a landing page that lists available projects with one-click launch buttons.
- Offers basic content-type detection for common web assets (HTML, CSS, JS, images, fonts, media, etc.).
- Prevents path traversal by sandboxing requests inside the `www` root.
- Responds to `GET` and `HEAD` requests and sends helpful error pages for invalid requests.
- Automatically attempts to open the default web browser on startup (gracefully logs failures on headless systems).

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Windows, macOS, or Linux with a default browser configured (browser launch is optional).

## Getting Started
1. Clone or copy the repository.
2. Place your static projects under `src/uwebhost/www/` (each project gets its own subdirectory).
3. Build the server:
   ```bash
   dotnet build src/uwebhost/uwebhost.csproj
   ```
4. Run the server (optional port argument defaults to `5000`):
   ```bash
   dotnet run --project src/uwebhost/uwebhost.csproj -- 8080
   ```
5. Visit `http://localhost:<port>/` — the landing page lists every project under `www`. Selecting **Play** opens the app in the browser.

### Running the Published Binary
```bash
dotnet publish src/uwebhost/uwebhost.csproj -c Release -r <RID> --self-contained false
```
The resulting executable (found in `publish/`) can be distributed; ensure the `www` folder sits alongside the binary so assets are served correctly.

## Project Structure
```
src/
  uwebhost/
    Program.cs        # Minimal TCP-based HTTP server
    uwebhost.csproj   # .NET 8 console project
    www/
      <project>/      # Each static app lives in its own folder
```

## Notes
- The server binds to `localhost` only; expose it publicly with caution.
- Static apps should manage their own client-side state (e.g., `localStorage`, `IndexedDB`, service workers) if persistence is required.
- Add new MIME types in `Program.cs` (`GetContentType`) if you host uncommon asset formats.

## License
Released under the [MIT License](LICENSE.md).
