using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevToy;

/// <summary>
/// Serializes/deserializes the full editor session state to/from JSON
/// in the _edits folder. Saves annotations, canvas settings, border, colors.
/// </summary>
static class SessionSerializer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Save session state to _edits/{editId}/state.json</summary>
    public static void Save(EditorSession session)
    {
        if (string.IsNullOrEmpty(session.EditId)) return;

        try
        {
            var state = new SessionState
            {
                EditId = session.EditId,
                CanvasWidth = session.CanvasSize.Width,
                CanvasHeight = session.CanvasSize.Height,
                ImageOffsetX = session.ImageOffset.X,
                ImageOffsetY = session.ImageOffset.Y,
                BgColor = ToHex(session.CanvasBackgroundColor),
                CurrentTool = session.CurrentTool.ToString(),
                CurrentColor = ToHex(session.CurrentColor),
                CurrentThickness = session.CurrentThickness,
                CurrentFontSize = session.CurrentFontSize,
                BorderEnabled = session.BorderEnabled,
                BorderStyle = session.BorderStyle.ToString(),
                BorderColor = ToHex(session.BorderColor),
                BorderThickness = session.BorderThickness,
                Timestamp = DateTime.Now,
            };

            foreach (var obj in session.Annotations)
                state.Annotations.Add(SerializeAnnotation(obj));

            string dir = session.EditDir;
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(state, JsonOpts);
            File.WriteAllText(Path.Combine(dir, "state.json"), json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SessionSerializer.Save failed: {ex.Message}");
        }
    }

    /// <summary>Restore annotations and settings from _edits/{editId}/state.json into the session.</summary>
    public static bool Restore(EditorSession session)
    {
        if (string.IsNullOrEmpty(session.EditId)) return false;

        try
        {
            string path = Path.Combine(session.EditDir, "state.json");
            if (!File.Exists(path)) return false;

            string json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<SessionState>(json, JsonOpts);
            if (state == null) return false;

            session.CanvasSize = new Size(state.CanvasWidth, state.CanvasHeight);
            session.ImageOffset = new Point(state.ImageOffsetX, state.ImageOffsetY);
            session.CanvasBackgroundColor = FromHex(state.BgColor, Color.White);
            session.CurrentColor = FromHex(state.CurrentColor, Color.Red);
            session.CurrentThickness = state.CurrentThickness;
            session.CurrentFontSize = state.CurrentFontSize;
            session.BorderEnabled = state.BorderEnabled;
            session.BorderColor = FromHex(state.BorderColor, Color.FromArgb(60, 60, 60));
            session.BorderThickness = state.BorderThickness;

            if (Enum.TryParse<AnnotationTool>(state.CurrentTool, out var tool))
                session.CurrentTool = tool;
            if (Enum.TryParse<CanvasBorderStyle>(state.BorderStyle, out var bs))
                session.BorderStyle = bs;

            // Restore annotations directly (not through undo system)
            session.Annotations.Clear();
            foreach (var data in state.Annotations)
            {
                var obj = DeserializeAnnotation(data);
                if (obj != null)
                    session.Annotations.Add(obj);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SessionSerializer.Restore failed: {ex.Message}");
            return false;
        }
    }

    // --- Annotation serialization ---

    private static AnnotationData SerializeAnnotation(AnnotationObject obj)
    {
        var data = new AnnotationData
        {
            Type = obj.GetType().Name,
            StrokeColor = ToHex(obj.StrokeColor),
            FillColor = ToHex(obj.FillColor),
            Thickness = obj.Thickness,
            Opacity = obj.Opacity,
            ZIndex = obj.ZIndex,
        };

        switch (obj)
        {
            case MarkerStroke ms:
                data.Points = ms.Points.Select(p => new float[] { p.X, p.Y }).ToList();
                break;
            case PenStroke ps:
                data.Points = ps.Points.Select(p => new float[] { p.X, p.Y }).ToList();
                break;
            case TextObject txt:
                data.Text = txt.Text;
                data.PositionX = txt.Position.X;
                data.PositionY = txt.Position.Y;
                data.FontSize = txt.FontSize;
                data.Bold = txt.Bold;
                break;
            case RectangleObject rect:
                data.StartX = rect.Start.X; data.StartY = rect.Start.Y;
                data.EndX = rect.End.X; data.EndY = rect.End.Y;
                data.Filled = rect.Filled;
                break;
            case EllipseObject ell:
                data.StartX = ell.Start.X; data.StartY = ell.Start.Y;
                data.EndX = ell.End.X; data.EndY = ell.End.Y;
                data.Filled = ell.Filled;
                break;
            case ArrowObject arr:
                data.StartX = arr.Start.X; data.StartY = arr.Start.Y;
                data.EndX = arr.End.X; data.EndY = arr.End.Y;
                break;
            case LineObject line:
                data.StartX = line.Start.X; data.StartY = line.Start.Y;
                data.EndX = line.End.X; data.EndY = line.End.Y;
                break;
        }

        return data;
    }

    private static AnnotationObject? DeserializeAnnotation(AnnotationData data)
    {
        AnnotationObject? obj = data.Type switch
        {
            "PenStroke" => new PenStroke
            {
                Points = data.Points?.Select(p => new PointF(p[0], p[1])).ToList() ?? new(),
            },
            "MarkerStroke" => new MarkerStroke
            {
                Points = data.Points?.Select(p => new PointF(p[0], p[1])).ToList() ?? new(),
            },
            "LineObject" => new LineObject
            {
                Start = new PointF(data.StartX, data.StartY),
                End = new PointF(data.EndX, data.EndY),
            },
            "ArrowObject" => new ArrowObject
            {
                Start = new PointF(data.StartX, data.StartY),
                End = new PointF(data.EndX, data.EndY),
            },
            "RectangleObject" => new RectangleObject
            {
                Start = new PointF(data.StartX, data.StartY),
                End = new PointF(data.EndX, data.EndY),
                Filled = data.Filled,
            },
            "EllipseObject" => new EllipseObject
            {
                Start = new PointF(data.StartX, data.StartY),
                End = new PointF(data.EndX, data.EndY),
                Filled = data.Filled,
            },
            "TextObject" => new TextObject
            {
                Text = data.Text ?? "",
                Position = new PointF(data.PositionX, data.PositionY),
                FontSize = data.FontSize > 0 ? data.FontSize : 16f,
                Bold = data.Bold,
            },
            _ => null,
        };

        if (obj != null)
        {
            obj.StrokeColor = FromHex(data.StrokeColor, Color.Red);
            obj.FillColor = FromHex(data.FillColor, Color.Transparent);
            obj.Thickness = data.Thickness;
            obj.Opacity = data.Opacity;
            obj.ZIndex = data.ZIndex;
        }

        return obj;
    }

    // --- Color helpers ---

    private static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color FromHex(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7) return fallback;
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 8) // ARGB
                return Color.FromArgb(
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16),
                    Convert.ToInt32(hex[6..8], 16));
            if (hex.Length == 6) // RGB
                return Color.FromArgb(
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16));
        }
        catch { }
        return fallback;
    }
}

