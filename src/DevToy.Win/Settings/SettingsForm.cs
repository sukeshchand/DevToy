using System.Drawing;
using System.Drawing.Drawing2D;

namespace DevToy;

class SettingsForm : Form
{
    private PopupTheme _currentTheme;
    private readonly Panel _themePreview;
    private readonly Label _themeNameLabel;
    private readonly List<RoundedButton> _tabButtons = new();
    private readonly List<Panel> _tabPanels = new();

    public event Action<PopupTheme>? ThemeChanged;
    public event Action<bool>? HistoryEnabledChanged;
    public event Action<bool>? SnoozeChanged;
    public event Action<bool>? ShowQuotesChanged;
    public event Action? UninstallRequested;

    public SettingsForm(PopupTheme currentTheme, DateTime snoozeUntil)
    {
        _currentTheme = currentTheme;

        Text = "DevToy Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 460);
        BackColor = currentTheme.BgDark;
        ForeColor = currentTheme.TextPrimary;
        Font = new Font("Segoe UI", 10f);
        Icon = Themes.CreateAppIcon(currentTheme.Primary);

        int leftMargin = 28;
        int contentWidth = ClientSize.Width - leftMargin * 2;

        // --- Title ---
        int y = 16;
        var titleLabel = new Label
        {
            Text = "Settings",
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = currentTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(leftMargin, y),
            BackColor = Color.Transparent,
        };
        Controls.Add(titleLabel);
        y += 40;

        // --- Tab bar ---
        var tabBar = new Panel
        {
            Location = new Point(leftMargin, y),
            Size = new Size(contentWidth, 34),
            BackColor = Color.Transparent,
        };
        Controls.Add(tabBar);

        string[] tabNames = ["Appearance", "General", "Advanced"];
        int tabWidth = 130;
        int tabSpacing = 6;
        for (int i = 0; i < tabNames.Length; i++)
        {
            var tabBtn = new RoundedButton
            {
                Text = tabNames[i],
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                Size = new Size(tabWidth, 30),
                Location = new Point(i * (tabWidth + tabSpacing), 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = i == 0 ? currentTheme.Primary : currentTheme.BgHeader,
                ForeColor = i == 0 ? Color.White : currentTheme.TextSecondary,
                Cursor = Cursors.Hand,
                Tag = i,
            };
            tabBtn.FlatAppearance.BorderSize = 0;
            tabBtn.FlatAppearance.MouseOverBackColor = i == 0 ? currentTheme.PrimaryLight : currentTheme.Border;
            tabBtn.Click += OnTabClick;
            tabBar.Controls.Add(tabBtn);
            _tabButtons.Add(tabBtn);
        }
        y += 42;

        // --- Accent line under tabs ---
        var accentLine = new Panel
        {
            BackColor = currentTheme.Primary,
            Location = new Point(leftMargin, y),
            Size = new Size(contentWidth, 2),
        };
        Controls.Add(accentLine);
        y += 10;

        int tabTop = y;
        int tabContentHeight = 340;

        // =============================================
        // TAB 0: Appearance
        // =============================================
        var appearancePanel = CreateTabPanel(tabTop, tabContentHeight, contentWidth, leftMargin);
        int ay = 14;

        // --- Theme Section ---
        var themeSectionLabel = CreateSectionLabel("THEME", leftMargin, ay);
        appearancePanel.Controls.Add(themeSectionLabel);
        ay += 24;

        int circleSize = 36;
        int circleSpacing = 8;
        int circlesPerRow = Themes.All.Length;

        for (int i = 0; i < Themes.All.Length; i++)
        {
            var theme = Themes.All[i];
            int col = i % circlesPerRow;
            int row = i / circlesPerRow;

            var btn = new ThemeCircleButton
            {
                Theme = theme,
                IsSelected = theme.Name == currentTheme.Name,
                Size = new Size(circleSize, circleSize),
                Location = new Point(leftMargin + col * (circleSize + circleSpacing), ay + row * (circleSize + circleSpacing)),
                Cursor = Cursors.Hand,
            };
            btn.Click += OnThemeCircleClick;
            appearancePanel.Controls.Add(btn);
        }

        int themeRows = (Themes.All.Length + circlesPerRow - 1) / circlesPerRow;
        ay += themeRows * (circleSize + circleSpacing) + 4;

        _themeNameLabel = new Label
        {
            Text = currentTheme.Name,
            Font = new Font("Segoe UI", 10f),
            ForeColor = currentTheme.Primary,
            AutoSize = true,
            Location = new Point(leftMargin, ay),
            BackColor = Color.Transparent,
        };
        appearancePanel.Controls.Add(_themeNameLabel);

        _themePreview = new Panel
        {
            BackColor = currentTheme.Primary,
            Location = new Point(leftMargin, ay + 24),
            Size = new Size(contentWidth, 4),
        };
        appearancePanel.Controls.Add(_themePreview);
        ay += 48;

        // --- Separator ---
        appearancePanel.Controls.Add(CreateSeparator(leftMargin, ay, contentWidth));
        ay += 18;

        // --- Show quotes toggle ---
        var quotesCheck = new CheckBox
        {
            Text = "Show quotes in header",
            Font = new Font("Segoe UI", 10f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = AppSettings.Load().ShowQuotes,
            AutoSize = true,
            Location = new Point(leftMargin, ay),
            Cursor = Cursors.Hand,
        };
        quotesCheck.CheckedChanged += (_, _) =>
        {
            var settings = AppSettings.Load();
            AppSettings.Save(settings with { ShowQuotes = quotesCheck.Checked });
            ShowQuotesChanged?.Invoke(quotesCheck.Checked);
        };
        appearancePanel.Controls.Add(quotesCheck);

        var quotesDesc = new Label
        {
            Text = "Displays a random programmer quote with typewriter animation",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(leftMargin + 20, ay + 24),
            BackColor = Color.Transparent,
        };
        appearancePanel.Controls.Add(quotesDesc);

        Controls.Add(appearancePanel);
        _tabPanels.Add(appearancePanel);

        // =============================================
        // TAB 1: General
        // =============================================
        var generalPanel = CreateTabPanel(tabTop, tabContentHeight, contentWidth, leftMargin);
        generalPanel.Visible = false;
        int gy = 14;

        // --- Notifications Section ---
        var notifSectionLabel = CreateSectionLabel("NOTIFICATIONS", leftMargin, gy);
        generalPanel.Controls.Add(notifSectionLabel);
        gy += 28;

        // History toggle
        var historyCheck = new CheckBox
        {
            Text = "Save response history",
            Font = new Font("Segoe UI", 10f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = ResponseHistory.IsEnabled,
            AutoSize = true,
            Location = new Point(leftMargin, gy),
            Cursor = Cursors.Hand,
        };
        historyCheck.CheckedChanged += (_, _) =>
        {
            ResponseHistory.IsEnabled = historyCheck.Checked;
            HistoryEnabledChanged?.Invoke(historyCheck.Checked);
        };
        generalPanel.Controls.Add(historyCheck);

        var historyDesc = new Label
        {
            Text = "Keeps a daily log of responses in _data/history/",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(leftMargin + 20, gy + 24),
            BackColor = Color.Transparent,
        };
        generalPanel.Controls.Add(historyDesc);
        gy += 56;

        // Snooze toggle
        bool isSnoozed = DateTime.Now < snoozeUntil;
        var snoozeCheck = new CheckBox
        {
            Text = isSnoozed
                ? $"Snoozed ({Math.Max(1, (int)(snoozeUntil - DateTime.Now).TotalMinutes)} min left)"
                : "Snooze notifications (30 minutes)",
            Font = new Font("Segoe UI", 10f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = Color.Transparent,
            Checked = isSnoozed,
            AutoSize = true,
            Location = new Point(leftMargin, gy),
            Cursor = Cursors.Hand,
        };

        var snoozeDesc = new Label
        {
            Text = "Suppresses popup windows while snoozed",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(leftMargin + 20, gy + 24),
            BackColor = Color.Transparent,
        };

        var _snoozeUntil = snoozeUntil;
        var snoozeTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        snoozeTimer.Tick += (_, _) =>
        {
            if (DateTime.Now >= _snoozeUntil && snoozeCheck.Checked)
            {
                snoozeCheck.Checked = false;
                snoozeCheck.Text = "Snooze notifications (30 minutes)";
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
                int mins = 30;
                snoozeCheck.Text = $"Snoozed ({mins} min left)";
                snoozeTimer.Start();
            }
            else
            {
                _snoozeUntil = DateTime.MinValue;
                snoozeCheck.Text = "Snooze notifications (30 minutes)";
                snoozeTimer.Stop();
            }
            SnoozeChanged?.Invoke(snoozeCheck.Checked);
        };

        FormClosed += (_, _) => { snoozeTimer.Stop(); snoozeTimer.Dispose(); };

        generalPanel.Controls.Add(snoozeCheck);
        generalPanel.Controls.Add(snoozeDesc);
        gy += 56;

        // --- Separator ---
        generalPanel.Controls.Add(CreateSeparator(leftMargin, gy, contentWidth));
        gy += 18;

        // --- Updates Section ---
        var updateSectionLabel = CreateSectionLabel("UPDATES", leftMargin, gy);
        generalPanel.Controls.Add(updateSectionLabel);
        gy += 24;

        var updatePathLabel = new Label
        {
            Text = "Update location (network path):",
            Font = new Font("Segoe UI", 9f),
            ForeColor = currentTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(leftMargin, gy),
            BackColor = Color.Transparent,
        };
        generalPanel.Controls.Add(updatePathLabel);
        gy += 22;

        var updatePathBox = new TextBox
        {
            Text = AppSettings.Load().UpdateLocation,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = currentTheme.TextPrimary,
            BackColor = currentTheme.BgHeader,
            BorderStyle = BorderStyle.FixedSingle,
            Size = new Size(contentWidth - 40, 26),
            Location = new Point(leftMargin, gy),
        };
        updatePathBox.LostFocus += (_, _) =>
        {
            var settings = AppSettings.Load();
            AppSettings.Save(settings with { UpdateLocation = updatePathBox.Text.Trim() });
        };
        generalPanel.Controls.Add(updatePathBox);

        var savePathButton = new RoundedButton
        {
            Text = "Save",
            Font = new Font("Segoe UI", 8.5f),
            Size = new Size(34, 26),
            Location = new Point(leftMargin + contentWidth - 34, gy),
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
        generalPanel.Controls.Add(savePathButton);
        gy += 34;

        var checkNowButton = new RoundedButton
        {
            Text = "Check for Updates",
            Font = new Font("Segoe UI", 8.5f),
            Size = new Size(140, 28),
            Location = new Point(leftMargin, gy),
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
            Location = new Point(leftMargin + 150, gy + 5),
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
        generalPanel.Controls.Add(checkNowButton);
        generalPanel.Controls.Add(checkResultLabel);
        generalPanel.Controls.Add(updateLinkLabel);

        Controls.Add(generalPanel);
        _tabPanels.Add(generalPanel);

        // =============================================
        // TAB 2: Advanced
        // =============================================
        var advancedPanel = CreateTabPanel(tabTop, tabContentHeight, contentWidth, leftMargin);
        advancedPanel.Visible = false;
        int uy = 14;

        // --- Uninstall Section ---
        var uninstallSectionLabel = CreateSectionLabel("UNINSTALL", leftMargin, uy);
        advancedPanel.Controls.Add(uninstallSectionLabel);
        uy += 28;

        var uninstallDesc = new Label
        {
            Text = "Remove DevToy from the tools folder and clean up hook\nentries from Claude Code settings. Your response history and app\nsettings will be preserved.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = currentTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(leftMargin, uy),
            BackColor = Color.Transparent,
        };
        advancedPanel.Controls.Add(uninstallDesc);
        uy += 60;

        var uninstallButton = new RoundedButton
        {
            Text = "Uninstall DevToy",
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Size = new Size(200, 34),
            Location = new Point(leftMargin, uy),
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

                // Launch the cleanup script before exiting — it waits for
                // this process to die, then deletes the exe files.
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
        advancedPanel.Controls.Add(uninstallButton);

        Controls.Add(advancedPanel);
        _tabPanels.Add(advancedPanel);

        // --- Version label (on main form, below tabs) ---
        var versionLabel = new Label
        {
            Text = $"DevToy v{AppVersion.Current}",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(60, 75, 105),
            AutoSize = true,
            Location = new Point(leftMargin, tabTop + tabContentHeight + 6),
            BackColor = Color.Transparent,
        };
        Controls.Add(versionLabel);

        // Final form height
        ClientSize = new Size(ClientSize.Width, tabTop + tabContentHeight + 30);
    }

    private Panel CreateTabPanel(int top, int height, int contentWidth, int leftMargin)
    {
        return new Panel
        {
            Location = new Point(0, top),
            Size = new Size(ClientSize.Width, height),
            BackColor = BackColor,
            AutoScroll = false,
        };
    }

    private Label CreateSectionLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = _currentTheme.TextSecondary,
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

    private void OnTabClick(object? sender, EventArgs e)
    {
        if (sender is not RoundedButton clicked || clicked.Tag is not int index) return;

        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool active = i == index;
            _tabButtons[i].BackColor = active ? _currentTheme.Primary : _currentTheme.BgHeader;
            _tabButtons[i].ForeColor = active ? Color.White : _currentTheme.TextSecondary;
            _tabButtons[i].FlatAppearance.MouseOverBackColor = active ? _currentTheme.PrimaryLight : _currentTheme.Border;
            _tabPanels[i].Visible = active;
        }
    }

    private void OnThemeCircleClick(object? sender, EventArgs e)
    {
        if (sender is not ThemeCircleButton btn) return;

        _currentTheme = btn.Theme;

        // Update all circle selections (circles are inside the appearance tab panel)
        foreach (var panel in _tabPanels)
        {
            foreach (Control c in panel.Controls)
            {
                if (c is ThemeCircleButton circle)
                {
                    circle.IsSelected = circle.Theme.Name == _currentTheme.Name;
                    circle.Invalidate();
                }
            }
        }

        // Update preview
        _themeNameLabel.Text = _currentTheme.Name;
        _themeNameLabel.ForeColor = _currentTheme.Primary;
        _themePreview.BackColor = _currentTheme.Primary;

        // Save and notify
        Themes.Save(_currentTheme);
        ThemeChanged?.Invoke(_currentTheme);
    }
}

class ThemeCircleButton : Control
{
    public PopupTheme Theme { get; set; } = Themes.Default;
    public bool IsSelected { get; set; }

    public ThemeCircleButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int pad = IsSelected ? 3 : 6;
        var circleRect = new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2);

        // Use BgDark as the circle fill for light/mono themes, Primary for dark themes
        bool isLightTheme = Theme.BgDark.GetBrightness() > 0.5f;
        var fillColor = isLightTheme ? Theme.BgDark : Theme.Primary;

        using var brush = new SolidBrush(fillColor);
        g.FillEllipse(brush, circleRect);

        // Light themes need a visible border since the fill is white/light
        if (isLightTheme)
        {
            using var borderPen = new Pen(Theme.Border, 1.5f);
            g.DrawEllipse(borderPen, circleRect);
        }

        // Selection ring
        if (IsSelected)
        {
            var ringColor = isLightTheme ? Theme.TextSecondary : Theme.Primary;
            using var pen = new Pen(ringColor, 2.5f);
            g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }
}
