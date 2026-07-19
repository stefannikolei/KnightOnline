# OpenKO — C#/.NET Cross-Platform Port

This directory contains the C#/.NET 10 port of the Open-KO server (and, in later
stages, client) codebase. The C++ implementation in the rest of the repository is
the reference and remains untouched; the port lives side by side with it.

## Hard rules

- **The client↔server wire protocol is bit-exact.** Framing
  (`[0xAA 0x55][int16 LE length][payload][0x55 0xAA]`), the JvCryption cipher,
  the seedable no-final-XOR CRC32 variant, LZF compression (HLOG=16, VERY_FAST)
  and all opcode/field layouts match the C++ byte for byte. This is pinned by
  golden-vector tests generated from the original C++ code
  (`tools/golden-gen/` → `tests/vectors/`).
- **The database schema and stored procedures are unchanged** — the port talks
  to the same dockerized `KN_online` MSSQL database via `Microsoft.Data.SqlClient`.
- **Configuration follows .NET conventions** — each server reads an
  `appsettings.json` (copied next to its binary) bound to strongly-typed
  `Options` classes with `IConfiguration` + DataAnnotations validation. Every
  value can be overridden by environment variables (`Section__Key=…`,
  e.g. `ConnectionStrings__GameDb=…`, `Ebenezer__ServerNo=2`) or command-line
  args, per the standard configuration precedence. The legacy `.ini` reader was
  removed.

## Deliberate deviations from the C++

- **Internal server-to-server IPC is modernized.** The boost::interprocess
  shared-memory queues (`KNIGHT_SEND/RECV`, `ITEMLOG_SEND`) and the `KNIGHT_DB`
  shared block are ABI-specific and not portably readable from .NET. They are
  replaced by transport abstractions (`IItemLogSource`, later `IIpcChannel`)
  with in-process and TCP-loopback implementations. Only *internal* topology
  changes; the client protocol does not.
- **No FTXUI terminal UI** — plain console logging via
  `Microsoft.Extensions.Logging`. `--headless` is accepted and ignored.
- The ItemManager listens on a TCP loopback port (`[ITEMLOG] PORT`, default
  15200) instead of opening a shared-memory queue; the payloads are the same
  `[opcode][body]` messages, wrapped in standard KO frames.
- The database connection uses a standard `ConnectionStrings:GameDb` connection
  string (or the component `Database` section: `Dsn`/`Uid`/`Pwd`/`Server`,
  resolved via `AddGameDatabase`). `appsettings.json` ships the docker-compose
  dev defaults (`knight`/`knight`, `localhost`); override the connection string
  per environment with `ConnectionStrings__GameDb=…` (or user-secrets /
  `appsettings.Development.json`) for anything other than local dev.

## Layout

```
src/OpenKO.Core        shared/: ByteBuffer, Packet, opcodes, JvCryption, KoCrc32,
                       Lzf, CP949 encoding, djb2
src/OpenKO.Network     framing (PullOutCore port incl. resync quirks),
                       PacketReader/Writer (utilities.cpp equivalents), TCP server
src/OpenKO.Data        SqlClient connection factory, game constants, models
                       (UserData/_USER_DATA, warehouse, knights, version…)
src/OpenKO.GameData    server-side map data: .smd reader (terrain heightmap,
                       N3ShapeMgr collision cells, object/regene events, warps),
                       height/collision queries, _IntersectTriangle port
src/OpenKO.Hosting     generic-host glue (INI config resolution, logging)
src/Servers/…          VersionManager, ItemManager (stage 1),
                       Aujard DB-agent library + thin host (stage 2)
tests/…                unit + golden-vector + end-to-end tests
tests/vectors/         checked-in golden vectors generated from the C++ reference
tools/OpenKO.TestClient  scripted protocol client (verification / parity harness)
tools/golden-gen/      C++ generator for the golden vectors (links shared/)
```

### AIServer (stage 3)

`OpenKO.Servers.AIServer` is a full port of the C++ AIServer: `PathFinder`
(the NPC A* incl. its original quirks), the `EbenezerLink` session handler
(AI_SERVER_CONNECT handshake, AG_COMPRESSED_DATA envelope — LZF + KO-CRC32,
see `AgCompressedCodec`) on the zone-type listen ports (10020/10030/10040),
the DB loaders for the startup tables, the complete NPC AI (`Npc.*` — state
machine, combat, exp/loot, battle events), both magic processors
(`NpcMagicProcessor` = CNpcMagicProcess, `UserMagicProcessor` = CMagicProcess),
the dungeon room events (`RoomEvent` + the `.evt` parser on `AiZone`,
including the upstream `CheckMonsterCount` variable-shadowing no-op, kept
verbatim), parties and all `AG_*` game handlers (`GameSocketHandlers`).

