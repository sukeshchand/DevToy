using System.Text.Json;
using System.Text.Json.Nodes;
using ProdToy.Sdk;

namespace ProdToy.Plugins.Alarm;

/// <summary>
/// MCP tools exposed by the Alarm plugin: list/create/update/delete alarms,
/// pause/resume/snooze, and read the trigger history. Lets a Claude instance
/// set reminders for the user ("remind me in 20 minutes to check the deploy")
/// and inspect why an alarm did or didn't fire. All handlers run on the UI
/// thread (RpcRouter contract) and return single-line JSON.
/// </summary>
static class AlarmMcpTools
{
    private const string Section = "Alarms";

    private static readonly (string Name, string Type, string Desc, bool Required) NameArg =
        ("name", "string", "The alarm's title (or id).", true);

    public static List<IDisposable> RegisterAll(IPluginContext ctx)
    {
        var host = ctx.Host;
        return new List<IDisposable>
        {
            host.RegisterMcpTool(new McpTool("alarm_list",
                "List the user's alarms: title, schedule, live status (Active/Paused/Snoozed/Disabled/…), " +
                "next trigger time, category, and priority.",
                Schema(("status", "string", "Filter by display status, e.g. Active, Paused, Disabled.", false)),
                Section),
                cmd => Task.FromResult(List(cmd))),

            host.RegisterMcpTool(new McpTool("alarm_create",
                "Create an alarm/reminder. Simplest: {title, inMinutes} for a one-shot reminder N minutes from " +
                "now. Or give time (\"HH:mm\") plus a schedule: once (with date), daily, weekdays, weekend, " +
                "weekly/custom (with days), monthly (with dayOfMonth), interval (with intervalMinutes).",
                Schema(
                    ("title", "string", "Alarm title (shown in the popup).", true),
                    ("message", "string", "Longer message shown when it fires.", false),
                    ("inMinutes", "integer", "Shortcut: fire once N minutes from now (overrides time/schedule/date).", false),
                    ("time", "string", "Time of day \"HH:mm\" (default 09:00).", false),
                    ("schedule", "string", "once | daily | weekdays | weekend | weekly | monthly | interval | custom (default once).", false),
                    ("date", "string", "For once: \"yyyy-MM-dd\" (default: today if the time is still ahead, else tomorrow).", false),
                    ("days", "array", "For weekly/custom: day names, e.g. [\"Monday\",\"Thursday\"].", false),
                    ("dayOfMonth", "integer", "For monthly: 1–31.", false),
                    ("intervalMinutes", "integer", "For interval: fire every N minutes.", false),
                    ("category", "string", "Optional grouping label.", false),
                    ("priority", "string", "low | normal | high (default normal).", false),
                    ("snoozeMinutes", "integer", "Snooze length offered by the popup (default 5).", false)),
                Section,
                "The response includes the computed next trigger so you can confirm the schedule was understood."),
                cmd => Task.FromResult(Create(ctx, cmd))),

            host.RegisterMcpTool(new McpTool("alarm_update",
                "Update an existing alarm's title, message, time, status (active/disabled), category, priority, " +
                "snoozeMinutes, or endDate.",
                Schema(NameArg,
                    ("title", "string", "New title.", false),
                    ("message", "string", "New message.", false),
                    ("time", "string", "New time of day \"HH:mm\".", false),
                    ("status", "string", "active | disabled.", false),
                    ("category", "string", "New category.", false),
                    ("priority", "string", "low | normal | high.", false),
                    ("snoozeMinutes", "integer", "New snooze length.", false),
                    ("endDate", "string", "Stop firing after this date (\"yyyy-MM-dd\"); empty string clears it.", false)),
                Section),
                cmd => Task.FromResult(Update(cmd))),

            host.RegisterMcpTool(new McpTool("alarm_delete",
                "Delete an alarm permanently. Confirm with the user before deleting an alarm you did not create.",
                Schema(NameArg), Section),
                cmd => Task.FromResult(Delete(cmd))),

            host.RegisterMcpTool(new McpTool("alarm_pause",
                "Pause an alarm so it stays silent for a while (default 60 minutes) without disabling it.",
                Schema(NameArg, ("minutes", "integer", "How long to pause (default 60).", false)), Section),
                cmd => Task.FromResult(Pause(cmd))),

            host.RegisterMcpTool(new McpTool("alarm_resume",
                "Resume an alarm: clears any pause/snooze and re-activates it if it was disabled.",
                Schema(NameArg), Section),
                cmd => Task.FromResult(Resume(cmd))),

            host.RegisterMcpTool(new McpTool("alarm_snooze",
                "Snooze an alarm (default: its configured snooze length).",
                Schema(NameArg, ("minutes", "integer", "Snooze length in minutes.", false)), Section),
                cmd => Task.FromResult(Snooze(cmd))),

            host.RegisterMcpTool(new McpTool("alarm_history",
                "Recent alarm events (Triggered/Missed/Snoozed/Dismissed/…), newest first — useful to check " +
                "whether and when an alarm actually fired.",
                Schema(("name", "string", "Only events for this alarm (title or id).", false),
                       ("limit", "integer", "Max events to return (default 30).", false)),
                Section),
                cmd => Task.FromResult(History(cmd))),
        };
    }

