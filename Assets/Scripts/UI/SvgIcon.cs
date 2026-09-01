using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

// Renders a single-path, 0..256 viewBox SVG (the Phosphor icon style dropped in
// Assets/UI/Textures) into a UI Toolkit element with Painter2D. Path data is
// embedded as strings -- copied verbatim from those .svg files -- so there is no
// SVG import pipeline or com.unity.vectorgraphics dependency.
public static class SvgIcon
{
    // --- path data (from Assets/UI/Textures/*.svg) ---
    public const string Cube =
        "M223.68,66.15,135.68,18h0a15.88,15.88,0,0,0-15.36,0l-88,48.17a16,16,0,0,0-8.32,14v95.64a16,16,0,0,0,8.32,14l88,48.17a15.88,15.88,0,0,0,15.36,0l88-48.17a16,16,0,0,0,8.32-14V80.18A16,16,0,0,0,223.68,66.15ZM128,32h0l80.34,44L128,120,47.66,76ZM40,90l80,43.78v85.79L40,175.82Zm96,129.57V133.82L216,90v85.78Z";
    public const string CubeTransparent =
        "M221.66,90.34h0l-56-56A8,8,0,0,0,160,32H40a8,8,0,0,0-8,8V160a8,8,0,0,0,2.3,5.61l56,56h0A8,8,0,0,0,96,224H216a8,8,0,0,0,8-8V96A8,8,0,0,0,221.66,90.34ZM168,59.31,196.69,88H168ZM88,196.69,59.31,168H88ZM88,152H48V59.31l40,40ZM59.31,48H152V88H99.31ZM152,104v48H104V104ZM104,208V168h52.69l40,40Zm104-11.31-40-40V104h40Z";
    public const string CubeFocus =
        "M232,48V88a8,8,0,0,1-16,0V56H184a8,8,0,0,1,0-16h40A8,8,0,0,1,232,48ZM72,200H40V168a8,8,0,0,0-16,0v40a8,8,0,0,0,8,8H72a8,8,0,0,0,0-16Zm152-40a8,8,0,0,0-8,8v32H184a8,8,0,0,0,0,16h40a8,8,0,0,0,8-8V168A8,8,0,0,0,224,160ZM32,96a8,8,0,0,0,8-8V56H72a8,8,0,0,0,0-16H32a8,8,0,0,0-8,8V88A8,8,0,0,0,32,96ZM188,167l-56,32a8,8,0,0,1-7.94,0L68,167A8,8,0,0,1,64,160V96a8,8,0,0,1,4-7l56-32a8,8,0,0,1,7.94,0l56,32a8,8,0,0,1,4,7v64A8,8,0,0,1,188,167ZM88.12,96,128,118.79,167.88,96,128,73.21ZM80,155.36l40,22.85V132.64L80,109.79Zm96,0V109.79l-40,22.85v45.57Z";
    public const string Dna =
        "M200,204.5V232a8,8,0,0,1-16,0V204.5a63.67,63.67,0,0,0-35.38-57.25l-48.4-24.19A79.58,79.58,0,0,1,56,51.5V24a8,8,0,0,1,16,0V51.5a63.67,63.67,0,0,0,35.38,57.25l48.4,24.19A79.58,79.58,0,0,1,200,204.5ZM160,200H72.17a63.59,63.59,0,0,1,3.23-16h72.71a8,8,0,0,0,0-16H83.46a63.71,63.71,0,0,1,14.65-15.08A8,8,0,1,0,88.64,140,80.27,80.27,0,0,0,56,204.5V232a8,8,0,0,0,16,0V216h88a8,8,0,0,0,0-16ZM192,16a8,8,0,0,0-8,8V40H96a8,8,0,0,0,0,16h87.83a63.59,63.59,0,0,1-3.23,16H107.89a8,8,0,1,0,0,16h64.65a63.71,63.71,0,0,1-14.65,15.08,8,8,0,0,0,9.47,12.9A80.27,80.27,0,0,0,200,51.5V24A8,8,0,0,0,192,16Z";
    public const string Aperture =
        "M201.54,54.46A104,104,0,0,0,54.46,201.54,104,104,0,0,0,201.54,54.46ZM190.23,65.78a88.18,88.18,0,0,1,11,13.48L167.55,119,139.63,40.78A87.34,87.34,0,0,1,190.23,65.78ZM155.59,133l-18.16,21.37-27.59-5L100.41,123l18.16-21.37,27.59,5ZM65.77,65.78a87.34,87.34,0,0,1,56.66-25.59l17.51,49L58.3,74.32A88,88,0,0,1,65.77,65.78ZM46.65,161.54a88.41,88.41,0,0,1,2.53-72.62l51.21,9.35Zm19.12,28.68a88.18,88.18,0,0,1-11-13.48L88.45,137l27.92,78.18A87.34,87.34,0,0,1,65.77,190.22Zm124.46,0a87.34,87.34,0,0,1-56.66,25.59l-17.51-49,81.64,14.91A88,88,0,0,1,190.23,190.22Zm-34.62-32.49,53.74-63.27a88.41,88.41,0,0,1-2.53,72.62Z";
    public const string DownloadSimple =
        "M224,144v64a8,8,0,0,1-8,8H40a8,8,0,0,1-8-8V144a8,8,0,0,1,16,0v56H208V144a8,8,0,0,1,16,0Zm-101.66,5.66a8,8,0,0,0,11.32,0l40-40a8,8,0,0,0-11.32-11.32L136,124.69V32a8,8,0,0,0-16,0v92.69L93.66,98.34a8,8,0,0,0-11.32,11.32Z";
    public const string UploadSimple =
        "M224,144v64a8,8,0,0,1-8,8H40a8,8,0,0,1-8-8V144a8,8,0,0,1,16,0v56H208V144a8,8,0,0,1,16,0ZM93.66,77.66,120,51.31V144a8,8,0,0,0,16,0V51.31l26.34,26.35a8,8,0,0,0,11.32-11.32l-40-40a8,8,0,0,0-11.32,0l-40,40A8,8,0,0,0,93.66,77.66Z";

