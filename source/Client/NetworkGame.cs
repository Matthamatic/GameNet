using GameNet.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public enum GameState 
{
    InitReady,
    AwaitConnect,
    AwaitAuth,
    AwaitLoading,

    Paused,
    Running,
    Ended
}

class NetworkGame
{
    public GameState RunningState { get; private set; }

    ClientProgram connection;

    public NetworkGame()
    {
        RunningState = GameState.InitReady;
        connection = new ClientProgram();
        connection.AuthEvent += OnAuthEvent;
        connection.DataReceived += OnReceiveData;

        connection.Client.Connected += OnConnected;
        connection.Client.Disconnected += OnDisconnected;
        
        connection.Client.ConnectFail += OnConnectFail;
    }

    public void Run()
    {
        if (RunningState != GameState.InitReady) { return; }

        RunningState = GameState.AwaitConnect;
        connection.Start();

        while (RunningState != GameState.Ended)
        {
            switch (RunningState)
            {
                case GameState.Running:
                    // Do local game logic
                    break;
                case GameState.AwaitLoading:
                    // Do game assets and world init 
                    break;
                case GameState.Paused:
                case GameState.InitReady:
                case GameState.AwaitAuth:
                case GameState.AwaitConnect:
                case GameState.Ended:
                    break;
            }
            Thread.Sleep(200);
        }
    }

    void OnReceiveData(GameDataResult result)
    {
        // Process data
        switch (result.DataType)
        {
            case DataType.LoadComplete:
                // Loading is complete. If we were waiting, we can start running now.
                if (RunningState == GameState.AwaitLoading)
                { RunningState = GameState.Running; }
                break;
            case DataType.GameObject:
                // Instantiate and replace/update objects
                break;
            default:
                break;
        }
    }

    void OnAuthEvent(AuthEventType authEvent, string message)
    {
        switch (authEvent)
        {
            case AuthEventType.AuthComplete:
                if (RunningState != GameState.AwaitAuth)
                { Console.WriteLine("Unexpected auth!"); }
                else
                {
                    // After auth, setup the scene
                    RunningState = GameState.AwaitLoading;
                }
                break;
            case AuthEventType.RegistraitionComplete:
                //
                doLogin();
                break;
        }


        
    }

    void OnConnected()
    {
        if (RunningState != GameState.AwaitConnect)
        {
            Console.WriteLine("Unexpected connect!");
        }

        Console.WriteLine("Connected!");
        //doAuth();
        // Next we need to auth
        RunningState = GameState.AwaitAuth;
        launchAuthScreen();
    }

    void OnConnectFail(Exception ex)
    { 
        Console.WriteLine($"{ex.GetType()}!\n{ex.Message}"); 

        // Show and wait?
    }

    /// <summary>
    /// Catch disconnects to pause local logic and, if we're not in an ended state we try to reconnect.
    /// </summary>
    void OnDisconnected()
    {
        // Setting the state will Pause game loop 
        if (RunningState != GameState.Ended)
        { 
            RunningState = GameState.AwaitConnect;
            Console.WriteLine("Unexpected Disconnect! Waiting for reconnect.");
        }
    }


    void launchServerScreen()
    {
        // Join (Existing)
        // Delete (Existing)
        // Add Server/Local World
        // Quit
    }
    void launchAuthScreen()
    {
        // New User
        // Log in
        // Disconnect & Quit

        Console.WriteLine("Press N to register as a new user\n" +
                "Press L to log in\n" +
                "Press Q to quit\n");

        switch (Console.ReadKey().Key)
        {
            case ConsoleKey.N:
                doRegister();
                break;
            case ConsoleKey.L:
                doLogin();
                break;
            case ConsoleKey.Q:
                doDisconnect();
                Environment.Exit(0);
                break;
        }

    }

    void doLogin()
    {
        Console.WriteLine("Enter Name:");
        string name = Console.ReadLine().Trim();
        Console.WriteLine("Enter Password:");
        string pass = Console.ReadLine().Trim();
        _ = connection.Client.LoginAsync(name, pass);
    }
    void doRegister()
    {
        Console.WriteLine("Enter a name:");
        string name = Console.ReadLine().Trim();
        Console.WriteLine("Enter a password:");
        string pass = Console.ReadLine().Trim();
        _ = connection.Client.RegisterAsync(name, pass);
    }
    void doDisconnect()
    { _ = connection.Client.DisconnectAsync(); }

}