    // ─────────────────────────── handlers ───────────────────────────

    private static string List(PipeCommand cmd)
    {
        string? statusFilter = GetString(cmd, "status");
        var items = AlarmStore.LoadAlarms()
            .Select(a => new { Alarm = a, Display = a.GetDisplayStatus() })
            .Where(x => statusFilter == null
                || string.Equals(x.Display.ToString(), statusFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Alarm.GetNextTrigger() ?? DateTime.MaxValue)
            .Select(x => new
            {
                id = x.Alarm.Id,
                title = x.Alarm.Title,
                message = string.IsNullOrWhiteSpace(x.Alarm.Message) ? null : x.Alarm.Message,
                schedule = x.Alarm.GetScheduleDescription(),
                time = x.Alarm.Schedule.TimeOfDay,
                status = x.Display.ToString(),
                nextTrigger = x.Alarm.GetNextTrigger(),
                category = x.Alarm.Category,
                priority = x.Alarm.Priority.ToString(),
                lastTriggeredAt = x.Alarm.LastTriggeredAt,
                triggerCount = x.Alarm.TriggerCount,
            })
            .ToList();
        return JsonSerializer.Serialize(new { ok = true, count = items.Count, alarms = items });
    }

    private static string Create(IPluginContext ctx, PipeCommand cmd)
    {
        string? title = GetString(cmd, "title");
        if (string.IsNullOrWhiteSpace(title)) return Fail("Missing 'title'.");

        var schedule = new AlarmSchedule();
        int? inMinutes = GetInt(cmd, "inMinutes");
        if (inMinutes is int mins)
        {
            if (mins < 1) return Fail("'inMinutes' must be at least 1.");
            var when = DateTime.Now.AddMinutes(mins);
            schedule = schedule with
            {
                Type = AlarmScheduleType.Once,
                OneTimeDate = when.ToString("yyyy-MM-dd"),
                TimeOfDay = when.ToString("HH:mm"),
            };
        }
        else
        {
            string time = GetString(cmd, "time") ?? "09:00";
            if (!TimeSpan.TryParse(time, out var tod)) return Fail($"Bad 'time' \"{time}\" — use \"HH:mm\".");

            string schedName = GetString(cmd, "schedule") ?? "once";
            if (!Enum.TryParse<AlarmScheduleType>(schedName, ignoreCase: true, out var type))
                return Fail($"Bad 'schedule' \"{schedName}\". Use once, daily, weekdays, weekend, weekly, monthly, interval, or custom.");

            schedule = schedule with { Type = type, TimeOfDay = time };
            switch (type)
            {
                case AlarmScheduleType.Once:
                {
                    string? date = GetString(cmd, "date");
                    if (date != null && !DateTime.TryParse(date, out _))
                        return Fail($"Bad 'date' \"{date}\" — use \"yyyy-MM-dd\".");
                    date ??= (DateTime.Now.Date + tod > DateTime.Now
                        ? DateTime.Now : DateTime.Now.AddDays(1)).ToString("yyyy-MM-dd");
                    schedule = schedule with { OneTimeDate = date };
                    break;
                }
                case AlarmScheduleType.Weekly:
                case AlarmScheduleType.Custom:
                {
                    var days = GetDays(cmd);
                    if (days == null || days.Length == 0)
                        return Fail("Weekly/custom schedules need 'days', e.g. [\"Monday\",\"Thursday\"].");
                    schedule = schedule with { CustomDays = days };
                    break;
                }
                case AlarmScheduleType.Monthly:
                {
                    int? dom = GetInt(cmd, "dayOfMonth");
                    if (dom is not (>= 1 and <= 31)) return Fail("Monthly schedules need 'dayOfMonth' (1–31).");
                    schedule = schedule with { DayOfMonth = dom };
                    break;
                }
                case AlarmScheduleType.Interval:
                {
                    int? iv = GetInt(cmd, "intervalMinutes");
                    if (iv is not > 0) return Fail("Interval schedules need 'intervalMinutes' (> 0).");
                    schedule = schedule with { IntervalMinutes = iv };
                    break;
                }
            }
        }

        var priority = AlarmPriority.Normal;
        if (GetString(cmd, "priority") is string prio
            && !Enum.TryParse(prio, ignoreCase: true, out priority))
            return Fail($"Bad 'priority' \"{prio}\" — use low, normal, or high.");

        var alarm = new AlarmEntry
        {
            Title = title!.Trim(),
            Message = GetString(cmd, "message") ?? "",
            Schedule = schedule,
            Category = GetString(cmd, "category"),
            Priority = priority,
            SnoozeMinutes = Math.Clamp(GetInt(cmd, "snoozeMinutes") ?? 5, 1, 24 * 60),
        };
        AlarmStore.AddAlarm(alarm);
        AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
        {
            AlarmId = alarm.Id,
            AlarmTitle = alarm.Title,
            EventType = AlarmHistoryEventType.Created,
            Detail = "Created via MCP",
        });

        bool enabled = ctx.LoadSettings<AlarmPluginSettings>().AlarmsEnabled;
        return JsonSerializer.Serialize(new
        {
            ok = true,
            id = alarm.Id,
            message = $"Created alarm '{alarm.Title}' — {alarm.GetScheduleDescription()} at {alarm.Schedule.TimeOfDay}."
                + (enabled ? "" : " WARNING: alarms are disabled in ProdToy settings — it will not fire until enabled."),
            nextTrigger = alarm.GetNextTrigger(),
        });
    }

