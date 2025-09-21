# uWebHost

uWebHost is a minimal cross-platform development web server written in .NET 8. It scans the local `www` directory, hydrates optional `manifest.json` metadata, and serves the static assets inside so you can iterate quickly on HTML/JavaScript utilities, games, and experiments.

## Purpose
This host exists as a lightweight runner for HTML + JavaScript projects you spin up quickly—whether they come from ChatGPT prompts, your own experiments, or other generative tools. Drop the generated app into `www`, hit run, and keep iterating without extra setup. It ships with a minimal Ollama chat client and a set of placeholder gallery entries to exercise pagination, but it is equally suited for one-off prototypes, small games, data visualizations, or any other browser-first idea.

## Features
- Manifest-aware landing page that renders a 4-column gallery with search, tag filters (Game/Utility), and client-side pagination (8 apps per page).
- Graceful defaults when a web app lacks `manifest.json`—the folder name becomes the display name, tags default to empty, and a fallback thumbnail is used.
- Static file directory listings with breadcrumb navigation when no `index.html` is present.
- Content-type detection for common web assets via `ContentTypeProvider`.
- Configurable hosting port through `appsettings.json`, `UWEBHOST_` environment variables, or command-line switches (`--port`/`-p`).
- Automatic browser launch on startup (logged failures on headless systems) and clean shutdown handling.

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Windows, macOS, or Linux with a default browser configured (browser launch is optional).

## Getting Started
1. Clone or copy the repository.
2. Place your static projects under `src/uwebhost/www/` (each project uses its own subdirectory).
3. (Optional) Edit `src/uwebhost/appsettings.json` to change the default port or supply an `UWEBHOST_Hosting__Port` environment variable.
4. Build the server:
   ```bash
   dotnet build src/uwebhost/uwebhost.csproj
   ```
5. Run the server (port defaults to `5000`; CLI arguments override configuration):
   ```bash
   dotnet run --project src/uwebhost/uwebhost.csproj -- --port 8080
   ```
6. Visit `http://localhost:<port>/` to browse the gallery, filter by tags, or open individual web apps.

## How to Request Work (Humans)
When collaborating with others or AI assistants, the clarity of the request determines the turnaround time. A few patterns that work well:
- **Add a new web app:** “Create a folder under `src/uwebhost/www/<name>` with `index.html` and, if possible, a `manifest.json` describing Name/Description/Image/Tags.”
- **Adjust gallery behaviour:** “Modify the landing page templates or `PageRenderer` to change pagination size / add a new quick filter / tweak card layout.”
- **Host changes:** “Update the request routing to support X” or “Extend `WebAppManifestLoader` so manifests can specify Y.” Mention relevant files (`Hosting/RequestRouter.cs`, `Rendering/PageRenderer.cs`, `www/index.html`, etc.).
- **Configuration updates:** “Change default port/app configuration; ensure `appsettings.json`, environment variables, and CLI switches stay in sync.”
- **Clean-up or verification:** “Remove placeholder `test-app-##` folders once the gallery has real apps” or “Run `dotnet build` to confirm changes compile.”

Provide context (why the change is needed), call out important directories/files, and indicate whether manifests should be required or optional for your scenario.

## Project Structure
```
src/
  uwebhost/
    Hosting/
      HostedApplication.cs      # Gallery DTO bound to manifests/defaults
      RequestRouter.cs          # Handles incoming HTTP requests
      WebAppManifest.cs         # POCO matching manifest.json schema
      WebAppManifestLoader.cs   # Loads manifests with fallback defaults
      WebServer.cs              # TCP listener wiring renderer + router
    Rendering/
      PageRenderer.cs           # Renders landing & directory pages
      TemplateProvider.cs       # Reads HTML templates from www/_templates
      Models/
        DirectoryEntry.cs       # Directory listing view model
    Utilities/
      ContentTypeProvider.cs    # Maps file extensions to MIME types
    www/
      index.html                # Landing page template + gallery logic
      _assets/                 # Shared icons, heroicons, and static art
      _templates/              # Shared partials for server-side rendering
      _uploads/                # Temporary manifest image staging (created at runtime)
      test-app-##/              # Placeholder apps used for paging tests
      ollama-chat/              # Sample web application
    appsettings.json            # Hosting configuration (port)
    Program.cs                  # Application entry point / configuration bootstrapping
```\r\n\r\n## Web App Manifests
Each app folder can include a `manifest.json` with the following optional properties:
```json
{
  "Name": "Display name for the gallery card",
  "Description": "One-line description shown under the title",
  "Image": "/app/banner.png" or "https://example.com/icon.png",
  "Tags": ["Game", "Utility", "Experimental"]
}
```
If the manifest is absent or incomplete, uWebHost falls back to the folder name, a generic description, and the shared favicon thumbnail under `/_assets/icons/`. Tags are normalized to lowercase for filtering, but their original casing is shown to users.
## Manifest Editor & API
- Open the manifest editor from the cog icon on each gallery card to adjust name, description, tags, and imagery with live previews.
- GET `/api/apps/{app}/manifest` returns the resolved metadata (including fallback defaults) for the specified app.
- POST `/api/apps/{app}/manifest` accepts JSON `{ name, description, tags, removeImage, image }`; when `image.tempId` is provided the staged upload is promoted into the app folder.
- POST `/api/uploads/temp` stages a Base64 file payload and returns a `tempId`; DELETE `/api/uploads/temp/{tempId}` cancels orphaned uploads (also cleaned on host startup).
- Client and server enforce a 5 MB hard limit for images and surface warnings once files exceed 1 MB.

## Landing Page UX
- Filter buttons default to `All`, `Game`, and `Utility` tag filters.
- Inline search matches against tags and app names (case-insensitive).
- Pagination displays 8 apps per page; placeholder `test-app-##` entries guarantee there is enough content to test the behaviour.
- Cards render the manifest image when available; for relative paths the server automatically scopes them to the app directory.

## Configuration
- `Hosting:Port` in `appsettings.json` defines the default port.
- Override with environment variables: `UWEBHOST_Hosting__Port=8080`.
- Override with CLI switches: `--port 8080` or `-p 8080` (first positional numeric argument is still honored for backwards compatibility).
- Changes to `appsettings.json` are watched; restart if you modify `_assets` or other static resources.

## Guidance for AI Agents
- Start by inspecting `README.md` (this file) and open tasks before editing—most workflows involve `Hosting/RequestRouter.cs`, `Hosting/WebAppManifestLoader.cs`, `Rendering/PageRenderer.cs`, and the `www/_templates` directory.
- Assume manifests are optional; never fail when `manifest.json` is missing, unreadable, or malformed—log and fall back to defaults.
- Preserve and update gallery pagination, search data attributes, and template placeholders when modifying `www/index.html` or gallery partials.
- When adding new apps, include both `index.html` and (if possible) a manifest with Name/Description/Image/Tags; ensure relative image paths resolve beneath the app folder.
- Run `dotnet build src/uwebhost/uwebhost.csproj` after changes and mention the result.
- Keep `_templates` ASCII, avoid introducing frameworks, and leave the placeholder `test-app-##` directories unless explicitly told to remove them.
- Update this README whenever workflows change (new commands, configuration options, or template expectations).

## Notes
- The server binds to `localhost` only; expose it publicly with caution.
- Static apps manage their own client-side state (e.g., `localStorage`, `IndexedDB`, Service Workers) if persistence is required.
- Add new MIME types inside `Utilities/ContentTypeProvider.cs` if you host uncommon asset formats.

## License
Released under the [MIT License](LICENSE.md).








