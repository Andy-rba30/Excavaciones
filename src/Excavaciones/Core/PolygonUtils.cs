using Autodesk.Revit.DB;

namespace Excavaciones.Core;

/// <summary>Operaciones con polígonos en planta (lista de vértices XYZ con Z constante).</summary>
public static class PolygonUtils
{
    public const double Tol = 1e-6;

    public static double SignedArea(IList<XYZ> p)
    {
        double a = 0;
        for (int i = 0, n = p.Count; i < n; i++)
        {
            XYZ p0 = p[i], p1 = p[(i + 1) % n];
            a += p0.X * p1.Y - p1.X * p0.Y;
        }
        return a / 2.0;
    }

    public static double Area(IList<XYZ> p) => Math.Abs(SignedArea(p));

    public static List<XYZ> EnsureCcw(IList<XYZ> p)
    {
        var list = new List<XYZ>(p);
        if (SignedArea(list) < 0) list.Reverse();
        return list;
    }

    public static List<XYZ> SetZ(IEnumerable<XYZ> p, double z) => p.Select(v => new XYZ(v.X, v.Y, z)).ToList();

    public static (double minX, double maxX, double minY, double maxY) Bounds(IEnumerable<XYZ> p)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var v in p)
        {
            minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
        }
        return (minX, maxX, minY, maxY);
    }

    /// <summary>Elimina vértices repetidos o colineales.</summary>
    public static List<XYZ> Clean(IList<XYZ> p, double tol)
    {
        var r = new List<XYZ>();
        foreach (var v in p)
        {
            if (r.Count > 0 && Dist2D(r[^1], v) < tol) continue;
            r.Add(v);
        }
        while (r.Count > 1 && Dist2D(r[0], r[^1]) < tol) r.RemoveAt(r.Count - 1);
        // colineales
        bool changed = true;
        while (changed && r.Count > 3)
        {
            changed = false;
            for (int i = 0; i < r.Count; i++)
            {
                XYZ a = r[(i + r.Count - 1) % r.Count], b = r[i], c = r[(i + 1) % r.Count];
                double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                double len = Dist2D(a, b) * Dist2D(b, c);
                if (len > 0 && Math.Abs(cross) / len < 1e-9)
                {
                    r.RemoveAt(i); changed = true; break;
                }
            }
        }
        return r;
    }

    public static double Dist2D(XYZ a, XYZ b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Convierte un CurveLoop a polígono (teselando arcos) en la cota indicada.</summary>
    public static List<XYZ> FromCurveLoop(CurveLoop loop, double z, double tol)
    {
        var pts = new List<XYZ>();
        foreach (Curve c in loop)
        {
            IList<XYZ> t = c.Tessellate();
            for (int i = 0; i < t.Count - 1; i++) pts.Add(new XYZ(t[i].X, t[i].Y, z));
        }
        return Clean(pts, tol);
    }

    /// <summary>Une curvas sueltas en un lazo cerrado ordenado. Devuelve null si no cierran.</summary>
    public static CurveLoop? JoinCurves(IList<Curve> curves, double tol)
    {
        if (curves.Count == 0) return null;
        var remaining = new List<Curve>(curves);
        var ordered = new List<Curve> { remaining[0] };
        remaining.RemoveAt(0);
        XYZ end = ordered[0].GetEndPoint(1);
        XYZ start = ordered[0].GetEndPoint(0);
        while (remaining.Count > 0)
        {
            int found = -1; bool reverse = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                if (remaining[i].GetEndPoint(0).DistanceTo(end) < tol) { found = i; break; }
                if (remaining[i].GetEndPoint(1).DistanceTo(end) < tol) { found = i; reverse = true; break; }
            }
            if (found < 0) return null;
            Curve c = remaining[found];
            remaining.RemoveAt(found);
            if (reverse) c = c.CreateReversed();
            ordered.Add(c);
            end = c.GetEndPoint(1);
        }
        if (end.DistanceTo(start) > tol * 10) return null;
        try { return CurveLoop.Create(ordered); } catch { return null; }
    }

    public static CurveLoop ToCurveLoop(IList<XYZ> poly)
    {
        var loop = new CurveLoop();
        for (int i = 0, n = poly.Count; i < n; i++)
            loop.Append(Line.CreateBound(poly[i], poly[(i + 1) % n]));
        return loop;
    }

    /// <summary>Verifica que el polígono sea simple (sin auto-intersecciones).</summary>
    public static bool IsSimple(IList<XYZ> p)
    {
        int n = p.Count;
        if (n < 3) return false;
        for (int i = 0; i < n; i++)
        {
            XYZ a1 = p[i], a2 = p[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
                if (j == i || (j + 1) % n == i || (i + 1) % n == j) continue;
                XYZ b1 = p[j], b2 = p[(j + 1) % n];
                if (SegmentsIntersect(a1, a2, b1, b2)) return false;
            }
        }
        return true;
    }

    private static bool SegmentsIntersect(XYZ p1, XYZ p2, XYZ p3, XYZ p4)
    {
        double d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2), d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
        return ((d1 > Tol && d2 < -Tol) || (d1 < -Tol && d2 > Tol)) && ((d3 > Tol && d4 < -Tol) || (d3 < -Tol && d4 > Tol));
    }

    private static double Cross(XYZ o, XYZ a, XYZ b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    /// <summary>Desfase por inglete (mantiene número de vértices). Polígono CCW; d &gt; 0 agranda.</summary>
    public static List<XYZ>? MiterOffset(IList<XYZ> poly, double d, double maxMiterRatio = 4.0)
    {
        int n = poly.Count;
        if (n < 3) return null;
        var r = new List<XYZ>(n);
        for (int i = 0; i < n; i++)
        {
            XYZ prev = poly[(i + n - 1) % n], cur = poly[i], next = poly[(i + 1) % n];
            XYZ e1 = (cur - prev); XYZ e2 = (next - cur);
            double l1 = Math.Sqrt(e1.X * e1.X + e1.Y * e1.Y), l2 = Math.Sqrt(e2.X * e2.X + e2.Y * e2.Y);
            if (l1 < 1e-12 || l2 < 1e-12) return null;
            // normales exteriores de un polígono CCW: (dy, -dx)
            XYZ n1 = new XYZ(e1.Y / l1, -e1.X / l1, 0);
            XYZ n2 = new XYZ(e2.Y / l2, -e2.X / l2, 0);
            XYZ bis = n1 + n2;
            double bl = Math.Sqrt(bis.X * bis.X + bis.Y * bis.Y);
            if (bl < 1e-9) return null; // giro de 180°
            double cosHalf = bl / 2.0; // |n1+n2|/2 = cos(theta/2)
            double miterLen = d / cosHalf;
            if (Math.Abs(miterLen) > Math.Abs(d) * maxMiterRatio) return null;
            r.Add(new XYZ(cur.X + bis.X / bl * miterLen, cur.Y + bis.Y / bl * miterLen, cur.Z));
        }
        return r;
    }

    public static List<XYZ> ConvexHull(IList<XYZ> pts)
    {
        var p = pts.OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
        if (p.Count < 3) return new List<XYZ>(p);
        var lower = new List<XYZ>();
        foreach (var v in p)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], v) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(v);
        }
        var upper = new List<XYZ>();
        for (int i = p.Count - 1; i >= 0; i--)
        {
            var v = p[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], v) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(v);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    /// <summary>
    /// Desfase exterior robusto: 1) inglete propio, 2) CurveLoop.CreateViaOffset de Revit,
    /// 3) inglete sobre la envolvente convexa (conservador). Devuelve también el método usado.
    /// </summary>
    public static (List<XYZ> poly, string method) OffsetOutward(IList<XYZ> ccwPoly, double d, double tol)
    {
        if (Math.Abs(d) < tol) return (new List<XYZ>(ccwPoly), "sin desfase");

        var miter = MiterOffset(ccwPoly, d);
        if (miter != null && IsSimple(miter) && SignedArea(miter) > SignedArea(ccwPoly))
            return (Clean(miter, tol), "inglete");

        try
        {
            CurveLoop loop = ToCurveLoop(ccwPoly);
            double z = ccwPoly[0].Z;
            CurveLoop off = CurveLoop.CreateViaOffset(loop, d, XYZ.BasisZ);
            var p = FromCurveLoop(off, z, tol);
            if (SignedArea(p) < SignedArea(ccwPoly))
            {
                off = CurveLoop.CreateViaOffset(loop, -d, XYZ.BasisZ);
                p = FromCurveLoop(off, z, tol);
            }
            p = EnsureCcw(p);
            if (p.Count >= 3 && IsSimple(p) && SignedArea(p) > SignedArea(ccwPoly))
                return (p, "Revit");
        }
        catch { /* continuar con envolvente convexa */ }

        var hull = ConvexHull(ccwPoly);
        var hm = MiterOffset(hull, d, 1e6) ?? hull;
        return (Clean(hm, tol), "envolvente convexa");
    }
}
