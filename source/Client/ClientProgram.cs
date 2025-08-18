using GameNet.Client;
using GameNet.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class ClientProgram
{
    ClientConnection client;

    bool isConnected
    { get { return client != null && client.IsConnected; } }

    public void Start()
    {
        _ = doConnect();
    }

    async Task doConnect()
    {
        client = new ClientConnection(new ClientOptions
        {
            Host = "127.0.0.1",
            Port = 9000,
            UseTls = false,                    // set true with a valid server cert
            AllowInvalidServerCertForTesting = true
        });
        client.DataReceived += OnDataReceived;
        await client.ConnectAsync();
        Console.WriteLine("Connected!");
        await doAuth();

        // Auth (stub always OK)
        //await client.LoginAsync("Alice", "supersecret");

        // Send opaque data payload
        //

        //byte[] data = File.ReadAllBytes("./Diablo.iso");
        //await client.SendDataAsync(data);

    }

    async Task doAuth()
    {
        Console.WriteLine("Press N to register as a new user\n" +
                "Press L to log in\n" +
                "Press Q to quit\n");

        switch (Console.ReadKey().Key)
        {
            case ConsoleKey.N:
                await doRegister();
                break;
            case ConsoleKey.L:
                await doLogin();
                break;
            case ConsoleKey.Q:
                await doDisconnect();
                Environment.Exit(0);
                break;
        }
    }

    async Task doLogin()
    {
        Console.WriteLine("Enter Name:");
        string name = Console.ReadLine().Trim();
        Console.WriteLine("Enter Password:");
        string pass = Console.ReadLine().Trim();
        await client.LoginAsync(name, pass);
    }
    async Task doRegister()
    {
        Console.WriteLine("Enter a name:");
        string name = Console.ReadLine().Trim();
        Console.WriteLine("Enter a password:");
        string pass = Console.ReadLine().Trim();
        await client.RegisterAsync(name, pass);
    }
    async Task doDisconnect()
    { await client.DisconnectAsync(); }

    public void SendText(string text)
    {
        List<byte> data = new List<byte>();
        data.AddRange(BitConverter.GetBytes((int)DataType.Chat));
        data.AddRange(Encoding.UTF8.GetBytes(text));

        _ = client.SendDataAsync(data.ToArray()); 
    }

    private void OnDataReceived(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.AuthResponse:
                using (var ms = new MemoryStream(payload))
                using (var br = new BinaryReader(ms))
                {
                    bool authenticationResult = br.ReadInt32() == 1;
                    if (authenticationResult)
                    { 
                        Console.WriteLine("Authenticated!");
                        SendText("Hooo!");
                    }
                    else
                    {
                        Console.WriteLine("Failed to Authenticate!");
                        _ = doAuth(); 
                    }
                }
                break;
            case MessageType.RegisterResponse:
                using (var ms = new MemoryStream(payload))
                using (var br = new BinaryReader(ms))
                {
                    bool registerResult = br.ReadInt32() == 1;
                    if (registerResult)
                    {
                        Console.WriteLine("Registered!");
                        _ = doLogin();
                    }
                    else
                    {
                        Console.WriteLine("Failed to Register!");
                        _ = doAuth();
                    }
                }
                break;
            default:

                if (payload.Length > 4)
                {
                    GameDataResult result = new GameDataResult(payload);
                    if (result.DataType == DataType.Chat)
                    { Console.WriteLine($"[Client] Received type={type}, {result.DataType} text='{(string)result.UntypedObject}'"); }
                    else
                    { Console.WriteLine($"[Client] Received type={type}, {result.DataType}"); }// text='{Encoding.UTF8.GetString(payload ?? Array.Empty<byte>())}'"); }
                }

                
                break;
        }

        
    }
}

