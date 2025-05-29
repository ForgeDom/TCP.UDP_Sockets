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

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        string ingredients = IngredientsBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ingredients))
        {
            ResponseBox.Text = "Введіть інгредієнти!";
            return;
        }
        try
        {
            using var udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = 3000;
            var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 9000); // або IP сервера
            byte[] data = Encoding.UTF8.GetBytes(ingredients);
            await udpClient.SendAsync(data, data.Length, serverEndpoint);
            var result = await udpClient.ReceiveAsync();
            string response = Encoding.UTF8.GetString(result.Buffer);
            ResponseBox.Text = response;
        }
        catch (SocketException ex)
        {
            ResponseBox.Text = "Сервер не відповідає або перевищено ліміт клієнтів.";
        }
        catch (Exception ex)
        {
            ResponseBox.Text = $"Помилка: {ex.Message}";
        }
    }
}

