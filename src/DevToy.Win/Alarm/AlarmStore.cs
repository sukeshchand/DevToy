using System.Diagnostics;
using System.Text.Json;

namespace DevToy;

static class AlarmStore
{
    private static readonly string _alarmsFile = Path.Combine(AppPaths.AlarmsDir, "alarms.json");
    private static readonly string _historyFile = Path.Combine(AppPaths.AlarmsDir, "alarm-history.json");
    private static readonly object _lock = new();
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static List<AlarmEntry>? _cachedAlarms;
    private static List<AlarmHistoryEntry>? _cachedHistory;

    public static List<AlarmEntry> LoadAlarms()
    {
        lock (_lock)
        {
            if (_cachedAlarms != null) return _cachedAlarms;
            try
            {
                if (File.Exists(_alarmsFile))
                {
                    var json = File.ReadAllText(_alarmsFile);
                    _cachedAlarms = JsonSerializer.Deserialize<List<AlarmEntry>>(json) ?? new();
                    return _cachedAlarms;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load alarms: {ex.Message}");
            }
            _cachedAlarms = new();
            return _cachedAlarms;
        }
    }

    public static void SaveAlarms(List<AlarmEntry> alarms)
    {
        lock (_lock)
        {
            _cachedAlarms = alarms;
            try
            {
                Directory.CreateDirectory(AppPaths.AlarmsDir);
                var json = JsonSerializer.Serialize(alarms, _jsonOpts);
                File.WriteAllText(_alarmsFile, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save alarms: {ex.Message}");
            }
        }
    }

    public static AlarmEntry? GetAlarm(string id)
    {
        return LoadAlarms().FirstOrDefault(a => a.Id == id);
    }

    public static void AddAlarm(AlarmEntry alarm)
    {
        var alarms = new List<AlarmEntry>(LoadAlarms()) { alarm };
        SaveAlarms(alarms);
        AddHistoryEntry(new AlarmHistoryEntry
        {
            AlarmId = alarm.Id,
            AlarmTitle = alarm.Title,
            EventType = AlarmHistoryEventType.Created,
        });
    }

    public static void UpdateAlarm(AlarmEntry alarm)
    {
        var alarms = new List<AlarmEntry>(LoadAlarms());
        int idx = alarms.FindIndex(a => a.Id == alarm.Id);
        if (idx >= 0)
            alarms[idx] = alarm with { UpdatedAt = DateTime.Now };
        else
            alarms.Add(alarm);
        SaveAlarms(alarms);
    }

    public static void DeleteAlarm(string id)
    {
        var alarms = new List<AlarmEntry>(LoadAlarms());
        var alarm = alarms.FirstOrDefault(a => a.Id == id);
        if (alarm != null)
        {
            alarms.Remove(alarm);
            SaveAlarms(alarms);
            AddHistoryEntry(new AlarmHistoryEntry
            {
                AlarmId = id,
                AlarmTitle = alarm.Title,
                EventType = AlarmHistoryEventType.Deleted,
            });
        }
    }

    public static void SetStatus(string id, AlarmStatus status)
    {
        var alarms = new List<AlarmEntry>(LoadAlarms());
        int idx = alarms.FindIndex(a => a.Id == id);
        if (idx >= 0)
        {
            alarms[idx] = alarms[idx] with { Status = status, UpdatedAt = DateTime.Now };
            SaveAlarms(alarms);
        }
    }

    public static void RecordTrigger(string id)
    {
        var alarms = new List<AlarmEntry>(LoadAlarms());
        int idx = alarms.FindIndex(a => a.Id == id);
        if (idx >= 0)
        {
            var alarm = alarms[idx];
            alarms[idx] = alarm with
            {
                LastTriggeredAt = DateTime.Now,
                TriggerCount = alarm.TriggerCount + 1,
                UpdatedAt = DateTime.Now,
            };

            // Handle one-time and fire-and-forget
            if (alarm.Schedule.Type == AlarmScheduleType.Once)
            {
                alarms[idx] = alarms[idx] with { Status = AlarmStatus.Completed };
            }

            if (alarm.FireAndForget)
            {
                alarms[idx] = alarms[idx] with { Status = AlarmStatus.Completed };
                AddHistoryEntry(new AlarmHistoryEntry
                {
                    AlarmId = id,
                    AlarmTitle = alarm.Title,
                    EventType = AlarmHistoryEventType.Completed,
                    Detail = "Fire-and-forget alarm completed",
                });
            }

            SaveAlarms(alarms);
        }
    }

    // --- History ---

    public static List<AlarmHistoryEntry> LoadHistory()
    {
        lock (_lock)
        {
            if (_cachedHistory != null) return _cachedHistory;
            try
            {
                if (File.Exists(_historyFile))
                {
                    var json = File.ReadAllText(_historyFile);
                    _cachedHistory = JsonSerializer.Deserialize<List<AlarmHistoryEntry>>(json) ?? new();
                    return _cachedHistory;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load alarm history: {ex.Message}");
            }
            _cachedHistory = new();
            return _cachedHistory;
        }
    }

    public static List<AlarmHistoryEntry> LoadHistory(string alarmId)
    {
        return LoadHistory().Where(h => h.AlarmId == alarmId).ToList();
    }

    public static void AddHistoryEntry(AlarmHistoryEntry entry)
    {
        lock (_lock)
        {
            var history = new List<AlarmHistoryEntry>(LoadHistory()) { entry };

            // Trim to max entries
            int max = AppSettings.Load().AlarmHistoryMaxEntries;
            if (history.Count > max)
                history = history.Skip(history.Count - max).ToList();

            _cachedHistory = history;
            try
            {
                Directory.CreateDirectory(AppPaths.AlarmsDir);
                var json = JsonSerializer.Serialize(history, _jsonOpts);
                File.WriteAllText(_historyFile, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save alarm history: {ex.Message}");
            }
        }
    }

    public static void Invalidate()
    {
        lock (_lock)
        {
            _cachedAlarms = null;
            _cachedHistory = null;
        }
    }
}
