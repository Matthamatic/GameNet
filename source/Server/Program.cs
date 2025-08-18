using GameNet.Common;
using GameNet.Data;
using GameNet.Server;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

class Program
{

    static Server server;

    static async Task Main()
    {
        await Database.InitializeAsync("data/gamenet.db");

        var options = new ServerOptions
        {
            BindAddress = System.Net.IPAddress.Any,
            Port = 9000,
            //UseTls = true,
            // Load a real certificate in production:
            // ServerCertificate = new X509Certificate2("server.pfx", "pfxPassword")
            // For quick local bring-up without a cert:
            AllowInsecureForTesting = true,
            UseTls = false
        };

        server = new Server(options);
        server.ClientConnected += id => Console.WriteLine($"ClientConnected: {id}");
        server.ClientDisconnected += id => Console.WriteLine($"ClientDisconnected: {id}");
        server.DataReceived += OnServerDataReceived;

        await server.StartAsync();
        Console.WriteLine("Press Enter to stop...");
        Console.ReadLine();
        await server.StopAsync();
    }


    static private void OnServerDataReceived(Guid id, MessageType type, byte[] payload)
    {
        Console.WriteLine($"[Server] Data from {id} type={type} len={payload?.Length ?? 0}");

        if (type == MessageType.Data)
        { Send(id,type,payload); }
    }

    static void Send(Guid id, MessageType type, byte[] payload)
    {
        _ = server.SendAsync(id, type, payload).ContinueWith(t =>
        {
            Console.Error.WriteLine($"Send failed: {t.Exception}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

}
