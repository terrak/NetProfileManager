using System.Windows;
using NetProfileManager.Core.Models;

namespace NetProfileManager.App;

public partial class ProfileEditorWindow : Window
{
    public NetworkProfile? Profile { get; private set; }

    public ProfileEditorWindow() : this(null)
    {
    }

    public ProfileEditorWindow(NetworkProfile? existing)
    {
        InitializeComponent();
        if (existing != null)
        {
            Title = $"Modifier le profil « {existing.Name} »";
            NameBox.Text = existing.Name;
            IpBox.Text = existing.IpAddress;
            MaskBox.Text = existing.SubnetMask;
            GatewayBox.Text = existing.Gateway ?? string.Empty;
            DnsBox.Text = string.Join(", ", existing.DnsServers);
            DescriptionBox.Text = existing.Description ?? string.Empty;
            Profile = existing;
        }
        else
        {
            MaskBox.Text = "255.255.255.0";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dns = DnsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        Profile = new NetworkProfile
        {
            Id = Profile?.Id ?? string.Empty,
            Name = NameBox.Text.Trim(),
            IpAddress = IpBox.Text.Trim(),
            SubnetMask = MaskBox.Text.Trim(),
            Gateway = string.IsNullOrWhiteSpace(GatewayBox.Text) ? null : GatewayBox.Text.Trim(),
            DnsServers = dns,
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim()
        };

        if (string.IsNullOrWhiteSpace(Profile.Name))
        {
            MessageBox.Show(this, "Le nom du profil est obligatoire.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
