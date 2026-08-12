using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soltex.App.Views;

public partial class ApplicationsView : UserControl
{
    private ApplicationInventorySnapshot? _snapshot;
    private bool _showStartup;

    public ApplicationsView()
    {
        InitializeComponent();
        SetTab(showStartup: false);
    }

    internal event EventHandler? RefreshRequested;

    internal void ShowLoading()
    {
        RefreshInventoryButton.IsEnabled = false;
        InventoryStateDot.Fill = (Brush)FindResource("WarningBrush");
        InventoryStateText.Foreground = (Brush)FindResource("WarningBrush");
        InventoryStateText.Text = "READING";
        InventorySummaryText.Text = "Reading installed software and sign-in entries…";
        InventoryProvenanceText.Text = "Reading supported Windows application sources…";
    }

    internal void UpdateSnapshot(ApplicationInventorySnapshot snapshot)
    {
        _snapshot = snapshot;
        RefreshInventoryButton.IsEnabled = true;
        int publisherCount = snapshot.Installed
            .Select(item => item.Publisher)
            .Where(publisher => !string.Equals(
                publisher,
                "Unknown publisher",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        InventorySummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} installed · {1:N0} publishers · {2:N0} sign-in entries",
            snapshot.Installed.Count,
            publisherCount,
            snapshot.Startup.Count);

        bool complete = snapshot.InaccessibleSourceCount == 0;
        Brush stateBrush = (Brush)FindResource(complete ? "SignalBrush" : "WarningBrush");
        InventoryStateDot.Fill = stateBrush;
        InventoryStateText.Foreground = stateBrush;
        InventoryStateText.Text = complete ? "CURRENT" : "PARTIAL";
        InventoryProvenanceText.Text =
            $"Updated {snapshot.CapturedAtUtc.ToLocalTime():t} · {snapshot.CaptureDuration.TotalMilliseconds:F0} ms · {snapshot.Provenance}";
        ApplyFilter();
    }

    internal void ShowUnavailable()
    {
        _snapshot = null;
        InstalledGrid.ItemsSource = Array.Empty<InstalledRow>();
        StartupGrid.ItemsSource = Array.Empty<StartupRow>();
        InventorySummaryText.Text = "Application inventory is unavailable.";
        RefreshInventoryButton.IsEnabled = true;
        InventoryStateDot.Fill = (Brush)FindResource("DangerBrush");
        InventoryStateText.Foreground = (Brush)FindResource("DangerBrush");
        InventoryStateText.Text = "UNAVAILABLE";
        InventoryCoverageText.Text = "READ FAILED";
        InventoryProvenanceText.Text =
            "Windows did not expose a bounded application inventory. No values were synthesized.";
        InventoryEmptyText.Text = "Application inventory could not be read.";
        InventoryEmptyText.Visibility = Visibility.Visible;
    }

    private void InstalledTab_Click(object sender, RoutedEventArgs e) =>
        SetTab(showStartup: false);

    private void StartupTab_Click(object sender, RoutedEventArgs e) =>
        SetTab(showStartup: true);

    private void InventorySearch_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyFilter();

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void SetTab(bool showStartup)
    {
        _showStartup = showStartup;
        InstalledGrid.Visibility = showStartup ? Visibility.Collapsed : Visibility.Visible;
        StartupGrid.Visibility = showStartup ? Visibility.Visible : Visibility.Collapsed;
        InstalledTabButton.Background = (Brush)FindResource(showStartup ? "NavRestBrush" : "SelectedNavBrush");
        InstalledTabButton.Foreground = (Brush)FindResource(showStartup ? "MutedBrush" : "AccentBrush");
        StartupTabButton.Background = (Brush)FindResource(showStartup ? "SelectedNavBrush" : "NavRestBrush");
        StartupTabButton.Foreground = (Brush)FindResource(showStartup ? "AccentBrush" : "MutedBrush");
        InventoryEmptyText.Text = showStartup
            ? "No startup entries match your search."
            : "No installed applications match your search.";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = InventorySearchBox.Text.Trim();
        SearchHintText.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_snapshot is null)
        {
            InventoryEmptyText.Visibility = Visibility.Visible;
            return;
        }

        InstalledRow[] installed = _snapshot.Installed
            .Where(item =>
                query.Length == 0 ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Version.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Scope.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => new InstalledRow(item))
            .ToArray();
        StartupRow[] startup = _snapshot.Startup
            .Where(item =>
                query.Length == 0 ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Scope.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Mode.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => new StartupRow(item))
            .ToArray();
        InstalledGrid.ItemsSource = installed;
        StartupGrid.ItemsSource = startup;
        int visibleCount = _showStartup ? startup.Length : installed.Length;
        InventoryEmptyText.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        InventoryCoverageText.Text = _snapshot.InaccessibleSourceCount == 0
            ? $"READ ONLY · {visibleCount:N0} SHOWN"
            : $"PARTIAL · READ ONLY · {visibleCount:N0} SHOWN";
    }

    private sealed class InstalledRow(InstalledApplicationObservation item)
    {
        public string Name => item.Name;

        public string Publisher => item.Publisher;

        public string Version => item.Version;

        public string Scope => item.Scope;
    }

    private sealed class StartupRow(StartupApplicationObservation item)
    {
        public string Name => item.Name;

        public string Scope => item.Scope;

        public string Source => item.Source;

        public string Mode => item.Mode;
    }
}
