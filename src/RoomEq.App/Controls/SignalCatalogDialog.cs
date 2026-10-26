using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RoomEq.Core.Io;

namespace RoomEq.App.Controls;

/// <summary>Élément affiché dans le catalogue des signaux téléchargeables.</summary>
public partial class SignalDownloadItem : ObservableObject
{
    public DownloadableSignalItem Source { get; }

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string statusText;

    public string DisplayName => Source.DisplayName;
    public string Description => string.IsNullOrEmpty(Source.Description) ? "" : Source.Description;
    public string SizeText => Source.Bytes >= 1_000_000
        ? $"{Source.Bytes / 1_000_000.0:0.#} Mo"
        : $"{Math.Max(1, Source.Bytes / 1000)} ko";

    public SignalDownloadItem(DownloadableSignalItem source, bool alreadyDownloaded)
    {
        Source = source;
        isSelected = !alreadyDownloaded;
        statusText = alreadyDownloaded ? "Déjà téléchargé." : "";
    }
}

/// <summary>
/// Catalogue des signaux de test supplémentaires : liste ce qui est publié avec
/// le projet (fichiers WAV hébergés sur le dépôt GitHub officiel), permet de
/// télécharger la sélection dans la bibliothèque locale
/// (%APPDATA%\RoomEq\signaux) et de la retrouver ensuite dans le menu déroulant
/// de la barre supérieure. Téléchargements vérifiés (taille + SHA-256).
/// </summary>
public static class SignalCatalogDialog
{
    public static void Show(Control owner, Action onChanged)
    {
        var window = new Window
        {
            Title = "Signaux de test supplémentaires",
            Width = 620,
            Height = 480,
            MinWidth = 520,
            MinHeight = 380,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var items = new ObservableCollection<SignalDownloadItem>();
        var store = new SignalStore();
        var client = new SignalCatalogClient();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto") };

        // ---------------------------------------------------------------- entête
        var intro = new TextBlock
        {
            Margin = new Thickness(16, 14, 16, 12),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#B8BFCC")),
            FontSize = 12.5,
            Text = "Ces signaux sont des fichiers audio publiés avec le projet (dépôt GitHub officiel). " +
                   "Téléchargez-les une fois : ils apparaissent ensuite dans le menu déroulant des signaux de test, " +
                   "à côté des bruits et sinus générés par l'application."
        };
        root.Children.Add(intro);

        // ---------------------------------------------------------------- liste
        var list = new ListBox { Margin = new Thickness(16, 0, 16, 8) };
        list.ItemsSource = items;
        root.Children.Add(list);
        Grid.SetRow(list, 1);

        // progression
        var progressBar = new ProgressBar { Height = 6, Margin = new Thickness(16, 6, 16, 2), IsIndeterminate = false, Minimum = 0, Maximum = 1 };
        var statusText = new TextBlock
        {
            Margin = new Thickness(16, 2, 16, 8),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#8B93A7")),
            Text = "Chargement du catalogue…"
        };
        Grid.SetRow(progressBar, 2);
        Grid.SetRow(statusText, 3);

        // ---------------------------------------------------------------- bas
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16, 8, 16, 14)
        };
        var selectAll = new Button { Content = "Tout cocher" };
        var selectNone = new Button { Content = "Tout décocher" };
        var download = new Button
        {
            Content = "Télécharger la sélection",
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#FF9F1C")),
            Foreground = Brushes.Black
        };
        var close = new Button { Content = "Fermer", MinWidth = 90 };
        buttons.Children.Add(selectAll);
        buttons.Children.Add(selectNone);
        buttons.Children.Add(download);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        window.Content = root;

        // ---------------------------------------------------------------- logique
        void SetStatus(string text) => statusText.Text = text;

        selectAll.Click += (_, _) => { foreach (var i in items) i.IsSelected = true; };
        selectNone.Click += (_, _) => { foreach (var i in items) i.IsSelected = false; };
        close.Click += (_, _) => window.Close();

        async void LoadCatalog()
        {
            SetStatus("Chargement du catalogue…");
            var (signals, error) = await client.FetchManifestAsync();
            if (error != null)
            {
                SetStatus("Catalogue indisponible : " + error + " — vérifiez la connexion Internet puis rouvrez cette fenêtre.");
                return;
            }
            items.Clear();
            foreach (var s in signals!.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
                items.Add(new SignalDownloadItem(s, store.Contains(s.FileName, s.Bytes)));
            SetStatus($"{items.Count} signal(s) publié(s). Cochez ceux que vous voulez ajouter, puis « Télécharger la sélection ».");
        }

        async void RunDownloads()
        {
            var selection = items.Where(i => i.IsSelected).ToList();
            if (selection.Count == 0)
            {
                SetStatus("Aucun signal coché.");
                return;
            }
            download.IsEnabled = false;
            int ok = 0;
            for (int n = 0; n < selection.Count; n++)
            {
                var item = selection[n];
                item.StatusText = "Téléchargement…";
                SetStatus($"Téléchargement {n + 1}/{selection.Count} : {item.DisplayName}…");
                var (content, error) = await client.DownloadFileAsync(item.Source,
                    new Progress<double>(p => progressBar.Value = p));
                if (content == null)
                {
                    item.StatusText = "Échec : " + error;
                    continue;
                }
                try
                {
                    store.Save(item.Source.FileName, content, item.Source.Sha256, item.Source.DisplayName);
                    item.StatusText = "Téléchargé.";
                    ok++;
                }
                catch (Exception ex)
                {
                    item.StatusText = "Rejeté : " + ex.Message;
                }
            }
            progressBar.Value = 0;
            download.IsEnabled = true;
            SetStatus(ok == selection.Count
                ? $"Terminé : {ok} signal(s) ajouté(s) au menu déroulant."
                : $"Terminé avec {selection.Count - ok} échec(s) sur {selection.Count}.");
            if (ok > 0) onChanged();
        }

        download.Click += (_, _) => RunDownloads();

        window.KeyDown += (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Escape) window.Close();
        };

        window.Opened += (_, _) => LoadCatalog();

        var parent = TopLevel.GetTopLevel(owner) as Window;
        if (parent != null && parent.IsVisible)
            window.ShowDialog(parent);
        else
            window.Show();
    }
}
