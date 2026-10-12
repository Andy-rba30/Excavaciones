using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB;

namespace Excavaciones.Core;

/// <summary>Obtención de contornos en planta a partir de elementos de Revit.</summary>
public static class Footprint
{
    /// <summary>Contorno cerrado de plataforma desde curvas de modelo/detalle, regiones rellenas, suelos, áreas o habitaciones.</summary>
    public static List<XYZ>? PlatformBoundary(Document doc, IList<Element> elements, double tol, out string error)
    {
        error = string.Empty;
        var curves = new List<Curve>();
        var loops = new List<List<XYZ>>();
        foreach (Element e in elements)
        {
            switch (e)
            {
                case ModelCurve mc: curves.Add(mc.GeometryCurve); break;
                case DetailCurve dc: curves.Add(dc.GeometryCurve); break;
                case FilledRegion fr:
                    foreach (CurveLoop l in fr.GetBoundaries()) loops.Add(PolygonUtils.FromCurveLoop(l, 0, tol));
                    break;
                case Area or Room:
                    {
                        var se = (SpatialElement)e;
                        var opt = new SpatialElementBoundaryOptions();
                        foreach (var segs in se.GetBoundarySegments(opt))
                        {
                            var pts = new List<XYZ>();
                            foreach (var s in segs) { var t = s.GetCurve().Tessellate(); for (int i = 0; i < t.Count - 1; i++) pts.Add(new XYZ(t[i].X, t[i].Y, 0)); }
                            loops.Add(PolygonUtils.Clean(pts, tol));
                        }
                        break;
                    }
                default:
                    foreach (var p in TopFaceLoops(e, tol)) loops.Add(p);
                    break;
            }
        }
        if (curves.Count > 0)
        {
            CurveLoop? loop = PolygonUtils.JoinCurves(curves, tol * 10);
            if (loop == null) { error = "Las curvas seleccionadas no forman un contorno cerrado único."; return null; }
            loops.Add(PolygonUtils.FromCurveLoop(loop, 0, tol));
        }
        var best = loops.Where(l => l.Count >= 3).OrderByDescending(PolygonUtils.Area).FirstOrDefault();
        if (best == null) { error = "No se encontró un contorno cerrado válido en la selección."; return null; }
        if (!PolygonUtils.IsSimple(best)) { error = "El contorno se cruza a sí mismo; corrígelo antes de continuar."; return null; }
        return PolygonUtils.EnsureCcw(best);
    }

    private static IEnumerable<List<XYZ>> TopFaceLoops(Element e, double tol)
    {
        foreach (Solid s in SolidOps.ElementSolids(e))
            foreach (Face f in s.Faces)
                if (f is PlanarFace pf && pf.FaceNormal.Z > 0.99)
                    foreach (CurveLoop l in pf.GetEdgesAsCurveLoops())
                        yield return PolygonUtils.FromCurveLoop(l, 0, tol);
    }

    /// <summary>Huella de una cimentación: caras inferiores (normal -Z) agrupadas por cota, contorno exterior de cada una.</summary>
    public static List<(List<XYZ> poly, double z)> FoundationFootprints(Element e, double tol, out List<Solid> solids)
    {
        solids = SolidOps.ElementSolids(e);
        var result = new List<(List<XYZ>, double)>();
        foreach (Solid s in solids)
        {
            foreach (Face f in s.Faces)
            {
                if (f is not PlanarFace pf || pf.FaceNormal.Z > -0.99) continue;
                double z = pf.Origin.Z;
                List<XYZ>? outer = null; double maxA = 0;
                foreach (CurveLoop l in pf.GetEdgesAsCurveLoops())
                {
                    var p = PolygonUtils.FromCurveLoop(l, z, tol);
                    double a = PolygonUtils.Area(p);
                    if (a > maxA) { maxA = a; outer = p; }
                }
                if (outer != null && outer.Count >= 3) result.Add((PolygonUtils.EnsureCcw(outer), z));
            }
        }
        if (result.Count == 0 && solids.Count > 0)
        {
            // Sin caras planas hacia abajo (p. ej. pilotes cónicos): usar envolvente convexa de la base de la caja
            foreach (Solid s in solids)
            {
                var b = SolidOps.Bounds(s);
                var poly = new List<XYZ> { new(b.min.X, b.min.Y, b.min.Z), new(b.max.X, b.min.Y, b.min.Z), new(b.max.X, b.max.Y, b.min.Z), new(b.min.X, b.max.Y, b.min.Z) };
                result.Add((poly, b.min.Z));
            }
        }
        return result;
    }
}
