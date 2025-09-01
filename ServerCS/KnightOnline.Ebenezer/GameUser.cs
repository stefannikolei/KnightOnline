using KnightOnline.Shared;
using KnightOnline.Shared.Networking;
using System.Net.Sockets;

namespace KnightOnline.Ebenezer;

/// <summary>
/// Represents a game user connected to the Ebenezer server
/// </summary>
public class GameUser : SocketConnection
{
    private readonly EbenezerConfig _config;
    private readonly EbenezerServer _server;
    private DateTime _lastSaveTime = DateTime.UtcNow;
    private DateTime _lastPacketTime = DateTime.UtcNow;

    public int UserId { get; }
    public string AccountId { get; private set; } = "";
    public string CharacterName { get; private set; } = "";
    public byte Nation { get; private set; } = 0;
    public byte Class { get; private set; } = 0;
    public int Level { get; private set; } = 1;
    public int Experience { get; private set; } = 0;
    public int HP { get; private set; } = 100;
    public int MP { get; private set; } = 100;
    public float X { get; private set; } = 0;
    public float Y { get; private set; } = 0;
    public float Z { get; private set; } = 0;
    public byte Zone { get; private set; } = 1;
    public bool IsGameStarted { get; private set; } = false;

    public GameUser(Socket socket, int userId, EbenezerConfig config, EbenezerServer server) 
        : base(socket)
    {
        UserId = userId;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    public override void OnReceive(byte[] data, int length)
    {
        if (length < 1) return;

        _lastPacketTime = DateTime.UtcNow;

        try
        {
            byte opcode = data[0];
            
            if (_config.EnablePacketLogging)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Game Packet received: 0x{opcode:X2} ({length} bytes) from {RemoteEndPoint} (User: {UserId})");
            }

            switch (opcode)
            {
                case Opcodes.WIZ_VERSION_CHECK:
                    HandleVersionCheck(data, length);
                    break;
                
                case Opcodes.WIZ_CRYPTION:
                    HandleCryption(data, length);
                    break;
                
                case Opcodes.WIZ_LOGIN:
                    HandleLogin(data, length);
                    break;
                
                case Opcodes.WIZ_ALLCHAR_INFO_REQ:
                    HandleAllCharacterInfoRequest(data, length);
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
                
                case Opcodes.WIZ_GAMESTART:
                    HandleGameStart(data, length);
                    break;
                
                case Opcodes.WIZ_MYINFO:
                    HandleMyInfo(data, length);
                    break;
                
                case Opcodes.WIZ_MOVE:
                    HandleMove(data, length);
                    break;
                
                case Opcodes.WIZ_ROTATE:
                    HandleRotate(data, length);
                    break;
                
                case Opcodes.WIZ_ATTACK:
                    HandleAttack(data, length);
                    break;
                
                case Opcodes.WIZ_CHAT:
                    HandleChat(data, length);
                    break;
                
                case Opcodes.WIZ_LOGOUT:
                    HandleLogout(data, length);
                    break;
                
                case Opcodes.WIZ_ITEM_MOVE:
                    HandleItemMove(data, length);
                    break;
                
                case Opcodes.WIZ_NPC_EVENT:
                    HandleNpcEvent(data, length);
                    break;
                
                case Opcodes.WIZ_MAGIC_PROCESS:
                    HandleMagicProcess(data, length);
                    break;
                
                case Opcodes.WIZ_PARTY:
                    HandleParty(data, length);
                    break;
                
                case Opcodes.WIZ_EXCHANGE:
                    HandleExchange(data, length);
                    break;
                
                case Opcodes.WIZ_KNIGHTS_PROCESS:
                    HandleKnightsProcess(data, length);
                    break;
                
                default:
                    if (_config.EnableDebugLogging)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Unknown opcode received from user {UserId}: 0x{opcode:X2}");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing packet from user {UserId}: {ex.Message}");
            if (_config.EnableDebugLogging)
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    public void OnGameTick()
    {
        // Update user state, handle timeouts, etc.
        if ((DateTime.UtcNow - _lastPacketTime).TotalMinutes > 5)
        {
            // Timeout - disconnect user
            Disconnect();
        }
    }

    public bool ShouldAutoSave()
    {
        return (DateTime.UtcNow - _lastSaveTime).TotalSeconds >= _config.AutoSaveInterval;
    }

    public void SaveUserData()
    {
        if (!IsGameStarted || string.IsNullOrEmpty(AccountId)) return;

        try
        {
            var savePacket = new Packet(Opcodes.WIZ_DATASAVE);
            savePacket.WriteString(AccountId);
            savePacket.WriteString(CharacterName);
            savePacket.WriteInt32(Experience);
            savePacket.WriteInt32(Level);
            // Add more user data...
            
            _server.SendToDatabaseAsync(savePacket);
            _lastSaveTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving user data for {AccountId}: {ex.Message}");
        }
    }

    #region Packet Handlers

    private void HandleVersionCheck(byte[] data, int length)
    {
        var response = new Packet(Opcodes.WIZ_VERSION_CHECK);
        response.WriteByte(1); // Version OK
        Send(response);
    }

    private void HandleCryption(byte[] data, int length)
    {
        var response = new Packet(Opcodes.WIZ_CRYPTION);
        response.WriteByte(1); // Encryption OK
        Send(response);
    }

    private void HandleLogin(byte[] data, int length)
    {
        // Forward to database server for validation
        var loginPacket = new Packet(Opcodes.WIZ_LOGIN);
        loginPacket.WriteBytes(data, 1, length - 1); // Copy data excluding opcode
        _server.SendToDatabaseAsync(loginPacket);
    }

    private void HandleAllCharacterInfoRequest(byte[] data, int length)
    {
        var charInfoPacket = new Packet(Opcodes.WIZ_ALLCHAR_INFO_REQ);
        charInfoPacket.WriteString(AccountId);
        _server.SendToDatabaseAsync(charInfoPacket);
    }

    private void HandleNewCharacter(byte[] data, int length)
    {
        var newCharPacket = new Packet(Opcodes.WIZ_NEW_CHAR);
        newCharPacket.WriteBytes(data, 1, length - 1);
        _server.SendToDatabaseAsync(newCharPacket);
    }

    private void HandleDeleteCharacter(byte[] data, int length)
    {
        var delCharPacket = new Packet(Opcodes.WIZ_DEL_CHAR);
        delCharPacket.WriteBytes(data, 1, length - 1);
        _server.SendToDatabaseAsync(delCharPacket);
    }

    private void HandleSelectCharacter(byte[] data, int length)
    {
        var selCharPacket = new Packet(Opcodes.WIZ_SEL_CHAR);
        selCharPacket.WriteBytes(data, 1, length - 1);
        _server.SendToDatabaseAsync(selCharPacket);
    }

    private void HandleSelectNation(byte[] data, int length)
    {
        if (length < 2) return;
        
        Nation = data[1];
        var response = new Packet(Opcodes.WIZ_SEL_NATION);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleGameStart(byte[] data, int length)
    {
        IsGameStarted = true;
        
        // Send user info to other players in the area
        var userInPacket = new Packet(Opcodes.WIZ_USER_INOUT);
        userInPacket.WriteByte(1); // IN
        userInPacket.WriteInt32(UserId);
        userInPacket.WriteString(CharacterName);
        userInPacket.WriteFloat(X);
        userInPacket.WriteFloat(Y);
        userInPacket.WriteFloat(Z);
        // Add more user data...
        
        _server.BroadcastPacket(userInPacket, this);
        
        // Send response to client
        var response = new Packet(Opcodes.WIZ_GAMESTART);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleMyInfo(byte[] data, int length)
    {
        var myInfoPacket = new Packet(Opcodes.WIZ_MYINFO);
        myInfoPacket.WriteInt32(HP);
        myInfoPacket.WriteInt32(MP);
        myInfoPacket.WriteInt32(Experience);
        myInfoPacket.WriteInt32(Level);
        // Add more detailed user info...
        Send(myInfoPacket);
    }

    private void HandleMove(byte[] data, int length)
    {
        if (length < 13) return; // Basic move packet size
        
        // Parse movement data
        X = BitConverter.ToSingle(data, 1);
        Y = BitConverter.ToSingle(data, 5);
        Z = BitConverter.ToSingle(data, 9);
        
        // Broadcast movement to other players
        var movePacket = new Packet(Opcodes.WIZ_MOVE);
        movePacket.WriteInt32(UserId);
        movePacket.WriteFloat(X);
        movePacket.WriteFloat(Y);
        movePacket.WriteFloat(Z);
        
        _server.BroadcastPacket(movePacket, this);
    }

    private void HandleRotate(byte[] data, int length)
    {
        // Broadcast rotation to other players
        var rotatePacket = new Packet(Opcodes.WIZ_ROTATE);
        rotatePacket.WriteInt32(UserId);
        rotatePacket.WriteBytes(data, 1, length - 1);
        
        _server.BroadcastPacket(rotatePacket, this);
    }

    private void HandleAttack(byte[] data, int length)
    {
        // TODO: Implement attack logic
        var attackPacket = new Packet(Opcodes.WIZ_ATTACK);
        attackPacket.WriteInt32(UserId);
        attackPacket.WriteBytes(data, 1, length - 1);
        
        _server.BroadcastPacket(attackPacket, this);
    }

    private void HandleChat(byte[] data, int length)
    {
        if (length < 3) return;
        
        byte chatType = data[1];
        // Parse chat message...
        
        var chatPacket = new Packet(Opcodes.WIZ_CHAT);
        chatPacket.WriteByte(chatType);
        chatPacket.WriteString(CharacterName);
        chatPacket.WriteBytes(data, 2, length - 2);
        
        _server.BroadcastPacket(chatPacket, this);
    }

    private void HandleLogout(byte[] data, int length)
    {
        SaveUserData();
        
        var logoutPacket = new Packet(Opcodes.WIZ_LOGOUT);
        logoutPacket.WriteString(AccountId);
        _server.SendToDatabaseAsync(logoutPacket);
        
        Disconnect();
    }

    private void HandleItemMove(byte[] data, int length)
    {
        // TODO: Implement item movement logic
        var response = new Packet(Opcodes.WIZ_ITEM_MOVE);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleNpcEvent(byte[] data, int length)
    {
        // TODO: Implement NPC event handling
        var response = new Packet(Opcodes.WIZ_NPC_EVENT);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleMagicProcess(byte[] data, int length)
    {
        // TODO: Implement magic processing
        var response = new Packet(Opcodes.WIZ_MAGIC_PROCESS);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleParty(byte[] data, int length)
    {
        // TODO: Implement party system
        var response = new Packet(Opcodes.WIZ_PARTY);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleExchange(byte[] data, int length)
    {
        // TODO: Implement item exchange
        var response = new Packet(Opcodes.WIZ_EXCHANGE);
        response.WriteByte(1); // Success
        Send(response);
    }

    private void HandleKnightsProcess(byte[] data, int length)
    {
        // Forward to database server for knights clan operations
        var knightsPacket = new Packet(Opcodes.WIZ_KNIGHTS_PROCESS);
        knightsPacket.WriteBytes(data, 1, length - 1);
        _server.SendToDatabaseAsync(knightsPacket);
    }

    #endregion
}