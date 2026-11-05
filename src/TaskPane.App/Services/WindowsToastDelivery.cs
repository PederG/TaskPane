using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TaskPane.Core.Interfaces;
using TaskPane.Core.Reminders;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace TaskPane.App.Services;

/// <summary>
/// Win11 toast delivery for unpackaged self-contained WPF (no admin, no cloud identity).
/// Falls back to caller when toast APIs or AUMID registration are unavailable.
/// </summary>
internal static class WindowsToastDelivery
{
    public const string Aumid = "TaskPane.Desktop";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    public static bool TryShow(
        ReminderNotification notification,
        Action<string> onActivatedArguments,
        ILogger logger)
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(Aumid);

            var xml = ReminderToastContent.BuildXml(
                notification.AppDisplayName,
                notification.Title,
                notification.ReminderId,
                notification.TaskId);

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var toast = new ToastNotification(doc)
            {
                Tag = TruncateTag(notification.DeliveryKey),
                Group = "TaskPane.Reminders"
            };

            toast.Activated += (_, args) =>
            {
                try
                {
                    var arguments = args is ToastActivatedEventArgs toastArgs
                        ? toastArgs.Arguments
                        : ReminderToastContent.BuildArguments(
                            "open",
                            notification.ReminderId.Value.ToString("D"),
                            notification.TaskId.Value.ToString("D"));
                    onActivatedArguments(arguments ?? string.Empty);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Toast activation handler failed");
                }
            };

            ToastNotificationManager.CreateToastNotifier(Aumid).Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Windows toast unavailable; using in-app reminder fallback");
            return false;
        }
    }

    private static string TruncateTag(string deliveryKey)
    {
        // Toast Tag max length is 64 characters.
        if (string.IsNullOrEmpty(deliveryKey))
        {
            return "reminder";
        }

        return deliveryKey.Length <= 64 ? deliveryKey : deliveryKey[..64];
    }
}
