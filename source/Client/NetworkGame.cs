using GameNet.Common;
using GameNet.Data;
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
    AwaitRegister,
    AwaitAuth,
    AwaitLoading,

    Paused,
    Running,
    Ended,
    ConnectFail
}

class NetworkGame : IDisposable
{
    public GameState RunningState { get; private set; }

    ClientProgram connection;

    public NetworkGame()
    {
        RunningState = GameState.InitReady;
        connection = new ClientProgram();
        connection.ClientEvent += OnClientEvent;
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
                case GameState.ConnectFail:
                    break;
            }
            Thread.Sleep(200);
        }
    }

    public void Stop()
    {
        RunningState = GameState.Ended;
        Dispose();
    }

    public void Dispose()
    {
        if (RunningState != GameState.Ended)
        {
            Console.WriteLine("Unexpected dispose of NetworkGame.");
            RunningState = GameState.Ended;
        }

        if (connection != null)
        {
            connection.ClientEvent -= OnClientEvent;
            connection.DataReceived -= OnReceiveData;
            connection.Client.Connected -= OnConnected;
            connection.Client.Disconnected -= OnDisconnected;
            connection.Client.ConnectFail -= OnConnectFail;
            connection.Stop();
            connection = null;
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

    void OnClientEvent(ClientEventType cevent, string message)
    {
        switch (cevent)
        {
            case ClientEventType.ConnectFail:
                RunningState = GameState.ConnectFail;
                Console.WriteLine("Failed to connect!");
                // End or try again
                break;
            case ClientEventType.AuthComplete:
                if (RunningState != GameState.AwaitAuth)
                { Console.WriteLine("Unexpected auth!"); }
                else
                {
                    // After auth, setup the scene
                    RunningState = GameState.AwaitLoading;
                }
                break;
            case ClientEventType.RegistraitionComplete:
                if (RunningState != GameState.AwaitRegister)
                { Console.WriteLine("Unexpected register!"); }

                doLogin();
                break;
            case ClientEventType.AuthFail:
                Console.WriteLine($"Log in failed!\n{message}");
                launchAuthMenu();
                break;

            case ClientEventType.RegistraitionFail:
                Console.WriteLine($"Registration failed!\n{message}");
                launchAuthMenu();
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
        launchAuthMenu();
    }

    void OnConnectFail(Exception ex)
    { 
        
        Console.WriteLine($"Failed to Connect!\n{ex.GetType()}!\n{ex.Message}"); 

    }

    /// <summary>
    /// Catch disconnects to pause local logic and, if we're not in an ended state await connect.
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


    //void launchServerScreen()
        // Join (Existing)
        // Delete (Existing)
        // Add Server/Local World
        // Quit
    
    void launchAuthMenu()
    {
        bool inputLoop = true;
        while (inputLoop)
        {
            Console.WriteLine("\nLogin Menu:\n" +
                "  Press N to register as a new user\n" +
                "  Press L to log in\n" +
                "  Press Escape to quit\n");
            
            ConsoleKey key = Console.ReadKey(true).Key;
            inputLoop = false;
            switch (key)
            {
                case ConsoleKey.N:
                    doRegister();
                    break;
                case ConsoleKey.L:
                    doLogin();
                    break;
                case ConsoleKey.Escape:
                    doDisconnect();
                    Environment.Exit(0);
                    break;
                default:
                    inputLoop = true;
                    Console.WriteLine("Bad input.");
                    break;
            }
        }
    }

    void doLogin()
    {
        RunningState = GameState.AwaitAuth;
        bool Cancel = false;

        string username = "";
        bool validName = false;
        while (!validName && !Cancel)
        {
            Console.WriteLine("Enter Name:");
            readInput(out username, out bool isEscape);

            if (isEscape)
            {
                Cancel = true;
                break;
            }

            username = username.Trim();
            var nameresult = UsernameValidator.Validate(username);
            validName = nameresult.ok;

            if (!nameresult.ok)
            {
                var errmsg = nameresult.errors.Length == 1 ? nameresult.errors[0] : string.Join("; ", nameresult.errors);
                Console.WriteLine($"Invald username\n{errmsg}\n");  
            }
        }

        string pass = "";
        bool validPW = false;
        while (!validPW && !Cancel)
        {
            Console.WriteLine("Enter Password:");
            readInput(out pass, out bool isEscape);

            if (isEscape)
            {
                Cancel = true;
                break;
            }
            pass = pass.Trim();
            var pwresult = PasswordValidator.Validate(pass, username);
            validPW = pwresult.ok;
            if (!pwresult.ok)
            {
                var errmsg = pwresult.errors.Length == 1 ? pwresult.errors[0] : string.Join("; ", pwresult.errors);
                Console.WriteLine($"Invald password\n{errmsg}\n");
            }
        }

        if (Cancel) { launchAuthMenu(); }
        else { _ = connection.Client.LoginAsync(username, pass); }

        
    }
    void doRegister()
    {
        RunningState = GameState.AwaitRegister;
        bool Cancel = false;

        string username = "";
        bool validName = false;
        while (!validName && !Cancel)
        {
            Console.WriteLine("Enter a Name:");

            readInput(out username, out bool isEscape);

            if (isEscape)
            {
                Cancel = true;
                break;
            }

            username = username.Trim();
            var nameresult = UsernameValidator.Validate(username);
            validName = nameresult.ok;

            if (!nameresult.ok)
            {
                var errmsg = nameresult.errors.Length == 1 ? nameresult.errors[0] : string.Join("; ", nameresult.errors);
                Console.WriteLine($"Invald username\n{errmsg}\n");
            }
        }

        string pass = "";
        bool validPW = false;
        while (!validPW && !Cancel)
        {
            Console.WriteLine("Enter a Password:");

            readInput(out pass, out bool isEscape);

            if (isEscape)
            {
                Cancel = true;
                break;
            }

            pass = pass.Trim();
            var pwresult = PasswordValidator.Validate(pass, username);
            validPW = pwresult.ok;
            if (!pwresult.ok)
            {
                var errmsg = pwresult.errors.Length == 1 ? pwresult.errors[0] : string.Join("; ", pwresult.errors);
                Console.WriteLine($"Invald password\n{errmsg}\n");
            }
        }

        if (Cancel) { launchAuthMenu(); } 
        else  { _ = connection.Client.RegisterAsync(username, pass); }
    }
    void doDisconnect()
    {
        connection.Stop();
        connection = null;
    }

    /// <summary>
    /// Console helper class.
    /// Limits characters.
    /// Allows canceling
    /// </summary>
    /// <param name="text"></param>
    /// <param name="isEscape"></param>
    void readInput(out string text, out bool isEscape)
    {
        int x = Console.CursorLeft;
        int y = Console.CursorTop;
        bool reading = true;
        isEscape = false;
        text = "";

        while (reading)
        {

            ConsoleKeyInfo ki = Console.ReadKey(true);
            switch (ki.Key)
            {
                case ConsoleKey.Enter:
                    Console.Write("\n");
                    reading = false;
                    break;
                case ConsoleKey.Escape:
                    reading = false;
                    isEscape = true;
                    break;
                case ConsoleKey.Backspace:
                    text = text.Substring(0, text.Length - 1);
                    Console.SetCursorPosition(x, y);
                    Console.Write(text + " ");
                    break;
                default:
                    if (char.IsLetterOrDigit(ki.KeyChar) || ki.KeyChar == ' ')
                    {
                        text += ki.KeyChar;
                        Console.SetCursorPosition(x, y);
                        Console.Write(text);
                    }
                    break;
            }
        }
    }

}
