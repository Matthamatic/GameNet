using GameNet.Client;
using GameNet.Common;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        Thread.Sleep(200);
        NetworkGame game = new NetworkGame();
        game.Run();
    }

    

}
