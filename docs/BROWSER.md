# Browser Plugin

The browser plugin gives the AI full control of a headless Chromium instance via Playwright. It is only available in the `full` tool profile and all functions require `OwnerOnly` authorization.

## Setup

### Local Development

Install the Playwright Chromium browser:

```bash
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

This downloads the Chromium binary that Playwright needs. The `playwright.ps1` script is bundled with the `Microsoft.Playwright` NuGet package.

### Docker

The Dockerfile uses Debian-based images (not Alpine) because Playwright's .NET driver is a glibc binary. System Chromium is installed via `apt-get` and Playwright is pointed to it via `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`:

```dockerfile
# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
EXPOSE 3010
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=pt_BR.UTF-8 \
    LANG=pt_BR.UTF-8
RUN apt-get update && apt-get install -y --no-install-recommends \
    locales tzdata bash chromium \
    && sed -i '/pt_BR.UTF-8/s/^# //g' /etc/locale.gen \
    && locale-gen \
    && rm -rf /var/lib/apt/lists/*

ENV PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH=/usr/bin/chromium

COPY --from=build /app .
ENTRYPOINT ["dotnet", "Lis.Api.dll"]
```

No `playwright install` is needed — the Playwright driver ships with the NuGet package and system Chromium is used directly.

## Session Management

`BrowserSessionManager` is registered as a singleton and manages one browser session per agent:

- **GetOrStartAsync** — Launches a new Chromium instance (or reuses an existing one) for the given agent ID. Thread-safe via `SemaphoreSlim`.
- **GetPageAsync** — Returns the active page for an agent, or null if no session exists.
- **CloseAsync** — Closes the browser context and instance for an agent.
- **DisposeAsync** — Closes all sessions and disposes the Playwright runtime on shutdown.

Sessions track `LastActivityAt` for future idle-timeout cleanup.

## Tools

### start

Launch a browser. Optionally navigate to a URL immediately.

Parameters:
- `url` (optional) — URL to open after launch
- `headless` (default: true) — Run headless or with a visible window

### navigate

Navigate to a URL.

Parameters:
- `url` — Target URL
- `waitUntil` (optional) — `load`, `domcontentloaded`, or `networkidle`

Returns the page title after navigation.

### snapshot

Get the current page content as plain text (via `body.innerText`).

Parameters:
- `maxLength` (default: 15000) — Truncate output to this many characters

This is the primary way the AI "reads" a page. The text representation is token-efficient compared to screenshots.

### screenshot

Capture a PNG screenshot, returned as a base64 data URI (`data:image/png;base64,...`).

Parameters:
- `fullPage` (default: false) — Capture the full scrollable page vs. viewport only

### click

Click an element by CSS selector.

Parameters:
- `selector` — CSS selector (e.g. `button.submit`, `#login`)

### type

Fill a form field with text (uses Playwright's `FillAsync`, which clears the field first).

Parameters:
- `selector` — CSS selector of the input
- `text` — Text to enter

### evaluate

Execute arbitrary JavaScript in the page context and return the JSON-serialized result.

Parameters:
- `script` — JavaScript code to execute

### tabs

List all open tabs with their titles and URLs.

### close

Close the browser session and release all resources.

## Error Handling

All browser functions catch exceptions and return error strings rather than throwing. If no session exists, functions return "No browser session. Call browser_start first."

## Profile Gating

Browser tools are only available when `tool_profile = full`. They can also be explicitly denied:

```
tools_deny = group:browser
```

Or explicitly allowed on a non-full profile:

```
tool_profile = coding
tools_allow = group:browser
```
