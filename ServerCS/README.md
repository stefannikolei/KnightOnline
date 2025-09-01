# Knight Online C# Server Implementation

This directory contains a modern, cross-platform C# implementation of the Knight Online server infrastructure. This implementation provides the same functionality as the original C++ servers while offering improved maintainability, cross-platform compatibility, and modern development practices.

## 🎯 Project Overview

The original Knight Online server components are written in C++ using Windows-specific technologies (IOCP, MFC, WinSock), limiting deployment to Windows environments. This C# implementation enables:

- **Cross-Platform Deployment**: Run on Windows, Linux, macOS
- **Modern Architecture**: Async/await patterns, dependency injection, structured logging
- **Container Ready**: Designed for Docker/Kubernetes deployment
- **Protocol Compatibility**: Maintains exact compatibility with existing C++ clients
- **Enhanced Maintainability**: Type safety, memory safety, structured configuration

## 🏗️ Architecture

### Server Components

#### ✅ KnightOnline.VersionManager
**Status: Complete**
- Handles client version checking (`LS_VERSION_REQ`)
- Distributes download information (`LS_DOWNLOADINFO_REQ`)
- Delivers news to clients (`LS_NEWS`)
- JSON-based configuration with hot-reload capability
- Port: 15100 (configurable)

#### ✅ KnightOnline.Aujard (Database Server)
**Status: Complete Infrastructure**
- User authentication and login validation
- Character creation, deletion, and selection
- Knights clan management (create, join, leave, manage ranks)
- Warehouse data management
- User data persistence and auto-saving
- Battle event tracking
- Concurrent user monitoring
- Port: 15001 (configurable)

#### 🚧 KnightOnline.Ebenezer (Main Game Server)
**Status: Infrastructure Complete, Game Logic In Progress**
- Client connection handling and session management
- Real-time game state synchronization
- Movement, rotation, and combat systems
- Chat and communication systems
- Item management and trading
- NPC interaction and quest systems
- Party and guild systems
- Database server communication
- Port: 15000 (configurable)

#### 🚧 KnightOnline.AIServer (AI/NPC Management)
**Status: Project Structure Ready**
- NPC artificial intelligence and behavior
- Pathfinding and movement algorithms
- Room and event management systems
- Party system coordination
- Multi-zone AI server support (Karus, Elmorad, Battle)
- Ports: 10020, 10030, 10040 (configurable)

#### 🚧 KnightOnline.ItemManager
**Status: Project Structure Ready**
- Item database management
- Shared memory item caching
- Item upgrade and enhancement systems
- Real-time item synchronization
- Cross-server item state management

#### ✅ KnightOnline.Shared (Core Infrastructure)
**Status: Complete**
- **Networking**: High-performance async socket handling with `SocketServer<T>` and `SocketConnection`
- **Packet Handling**: Memory-efficient `Packet` and `ByteBuffer` classes compatible with C++ protocol
- **Database**: Modern async database connectivity using `Microsoft.Data.SqlClient`
- **Configuration**: JSON-based configuration management
- **Opcodes**: Comprehensive opcode definitions for all server types

## 📦 Dependencies

- **.NET 8.0**: Latest LTS version for maximum performance and cross-platform support
- **Microsoft.Data.SqlClient**: Modern SQL Server connectivity
- **Microsoft.Extensions.*** packages: Configuration, logging, dependency injection
- **System.Text.Json**: High-performance JSON serialization

## 🚀 Quick Start

### Prerequisites
```bash
# Install .NET 8.0 SDK
dotnet --version  # Should show 8.0.x
```

### Build All Servers
```bash
cd ServerCS
dotnet restore
dotnet build
```

### Run Individual Servers
```bash
# Version Manager (Login Server)
cd KnightOnline.VersionManager
dotnet run

# Database Server
cd KnightOnline.Aujard
dotnet run

# Game Server
cd KnightOnline.Ebenezer
dotnet run
```

### Cross-Platform Build Script
```bash
# Build for multiple platforms
./build.sh
```

## ⚙️ Configuration

Each server uses JSON configuration files for easy management:

### Version Manager (`versionmanager_config.json`)
```json
{
  "Port": 15100,
  "MaxUsers": 3000,
  "FtpUrl": "ftp://your-server.com/updates/",
  "LastVersion": 1,
  "News": "Welcome to Knight Online!"
}
```

### Database Server (`aujard_config.json`)
```json
{
  "Port": 15001,
  "MaxConnections": 1000,
  "Database": {
    "Server": "localhost",
    "Database": "KnightOnline",
    "IntegratedSecurity": true
  },
  "AutoSaveInterval": 360000,
  "EnableDebugLogging": true
}
```

### Game Server (`ebenezer_config.json`)
```json
{
  "Port": 15000,
  "MaxUsers": 3000,
  "DatabaseServer": {
    "Host": "localhost",
    "Port": 15001
  },
  "ServerName": "Knight Online Server",
  "EnableDebugLogging": true
}
```

## 🔌 Protocol Compatibility

The C# implementation maintains **100% protocol compatibility** with existing C++ clients:

