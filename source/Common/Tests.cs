//using GameNet.Client;
using GameNet.Common;
//using GameNet.Server;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace GameNet.Tests
{
    /*
    public static class SelfTest
    {
        // Toggle tests here:
        public static bool Run = false;

        public static async Task RunAllAsync()
        {
            if (!Run) return;

            Console.WriteLine("=== SelfTest starting ===");

            var server = new Server.Server(new ServerOptions
            {
                BindAddress = System.Net.IPAddress.Loopback,
                Port = 9443,
                // For local testing without a cert:
                UseTls = false,
                AllowInsecureForTesting = true,

                // If you have a PFX:
                // UseTls = true,
                // ServerCertificate = new X509Certificate2("server.pfx", "pfxPassword")
            });

            server.DataReceived += (id, type, payload) =>
            {
                Console.WriteLine($"[Test-Server] DataReceived from {id}: type={type}, {payload?.Length ?? 0} bytes");
                // Echo back data messages
                if (type == MessageType.Data)
                    server.SendAsync(id, MessageType.Data, payload).Wait();
            };

            await server.StartAsync();

            var client = new ClientConnection(new ClientOptions
            {
                Host = "127.0.0.1",
                Port = 9443,
                UseTls = false,
                AllowInvalidServerCertForTesting = true
            });

            client.DataReceived += (type, payload) =>
            {
                Console.WriteLine($"[Test-Client] DataReceived: type={type}, len={payload?.Length ?? 0}");
            };
            client.Disconnected += () => Console.WriteLine("[Test-Client] Disconnected.");

            await client.ConnectAsync();
            await client.LoginAsync("tester", "password123"); // stub always OK

            var msg = System.Text.Encoding.UTF8.GetBytes("Hello, loopback!");
            await client.SendDataAsync(msg);

            // Allow some time for echo
            await Task.Delay(500);

            await client.DisconnectAsync();
            await server.StopAsync();

            Console.WriteLine("=== SelfTest complete ===");
        }
    }
    */

}
