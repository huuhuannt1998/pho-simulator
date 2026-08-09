# Multiplayer setup — Phở Simulator

Status: **LAN co-op works end to end. Steam runs the traffic but cannot yet
find your friends.** Read §6 before promising anyone a Steam build.

This document assumes you have never touched netcode. It explains what is
built, exactly what to click, exactly what to type to test four-player co-op
on one machine, and what is honestly still missing.

---

## 1. The five-minute version

- The game is **host-authoritative**. One player's game is the referee. It
  decides who is holding which bowl (`CarryAuthority`) and who is allowed in
  (`NetworkSession`). Clients ask and wait; they never decide for themselves.
- **Four players maximum**, host included. A fifth is turned away with a
  sentence explaining why, not silently dropped.
- Everything the game does about multiplayer goes through one class,
  **`Pho.Net.Session.NetworkSession`**. Gameplay code must never touch
  `NetworkManager` directly — see §7 for why that rule matters.
- The default wire is **UnityTransport** (localhost/LAN). It needs no
  accounts, no Steam, and no internet.
- **Steam is installed and compiles**, and will carry game traffic over
  Valve's relay if you know the host's Steam ID. Lobbies, friends-list
  invites and the Steam overlay's "Join game" are **not built yet** (§6).

---

## 2. What each piece is

| Thing | Where | What it does |
|---|---|---|
| `NetworkSession` | `Assets/Scripts/Net/Session/NetworkSession.cs` | The only multiplayer API the game uses. Host / Join / Leave, connection state, the 4-player cap. |
| `ISessionTransport` | `Assets/Scripts/Net/Session/ISessionTransport.cs` | The swap point between "we are in a session" and "we are using this particular wire". |
| `UnityTransportSession` | same folder | The default wire: UDP over localhost and LAN. |
| `SteamTransportSession` | same folder | The Steam wire: P2P over Valve's relay, via the Facepunch transport. |
| `SessionSlots` | `Assets/Scripts/Domain/Multiplayer/SessionSlots.cs` | Pure C#. The 4-player cap and the wording of every refusal. |
| `SessionStatus` / `SessionStateMachine` | `Assets/Scripts/Domain/Multiplayer/SessionStateMachine.cs` | Pure C#. The connection state machine (Offline → Starting → Hosting → …). |
| `NetSessionTests` | `Assets/Scripts/Tests/DomainTests/NetSessionTests.cs` | 79 tests covering both of the above. Run in ~0.3s, no Unity needed for the logic. |

The two pure files exist for the same reason `CarryRegistry` does: "is there
room for a fifth player" and "may the user press Leave while still
connecting" are *rules*, and rules are cheaper to verify in a test than by
launching five game clients. What is left in `NetworkSession` is message
plumbing.

### The connection state machine

```
Offline ──► Starting ──► Hosting ──┐
   │                               ├──► Stopping ──► Offline
   └──► Connecting ──► Connected ──┘

any of the above ──► Failed ──► Offline (or straight back to Starting/Connecting on retry)
```

`Failed` always carries a sentence for the player in
`NetworkSession.FailureReason`. Subscribe to `NetworkSession.StateChanged`
rather than polling.

---

## 3. What must be wired in the scene

Owned by the integrator (scene/prefab agent). `SceneBuilder` should produce:

1. **A `NetworkManager` GameObject** in `Boot.unity`.
   - Do **not** hand-assign a transport component. `NetworkSession` attaches
     and configures the right one when you host or join. If a `UnityTransport`
     is already on the object, its inspector values are respected.
   - Leave `Connection Approval` alone; `NetworkSession` turns it on. Without
     it, Netcode admits everyone and the 4-player cap cannot be enforced.
   - Set `Player Prefab` if you want Netcode to spawn a player object per
     connection. `NetworkSession` only requests one when this is non-null —
     requesting a prefab that does not exist fails the connection with a
     confusing, unrelated-looking error.

2. **A `NetworkSession` component**, on the `NetworkManager` object or its own.
   Inspector fields, all with working defaults:

   | Field | Default | Notes |
   |---|---|---|
   | Network Manager | empty | Empty means "use `NetworkManager.Singleton`". |
   | Max Players | 4 | The co-op cap. |
   | Preferred Transport | `Unity` | Switch to `Steam` for a Steam build. |
   | Fall Back To Lan If Steam Unavailable | on | Keep this on during development so a closed Steam client doesn't stop you playing. |
   | Port | 7777 | LAN only. |
   | Listen Address | `0.0.0.0` | Accepts LAN peers. `127.0.0.1` would restrict hosting to this machine. |
   | Default Join Address | `127.0.0.1` | Where `Join()` goes with no argument — makes the two-instance test typing-free. |
   | Steam App Id | 480 | See §5. |
   | Auto Start From Command Line | on | Enables `-pho host` / `-pho join`. Inert without those arguments. |

