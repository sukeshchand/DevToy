using System.Diagnostics;
using System.Drawing;

namespace DevToy;

class ScreenshotEditorForm : Form
{
    private readonly EditorSession _session;
    private readonly ScreenshotCanvas _canvas;
    private readonly CanvasContainer _canvasContainer;
    private readonly ScreenshotToolbar _toolbar;
    private readonly PopupTheme _theme;

    /// <summary>Fires when the user saves. Provides the saved file path.</summary>
    public event Action<string>? ImageSaved;

    /// <summary>Fires when the user copies to clipboard.</summary>
    public event Action? ImageCopied;

    public ScreenshotEditorForm(Bitmap capturedImage)
    {
        _session = new EditorSession(capturedImage);
        _theme = Themes.LoadSaved();

        // Standard window with full chrome
        Text = "DevToy — Screenshot Editor";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;
        MinimumSize = new Size(500, 350);
        Icon = Themes.CreateAppIcon(_theme.Primary);

        // Theme colors
        BackColor = _theme.BgDark;
        AutoScaleMode = AutoScaleMode.Dpi;

        // Size form to exactly fit the captured image + toolbar + gap
        int toolbarAreaHeight = 60;
        int pad = 8;
        int imgW = capturedImage.Width;
        int imgH = capturedImage.Height;

        // Client area = image + padding around it + toolbar area
        int clientW = Math.Max(imgW + pad * 2, 600);
        int clientH = imgH + toolbarAreaHeight + pad * 2;

        // Clamp to screen
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        clientW = Math.Min(clientW, screen.Width - 40);
        clientH = Math.Min(clientH, screen.Height - 40);

        ClientSize = new Size(clientW, clientH);
        StartPosition = FormStartPosition.CenterScreen;

        // Toolbar — centered at top, recentered on resize
        _toolbar = new ScreenshotToolbar
        {
            Session = _session,
        };
        _toolbar.Location = new Point(
            Math.Max(pad, (ClientSize.Width - _toolbar.Width) / 2), pad);
        Controls.Add(_toolbar);

        // Canvas — fixed size matching the captured image
        int canvasTop = toolbarAreaHeight;
        _canvas = new ScreenshotCanvas
        {
            Size = new Size(imgW, imgH),
            Session = _session,
        };

        // Container — fills the area below toolbar, centers the canvas, provides resize handles
        _canvasContainer = new CanvasContainer(_canvas)
        {
            Location = new Point(0, canvasTop),
            Size = new Size(ClientSize.Width, ClientSize.Height - canvasTop),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = _theme.BgDark,
        };
        Controls.Add(_canvasContainer);

        // Wire toolbar events
        WireToolbarEvents();

        // Recenter toolbar on resize
        Resize += (_, _) => CenterToolbar();
    }

    private void CenterToolbar()
    {
        _toolbar.Location = new Point(
            Math.Max(8, (ClientSize.Width - _toolbar.Width) / 2),
            _toolbar.Location.Y);
    }