    private static string Update(PipeCommand cmd)
    {
        var (alarm, error) = Resolve(cmd);
        if (error != null) return Fail(error);

        var updated = alarm!;
        if (GetString(cmd, "title") is { Length: > 0 } t) updated = updated with { Title = t.Trim() };
        if (GetString(cmd, "message") is string m) updated = updated with { Message = m };
        if (GetString(cmd, "time") is string time)
        {
            if (!TimeSpan.TryParse(time, out _)) return Fail($"Bad 'time' \"{time}\" — use \"HH:mm\".");
            updated = updated with { Schedule = updated.Schedule with { TimeOfDay = time } };
        }
        if (GetString(cmd, "status") is string st)
        {
            if (string.Equals(st, "active", StringComparison.OrdinalIgnoreCase))
                updated = updated with { Status = AlarmStatus.Active };
            else if (string.Equals(st, "disabled", StringComparison.OrdinalIgnoreCase))
                updated = updated with { Status = AlarmStatus.Disabled };
            else return Fail($"Bad 'status' \"{st}\" — use active or disabled.");
        }
        if (GetString(cmd, "category") is string cat) updated = updated with { Category = cat };
        if (GetString(cmd, "priority") is string prio)
        {
            if (!Enum.TryParse<AlarmPriority>(prio, ignoreCase: true, out var p))
                return Fail($"Bad 'priority' \"{prio}\" — use low, normal, or high.");
            updated = updated with { Priority = p };
        }
        if (GetInt(cmd, "snoozeMinutes") is int sn) updated = updated with { SnoozeMinutes = Math.Clamp(sn, 1, 24 * 60) };
        if (GetString(cmd, "endDate") is string ed)
            updated = updated with { EndDate = ed.Length == 0 ? null : ed };

        updated = updated with { UpdatedAt = DateTime.Now };
        AlarmStore.UpdateAlarm(updated);
        LogEvent(updated, AlarmHistoryEventType.Edited, "Edited via MCP");
        return Ok($"Updated alarm '{updated.Title}'. Next trigger: {updated.GetNextTrigger()?.ToString() ?? "none"}.");
    }

    private static string Delete(PipeCommand cmd)
    {
        var (alarm, error) = Resolve(cmd);
        if (error != null) return Fail(error);
        AlarmStore.DeleteAlarm(alarm!.Id);
        LogEvent(alarm, AlarmHistoryEventType.Deleted, "Deleted via MCP");
        return Ok($"Deleted alarm '{alarm.Title}'.");
    }

    private static string Pause(PipeCommand cmd)
    {
        var (alarm, error) = Resolve(cmd);
        if (error != null) return Fail(error);
        int minutes = Math.Clamp(GetInt(cmd, "minutes") ?? 60, 1, 7 * 24 * 60);
        var until = DateTime.Now.AddMinutes(minutes);
        AlarmStore.UpdateAlarm(alarm! with { PausedUntil = until, UpdatedAt = DateTime.Now });
        LogEvent(alarm, AlarmHistoryEventType.Paused, $"Paused {minutes} min via MCP");
        return Ok($"Paused '{alarm.Title}' until {until:HH:mm} ({minutes} min).");
    }

