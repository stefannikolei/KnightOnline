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
| 5 | Client foundations: N3 asset loaders + math | in progress — framework-free `OpenKO.Client.Assets` scaffold, the `CN3BaseFileAccess` header port, the My_3DStruct.h vertex layouts (size-pinned) and the __Quaternion Slerp/YawPitchRoll port (5.1) done; texture/mesh/anim/char/terrain/UI readers and AssetDump open. The `Client/Data` asset submodule (ko-client-assets, openko-1298) checks out in this environment, so the loaders can be verified against the real 1298 asset corpus |
| 6 | Client engine core on MonoGame (fixed-function emulation, UI system) | open |
| 7 | Client game (WarFare port: states, networking, ~80 UI dialogs) | open |
| 8 | Hardening, soak tests, docs | open |

See the stage plan in the repository history / PR description for details.
