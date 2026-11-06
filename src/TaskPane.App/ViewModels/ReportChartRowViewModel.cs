using CommunityToolkit.Mvvm.ComponentModel;
using TaskPane.Application.Models;
using TaskPane.Core.Ids;
using TaskPane.Core.Time;

namespace TaskPane.App.ViewModels;

/// <summary>One horizontal bar in the Reports pane (today / week activity).</summary>
public partial class ReportChartRowViewModel : ObservableObject
{
    public required TaskId TaskId { get; init; }

    public required string Title { get; init; }

    public required string DurationText { get; init; }

    /// <summary>0–1 share of the report total (drives ProgressBar).</summary>
    public required double Share { get; init; }

    public static IReadOnlyList<ReportChartRowViewModel> FromReport(TimeReport report, int take = 12)
    {
        if (report.Rows.Count == 0 || report.Total <= TimeSpan.Zero)
        {
            return [];
        }

        var totalTicks = report.Total.Ticks;
        return report.Rows
            .Take(take)
            .Select(row => new ReportChartRowViewModel
            {
                TaskId = row.TaskId,
                Title = row.Title,
                DurationText = SessionDuration.FormatCompact(row.Direct),
                Share = Math.Clamp(row.Direct.Ticks / (double)totalTicks, 0, 1)
            })
            .ToList();
    }
}
