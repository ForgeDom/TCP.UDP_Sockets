using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client;

class RecipeClient
{
    private readonly UdpClient _udpClient;
    private readonly int _serverPort;
    private readonly string _serverAddress;

    public RecipeClient(string serverAddress, int serverPort)
    {
        _udpClient = new UdpClient();
        _serverPort = serverPort;
        _serverAddress = serverAddress;
    }

    public void Run()
    {
        Console.WriteLine("Введіть інгредієнти через кому (або 'exit' для виходу):");
        while (true)
        {
            Console.Write("> ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLower() == "exit")
                break;
            var data = Encoding.UTF8.GetBytes(input);
            _udpClient.Send(data, data.Length, _serverAddress, _serverPort);
            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            var response = _udpClient.Receive(ref remoteEP);
            string recipes = Encoding.UTF8.GetString(response);
            Console.WriteLine($"Рецепти: {recipes}\n");
        }
        _udpClient.Close();
    }
}

class Program
{
    static void Main(string[] args)
    {
        var client = new RecipeClient("127.0.0.1", 9000);
        client.Run();
    }
}

