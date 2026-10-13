using Autodesk.Revit.DB;

namespace Excavaciones.Core;

/// <summary>Un triángulo de la superficie superior del terreno, orientado con normal hacia arriba.</summary>
public sealed class TerrainTriangle
{
    public TerrainTriangle(XYZ a, XYZ b, XYZ c)
    {
        A = a; B = b; C = c;
        XYZ n = (b - a).CrossProduct(c - a);
        if (n.Z < 0) { (B, C) = (c, b); n = -n; }
        Normal = n.Normalize();
        MinX = Math.Min(A.X, Math.Min(B.X, C.X));
        MaxX = Math.Max(A.X, Math.Max(B.X, C.X));
        MinY = Math.Min(A.Y, Math.Min(B.Y, C.Y));
        MaxY = Math.Max(A.Y, Math.Max(B.Y, C.Y));
        MinZ = Math.Min(A.Z, Math.Min(B.Z, C.Z));
        MaxZ = Math.Max(A.Z, Math.Max(B.Z, C.Z));
        Area2D = Math.Abs((B.X - A.X) * (C.Y - A.Y) - (C.X - A.X) * (B.Y - A.Y)) / 2.0;
    }

    public XYZ A { get; }
    public XYZ B { get; }
    public XYZ C { get; }
    public XYZ Normal { get; }
    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }
    public double MinZ { get; }
    public double MaxZ { get; }
    /// <summary>Área proyectada en planta.</summary>
    public double Area2D { get; }

    public XYZ Centroid => (A + B + C) / 3.0;

    public bool OverlapsXY(double minX, double maxX, double minY, double maxY)
        => MaxX >= minX && MinX <= maxX && MaxY >= minY && MinY <= maxY;

    /// <summary>Elevación del plano del triángulo en (x,y) si el punto cae dentro de él en planta.</summary>
    public double? ElevationAt(double x, double y)
    {
        double d = (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);
        if (Math.Abs(d) < 1e-12) return null;
        double l1 = ((B.Y - C.Y) * (x - C.X) + (C.X - B.X) * (y - C.Y)) / d;
        double l2 = ((C.Y - A.Y) * (x - C.X) + (A.X - C.X) * (y - C.Y)) / d;
        double l3 = 1 - l1 - l2;
        const double eps = -1e-6;
        if (l1 < eps || l2 < eps || l3 < eps) return null;
        return l1 * A.Z + l2 * B.Z + l3 * C.Z;
    }
}

/// <summary>
/// Superficie del terreno natural como un campo de alturas triangulado, extraído de la cara superior
/// de uno o más elementos (Toposolid, suelos, masas, DirectShape, importaciones con mallas).
/// </summary>
public sealed class TerrainModel
{
    private readonly List<TerrainTriangle> _triangles = new();

    public IReadOnlyList<TerrainTriangle> Triangles => _triangles;
    public double MinZ { get; private set; } = double.MaxValue;
    public double MaxZ { get; private set; } = double.MinValue;
    public double MinX { get; private set; } = double.MaxValue;
    public double MaxX { get; private set; } = double.MinValue;
    public double MinY { get; private set; } = double.MaxValue;
    public double MaxY { get; private set; } = double.MinValue;
    public List<ElementId> SourceIds { get; } = new();

    /// <summary>True si el terreno es un plano horizontal definido por un nivel (sin topografía).</summary>
    public bool IsPlane { get; private set; }

    /// <summary>
    /// Terreno de referencia plano: un rectángulo horizontal a la cota <paramref name="z"/> (p. ej. la de un nivel)
    /// que cubre la zona indicada. Se usa cuando el nivel define la parte superior de la excavación.
    /// </summary>
    public static TerrainModel FromPlane(double z, double minX, double maxX, double minY, double maxY)
    {
        var model = new TerrainModel { IsPlane = true };
        var p00 = new XYZ(minX, minY, z);
        var p10 = new XYZ(maxX, minY, z);
        var p11 = new XYZ(maxX, maxY, z);
        var p01 = new XYZ(minX, maxY, z);
        model.Add(new TerrainTriangle(p00, p10, p11));
        model.Add(new TerrainTriangle(p00, p11, p01));
        return model;
    }

