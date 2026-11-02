# Kemet fork of AutoCAD MCP

This is Kemet Corporation's fork of the AutoCAD MCP engine, used by **Kemet-AutoCAD-Addons** (the Kemet add-in that owns everything users see). It follows the same pattern as the Navisworks MCP: the engine runs in the background and the Kemet add-in provides the ribbon, the `Kemet - CAD MCP` window, the AI-app configurator, the silent updater and the office-share installer.

| | |
| --- | --- |
| Forked from | `iwanschelokov-byte/AutoCAD_mcp` at `4b765b3` (itself a fork of `NCO-1986/AutoCAD_mcp` v2.0.0, which went offline on 18 Aug 2026) |
| Licence | MIT, as declared in the upstream README. **The repository has no LICENSE file** (see Open items). |
| AutoCAD | 2021–2027 from one bundle (net48 / net8.0-windows / net10.0-windows) |
| Tools | 184, all kept |

Every Kemet change is marked `KEMET:` in a code comment, so a search for `KEMET:` lists them all.

## Changes

| # | Area | Change | Why |
| --- | --- | --- | --- |
| 1 | `Core/Plugin.cs` | The TCP bridge starts automatically when the plugin loads. `MCPSTART` no longer asks for a port (it reads `AUTOCAD_MCP_PORT`, default 8081) and is now only for restarting. | Users forget `MCPSTART`, and an AI client cannot answer a port prompt. Matches the Navisworks MCP, whose bridge starts with Navisworks. The listener binds to loopback only. |
| 2 | `Core/Plugin.cs` | The HTTP shim (port 8082, CORS `*`) is **off** unless `AUTOCAD_MCP_HTTP_PORT` is set. | With CORS `*`, any web page open in a browser on the PC could drive AutoCAD. MCP clients only use the TCP port. |
| 3 | `PackageContents.xml` | New `ProductCode` and `UpgradeCode` (upstream used placeholder GUIDs `A1B2C3D4-…`); name `Kemet - CAD MCP Engine`; company Kemet Corporation. | Placeholder GUIDs can collide with any other bundle that copied them. The bundle folder name is unchanged, so installing over an upstream install replaces it instead of loading twice. |
| 4 | `Core/Plugin.cs` | Build tag `2027.8-kemet.1`. | `system_status` and the load message identify the Kemet build. |
| 5 | `build/build-all.ps1`, `autocad-plugin/install.bat` | The AutoCAD 2027 leg is always built. `build-all.ps1` stops if the .NET 10 SDK is missing (pass `-IncludeNet10:$false` to skip 2027 on purpose); `install.bat` passes `-p:IncludeNet10=true`. | Kemet's target is AutoCAD 2021–2027 from one bundle. Upstream silently left 2027 out when the build PC lacked the .NET 10 SDK (`build-all.ps1`) or AutoCAD 2027 (`install.bat`), so the bundle depended on which PC built it. Building 2027 needs the .NET 10 SDK only, not AutoCAD 2027. |
| 6 | `AutoCADMCP.Server/Program.cs` (also the agent and RuntimeVerify) | The server connects to `127.0.0.1` by default instead of `localhost`. | The plugin listens on IPv4 loopback only. `localhost` resolves to `::1` first, and on Windows a refused loopback connect takes about 2 s, so every call paid that delay and `--check` (2 s budget) always reported the plugin as down. Found on the first live test. |
| 7 | New project `AutoCADMCP.Configurator` (`autocad-mcp-configurator.exe`); `build-all.ps1` publishes it to `dist\configurator`; solution and `.gitignore` | Connects AI apps to the server: `--status`, `--configure --clients <id>`, `--remove --clients <id>`, `--detect`, `--dry-run`. Writes the `autocad-mcp` entry for Claude Desktop (installer and Microsoft Store), Claude Code, Codex, Cursor, VS Code, Windsurf, Gemini CLI, Antigravity, opencode, Kimi Code and ZCode, with a backup (`<config>.bak_autocadmcp_<utc>`) before every write. | Kemet-AutoCAD-Addons' Connect buttons drive it exactly like the Navisworks window drives NavisHelper's configurator, so `AiApps.cs` is reused unchanged. Ported from the Kemet NavisHelper fork's configurator (MIT, see the project's `NOTICE.md`); the NavisHelper-only parts (server versioning, `--doctor`, `.mcpb` check) are left out. |

**Supported AutoCAD versions:** 2021–2027. 2020 and older are out of scope.

## Open items

- [ ] **Licence:** ask the fork's maintainer to add a LICENSE file (MIT), or add one here citing the README's declaration. Do this before distributing outside Kemet.
- [ ] Per-session token on the TCP port (pattern from bimwright/dwg-mcp).
- [x] One undo group per write call: **already upstream behaviour.** Checked live on AutoCAD 2024: each tool call is one "Group of commands" in the undo list, and Ctrl+Z reverts it.
- [ ] Two AutoCADs open at once: the second cannot take port 8081, so its bridge does not start (it says so on the command line) and AI apps only reach the first. A per-process port or pipe, like NavisHelper's `navishelper-mcp-<pid>`, would fix it.
- [ ] Refuse writes when the active drawing changed mid-sequence (document binding).
- [ ] Toolsets: core always on; 3D, sheet sets, groups/UCS, xdata as toggles in the Kemet window.
- [ ] Guard `execute_command` behind approval; mark the 4 sheet-set tools experimental.
- [ ] In-plugin `run_csharp`, off by default, approval required.
- [ ] Settings and audit log still live in `%APPDATA%\AutoCADMCP\`; move under a Kemet folder once the Kemet window reads them.
- [ ] Verify: `tests/RuntimeVerify` against a scratch DWG in AutoCAD 2024 and 2027.

## Taking upstream updates

The fork does not follow upstream automatically:
1. Click **Sync fork** on GitHub (or merge `upstream/main` into `kemet`).
2. Re-check every change listed above (search for `KEMET:`).
3. Build, test, publish.
