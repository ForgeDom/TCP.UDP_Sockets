using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text.Json;

namespace WpfClient;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ShowRecipeImage(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            RecipeImage.Source = null;
            return;
        }
        try
        {
            byte[] imageBytes = Convert.FromBase64String(base64);
            using var ms = new System.IO.MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            RecipeImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            RecipeImage.Source = null;
            ResponseBox.Text += $"\n[Діагностика] Помилка декодування картинки: {ex.Message}";
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        string ingredients = IngredientsBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ingredients))
        {
            ResponseBox.Text = "Введіть інгредієнти!";
            ShowRecipeImage(null);
            return;
        }
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 9001);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8, 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
            await writer.WriteLineAsync(ingredients);
            string? response = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(response))
            {
                ResponseBox.Text = "Пуста відповідь від сервера.";
                ShowRecipeImage(null);
                return;
            }
            try
            {
                var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("Error", out var err))
                {
                    ResponseBox.Text = err.GetString() ?? "Невідома помилка";
                    ShowRecipeImage(null);
                }
                else
                {
                    string? recipe = doc.RootElement.GetProperty("Recipe").GetString();
                    string? ingr = doc.RootElement.GetProperty("Ingredients").GetString();
                    string? img = doc.RootElement.GetProperty("ImageBase64").GetString();
                    string? imgError = doc.RootElement.TryGetProperty("ImageError", out var errImg) ? errImg.GetString() : null;
                    ResponseBox.Text = $"Рецепт: {recipe}\nІнгредієнти: {ingr}\n[Діагностика] Довжина base64: {(img?.Length ?? 0)}\n[ImageError]: {imgError}";
                    ShowRecipeImage(img);
                }
            }
            catch (Exception ex)
            {
                ResponseBox.Text = response + $"\n[Діагностика] JSON error: {ex.Message}";
                ShowRecipeImage(null);
            }
        }
        catch (SocketException)
        {
            ResponseBox.Text = "Сервер не відповідає або перевищено ліміт клієнтів.";
            ShowRecipeImage(null);
        }
        catch (Exception ex)
        {
            ResponseBox.Text = $"Помилка: {ex.Message}";
            ShowRecipeImage(null);
        }
    }
}