    public static VisualElement Create(string pathData, Color color, float size)
    {
        VisualElement el = new VisualElement();
        el.style.width = size;
        el.style.height = size;
        el.style.flexShrink = 0;
        el.pickingMode = PickingMode.Ignore;
        el.userData = color;
        el.generateVisualContent += ctx =>
        {
            Rect r = el.contentRect;
            if (r.width <= 1f || r.height <= 1f)
            {
                return;
            }

            float s = Mathf.Min(r.width, r.height) / 256f;
            Vector2 o = new Vector2((r.width - 256f * s) * 0.5f, (r.height - 256f * s) * 0.5f);

            Painter2D p = ctx.painter2D;
            p.fillColor = el.userData is Color c ? c : Color.white;
            p.BeginPath();
            Emit(p, pathData, s, o);
            p.Fill(FillRule.OddEven);
        };
        return el;
    }

    public static void Recolor(VisualElement icon, Color color)
    {
        if (icon == null)
        {
            return;
        }
        icon.userData = color;
        icon.MarkDirtyRepaint();
    }

    // --- SVG path -> Painter2D ---

    private static void Emit(Painter2D p, string d, float scale, Vector2 off)
    {
        Vector2 Pt(float x, float y) => new Vector2(off.x + x * scale, off.y + y * scale);

        int i = 0;
        int len = d.Length;
        float cx = 0f, cy = 0f, sx = 0f, sy = 0f;
        float lastCubX = 0f, lastCubY = 0f, lastQuadX = 0f, lastQuadY = 0f;
        char prevUpper = ' ';
        char prevRepeat = ' ';

        while (i < len)
        {
            while (i < len && (d[i] == ' ' || d[i] == ',' || d[i] == '\t' || d[i] == '\n' || d[i] == '\r'))
            {
                i++;
            }
            if (i >= len)
            {
                break;
            }

            char cmd;
            bool hadLetter = char.IsLetter(d[i]);
            if (hadLetter)
            {
                cmd = d[i];
                i++;
            }
            else
            {
                cmd = prevRepeat; // implicit repeat of the previous command
            }

            // A close command has no arguments, so an implicit repeat of it
            // would spin forever -- require an explicit letter for Z.
            if ((cmd == 'Z' || cmd == 'z') && !hadLetter)
            {
                break;
            }

            prevRepeat = cmd == 'M' ? 'L' : (cmd == 'm' ? 'l' : cmd);
            bool rel = char.IsLower(cmd);
            char u = char.ToUpperInvariant(cmd);

            switch (u)
            {
                case 'M':
                {
                    float x = Num(d, ref i), y = Num(d, ref i);
                    if (rel) { x += cx; y += cy; }
                    cx = x; cy = y; sx = x; sy = y;
                    p.MoveTo(Pt(cx, cy));
                    break;
                }
                case 'L':
                {
                    float x = Num(d, ref i), y = Num(d, ref i);
                    if (rel) { x += cx; y += cy; }
                    cx = x; cy = y;
                    p.LineTo(Pt(cx, cy));
                    break;
                }
                case 'H':
                {
                    float x = Num(d, ref i);
                    cx = rel ? cx + x : x;
                    p.LineTo(Pt(cx, cy));
                    break;
                }
                case 'V':
                {
                    float y = Num(d, ref i);
                    cy = rel ? cy + y : y;
                    p.LineTo(Pt(cx, cy));
                    break;
                }
                case 'C':
                {
                    float x1 = Num(d, ref i), y1 = Num(d, ref i), x2 = Num(d, ref i), y2 = Num(d, ref i), x = Num(d, ref i), y = Num(d, ref i);
                    if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                    p.BezierCurveTo(Pt(x1, y1), Pt(x2, y2), Pt(x, y));
                    lastCubX = x2; lastCubY = y2; cx = x; cy = y;
                    break;
                }
                case 'S':
                {
                    float x2 = Num(d, ref i), y2 = Num(d, ref i), x = Num(d, ref i), y = Num(d, ref i);
                    if (rel) { x2 += cx; y2 += cy; x += cx; y += cy; }
                    bool smooth = prevUpper == 'C' || prevUpper == 'S';
                    float x1 = smooth ? 2f * cx - lastCubX : cx;
                    float y1 = smooth ? 2f * cy - lastCubY : cy;
                    p.BezierCurveTo(Pt(x1, y1), Pt(x2, y2), Pt(x, y));
                    lastCubX = x2; lastCubY = y2; cx = x; cy = y;
                    break;
                }
                case 'Q':
                {
                    float x1 = Num(d, ref i), y1 = Num(d, ref i), x = Num(d, ref i), y = Num(d, ref i);
                    if (rel) { x1 += cx; y1 += cy; x += cx; y += cy; }
                    p.QuadraticCurveTo(Pt(x1, y1), Pt(x, y));
                    lastQuadX = x1; lastQuadY = y1; cx = x; cy = y;
                    break;
                }
                case 'T':
                {
                    float x = Num(d, ref i), y = Num(d, ref i);
                    if (rel) { x += cx; y += cy; }
                    bool smooth = prevUpper == 'Q' || prevUpper == 'T';
                    float x1 = smooth ? 2f * cx - lastQuadX : cx;
                    float y1 = smooth ? 2f * cy - lastQuadY : cy;
                    p.QuadraticCurveTo(Pt(x1, y1), Pt(x, y));
                    lastQuadX = x1; lastQuadY = y1; cx = x; cy = y;
                    break;
                }
                case 'A':
                {
                    float rx = Num(d, ref i), ry = Num(d, ref i), rot = Num(d, ref i);
                    float large = Num(d, ref i), sweep = Num(d, ref i), x = Num(d, ref i), y = Num(d, ref i);
                    if (rel) { x += cx; y += cy; }
                    FlattenArc(p, cx, cy, rx, ry, rot, large != 0f, sweep != 0f, x, y, scale, off);
                    cx = x; cy = y;
                    break;
                }
                case 'Z':
                {
                    p.ClosePath();
                    cx = sx; cy = sy;
                    break;
                }
            }

            prevUpper = u;
        }
    }

