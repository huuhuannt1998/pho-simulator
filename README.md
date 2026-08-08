# Phở Simulator

First-person Vietnamese restaurant simulator. Unity 6.3 LTS + C#, Blender for
procedural art, Steam-oriented once the core loop proves fun. Full design in
[`PHO_SIMULATOR_GDD_CLAUDE_CODE.md`](PHO_SIMULATOR_GDD_CLAUDE_CODE.md); the
build plan and technical architecture live in [`docs/`](docs/).

## Status

Phase 0 (toolchain bootstrap) — in progress. See
[`docs/architecture.md`](docs/architecture.md) for the vertical-slice
technical design. The Unity project itself doesn't exist yet; that starts
once Unity 6.3 LTS is installed (see below).

## Repo layout

```
PHO_SIMULATOR_GDD_CLAUDE_CODE.md   the source design doc
docs/
  architecture.md                  vertical-slice technical architecture (M1)
art/blender/
  scripts/                         parametric asset generators (bpy)
  out/                             generated .glb output
  Makefile                         `make all` regenerates everything headless
  README.md
Assets/, Packages/, ProjectSettings/   (Unity project — created once Unity is installed)
Tools/                              dotnet test harness for pure-C# game logic
```

## Toolchain

| Tool | Why | Status |
|---|---|---|
| Unity 6.3 LTS | Engine (free Personal license, no royalties, commercial Steam release permitted under $200K revenue) | **Needs manual install** — Hub sign-in is an interactive OAuth flow |
| Blender 5.1.1 | Procedural art via checked-in Python scripts | Installed |
| Official Blender Lab MCP (`blender-mcp`) | Lets Claude drive a running Blender session | Installed + verified end-to-end (see below) |
| .NET SDK 9 (user-local, `~/.dotnet`) | `dotnet test` on pure-C# game logic without opening Unity | Installed |
| Git LFS | Binary art/audio | Installed, tracked via `.gitattributes` |

### A dated gotcha, documented so it doesn't get rediscovered

The official Blender MCP server's `pyproject.toml` pins only
`mcp[cli]>=1.2.0` (no upper bound). The `mcp` Python SDK shipped a breaking
rewrite in **v2.0.0 (2026-07-28)** — `FastMCP` renamed to `MCPServer` and
moved modules — which breaks `blender-mcp` on a fresh install. Fixed by
pinning below it:

```bash
uv tool install --with "mcp[cli]<2.0.0" "$HOME/blender_mcp/mcp"
```

Verified working end-to-end: a real stdio MCP client called
`execute_blender_code` against the installed `blender-mcp` server, which
relayed over TCP to a **headless, no-GUI** Blender instance
(`blender --online-mode --background -c blender_mcp --port 9876`), built a
scene, and rendered it to disk. No human needed to open Blender for this.

### Why Unity, not community MCP bridges

Unity 6.3 ships an **official** MCP server built into the Editor
(`Project Settings → AI → Unity MCP`) — used in preference to the
community `CoplayDev/unity-mcp`, which is kept as a documented fallback.
Similarly, Blender's **official** Lab MCP server is used instead of the
popular `ahujasid/blender-mcp`, which targets the legacy `bl_info` addon
format Blender dropped in 5.0 (this machine runs 5.1.1).

## Next steps

1. **You:** Unity Hub → sign in → accept free Personal license → install
   Unity 6.3 LTS with macOS + Windows (IL2CPP) build support.
2. Create the URP project, enable the built-in Unity MCP server, verify the
   remaining Phase 0 gate checks.
3. Build M1 (architecture skeleton — see `docs/architecture.md`), then fan
   out the Wave 1 parallel agents.

## Art pipeline

See [`art/blender/README.md`](art/blender/README.md).
