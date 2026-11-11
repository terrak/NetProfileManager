using System.Windows;
using NetProfileManager.Core.Models;
using NetProfileManager.Core.Protocol;

namespace NetProfileManager.App;

public partial class ConflictDialog : Window
{
    public ProfileChoice? SelectedChoice { get; private set; }

    public ConflictDialog(ConflictInfo conflict, List<NetworkProfile> profiles)
    {
        InitializeComponent();

        MessageText.Text = $"Le profil « {conflict.ProfileName} » est déjà actif sur la carte « {conflict.AdapterAlias} ». " +
                           "Un profil ne peut être actif que sur une seule carte (le DHCP peut être utilisé partout). " +
                           $"Choisissez le réglage à appliquer sur « {conflict.AdapterAlias} » :";

        var choices = new List<ProfileChoice>
        {
            new() { Id = string.Empty, DisplayName = "DHCP (automatique)", IsDhcp = true }
        };
        choices.AddRange(profiles
            .Where(p => p.Id != conflict.ProfileId)
            .Select(p => new ProfileChoice { Id = p.Id, DisplayName = p.Name }));

        ChoiceCombo.ItemsSource = choices;
        ChoiceCombo.SelectedIndex = 0;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedChoice = ChoiceCombo.SelectedItem as ProfileChoice;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