    private static float Num(string d, ref int i)
    {
        int n = d.Length;
        while (i < n && (d[i] == ' ' || d[i] == ',' || d[i] == '\t' || d[i] == '\n' || d[i] == '\r'))
        {
            i++;
        }

        int start = i;
        if (i < n && (d[i] == '+' || d[i] == '-'))
        {
            i++;
        }
        while (i < n && char.IsDigit(d[i]))
        {
            i++;
        }
        if (i < n && d[i] == '.')
        {
            i++;
            while (i < n && char.IsDigit(d[i]))
            {
                i++;
            }
        }
        if (i < n && (d[i] == 'e' || d[i] == 'E'))
        {
            i++;
            if (i < n && (d[i] == '+' || d[i] == '-'))
            {
                i++;
            }
            while (i < n && char.IsDigit(d[i]))
            {
                i++;
            }
        }

        return start == i ? 0f : float.Parse(d.Substring(start, i - start), CultureInfo.InvariantCulture);
    }

    private static void FlattenArc(Painter2D p, float x1, float y1, float rx, float ry, float rotDeg,
                                  bool large, bool sweep, float x2, float y2, float scale, Vector2 off)
    {
        Vector2 Pt(float x, float y) => new Vector2(off.x + x * scale, off.y + y * scale);

        rx = Mathf.Abs(rx);
        ry = Mathf.Abs(ry);
        if (rx < 1e-4f || ry < 1e-4f || (Mathf.Approximately(x1, x2) && Mathf.Approximately(y1, y2)))
        {
            p.LineTo(Pt(x2, y2));
            return;
        }

        float phi = rotDeg * Mathf.Deg2Rad;
        float cosP = Mathf.Cos(phi), sinP = Mathf.Sin(phi);
        float dx = (x1 - x2) * 0.5f, dy = (y1 - y2) * 0.5f;
        float x1p = cosP * dx + sinP * dy;
        float y1p = -sinP * dx + cosP * dy;

        float lam = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lam > 1f)
        {
            float sl = Mathf.Sqrt(lam);
            rx *= sl;
            ry *= sl;
        }

