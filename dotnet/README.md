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
- **Configuration files are unchanged** — the servers read the same INI files
  (`Version.ini`, `ItemManager.ini`, …) with the same keys and defaults, so a C++
  deployment can swap in the .NET binary without config changes.

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
- SqlClient needs a server host, which ODBC DSNs carried out-of-band: the
  optional `[ODBC] SERVER` key or `OPENKO_DB_SERVER` env var provides it
  (default `localhost`, matching `docker-compose.yaml`).

## Layout

```
src/OpenKO.Core        shared/: ByteBuffer, Packet, opcodes, JvCryption, KoCrc32,
                       Lzf, CP949 encoding, IniFile, djb2
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

## Running

```bash
# VersionManager: reads Version.ini from the working directory, listens on 15100.
# Needs the MSSQL database (docker compose up) with the KN_online schema.
cd <dir with Version.ini> && dotnet run --project dotnet/src/Servers/OpenKO.Servers.VersionManager

# ItemManager: reads ItemManager.ini (optional), listens on 127.0.0.1:15200.
dotnet run --project dotnet/src/Servers/OpenKO.Servers.ItemManager

# AIServer: reads server.ini ([SERVER] ZONE, [ODBC] GAME_DSN/UID/PWD) and the
# MAP/ directory (SMDs + <n>.evt room events) next to it; listens on the
# zone-type port (10020 karus/unify, 10030 elmorad, 10040 battle) for Ebenezer.
cd <dir with server.ini + MAP/> && dotnet run --project dotnet/src/Servers/OpenKO.Servers.AIServer

# AssetDump: N3 client asset inspection — JSON metrics per file, textures to PNG.
dotnet run --project dotnet/tools/OpenKO.AssetDump -- Client/Data/Item/1_1011_00_0.n3cplug
dotnet run --project dotnet/tools/OpenKO.AssetDump -- Client/Data/Zones          # whole directory
dotnet run --project dotnet/tools/OpenKO.AssetDump -- Client/Data/symbol_us --png /tmp/pngs

# Scripted protocol client (also used by the parity harness):
dotnet run --project dotnet/tools/OpenKO.TestClient -- 127.0.0.1:15100 version
dotnet run --project dotnet/tools/OpenKO.TestClient -- 127.0.0.1:15100 serverlist
dotnet run --project dotnet/tools/OpenKO.TestClient -- 127.0.0.1:15100 login account pw
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
| 7 | Client game (WarFare port: states, networking, ~80 UI dialogs) | in progress — `OpenKO.Client.Game` scaffold: the `GameState`/`GameStateMachine` port of the CGameProcedure driver (single active-state pointer with the deferred Release()→Init() swap and the render-guard), the client network layer — `GameClientSocketCore` (the mirror of the server core: unencrypted login-server framing, then the WIZ_VERSION_CHECK-keyed encrypt-on-send [counter+payload+crc32] / decrypt-on-recv [0x1EFC-tagged block] and WIZ_COMPRESS_PACKET/LZF unwrap) and the `KoClientConnection` async link, plus the `LoginProtocol`/`GameProtocol` builders+parsers for the login→char-select opcode set (LS_SERVERLIST/LOGIN/NEWS, WIZ_VERSION_CHECK/LOGIN/SEL_NATION/ALLCHAR_INFO/NEW_CHAR/DEL_CHAR/SEL_CHAR/GAMESTART). Verified by a headless test suite that round-trips frames through the real server `GameSocketCore` in both directions (encrypted + plain) and pins every packet layout (7.1); and the login→char-select flow states — `GameContext` (the shared-statics analog: account/server/nation/spawn + the CGameProcedure shared handler for WIZ_VERSION_CHECK/WIZ_SEL_CHAR) with `LoginState`/`NationSelectState`/`CharSelectState`/`CharCreateState`/`InGameState` driving the exact C++ transition graph (server-list → account login → version-check → game login → nation branch → char list → select/create → spawn → in-game), plus the `NetworkGameClient` pump adapter (WM_SOCKETMSG-style receive queue drained on the game thread). Verified by a headless flow test that drives the whole login→nation→char-select→in-game sequence through synthetic server replies (7.2); and the in-world entry core — `WorldEntities` (the CPlayerOtherMgr roster: local player + region-visible remote players by socket id), the `WorldProtocol` parsers (WIZ_MYINFO prefix, WIZ_MOVE, the WIZ_USER_INOUT GetUserInfo blob, WIZ_CHAT) with the ×10 fixed-point→world-float conversion, and `InGameState` wiring the WIZ_GAMESTART two-phase handshake, the entity in/out/move stream and chat — field order pinned against the C# Ebenezer send side and verified by a headless test that drives the entry handshake, MyInfo, user in/out, movement (local + remote) and chat through server-shaped packets (7.3); and the item/magic slice — `ItemProtocol` (WIZ_ITEM_MOVE build for the four e_ItemMoveDirection modes + result byte), `MagicProtocol` (the central WIZ_MAGIC_PROCESS command/spell/source/target/6-data packet, build+parse), a client-side `Inventory` (flat position map with the drag move/swap), and `InGameState` wiring optimistic item moves + the magic broadcast — pinned against the C# Ebenezer and verified headless (7.4) done |
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
- **Deferred (not yet ported):** runtime terrain lightmaps (`.tlt` streaming), the
  `Terrain_Base.bmp` detail overlay (a `.bmp`, not an NTF `.dxt`), sky sun/moon/stars
  and the day-change colour simulation, and TGA-format cloud textures — so the sky
  currently shows the horizon-glow fans in the day fog colour without the celestial
  bodies. FX particles (`N3FX*`) and audio are later stages.
