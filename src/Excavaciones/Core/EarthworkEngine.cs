using Autodesk.Revit.DB;

namespace Excavaciones.Core;

/// <summary>Definición de talud: H horizontales por cada 1 vertical, con bermas opcionales.</summary>
public sealed class SlopeSpec
{
    /// <summary>Relación H:1 (0 = pared vertical).</summary>
    public double RatioH { get; init; }
    /// <summary>Altura entre bermas (0 = sin bermas).</summary>
    public double BermHeight { get; init; }
    /// <summary>Ancho de berma.</summary>
    public double BermWidth { get; init; }

    public string Label => RatioH <= 1e-9 ? "Vertical" : $"{RatioH:0.##}H:1V" + (BermHeight > 1e-9 ? $" + bermas {Medidas.FtToM(BermWidth):0.00}m @ {Medidas.FtToM(BermHeight):0.00}m" : "");

    /// <summary>Alcance horizontal del talud (incluidas bermas) para una altura dada.</summary>
    public double Reach(double height)
        => height * RatioH + (BermHeight > 1e-9 ? Math.Floor(height / BermHeight) * BermWidth : 0);
}

/// <summary>Una porción (segmento vertical) de la envolvente de excavación o relleno.</summary>
public sealed class EnvelopeSegment
{
    public EnvelopeSegment(Solid solid, bool upward, IEnumerable<double> baseZs)
    {
        Solid = solid;
        Bounds = SolidOps.Bounds(solid);
        ZBottom = Bounds.min.Z;
        ZTop = Bounds.max.Z;
        Upward = upward;
        BaseZs.AddRange(baseZs);
        // Caras del sólido que no son tapas: taludes y bermas.
        foreach (Face f in solid.Faces)
        {
            if (f is not PlanarFace pf) continue;
            XYZ n = pf.FaceNormal;
            if (IsBase(pf)) continue;
            if (upward && n.Z > 0.999 && Math.Abs(pf.Origin.Z - ZTop) < 1e-4) continue;   // tapa superior (corte)
            if (!upward && n.Z < -0.999 && Math.Abs(pf.Origin.Z - ZBottom) < 1e-4) continue; // tapa inferior (relleno)
            SidePlanes.Add((pf.Origin, n));
        }
    }

    public Solid Solid { get; }
    public double ZBottom { get; }
    public double ZTop { get; }
    public (XYZ min, XYZ max) Bounds { get; }
    /// <summary>true = envolvente de corte (crece hacia arriba); false = relleno (crece hacia abajo).</summary>
    public bool Upward { get; }
    /// <summary>Cotas de fondo de excavación (corte) o de plataforma (relleno) contenidas en este sólido.</summary>
    public List<double> BaseZs { get; } = new();
    /// <summary>Caras laterales (taludes) y bermas: (origen, normal) para clasificar áreas.</summary>
    public List<(XYZ origin, XYZ normal)> SidePlanes { get; } = new();

    /// <summary>¿La cara es fondo de excavación (corte) o superficie de plataforma (relleno)?</summary>
    public bool IsBase(PlanarFace f)
    {
        double nz = f.FaceNormal.Z;
        if (Upward ? nz > -0.999 : nz < 0.999) return false;
        return BaseZs.Any(z => Math.Abs(f.Origin.Z - z) < 1e-4);
    }
}

public sealed class EarthworkResult
{
    public List<Solid> CutSolids { get; } = new();
    public List<Solid> FillSolids { get; } = new();
    public double CutVolume { get; set; }
    public double FillVolume { get; set; }
    public double CutBottomArea { get; set; }
    public double CutSlopeArea { get; set; }
    public double CutTerrainArea { get; set; }
    public double FillTopArea { get; set; }
    public double FillSlopeArea { get; set; }
    public double FillTerrainArea { get; set; }
    public int TrianglesProcessed { get; set; }
    public int FailedOperations { get; set; }
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Motor de movimiento de tierras. Corte = terreno ∩ envolvente de corte (sobre la plataforma);
/// Relleno = (aire sobre el terreno) ∩ envolvente de relleno (bajo la plataforma).
/// El terreno se procesa triángulo a triángulo (columna vertical ∩ semiespacio bajo/sobre el plano del triángulo),
/// lo que mantiene cada booleana pequeña y robusta.
/// </summary>
public sealed class EarthworkEngine
{
    private readonly TerrainModel _terrain;
    private readonly double _tol;
    private readonly ElementId _cutMaterial;
    private readonly ElementId _fillMaterial;

    public EarthworkEngine(TerrainModel terrain, double shortCurveTolerance, ElementId cutMaterial, ElementId fillMaterial)
    {
        _terrain = terrain;
        _tol = shortCurveTolerance;
        _cutMaterial = cutMaterial;
        _fillMaterial = fillMaterial;
    }