        float sign = large != sweep ? 1f : -1f;
        float num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        float den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        float coef = den < 1e-9f ? 0f : sign * Mathf.Sqrt(Mathf.Max(0f, num / den));
        float cxp = coef * (rx * y1p / ry);
        float cyp = coef * -(ry * x1p / rx);
        float cx = cosP * cxp - sinP * cyp + (x1 + x2) * 0.5f;
        float cy = sinP * cxp + cosP * cyp + (y1 + y2) * 0.5f;

        float ux = (x1p - cxp) / rx, uy = (y1p - cyp) / ry;
        float vx = (-x1p - cxp) / rx, vy = (-y1p - cyp) / ry;
        float theta1 = Mathf.Atan2(uy, ux);
        float dtheta = Mathf.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        if (!sweep && dtheta > 0f)
        {
            dtheta -= 2f * Mathf.PI;
        }
        else if (sweep && dtheta < 0f)
        {
            dtheta += 2f * Mathf.PI;
        }

        int segs = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(dtheta) / (Mathf.PI / 12f)));
        for (int k = 1; k <= segs; k++)
        {
            float t = theta1 + dtheta * k / segs;
            float ct = Mathf.Cos(t), st = Mathf.Sin(t);
            float ex = cx + rx * ct * cosP - ry * st * sinP;
            float ey = cy + rx * ct * sinP + ry * st * cosP;
            p.LineTo(Pt(ex, ey));
        }
    }
}