// --- JSON data models ---

class SessionState
{
    [JsonPropertyName("editId")] public string EditId { get; set; } = "";
    [JsonPropertyName("canvasWidth")] public int CanvasWidth { get; set; }
    [JsonPropertyName("canvasHeight")] public int CanvasHeight { get; set; }
    [JsonPropertyName("imageOffsetX")] public int ImageOffsetX { get; set; }
    [JsonPropertyName("imageOffsetY")] public int ImageOffsetY { get; set; }
    [JsonPropertyName("bgColor")] public string? BgColor { get; set; }
    [JsonPropertyName("currentTool")] public string? CurrentTool { get; set; }
    [JsonPropertyName("currentColor")] public string? CurrentColor { get; set; }
    [JsonPropertyName("currentThickness")] public float CurrentThickness { get; set; }
    [JsonPropertyName("currentFontSize")] public float CurrentFontSize { get; set; }
    [JsonPropertyName("borderEnabled")] public bool BorderEnabled { get; set; }
    [JsonPropertyName("borderStyle")] public string? BorderStyle { get; set; }
    [JsonPropertyName("borderColor")] public string? BorderColor { get; set; }
    [JsonPropertyName("borderThickness")] public float BorderThickness { get; set; }
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("annotations")] public List<AnnotationData> Annotations { get; set; } = new();
}

class AnnotationData
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("strokeColor")] public string? StrokeColor { get; set; }
    [JsonPropertyName("fillColor")] public string? FillColor { get; set; }
    [JsonPropertyName("thickness")] public float Thickness { get; set; }
    [JsonPropertyName("opacity")] public float Opacity { get; set; }
    [JsonPropertyName("zIndex")] public int ZIndex { get; set; }

    // Points (for PenStroke, MarkerStroke)
    [JsonPropertyName("points")] public List<float[]>? Points { get; set; }

    // Shape start/end
    [JsonPropertyName("startX")] public float StartX { get; set; }
    [JsonPropertyName("startY")] public float StartY { get; set; }
    [JsonPropertyName("endX")] public float EndX { get; set; }
    [JsonPropertyName("endY")] public float EndY { get; set; }
    [JsonPropertyName("filled")] public bool Filled { get; set; }

    // Text
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("positionX")] public float PositionX { get; set; }
    [JsonPropertyName("positionY")] public float PositionY { get; set; }
    [JsonPropertyName("fontSize")] public float FontSize { get; set; }
    [JsonPropertyName("bold")] public bool Bold { get; set; }
}