`AiServerApp` ports the AIServerApp startup (tables → maps/rooms/object-event
NPCs → spawn → wiring), the Ebenezer connect bookkeeping with the
`AllNpcInfo` push and the 10s `CheckAliveTest`. Instead of the C++
NpcThread/ZoneEventThread/timer + mutex model, the host (`AiServerService`)
runs ONE single-writer game loop: inbound packets are queued and drained
between the 100ms NPC ticks, 1s room ticks and 10s alive checks — the same
serialization the C++ enforced with its recursive mutexes, without the locks.

### Aujard (stage 2)

`OpenKO.Servers.Aujard` ports `CDBAgent` as an async library (`IDbAgent`): all
stored-procedure calls (LOAD_USER_DATA, UPDATE_USER_DATA, ACCOUNT_LOGIN/LOGOUT,
CREATE_NEW_CHAR, NATION_SELECT, the KNIGHTS procs, UPDATE_WAREHOUSE, …) with the
exact blob layouts (items/serials/quests codecs in `UserDataBlobCodec`, pinned by
round-trip tests) and the C++ validation quirks. The KNIGHT_SEND/RECV shared-memory
packet loop is intentionally not ported — the stage-4 C# Ebenezer calls the library
directly. DB-backed smoke tests are opt-in via the `OPENKO_TEST_DB` environment
variable (they need a seeded KN_online database, see `docker-compose.yaml` and the
OpenKO-db project) and are tagged `Category=Database`.

## Build & test

Requires the .NET 10 SDK (`apt install dotnet-sdk-10.0` on Ubuntu 24.04+).

```bash
dotnet build dotnet/OpenKO.slnx -c Release
dotnet test  dotnet/OpenKO.slnx -c Release
```

## Orchestration (.NET Aspire)

The `OpenKO.AppHost` project orchestrates the whole stack — it **creates and
starts the SQL Server database, brings up all five servers in dependency order,
and launches the client** — with one command (requires a running Docker engine):

```bash
dotnet run --project dotnet/OpenKO.AppHost
```

It provisions a SQL Server container (named `sqlserver`, persistent
`openko-sqldata` volume) with a `KN_online` database exposed as the `GameDb`
connection, then injects `ConnectionStrings__GameDb` into each server (overriding
their appsettings.json).

The **real game schema** (tables + stored procedures) is loaded automatically by
the `kodb-util` loader resource — Aspire builds the existing upstream
`docker/kodb-util` image (a Go tool that fetches the `OpenKO-db` schema from
GitHub) and runs it against the `sqlserver` container. The import runs **only when
the database is empty** (guarded by a sentinel in a persistent volume), so
subsequent restarts are fast. A custom schema health check keeps the loader
`Unhealthy` until `KN_online` actually contains the imported tables, and the game
servers `WaitFor` that — so they never start against an empty database. Startup
order:

```
sqlserver (KN_online)
   └─ kodb-util  (imports the real schema; Healthy = schema present)
         ├─ itemmanager, aujard, versionmanager, aiserver
         ├─ ebenezer (also waits for aiserver)
         └─ client   (waits for versionmanager + ebenezer)
```

> **First run is slow and needs internet:** the schema is fetched from GitHub the
> first time. Later runs reuse the persistent volume.

The Aspire dashboard (printed on startup) shows each resource's logs, health and
endpoints, and exposes two commands on the **kodb-util** resource (both re-run
the upstream `cleanImport.sh` inside the loader container, with a confirmation
prompt):

- **Reset database (clean import)** — drops `KN_online` and re-imports the schema
  from scratch.
- **Reload schema (git pull + import)** — pulls the latest `OpenKO-db` schema and
  re-imports it.

Override the SA password / ports via Aspire parameters as usual (the SA password
defaults to the `docker/default.env` value and is shared with the loader). The
`docker/*` files are used unchanged; only the loader's entrypoint is overridden by
the AppHost to add the import guard.

To run a server or the client individually instead, use `dotnet run` directly:

## Running

