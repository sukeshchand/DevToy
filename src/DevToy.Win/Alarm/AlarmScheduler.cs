using System.Diagnostics;

namespace DevToy;

static class AlarmScheduler
{
    private static System.Threading.Timer? _timer;
    private static readonly HashSet<string> _firedKeys = new();
    private static readonly object _lock = new();

    public static event Action<AlarmEntry>? AlarmTriggered;

    public static void Start()
    {
        Stop();
        // Initial delay 5 seconds, then tick every 30 seconds
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        Debug.WriteLine("AlarmScheduler started");
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_lock) { _firedKeys.Clear(); }
        Debug.WriteLine("AlarmScheduler stopped");
    }

    public static void Refresh()
    {
        AlarmStore.Invalidate();
    }

    public static void TestTrigger(AlarmEntry alarm)
    {
        AlarmTriggered?.Invoke(alarm);
    }

    private static void Tick()
    {
        try
        {
            var now = DateTime.Now;
            var alarms = AlarmStore.LoadAlarms();

            foreach (var alarm in alarms)
            {
                if (alarm.Status != AlarmStatus.Active) continue;

                if (ShouldFire(alarm, now))
                {
                    var key = $"{alarm.Id}|{now:yyyy-MM-dd HH:mm}";
                    bool isNew;
                    lock (_lock) { isNew = _firedKeys.Add(key); }

                    if (isNew)
                    {
                        try
                        {
                            AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
                            {
                                AlarmId = alarm.Id,
                                AlarmTitle = alarm.Title,
                                EventType = AlarmHistoryEventType.Triggered,
                                Detail = $"Scheduled: {alarm.Schedule.TimeOfDay}, Actual: {now:HH:mm:ss}",
                            });

                            AlarmStore.RecordTrigger(alarm.Id);
                            AlarmTriggered?.Invoke(alarm);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Alarm trigger failed for {alarm.Id}: {ex.Message}");
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
            }

            // Check for missed alarms on startup (grace period)
            CheckMissedAlarms(now, alarms);

            // Prune old fired keys (older than 24h)
            PruneFiredKeys(now);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AlarmScheduler tick error: {ex.Message}");
        }
    }

    private static bool ShouldFire(AlarmEntry alarm, DateTime now)
    {
        var time = alarm.Schedule.GetTimeOfDay();
        var nowTime = now.TimeOfDay;

        // Must match within same minute
        bool timeMatch = nowTime.Hours == time.Hours && nowTime.Minutes == time.Minutes;

        // For interval-based, check differently
        if (alarm.Schedule.Type == AlarmScheduleType.Interval)
        {
            if (alarm.Schedule.IntervalMinutes is int mins and > 0)
            {
                if (alarm.LastTriggeredAt is DateTime last)
                    return (now - last).TotalMinutes >= mins;
                return true; // First trigger
            }
            return false;
        }

        if (!timeMatch) return false;

        // Check end date
        if (alarm.EndDate != null && DateTime.TryParse(alarm.EndDate, out var end) && now.Date > end.Date)
            return false;

        return alarm.Schedule.Type switch
        {
            AlarmScheduleType.Once => alarm.Schedule.OneTimeDate != null
                && DateTime.TryParse(alarm.Schedule.OneTimeDate, out var d)
                && d.Date == now.Date,
            AlarmScheduleType.Daily => true,
            AlarmScheduleType.Weekdays => now.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
            AlarmScheduleType.Weekend => now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            AlarmScheduleType.Weekly => alarm.Schedule.CustomDays is { Length: > 0 } days && now.DayOfWeek == days[0],
            AlarmScheduleType.Monthly => alarm.Schedule.DayOfMonth is int dom && now.Day == dom,
            AlarmScheduleType.Custom => alarm.Schedule.CustomDays is { Length: > 0 } days && days.Contains(now.DayOfWeek),
            _ => false,
        };
    }

    private static bool _missedCheckDone;

    private static void CheckMissedAlarms(DateTime now, List<AlarmEntry> alarms)
    {
        if (_missedCheckDone) return;
        _missedCheckDone = true;

        int graceMinutes = AppSettings.Load().AlarmMissedGraceMinutes;
        if (graceMinutes <= 0) return;

        foreach (var alarm in alarms)
        {
            if (alarm.Status != AlarmStatus.Active) continue;
            if (alarm.Schedule.Type == AlarmScheduleType.Interval) continue;

            var nextTrigger = alarm.GetNextTrigger();
            if (nextTrigger == null) continue;

            // Check if alarm should have fired recently (within grace period before now)
            var time = alarm.Schedule.GetTimeOfDay();
            var scheduledToday = now.Date + time;
            if (scheduledToday < now && (now - scheduledToday).TotalMinutes <= graceMinutes)
            {
                // Check it wasn't already fired
                var key = $"{alarm.Id}|{scheduledToday:yyyy-MM-dd HH:mm}";
                bool isNew;
                lock (_lock) { isNew = _firedKeys.Add(key); }

                if (isNew && (alarm.LastTriggeredAt == null || alarm.LastTriggeredAt.Value.Date < now.Date
                    || alarm.LastTriggeredAt.Value.TimeOfDay < time.Subtract(TimeSpan.FromMinutes(1))))
                {
                    AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
                    {
                        AlarmId = alarm.Id,
                        AlarmTitle = alarm.Title,
                        EventType = AlarmHistoryEventType.RestartRecovered,
                        Detail = $"Missed at {scheduledToday:HH:mm}, recovered within {graceMinutes}min grace period",
                    });

                    AlarmStore.RecordTrigger(alarm.Id);
                    AlarmTriggered?.Invoke(alarm);
                }
            }
        }
    }

    private static void PruneFiredKeys(DateTime now)
    {
        lock (_lock)
        {
            var stale = _firedKeys.Where(k =>
            {
                var parts = k.Split('|');
                return parts.Length == 2 && DateTime.TryParse(parts[1], out var dt) && (now - dt).TotalHours > 24;
            }).ToList();

            foreach (var key in stale)
                _firedKeys.Remove(key);
        }
    }
}
