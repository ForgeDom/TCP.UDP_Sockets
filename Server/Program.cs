using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TCP.UDP_Sockets;

class RecipeServer
{
    private readonly int _port;
    private readonly UdpClient _udpClient;
    private readonly Dictionary<string, List<string>> _recipes;
    private readonly Dictionary<string, List<DateTime>> _clientRequests = new();
    private const int MaxRequestsPerHour = 10;
    private static readonly TimeSpan TimeWindow = TimeSpan.FromHours(1);

    public RecipeServer(int port)
    {
        _port = port;
        _udpClient = new UdpClient(_port);
       
        _recipes = new Dictionary<string, List<string>>
        {
            { "Омлет", new List<string> { "яйце", "молоко", "сіль" } },
            { "Салат Цезар", new List<string> { "курка", "салат", "сир", "яйце", "сухарики" } },
            { "Борщ", new List<string> { "буряк", "капуста", "картопля", "морква", "цибуля" } },
            { "Картопля фрі", new List<string> { "картопля", "олія", "сіль" } },
        };
    }

    public async Task StartAsync()
    {
        Console.WriteLine($"UDP сервер запущено на порті {_port}");
        while (true)
        {
            var result = await _udpClient.ReceiveAsync();
            var clientKey = result.RemoteEndPoint.ToString();
            lock (_clientRequests)
            {
                if (!_clientRequests.ContainsKey(clientKey))
                    _clientRequests[clientKey] = new List<DateTime>();
                // Видалити старі запити
                _clientRequests[clientKey].RemoveAll(dt => dt < DateTime.UtcNow - TimeWindow);
                if (_clientRequests[clientKey].Count >= MaxRequestsPerHour)
                {
                    var limitMsg = Encoding.UTF8.GetBytes($"Перевищено ліміт: не більше {MaxRequestsPerHour} запитів на годину");
                    _udpClient.Send(limitMsg, limitMsg.Length, result.RemoteEndPoint);
                    continue;
                }
                _clientRequests[clientKey].Add(DateTime.UtcNow);
            }
            var request = Encoding.UTF8.GetString(result.Buffer);
            Console.WriteLine($"Отримано запит від {result.RemoteEndPoint}: {request}");
            var ingredients = request.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var foundRecipes = _recipes.Where(r => ingredients.All(i => r.Value.Contains(i, StringComparer.OrdinalIgnoreCase)))
                                       .Select(r => r.Key)
                                       .ToList();
            string response = foundRecipes.Count > 0 ? string.Join(", ", foundRecipes) : "Рецептів не знайдено";
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await _udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        var server = new RecipeServer(9000);
        server.StartAsync().GetAwaiter().GetResult();
    }
}

