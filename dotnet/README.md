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
src/OpenKO.Data        SqlClient connection factory + models (stage-1 subset)
src/OpenKO.Hosting     generic-host glue (INI config resolution, logging)
src/Servers/…          VersionManager, ItemManager (stage 1)
tests/…                unit + golden-vector + end-to-end tests
tests/vectors/         checked-in golden vectors generated from the C++ reference
tools/OpenKO.TestClient  scripted protocol client (verification / parity harness)
tools/golden-gen/      C++ generator for the golden vectors (links shared/)
```

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
| 2 | OpenKO.Data full build-out + Aujard as library/hosted service | open |
| 3 | AIServer (+ OpenKO.GameData: .tbl loader, N3ShapeMgr MAP collision) | open |
| 4 | Ebenezer (WIZ_CRYPTION handshake, LZF packets, game logic) | open |
| 5 | Client foundations: N3 asset loaders + math | open |
| 6 | Client engine core on MonoGame (fixed-function emulation, UI system) | open |
| 7 | Client game (WarFare port: states, networking, ~80 UI dialogs) | open |
| 8 | Hardening, soak tests, docs | open |

See the stage plan in the repository history / PR description for details.