    // ------------------------------------------------------------------ envolventes

    /// <summary>Cota superior necesaria para que los taludes de corte "salgan" al terreno (daylight).</summary>
    public double EstimateCutTopZ(IList<XYZ> basePoly, double zBase, SlopeSpec slope)
    {
        double zTop = zBase + Medidas.MToFt(0.5);
        for (int iter = 0; iter < 12; iter++)
        {
            double reach = (zTop - zBase) * slope.RatioH + BermReach(zTop - zBase, slope);
            var (minX, maxX, minY, maxY) = PolygonUtils.Bounds(basePoly);
            var (_, hi) = _terrain.ZRange(minX - reach, maxX + reach, minY - reach, maxY + reach);
            double needed = hi + Medidas.MToFt(0.5);
            if (needed <= zTop + 1e-6) return zTop;
            zTop = needed;
        }
        return zTop;
    }

    public double EstimateFillBottomZ(IList<XYZ> basePoly, double zBase, SlopeSpec slope)
    {
        double zBot = zBase - Medidas.MToFt(0.5);
        for (int iter = 0; iter < 12; iter++)
        {
            double reach = (zBase - zBot) * slope.RatioH + BermReach(zBase - zBot, slope);
            var (minX, maxX, minY, maxY) = PolygonUtils.Bounds(basePoly);
            var (lo, _) = _terrain.ZRange(minX - reach, maxX + reach, minY - reach, maxY + reach);
            double needed = lo - Medidas.MToFt(0.5);
            if (needed >= zBot - 1e-6) return zBot;
            zBot = needed;
        }
        return zBot;
    }

    private static double BermReach(double height, SlopeSpec s)
        => s.BermHeight > 1e-9 ? Math.Floor(height / s.BermHeight) * s.BermWidth : 0;

    /// <summary>
    /// Construye la envolvente de corte: desde el polígono base en zBase hacia arriba, abriéndose con el talud,
    /// con bermas horizontales cada BermHeight. Cada tramo es un sólido independiente.
    /// </summary>
    public List<EnvelopeSegment> BuildCutEnvelope(IList<XYZ> basePoly, double zBase, double zTop, SlopeSpec slope, List<string> warnings)
        => BuildEnvelope(basePoly, zBase, zTop, slope, upward: true, _cutMaterial, warnings);

    /// <summary>Envolvente de relleno: desde la plataforma hacia abajo, abriéndose con el talud de relleno.</summary>
    public List<EnvelopeSegment> BuildFillEnvelope(IList<XYZ> basePoly, double zBase, double zBottom, SlopeSpec slope, List<string> warnings)
        => BuildEnvelope(basePoly, zBase, zBottom, slope, upward: false, _fillMaterial, warnings);

    private List<EnvelopeSegment> BuildEnvelope(IList<XYZ> basePoly, double zStart, double zEnd, SlopeSpec slope, bool upward, ElementId mat, List<string> warnings)
    {
        var segments = new List<EnvelopeSegment>();
        double total = Math.Abs(zEnd - zStart);
        if (total <= 1e-9) return segments;
        // Sin bermas se trocea igualmente en tramos de ≤ 3 m: el desfase se resuelve por partes y, si en algún
        // tramo alto hay que recurrir a la envolvente convexa, los tramos bajos conservan el contorno exacto.
        double step = slope.BermHeight > 1e-9 ? slope.BermHeight : Math.Min(total, Medidas.MToFt(3.0));
        List<XYZ> current = PolygonUtils.EnsureCcw(basePoly);
        double covered = 0;
        int guard = 0;
        while (covered < total - 1e-9 && guard++ < 500)
        {
            double h = Math.Min(step, total - covered);
            double offset = h * slope.RatioH;
            List<XYZ> outer; string method;
            if (offset > _tol)
            {
                (outer, method) = PolygonUtils.OffsetOutward(current, offset, _tol);
                if (method == "envolvente convexa")
                    warnings.Add($"Tramo {segments.Count + 1}: el desfase del talud ({Medidas.FtToM(offset):0.00} m) no pudo resolverse sobre el contorno original; se usó la envolvente convexa (resultado conservador).");
            }
            else { outer = current; method = "vertical"; }

            double z0 = upward ? zStart + covered : zStart - covered - h;
            double z1 = z0 + h;
            List<XYZ> bottom = upward ? current : outer;
            List<XYZ> top = upward ? outer : current;

            Solid? solid = offset > _tol ? SolidOps.Loft(bottom, z0, top, z1, mat) : SolidOps.Extrude(current, z0, h, mat);
            if (solid == null && offset > _tol)
            {
                // Reintento con envolvente convexa (loft más simple)
                var hull = PolygonUtils.ConvexHull(current);
                var hullOff = PolygonUtils.MiterOffset(hull, offset, 1e6) ?? hull;
                solid = upward ? SolidOps.Loft(hull, z0, hullOff, z1, mat) : SolidOps.Loft(hullOff, z0, hull, z1, mat);
                if (solid != null)
                {
                    warnings.Add($"Tramo {segments.Count + 1}: el loft del talud falló con el contorno original; se usó la envolvente convexa.");
                    outer = hullOff;
                }
            }
            if (solid == null)
            {
                warnings.Add($"Tramo {segments.Count + 1}: no se pudo generar la envolvente del talud; se usó pared vertical en ese tramo.");
                solid = SolidOps.Extrude(current, z0, h, mat);
                outer = current;
            }
            if (solid == null) { warnings.Add("No se pudo construir la envolvente de la excavación."); break; }

            bool first = segments.Count == 0;
            segments.Add(new EnvelopeSegment(solid, upward, first ? new[] { zStart } : Array.Empty<double>()));

            covered += h;
            current = outer;
            // berma horizontal
            if (slope.BermHeight > 1e-9 && slope.BermWidth > _tol && covered < total - 1e-9)
            {
                var (bermPoly, _) = PolygonUtils.OffsetOutward(current, slope.BermWidth, _tol);
                current = bermPoly;
            }
        }
        return segments;
    }

