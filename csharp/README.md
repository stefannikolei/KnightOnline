# OpenKO — Cross-Platform C# Client Port

This directory contains an in-progress, cross-platform (.NET 8) C# port of the
OpenKO client, originally written in C++ for Windows (DirectX 9 / MFC / Winsock).

The port runs on **Windows, Linux and macOS** using [Silk.NET](https://github.com/dotnet/Silk.NET)
for windowing, input, OpenGL and OpenAL once the rendering layer is ported.

## Why a port?

The original client is tightly coupled to Windows-only technologies:

| Original (C++)            | Cross-platform replacement (C#) |
|---------------------------|---------------------------------|
| DirectX 9                 | OpenGL via Silk.NET             |
| MFC / Win32 windows       | Silk.NET windowing (GLFW)       |
| Winsock (`WSAAsyncSelect`)| `System.Net.Sockets` async      |
| DirectSound / OpenAL      | OpenAL via Silk.NET             |
| Win32 file APIs           | `System.IO`                     |

## Strategy: foundation first

Rather than attempt a risky big-bang rewrite, the port proceeds bottom-up.
The genuinely portable, headless layers are ported and unit-tested first; the
graphics/audio layers build on top of them later. This keeps every commit
buildable and testable on Linux CI without a GPU.

### Project layout

```
csharp/
├── OpenKO.sln
├── Directory.Build.props          # shared build settings (net8.0)
├── src/
│   ├── OpenKO.Common/             # ByteBuffer, Packet, opcodes, crypto  (port of shared/)
│   ├── OpenKO.Numerics/           # Vector2/3/4, Matrix44, Quaternion    (port of MathUtils/)
│   ├── OpenKO.IO/                 # File access, N3 base + .tbl tables     (port of FileIO/, N3Base file IO)
│   ├── OpenKO.N3/                 # N3 resources (indexed meshes, …)       (port of N3Base resource types)
│   ├── OpenKO.Net/                # TCP game/login client                 (port of CAPISocket)
│   ├── OpenKO.Game/               # game-procedure state machine + UI model (port of CGameProcedure)
│   └── OpenKO.Client/             # cross-platform entry point (Silk.NET)
└── tests/
    └── OpenKO.Tests/              # xUnit tests for the ported layers
```

### Status

- [x] Solution scaffolding (net8.0, cross-platform)
- [x] `OpenKO.Common` — byte buffer, packet, opcode enums, JvCryption, CRC32, circular buffer
- [x] `OpenKO.Numerics` — vectors, 4x4 matrix, quaternion
- [x] `OpenKO.IO` — file reader/writer, N3 base file access + version flags
- [x] `OpenKO.IO` — `.tbl` data tables (typed rows + KO stream-cipher decryption)
- [x] `OpenKO.N3` — indexed mesh loader (`N3IMesh`) with bounds computation + vertex-list expansion
- [x] `OpenKO.N3` — texture loader (`N3Texture`): NTF/.dxt header, DXT & 16/24/32-bit mip chains
- [x] `OpenKO.N3` — GPU upload mapping (`GpuTextureLayout`, backend-neutral, unit-tested)
- [x] `OpenKO.Net` — cross-platform async TCP client with KO packet framing & encryption
- [x] `OpenKO.Client` — OpenGL renderer (shader + VBO mesh + DXT/uncompressed texture upload)
- [x] `OpenKO.N3` — UI tree loader (`.uif`): `N3UIBase`/`N3UIImage`/`N3UIArea`, recursive hierarchy
- [x] `OpenKO.N3` — UI controls: button / string / edit / static / progress / scrollbar / trackbar / tooltip / list
      (the full set the engine-level `.uif` loader can instantiate; icon types are runtime-only, game-layer)
- [x] `OpenKO.Game` — game-procedure state machine (`GameProcedure`/`GameProcedureManager`/`GameContext`)
- [x] `OpenKO.Game` + `OpenKO.Client` — 2D UI render path (`IUiRenderer` + OpenGL `UiRenderer`) and a
      working **login screen** (`LoginProcedure`); `OpenKO.Net` login handshake packets (`LoginProtocol`)
- [ ] Login flow continued: server-select + character-select procedures, real `.uif`/texture assets
- [ ] N3 skin / scene loaders (`CN3Skin`, `CN3Joint`, `CN3AnimControl`, `CN3Chr`, `CN3Scene`, terrain/sky)
- [ ] Game procedures (character select → in-game)

## Building

```bash
cd csharp
dotnet build
dotnet test          # runs the headless unit tests

# Open the client window (shows the login screen through the ported 2D UI render path):
dotnet run --project src/OpenKO.Client

# Other scenes / modes:
dotnet run --project src/OpenKO.Client -- --demo3d        # rotating textured demo mesh (3D path)
dotnet run --project src/OpenKO.Client -- --selftest      # foundation checks, no GPU needed
dotnet run --project src/OpenKO.Client -- --render-test   # render N frames then exit (needs a display/Xvfb)
dotnet run --project src/OpenKO.Client -- --screenshot login.png   # save one frame to PNG/BMP then exit
```

> **Note on wire compatibility:** the network and file-format code preserves the
> exact byte layout of the original (little-endian, KO's single/double-byte
> string length prefix, the `0xAA55 … 0x55AA` packet frame and the JvCryption
> stream cipher) so this client stays compatible with the official protocol and
> on-disk asset formats, per the project goals.
