# Data-M Windows Desktop — P1 shell

This is the first Windows desktop shell for the selected **React + shadcn/ui + Tauri 2 + Rust** route. It reuses the Gemini workbench and the shared selection engine. It intentionally does not import or mutate scientific files yet.

## Windows prerequisites

- Node.js LTS / npm (PowerShell may require `npm.cmd`).
- Rust stable via rustup (`cargo --version`).
- Microsoft Visual Studio C++ Build Tools with Desktop development with C++.
- Edge WebView2 Runtime (Windows 11 usually includes it).

See https://v2.tauri.app/start/prerequisites/.

## Local development

From repo root:

```powershell
npm.cmd install
npm.cmd run test:interaction
npm.cmd run build:desktop
npm.cmd run tauri:dev
```

`tauri:dev` opens a native desktop window. `npm.cmd run dev:desktop` starts only the Vite browser preview; it is not a native app.

## Current boundaries

- Simulated sample rows remain in-memory; drag-and-drop between groups shows **intent only**.
- External Explorer drag-and-drop is received through Tauri's native WebView event and displays the count; it never moves or imports source files.
- Windows Explorer drag-out is not implemented.
- No SQLite or CAS backend in this milestone.
- Installer bundling remains disabled until the official release task (#21).

## CI

`desktop-windows.yml` runs TypeScript, interaction tests, Rust tests/checks and a Windows Tauri binary build. Build success is not a substitute for real pointer/keyboard acceptance on Windows.
