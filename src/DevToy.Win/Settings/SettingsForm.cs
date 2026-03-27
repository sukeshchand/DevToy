using System.Drawing;
using System.Drawing.Drawing2D;

namespace DevToy;

class SettingsForm : Form
{
    private PopupTheme _currentTheme;
    private readonly Panel _themePreview;
    private readonly Label _themeNameLabel;
    private readonly ComboBox _themeCombo;
    private readonly ThemedTabControl _tabControl;
    private readonly Label _versionLabel;

    private readonly Label _titleLabel;
    private readonly Panel _accentLine;

    public event Action<PopupTheme>? ThemeChanged;
    public event Action<bool>? HistoryEnabledChanged;
    public event Action<bool>? SnoozeChanged;
    public event Action<bool>? ShowQuotesChanged;
    public event Action? UninstallRequested;
    public event Action<string>? ScreenshotHotkeyChanged;
    public event Action<bool>? ScreenshotEnabledChanged;

    public SettingsForm(PopupTheme currentTheme, DateTime snoozeUntil)
    {
        _currentTheme = currentTheme;

        Text = "DevToy Settings";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(700, 560);
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = currentTheme.BgDark;
        ForeColor = currentTheme.TextPrimary;
        Font = new Font("Segoe UI", 10f);
        Icon = Themes.CreateAppIcon(currentTheme.Primary);

        int leftMargin = 24;
        int contentWidth = 652;

        // --- Title ---
        int y = 16;
        _titleLabel = new Label
        {
            Text = "Settings",
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = currentTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(leftMargin, y),
            BackColor = Color.Transparent,
        };
        Controls.Add(_titleLabel);
        y += 44;

        // --- Accent line ---
        _accentLine = new Panel
        {
            BackColor = currentTheme.Primary,
            Location = new Point(leftMargin, y),
            Size = new Size(contentWidth, 2),
        };
        Controls.Add(_accentLine);
        y += 8;

        // --- TabControl (owner-drawn) ---
        int tabCount = 6;
        int tabWidth = contentWidth / tabCount;
        _tabControl = new ThemedTabControl(currentTheme)
        {
            Location = new Point(leftMargin, y),
            Size = new Size(contentWidth, 440),
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(tabWidth, 32),
            Padding = new Point(0, 0),
        };
        Controls.Add(_tabControl);

        int tp = 16; // inner padding for controls inside tab pages
        int tabInner = contentWidth - tp * 2 - 2;

        // =============================================
        // TAB 0: General
        // =============================================
        var generalPage = CreateTabPage("General", currentTheme);
        _tabControl.TabPages.Add(generalPage);

        var generalPlaceholder = new Label
        {
            Text = "More settings coming soon.",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(tp, tp),
            BackColor = Color.Transparent,
        };
        generalPage.Controls.Add(generalPlaceholder);

        // =============================================
        // TAB 1: Screen Capture
        // =============================================
        var capturePage = CreateTabPage("Capture", currentTheme);
        _tabControl.TabPages.Add(capturePage);

        int sc = tp;

        var captureSettings = AppSettings.Load();
        bool captureEnabled = captureSettings.ScreenshotEnabled;

        var enableCaptureLabel = CreateSectionLabel("SCREEN CAPTURE", tp, sc);
        capturePage.Controls.Add(enableCaptureLabel);
        sc += 26;

        var captureHotkeyControls = new List<Control>();

        var enableCaptureCheck = new CheckBox
        {
            Text = "Enable screen capture",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = captureEnabled,
            AutoSize = true,
            Location = new Point(tp, sc),
            Cursor = Cursors.Hand,
        };
        capturePage.Controls.Add(enableCaptureCheck);
        sc += 32;

        capturePage.Controls.Add(CreateSeparator(tp, sc, tabInner));
        sc += 14;

        var captureShortcutLabel = CreateSectionLabel("SHORTCUT KEY", tp, sc);
        capturePage.Controls.Add(captureShortcutLabel);
        captureHotkeyControls.Add(captureShortcutLabel);
        sc += 26;

        var captureShortcutHint = new Label
        {
            Text = "Global hotkey to start screen capture:",
            Font = new Font("Segoe UI", 9f),
            ForeColor = currentTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(tp, sc),
            BackColor = Color.Transparent,
        };
        capturePage.Controls.Add(captureShortcutHint);
        captureHotkeyControls.Add(captureShortcutHint);
        sc += 24;

        var currentHotkey = captureSettings.ScreenshotHotkey;
        var hotkeyBox = new TextBox
        {
            Text = currentHotkey,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            ForeColor = currentTheme.Primary,
            BackColor = currentTheme.BgHeader,
            BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(260, 30),
            Location = new Point(tp, sc),
            ReadOnly = true,
            TextAlign = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
        };

        bool hotkeyRecording = false;
        var hotkeyRecordButton = new RoundedButton
        {
            Text = "Change",
            Font = new Font("Segoe UI", 8.5f),
            Size = new Size(80, 30),
            Location = new Point(tp + 268, sc),
            FlatStyle = FlatStyle.Flat,
            BackColor = currentTheme.PrimaryDim,
            ForeColor = currentTheme.TextSecondary,
            Cursor = Cursors.Hand,
        };
        hotkeyRecordButton.FlatAppearance.BorderSize = 0;
        hotkeyRecordButton.FlatAppearance.MouseOverBackColor = currentTheme.Primary;

        var hotkeyClearButton = new RoundedButton
        {
            Text = "Clear",
            Font = new Font("Segoe UI", 8.5f),
            Size = new Size(60, 30),
            Location = new Point(tp + 356, sc),
            FlatStyle = FlatStyle.Flat,
            BackColor = currentTheme.PrimaryDim,
            ForeColor = currentTheme.TextSecondary,
            Cursor = Cursors.Hand,
        };
        hotkeyClearButton.FlatAppearance.BorderSize = 0;
        hotkeyClearButton.FlatAppearance.MouseOverBackColor = currentTheme.Primary;

        var hotkeyStatusLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(tp, sc + 38),
            BackColor = Color.Transparent,
        };

