using GameNet.Client;
using GameNet.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


public enum AuthEventType
{
    RegistraitionComplete,
    RegistraitionFail,
    AuthComplete,
    AuthFail
}

public class ClientProgram : IDisposable
{
    public event Action<GameDataResult> DataReceived;
    public event Action<AuthEventType, string> AuthEvent;

    public ClientConnection Client { get; private set; }
    public bool Stopped { get; private set; }
    public bool isConnected => (Client != null && Client.IsConnected);

    public ClientProgram()
    {
        Stopped = false;
        Client = new ClientConnection(new ClientOptions
        {
            Host = "127.0.0.1",
            Port = 9000,
            UseTls = false,                    // set true with a valid server cert
            AllowInvalidServerCertForTesting = true
        });
        Client.DataReceived += OnDataReceived;
        Client.Disconnected += OnDisconnected;
    }

    public void Start()
    {
        doConnect();
    }
    public void Stop()
    {
        Stopped = true;
        Dispose();
    }
    public void SendText(string text)
    {
        List<byte> data = new List<byte>();
        data.AddRange(BitConverter.GetBytes((int)DataType.Chat));
        data.AddRange(Encoding.UTF8.GetBytes(text));

        _ = Client.SendDataAsync(data.ToArray());
    }

    public void Dispose()
    {
        if (!Stopped) {
            Console.WriteLine("Warning! The Client program was disposed before stop was called on it.");
            Stopped = true; 
        }

        if (Client != null)
        {
            Client.DataReceived -= OnDataReceived;
            Client.Disconnected -= OnDisconnected;
            if (Client.IsConnected)
            {
                _ = Client.DisconnectAsync();
                while (Client.IsConnected)
                { Thread.Sleep(100); }
            }
            Client.Dispose();
            Client = null;
        }
    }

    /// <summary>
    /// Methods to manage the relationship with the server
    /// That is, internal stuff relating to Connection & Auth
    /// </summary>
    #region Internal Connection & Auth Methods
    void doConnect()
    { _ = Client.ConnectAsync(); }
    
    private void OnDisconnected()
    {

        if (!Stopped)
        {
            Thread.Sleep(1000);
            doConnect();
        }
        else
        { 
            Console.WriteLine("Disconnected!(Stopping)"); 
        }
    }

    #endregion Internal Connection & Auth Methods


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
                        //Console.WriteLine("Authenticated!");
                        AuthEvent?.Invoke(AuthEventType.AuthComplete, "");
                    }
                    else
                    {
                        AuthEvent?.Invoke(AuthEventType.AuthFail, "Failed to Authenticate!");
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
                        AuthEvent?.Invoke(AuthEventType.RegistraitionComplete, "");
                    }
                    else
                    {
                        AuthEvent?.Invoke(AuthEventType.RegistraitionFail, "Failed to Register! Name or password issue!");
                    }
                }
                break;
            case MessageType.Data:
                GameDataResult result = new GameDataResult(payload);
                if (result.DataType != DataType.Invalid)
                { DataReceived?.Invoke(result); }
                else
                { Console.WriteLine("Invalid data received from server"); }
                break;
            default:
                Console.WriteLine($"Received {type} data");
                break;
        }

        
    }
    
}

