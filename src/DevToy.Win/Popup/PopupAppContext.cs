using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DevToy;

class PopupAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly PopupForm _popupForm;
    private readonly CancellationTokenSource _cts = new();
    private Icon _appIcon;
    private SettingsForm? _settingsForm;

    public PopupAppContext(string initialTitle, string initialMessage, string initialType, string sessionId = "", string cwd = "")
    {
        var theme = Themes.LoadSaved();
        _appIcon = Themes.CreateAppIcon(theme.Primary);

        // Ensure hook script matches this exe version (critical after updates)
        Updater.EnsureHookScript(Application.ExecutablePath);

        _popupForm = new PopupForm(theme);
        _popupForm.ShowPopup(initialTitle, initialMessage, initialType, sessionId, cwd);

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "DevToy",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _trayIcon.DoubleClick += (_, _) => _popupForm.BringToForeground();

        Task.Run(() => PipeServerLoop(_cts.Token));

        // Exit when popup requests it (e.g. after update)
        _popupForm.ExitRequested += () => ExitApp();

        // Start update checker
        UpdateChecker.UpdateAvailable += metadata =>
        {
            _popupForm.ShowUpdateAvailable(metadata);
        };
        UpdateChecker.Start();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Last Notification", null, (_, _) => _popupForm.BringToForeground());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Take Screenshot", null, (_, _) => TakeScreenshot());
        menu.Items.Add("Edit Last Screenshot", null, (_, _) => EditLastScreenshot());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettingsForm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        return menu;
    }

    private void TakeScreenshot()
    {
        var overlay = new ScreenshotOverlay();
        overlay.RegionCaptured += bitmap =>
        {
            // Open the editor instead of saving immediately
            var editor = new ScreenshotEditorForm(bitmap);
            editor.ImageSaved += filePath =>
            {
                _popupForm.Invoke(() =>
                {
                    _popupForm.ShowPopup(
                        "Screenshot Saved",
                        $"Saved to:\n`{filePath}`",
                        NotificationType.Success);
                });
            };
            editor.ImageCopied += () =>
            {
                _popupForm.Invoke(() =>
                {
                    _popupForm.ShowPopup(
                        "Screenshot Copied",
                        "Screenshot copied to clipboard.",
                        NotificationType.Success);
                });
            };
            editor.Show();
        };
        overlay.Show();
    }

    private void EditLastScreenshot()
    {
        try
        {
            string dir = AppPaths.ScreenshotsDir;
            if (!Directory.Exists(dir)) return;

            var lastFile = Directory.GetFiles(dir, "*.png")
                .Concat(Directory.GetFiles(dir, "*.jpg"))
                .Concat(Directory.GetFiles(dir, "*.bmp"))
                .OrderByDescending(File.GetCreationTime)
                .FirstOrDefault();

            if (lastFile == null) return;

            Bitmap image;
            using (var stream = File.OpenRead(lastFile))
            using (var bmp = new Bitmap(stream))
            {
                image = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(image);
                g.DrawImage(bmp, 0, 0);
            }

            var editor = new ScreenshotEditorForm(image);
            editor.ImageSaved += filePath =>
            {
                _popupForm.Invoke(() =>
                {
                    _popupForm.ShowPopup("Screenshot Saved", $"Saved to:\n`{filePath}`", NotificationType.Success);
                });
            };
            editor.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EditLastScreenshot failed: {ex.Message}");
        }
    }

    private void ShowSettingsForm()
    {
        // Bring existing settings form to front if already open
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_popupForm.CurrentTheme, _popupForm.SnoozeUntil);

        _settingsForm.ThemeChanged += theme =>
        {
            // Update tray icon
            _appIcon.Dispose();
            _appIcon = Themes.CreateAppIcon(theme.Primary);
            _trayIcon.Icon = _appIcon;

            // Apply to popup
            _popupForm.ApplyTheme(theme);
        };

        _settingsForm.HistoryEnabledChanged += _ =>
        {
            _popupForm.UpdateHistoryNav();
        };

        _settingsForm.ShowQuotesChanged += show =>
        {
            _popupForm.SetShowQuotes(show);
        };

        _settingsForm.SnoozeChanged += snoozed =>
        {
            if (snoozed)
                _popupForm.Snooze();
            else
                _popupForm.Unsnooze();

            UpdateTrayText();
        };

        _settingsForm.UninstallRequested += () => ExitApp();

        _popupForm.SnoozeChanged += UpdateTrayText;

        _settingsForm.FormClosed += (_, _) =>
        {
            _popupForm.SnoozeChanged -= UpdateTrayText;
            _settingsForm = null;
        };

        _settingsForm.Show();
    }

    private void UpdateTrayText()
    {
        if (_popupForm.IsSnoozed)
        {
            var remaining = _popupForm.SnoozeUntil - DateTime.Now;
            int mins = Math.Max(1, (int)remaining.TotalMinutes);
            _trayIcon.Text = $"DevToy (snoozed {mins}m)";
        }
        else
        {
            _trayIcon.Text = "DevToy";
        }
    }

    private void ExitApp()
    {
        _cts.Cancel();
        UpdateChecker.Stop();
        _settingsForm?.Close();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
        _popupForm.ForceExit();
        ExitThread();
    }

    private async Task PipeServerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    Program.PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var json = await reader.ReadToEndAsync(ct);

                if (!string.IsNullOrEmpty(json))
                {
                    var msg = JsonSerializer.Deserialize<PipeMessage>(json);
                    if (msg != null)
                    {
                        _popupForm.Invoke(() => _popupForm.ShowPopup(
                            msg.title ?? "DevToy",
                            msg.message ?? "Task completed.",
                            msg.type ?? NotificationType.Info,
                            msg.sessionId ?? "",
                            msg.cwd ?? ""));
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"Pipe server error: {ex.Message}");
                await Task.Delay(100, ct);
            }
        }
    }

    private record PipeMessage(string? title, string? message, string? type, string? sessionId, string? cwd);
}