        hotkeyRecordButton.Click += (_, _) =>
        {
            if (!hotkeyRecording)
            {
                hotkeyRecording = true;
                hotkeyBox.Text = "Press a key combination...";
                hotkeyBox.ForeColor = currentTheme.TextSecondary;
                hotkeyRecordButton.Text = "Cancel";
                hotkeyStatusLabel.Text = "Press modifier(s) + key, e.g. Ctrl+Shift+S";
            }
            else
            {
                hotkeyRecording = false;
                hotkeyBox.Text = AppSettings.Load().ScreenshotHotkey;
                hotkeyBox.ForeColor = currentTheme.Primary;
                hotkeyRecordButton.Text = "Change";
                hotkeyStatusLabel.Text = "";
            }
        };

        hotkeyBox.KeyDown += (_, e) =>
        {
            if (!hotkeyRecording) return;
            e.SuppressKeyPress = true;

            // Ignore modifier-only presses
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LMenu
                or Keys.RMenu or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey
                or Keys.RShiftKey or Keys.LWin or Keys.RWin)
                return;

            // Require at least one modifier
            if (!e.Control && !e.Shift && !e.Alt)
            {
                hotkeyStatusLabel.ForeColor = currentTheme.ErrorColor;
                hotkeyStatusLabel.Text = "At least one modifier (Ctrl, Shift, Alt) is required";
                return;
            }

            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            parts.Add(e.KeyCode.ToString());

            string hotkey = string.Join("+", parts);
            hotkeyBox.Text = hotkey;
            hotkeyBox.ForeColor = currentTheme.Primary;
            hotkeyRecording = false;
            hotkeyRecordButton.Text = "Change";

