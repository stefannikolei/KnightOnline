# Knight Online Server C# Port

This directory contains the C# port of the Knight Online server components, designed to enable cross-platform deployment and easier development.

## Architecture

The C# port maintains the same modular architecture as the original C++ implementation:

### Core Components

- **KnightOnline.Shared**: Common library containing networking, database, and utility classes
- **KnightOnline.VersionManager**: Version and patch management server
- **KnightOnline.Aujard**: Database management server  
- **KnightOnline.AIServer**: AI and NPC management server (planned)
- **KnightOnline.Ebenezer**: Main game logic server (planned)
- **KnightOnline.ItemManager**: Item management server (planned)

## Technology Stack

- **.NET 8.0**: Modern, cross-platform framework
- **Microsoft.Data.SqlClient**: SQL Server database connectivity
- **System.Text.Json**: Configuration and data serialization
- **Custom Networking**: Async socket-based communication

## Key Improvements

### Cross-Platform Compatibility
- Runs on Windows, Linux, and macOS
- No dependency on Windows-specific APIs (IOCP, MFC)
- Uses .NET's async/await for high-performance networking

### Modern Development Features
- JSON-based configuration instead of INI files
- Structured logging
- Memory-safe implementation
- Async/await patterns for better scalability

### Networking Architecture
The C++ IOCP-based networking has been replaced with:
- `SocketConnection`: Base class for client connections
- `SocketServer<T>`: Generic server for accepting connections
- Async networking using .NET's Socket APIs

## Current Status

### ✅ Completed
- [x] Basic networking infrastructure
- [x] Packet handling system (ByteBuffer, Packet)
- [x] Circular buffer implementation
- [x] VersionManager server with version checking
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