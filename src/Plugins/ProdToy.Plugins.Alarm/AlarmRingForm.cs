using System.Drawing;
using ProdToy.Sdk;

namespace ProdToy.Plugins.Alarm;

class AlarmRingForm : Form
{
    private readonly AlarmEntry _alarm;
    private readonly System.Windows.Forms.Timer _autoCloseTimer;

    public event Action? Dismissed;
    public event Action<int>? Snoozed;

    public AlarmRingForm(AlarmEntry alarm, PluginTheme theme, string globalFont)
    {
        _alarm = alarm;

        // Defensive: if the host passed a null theme, fall back to hard-coded
        // dark colors so the form at least renders (blank controls on black is
        // better than an NRE that silently swallows the whole test-trigger flow).
        if (theme == null)
        {
            PluginLog.Warn("AlarmRingForm: theme was null, falling back to defaults");
            theme = new PluginTheme(
                Name: "fallback",
                BgDark:        Color.FromArgb(0x1e, 0x1e, 0x24),
                BgHeader:      Color.FromArgb(0x2a, 0x2a, 0x32),
                Primary:       Color.FromArgb(0x5f, 0xc7, 0x6e),
                PrimaryLight:  Color.FromArgb(0x7f, 0xd7, 0x8e),
                PrimaryDim:    Color.FromArgb(0x3a, 0x5a, 0x4a),
                TextPrimary:   Color.FromArgb(0xe6, 0xe6, 0xee),
                TextSecondary: Color.FromArgb(0x9a, 0x9a, 0xa8),
                Border:        Color.FromArgb(0x3a, 0x3a, 0x44),
                SuccessColor:  Color.FromArgb(0x5f, 0xc7, 0x6e),
                ErrorColor:    Color.FromArgb(0xe7, 0x48, 0x56),
                SuccessBg:     Color.FromArgb(0x24, 0x3a, 0x2a),
                ErrorBg:       Color.FromArgb(0x3a, 0x24, 0x24));
        }

        Text = "Alarm";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        Size = new Size(420, 320);
        BackColor = theme.BgDark;
        ForeColor = theme.TextPrimary;
        Font = new Font("Segoe UI", 10f);
        try { Icon = IconHelper.CreateAppIcon(theme.Primary); }
        catch (Exception ex) { PluginLog.Warn($"AlarmRingForm: CreateAppIcon failed: {ex.Message}"); }
        AutoScaleMode = AutoScaleMode.Dpi;

        int pad = 24;
        int y = pad;
        int contentWidth = ClientSize.Width - pad * 2;

        var titleLabel = new Label
        {
            Text = $"\u23F0  {alarm.Title}",
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = theme.Primary,
            AutoSize = true,
            MaximumSize = new Size(contentWidth, 0),
            Location = new Point(pad, y),
            BackColor = Color.Transparent,
        };
        Controls.Add(titleLabel);
        y += titleLabel.PreferredHeight + 12;

        if (!string.IsNullOrWhiteSpace(alarm.Message))
        {
            var msgLabel = new Label
            {
                Text = alarm.Message,
                Font = new Font("Segoe UI", 10f),
                ForeColor = theme.TextPrimary,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 80),
                Location = new Point(pad, y),
                BackColor = Color.Transparent,
            };
            Controls.Add(msgLabel);
            y += msgLabel.PreferredHeight + 12;
        }

        var timeLabel = new Label
        {
            Text = $"Triggered at {DateTime.Now:HH:mm:ss}",
            Font = new Font("Segoe UI", 9f),
            ForeColor = theme.TextSecondary,
            AutoSize = true,
            Location = new Point(pad, y),
            BackColor = Color.Transparent,
        };
        Controls.Add(timeLabel);
        y += 30;

        var line = new Panel
        {
            BackColor = theme.Primary,
            Location = new Point(pad, y),
            Size = new Size(contentWidth, 2),
        };
        Controls.Add(line);
        y += 16;

        var snoozeLabel = new Label
        {
            Text = "Snooze:",
            Font = new Font("Segoe UI", 9f),
            ForeColor = theme.TextSecondary,
            AutoSize = true,
            Location = new Point(pad, y + 5),
            BackColor = Color.Transparent,
        };
        Controls.Add(snoozeLabel);

        var snoozeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9f),
            BackColor = theme.BgHeader,
            ForeColor = theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(100, 24),
            Location = new Point(pad + 60, y + 2),
        };
        foreach (var m in new[] { "5 min", "10 min", "15 min", "30 min", "1 hour" })
            snoozeCombo.Items.Add(m);
        snoozeCombo.SelectedIndex = 0;
        Controls.Add(snoozeCombo);

        var snoozeButton = new RoundedButton
        {
            Text = "Snooze",
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Size = new Size(80, 30),
            Location = new Point(pad + 170, y),
            FlatStyle = FlatStyle.Flat,
            BackColor = theme.PrimaryDim,
            ForeColor = theme.TextPrimary,
            Cursor = Cursors.Hand,
        };
        snoozeButton.FlatAppearance.BorderSize = 0;
        snoozeButton.FlatAppearance.MouseOverBackColor = theme.Primary;
        snoozeButton.Click += (_, _) =>
        {
            int minutes = snoozeCombo.SelectedIndex switch
            {
                0 => 5, 1 => 10, 2 => 15, 3 => 30, 4 => 60, _ => 5,
            };
            Snoozed?.Invoke(minutes);
            Close();
        };
        Controls.Add(snoozeButton);
        y += 42;

        var dismissButton = new RoundedButton
        {
            Text = "Dismiss",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Size = new Size(contentWidth, 36),
            Location = new Point(pad, y),
            FlatStyle = FlatStyle.Flat,
            BackColor = theme.Primary,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
        };
        dismissButton.FlatAppearance.BorderSize = 0;
        dismissButton.FlatAppearance.MouseOverBackColor = theme.PrimaryLight;
        dismissButton.Click += (_, _) =>
        {
            Dismissed?.Invoke();
            Close();
        };
        Controls.Add(dismissButton);

        ClientSize = new Size(ClientSize.Width, y + 36 + pad);

        _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 300000 };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            AlarmStore.AddHistoryEntry(new AlarmHistoryEntry
            {
                AlarmId = alarm.Id,
                AlarmTitle = alarm.Title,
                EventType = AlarmHistoryEventType.Missed,
                Detail = "Auto-closed after 5 minutes without interaction",
            });
            Close();
        };
        _autoCloseTimer.Start();

        Shown += (_, _) =>
        {
            TopMost = true;
            Activate();
            try { AlarmNativeMethods.SetForegroundWindow(Handle); }
            catch (Exception ex) { PluginLog.Warn($"SetForegroundWindow failed: {ex.Message}"); }
        };

        FormClosed += (_, _) =>
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Dispose();
        };

        if (!string.IsNullOrEmpty(globalFont) && globalFont != "Segoe UI")
        {
            try
            {
                Font = new Font(globalFont, Font.Size, Font.Style);
                foreach (Control c in Controls)
                    ApplyFontRecursive(c, globalFont);
            }
            catch (Exception ex)
            {
                PluginLog.Warn($"AlarmRingForm: global font '{globalFont}' apply failed: {ex.Message}");
            }
        }
    }

    private static void ApplyFontRecursive(Control control, string fontFamily)
    {
        try { control.Font = new Font(fontFamily, control.Font.Size, control.Font.Style); } catch { }
        foreach (Control child in control.Controls)
            ApplyFontRecursive(child, fontFamily);
    }
}
