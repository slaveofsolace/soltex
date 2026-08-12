using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soltex.App.Views;

public partial class ApplicationsView : UserControl
{
    private ApplicationInventorySnapshot? _snapshot;
    private WindowsServiceInventorySnapshot? _serviceSnapshot;
    private InventoryTab _activeTab;

    public ApplicationsView()
    {
        InitializeComponent();
        SetTab(InventoryTab.Installed);
    }

    internal event EventHandler? RefreshRequested;

    internal void ShowLoading()
    {
        RefreshInventoryButton.IsEnabled = false;
        InventoryStateDot.Fill = (Brush)FindResource("WarningBrush");
        InventoryStateText.Foreground = (Brush)FindResource("WarningBrush");
        InventoryStateText.Text = "READING";
        InventorySummaryText.Text = "Reading software, sign-in entries, and Windows services…";
        InventoryProvenanceText.Text = "Reading bounded, supported Windows inventory sources…";
    }

    internal void UpdateSnapshot(
        ApplicationInventorySnapshot snapshot,
        WindowsServiceInventorySnapshot serviceSnapshot)
    {
        _snapshot = snapshot;
        _serviceSnapshot = serviceSnapshot;
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
            "{0:N0} installed · {1:N0} publishers · {2:N0} sign-in · {3:N0} services",
            snapshot.Installed.Count,
            publisherCount,
            snapshot.Startup.Count,
            serviceSnapshot.Services.Count);

        bool complete =
            snapshot.InaccessibleSourceCount == 0 &&
            serviceSnapshot.State == ServiceInventoryState.Current;
        Brush stateBrush = (Brush)FindResource(complete ? "SignalBrush" : "WarningBrush");
        InventoryStateDot.Fill = stateBrush;
        InventoryStateText.Foreground = stateBrush;
        InventoryStateText.Text = complete ? "CURRENT" : "PARTIAL";
        InventoryProvenanceText.Text =
            $"Updated {snapshot.CapturedAtUtc.ToLocalTime():t} · apps {snapshot.CaptureDuration.TotalMilliseconds:F0} ms · services {serviceSnapshot.CaptureDuration.TotalMilliseconds:F0} ms · supported Windows sources";
        ServiceAttentionColumn.Visibility = serviceSnapshot.Services.Any(item =>
            !string.IsNullOrWhiteSpace(item.Signal))
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter();
    }

    internal void ShowUnavailable()
    {
        _snapshot = null;
        _serviceSnapshot = null;
        InstalledGrid.ItemsSource = Array.Empty<InstalledRow>();
        StartupGrid.ItemsSource = Array.Empty<StartupRow>();
        ServicesGrid.ItemsSource = Array.Empty<ServiceRow>();
        ServiceAttentionColumn.Visibility = Visibility.Collapsed;
        InventorySummaryText.Text = "System inventory is unavailable.";
        RefreshInventoryButton.IsEnabled = true;
        InventoryStateDot.Fill = (Brush)FindResource("DangerBrush");
        InventoryStateText.Foreground = (Brush)FindResource("DangerBrush");
        InventoryStateText.Text = "UNAVAILABLE";
        InventoryCoverageText.Text = "READ FAILED";
        InventoryProvenanceText.Text =
            "Windows did not expose a bounded inventory. No values were synthesized.";
        InventoryEmptyText.Text = "System inventory could not be read.";
        InventoryEmptyText.Visibility = Visibility.Visible;
    }

    internal void ShowServicesForEvidence() => SetTab(InventoryTab.Services);

    private void InstalledTab_Click(object sender, RoutedEventArgs e) =>
        SetTab(InventoryTab.Installed);

    private void StartupTab_Click(object sender, RoutedEventArgs e) =>
        SetTab(InventoryTab.Startup);

    private void ServicesTab_Click(object sender, RoutedEventArgs e) =>
        SetTab(InventoryTab.Services);

    private void InventorySearch_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyFilter();

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void SetTab(InventoryTab tab)
    {
        _activeTab = tab;
        InstalledGrid.Visibility = tab == InventoryTab.Installed ? Visibility.Visible : Visibility.Collapsed;
        StartupGrid.Visibility = tab == InventoryTab.Startup ? Visibility.Visible : Visibility.Collapsed;
        ServicesGrid.Visibility = tab == InventoryTab.Services ? Visibility.Visible : Visibility.Collapsed;
        SetSelected(InstalledTabButton, tab == InventoryTab.Installed);
        SetSelected(StartupTabButton, tab == InventoryTab.Startup);
        SetSelected(ServicesTabButton, tab == InventoryTab.Services);
        InventoryEmptyText.Text = tab switch
        {
            InventoryTab.Startup => "No startup entries match your search.",
            InventoryTab.Services when _serviceSnapshot?.State == ServiceInventoryState.Unavailable =>
                "Windows service inventory is unavailable.",
            InventoryTab.Services => "No Windows services match your search.",
            _ => "No installed applications match your search."
        };
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = InventorySearchBox.Text.Trim();
        SearchHintText.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_snapshot is null || _serviceSnapshot is null)
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
        ServiceRow[] services = _serviceSnapshot.Services
            .Where(item =>
                query.Length == 0 ||
                item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Status.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.StartMode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Signal.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => new ServiceRow(item))
            .ToArray();
        InstalledGrid.ItemsSource = installed;
        StartupGrid.ItemsSource = startup;
        ServicesGrid.ItemsSource = services;
        int visibleCount = _activeTab switch
        {
            InventoryTab.Startup => startup.Length,
            InventoryTab.Services => services.Length,
            _ => installed.Length
        };
        InventoryEmptyText.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool complete = _activeTab == InventoryTab.Services
            ? _serviceSnapshot.State == ServiceInventoryState.Current
            : _snapshot.InaccessibleSourceCount == 0;
        InventoryCoverageText.Text = complete
            ? $"READ ONLY · {visibleCount:N0} SHOWN"
            : $"PARTIAL · READ ONLY · {visibleCount:N0} SHOWN";
    }

    private void SetSelected(Button button, bool selected)
    {
        button.Background = (Brush)FindResource(selected ? "SelectedNavBrush" : "NavRestBrush");
        button.Foreground = (Brush)FindResource(selected ? "AccentBrush" : "MutedBrush");
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

    private sealed class ServiceRow(WindowsServiceObservation item)
    {
        public string DisplayName => item.DisplayName;

        public string Status => item.Status;

        public string StartMode => item.StartMode;

        public string Signal => item.Signal;
    }

    private enum InventoryTab
    {
        Installed,
        Startup,
        Services
    }
}