```bash
# Every server reads its appsettings.json (copied next to the binary). Override
# any value with env vars, e.g. ConnectionStrings__GameDb=... or Ebenezer__ServerNo=2.

# VersionManager: listens on 15100. Needs the MSSQL database (docker compose up)
# with the KN_online schema (ConnectionStrings:GameDb in appsettings.json).
dotnet run --project dotnet/src/Servers/OpenKO.Servers.VersionManager

# ItemManager: listens on 127.0.0.1:15200 (ItemManager:Port). No database.
dotnet run --project dotnet/src/Servers/OpenKO.Servers.ItemManager

# AIServer: AiServer:Zone selects the listen port (10020 karus/unify, 10030
# elmorad, 10040 battle) for Ebenezer; reads the MAP/ directory (SMDs + <n>.evt
# room events) resolved beside the binary.
dotnet run --project dotnet/src/Servers/OpenKO.Servers.AIServer

# AssetDump: N3 client asset inspection — JSON metrics per file, textures to PNG.
dotnet run --project dotnet/tools/OpenKO.AssetDump -- Client/Data/Item/1_1011_00_0.n3cplug
dotnet run --project dotnet/tools/OpenKO.AssetDump -- Client/Data/Zones          # whole directory
dotnet run --project dotnet/tools/OpenKO.AssetDump -- Client/Data/symbol_us --png /tmp/pngs

# Scripted protocol client (also used by the parity harness):
dotnet run --project dotnet/tools/OpenKO.TestClient -- 127.0.0.1:15100 version
dotnet run --project dotnet/tools/OpenKO.TestClient -- 127.0.0.1:15100 serverlist
dotnet run --project dotnet/tools/OpenKO.TestClient -- 127.0.0.1:15100 login account pw

# Clean game client (MonoGame window + game loop; Esc quits). Takes no CLI args —
# the server endpoint + data path come from appsettings.json (section "Client")
# and graphics/sound from options.json next to the binary (see the settings tool).
dotnet run --project dotnet/src/Client/OpenKO.Client

# Debug/CLI client (OpenKO.Client.Dev) — the offline zone / scripted login / screenshot modes.
dotnet run --project dotnet/src/Client/OpenKO.Client.Dev -- --offline moradon        # zone + player model, third-person cam, no server
dotnet run --project dotnet/src/Client/OpenKO.Client.Dev -- --server 127.0.0.1:15100 --account acct --password pw
# Headless smoke (Linux/CI): render one frame to a PNG under xvfb + llvmpipe.
LIBGL_ALWAYS_SOFTWARE=1 xvfb-run -a dotnet run --project dotnet/src/Client/OpenKO.Client.Dev -c Release -- \
    --offline moradon --screenshot /tmp/client.png

# Settings tool (Option.exe equivalent): an Avalonia GUI that writes options.json.
# --path points it at the game's binary directory so the game reads what it wrote;
# the in-game exit menu launches it automatically. Settings apply at the next start.
dotnet run --project dotnet/src/Client/OpenKO.Client.Settings -- --path dotnet/src/Client/OpenKO.Client/bin/Debug/net10.0
```

### Parity harness

To byte-diff the C# VersionManager against the C++ one, run both against the
same database (ports differ, e.g. run the C# one with a modified `_LISTEN_PORT`
via a port-forward or run them on separate hosts), then issue identical
commands with the TestClient against each and compare the hex output:

```bash
dotnet run --project dotnet/tools/OpenKO.TestClient -- <cpp-host>:15100 serverlist > cpp.hex
dotnet run --project dotnet/tools/OpenKO.TestClient -- <cs-host>:15100  serverlist > cs.hex
diff cpp.hex cs.hex
```

### Regenerating the golden vectors

