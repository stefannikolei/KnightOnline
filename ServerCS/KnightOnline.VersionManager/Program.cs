using KnightOnline.VersionManager;

Console.WriteLine("Knight Online Version Manager Server (C#)");
Console.WriteLine("=========================================");

var server = new VersionManagerServer();
server.Start();

Console.WriteLine("Press any key to stop the server...");
Console.ReadKey();

server.Stop();
