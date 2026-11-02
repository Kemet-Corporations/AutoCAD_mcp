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
| 8 | `Commands/SystemCommands.cs` (set_server_options), new `Core/LocalControl.cs`, `Models/ErrorCode.cs` (`NotAllowed`), `tools.json` | Over the wire the safety settings can only be **tightened**. Turning read-only, destructive confirmation or the audit log off returns `NotAllowed` and changes nothing. Loosening is possible only in-process, through `LocalControl.SetOption`, which Kemet-AutoCAD-Addons calls from its window. Values are read the same way the apply code reads them, so `"false"` or `0` cannot side-step the check. | `set_server_options` is also an MCP tool, so an AI app could switch off its own guardrails. The person at the PC decides that, not the AI. |
| 9 | `Core/JsonRpcHandler.cs` | The read-only refusal says to ask the user (Kemet AutoCAD Addons > AutoCAD MCP) instead of telling the AI to call `set_server_options({"read_only": false})`. | That message taught the AI how to disable the guardrail. |
| 10 | `Commands/SystemCommands.cs` (get_capabilities), `Core/SocketServer.cs` | `get_capabilities` reports `pid`. The socket server no longer writes to AutoCAD's command line. | The PID lets Kemet - AutoCAD MCP tell this AutoCAD from another one holding port 8081. The command-line writes ran on the socket threads on every call, and the AutoCAD API is not thread-safe. |
| 11 | `AutoCADMCPPlugin.csproj`, `Core/JsonRpcHandler.cs`, `Core/Settings.cs`, `Core/ActivityLogger.cs` | Newtonsoft.Json pinned to 13.0.1 (was `13.0.*`). The four `JToken.ToString(Formatting)` calls are now `JsonConvert.SerializeObject(token, Formatting)`. | AutoCAD 2025+ loads its own Newtonsoft.Json before plug-ins, and every 13.x has assembly version 13.0.0.0, so the plug-in runs against AutoCAD's copy. Built against 13.0.4, the plug-in called a `ToString(Formatting)` overload that AutoCAD 2026's copy lacks, so **every** call (even `get_capabilities`) failed with "Method not found". Found on the first AutoCAD 2026 test. Upstream had already handled this for the 2027 leg only. |
| 12 | `.gitignore`, `autocad-plugin/dist/net48`, `dist/net8.0-windows` | The prebuilt DLLs under `dist/` are no longer committed (removed from git, now ignored). | Upstream committed a snapshot so users could install without building. Kemet users install from the office share, and the committed copies made every build an uncommitted change, so `publish-share.ps1` (Kemet-AutoCAD-Addons) refused to publish. The README's "copy the DLLs from dist" route no longer applies to this fork. |

**Supported AutoCAD versions:** 2021–2027. 2020 and older are out of scope.

## Open items

- [ ] **Licence:** ask the fork's maintainer to add a LICENSE file (MIT), or add one here citing the README's declaration. Do this before distributing outside Kemet.
- [ ] Per-session token on the TCP port (pattern from bimwright/dwg-mcp).
- [x] One undo group per write call: **already upstream behaviour.** Checked live on AutoCAD 2024: each tool call is one "Group of commands" in the undo list, and Ctrl+Z reverts it.
- [ ] Two AutoCADs open at once: the second cannot take port 8081, so its bridge does not start (it says so on the command line) and AI apps only reach the first. A per-process port or pipe, like NavisHelper's `navishelper-mcp-<pid>`, would fix it.
- [ ] Refuse writes when the active drawing changed mid-sequence (document binding).
- [ ] With read-only off, `execute_command` runs raw AutoCAD commands (for example `ERASE ALL`) without the destructive confirmation, which only covers the named delete tools. Ctrl+Z still undoes it. Consider requiring confirmation for `execute_command`.
- [ ] An AI app with its own file-system tool could edit `%AppData%\AutoCADMCP\settings.json`. It takes effect only at the next AutoCAD start, but it is a way round change 8. Consider moving the settings where the AI apps' file tools are not pointed, or signing them.
- [ ] `drawing_close` with discard is not marked destructive.
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
