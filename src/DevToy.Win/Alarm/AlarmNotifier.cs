using System.Diagnostics;
using System.Media;

namespace DevToy;

static class AlarmNotifier
{
    private static Form? _marshalForm;
    private static NotifyIcon? _trayIcon;

    public static void Initialize(Form marshalForm, NotifyIcon trayIcon)
    {
        _marshalForm = marshalForm;
        _trayIcon = trayIcon;
    }

    public static void HandleAlarmTriggered(AlarmEntry alarm)
    {
        if (_marshalForm == null || _marshalForm.IsDisposed) return;

        try
        {
            _marshalForm.Invoke(() => ShowAlarm(alarm));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AlarmNotifier dispatch failed: {ex.Message}");
        }
    }

    private static void ShowAlarm(AlarmEntry alarm)
    {
        try
        {
            // Play sound first
            if (alarm.SoundEnabled && AppSettings.Load().AlarmSoundEnabled)
            {
                try
                {
                    SystemSounds.Exclamation.Play();
                    AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
                    {
                        AlarmId = alarm.Id,
                        AlarmTitle = alarm.Title,
                        EventType = AlarmHistoryEventType.SoundPlayed,
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Sound play failed: {ex.Message}");
                }
            }

            // Show popup
            if (alarm.Notification is AlarmNotificationMode.Popup or AlarmNotificationMode.Both)
            {
                var ringForm = new AlarmRingForm(alarm, Themes.LoadSaved());
                ringForm.Dismissed += () =>
                {
                    AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
                    {
                        AlarmId = alarm.Id,
                        AlarmTitle = alarm.Title,
                        EventType = AlarmHistoryEventType.Dismissed,
                    });
                };
                ringForm.Snoozed += minutes =>
                {
                    AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
                    {
                        AlarmId = alarm.Id,
                        AlarmTitle = alarm.Title,
                        EventType = AlarmHistoryEventType.Snoozed,
                        Detail = $"Snoozed for {minutes} minutes",
                    });

                    // Schedule re-trigger after snooze
                    var snoozeTimer = new System.Threading.Timer(_ =>
                    {
                        _marshalForm?.Invoke(() => ShowAlarm(alarm));
                    }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
                };
                ringForm.Show();

                AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
                {
                    AlarmId = alarm.Id,
                    AlarmTitle = alarm.Title,
                    EventType = AlarmHistoryEventType.PopupShown,
                });
            }

            // Show Windows notification
            if (alarm.Notification is AlarmNotificationMode.Windows or AlarmNotificationMode.Both)
            {
                if (_trayIcon != null)
                {
                    string message = alarm.Message.Length > 200 ? alarm.Message[..197] + "..." : alarm.Message;
                    if (string.IsNullOrEmpty(message)) message = alarm.GetScheduleDescription();
                    _trayIcon.ShowBalloonTip(5000, alarm.Title, message, ToolTipIcon.Info);

                    AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
                    {
                        AlarmId = alarm.Id,
                        AlarmTitle = alarm.Title,
                        EventType = AlarmHistoryEventType.NotificationShown,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AlarmNotifier.ShowAlarm failed: {ex.Message}");
            AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
            {
                AlarmId = alarm.Id,
                AlarmTitle = alarm.Title,
                EventType = AlarmHistoryEventType.TriggerFailed,
                Detail = ex.Message,
            });
        }
    }
}
