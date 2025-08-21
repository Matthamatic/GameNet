using System.Threading;

class Program
{
    static void Main()
    {
        Thread.Sleep(200);
        NetworkGame game = new NetworkGame();
        game.Run();
    }

    

}