    public static TerrainModel FromElements(IEnumerable<Element> elements, double shortCurveTolerance)
    {
        var model = new TerrainModel();
        var options = new Options
        {
            DetailLevel = ViewDetailLevel.Fine,
            ComputeReferences = false,
            IncludeNonVisibleObjects = false
        };

        foreach (Element e in elements)
        {
            model.SourceIds.Add(e.Id);
            GeometryElement? ge = e.get_Geometry(options);
            if (ge == null) continue;
            model.AddGeometry(ge, Transform.Identity, shortCurveTolerance);
        }
        return model;
    }

    private void AddGeometry(GeometryElement ge, Transform t, double tol)
    {
        foreach (GeometryObject go in ge)
        {
            switch (go)
            {
                case Solid s when s.Volume > 1e-9 || s.Faces.Size > 0:
                    AddSolid(s, t, tol);
                    break;
                case Mesh m:
                    AddMesh(m, t, tol, null);
                    break;
                case GeometryInstance gi:
                    AddGeometry(gi.GetSymbolGeometry(), t.Multiply(gi.Transform), tol);
                    break;
            }
        }
    }

    private void AddSolid(Solid s, Transform t, double tol)
    {
        foreach (Face f in s.Faces)
        {
            XYZ? n = FaceNormal(f);
            if (n == null) continue;
            n = t.OfVector(n);
            if (n.Z <= 0.01) continue; // solo caras que miran hacia arriba (superficie del terreno)
            Mesh mesh;
            try { mesh = f.Triangulate(); } catch { continue; }
            AddMesh(mesh, t, tol, n);
        }
    }

    private static XYZ? FaceNormal(Face f)
    {
        try
        {
            if (f is PlanarFace pf) return pf.FaceNormal;
            BoundingBoxUV bb = f.GetBoundingBox();
            UV mid = new UV((bb.Min.U + bb.Max.U) / 2, (bb.Min.V + bb.Max.V) / 2);
            return f.ComputeNormal(mid);
        }
        catch { return null; }
    }

    private void AddMesh(Mesh mesh, Transform t, double tol, XYZ? referenceNormal)
    {
        for (int i = 0; i < mesh.NumTriangles; i++)
        {
            MeshTriangle mt = mesh.get_Triangle(i);
            XYZ a = t.OfPoint(mt.get_Vertex(0));
            XYZ b = t.OfPoint(mt.get_Vertex(1));
            XYZ c = t.OfPoint(mt.get_Vertex(2));
            if (a.DistanceTo(b) < tol || b.DistanceTo(c) < tol || c.DistanceTo(a) < tol) continue;
            XYZ n = (b - a).CrossProduct(c - a);
            if (n.GetLength() < 1e-9) continue;
            n = n.Normalize();
            if (referenceNormal == null)
            {
                // Malla suelta (importación): se asume superficie simple; descartar caras verticales.
                if (Math.Abs(n.Z) <= 0.01) continue;
            }
            var tri = new TerrainTriangle(a, b, c);
            if (tri.Area2D < 1e-7) continue;
            Add(tri);
        }
    }

    private void Add(TerrainTriangle tri)
    {
        _triangles.Add(tri);
        MinZ = Math.Min(MinZ, tri.MinZ); MaxZ = Math.Max(MaxZ, tri.MaxZ);
        MinX = Math.Min(MinX, tri.MinX); MaxX = Math.Max(MaxX, tri.MaxX);
        MinY = Math.Min(MinY, tri.MinY); MaxY = Math.Max(MaxY, tri.MaxY);
    }

    public bool IsEmpty => _triangles.Count == 0;

    public IEnumerable<TerrainTriangle> InRange(double minX, double maxX, double minY, double maxY)
        => _triangles.Where(t => t.OverlapsXY(minX, maxX, minY, maxY));

    /// <summary>Cota máxima / mínima del terreno dentro de un rectángulo en planta.</summary>
    public (double min, double max) ZRange(double minX, double maxX, double minY, double maxY)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var t in InRange(minX, maxX, minY, maxY))
        {
            lo = Math.Min(lo, t.MinZ);
            hi = Math.Max(hi, t.MaxZ);
        }
        if (lo == double.MaxValue) return (MinZ, MaxZ);
        return (lo, hi);
    }

    public double? ElevationAt(double x, double y)
    {
        foreach (var t in _triangles)
        {
            if (x < t.MinX || x > t.MaxX || y < t.MinY || y > t.MaxY) continue;
            double? z = t.ElevationAt(x, y);
            if (z.HasValue) return z;
        }
        return null;
    }
}
