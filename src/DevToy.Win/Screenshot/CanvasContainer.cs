using System.Drawing;
using System.Drawing.Drawing2D;

namespace DevToy;

/// <summary>
/// Hosts the ScreenshotCanvas as a fixed-size child, centers it, and provides
/// resize handles on the canvas edges that the user can drag to expand/shrink the canvas.
/// </summary>
class CanvasContainer : Panel
{
    private readonly ScreenshotCanvas _canvas;
    private const int HandleSize = 8;
    private const int HandleHitZone = 10;

    private bool _isResizingCanvas;
    private HandlePosition _resizeHandle;
    private Point _resizeStart;
    private Size _canvasSizeAtStart;

    public CanvasContainer(ScreenshotCanvas canvas)
    {
        _canvas = canvas;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Color.FromArgb(30, 30, 30);
        AutoScroll = false;

        Controls.Add(_canvas);
        CenterCanvas();
    }

    /// <summary>Recenter the canvas within this container.</summary>
    public void CenterCanvas()
    {
        int x = Math.Max(0, (ClientSize.Width - _canvas.Width) / 2);
        int y = Math.Max(0, (ClientSize.Height - _canvas.Height) / 2);
        _canvas.Location = new Point(x, y);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        CenterCanvas();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Background
        using (var bgBrush = new SolidBrush(BackColor))
            g.FillRectangle(bgBrush, ClientRectangle);

        // Border around the canvas
        var cr = _canvas.Bounds;
        using var borderPen = new Pen(Color.FromArgb(100, 128, 128, 128), 1f);
        g.DrawRectangle(borderPen, cr.X - 1, cr.Y - 1, cr.Width + 1, cr.Height + 1);

        // Draw resize handles
        DrawResizeHandles(g, cr);
    }

    private void DrawResizeHandles(Graphics g, Rectangle canvasRect)
    {
        using var brush = new SolidBrush(Color.FromArgb(200, 80, 160, 255));
        int hs = HandleSize;
        int hh = hs / 2;

        float cx = canvasRect.X + canvasRect.Width / 2f;
        float cy = canvasRect.Y + canvasRect.Height / 2f;

        // 8 handles: corners + edge midpoints
        var handles = GetHandleRects(canvasRect);
        foreach (var r in handles)
            g.FillRectangle(brush, r);
    }

    private RectangleF[] GetHandleRects(Rectangle cr)
    {
        int hs = HandleSize;
        int hh = hs / 2;
        float cx = cr.X + cr.Width / 2f;
        float cy = cr.Y + cr.Height / 2f;

        return new RectangleF[]
        {
            new(cr.Left - hh, cr.Top - hh, hs, hs),           // TopLeft
            new(cx - hh, cr.Top - hh, hs, hs),                // TopCenter
            new(cr.Right - hh, cr.Top - hh, hs, hs),          // TopRight
            new(cr.Left - hh, cy - hh, hs, hs),               // MiddleLeft
            new(cr.Right - hh, cy - hh, hs, hs),              // MiddleRight
            new(cr.Left - hh, cr.Bottom - hh, hs, hs),        // BottomLeft
            new(cx - hh, cr.Bottom - hh, hs, hs),             // BottomCenter
            new(cr.Right - hh, cr.Bottom - hh, hs, hs),       // BottomRight
        };
    }

    private static readonly HandlePosition[] HandlePositions =
    {
        HandlePosition.TopLeft, HandlePosition.TopCenter, HandlePosition.TopRight,
        HandlePosition.MiddleLeft, HandlePosition.MiddleRight,
        HandlePosition.BottomLeft, HandlePosition.BottomCenter, HandlePosition.BottomRight,
    };

    private HandlePosition HitTestHandle(Point pt)
    {
        var handles = GetHandleRects(_canvas.Bounds);
        for (int i = 0; i < handles.Length; i++)
        {
            var r = handles[i];
            r.Inflate(HandleHitZone / 2f, HandleHitZone / 2f);
            if (r.Contains(pt))
                return HandlePositions[i];
        }
        return HandlePosition.None;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var handle = HitTestHandle(e.Location);
        if (handle != HandlePosition.None)
        {
            _isResizingCanvas = true;
            _resizeHandle = handle;
            _resizeStart = e.Location;
            _canvasSizeAtStart = _canvas.Size;
            Capture = true;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isResizingCanvas)
        {
            int dx = e.X - _resizeStart.X;
            int dy = e.Y - _resizeStart.Y;

            int newW = _canvasSizeAtStart.Width;
            int newH = _canvasSizeAtStart.Height;

            switch (_resizeHandle)
            {
                case HandlePosition.TopLeft:
                    newW -= dx; newH -= dy; break;
                case HandlePosition.TopCenter:
                    newH -= dy; break;
                case HandlePosition.TopRight:
                    newW += dx; newH -= dy; break;
                case HandlePosition.MiddleLeft:
                    newW -= dx; break;
                case HandlePosition.MiddleRight:
                    newW += dx; break;
                case HandlePosition.BottomLeft:
                    newW -= dx; newH += dy; break;
                case HandlePosition.BottomCenter:
                    newH += dy; break;
                case HandlePosition.BottomRight:
                    newW += dx; newH += dy; break;
            }

            // Enforce minimum canvas size
            newW = Math.Max(50, newW);
            newH = Math.Max(50, newH);

            _canvas.Size = new Size(newW, newH);
            CenterCanvas();
            return;
        }

        // Update cursor based on handle hover
        var hit = HitTestHandle(e.Location);
        Cursor = hit switch
        {
            HandlePosition.TopLeft or HandlePosition.BottomRight => Cursors.SizeNWSE,
            HandlePosition.TopRight or HandlePosition.BottomLeft => Cursors.SizeNESW,
            HandlePosition.TopCenter or HandlePosition.BottomCenter => Cursors.SizeNS,
            HandlePosition.MiddleLeft or HandlePosition.MiddleRight => Cursors.SizeWE,
            _ => Cursors.Default,
        };

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_isResizingCanvas)
        {
            _isResizingCanvas = false;
            Capture = false;
            Invalidate();
            return;
        }

        base.OnMouseUp(e);
    }
}