Only needed when the C++ reference changes (it shouldn't — the protocol is frozen):

```bash
g++ -std=c++23 -O2 -I . -I shared -I deps/djb2 \
    dotnet/tools/golden-gen/golden_gen.cpp \
    shared/JvCryption.cpp shared/crc32.cpp shared/lzf.cpp \
    -o /tmp/golden-gen
/tmp/golden-gen dotnet/tests/vectors
```

(`deps/djb2` is a git submodule: `git submodule update --init deps/djb2`.)

## Port roadmap

| Stage | Scope | Status |
|---|---|---|
| 1 | Foundations (Core/Network/Data/Hosting) + VersionManager + ItemManager | **done** |
| 2 | OpenKO.Data full build-out + Aujard as library/hosted service | **done** |
| 3 | AIServer (+ OpenKO.GameData: .tbl loader, N3ShapeMgr MAP collision) | done — parity verification vs the C++ AIServer pending (needs seeded DB + MAP files) |
| 4 | Ebenezer (WIZ_CRYPTION handshake, LZF packets, game logic) | done — socket layer, login/char flow, GAMESTART, region world, AISocket link/NPC mirror sync, combat, magic, respawn/warp, chat/inventory, parties + SMD map loading, warehouse, exchange, ZoneChange/warp gates, points/class changes, object events, knights/clans, the quest VM (QUESTS/<zone>.evt), battle events + GM commands and the party/market BBS boards; the WIZ_* dispatch covers every opcode the C++ handles (WIZ_FRIEND_PROCESS keeps the upstream #if 0 no-op, the server-to-server UDP channel stays an optional hook). Parity verification against the C++ Ebenezer pending (needs seeded DB + MAP/QUESTS assets) |
| 5 | Client foundations: N3 asset loaders + math | **done** — framework-free `OpenKO.Client.Assets` scaffold, the `CN3BaseFileAccess` header port, the My_3DStruct.h vertex layouts (size-pinned) and the __Quaternion Slerp/YawPitchRoll port (5.1) done; the `.dxt` (NTF) texture container reader with the C++ reader's stream positioning kept verbatim (incl. its non-mip fallback under-skip) plus a CPU DXT1–5/uncompressed decoder, verified by a full-corpus scan over all ~6.5k real `.dxt` assets (5.2) done; the four mesh readers — `.n3pmesh` progressive meshes incl. the edge-collapse LOD walk (`N3PMeshInstance`), `CN3VMesh`, `CN3IMesh` and the headerless `CN3Mesh` — full-corpus-verified against all ~3.8k `.n3pmesh` files (5.3) done; the animation stack — `CN3AnimKey` channels (30fps mapping, verbatim slerp), `CN3Transform`/`CN3Joint` hierarchies (D3D matrix composition), the headerless `.n3anim` clip tables and `CN3Skin` skinning data — corpus-verified against all `.n3joint`/`.n3anim` files (5.4, 11 pre-orient legacy ChrSelect joints pinned as known-misparsed exactly like the C++) done; the character/shape layer — `__Material`, `CN3TransformCollision`, `CN3CPart(+Skins)`, `CN3CPlug(Base/Cloak)` with the embedded FX-PMesh, `CN3Chr` and `CN3Shape`/`CN3SPart` — corpus-verified against all `.n3cpart`/`.n3cskins`/`.n3cplug`/`.n3shape`/`.n3chr` files, mirroring the C++'s no-op reads past EOF for pre-1298 tails and leaving the unread skin-collision trailer exactly like the C++ (5.5) done; the `.gtd` terrain reader (map cells, patches, grass, tile-texture table, embedded lightmaps, rivers, and the upstream-disabled pond load kept verbatim) plus the `.uif` widget-tree reader (`CN3UIBase` + Image/String/Button/Static/Edit/Progress/ScrollBar/TrackBar/Area/List), corpus-verified against all `.gtd` and 174/175 `.uif` files (`char_select.uif` is a pre-1264-era layout the 1298 C++ cannot parse either) (5.6) and the `tools/OpenKO.AssetDump` CLI (JSON metrics for every N3 format, .dxt→PNG via the CPU decoder) (5.7) done. **Stage complete** — every reader is verified against the full real 1298 asset corpus (~13k files); FX formats (`N3FX*`) follow after stage 6 with particle rendering The `Client/Data` asset submodule (ko-client-assets, openko-1298) checks out in this environment, so the loaders can be verified against the real 1298 asset corpus |
| 6 | Client engine core on MonoGame (fixed-function emulation, UI system) | in progress — `OpenKO.Client.Engine` (MonoGame DesktopGL 3.8.4, split into a headless-testable pure layer and a thin device layer) + `OpenKO.Client.Viewer` debug viewer: interop (Numerics↔XNA, D3DCOLOR, TriangleFan→list), D3D render-state mapping tables, LH camera + frustum + EXP2→linear fog fit, the CLocalInput edge machine with DIK mapping, the C++ frame clock, and the pinned DualTextureEffect Modulate2X compensation (6.1) done; the pure TextureUploadPlan (DXT passthrough incl. GL mip-tail synthesis, CPU conversion for the uncompressed formats — swept over the full .dxt corpus), the case-insensitive KoPathResolver + TextureCache, PMesh vertex conversion and the PMeshInstanceRenderer with the Mesh-Browser viewer scene (6.2), and the material layer — MaterialPlan from the RF_* flags, the back-to-front AlphaManager (CN3AlphaPrimitiveManager), ShapeRenderer with per-part culling/LOD/texture-animation/RF_BOARD_Y/RF_WINDY and the Shape-Browser scene (6.3), plus the character runtime — bind-pose/inverse matrices, CPU SkinDeformer, the __FrmCtrl AnimPlayer (loop/freeze/loop-delay/motion-blend), joint ticking with ReCalcMatrixBlended, plug rendering on the joint chain and the Character viewer scene (6.4), plus the UI layer — pure UiRenderer traversal (tail-first order, button-state/anim-frame selection, PtInRect hit test), the ortho UiQuadBatcher replacing the RHW quads, FontStashSharp text over bundled Noto Sans (KR) fonts and the UI-Browser scene (6.5), and the char-select milestone scene — background stage shape, validated characters (legacy-skeleton models are filtered like the C++ leaves them broken), fog, alpha manager and a --screenshot flow that runs headless under xvfb+llvmpipe and produced the port's first rendered frames (6.6) done; and the terrain + sky slice — the pure `TerrainVertexBuilder` (level-1 patch VNT2 geometry with the TileDir UV tables and the (ix+iz) fan winding), the pure `TilePassPlanner` (colormap / one-tile / two-tile ADD pass lists), and the device `TerrainRenderer` (colormap `.tct` grid + tile `.gtt` textures + per-patch frustum cull, DualTexture Modulate2X-compensated colormap and additive tile blending) plus the pure `SkyGeometry`/device `SkyRenderer` (camera-centred horizon-glow fans + cloud dome, Z/fog off, alpha-blended) and a Terrain viewer scene that renders whole zones headless — verified against the real Moradon/arena zones (6.7, lightmaps [`.tlt` streaming], the `Terrain_Base.bmp` detail overlay and sun/moon/stars deferred as documented visual deviations) done; and the river/water + polish slice — the pure `RiverVertexBuilder` (the CN3River strip-index stencil expansion and the per-vertex wave oscillator table + `UpdateWaterPositions` step) and the device `RiverRenderer` (the animated 32-frame caustic × per-river wave overlay as a DualTexture Modulate2X-compensated draw, NonPremultiplied alpha, depth-read water, UV scroll + wave bob + per-river frustum cull) wired into the Terrain scene — verified against Moradon's four rivers, plus the viewer's `--novsync`/`--fullscreen` options (F toggles) and the "Known visual deviations vs. D3D9" section below (6.8) done. **Stage 6 is feature-complete for login→char-select→in-world rendering** (FX particles follow in a later slice with the `N3FX*` formats; audio is stage 7) |
| 7 | Client game (WarFare port: states, networking, ~80 UI dialogs) | in progress — `OpenKO.Client.Game` scaffold: the `GameState`/`GameStateMachine` port of the CGameProcedure driver (single active-state pointer with the deferred Release()→Init() swap and the render-guard), the client network layer — `GameClientSocketCore` (the mirror of the server core: unencrypted login-server framing, then the WIZ_VERSION_CHECK-keyed encrypt-on-send [counter+payload+crc32] / decrypt-on-recv [0x1EFC-tagged block] and WIZ_COMPRESS_PACKET/LZF unwrap) and the `KoClientConnection` async link, plus the `LoginProtocol`/`GameProtocol` builders+parsers for the login→char-select opcode set (LS_SERVERLIST/LOGIN/NEWS, WIZ_VERSION_CHECK/LOGIN/SEL_NATION/ALLCHAR_INFO/NEW_CHAR/DEL_CHAR/SEL_CHAR/GAMESTART). Verified by a headless test suite that round-trips frames through the real server `GameSocketCore` in both directions (encrypted + plain) and pins every packet layout (7.1); and the login→char-select flow states — `GameContext` (the shared-statics analog: account/server/nation/spawn + the CGameProcedure shared handler for WIZ_VERSION_CHECK/WIZ_SEL_CHAR) with `LoginState`/`NationSelectState`/`CharSelectState`/`CharCreateState`/`InGameState` driving the exact C++ transition graph (server-list → account login → version-check → game login → nation branch → char list → select/create → spawn → in-game), plus the `NetworkGameClient` pump adapter (WM_SOCKETMSG-style receive queue drained on the game thread). Verified by a headless flow test that drives the whole login→nation→char-select→in-game sequence through synthetic server replies (7.2); and the in-world entry core — `WorldEntities` (the CPlayerOtherMgr roster: local player + region-visible remote players by socket id), the `WorldProtocol` parsers (WIZ_MYINFO prefix, WIZ_MOVE, the WIZ_USER_INOUT GetUserInfo blob, WIZ_CHAT) with the ×10 fixed-point→world-float conversion, and `InGameState` wiring the WIZ_GAMESTART two-phase handshake, the entity in/out/move stream and chat — field order pinned against the C# Ebenezer send side and verified by a headless test that drives the entry handshake, MyInfo, user in/out, movement (local + remote) and chat through server-shaped packets (7.3); and the item/magic slice — `ItemProtocol` (WIZ_ITEM_MOVE build for the four e_ItemMoveDirection modes + result byte), `MagicProtocol` (the central WIZ_MAGIC_PROCESS command/spell/source/target/6-data packet, build+parse), a client-side `Inventory` (flat position map with the drag move/swap), and `InGameState` wiring optimistic item moves + the magic broadcast — pinned against the C# Ebenezer and verified headless (7.4); and the group-dialog protocols — `PartyProtocol` (WIZ_PARTY create/invite/permit/kick/leave), `ExchangeProtocol` (WIZ_EXCHANGE request/agree/add/decide/cancel), `WarehouseProtocol` (WIZ_WAREHOUSE open/req/input/output) and `KnightsProtocol` (WIZ_KNIGHTS_PROCESS create/join/withdraw/list/member) with the sub-command constants and request builders, routed through `InGameState` per-family (sub-command + payload) events — pinned against the C# Ebenezer read side and verified headless (7.5); and the gameplay-math slice — `TerrainCollision.GetHeight` (a verbatim port of CN3Terrain::GetHeight with the (ix+iz) diagonal split and barycentric interpolation), `EntityInterpolator` (frame-rate-independent glide toward the streamed WIZ_MOVE targets) and `GameCamera` (the third-person orbit/zoom → eye/at the engine camera consumes) — pure and verified by headless tests over flat/sloped terrain, the step-then-snap interpolation and the camera clamp/orbit (7.6); and the runnable client executable `OpenKO.Client` — a MonoGame host that wires the game-state machine, the client network layer and the stage-6 engine into a real window + game loop with a FontStashSharp status HUD: `--server host[:port] --account … --password …` connects and auto-runs the login→char-select→in-game flow, `--offline <zone>` renders a zone with no server (verified headless: Moradon renders under xvfb) (7.7-host); and the audio subsystem (CN3SndMgr) — the pure `WavAudio` RIFF/WAVE PCM decoder, `Audio3D` (the OpenAL inverse-distance-clamped attenuation), `SoundSettings`/`SoundType` and a `SoundManager` over an `IAudioBackend`, with the production `MonoGameAudioBackend` on MonoGame's bundled OpenAL (SoundEffect + AudioEmitter/Listener 3D) that degrades to silent when no device opens (headless) — the client wires it with the listener following the camera; verified by headless tests (WAV decode, attenuation, manager) and a graceful no-device run (7.7); and the in-world view — the client's offline demo places a player character (a corpus `.n3chr`, rendered through the stage-6 `ChrRenderer` CPU-skinning) on the terrain at the `TerrainCollision.GetHeight` surface with the `GameCamera` third-person follow, so `--offline <zone>` shows an actual character standing in the rendered zone (verified headless: a Karus mage on Moradon) (7.8); and player control — the pure `PlayerController` (camera-relative movement at the run speed, terrain-height following, facing from travel direction) with `WorldProtocol.BuildMove` (the CUser::MoveProcess request) and `InGameState.SendMove`, wired into the client as WASD movement + ←→ camera orbit that turns and walks the character over the terrain and streams WIZ_MOVE to the server (7.9); and the `.tbl` game-data reader `N3TableFile` (port of CN3TableBase) — the per-byte XOR stream cipher plus the `[columns][types][rows]` typed-cell parse indexed by the DWORD id, verified by a cipher round-trip and a corpus test that decrypts and parses the real `UPC_DefaultLooks`/`NPC_Looks`/`Item_Org` tables (7.10, the foundation for faithful runtime character assembly from race/class/items) done; and a fidelity pass against the actual C++ WarFare client — the `MsgSend_Move` request corrected to `[WIZ_MOVE][word x*10][word z*10][short y*10][word speed*10][byte moveFlag]` (speed is ×10 fixed-point, the trailing byte is the 0x01 moving | 0x02 continuous flag) with the new `MsgSend_Rotation` (`[WIZ_ROTATE][short yaw*100]`), the `MsgSend_PerTradeReq` trailing trade-type byte restored, and the non-leader party-leave (`REMOVE + own id`) added, after auditing every client→server builder as a 1:1 match; and the faithful runtime character assembly — the `CPlayerOther::Init` data layer (`__TABLE_PLAYER_LOOKS`/`NPC_Looks` + `__TABLE_ITEM_BASIC`/`_EXT` over `N3TableFile`, the verbatim `CGameBase::MakeResrcFileNameForUPC` resource-name rule with the race-folded body-part id, and the `CharacterAssembler` running the 8-slot equip loop with robe upper/lower interaction, hand/shield joint anchoring and InitFace/InitHair template suffixing), the engine `CharacterFactory` that loads the tables and assembles a live `ChrRenderer` (with the looks-table plug joint anchors), the client demo now rendering a runtime-assembled El Morad character instead of a baked `.n3chr`, the WIZ_USER_INOUT parser capturing each remote player's eight visible-equipment ids, and the client-side `CPlayerOtherMgr` (`RemotePlayerRenderer`) that dresses and glides each region-visible remote player from that stream — done. **Etappe 7 delivers a runnable, walkable client covering login→char-select→in-game with the world/entity/item/magic/party/trade/warehouse/knights protocols (audited 1:1 vs the C++ WarFare client), gameplay math, audio, a third-person in-world view, WASD control and faithful runtime character assembly for the local player and region-visible remote players; remaining depth: the full MyInfo stat/skill block → real HUD, online zone loading, NPC display, ray picking, interactive in-game `.uif` dialogs and FX particles** |
| 8 | Hardening, soak tests, docs | open |

See the stage plan in the repository history / PR description for details.

### Known visual deviations vs. the D3D9 client (stage 6)

The engine emulates the fixed-function D3D9 pipeline on MonoGame's stock effects,
so a few things render slightly differently from the original client. These are
deliberate and documented, not bugs:

- **Two-stage MODULATE → DualTextureEffect (Modulate2X).** MonoGame's
  `DualTextureEffect` doubles RGB; the engine halves the diffuse/vertex-colour RGB
  to recover plain `MODULATE` (alpha untouched). Used by the char overlap, the
  terrain colormap pass and the river. Sub-1-LSB rounding differences are possible.
- **Terrain multi-pass.** The D3D9 terrain uses three texture stages per tile; the
  port splits them into passes (tile0 opaque, tile1 additive = `D3DTOP_ADD` exact,
  colormap as a dual-texture modulate). Per-pass diffuse folding can shift brightness
  slightly on multi-tile cells.
- **EXP2 fog → linear.** `BasicEffect` only does linear fog; the C++ EXP2 table fog
  is fitted to a two-point linear curve (`FogMapper`). The falloff shape differs
  mid-range.
- **UI −0.5 px offset dropped.** The D3D9 half-texel RHW offset is gone under GL
  raster rules (`UiQuadBatcher`).
- **Terrain lightmaps + detail overlay** (`.tlt` streaming, `Terrain_Base.bmp`) are
  ported (stage 9.11); the `.tlt` is loaded whole rather than paged in a 3×3 patch
  window (equivalent render, higher memory).
- **Sky sun/moon/stars + day-night** are ported (stages 9.11 / 10.5): the sun renders
  as 3 additive billboards (disk/glow/flare), the moon samples its phase strip, stars
  fade in at night, and the day-change colour sim is driven by the server clock
  (`WIZ_TIME`). Falls back to a free-running frame clock offline.

### Client feature scope + known deviations (text input / IME)

The client accepts text through MonoGame's `TextInput` event, which delivers basic
Latin plus whatever the OS IME commits. Inline CJK composition — the underlined
preview drawn at the caret while a syllable is still being assembled — is handled
in two layers:

- **OS IME (primary).** `SdlIme` (stage 9.3) anchors the OS composition/candidate
  window at the focused edit box and gates text input on edit focus.
  `SdlImeComposition` adds an `SDL_AddEventWatch` hook that observes the
  `SDL_TEXTEDITING` events MonoGame's own loop drops, so the focused edit can render
  the in-progress composition string. Both bindings resolve the same SDL2 the
  DesktopGL backend already loaded and **degrade to an inert no-op** when SDL isn't
  present (headless/tests) — the native watch callback never throws back into SDL.
- **Dubeolsik automaton (fallback).** `HangulAutomaton` is a pure, headless-testable
  2-set Korean (두벌식) Jamo→syllable composer for when the Linux OS IME
  (IBus/Fcitx behind SDL) is unreliable. It maps QWERTY to initial/medial/final jamo
  and composes precomposed Hangul (U+AC00 block), handling double finals (겹받침),
  final-consonant reassignment when a vowel follows, and backspace decomposition.
- **Composition preview (rendered).** The focused edit draws the in-progress
  composition inline at the caret with a flat underline (stage 10.7,
  `UiEditControl.GetCompositionLayout` → `UiScreenRenderer`). The original's per-clause
  (thick/thin) underline segments are approximated by one flat underline —
  `SDL_TEXTEDITING` surfaces no per-clause attributes, and the modernised C++ itself
  hosts a native EDIT+IMM32 (no custom draw to copy).
- **Deviations from the C++.** The original used IMM32 and shipped a CP949 (EUC-KR)
  build — Korean only. This port matches that scope: **only Korean composition is
  provided; there is no Chinese or Japanese inline input** (the CP949 client couldn't
  do those either).

### Client feature scope (stages 9–10): 1:1 WarFare parity

The C# client is a functional, playable 1:1 port of the WarFare client. Interactive
`.uif` dialogs drive the whole flow — login → server list → nation → character
select/create → in-game — and in-world it renders terrain (with lightmaps), sky
(day-night, sun/moon/stars), water, weather, zone objects, and CPU-skinned animated
characters, with WASD movement and a third-person camera.

Implemented in-game systems (all wired to the byte-exact server protocol):

- **HUD** — state bar (HP/MP/EXP/position), target bar, chat (channels + scrollback),
  command bar, death/revival, and the **minimap** (UV-scrolled zone map, party/NPC/
  enemy dots, rotated player arrow, buff-icon strip).
- **Inventory & items** — grid + equipment slots, drag/drop moves, loot boxes, item
  tooltips, countable stack-split, and **NPC blacksmith repair** (hover price → repair).
- **Skills & magic** — skill tree, class change, the hotkey bar with **radial
  cooldown rings**, and real FX: spells resolve `Data\fx.tbl` effects (particle /
  billboard / mesh parts) that follow caster/target joints.
- **Social** — party/force, Knights (clan create/join/browse + clan pages), the
  character sheet, the **party-recruitment BBS**, and a client-local **friends list**.
- **Trade** — the **NPC vendor store** (buy/sell), player-to-player trade, the
  warehouse, warp/teleport, the inn, and the upgrade anvil.
- **Quests & menus** — NPC quest menu/talk, NPC-event, notice banner, options/exit,
  help, level guide.
- **Audio** — 3D positional SFX and **streaming MP3 BGM** (NLayer) with town/battle
  track switching, looping, and fade.

Known deviations — all **faithful to the C++ 1.298 original or its own limits** (the
project's rule is strict 1:1, mirroring upstream stubs rather than "fixing" them):

- **Friends online/party status is inert.** The upstream `WIZ_FRIEND_PROCESS` server
  handler is a no-op (`#if 0`), so the C++ client's friend list never lights up online
  either; the port shows the list + local add/remove/whisper and leaves status dead.
- **Item/ring upgrade send is a stub** — it is a stub in the C++ client too
  (`CUIUpgradeSelect` only opens; the enchant send path is empty upstream).
- **IME clause-segment styling** is a single flat underline (see above).
- **FX fine detail** — the FXPMesh LOD collapse walk and per-component `DependScale`
  target sizing are not ported (full-detail draw, scalar scale); visually identical at
  gameplay distance.

Verification is per-slice: pure interaction/layout/protocol logic is unit-tested
headless (`dotnet test … Category!=Database`), protocol builders/parsers are pinned
byte-for-byte against the C# Ebenezer server, and the executable is smoke-rendered
under `xvfb` via `--offline <zone> --screenshot`.

### Client executables + settings tool (Option.exe equivalent)

Like the C++ (`WarFare.exe` + `Option.exe`), the client ships as separate executables:

- **`OpenKO.Client`** — the clean game. No CLI args; it goes straight into the
  interactive login screen. The server endpoint + data path come from
  `appsettings.json` (section `Client`); graphics/sound from `options.json` next
  to the binary.
- **`OpenKO.Client.Dev`** — the debug/CLI build (`--offline <zone>`, scripted
  `--server/--account/--password` auto-login, `--screenshot`, text HUD). None of
  this is compiled into the clean game.
- **`OpenKO.Client.Settings`** — the settings tool, a cross-platform Avalonia GUI
  standing in for `Option.exe`. It reads/writes `options.json` (a
  `System.Text.Json` file next to the exe, the counterpart of the C++ `Option.ini`)
  via the shared `OpenKO.Client.Configuration` library, so the game and the tool
  agree on one model. The in-game **exit menu launches it** (`SettingsLauncher`,
  the port of `ShellExecute("Option.exe")`), passing `--path <game dir>` so the
  file lands where the game reads it. **Settings apply at the next game start** —
  a resolution change needs a restart, exactly as in the original.

**Which knobs take effect now** (the `WarFareMain.cpp:45-118` read/clamp logic is
mirrored in `GameSettings.Normalize`):

- **Applied:** resolution (width/height), fullscreen, VSync, BGM on/off, SFX
  on/off, and the port-added BGM/SFX volume sliders.
- **Stored but inert** (kept for the 1:1 `Option.ini` contract, ready for later
  engine features): texture LOD (Chr/Shape/Terrain), shadows, view distance,
  colour depth, sound distance, window cursor.

The tool defaults to windowed (the current client's default; the original release
build defaulted to fullscreen). On Linux/CI only the build and the store/ViewModel
round-trip tests run headless — the GUI itself is exercised manually on macOS.