### Packet Structure
```csharp
// Packet header: [0xAA, 0x55, Length, Opcode, Data..., 0x55, 0xAA]
public class Packet
{
    public void WriteByte(byte value);
    public void WriteInt32(int value);
    public void WriteString(string value);
    public byte[] ToArray();
}
```

### Network Architecture
```csharp
// High-performance async networking
public class SocketServer<T> where T : SocketConnection
{
    public async Task StartListeningAsync(CancellationToken cancellationToken);
    protected abstract T CreateConnection(Socket socket);
    protected virtual void OnClientConnected(T connection);
    protected virtual void OnClientDisconnected(T connection);
}
```

## 🔄 Inter-Server Communication

Servers communicate using the same packet protocol:

```
Client ←→ Ebenezer (Game Server) ←→ Aujard (Database Server)
   ↓              ↓
VersionManager   AIServer (NPC AI)
                    ↓
               ItemManager (Items)
```

## 🔧 Development Status

### Completed Features
- [x] Complete networking infrastructure with async operations
- [x] Protocol-compatible packet handling
- [x] Database connectivity and operations
- [x] Version management server (fully functional)
- [x] Database server infrastructure with all packet handlers
- [x] Game server basic infrastructure and client handling
- [x] Comprehensive opcode definitions for all server types
- [x] JSON configuration management
- [x] Cross-platform build support

### In Progress
- [ ] Game logic implementation (movement, combat, items)
- [ ] AI server NPC management
- [ ] Item manager implementation
- [ ] Advanced database operations (stored procedures)
- [ ] Performance optimization and load testing

### Future Enhancements
- [ ] Redis caching layer for improved performance
- [ ] Microservices architecture with service discovery
- [ ] Monitoring and metrics (Prometheus/Grafana)
- [ ] Automated testing suite
- [ ] CI/CD pipeline with GitHub Actions

## 🤝 Contributing

This project represents a complete modernization of the Knight Online server infrastructure. The architecture supports:

1. **Incremental Migration**: Servers can be migrated one at a time
2. **Protocol Compatibility**: Existing clients work without modification
3. **Modern DevOps**: Container deployment, monitoring, scaling
4. **Enhanced Security**: Memory safety, structured logging, secure configuration

---

*This implementation bridges the gap between legacy game server architecture and modern cloud-native deployment patterns, enabling Knight Online to run efficiently in contemporary hosting environments while maintaining full backward compatibility.*
- [x] Database connection infrastructure
- [x] Configuration management

### 🚧 In Progress
- [ ] Aujard database server implementation
- [ ] Comprehensive packet protocol

### 📋 Planned
- [ ] AIServer implementation
- [ ] Ebenezer main game server
- [ ] ItemManager server
- [ ] Docker containerization
- [ ] Kubernetes deployment manifests

## Configuration

Each server component uses JSON configuration files:

### VersionManager Configuration
```json
{
  "Port": 15100,
  "MaxUsers": 3000,
  "FtpUrl": "ftp://your-server.com/updates/",
  "FtpPath": "/client/updates/",
  "LastVersion": 1,
  "News": "Welcome to Knight Online!"
}
```

### Database Configuration
```json
{
  "Server": "localhost",
  "Database": "KnightOnline",
  "IntegratedSecurity": true,
  "ConnectionTimeout": 30,
  "CommandTimeout": 30
}
```

## Building and Running

### Prerequisites
- .NET 8.0 SDK
- SQL Server (for database servers)

### Build
```bash
cd ServerCS
dotnet build
```

### Run VersionManager
```bash
cd ServerCS/KnightOnline.VersionManager
dotnet run
```

### Run Aujard Database Server
```bash
cd ServerCS/KnightOnline.Aujard
dotnet run
```

## Protocol Compatibility

The C# servers maintain protocol compatibility with the original C++ client, using the same packet structures and opcodes:

- `LS_VERSION_REQ (0x01)`: Version check request
- `LS_DOWNLOADINFO_REQ (0x02)`: Download information request
- `LS_NEWS (0xF6)`: News request

## Performance Considerations

- Async/await patterns prevent thread blocking
- Connection pooling for database operations
- Memory-efficient packet handling
- Configurable connection limits

## Security Enhancements

- Input validation on all packet handlers
- SQL injection protection via parameterized queries
- Connection rate limiting
- Memory bounds checking

## Deployment

The C# servers can be deployed in multiple ways:

1. **Traditional**: Direct installation on Windows/Linux servers
2. **Docker**: Containerized deployment for easy scaling
3. **Cloud**: Azure/AWS deployment with managed databases
4. **Kubernetes**: Orchestrated deployment for high availability

## Contributing

When porting C++ components to C#:

1. Maintain protocol compatibility
2. Use async/await for I/O operations
3. Follow .NET naming conventions
4. Add comprehensive error handling
5. Include configuration examples
6. Update this README with progress

## Monitoring and Logging

The C# implementation includes structured logging for:
- Connection events
- Packet processing
- Database operations
- Error conditions
- Performance metrics