    private static string Resume(PipeCommand cmd)
    {
        var (alarm, error) = Resolve(cmd);
        if (error != null) return Fail(error);
        var updated = alarm! with
        {
            PausedUntil = null,
            SnoozedUntil = null,
            Status = alarm.Status == AlarmStatus.Disabled ? AlarmStatus.Active : alarm.Status,
            UpdatedAt = DateTime.Now,
        };
        AlarmStore.UpdateAlarm(updated);
        LogEvent(updated, AlarmHistoryEventType.Resumed, "Resumed via MCP");
        return Ok($"Resumed '{updated.Title}'. Next trigger: {updated.GetNextTrigger()?.ToString() ?? "none"}.");
    }

    private static string Snooze(PipeCommand cmd)
    {
        var (alarm, error) = Resolve(cmd);
        if (error != null) return Fail(error);
        int minutes = Math.Clamp(GetInt(cmd, "minutes") ?? alarm!.SnoozeMinutes, 1, 24 * 60);
        var until = DateTime.Now.AddMinutes(minutes);
        AlarmStore.UpdateAlarm(alarm! with { SnoozedUntil = until, UpdatedAt = DateTime.Now });
        LogEvent(alarm!, AlarmHistoryEventType.Snoozed, $"Snoozed {minutes} min via MCP");
        return Ok($"Snoozed '{alarm!.Title}' until {until:HH:mm}.");
    }

    private static string History(PipeCommand cmd)
    {
        int limit = Math.Clamp(GetInt(cmd, "limit") ?? 30, 1, 500);
        List<AlarmHistoryEntry> events;
        if (GetString(cmd, "name") is { Length: > 0 })
        {
            var (alarm, error) = Resolve(cmd);
            if (error != null) return Fail(error);
            events = AlarmStore.LoadHistory(alarm!.Id);
        }
        else
        {
            events = AlarmStore.LoadHistory();
        }

        var items = events
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new
            {
                alarm = e.AlarmTitle,
                @event = e.EventType.ToString(),
                at = e.Timestamp,
                detail = e.Detail,
            });
        return JsonSerializer.Serialize(new { ok = true, events = items });
    }

    // ─────────────────────────── helpers ───────────────────────────

    private static (AlarmEntry? Alarm, string? Error) Resolve(PipeCommand cmd)
    {
        string? name = GetString(cmd, "name");
        if (string.IsNullOrWhiteSpace(name)) return (null, "Missing 'name' — the alarm's title (or id).");

        var all = AlarmStore.LoadAlarms();
        var matches = all.Where(a =>
            string.Equals(a.Id, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Title, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return (matches[0], null);
        if (matches.Count > 1)
            return (null, $"Alarm '{name}' is ambiguous ({matches.Count} share that title) — use the id from alarm_list.");
        var titles = string.Join(", ", all.Select(a => a.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        return (null, $"No alarm named '{name}'." + (titles.Length > 0 ? $" Available: {titles}." : ""));
    }

    private static void LogEvent(AlarmEntry alarm, AlarmHistoryEventType type, string detail) =>
        AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
        {
            AlarmId = alarm.Id,
            AlarmTitle = alarm.Title,
            EventType = type,
            Detail = detail,
        });

    private static string? GetString(PipeCommand cmd, string prop)
    {
        if (string.IsNullOrWhiteSpace(cmd.PayloadJson)) return null;
        try { return JsonNode.Parse(cmd.PayloadJson)?[prop]?.GetValue<string>(); }
        catch (Exception) { return null; }
    }

    private static int? GetInt(PipeCommand cmd, string prop)
    {
        if (string.IsNullOrWhiteSpace(cmd.PayloadJson)) return null;
        try { return JsonNode.Parse(cmd.PayloadJson)?[prop]?.GetValue<int>(); }
        catch (Exception) { return null; }
    }

    private static DayOfWeek[]? GetDays(PipeCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.PayloadJson)) return null;
        try
        {
            if (JsonNode.Parse(cmd.PayloadJson)?["days"] is not JsonArray arr) return null;
            var days = new List<DayOfWeek>();
            foreach (var n in arr)
            {
                if (Enum.TryParse<DayOfWeek>(n?.GetValue<string>(), ignoreCase: true, out var d))
                    days.Add(d);
                else return null;
            }
            return days.Distinct().ToArray();
        }
        catch (Exception) { return null; }
    }

    private static string Schema(params (string Name, string Type, string Desc, bool Required)[] props)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var p in props)
        {
            var prop = new JsonObject { ["type"] = p.Type, ["description"] = p.Desc };
            if (p.Type == "array") prop["items"] = new JsonObject { ["type"] = "string" };
            properties[p.Name] = prop;
            if (p.Required) required.Add(p.Name);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        }.ToJsonString();
    }

    private static string Ok(string message) =>
        JsonSerializer.Serialize(new { ok = true, message });

    private static string Fail(string message) =>
        JsonSerializer.Serialize(new { ok = false, message });
}
