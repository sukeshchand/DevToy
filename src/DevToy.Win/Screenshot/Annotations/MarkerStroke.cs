using System.Drawing;

namespace DevToy;

class MarkerStroke : PenStroke
{
    public MarkerStroke()
    {
        Opacity = 0.4f;
        Thickness = 16f;
        StrokeColor = Color.Yellow;
    }

    public override AnnotationObject Clone()
    {
        return new MarkerStroke
        {
            Points = new List<PointF>(Points),
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Opacity = Opacity,
            ZIndex = ZIndex,
        };
    }
}
