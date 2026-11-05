using System.Windows;
using TaskPane.Core.Enums;
using TaskPane.Core.Interfaces;

namespace TaskPane.App.Windows;

/// <summary>
/// In-app reminder surface used when Win11 toasts are unavailable.
/// Shows title only — never task notes.
/// </summary>
public partial class ReminderActionWindow : Window
{
    private readonly ReminderNotification _notification;
    private readonly Action<ReminderNotificationAction, ReminderNotification> _onAction;
    private bool _completed;

    public ReminderActionWindow(
        ReminderNotification notification,
        Action<ReminderNotificationAction, ReminderNotification> onAction)
    {
        _notification = notification;
        _onAction = onAction;
        InitializeComponent();
        Title = string.IsNullOrWhiteSpace(notification.AppDisplayName)
            ? "Reminder"
            : notification.AppDisplayName;
        BrandText.Text = notification.AppDisplayName;
        TitleText.Text = notification.Title;
        Closed += (_, _) =>
        {
            // Closing without a button leaves the reminder marked delivered (no spam).
        };
    }

    private void Open_Click(object sender, RoutedEventArgs e) => Complete(ReminderNotificationAction.OpenTask);

    private void Snooze_Click(object sender, RoutedEventArgs e) => Complete(ReminderNotificationAction.Snooze);

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Complete(ReminderNotificationAction.Dismiss);

    private void Complete(ReminderNotificationAction action)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _onAction(action, _notification);
        Close();
    }
}