    private void WireToolbarEvents()
    {
        _toolbar.QuickCopyRequested += DoCopy;
        _toolbar.CopyPathRequested += DoCopyPath;

        _toolbar.ToolSelected += tool =>
        {
            _canvas.CommitTextEdit();
            _session.CurrentTool = tool;
            if (tool != AnnotationTool.Select)
                _session.DeselectAll();
            _canvas.UpdateToolCursor();
            _canvas.Invalidate();
            _toolbar.Invalidate();
        };

        _toolbar.UndoRequested += () => { _session.UndoRedo.Undo(); _canvas.Invalidate(); _toolbar.Invalidate(); };
        _toolbar.RedoRequested += () => { _session.UndoRedo.Redo(); _canvas.Invalidate(); _toolbar.Invalidate(); };
        _toolbar.DeleteRequested += () => { _session.DeleteSelected(); _canvas.Invalidate(); };
        _toolbar.BringForwardRequested += () => { _session.BringForward(); _canvas.Invalidate(); };
        _toolbar.SendBackwardRequested += () => { _session.SendBackward(); _canvas.Invalidate(); };

        _toolbar.ColorChanged += color =>
        {
            _session.CurrentColor = color;
            if (_session.SelectedObject != null)
            {
                var obj = _session.SelectedObject;
                var old = obj.StrokeColor;
                _session.UndoRedo.Execute(new ModifyPropertyAction<Color>(
                    "Change color", c => obj.StrokeColor = c, old, color));
                _canvas.Invalidate();
            }
        };

        _toolbar.ThicknessChanged += thickness =>
        {
            _session.CurrentThickness = thickness;
            if (_session.SelectedObject != null)
            {
                var obj = _session.SelectedObject;
                var old = obj.Thickness;
                _session.UndoRedo.Execute(new ModifyPropertyAction<float>(
                    "Change thickness", t => obj.Thickness = t, old, thickness));
                _canvas.Invalidate();
            }
        };

        _toolbar.FontSizeChanged += size =>
        {
            _session.CurrentFontSize = size;
            if (_session.SelectedObject is TextObject txt)
            {
                var old = txt.FontSize;
                _session.UndoRedo.Execute(new ModifyPropertyAction<float>(
                    "Change font size", s => txt.FontSize = s, old, size));
                _canvas.Invalidate();
            }
        };

        _toolbar.BorderToggled += () =>
        {
            // Show border popup below the toolbar
            var screenPt = _toolbar.PointToScreen(new Point(_toolbar.Width / 2 - 110, _toolbar.Height));
            var popup = new BorderPopup(_session, screenPt);
            popup.SettingsChanged += () =>
            {
                _canvas.Invalidate();
                _toolbar.Invalidate();
            };
            popup.Show(this);
        };

        _toolbar.SaveRequested += DoSave;
        _toolbar.SaveAsRequested += DoSaveAs;
        _toolbar.CopyRequested += DoCopy;
        _toolbar.CancelRequested += DoCancel;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Global keyboard shortcuts
        if (e.Control && e.Shift && e.KeyCode == Keys.S) { DoSaveAs(); e.Handled = true; return; }
        if (e.Control && e.Shift && e.KeyCode == Keys.C) { DoCopyPath(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.S) { DoSave(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.C) { DoCopy(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.Z) { _session.UndoRedo.Undo(); _canvas.Invalidate(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.Y) { _session.UndoRedo.Redo(); _canvas.Invalidate(); e.Handled = true; return; }
        if (e.KeyCode == Keys.Delete) { _session.DeleteSelected(); _canvas.Invalidate(); e.Handled = true; return; }
        if (e.KeyCode == Keys.Escape) { DoCancel(); e.Handled = true; return; }

        // Tool shortcuts
        if (!e.Control && !e.Alt)
        {
            AnnotationTool? tool = e.KeyCode switch
            {
                Keys.V => AnnotationTool.Select,
                Keys.P => AnnotationTool.Pen,
                Keys.M => AnnotationTool.Marker,
                Keys.L => AnnotationTool.Line,
                Keys.A => AnnotationTool.Arrow,
                Keys.R => AnnotationTool.Rectangle,
                Keys.E => AnnotationTool.Ellipse,
                _ => null,
            };
            if (e.KeyCode == Keys.T && _session.Annotations.All(a => a is not TextObject { IsEditing: true }))
                tool = AnnotationTool.Text;

            if (e.KeyCode == Keys.B)
            {
                _session.BorderEnabled = !_session.BorderEnabled;
                _canvas.Invalidate();
                _toolbar.Invalidate();
                e.Handled = true;
                return;
            }

            if (tool != null)
            {
                _canvas.CommitTextEdit();
                _session.CurrentTool = tool.Value;
                if (tool != AnnotationTool.Select)
                    _session.DeselectAll();
                _canvas.UpdateToolCursor();
                _canvas.Invalidate();
                _toolbar.Invalidate();
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    private void DoSave()
    {
        try
        {
            _canvas.CommitTextEdit();
            string path = ScreenshotExporter.SaveToFile(_session);
            ImageSaved?.Invoke(path);
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Save failed: {ex.Message}");
            MessageBox.Show(this, $"Save failed: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DoSaveAs()
    {
        try
        {
            _canvas.CommitTextEdit();
            using var dlg = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap|*.bmp",
                DefaultExt = "png",
                FileName = $"screenshot_{DateTime.Now:yyyy-MM-dd_HHmmss}",
                InitialDirectory = AppPaths.ScreenshotsDir,
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                ScreenshotExporter.SaveToFile(_session, dlg.FileName);
                ImageSaved?.Invoke(dlg.FileName);
                Close();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Save As failed: {ex.Message}");
            MessageBox.Show(this, $"Save failed: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DoCopy()
    {
        try
        {
            _canvas.CommitTextEdit();
            ScreenshotExporter.CopyToClipboard(_session);
            ImageCopied?.Invoke();
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copy failed: {ex.Message}");
        }
    }

    private void DoCopyPath()
    {
        try
        {
            _canvas.CommitTextEdit();
            string path = ScreenshotExporter.SaveToFile(_session);

            // Copy the file to clipboard as a file drop (like selecting a file in Explorer and pressing Ctrl+C)
            var fileList = new System.Collections.Specialized.StringCollection();
            fileList.Add(path);
            Clipboard.SetFileDropList(fileList);

            ImageSaved?.Invoke(path);
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copy Path failed: {ex.Message}");
            MessageBox.Show(this, $"Copy Path failed: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DoCancel()
    {
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session.OriginalImage.Dispose();
        base.OnFormClosed(e);
    }
}