            var settings = AppSettings.Load();
            AppSettings.Save(settings with { ScreenshotHotkey = hotkey });
            hotkeyStatusLabel.ForeColor = currentTheme.SuccessColor;
            hotkeyStatusLabel.Text = "Hotkey saved — active now";
            ScreenshotHotkeyChanged?.Invoke(hotkey);
        };

        hotkeyClearButton.Click += (_, _) =>
        {
            hotkeyRecording = false;
            hotkeyBox.Text = "(none)";
            hotkeyBox.ForeColor = currentTheme.TextSecondary;
            hotkeyRecordButton.Text = "Change";

            var settings = AppSettings.Load();
            AppSettings.Save(settings with { ScreenshotHotkey = "" });
            hotkeyStatusLabel.ForeColor = currentTheme.TextSecondary;
            hotkeyStatusLabel.Text = "Hotkey cleared";
            ScreenshotHotkeyChanged?.Invoke("");
        };

        if (string.IsNullOrEmpty(currentHotkey))
        {
            hotkeyBox.Text = "(none)";
            hotkeyBox.ForeColor = currentTheme.TextSecondary;
        }

        capturePage.Controls.Add(hotkeyBox);
        capturePage.Controls.Add(hotkeyRecordButton);
        capturePage.Controls.Add(hotkeyClearButton);
        capturePage.Controls.Add(hotkeyStatusLabel);
        captureHotkeyControls.AddRange(new Control[] { hotkeyBox, hotkeyRecordButton, hotkeyClearButton, hotkeyStatusLabel });

        // Set initial enabled state for hotkey controls
        foreach (var ctrl in captureHotkeyControls)
            ctrl.Enabled = captureEnabled;

        enableCaptureCheck.CheckedChanged += (_, _) =>
        {
            bool enabled = enableCaptureCheck.Checked;
            foreach (var ctrl in captureHotkeyControls)
                ctrl.Enabled = enabled;

            var s = AppSettings.Load();
            AppSettings.Save(s with { ScreenshotEnabled = enabled });
            ScreenshotEnabledChanged?.Invoke(enabled);
        };

        // =============================================
        // TAB 2: Appearance
        // =============================================
        var appearancePage = CreateTabPage("Appearance", currentTheme);
        _tabControl.TabPages.Add(appearancePage);

        int ay = tp;

        var themeSectionLabel = CreateSectionLabel("THEME", tp, ay);
        appearancePage.Controls.Add(themeSectionLabel);
        ay += 28;

        _themeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10f),
            BackColor = currentTheme.BgHeader,
            ForeColor = currentTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(tabInner, 28),
            Location = new Point(tp, ay),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 30,
        };

        int selectedIndex = 0;
        for (int i = 0; i < Themes.All.Length; i++)
        {
            _themeCombo.Items.Add(Themes.All[i]);
            if (Themes.All[i].Name == currentTheme.Name)
                selectedIndex = i;
        }

        _themeCombo.DrawItem += OnThemeComboDrawItem;
        _themeCombo.SelectedIndexChanged += OnThemeComboChanged;
        appearancePage.Controls.Add(_themeCombo);
        ay += 40;

        _themeNameLabel = new Label
        {
            Text = currentTheme.Name,
            Font = new Font("Segoe UI", 10f),
            ForeColor = currentTheme.Primary,
            AutoSize = true,
            Location = new Point(tp, ay),
            BackColor = Color.Transparent,
        };
        appearancePage.Controls.Add(_themeNameLabel);

        _themePreview = new Panel
        {
            BackColor = currentTheme.Primary,
            Location = new Point(tp, ay + 24),
            Size = new Size(tabInner, 4),
        };
        appearancePage.Controls.Add(_themePreview);

        // =============================================
        // TAB 3: Claude CLI
        // =============================================
        var claudePage = CreateTabPage("Claude CLI", currentTheme);
        _tabControl.TabPages.Add(claudePage);

        int cy = tp;

        // --- Conversations main heading ---
        var conversationsLabel = CreateSectionLabel("CONVERSATIONS", tp, cy);
        claudePage.Controls.Add(conversationsLabel);
        cy += 28;

        // --- Notifications sub-group ---
        var notifLabel = CreateSubSectionLabel("Notifications", tp, cy, currentTheme);
        claudePage.Controls.Add(notifLabel);
        cy += 22;

        bool notifEnabled = AppSettings.Load().NotificationsEnabled;
        var notifEnabledCheck = new CheckBox
        {
            Text = "Enable notifications",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = notifEnabled,
            AutoSize = true,
            Location = new Point(tp + 8, cy),
            Cursor = Cursors.Hand,
        };
        claudePage.Controls.Add(notifEnabledCheck);
        cy += 28;

        var notifSubControls = new List<Control>();

        var notifModeLabel = new Label
        {
            Text = "Notification type:",
            Font = new Font("Segoe UI", 9f),
            ForeColor = currentTheme.TextPrimary,
            AutoSize = true,
            Enabled = notifEnabled,
            Location = new Point(tp + 8, cy + 3),
            BackColor = Color.Transparent,
        };
        claudePage.Controls.Add(notifModeLabel);
        notifSubControls.Add(notifModeLabel);

        var notifModes = new[] { "Popup", "Windows", "Popup + Windows" };
        var currentMode = AppSettings.Load().NotificationMode;
        var notifModeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 9f),
            BackColor = currentTheme.BgHeader,
            ForeColor = currentTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(140, 24),
            Enabled = notifEnabled,
            Location = new Point(tp + 130, cy),
        };
        foreach (var mode in notifModes)
            notifModeCombo.Items.Add(mode);
        notifModeCombo.SelectedItem = notifModes.Contains(currentMode) ? currentMode : "Popup";
        notifModeCombo.SelectedIndexChanged += (_, _) =>
        {
            var s = AppSettings.Load();
            AppSettings.Save(s with { NotificationMode = notifModeCombo.SelectedItem?.ToString() ?? "Popup" });
        };
        claudePage.Controls.Add(notifModeCombo);
        notifSubControls.Add(notifModeCombo);
        cy += 30;

        var quotesCheck = new CheckBox
        {
            Text = "Show quotes in popup header",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = AppSettings.Load().ShowQuotes,
            Enabled = notifEnabled,
            AutoSize = true,
            Location = new Point(tp + 8, cy),
            Cursor = Cursors.Hand,
        };
        quotesCheck.CheckedChanged += (_, _) =>
        {
            var settings = AppSettings.Load();
            AppSettings.Save(settings with { ShowQuotes = quotesCheck.Checked });
            ShowQuotesChanged?.Invoke(quotesCheck.Checked);
        };
        claudePage.Controls.Add(quotesCheck);
        notifSubControls.Add(quotesCheck);
        cy += 28;

        bool isSnoozed = DateTime.Now < snoozeUntil;
        var snoozeCheck = new CheckBox
        {
            Text = isSnoozed
                ? $"Snoozed ({Math.Max(1, (int)(snoozeUntil - DateTime.Now).TotalMinutes)} min left)"
                : "Snooze notifications (30 min)",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = isSnoozed,
            Enabled = notifEnabled,
            AutoSize = true,
            Location = new Point(tp + 8, cy),
            Cursor = Cursors.Hand,
        };

        var _snoozeUntil = snoozeUntil;
        var snoozeTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        snoozeTimer.Tick += (_, _) =>
        {
            if (DateTime.Now >= _snoozeUntil && snoozeCheck.Checked)
            {
                snoozeCheck.Checked = false;
                snoozeCheck.Text = "Snooze notifications (30 min)";
                SnoozeChanged?.Invoke(false);
                snoozeTimer.Stop();
            }
            else if (snoozeCheck.Checked && DateTime.Now < _snoozeUntil)
            {
                int mins = Math.Max(1, (int)(_snoozeUntil - DateTime.Now).TotalMinutes);
                snoozeCheck.Text = $"Snoozed ({mins} min left)";
            }
        };
        if (isSnoozed) snoozeTimer.Start();

        snoozeCheck.CheckedChanged += (_, _) =>
        {
            if (snoozeCheck.Checked)
            {
                _snoozeUntil = DateTime.Now.AddMinutes(30);
                snoozeCheck.Text = "Snoozed (30 min left)";
                snoozeTimer.Start();
            }
            else
            {
                _snoozeUntil = DateTime.MinValue;
                snoozeCheck.Text = "Snooze notifications (30 min)";
                snoozeTimer.Stop();
            }
            SnoozeChanged?.Invoke(snoozeCheck.Checked);
        };

        FormClosed += (_, _) => { snoozeTimer.Stop(); snoozeTimer.Dispose(); };

        claudePage.Controls.Add(snoozeCheck);
        notifSubControls.Add(snoozeCheck);

        notifEnabledCheck.CheckedChanged += (_, _) =>
        {
            var s = AppSettings.Load();
            AppSettings.Save(s with { NotificationsEnabled = notifEnabledCheck.Checked });
            foreach (var ctrl in notifSubControls)
                ctrl.Enabled = notifEnabledCheck.Checked;
        };
        cy += 28;

        // --- Chats sub-group ---
        cy += 4;
        var chatsLabel = CreateSubSectionLabel("Chats", tp, cy, currentTheme);
        claudePage.Controls.Add(chatsLabel);
        cy += 22;

        var historyCheck = new CheckBox
        {
            Text = "Save chat history",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = ResponseHistory.IsEnabled,
            AutoSize = true,
            Location = new Point(tp + 8, cy),
            Cursor = Cursors.Hand,
        };
        historyCheck.CheckedChanged += (_, _) =>
        {
            ResponseHistory.IsEnabled = historyCheck.Checked;
            HistoryEnabledChanged?.Invoke(historyCheck.Checked);
        };
        claudePage.Controls.Add(historyCheck);
        cy += 30;

        // --- Status Line group ---
        claudePage.Controls.Add(CreateSeparator(tp, cy, tabInner));
        cy += 10;

        var statusLineLabel = CreateSectionLabel("STATUS LINE", tp, cy);
        claudePage.Controls.Add(statusLineLabel);
        cy += 26;

        var slCheckboxes = new List<CheckBox>();

        var statusLineCheck = new CheckBox
        {
            Text = "Enable Claude Code status line",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = ClaudeStatusLine.IsEnabled(),
            AutoSize = true,
            Location = new Point(tp, cy),
            Cursor = Cursors.Hand,
        };

        var statusLineHint = new Label
        {
            Text = "Shows model, branch, context usage, edit stats in Claude CLI",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(tp + 2, cy + 22),
            BackColor = Color.Transparent,
        };

        var statusLineStatus = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = currentTheme.SuccessColor,
            AutoSize = true,
            Location = new Point(tp + 2, cy + 40),
            BackColor = Color.Transparent,
        };

        statusLineCheck.CheckedChanged += (_, _) =>
        {
            try
            {
                if (statusLineCheck.Checked)
                {
                    ClaudeStatusLine.Enable();
                    statusLineStatus.ForeColor = currentTheme.SuccessColor;
                    statusLineStatus.Text = "Enabled — restart Claude Code to apply";
                }
                else
                {
                    ClaudeStatusLine.Disable();
                    statusLineStatus.ForeColor = currentTheme.TextSecondary;
                    statusLineStatus.Text = "Disabled — restart Claude Code to apply";
                }
                // Enable/disable sub-checkboxes
                foreach (var slCb in slCheckboxes)
                    slCb.Enabled = statusLineCheck.Checked;
            }
            catch (Exception ex)
            {
                statusLineStatus.ForeColor = currentTheme.ErrorColor;
                statusLineStatus.Text = $"Error: {ex.Message}";
            }
        };

        claudePage.Controls.Add(statusLineCheck);
        claudePage.Controls.Add(statusLineHint);
        claudePage.Controls.Add(statusLineStatus);
        cy += 62;

        // Status line item checkboxes
        var slItems = new (string Label, string Setting, bool Default)[]
        {
            ("Model", "SlShowModel", true),
            ("Directory", "SlShowDir", true),
            ("Branch", "SlShowBranch", true),
            ("Prompts", "SlShowPrompts", true),
            ("Context %", "SlShowContext", true),
            ("Duration", "SlShowDuration", true),
            ("Mode", "SlShowMode", true),
            ("Version", "SlShowVersion", true),
            ("Edit Stats", "SlShowEditStats", true),
        };

        var slSettings = AppSettings.Load();
        int colWidth = tabInner / 3;
        for (int i = 0; i < slItems.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            var item = slItems[i];

            // Read current value via reflection
            var prop = typeof(AppSettingsData).GetProperty(item.Setting);
            bool isChecked = prop != null ? (bool)prop.GetValue(slSettings)! : item.Default;

            bool slEnabled = statusLineCheck.Checked;
            var cb = new CheckBox
            {
                Text = item.Label,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = currentTheme.TextPrimary,
                BackColor = Color.Transparent,
                Checked = isChecked,
                Enabled = slEnabled,
                AutoSize = true,
                Location = new Point(tp + col * colWidth, cy + row * 22),
                Cursor = Cursors.Hand,
            };
            slCheckboxes.Add(cb);
            string settingName = item.Setting;
            cb.CheckedChanged += (_, _) =>
            {
                var s = AppSettings.Load();
                // Use reflection to set the property via with expression workaround
                s = settingName switch
                {
                    "SlShowModel" => s with { SlShowModel = cb.Checked },
                    "SlShowDir" => s with { SlShowDir = cb.Checked },
                    "SlShowBranch" => s with { SlShowBranch = cb.Checked },
                    "SlShowPrompts" => s with { SlShowPrompts = cb.Checked },
                    "SlShowContext" => s with { SlShowContext = cb.Checked },
                    "SlShowDuration" => s with { SlShowDuration = cb.Checked },
                    "SlShowMode" => s with { SlShowMode = cb.Checked },
                    "SlShowVersion" => s with { SlShowVersion = cb.Checked },
                    "SlShowEditStats" => s with { SlShowEditStats = cb.Checked },
                    _ => s,
                };
                AppSettings.Save(s);
                ClaudeStatusLine.WriteConfig();
            };
            claudePage.Controls.Add(cb);
        }
        cy += (slItems.Length / 3 + 1) * 22 + 4;

        // =============================================
        // TAB 4: Advanced
        // =============================================
        var advancedPage = CreateTabPage("Advanced", currentTheme);
        _tabControl.TabPages.Add(advancedPage);
        int uy = tp;

        var uninstallSectionLabel = CreateSectionLabel("UNINSTALL", tp, uy);
        advancedPage.Controls.Add(uninstallSectionLabel);
        uy += 28;

        var uninstallDesc = new Label
        {
            Text = "Remove DevToy from the tools folder and clean up hook\nentries from Claude Code settings. Your response history\nand app settings will be preserved.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(tp, uy),
            BackColor = Color.Transparent,
        };
        advancedPage.Controls.Add(uninstallDesc);
        uy += 60;

        var uninstallButton = new RoundedButton
        {
            Text = "Uninstall DevToy",
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Size = new Size(200, 34),
            Location = new Point(tp, uy),
            FlatStyle = FlatStyle.Flat,
            BackColor = currentTheme.ErrorColor,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
        };
        uninstallButton.FlatAppearance.BorderSize = 0;
        uninstallButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Min(255, currentTheme.ErrorColor.R + 30),
            Math.Min(255, currentTheme.ErrorColor.G + 10),
            Math.Min(255, currentTheme.ErrorColor.B + 10));
        uninstallButton.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(this,
                "This will remove DevToy from the tools folder and remove the hook entries from Claude Code settings.\n\n" +
                "Your response history and app settings will be kept.\n\n" +
                "Are you sure you want to uninstall?",
                "Confirm Uninstall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;

            var result = Uninstaller.Run(out string? cleanupBatPath);
            if (result.Success)
            {
                MessageBox.Show(this,
                    result.Message + "\n\nThe application will now close.",
                    "Uninstall Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                if (cleanupBatPath != null)
                    Uninstaller.LaunchCleanupScript(cleanupBatPath);

                UninstallRequested?.Invoke();
            }
            else
            {
                MessageBox.Show(this,
                    result.Message,
                    "Uninstall Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        advancedPage.Controls.Add(uninstallButton);

        // =============================================
        // TAB 5: About
        // =============================================
        var aboutPage = CreateTabPage("About", currentTheme);
        _tabControl.TabPages.Add(aboutPage);
        int ab = tp;

        // --- Version info ---
        var aboutVersionLabel = new Label
        {
            Text = $"DevToy v{AppVersion.Current}",
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            ForeColor = currentTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(tp, ab),
            BackColor = Color.Transparent,
        };
        aboutPage.Controls.Add(aboutVersionLabel);
        ab += 34;

        var aboutDescLabel = new Label
        {
            Text = "Developer utility toolkit for Windows",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(tp, ab),
            BackColor = Color.Transparent,
        };
        aboutPage.Controls.Add(aboutDescLabel);
        ab += 32;

        // --- Separator ---
        aboutPage.Controls.Add(CreateSeparator(tp, ab, tabInner));
        ab += 18;

        // --- Updates Section ---
        var updateSectionLabel = CreateSectionLabel("UPDATES", tp, ab);
        aboutPage.Controls.Add(updateSectionLabel);
        ab += 24;

        var updatePathLabel = new Label
        {
            Text = "Update location (network path):",
            Font = new Font("Segoe UI", 9f),
            ForeColor = currentTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(tp, ab),
            BackColor = Color.Transparent,
        };
        aboutPage.Controls.Add(updatePathLabel);
        ab += 22;

        var updatePathBox = new TextBox
        {
            Text = AppSettings.Load().UpdateLocation,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = currentTheme.BgHeader,
            BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(tabInner - 40, 26),
            Location = new Point(tp, ab),
        };
        updatePathBox.LostFocus += (_, _) =>
        {
            var settings = AppSettings.Load();
            AppSettings.Save(settings with { UpdateLocation = updatePathBox.Text.Trim() });
        };
        aboutPage.Controls.Add(updatePathBox);

        var savePathButton = new RoundedButton
        {
            Text = "Save",
            Font = new Font("Segoe UI", 8.5f),
            Size = new Size(34, 26),
            Location = new Point(tp + tabInner - 34, ab),
            FlatStyle = FlatStyle.Flat,
            BackColor = currentTheme.PrimaryDim,
            ForeColor = currentTheme.TextSecondary,
            Cursor = Cursors.Hand,
        };
        savePathButton.FlatAppearance.BorderSize = 0;
        savePathButton.FlatAppearance.MouseOverBackColor = currentTheme.Primary;
        savePathButton.Click += (_, _) =>
        {
            var settings = AppSettings.Load();
            AppSettings.Save(settings with { UpdateLocation = updatePathBox.Text.Trim() });
            savePathButton.Text = "\u2713";
            var resetTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            resetTimer.Tick += (_, _) => { savePathButton.Text = "Save"; resetTimer.Stop(); resetTimer.Dispose(); };
            resetTimer.Start();
        };
        aboutPage.Controls.Add(savePathButton);
        ab += 34;

        var checkNowButton = new RoundedButton
        {
            Text = "Check for Updates",
            Font = new Font("Segoe UI", 8.5f),
            Size = new Size(140, 28),
            Location = new Point(tp, ab),
            FlatStyle = FlatStyle.Flat,
            BackColor = currentTheme.PrimaryDim,
            ForeColor = currentTheme.TextSecondary,
            Cursor = Cursors.Hand,
        };
        checkNowButton.FlatAppearance.BorderSize = 0;
        checkNowButton.FlatAppearance.MouseOverBackColor = currentTheme.Primary;

        var checkResultLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(tp + 150, ab + 5),
            BackColor = Color.Transparent,
        };

        var updateLinkLabel = new Label
        {
            Text = "Install Update",
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Underline | FontStyle.Bold),
            ForeColor = currentTheme.Primary,
            BackColor = Color.Transparent,
            AutoSize = true,
            Cursor = Cursors.Hand,
            Visible = false,
        };
        updateLinkLabel.Click += (_, _) =>
        {
            updateLinkLabel.Text = "Updating...";
            updateLinkLabel.Enabled = false;
            var result = Updater.Apply();
            if (result.Success)
            {
                Application.Exit();
            }
            else
            {
                MessageBox.Show(this, result.Message, "Update Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                updateLinkLabel.Text = "Install Update";
                updateLinkLabel.Enabled = true;
            }
        };

        checkNowButton.Click += (_, _) =>
        {
            var settings = AppSettings.Load();
            AppSettings.Save(settings with { UpdateLocation = updatePathBox.Text.Trim() });

            checkNowButton.Enabled = false;
            checkNowButton.Text = "Checking...";
            checkResultLabel.Text = "";
            updateLinkLabel.Visible = false;

            UpdateChecker.CheckNow();
            var meta = UpdateChecker.LatestMetadata;
            if (meta != null)
            {
                checkResultLabel.ForeColor = currentTheme.SuccessColor;
                checkResultLabel.Text = $"v{meta.Version} available";
                updateLinkLabel.Visible = true;
                updateLinkLabel.Location = new Point(
                    checkResultLabel.Left + checkResultLabel.PreferredWidth + 10,
                    checkResultLabel.Top);
            }
            else
            {
                checkResultLabel.ForeColor = currentTheme.TextSecondary;
                checkResultLabel.Text = "You are up to date.";
            }

            checkNowButton.Enabled = true;
            checkNowButton.Text = "Check for Updates";
        };
        aboutPage.Controls.Add(checkNowButton);
        aboutPage.Controls.Add(checkResultLabel);
        aboutPage.Controls.Add(updateLinkLabel);

        // --- Bottom version label on form ---
        _versionLabel = new Label
        {
            Text = $"DevToy v{AppVersion.Current}",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(60, 75, 105),
            AutoSize = true,
            Location = new Point(leftMargin, ClientSize.Height - 30),
            BackColor = Color.Transparent,
        };
        Controls.Add(_versionLabel);

        // Set initial selection
        _themeCombo.SelectedIndex = selectedIndex;
    }

    private static TabPage CreateTabPage(string text, PopupTheme theme)
    {
        return new TabPage(text)
        {
            BackColor = theme.BgDark,
            ForeColor = theme.TextPrimary,
        };
    }

    private void OnThemeComboDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var theme = (PopupTheme)_themeCombo.Items[e.Index];

        bool selected = (e.State & DrawItemState.Selected) != 0;
        var bgColor = selected ? _currentTheme.Primary : _currentTheme.BgHeader;
        var txtColor = selected ? Color.White : _currentTheme.TextPrimary;

        using (var bgBrush = new SolidBrush(bgColor))
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int circleSize = 18;
        int circleY = e.Bounds.Y + (e.Bounds.Height - circleSize) / 2;
        var circleRect = new Rectangle(e.Bounds.X + 8, circleY, circleSize, circleSize);

        bool isLight = theme.BgDark.GetBrightness() > 0.5f;
        var fillColor = isLight ? theme.BgDark : theme.Primary;
        using (var brush = new SolidBrush(fillColor))
            g.FillEllipse(brush, circleRect);

        if (isLight)
        {
            using var borderPen = new Pen(theme.Border, 1f);
            g.DrawEllipse(borderPen, circleRect);
        }

        using var textBrush = new SolidBrush(txtColor);
        var textRect = new Rectangle(e.Bounds.X + 34, e.Bounds.Y, e.Bounds.Width - 34, e.Bounds.Height);
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(theme.Name, e.Font ?? Font, textBrush, textRect, sf);
    }

    private void OnThemeComboChanged(object? sender, EventArgs e)
    {
        if (_themeCombo.SelectedItem is not PopupTheme theme) return;

        _currentTheme = theme;

        _themeNameLabel.Text = theme.Name;
        _themeNameLabel.ForeColor = theme.Primary;
        _themePreview.BackColor = theme.Primary;

        ApplyThemeToForm(theme);

        Themes.Save(theme);
        ThemeChanged?.Invoke(theme);
    }

    private void ApplyThemeToForm(PopupTheme theme)
    {
        SuspendLayout();

        BackColor = theme.BgDark;
        ForeColor = theme.TextPrimary;
        Icon = Themes.CreateAppIcon(theme.Primary);

        _titleLabel.ForeColor = theme.TextPrimary;
        _accentLine.BackColor = theme.Primary;

        _tabControl.Theme = theme;
        _tabControl.Invalidate();

        foreach (TabPage page in _tabControl.TabPages)
        {
            page.BackColor = theme.BgDark;
            page.ForeColor = theme.TextPrimary;
            RecolorControls(page.Controls, theme);
        }

        _themeCombo.BackColor = theme.BgHeader;
        _themeCombo.ForeColor = theme.TextPrimary;
        _themeCombo.Invalidate();

        ResumeLayout();
        Invalidate(true);
    }

    private static void RecolorControls(Control.ControlCollection controls, PopupTheme theme)
    {
        foreach (Control c in controls)
        {
            switch (c)
            {
                case Label lbl when lbl.Font.Bold && lbl.Font.Size < 10:
                    lbl.ForeColor = theme.TextSecondary;
                    break;
                case Label lbl:
                    if (lbl.ForeColor != theme.Primary)
                        lbl.ForeColor = lbl.Font.Size <= 8.5f ? theme.TextSecondary : theme.TextPrimary;
                    break;
                case CheckBox cb:
                    cb.ForeColor = theme.TextPrimary;
                    break;
                case TextBox tb:
                    tb.BackColor = theme.BgHeader;
                    tb.ForeColor = theme.TextPrimary;
                    break;
                case RoundedButton btn:
                    if (btn.ForeColor != Color.White)
                    {
                        btn.BackColor = theme.PrimaryDim;
                        btn.ForeColor = theme.TextSecondary;
                        btn.FlatAppearance.MouseOverBackColor = theme.Primary;
                    }
                    break;
                case Panel panel when panel.Height == 1:
                    panel.BackColor = theme.Border;
                    break;
            }
        }
    }

    private static Label CreateSubSectionLabel(string text, int x, int y, PopupTheme theme)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = theme.TextSecondary,
            AutoSize = true,
            Location = new Point(x + 4, y),
            BackColor = Color.Transparent,
        };
    }

    private Label CreateSectionLabel(string text, int x, int y)
    {
        // Add letter spacing for better readability
        string spaced = string.Join(" ", text.ToCharArray());
        return new Label
        {
            Text = spaced,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            ForeColor = _currentTheme.Primary,
            AutoSize = true,
            Location = new Point(x, y),
            BackColor = Color.Transparent,
        };
    }

    private Panel CreateSeparator(int x, int y, int width)
    {
        return new Panel
        {
            BackColor = _currentTheme.Border,
            Location = new Point(x, y),
            Size = new Size(width, 1),
        };
    }
}