3. **`CarryAuthority`** on a spawned `NetworkObject`, as it already is. It is
   unchanged by this work and keeps watching disconnects itself.

4. **Nothing else.** No UI is required to test — §4 uses the command line.

---

## 4. Testing four players on one machine

### 4.1 Build once

In the Editor: `Pho ▸ Build ▸ macOS Player`. The build lands in
`Build/PhoSimulator.app`. (Run the content, prefab and scene builders first if
you have changed any of them — the build ships whatever generated assets are
on disk.)

### 4.2 Start a host and a client

Two terminal windows. **macOS:**

```bash
# window 1 — the host
open -n Build/PhoSimulator.app --args -pho host

# window 2 — a second player joining localhost
open -n Build/PhoSimulator.app --args -pho join
```

`open -n` is load-bearing: without `-n`, macOS focuses the copy that is
already running instead of launching a second one.

**Windows:**

```
start "" Build\PhoSimulator.exe -pho host
start "" Build\PhoSimulator.exe -pho join
```

For a third and fourth player, run `-pho join` twice more. A **fifth**
`-pho join` is the interesting one: it should be refused, and the refused
instance's log should contain

```
This kitchen is full (4/4 players). Ask your friend to let you know when someone leaves.
```

That message travels from the host's `SessionSlots.Describe(...)` through
Netcode's connection-approval `Reason` field and lands in the client's
`NetworkManager.DisconnectReason`. If you instead see a silent disconnect,
something is wrong — say so, that is the exact failure this was built to
avoid.

### 4.3 Editor plus one build

Often easier while iterating: press Play in the Editor for the host, then
launch one built client with `-pho join`. The Editor cannot be launched twice
against the same project (it locks `Library/`), so the second player must be a
build.

### 4.4 Across two machines on the same wifi

On the host, find its LAN IP (`ipconfig getifaddr en0` on macOS,
`ipconfig` on Windows). On the other machine:

```bash
open -n Build/PhoSimulator.app --args -pho join 192.168.1.42:7777
```

If nothing connects: the host's firewall is almost always the reason. Allow
`PhoSimulator` on UDP 7777.

### 4.5 Driving it from code instead

```csharp
NetworkSession.Instance.StartHost(out var error);   // host
NetworkSession.Instance.Join("192.168.1.42:7777");  // client; empty = localhost
NetworkSession.Instance.Leave();                    // either side

NetworkSession.Instance.StateChanged += (from, to) => { /* update the menu */ };
Debug.Log(NetworkSession.Instance.FailureReason);   // why the last attempt failed
Debug.Log(NetworkSession.Instance.JoinTarget);      // what to send your friends
```

`Join` returning `true` means *the attempt started*, not that you are in. The
host has not decided yet. Wait for `StateChanged` to reach `Connected`, or
`Failed` with a reason.

---

## 5. Steam — what is true today

### 5.1 The package resolves and compiles

`Packages/manifest.json` contains:

```json
"com.community.netcode.transport.facepunch":
  "https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.facepunch#0eda04fc2146a4f907a61de6403315bce705279e"
```

Verified on Unity 6.3 (6000.3.21f1) with Netcode for GameObjects 2.13.1: the
package resolves, `Facepunch Transport for Netcode for GameObjects.dll`
builds, and all 14 `Pho.*` assemblies compile with zero errors. The
Facepunch.Steamworks binaries for Win32/Win64/macOS/Linux ship inside the
package — there is nothing to download separately.

### 5.2 Why the URL is pinned to a commit — do not remove the `#…`

