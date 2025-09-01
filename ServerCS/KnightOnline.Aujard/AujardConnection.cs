using KnightOnline.Shared;
using KnightOnline.Shared.Database;
using KnightOnline.Shared.Networking;
using Microsoft.Data.SqlClient;
using System.Net.Sockets;
using System.Text;

namespace KnightOnline.Aujard;

/// <summary>
/// Represents a connection to the Aujard database server from a game server (Ebenezer, etc.)
/// </summary>
public class AujardConnection : SocketConnection
{
    private readonly DatabaseManager _database;
    private readonly AujardConfig _config;

    public AujardConnection(Socket socket, DatabaseManager database, AujardConfig config) 
        : base(socket)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public override void OnReceive(byte[] data, int length)
    {
        if (length < 1) return;

        try
        {
            byte opcode = data[0];
            
            if (_config.EnablePacketLogging)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] DB Packet received: 0x{opcode:X2} ({length} bytes) from {RemoteEndPoint}");
            }

            switch (opcode)
            {
                case Opcodes.WIZ_LOGIN:
                    HandleLogin(data, length);
                    break;
                
                case Opcodes.WIZ_NEW_CHAR:
                    HandleNewCharacter(data, length);
                    break;
                
                case Opcodes.WIZ_DEL_CHAR:
                    HandleDeleteCharacter(data, length);
                    break;
                
                case Opcodes.WIZ_SEL_CHAR:
                    HandleSelectCharacter(data, length);
                    break;
                
                case Opcodes.WIZ_SEL_NATION:
                    HandleSelectNation(data, length);
                    break;
                
                case Opcodes.WIZ_ALLCHAR_INFO_REQ:
                    HandleAllCharacterInfoRequest(data, length);
                    break;
                
                case Opcodes.WIZ_LOGOUT:
                    HandleLogout(data, length);
                    break;
                
                case Opcodes.WIZ_DATASAVE:
                    HandleDataSave(data, length);
                    break;
                
                case Opcodes.WIZ_KNIGHTS_PROCESS:
                    HandleKnightsProcess(data, length);
                    break;
                
                case Opcodes.WIZ_CLAN_PROCESS:
                    HandleClanProcess(data, length);
                    break;
                
                case Opcodes.WIZ_LOGIN_INFO:
                    HandleLoginInfo(data, length);
                    break;
                
                case Opcodes.WIZ_KICKOUT:
                    HandleKickout(data, length);
                    break;
                
                case Opcodes.WIZ_BATTLE_EVENT:
                    HandleBattleEvent(data, length);
                    break;
                
                case Opcodes.DB_COUPON_EVENT:
                    HandleCouponEvent(data, length);
                    break;
                
                default:
                    if (_config.EnableDebugLogging)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Unknown opcode received: 0x{opcode:X2}");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing packet: {ex.Message}");
            if (_config.EnableDebugLogging)
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    private void HandleLogin(byte[] data, int length)
    {
        // TODO: Implement login validation
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Login request received");
        
        // For now, send a success response
        var response = new Packet(Opcodes.WIZ_LOGIN);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleNewCharacter(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] New character request received");
        
        // TODO: Implement character creation
        var response = new Packet(Opcodes.WIZ_NEW_CHAR);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleDeleteCharacter(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Delete character request received");
        
        // TODO: Implement character deletion
        var response = new Packet(Opcodes.WIZ_DEL_CHAR);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleSelectCharacter(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Select character request received");
        
        // TODO: Implement character selection
        var response = new Packet(Opcodes.WIZ_SEL_CHAR);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleSelectNation(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Select nation request received");
        
        // TODO: Implement nation selection
        var response = new Packet(Opcodes.WIZ_SEL_NATION);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleAllCharacterInfoRequest(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] All character info request received");
        
        // TODO: Implement character list retrieval
        var response = new Packet(Opcodes.WIZ_ALLCHAR_INFO_REQ);
        response.WriteByte(0); // Character count (0 for now)
        Send(response);
    }

    private void HandleLogout(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Logout request received");
        
        // TODO: Implement logout logic
        var response = new Packet(Opcodes.WIZ_LOGOUT);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleDataSave(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Data save request received");
        
        // TODO: Implement user data saving
        var response = new Packet(Opcodes.WIZ_DATASAVE);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleKnightsProcess(byte[] data, int length)
    {
        if (length < 2) return;
        
        byte subOpcode = data[1];
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Knights process request received: 0x{subOpcode:X2}");
        
        // TODO: Implement knights clan management
        var response = new Packet(Opcodes.WIZ_KNIGHTS_PROCESS);
        response.WriteByte(subOpcode);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleClanProcess(byte[] data, int length)
    {
        if (length < 2) return;
        
        byte subOpcode = data[1];
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Clan process request received: 0x{subOpcode:X2}");
        
        // TODO: Implement clan management
        var response = new Packet(Opcodes.WIZ_CLAN_PROCESS);
        response.WriteByte(subOpcode);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleLoginInfo(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Login info request received");
        
        // TODO: Implement login info update
        var response = new Packet(Opcodes.WIZ_LOGIN_INFO);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleKickout(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Kickout request received");
        
        // TODO: Implement user kickout
        var response = new Packet(Opcodes.WIZ_KICKOUT);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleBattleEvent(byte[] data, int length)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Battle event request received");
        
        // TODO: Implement battle event handling
        var response = new Packet(Opcodes.WIZ_BATTLE_EVENT);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleCouponEvent(byte[] data, int length)
    {
        if (length < 2) return;
        
        byte subOpcode = data[1];
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Coupon event request received: 0x{subOpcode:X2}");
        
        // TODO: Implement coupon event handling
        var response = new Packet(Opcodes.DB_COUPON_EVENT);
        response.WriteByte(subOpcode);
        response.WriteByte(1); // Success
        Send(response);
    }
}