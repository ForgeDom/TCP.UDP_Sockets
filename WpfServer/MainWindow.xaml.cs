using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Windows;
using System.Text;

namespace WpfServer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ObservableCollection<ClientInfo> _clients = new();
    private CancellationTokenSource _cts = new();
    private readonly string _logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerLog.txt");

    public MainWindow()
    {
        InitializeComponent();
        ClientsList.ItemsSource = _clients;
        StartServer();
    }

    private void StartServer()
    {
        Task.Run(() => RunServer(_cts.Token));
    }

    private void Log(string message)
    {
        var logLine = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}\n", DateTime.Now, message);
        File.AppendAllText(_logFile, logLine);
    }

    private async Task RunServer(CancellationToken token)
    {
        int port = 9000;
        var udpClient = new UdpClient(port);
        var activeClients = new Dictionary<string, DateTime>();
        var recipes = new Dictionary<string, List<string>>
        {
            { "Омлет", new List<string> { "яйце", "молоко", "сіль" } },
            { "Салат Цезар", new List<string> { "курка", "салат", "сир", "яйце", "сухарики" } },
            { "Борщ", new List<string> { "буряк", "капуста", "картопля", "морква", "цибуля" } },
            { "Картопля фрі", new List<string> { "картопля", "олія", "сіль" } },
        };
        const int MaxConcurrentClients = 5;
        TimeSpan ClientTimeout = TimeSpan.FromMinutes(10);
        Log($"Сервер запущено на порті {port}");
        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var inactive = activeClients.Where(kv => now - kv.Value > ClientTimeout).Select(kv => kv.Key).ToList();
            foreach (var key in inactive)
            {
                Log($"Клієнт {key} відключений через неактивність");
                activeClients.Remove(key);
            }
            UpdateClientsUI(activeClients);

            if (udpClient.Available > 0)
            {
                var result = await udpClient.ReceiveAsync();
                var clientKey = result.RemoteEndPoint.ToString();
                if (!activeClients.ContainsKey(clientKey))
                {
                    if (activeClients.Count >= MaxConcurrentClients)
                    {
                        Log($"Відмовлено клієнту {clientKey}: перевищено ліміт {MaxConcurrentClients}");
                        var limitMsg = Encoding.UTF8.GetBytes($"Перевищено ліміт одночасних клієнтів: {MaxConcurrentClients}");
                        udpClient.Send(limitMsg, limitMsg.Length, result.RemoteEndPoint);
                        continue;
                    }
                    activeClients[clientKey] = DateTime.UtcNow;
                    Log($"Підключено нового клієнта: {clientKey}");
                }
                else
                {
                    activeClients[clientKey] = DateTime.UtcNow;
                }
                UpdateClientsUI(activeClients);

                try
                {
                    var request = Encoding.UTF8.GetString(result.Buffer);
                    Log($"Запит від {clientKey}: '{request}'");
                    var ingredients = request.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var foundRecipes = recipes.Where(r => ingredients.All(i => r.Value.Contains(i, StringComparer.OrdinalIgnoreCase)))
                                             .Select(r => r.Key)
                                             .ToList();
                    string response = foundRecipes.Count > 0 ? string.Join(", ", foundRecipes) : "Рецептів не знайдено";
                    Log($"Відповідь для {clientKey}: '{response}'");
                    var responseBytes = Encoding.UTF8.GetBytes(response);
                    await udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                }
                catch (Exception ex)
                {
                    Log($"Помилка обробки запиту від {clientKey}: {ex.Message}");
                }
            }
            await Task.Delay(500);
        }
    }

    private void UpdateClientsUI(Dictionary<string, DateTime> activeClients)
    {
        Dispatcher.Invoke(() =>
        {
            _clients.Clear();
            foreach (var kv in activeClients)
            {
                _clients.Add(new ClientInfo { Client = kv.Key, LastActive = kv.Value.ToLocalTime().ToString("HH:mm:ss") });
            }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts.Cancel();
        base.OnClosing(e);
    }
}

public class ClientInfo
{
    public string Client { get; set; }
    public string LastActive { get; set; }
}

