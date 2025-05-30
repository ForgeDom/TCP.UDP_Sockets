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

public partial class MainWindow : Window
{
    private ObservableCollection<ClientInfo> _clients = new();
    private CancellationTokenSource _cts = new();
    private readonly string _logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerLog.txt");

    private readonly Dictionary<string, List<string>> recipes = new()
    {
        { "Омлет", new List<string> { "яйце", "молоко", "сіль" } },
        { "Салат Цезар", new List<string> { "курка", "салат", "сир", "яйце", "сухарики" } },
        { "Борщ", new List<string> { "буряк", "капуста", "картопля", "морква", "цибуля" } },
        { "Картопля фрі", new List<string> { "картопля", "олія", "сіль" } },
    };
    private readonly Dictionary<string, string> recipeImages = new()
    {
        { "Омлет", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "omlet.jpg") },
        { "Салат Цезар", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "salad.jpg") },
        { "Борщ", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "borsh.jpg") },
        { "Картопля фрі", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "kartofel-fry.jpg") },
    };

    private readonly Dictionary<string, DateTime> _activeTcpClients = new();

    public MainWindow()
    {
        InitializeComponent();
        ClientsList.ItemsSource = _clients;
        StartServer();
        StartTcpServer();
    }

    private void StartServer()
    {
        Task.Run(() => RunServer(_cts.Token));
    }

    private void StartTcpServer()
    {
        Task.Run(() => RunTcpServer(_cts.Token));
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
                    var foundRecipes = recipes.Where(r => ingredients.Any(i => r.Value.Contains(i, StringComparer.OrdinalIgnoreCase)))
                                             .Select(r => r.Key)
                                             .ToList();
                    string response;
                    if (foundRecipes.Count > 0)
                    {
                        var recipeName = foundRecipes[0];
                        var recipeIngredients = string.Join(", ", recipes[recipeName]);
                        string imageBase64 = null;
                        string imageError = null;
                        if (recipeImages.TryGetValue(recipeName, out var imagePath))
                        {
                            if (File.Exists(imagePath))
                            {
                                try
                                {
                                    var imageBytes = File.ReadAllBytes(imagePath);
                                    imageBase64 = Convert.ToBase64String(imageBytes);
                                }
                                catch (Exception ex)
                                {
                                    imageError = $"Помилка читання файлу: {ex.Message}";
                                    Log($"Помилка читання картинки для '{recipeName}': {ex.Message}");
                                }
                            }
                            else
                            {
                                imageError = $"Файл не знайдено: {imagePath}";
                                Log($"Картинка для '{recipeName}' не знайдена: {imagePath}");
                            }
                        }
                        else
                        {
                            imageError = "Відсутній шлях до картинки для рецепта";
                            Log($"Відсутній шлях до картинки для '{recipeName}'");
                        }
                        var responseObj = new
                        {
                            Recipe = recipeName,
                            Ingredients = recipeIngredients,
                            ImageBase64 = imageBase64,
                            ImageError = imageError
                        };
                        response = System.Text.Json.JsonSerializer.Serialize(responseObj);
                    }
                    else
                    {
                        response = System.Text.Json.JsonSerializer.Serialize(new { Error = "Рецептів не знайдено" });
                    }
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

    private async Task RunTcpServer(CancellationToken token)
    {
        int port = 9001;
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Log($"TCP сервер запущено на порті {port}");
        while (!token.IsCancellationRequested)
        {
            if (!listener.Pending())
            {
                await Task.Delay(200, token);
                continue;
            }
            var client = await listener.AcceptTcpClientAsync(token);
            _ = Task.Run(() => HandleTcpClient(client), token);
        }
    }

    private async Task HandleTcpClient(TcpClient client)
    {
        string clientKey = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
        _activeTcpClients[clientKey] = DateTime.Now;
        UpdateClientsUI();
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, 4096, true) { AutoFlush = true };
            string? request = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(request)) return;
            Log($"[TCP] Запит: '{request}'");
            var ingredients = request.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var foundRecipes = recipes.Where(r => ingredients.Any(i => r.Value.Contains(i, StringComparer.OrdinalIgnoreCase)))
                                     .Select(r => r.Key)
                                     .ToList();
            string response;
            if (foundRecipes.Count > 0)
            {
                var recipeName = foundRecipes[0];
                var recipeIngredients = string.Join(", ", recipes[recipeName]);
                string? imageBase64 = null;
                string? imageError = null;
                if (recipeImages.TryGetValue(recipeName, out var imagePath))
                {
                    if (File.Exists(imagePath))
                    {
                        try
                        {
                            var imageBytes = File.ReadAllBytes(imagePath);
                            imageBase64 = Convert.ToBase64String(imageBytes);
                        }
                        catch (Exception ex)
                        {
                            imageError = $"Помилка читання файлу: {ex.Message}";
                            Log("[TCP] Помилка читання картинки для '" + recipeName + "': " + ex.Message);
                        }
                    }
                    else
                    {
                        imageError = $"Файл не знайдено: {imagePath}";
                        Log("[TCP] Картинка для '" + recipeName + "' не знайдена: " + imagePath);
                    }
                }
                else
                {
                    imageError = "Відсутній шлях до картинки для рецепта";
                    Log("[TCP] Відсутній шлях до картинки для '" + recipeName + "'");
                }
                var responseObj = new
                {
                    Recipe = recipeName,
                    Ingredients = recipeIngredients,
                    ImageBase64 = imageBase64,
                    ImageError = imageError
                };
                response = System.Text.Json.JsonSerializer.Serialize(responseObj);
            }
            else
            {
                response = System.Text.Json.JsonSerializer.Serialize(new { Error = "Рецептів не знайдено" });
            }
            await writer.WriteLineAsync(response);
        }
        catch (Exception ex)
        {
            Log("[TCP] Помилка обробки TCP-запиту: " + ex.Message);
        }
        finally
        {
            _activeTcpClients.Remove(clientKey);
            UpdateClientsUI();
            client.Close();
        }
    }

    // Оновлення UI для обох типів клієнтів
    private void UpdateClientsUI(Dictionary<string, DateTime>? udpClients = null)
    {
        Dispatcher.Invoke(() =>
        {
            _clients.Clear();
            if (udpClients != null)
            {
                foreach (var kv in udpClients)
                {
                    _clients.Add(new ClientInfo { Client = kv.Key + " (UDP)", LastActive = kv.Value.ToLocalTime().ToString("HH:mm:ss") });
                }
            }
            foreach (var kv in _activeTcpClients)
            {
                _clients.Add(new ClientInfo { Client = kv.Key + " (TCP)", LastActive = kv.Value.ToLocalTime().ToString("HH:mm:ss") });
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