The tip of `master` **does not compile**. Commit `f5d8000` ("Don't use
MonoBehaviour methods in transports", #269) deleted two `#region` headers but
left an `#endregion` behind, so `FacepunchTransport.cs` has three `#region`
and four `#endregion` and fails with:

```
FacepunchTransport.cs(288,9): error CS1028: Unexpected preprocessor directive
```

That single error aborts the whole project's compile — it is not confined to
the package. The pin at `0eda04f` is the last commit before the breakage and
is balanced (5 regions, 5 endregions). If someone "tidies up" the URL by
dropping the commit hash, the entire project stops building. When upstream
fixes it, re-pin to the fix rather than to a moving `master`.

### 5.3 App ID 480 (Spacewar)

`480` is Spacewar, Valve's public placeholder App ID. Every Steamworks
tutorial uses it, and it lets Steam initialise, lobbies form and the relay
carry traffic before your title has an App ID of its own. Two caveats:

- It is shared with every other developer testing on 480, so anything
  publicly discoverable can collide with strangers' sessions. Fine for
  point-to-point testing, not for a public lobby list.
- Technically it is not sanctioned. Valve does not act on it, and it is the
  normal practice, but do not ship on it.

### 5.4 What Steam costs

- **Steamworks networking is free.** Lobbies, the relay/SDR network, P2P
  sockets, friends-list invites and the overlay all cost nothing, with no
  per-player or bandwidth charge. This is the main reason to prefer Steam
  relay over renting relay servers.
- **Steam Direct is $100 per title, one time**, recoupable once the title
  earns $1,000 in adjusted gross revenue. That fee is what gets you a real
  App ID. Valve's revenue share applies to sales, not to networking.

### 5.5 Running the Steam path today

1. Set `Preferred Transport` to `Steam` on the `NetworkSession` component.
2. Have Steam running and signed in on both machines.
3. Host: read the host's Steam ID from `NetworkSession.Instance.JoinTarget`.
4. Client: `NetworkSession.Instance.Join("76561198…")` with that Steam ID.

That works — the traffic goes over Valve's relay with no port forwarding. What
is missing is everything that would let a player do step 3 and 4 *without
copying a number by hand*.

---

## 6. What is still missing for a real Steam release

Listed in the order they block a shippable friends-list experience.

### 6.1 Steam lobbies, invites and the overlay — the big one

`NetworkSession` can host and join over Steam, but there is no **lobby**. A
lobby is what turns "type your friend's 17-digit Steam ID" into "click your
friend's name". Specifically, still to build:

- `SteamMatchmaking.CreateLobbyAsync(4)` on host, `SetFriendsOnly()`, and
  publishing the host's Steam ID as lobby data.
- `SteamMatchmaking.JoinLobbyAsync(lobbyId)` on the client, then reading the
  host's Steam ID back out of the lobby and handing it to
  `NetworkSession.Join`.
- `SteamFriends.OnGameLobbyJoinRequested` — fires when a friend clicks "Join
  game" in the overlay or accepts an invite *while your game is running*.
- `SteamFriends.OpenGameInviteOverlay(lobbyId)` for the in-game invite button.
- The launch-argument path, for when your game is **not** running: Steam
  relaunches it with `+connect_lobby <id>`.
  `SteamTransportSession.TryGetLaunchLobbyId()` already reads that argument;
  resolving the lobby id to a host still needs the lobby API above.

**Why this was not built.** `Pho.Net.asmdef` lists exactly three assembly
references (`Pho.Domain`, `Pho.Core`, `Unity.Netcode.Runtime`) and
`docs/architecture.md` §10 freezes it: "Any later change is an architecture
change — goes through the integration agent." The Facepunch assembly is not
in that list, so nothing in `Pho.Net` can name `FacepunchTransport`,
`SteamMatchmaking` or `Lobby` at compile time. `SteamTransportSession` works
around this with reflection, which is fine for "attach a component and set two
fields" but not for Facepunch's lobby API, which is built on
`Task<Lobby?>` returns and `static event Action<Lobby>` callbacks typed on
Steamworks structs. Subscribing to those by reflection requires emitting a
delegate at runtime, which does not survive IL2CPP — i.e. it would work in the
Editor and break in the shipped build, which is the worst possible outcome.

**The fix is one line, and it is not mine to make.** Add to
`Assets/Scripts/Net/Pho.Net.asmdef`:

```json
"references": [
  "Pho.Domain",
  "Pho.Core",
  "Unity.Netcode.Runtime",
  "Facepunch Transport for Netcode for GameObjects"
]
```

(Yes, the assembly name really does contain spaces.) After that, a
`SteamLobbyService` can be written against the typed API in
`Assets/Scripts/Net/Session/`, and `SteamTransportSession`'s reflection can be
replaced with direct calls. Budget half a day plus two Steam accounts to test
with. Guard the whole file with the transport's presence so a project without
the package still builds.

### 6.2 A multiplayer UI

There is none. Today's entry points are `-pho host` / `-pho join` and the
`NetworkSession` API. Someone needs: a Host button, a friends/lobby list, a
"connecting…" spinner driven by `IsBusy`, and an error dialog that shows
`FailureReason` and calls `AcknowledgeFailure()` when dismissed.

### 6.3 Nothing is actually replicated yet

`NetworkSession` gets four players into the same session. It does not
synchronise them. Still needed: a networked player prefab, movement
replication, and networked state for stations, orders, bowls and the
economy — other agents' territory, but worth stating plainly so nobody
mistakes "four players connected" for "four players cooking together".

### 6.4 No host migration, deliberately

If the host quits, the session ends and everyone else sees "Disconnected from
the host." Re-electing a host mid-service would have to reconstruct who was
holding which bowl, which order belonged to which table and how far the broth
had simmered; getting that subtly wrong is worse for a co-op session than
ending it cleanly. Revisit only with evidence that players are hitting it.

### 6.5 Smaller gaps

- **Steam initialisation is incidental.** `SteamClient.Init` currently happens
  inside `FacepunchTransport.Initialize` (and in the availability probe). A
  shipped game should initialise Steam once at boot and shut it down once at
  quit.
- **`steam_appid.txt`** is needed next to the executable for non-Steam-launched
  development builds. Not committed; add it when a real App ID exists.
- **A real App ID everywhere.** `NetworkSession.steamAppId`,
  `steam_appid.txt`, and Steamworks partner-site config for P2P/SDR.
- **The rejection path is untested against a real fifth player.** The logic has
  unit tests; the end-to-end refusal has not been run with five live clients.
  Do §4.2 with five instances before calling the cap done.
- **No `Tools/build.sh`.** `Tools/test.sh` exists; builds are Editor-menu only.

---

## 7. Rules for anyone adding multiplayer code

1. **Never reference `NetworkManager` outside `Assets/Scripts/Net/`.** Talk to
   `NetworkSession`. Netcode's NetworkManager is transport-coupled with a
   lifecycle that does not match a game menu's; once six systems reach into
   it, swapping the transport or changing the player cap becomes a change in
   six places. `CarryAuthority` is the deliberate exception — it is a
   `NetworkBehaviour`, so it is *part of* the netcode layer, not a consumer.
2. **Put rules in `Pho.Domain`, plumbing in `Pho.Net`.** If you are writing an
   `if` that a designer would recognise as a game rule, it belongs next to
   `SessionSlots` with a test.
3. **The host decides.** No client-side prediction of authority questions. See
   the comment at the top of `CarryAuthority`.
4. **Refusals get sentences.** Every rejection path produces text a player can
   read. A silent disconnect is indistinguishable from a crash and generates a
   bug report every time.

---

## 8. Troubleshooting

| Symptom | Cause |
|---|---|
| `error CS1028: Unexpected preprocessor directive` in `FacepunchTransport.cs` | The commit pin was removed from the manifest URL. Restore it — see §5.2. |
| Second instance won't launch on macOS | Missing `-n` on `open`. Without it macOS focuses the running copy. |
| "No NetworkManager in the scene." | `Boot.unity` has no `NetworkManager`. See §3. |
| Client connects then instantly drops, no message | Check the client's `NetworkManager.DisconnectReason`. If it is empty and the host is full, the approval callback is not registered — something else claimed `ConnectionApprovalCallback` and `NetworkSession` logs a warning about exactly this at startup. |
| Steam preferred but LAN is used | Steam is not running, or the package is missing. `NetworkSession` logs the specific reason and falls back on purpose. Turn off `Fall Back To Lan If Steam Unavailable` to make it fail loudly instead. |
| LAN join times out across machines | Host firewall on UDP 7777. |
| Unity compiles nothing, `grep "error CS"` is clean, `Library/ScriptAssemblies/Pho.*.dll` is empty | Files under `.claude/worktrees/` inherit the macOS `UF_HIDDEN` flag and Unity silently skips hidden paths. Run `chflags -R nohidden .` immediately before Unity, in the *same* shell command — the flag gets re-applied between commands and during long runs. The assembly count is the only trustworthy pass signal. |

---

## 9. Verification performed

On Unity 6000.3.21f1, in a clean worktree, with the manifest as committed:

- `-batchmode -quit` compile: exit 0, **zero** `error CS` in the entire log,
  all 14 `Pho.*.dll` rebuilt from scratch (deleted beforehand so a stale
  assembly could not fake a pass).
- `-runTests -testPlatform EditMode`: **287 passed, 0 failed**, including 79
  new `SessionSlotsTests` / `SessionStateMachineTests` / `SessionStatusTests`.
- Facepunch transport assembly and all four Steamworks native binaries present
  in `Library/`.

Not verified, because it needs two Steam accounts and a human: an actual Steam
connection between two machines, and the five-client refusal end to end.
