using GameNet.Common;
using GameNetClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


public enum ClientEventType
{
    RegistraitionComplete,
    RegistraitionFail,
    AuthComplete,
    AuthFail,
    ConnectFail
}

public class ClientProgram : IDisposable
{
    const int CONNECTTRYLIMIT = 10;
    public bool AutoReconnect
    {
        get { return _autoReconnect; }
        set
        {
            if (value)
            {
                if (Client != null && !Client.IsConnected)
                { Start(); }
            }
            _autoReconnect = value;
        }
    }
    private bool _autoReconnect = true;

    public event Action<GameDataResult> DataReceived;
    public event Action<ClientEventType, string> ClientEvent;

    public ClientConnection Client { get; private set; }
    public bool Stopped { get; private set; }
    

    public bool isConnected => (Client != null && Client.IsConnected);

    int connectTryCount = 0;
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
        Client.Connected += OnConnected;
        Client.ConnectFail += OnFailConnect;
    }

    public void Start()
    {
        if (Client.IsConnected)
        { return; }
        _ = Client.ConnectAsync();
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
            Client.Connected -= OnConnected;
            Client.ConnectFail -= OnFailConnect;

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

    void doReconnect(int delaySeconds)
    {
        connectTryCount++;
        if (connectTryCount > CONNECTTRYLIMIT)
        {
            ClientEvent?.Invoke(ClientEventType.ConnectFail, "Connection retry limit reached.");
        }
        else
        {
            Thread reconnectThread = new Thread(() => {
                Thread.Sleep(delaySeconds * 1000);
                Start();
            });
            reconnectThread.Start();
        }
    }

    private void OnConnected()
    {
        // Reset the connect try count
        connectTryCount = 0;
    }
    private void OnDisconnected()
    {

        if (!Stopped && AutoReconnect)
        { doReconnect(5); }
        else
        { 
            Console.WriteLine("Disconnected!(Stopping)"); 
        }
    }
    private void OnFailConnect(Exception ex)
    {
        if (AutoReconnect)
        { doReconnect(10); }
        else 
        { ClientEvent?.Invoke(ClientEventType.ConnectFail, "Could not connect."); }
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
                    string message = Protocol.ReadLPString(br);
                    if (authenticationResult)
                    {
                        //Console.WriteLine("Authenticated!");
                        ClientEvent?.Invoke(ClientEventType.AuthComplete, message);
                    }
                    else
                    {
                        ClientEvent?.Invoke(ClientEventType.AuthFail, message);
                    }
                }
                break;
            case MessageType.RegisterResponse:
                using (var ms = new MemoryStream(payload))
                using (var br = new BinaryReader(ms))
                {
                    bool registerResult = br.ReadInt32() == 1;
                    string message = Protocol.ReadLPString(br);
                    if (registerResult)
                    {
                        ClientEvent?.Invoke(ClientEventType.RegistraitionComplete, message);
                    }
                    else
                    {
                        ClientEvent?.Invoke(ClientEventType.RegistraitionFail, message);
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