/// <summary>
/// Owner-drawn TabControl that renders tabs with theme colors.
/// </summary>
class ThemedTabControl : TabControl
{
    public PopupTheme Theme { get; set; }

    public ThemedTabControl(PopupTheme theme)
    {
        Theme = theme;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bgBrush = new SolidBrush(Theme.BgDark))
            g.FillRectangle(bgBrush, ClientRectangle);

        if (TabCount == 0) return;

        var pageRect = GetPageBounds();
        using (var borderPen = new Pen(Theme.Border, 1f))
            g.DrawRectangle(borderPen, pageRect);

        for (int i = 0; i < TabCount; i++)
        {
            var tabRect = GetTabRect(i);
            bool isActive = SelectedIndex == i;

            if (isActive)
            {
                using (var activeBrush = new SolidBrush(Theme.BgDark))
                    g.FillRectangle(activeBrush, tabRect);

                using (var accentPen = new Pen(Theme.Primary, 2.5f))
                    g.DrawLine(accentPen, tabRect.Left + 2, tabRect.Top + 1, tabRect.Right - 2, tabRect.Top + 1);

                using var borderPen = new Pen(Theme.Border, 1f);
                g.DrawLine(borderPen, tabRect.Left, tabRect.Top, tabRect.Left, tabRect.Bottom);
                g.DrawLine(borderPen, tabRect.Right, tabRect.Top, tabRect.Right, tabRect.Bottom);
                g.DrawLine(borderPen, tabRect.Left, tabRect.Top, tabRect.Right, tabRect.Top);

                using (var erasePen = new Pen(Theme.BgDark, 2f))
                    g.DrawLine(erasePen, tabRect.Left + 1, tabRect.Bottom, tabRect.Right - 1, tabRect.Bottom);
            }
            else
            {
                using (var inactiveBrush = new SolidBrush(Theme.BgHeader))
                    g.FillRectangle(inactiveBrush, tabRect.X, tabRect.Y + 2, tabRect.Width, tabRect.Height - 2);

                using var borderPen = new Pen(Theme.Border, 1f);
                g.DrawLine(borderPen, tabRect.Left, tabRect.Bottom, tabRect.Right, tabRect.Bottom);
            }

            var textColor = isActive ? Theme.TextPrimary : Theme.TextSecondary;
            using var textBrush = new SolidBrush(textColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(TabPages[i].Text, Font, textBrush, tabRect, sf);
        }
    }

    private Rectangle GetPageBounds()
    {
        if (TabCount == 0) return ClientRectangle;
        var firstTab = GetTabRect(0);
        int tabStripHeight = firstTab.Bottom;
        return new Rectangle(0, tabStripHeight, Width - 1, Height - tabStripHeight - 1);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }
}
