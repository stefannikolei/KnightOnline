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
│   ├── OpenKO.IO/                 # File access + N3 file-format base     (port of FileIO/, N3Base file IO)
│   ├── OpenKO.Net/                # TCP game/login client                 (port of CAPISocket)
│   └── OpenKO.Client/             # cross-platform entry point (Silk.NET)
└── tests/
    └── OpenKO.Tests/              # xUnit tests for the ported layers
```

### Status

- [x] Solution scaffolding (net8.0, cross-platform)
- [x] `OpenKO.Common` — byte buffer, packet, opcode enums, JvCryption, CRC32, circular buffer
- [x] `OpenKO.Numerics` — vectors, 4x4 matrix, quaternion
- [x] `OpenKO.IO` — file reader/writer, N3 base file access + version flags
- [x] `OpenKO.Net` — cross-platform async TCP client with KO packet framing & encryption
- [x] `OpenKO.Client` — Silk.NET windowed entry point (opens a window + GL clear)
- [ ] N3 mesh / texture / scene loaders
- [ ] Renderer (OpenGL)
- [ ] UI system (N3UI*)
- [ ] Game procedures (login → character select → in-game)

## Building

```bash
cd csharp
dotnet build
dotnet test          # runs the headless unit tests
dotnet run --project src/OpenKO.Client
```

> **Note on wire compatibility:** the network and file-format code preserves the
> exact byte layout of the original (little-endian, KO's single/double-byte
> string length prefix, the `0xAA55 … 0x55AA` packet frame and the JvCryption
> stream cipher) so this client stays compatible with the official protocol and
> on-disk asset formats, per the project goals.
