using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using NetProfileManager.Core.Models;
using NetProfileManager.Core.Protocol;

namespace NetProfileManager.App;

public class ProfileChoice : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsDhcp { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AdapterRow : INotifyPropertyChanged
{
    public AdapterInfo Adapter { get; set; } = new();

    public string InterfaceAlias => Adapter.InterfaceAlias;

    public string DisplayName => string.IsNullOrWhiteSpace(Adapter.Description) ? Adapter.InterfaceAlias : $"{Adapter.InterfaceAlias} — {Adapter.Description}";

    public string StatusText => $"État : {Adapter.OperationalStatus}";

    public string ConfigText
    {
        get
        {
            var ip = Adapter.CurrentAddresses.Count > 0 ? string.Join(", ", Adapter.CurrentAddresses) : "aucune";
            var gw = Adapter.CurrentGateway ?? "aucune";
            var dns = Adapter.CurrentDns.Count > 0 ? string.Join(", ", Adapter.CurrentDns) : "aucun";
            var mode = Adapter.DhcpEnabled ? "DHCP actif" : "IP statique";
            return $"IP : {ip} | Passerelle : {gw} | DNS : {dns} | {mode}";
        }
    }

    public string ActiveProfileText
    {
        get
        {
            if (Adapter.AssignedDhcp) return "Profil actif : DHCP (automatique)";
            if (Adapter.ActiveProfileId != null)
            {
                var name = Profiles.FirstOrDefault(p => p.Id == Adapter.ActiveProfileId)?.Name ?? Adapter.ActiveProfileId;
                return $"Profil actif : {name}";
            }
            return "Profil actif : aucun (hors gestion)";
        }
    }

    public ObservableCollection<ProfileChoice> AvailableChoices { get; set; } = new();
    public ProfileChoice? SelectedChoice { get; set; }

    public List<NetworkProfile> Profiles { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AdapterRow> _adapterRows = new();
    private List<NetworkProfile> _profiles = new();

    public MainWindow()
    {
        InitializeComponent();
        AdaptersList.ItemsSource = _adapterRows;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        SetStatus("Connexion au service…");
        var profilesResponse = await PipeClient.SendAsync(Commands.ListProfiles);
        if (!profilesResponse.Success)
        {
            SetStatus($"Service injoignable ou erreur : {profilesResponse.Message}");
            return;
        }

        _profiles = PipeClient.DeserializeData<List<NetworkProfile>>(profilesResponse) ?? new List<NetworkProfile>();
        ProfilesList.ItemsSource = _profiles;

        var adaptersResponse = await PipeClient.SendAsync(Commands.ListAdapters);
        if (!adaptersResponse.Success)
        {
            SetStatus(adaptersResponse.Message ?? "Erreur inconnue.");
            return;
        }

        var adapters = PipeClient.DeserializeData<List<AdapterInfo>>(adaptersResponse) ?? new List<AdapterInfo>();

        _adapterRows.Clear();
        foreach (var adapter in adapters)
        {
            var row = new AdapterRow { Adapter = adapter, Profiles = _profiles };
            row.AvailableChoices.Clear();
            row.AvailableChoices.Add(new ProfileChoice { Id = string.Empty, DisplayName = "DHCP (automatique)", IsDhcp = true });
            foreach (var profile in _profiles)
            {
                row.AvailableChoices.Add(new ProfileChoice { Id = profile.Id, DisplayName = profile.Name });
            }

            if (adapter.AssignedDhcp)
            {
                row.SelectedChoice = row.AvailableChoices.FirstOrDefault(c => c.IsDhcp);
            }
            else if (adapter.ActiveProfileId != null)
            {
                row.SelectedChoice = row.AvailableChoices.FirstOrDefault(c => c.Id == adapter.ActiveProfileId);
            }
            _adapterRows.Add(row);
        }
        SetStatus($"{adapters.Count} carte(s) réseau détectée(s), {_profiles.Count} profil(s) enregistré(s).");
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not AdapterRow row || row.SelectedChoice == null)
        {
            return;
        }

        SetStatus("Application en cours…");
        PipeResponse response;
        if (row.SelectedChoice.IsDhcp)
        {
            response = await PipeClient.SendAsync(Commands.SetDhcp, new SetDhcpPayload { InterfaceAlias = row.InterfaceAlias });
        }
        else
        {
            response = await PipeClient.SendAsync(Commands.ApplyProfile, new ApplyProfilePayload { InterfaceAlias = row.InterfaceAlias, ProfileId = row.SelectedChoice.Id });
        }

        if (response.Success)
        {
            SetStatus(response.Message ?? "OK.");
            await RefreshAsync();
            return;
        }

        if (response.ErrorCode == ErrorCodes.ProfileInUse)
        {
            var conflict = PipeClient.DeserializeData<ConflictInfo>(response);
            if (conflict != null && await ResolveConflictAsync(conflict, row.InterfaceAlias))
            {
                await RefreshAsync();
                return;
            }
            SetStatus(response.Message ?? "Profil déjà utilisé ailleurs.");
            await RefreshAsync();
            return;
        }

        SetStatus(response.Message ?? "Échec de l'application.");
        await RefreshAsync();
    }

    private async Task<bool> ResolveConflictAsync(ConflictInfo conflict, string targetAdapter)
    {
        var dialog = new ConflictDialog(conflict, _profiles) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedChoice == null)
        {
            return false;
        }

        PipeResponse replacement;
        if (dialog.SelectedChoice.IsDhcp)
        {
            replacement = await PipeClient.SendAsync(Commands.SetDhcp, new SetDhcpPayload { InterfaceAlias = conflict.AdapterAlias });
        }
        else
        {
            replacement = await PipeClient.SendAsync(Commands.ApplyProfile, new ApplyProfilePayload { InterfaceAlias = conflict.AdapterAlias, ProfileId = dialog.SelectedChoice.Id });
        }

        if (!replacement.Success)
        {
            MessageBox.Show(this, replacement.Message ?? "Impossible de modifier l'autre carte.", "Conflit non résolu", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var retry = await PipeClient.SendAsync(Commands.ApplyProfile, new ApplyProfilePayload { InterfaceAlias = targetAdapter, ProfileId = conflict.ProfileId });
        if (retry.Success)
        {
            SetStatus(retry.Message ?? "OK.");
            return true;
        }
        MessageBox.Show(this, retry.Message ?? "Échec après résolution du conflit.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ProfileEditorWindow { Owner = this };
        if (editor.ShowDialog() == true && editor.Profile != null)
        {
            var response = await PipeClient.SendAsync(Commands.SaveProfile, editor.Profile);
            SetStatus(response.Message ?? (response.Success ? "Enregistré." : "Échec."));
            await RefreshAsync();
        }
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not NetworkProfile selected)
        {
            MessageBox.Show(this, "Sélectionnez d'abord un profil dans la liste.", "Modifier", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new ProfileEditorWindow(selected) { Owner = this };
        if (editor.ShowDialog() == true && editor.Profile != null)
        {
            var response = await PipeClient.SendAsync(Commands.SaveProfile, editor.Profile);
            SetStatus(response.Message ?? (response.Success ? "Enregistré." : "Échec."));
            await RefreshAsync();
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not NetworkProfile selected)
        {
            MessageBox.Show(this, "Sélectionnez d'abord un profil dans la liste.", "Supprimer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Supprimer le profil « {selected.Name} » ?", "Confirmer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var response = await PipeClient.SendAsync(Commands.DeleteProfile, new DeleteProfilePayload { Id = selected.Id });
        SetStatus(response.Message ?? (response.Success ? "Supprimé." : "Échec."));
        await RefreshAsync();
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
