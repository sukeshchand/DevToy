using System.Diagnostics;
using System.Drawing;

namespace DevToy;

class ScreenshotEditorForm : Form
{
    private readonly EditorSession _session;
    private readonly ScreenshotCanvas _canvas;
    private readonly ScreenshotToolbar _toolbar;

    /// <summary>Fires when the user saves. Provides the saved file path.</summary>
    public event Action<string>? ImageSaved;

    /// <summary>Fires when the user copies to clipboard.</summary>
    public event Action? ImageCopied;

    public ScreenshotEditorForm(Bitmap capturedImage)
    {
        _session = new EditorSession(capturedImage);

        Text = "DevToy - Screenshot Editor";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;
        BackColor = Color.FromArgb(25, 25, 30);

        // Size form to fit the image with padding for toolbar
        int toolbarHeight = 44;
        int padding = 0;
        int formW = Math.Max(capturedImage.Width + padding * 2, 600);
        int formH = capturedImage.Height + toolbarHeight + padding * 2 + 8;

        // Clamp to screen bounds
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        formW = Math.Min(formW, screen.Width);
        formH = Math.Min(formH, screen.Height);

        Size = new Size(formW, formH);
        StartPosition = FormStartPosition.Manual;

        // Center on screen
        Location = new Point(
            screen.Left + (screen.Width - formW) / 2,
            screen.Top + (screen.Height - formH) / 2);

        // Canvas
        _canvas = new ScreenshotCanvas
        {
            Location = new Point(
                Math.Max(0, (formW - capturedImage.Width) / 2),
                toolbarHeight + 8),
            Size = new Size(
                Math.Min(capturedImage.Width, formW),
                Math.Min(capturedImage.Height, formH - toolbarHeight - 8)),
            Session = _session,
        };
        Controls.Add(_canvas);

        // Toolbar — centered at top
        _toolbar = new ScreenshotToolbar
        {
            Session = _session,
        };
        _toolbar.Location = new Point(
            Math.Max(4, (formW - _toolbar.Width) / 2), 4);
        Controls.Add(_toolbar);

        // Wire toolbar events
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

        _toolbar.SaveRequested += DoSave;
        _toolbar.SaveAsRequested += DoSaveAs;
        _toolbar.CopyRequested += DoCopy;
        _toolbar.CancelRequested += DoCancel;

        // Make form draggable from empty areas
        MouseDown += OnFormDrag;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Global keyboard shortcuts
        if (e.Control && e.Shift && e.KeyCode == Keys.S) { DoSaveAs(); e.Handled = true; return; }
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
            // T key only switches to text if we're not editing text
            if (e.KeyCode == Keys.T && _session.Annotations.All(a => a is not TextObject { IsEditing: true }))
                tool = AnnotationTool.Text;

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

    private void DoCancel()
    {
        Close();
    }

    // Allow dragging the borderless form
    private Point _dragOffset;
    private void OnFormDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragOffset = e.Location;
            MouseMove += OnFormDragMove;
            MouseUp += OnFormDragEnd;
        }
    }
    private void OnFormDragMove(object? sender, MouseEventArgs e)
    {
        Location = new Point(Location.X + e.X - _dragOffset.X, Location.Y + e.Y - _dragOffset.Y);
    }
    private void OnFormDragEnd(object? sender, MouseEventArgs e)
    {
        MouseMove -= OnFormDragMove;
        MouseUp -= OnFormDragEnd;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session.OriginalImage.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Subtle border around the form
        using var pen = new Pen(Color.FromArgb(60, 80, 160, 255), 1f);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
