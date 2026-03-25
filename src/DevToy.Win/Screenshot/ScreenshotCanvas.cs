using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DevToy;

class ScreenshotCanvas : Control
{
    private EditorSession? _session;

    // Drawing state
    private bool _isDrawing;
    private AnnotationObject? _drawingObject;
    private PointF _lastDragPos;

    // Selection/move state
    private bool _isMoving;
    private bool _isResizing;
    private HandlePosition _resizeHandle;
    private float _totalMoveDx, _totalMoveDy;

    // Text editing
    private TextObject? _editingText;

    public event Action? CanvasChanged;
    public event Action? SelectionChanged;

    public ScreenshotCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(30, 30, 30);
        Cursor = Cursors.Cross;
    }

    public EditorSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Checkerboard background
        using (var bgBrush = new HatchBrush(HatchStyle.LargeCheckerBoard,
            Color.FromArgb(45, 45, 45), Color.FromArgb(35, 35, 35)))
            g.FillRectangle(bgBrush, ClientRectangle);

        if (_session == null) return;

        // Draw the original image
        g.DrawImage(_session.OriginalImage, 0, 0);

        // Draw all annotations in order (list order = z-order)
        foreach (var obj in _session.Annotations)
        {
            obj.Render(g);
            if (obj.IsSelected)
                obj.RenderSelectionHandles(g);
        }

        // Draw in-progress shape/line
        if (_isDrawing && _drawingObject != null)
        {
            _drawingObject.Render(g);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_session == null || e.Button != MouseButtons.Left) return;
        Focus();
        var pt = new PointF(e.X, e.Y);

        // If editing text, commit on click outside
        if (_editingText != null)
        {
            if (!_editingText.HitTest(pt, 6f))
                CommitTextEdit();
        }

        switch (_session.CurrentTool)
        {
            case AnnotationTool.Select:
                HandleSelectMouseDown(pt);
                break;
            case AnnotationTool.Pen:
                StartPenDraw(pt, false);
                break;
            case AnnotationTool.Marker:
                StartPenDraw(pt, true);
                break;
            case AnnotationTool.Line:
            case AnnotationTool.Arrow:
            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
                StartShapeDraw(pt);
                break;
            case AnnotationTool.Text:
                HandleTextClick(pt);
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_session == null) return;
        var pt = new PointF(e.X, e.Y);

        if (_isDrawing && _drawingObject != null)
        {
            switch (_drawingObject)
            {
                case PenStroke stroke:
                    stroke.Points.Add(pt);
                    break;
                case ShapeObject shape:
                    shape.End = pt;
                    break;
            }
            Invalidate();
            return;
        }

        if (_isMoving && _session.SelectedObject != null)
        {
            float dx = pt.X - _lastDragPos.X;
            float dy = pt.Y - _lastDragPos.Y;
            _session.SelectedObject.Move(dx, dy);
            _totalMoveDx += dx;
            _totalMoveDy += dy;
            _lastDragPos = pt;
            Invalidate();
            return;
        }

        if (_isResizing && _session.SelectedObject != null)
        {
            float dx = pt.X - _lastDragPos.X;
            float dy = pt.Y - _lastDragPos.Y;
            _session.SelectedObject.Resize(_resizeHandle, dx, dy);
            _totalMoveDx += dx;
            _totalMoveDy += dy;
            _lastDragPos = pt;
            Invalidate();
            return;
        }

        // Update cursor for select tool
        if (_session.CurrentTool == AnnotationTool.Select)
        {
            UpdateCursor(pt);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_session == null || e.Button != MouseButtons.Left) return;

        if (_isDrawing && _drawingObject != null)
        {
            FinishDraw();
            return;
        }

        if (_isMoving && _session.SelectedObject != null)
        {
            // Record as undoable move
            if (Math.Abs(_totalMoveDx) > 0.5f || Math.Abs(_totalMoveDy) > 0.5f)
            {
                // Undo the live move, then execute via undo system
                _session.SelectedObject.Move(-_totalMoveDx, -_totalMoveDy);
                _session.UndoRedo.Execute(new MoveObjectAction(_session.SelectedObject, _totalMoveDx, _totalMoveDy));
            }
            _isMoving = false;
            Invalidate();
            return;
        }

        if (_isResizing && _session.SelectedObject != null)
        {
            // Record as undoable resize
            if (Math.Abs(_totalMoveDx) > 0.5f || Math.Abs(_totalMoveDy) > 0.5f)
            {
                _session.SelectedObject.Resize(_resizeHandle, -_totalMoveDx, -_totalMoveDy);
                _session.UndoRedo.Execute(new ResizeObjectAction(_session.SelectedObject, _resizeHandle, _totalMoveDx, _totalMoveDy));
            }
            _isResizing = false;
            Invalidate();
            return;
        }
    }

    // --- Key handling for text editing ---
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_editingText != null)
        {
            HandleTextKeyDown(e);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (_editingText != null && !char.IsControl(e.KeyChar))
        {
            var old = _editingText.Text;
            _editingText.Text += e.KeyChar;
            e.Handled = true;
            Invalidate();
            return;
        }
        base.OnKeyPress(e);
    }

    // --- Private methods ---

    private void HandleSelectMouseDown(PointF pt)
    {
        // Check if clicking a handle of selected object
        if (_session!.SelectedObject != null)
        {
            var handle = _session.SelectedObject.HitTestHandle(pt);
            if (handle != HandlePosition.None)
            {
                _isResizing = true;
                _resizeHandle = handle;
                _lastDragPos = pt;
                _totalMoveDx = 0;
                _totalMoveDy = 0;
                return;
            }
        }

        _session.SelectAt(pt);
        SelectionChanged?.Invoke();

        if (_session.SelectedObject != null)
        {
            _isMoving = true;
            _lastDragPos = pt;
            _totalMoveDx = 0;
            _totalMoveDy = 0;
        }

        Invalidate();
    }

    private void StartPenDraw(PointF pt, bool isMarker)
    {
        PenStroke stroke = isMarker
            ? new MarkerStroke { StrokeColor = _session!.CurrentColor }
            : new PenStroke { StrokeColor = _session!.CurrentColor, Thickness = _session.CurrentThickness };

        if (isMarker)
        {
            stroke.Thickness = Math.Max(_session.CurrentThickness * 4, 16f);
        }

        stroke.Points.Add(pt);
        _drawingObject = stroke;
        _isDrawing = true;
    }

    private void StartShapeDraw(PointF pt)
    {
        ShapeObject shape = _session!.CurrentTool switch
        {
            AnnotationTool.Line => new LineObject(),
            AnnotationTool.Arrow => new ArrowObject(),
            AnnotationTool.Rectangle => new RectangleObject(),
            AnnotationTool.Ellipse => new EllipseObject(),
            _ => new LineObject(),
        };
        shape.Start = pt;
        shape.End = pt;
        shape.StrokeColor = _session.CurrentColor;
        shape.Thickness = _session.CurrentThickness;
        _drawingObject = shape;
        _isDrawing = true;
    }

    private void FinishDraw()
    {
        _isDrawing = false;
        if (_drawingObject == null || _session == null) return;

        // Validate minimum size
        bool valid = _drawingObject switch
        {
            PenStroke ps => ps.Points.Count >= 2,
            ShapeObject so => Math.Abs(so.End.X - so.Start.X) > 2 || Math.Abs(so.End.Y - so.Start.Y) > 2,
            _ => true,
        };

        if (valid)
        {
            _session.AddAnnotation(_drawingObject);
            CanvasChanged?.Invoke();
        }

        _drawingObject = null;
        Invalidate();
    }

    private void HandleTextClick(PointF pt)
    {
        if (_session == null) return;

        // Check if clicking an existing text object
        for (int i = _session.Annotations.Count - 1; i >= 0; i--)
        {
            if (_session.Annotations[i] is TextObject existing && existing.HitTest(pt, 6f))
            {
                StartTextEdit(existing);
                return;
            }
        }

        // Create new text object
        var txt = new TextObject
        {
            Position = pt,
            StrokeColor = _session.CurrentColor,
            FontSize = _session.CurrentFontSize,
        };
        _session.AddAnnotation(txt);
        StartTextEdit(txt);
    }

    private void StartTextEdit(TextObject txt)
    {
        if (_editingText != null) CommitTextEdit();
        _editingText = txt;
        _editingText.IsEditing = true;
        _session!.SelectedObject = txt;
        txt.IsSelected = true;
        Cursor = Cursors.IBeam;
        Invalidate();
    }

    public void CommitTextEdit()
    {
        if (_editingText == null) return;

        // Remove empty text objects
        if (string.IsNullOrWhiteSpace(_editingText.Text))
        {
            _session?.Annotations.Remove(_editingText);
        }

        _editingText.IsEditing = false;
        _editingText = null;

        if (_session?.CurrentTool != AnnotationTool.Text)
            Cursor = Cursors.Cross;
        else
            Cursor = Cursors.IBeam;

        Invalidate();
        CanvasChanged?.Invoke();
    }

    private void HandleTextKeyDown(KeyEventArgs e)
    {
        if (_editingText == null) return;

        switch (e.KeyCode)
        {
            case Keys.Back:
                if (_editingText.Text.Length > 0)
                    _editingText.Text = _editingText.Text[..^1];
                e.Handled = true;
                break;
            case Keys.Enter:
                _editingText.Text += "\n";
                e.Handled = true;
                break;
            case Keys.Escape:
                CommitTextEdit();
                e.Handled = true;
                break;
        }
        Invalidate();
    }

    private void UpdateCursor(PointF pt)
    {
        if (_session?.SelectedObject != null)
        {
            var handle = _session.SelectedObject.HitTestHandle(pt);
            Cursor = handle switch
            {
                HandlePosition.TopLeft or HandlePosition.BottomRight => Cursors.SizeNWSE,
                HandlePosition.TopRight or HandlePosition.BottomLeft => Cursors.SizeNESW,
                HandlePosition.TopCenter or HandlePosition.BottomCenter => Cursors.SizeNS,
                HandlePosition.MiddleLeft or HandlePosition.MiddleRight => Cursors.SizeWE,
                _ => _session.SelectedObject.HitTest(pt, 6f) ? Cursors.SizeAll : Cursors.Default,
            };
        }
        else
        {
            // Check if hovering over any object
            bool overObj = false;
            for (int i = _session!.Annotations.Count - 1; i >= 0; i--)
            {
                if (_session.Annotations[i].HitTest(pt, 6f))
                {
                    overObj = true;
                    break;
                }
            }
            Cursor = overObj ? Cursors.Hand : Cursors.Default;
        }
    }

    public void UpdateToolCursor()
    {
        if (_editingText != null) return;
        Cursor = _session?.CurrentTool switch
        {
            AnnotationTool.Select => Cursors.Default,
            AnnotationTool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
    }
}
