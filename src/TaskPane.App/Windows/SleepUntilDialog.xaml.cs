using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskPane.App.Windows;

public partial class SleepUntilDialog : Window
{
    private readonly SleepUntilDialogModel _model;

    public SleepUntilDialog(DateTimeOffset? suggestedLocal = null)
    {
        InitializeComponent();
        var local = suggestedLocal ?? DateTimeOffset.Now.AddHours(2);
        _model = new SleepUntilDialogModel
        {
            DateText = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeText = local.ToString("HH:mm", CultureInfo.InvariantCulture)
        };
        DataContext = _model;
    }

    public DateTimeOffset? SelectedUntilUtc { get; private set; }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_model.TryParse(out var untilUtc, out var error))
        {
            _model.ErrorMessage = error;
            return;
        }

        SelectedUntilUtc = untilUtc;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public partial class SleepUntilDialogModel : ObservableObject
{
    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    public bool TryParse(out DateTimeOffset untilUtc, out string error)
    {
        untilUtc = default;
        error = string.Empty;

        if (!DateOnly.TryParseExact(
                DateText.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            error = "Enter a valid date as YYYY-MM-DD.";
            return false;
        }

        if (!TimeOnly.TryParseExact(
                TimeText.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time)
            && !TimeOnly.TryParseExact(
                TimeText.Trim(),
                "H:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time))
        {
            error = "Enter a valid 24-hour time as HH:mm.";
            return false;
        }

        var local = date.ToDateTime(time);
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        untilUtc = new DateTimeOffset(local, offset).ToUniversalTime();
        if (untilUtc <= DateTimeOffset.UtcNow)
        {
            error = "Wake time must be in the future.";
            return false;
        }

        return true;
    }
}
