using Autodesk.Revit.DB;

namespace Excavaciones.Core;

/// <summary>Creación de sólidos y operaciones booleanas con tolerancia a fallos.</summary>
public static class SolidOps
{
    public const double MinVolume = 1e-7; // pies³

    public static Solid? Extrude(IList<XYZ> poly, double zBottom, double height, ElementId materialId)
    {
        if (height <= 1e-9) return null;
        try
        {
            var loop = PolygonUtils.ToCurveLoop(PolygonUtils.SetZ(poly, zBottom));
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop }, XYZ.BasisZ, height, new SolidOptions(materialId, ElementId.InvalidElementId));
        }
        catch { return null; }
    }

    public static Solid? Loft(IList<XYZ> bottom, double zBottom, IList<XYZ> top, double zTop, ElementId materialId)
    {
        if (zTop - zBottom <= 1e-9) return null;
        try
        {
            var l0 = PolygonUtils.ToCurveLoop(PolygonUtils.SetZ(bottom, zBottom));
            var l1 = PolygonUtils.ToCurveLoop(PolygonUtils.SetZ(top, zTop));
            return GeometryCreationUtilities.CreateLoftGeometry(
                new List<CurveLoop> { l0, l1 }, new SolidOptions(materialId, ElementId.InvalidElementId));
        }
        catch { return null; }
    }

    /// <summary>Semiespacio aproximado: caja grande apoyada en el plano (origen, normal) y extruida hacia la normal.</summary>
    public static Solid? HalfSpace(XYZ origin, XYZ normal, double size, ElementId materialId)
    {
        try
        {
            Plane pl = Plane.CreateByNormalAndOrigin(normal, origin);
            XYZ u = pl.XVec * size, v = pl.YVec * size;
            var loop = new CurveLoop();
            XYZ p0 = origin - u - v, p1 = origin + u - v, p2 = origin + u + v, p3 = origin - u + v;
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop }, normal, 2 * size, new SolidOptions(materialId, ElementId.InvalidElementId));
        }
        catch { return null; }
    }

    public static Solid? Intersect(Solid a, Solid b) => Boolean(a, b, BooleanOperationsType.Intersect);
    public static Solid? Union(Solid a, Solid b) => Boolean(a, b, BooleanOperationsType.Union);
    public static Solid? Difference(Solid a, Solid b) => Boolean(a, b, BooleanOperationsType.Difference);

    private static Solid? Boolean(Solid a, Solid b, BooleanOperationsType op)
    {
        try
        {
            Solid r = BooleanOperationsUtils.ExecuteBooleanOperation(a, b, op);
            if (r == null || r.Faces.Size == 0) return null;
            if (op != BooleanOperationsType.Union && r.Volume < MinVolume) return null;
            return r;
        }
        catch { return null; }
    }

    public static (XYZ min, XYZ max) Bounds(Solid s)
    {
        BoundingBoxXYZ bb = s.GetBoundingBox();
        Transform t = bb.Transform;
        XYZ[] corners =
        {
            t.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z)), t.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z)),
            t.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)), t.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z)),
            t.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z)), t.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z)),
            t.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z)), t.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)),
        };
        double minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X);
        double minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y);
        double minZ = corners.Min(c => c.Z), maxZ = corners.Max(c => c.Z);
        return (new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
    }

    public static bool BoundsOverlap((XYZ min, XYZ max) a, (XYZ min, XYZ max) b)
        => a.max.X >= b.min.X && a.min.X <= b.max.X && a.max.Y >= b.min.Y && a.min.Y <= b.max.Y && a.max.Z >= b.min.Z && a.min.Z <= b.max.Z;

    /// <summary>Todos los sólidos (con volumen) de un elemento, en coordenadas de proyecto.</summary>
    public static List<Solid> ElementSolids(Element e)
    {
        var list = new List<Solid>();
        var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
        GeometryElement? ge = e.get_Geometry(opt);
        if (ge == null) return list;
        Collect(ge, list);
        return list;
    }

    private static void Collect(GeometryElement ge, List<Solid> list)
    {
        foreach (GeometryObject go in ge)
        {
            if (go is Solid s && s.Volume > MinVolume) list.Add(s);
            else if (go is GeometryInstance gi) Collect(gi.GetInstanceGeometry(), list);
        }
    }
}