    /// <summary>
    /// Fusiona envolventes que se solapan (excavaciones adyacentes) para no contar dos veces el mismo volumen.
    /// Los sólidos que no pueden unirse se mantienen separados.
    /// </summary>
    public static List<EnvelopeSegment> Merge(IEnumerable<EnvelopeSegment> segments, List<string> warnings)
    {
        var pool = new List<(Solid solid, List<double> baseZs, bool upward)>();
        int failed = 0;
        foreach (var seg in segments)
        {
            Solid current = seg.Solid;
            var bases = new List<double>(seg.BaseZs);
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].upward != seg.Upward) continue;
                    if (!SolidOps.BoundsOverlap(SolidOps.Bounds(current), SolidOps.Bounds(pool[i].solid))) continue;
                    Solid? u = SolidOps.Union(current, pool[i].solid);
                    if (u == null) { failed++; continue; }
                    current = u;
                    bases.AddRange(pool[i].baseZs);
                    pool.RemoveAt(i);
                    merged = true;
                    break;
                }
            }
            pool.Add((current, bases, seg.Upward));
        }
        if (failed > 0) warnings.Add($"{failed} unión(es) de excavaciones adyacentes fallaron; esas zonas se metran por separado (posible doble conteo en el solape).");
        return pool.Select(p => new EnvelopeSegment(p.solid, p.upward, p.baseZs.Distinct())).ToList();
    }

    // ------------------------------------------------------------------ cálculo

    /// <summary>Calcula corte y relleno contra el terreno para las envolventes dadas.</summary>
    public EarthworkResult Compute(IList<EnvelopeSegment> cutSegments, IList<EnvelopeSegment> fillSegments, Action<int, int>? progress = null)
    {
        var result = new EarthworkResult();
        if (cutSegments.Count == 0 && fillSegments.Count == 0) return result;

        // Región de interés en planta
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var s in cutSegments.Concat(fillSegments))
        {
            minX = Math.Min(minX, s.Bounds.min.X); maxX = Math.Max(maxX, s.Bounds.max.X);
            minY = Math.Min(minY, s.Bounds.min.Y); maxY = Math.Max(maxY, s.Bounds.max.Y);
            minZ = Math.Min(minZ, s.Bounds.min.Z); maxZ = Math.Max(maxZ, s.Bounds.max.Z);
        }
        double zLow = Math.Min(minZ, _terrain.MinZ) - Medidas.MToFt(1);
        double zHigh = Math.Max(maxZ, _terrain.MaxZ) + Medidas.MToFt(1);
        double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (zHigh - zLow) * (zHigh - zLow));
        double halfSize = Math.Max(diag * 2, Medidas.MToFt(10));

        var tris = _terrain.InRange(minX, maxX, minY, maxY).ToList();
        int done = 0;
        foreach (TerrainTriangle tri in tris)
        {
            progress?.Invoke(++done, tris.Count);
            var triPoly = new List<XYZ> { tri.A, tri.B, tri.C };
            Solid? prism = SolidOps.Extrude(triPoly, zLow, zHigh - zLow, _cutMaterial);
            if (prism == null) { result.FailedOperations++; continue; }
            var prismBounds = SolidOps.Bounds(prism);
            XYZ c = tri.Centroid;

            Solid? ground = null, air = null;
            foreach (var seg in cutSegments)
            {
                if (!SolidOps.BoundsOverlap(prismBounds, seg.Bounds)) continue;
                if (seg.ZBottom > tri.MaxZ) continue; // tramo completamente sobre el terreno en esta zona
                Solid? col = SolidOps.Intersect(seg.Solid, prism);
                if (col == null) continue;
                ground ??= SolidOps.HalfSpace(c, -tri.Normal, halfSize, _cutMaterial);
                if (ground == null) { result.FailedOperations++; continue; }
                Solid? piece = SolidOps.Intersect(col, ground);
                if (piece == null) continue;
                result.CutSolids.Add(piece);
                result.CutVolume += piece.Volume;
                ClassifyCutFaces(piece, tri, seg, result);
            }
            foreach (var seg in fillSegments)
            {
                if (!SolidOps.BoundsOverlap(prismBounds, seg.Bounds)) continue;
                if (seg.ZTop < tri.MinZ) continue; // tramo completamente bajo el terreno
                Solid? col = SolidOps.Intersect(seg.Solid, prism);
                if (col == null) continue;
                air ??= SolidOps.HalfSpace(c, tri.Normal, halfSize, _fillMaterial);
                if (air == null) { result.FailedOperations++; continue; }
                Solid? piece = SolidOps.Intersect(col, air);
                if (piece == null) continue;
                result.FillSolids.Add(piece);
                result.FillVolume += piece.Volume;
                ClassifyFillFaces(piece, tri, seg, result);
            }
            result.TrianglesProcessed++;
        }
        if (result.FailedOperations > 0)
            result.Warnings.Add($"{result.FailedOperations} operación(es) geométrica(s) fallaron y se omitieron; el volumen puede estar ligeramente subestimado.");
        return result;
    }

    private static bool OnPlane(PlanarFace f, XYZ origin, XYZ normal)
    {
        if (f.FaceNormal.DotProduct(normal) < 0.9999) return false;
        return Math.Abs((f.Origin - origin).DotProduct(normal)) < 1e-4;
    }

    private static void ClassifyCutFaces(Solid piece, TerrainTriangle tri, EnvelopeSegment seg, EarthworkResult r)
    {
        foreach (Face face in piece.Faces)
        {
            if (face is not PlanarFace f) continue;
            if (OnPlane(f, tri.A, tri.Normal)) { r.CutTerrainArea += f.Area; continue; }
            if (seg.IsBase(f)) { r.CutBottomArea += f.Area; continue; }
            foreach (var (o, n) in seg.SidePlanes)
                if (OnPlane(f, o, n)) { r.CutSlopeArea += f.Area; break; }
        }
    }

    private static void ClassifyFillFaces(Solid piece, TerrainTriangle tri, EnvelopeSegment seg, EarthworkResult r)
    {
        foreach (Face face in piece.Faces)
        {
            if (face is not PlanarFace f) continue;
            if (OnPlane(f, tri.A, -tri.Normal)) { r.FillTerrainArea += f.Area; continue; }
            if (seg.IsBase(f)) { r.FillTopArea += f.Area; continue; }
            foreach (var (o, n) in seg.SidePlanes)
                if (OnPlane(f, o, n)) { r.FillSlopeArea += f.Area; break; }
        }
    }

    /// <summary>Volumen de los sólidos dados que queda bajo la superficie del terreno (p. ej. cimentación enterrada).</summary>
    public double BuriedVolume(IEnumerable<Solid> solids)
    {
        double total = 0;
        foreach (Solid s in solids)
        {
            var b = SolidOps.Bounds(s);
            double zLow = Math.Min(b.min.Z, _terrain.MinZ) - Medidas.MToFt(1);
            double zHigh = Math.Max(b.max.Z, _terrain.MaxZ) + Medidas.MToFt(1);
            double size = Math.Max(b.min.DistanceTo(b.max) * 2, Medidas.MToFt(10));
            foreach (var tri in _terrain.InRange(b.min.X, b.max.X, b.min.Y, b.max.Y))
            {
                if (tri.MinZ > b.max.Z) { // terreno completamente encima: toda la columna está enterrada
                    Solid? prismFull = SolidOps.Extrude(new List<XYZ> { tri.A, tri.B, tri.C }, zLow, zHigh - zLow, _cutMaterial);
                    if (prismFull == null) continue;
                    Solid? full = SolidOps.Intersect(s, prismFull);
                    if (full != null) total += full.Volume;
                    continue;
                }
                Solid? prism = SolidOps.Extrude(new List<XYZ> { tri.A, tri.B, tri.C }, zLow, zHigh - zLow, _cutMaterial);
                if (prism == null) continue;
                Solid? col = SolidOps.Intersect(s, prism);
                if (col == null) continue;
                Solid? ground = SolidOps.HalfSpace(tri.Centroid, -tri.Normal, size, _cutMaterial);
                if (ground == null) continue;
                Solid? piece = SolidOps.Intersect(col, ground);
                if (piece != null) total += piece.Volume;
            }
        }
        return total;
    }
}
