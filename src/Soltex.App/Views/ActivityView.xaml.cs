using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soltex.App.Views;

internal sealed record ActivityRow(
    string Area,
    string Summary,
    string When);

public partial class ActivityView : UserControl
{
    private readonly ObservableCollection<ActivityRow> _visibleRows = [];
    private ActivityEntry[] _entries = [];

    public ActivityView()
    {
        InitializeComponent();
        ActivityItems.ItemsSource = _visibleRows;
        ApplyFilter();
    }

    internal event EventHandler? ClearRequested;

    internal void UpdateEntries(
        IReadOnlyList<ActivityEntry> entries,
        ActivityRetention retention,
        string detail,
        bool storageHealthy)
    {
        _entries = entries
            .OrderByDescending(static entry => entry.OccurredAtUtc)
            .ToArray();
        ActivityStorageDetail.Text = detail;
        SetRetention(retention, storageHealthy);
        ApplyFilter();
    }

    private void ActivitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ActivitySearchHint.Visibility =
            string.IsNullOrEmpty(ActivitySearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        ApplyFilter();
    }

    private void ClearActivity_Click(object sender, RoutedEventArgs e) =>
        ClearRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyFilter()
    {
        string query = ActivitySearchBox?.Text.Trim() ?? string.Empty;
        IEnumerable<ActivityEntry> filtered = _entries;
        if (query.Length > 0)
        {
            filtered = filtered.Where(entry =>
                entry.Area.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        _visibleRows.Clear();
        foreach (ActivityEntry entry in filtered)
        {
            _visibleRows.Add(new ActivityRow(
                entry.Area,
                entry.Summary,
                FormatWhen(entry.OccurredAtUtc)));
        }

        bool empty = _visibleRows.Count == 0;
        ActivityItems.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ActivityEmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ActivityEmptyTitle.Text =
            query.Length > 0 ? "No matching activity" : "No activity yet";
        ActivityEmptyDetail.Text =
            query.Length > 0
                ? "Try a broader area or summary."
                : "Meaningful actions and recovery events will appear here.";
        ClearActivityButton.IsEnabled = _entries.Length > 0;
        ActivityCountText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} OF {1} EVENTS",
            _visibleRows.Count,
            _entries.Length);
    }

    private void SetRetention(ActivityRetention retention, bool storageHealthy)
    {
        string label;
        string brushResource;
        if (!storageHealthy)
        {
            label = "CHECK";
            brushResource = "WarningBrush";
        }
        else
        {
            switch (retention)
            {
                case ActivityRetention.ThirtyDays:
                    label = "30 DAYS";
                    brushResource = "SignalBrush";
                    break;
                case ActivityRetention.SevenDays:
                    label = "7 DAYS";
                    brushResource = "SignalBrush";
                    break;
                default:
                    label = "SESSION";
                    brushResource = "MutedBrush";
                    break;
            }
        }

        Brush brush = (Brush)FindResource(brushResource);
        ActivityStateDot.Fill = brush;
        ActivityRetentionText.Foreground = brush;
        ActivityRetentionText.Text = label;
    }

    private static string FormatWhen(DateTimeOffset timestamp)
    {
        DateTime local = timestamp.ToLocalTime().DateTime;
        return local.Date == DateTime.Today
            ? string.Concat("TODAY · ", local.ToString("t", CultureInfo.CurrentCulture))
            : string.Concat(
                local.ToString("MMM d", CultureInfo.CurrentCulture),
                " · ",
                local.ToString("t", CultureInfo.CurrentCulture)).ToUpperInvariant();
    }
}
