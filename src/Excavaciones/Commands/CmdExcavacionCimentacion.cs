using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Excavaciones.Core;
using Excavaciones.UI;

namespace Excavaciones.Commands;

/// <summary>
/// Excavación por cimentación: como la herramienta "Excavar" de Revit pero con sobreexcavación lateral,
/// profundidad adicional, taludes/bermas y metrado (excavación, relleno con material propio, eliminación).
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CmdExcavacionCimentacion : IExternalCommand
{
    private sealed class FoundationJob
    {
        public required Element Element { get; init; }
        public List<EnvelopeSegment> Segments { get; } = new();
        public List<Solid> Solids { get; } = new();
        public double ZBottom { get; set; } = double.MaxValue;
    }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;
        UIDocument uidoc = uiapp.ActiveUIDocument;
        Document doc = uidoc.Document;
        double tol = doc.Application.ShortCurveTolerance;

        var win = new CimentacionWindow(doc);
        RevitHelpers.SetOwner(win, uiapp);
        if (win.ShowDialog() != true) return Result.Cancelled;
        FoundationSettings s = win.Settings;

        var terrainElements = new List<Element>();
        if (!s.TerrainFromLevel)
        {
            List<Element>? picked = CommandSupport.ResolveTerrain(uidoc, s.TerrainId);
            if (picked == null) return Result.Cancelled;
            terrainElements = picked;
        }

        List<Element> foundations;
        try
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element,
                new PredicateSelectionFilter(SelectionFilters.IsFoundation),
                "Selecciona las cimentaciones a excavar y pulsa Finalizar");
            foundations = refs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList()!;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        if (foundations.Count == 0) return Result.Cancelled;

        var slope = new SlopeSpec { RatioH = s.RatioH, BermHeight = Medidas.MToFt(s.BermHeightM), BermWidth = Medidas.MToFt(s.BermWidthM) };
        var warnings = new List<string>();
        double lateral = Medidas.MToFt(s.LateralM), extra = Medidas.MToFt(s.ExtraDepthM);

        TerrainModel? terrain;
        double? zPlane = null;
        if (s.TerrainFromLevel)
        {
            // El nivel (+ desfase) es la superficie del terreno: la excavación empieza ahí y baja con el talud
            // hasta el fondo de cada cimentación (+ profundidad adicional).
            zPlane = s.TerrainLevel!.Elevation + Medidas.MToFt(s.TerrainLevelOffsetM);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue, minZ = double.MaxValue;
            foreach (Element e in foundations)
            {
                BoundingBoxXYZ? bb = e.get_BoundingBox(null);
                if (bb == null) continue;
                minX = Math.Min(minX, bb.Min.X); maxX = Math.Max(maxX, bb.Max.X);
                minY = Math.Min(minY, bb.Min.Y); maxY = Math.Max(maxY, bb.Max.Y);
                minZ = Math.Min(minZ, bb.Min.Z);
            }
            if (minX == double.MaxValue) { CommandSupport.ShowError("Las cimentaciones seleccionadas no tienen geometría."); return Result.Failed; }
            double height = Math.Max(0, zPlane.Value - minZ) + extra + Medidas.MToFt(1);
            double margin = slope.Reach(height) + lateral + Medidas.MToFt(10);
            terrain = CommandSupport.PlaneTerrain(zPlane.Value, (minX, maxX, minY, maxY), margin);
        }
        else
        {
            terrain = CommandSupport.BuildTerrain(doc, terrainElements, out string terr);
            if (terrain == null) { CommandSupport.ShowError(terr); return Result.Failed; }
        }

        using var group = new TransactionGroup(doc, "Excavación por cimentación");
        group.Start();
        var (cutMat, fillMat) = CommandSupport.Prepare(doc);
        var engine = new EarthworkEngine(terrain, tol, cutMat, fillMat);

        var progress = new ProgressWindow();
        RevitHelpers.SetOwner(progress, uiapp);
        progress.Show();

        var jobs = new List<FoundationJob>();
        var results = new List<(string name, string fuente, EarthworkResult r, double zBottom, double buried)>();
        double totalCut = 0, totalBuried = 0, totalFondo = 0, totalTalud = 0, totalTerreno = 0;
        try
        {
            // 1. Envolventes por cimentación
            int k = 0;
            foreach (Element e in foundations)
            {
                progress.Report("Construyendo envolventes…", ++k, foundations.Count);
                var job = new FoundationJob { Element = e };
                var prints = Footprint.FoundationFootprints(e, tol, out List<Solid> solids);
                job.Solids.AddRange(solids);
                if (prints.Count == 0) { warnings.Add($"{Describe(e)}: sin geometría de cimentación; omitido."); continue; }
                foreach (var (poly, z) in prints)
                {
                    var (outer, method) = PolygonUtils.OffsetOutward(poly, lateral, tol);
                    if (method == "envolvente convexa") warnings.Add($"{Describe(e)}: la sobreexcavación lateral se resolvió con envolvente convexa.");
                    double zBase = z - extra;
                    double zTop = engine.EstimateCutTopZ(outer, zBase, slope);
                    job.Segments.AddRange(engine.BuildCutEnvelope(outer, zBase, zTop, slope, warnings));
                    job.ZBottom = Math.Min(job.ZBottom, zBase);
                }
                if (job.Segments.Count > 0) jobs.Add(job);
            }
            if (jobs.Count == 0) throw new InvalidOperationException("Ninguna cimentación produjo una envolvente de excavación.");

            // 2. Cálculo (unido o por elemento)
            if (s.Merge)
            {
                progress.Report("Uniendo excavaciones adyacentes…", 0, 0);
                var merged = EarthworkEngine.Merge(jobs.SelectMany(j => j.Segments), warnings);
                var r = engine.Compute(merged, Array.Empty<EnvelopeSegment>(), (i, n) => progress.Report("Intersectando con el terreno…", i, n));
                double buried = 0;
                if (s.SubtractFoundation)
                {
                    progress.Report("Calculando volumen enterrado de cimentaciones…", 0, 0);
                    buried = engine.BuriedVolume(jobs.SelectMany(j => j.Solids));
                }
                results.Add((s.GroupName, string.Join(",", jobs.Select(j => j.Element.Id.Value)), r, jobs.Min(j => j.ZBottom), buried));
            }
            else
            {
                int i = 0;
                foreach (var job in jobs)
                {
                    var r = engine.Compute(job.Segments, Array.Empty<EnvelopeSegment>(), (a, n) => progress.Report($"Cimentación {i + 1}/{jobs.Count}: intersectando…", a, n));
                    double buried = s.SubtractFoundation ? engine.BuriedVolume(job.Solids) : 0;
                    results.Add((Describe(job.Element), job.Element.Id.Value.ToString(), r, job.ZBottom, buried));
                    i++;
                }
                // aviso de solapes
                for (int a = 0; a < jobs.Count; a++)
                    for (int b = a + 1; b < jobs.Count; b++)
                        if (jobs[a].Segments.Any(x => jobs[b].Segments.Any(y => SolidOps.BoundsOverlap(x.Bounds, y.Bounds))))
                        { warnings.Add("Hay excavaciones que se solapan; con 'Unir excavaciones adyacentes' desactivado el volumen común se cuenta dos veces."); a = jobs.Count; break; }
            }
        }
        catch (Exception ex)
        {
            progress.Close();
            group.RollBack();
            CommandSupport.ShowError("Error durante el cálculo:\n" + ex.Message);
            return Result.Failed;
        }
        progress.Close();

        foreach (var (_, _, r, _, buried) in results)
        {
            warnings.AddRange(r.Warnings);
            totalCut += r.CutVolume; totalBuried += Math.Min(buried, r.CutVolume);
            totalFondo += r.CutBottomArea; totalTalud += r.CutSlopeArea; totalTerreno += r.CutTerrainArea;
        }
        if (totalCut <= 0)
            warnings.Add(zPlane.HasValue
                ? "Las cimentaciones no quedan por debajo del nivel de terreno elegido: revisa el nivel y el desfase."
                : "Las cimentaciones no intersectan el terreno seleccionado: ¿están bajo la superficie del Toposolid elegido?");

        // 3. Metrado
        double relleno = Math.Max(0, totalCut - totalBuried);
        var rows = new List<MetradoRow>
        {
            new() { Partida = "Excavación para cimentación (volumen en banco)", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(totalCut), Nota = $"talud {slope.Label}" },
            new() { Partida = "Volumen de cimentación enterrada (descuento)", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(totalBuried), Nota = s.SubtractFoundation ? "" : "no calculado" },
            new() { Partida = "Relleno compactado con material propio", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(relleno), Nota = "excavación − cimentación" },
            new() { Partida = "Eliminación de material excedente (esponjado)", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(totalCut - relleno) * s.SwellFactor, Nota = $"(excavación − relleno) × {s.SwellFactor:0.00}" },
            new() { Partida = "Excavación esponjada (acarreo)", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(totalCut) * s.SwellFactor, Nota = $"× {s.SwellFactor:0.00}" },
            new() { Partida = "Área de fondo de excavación (nivelación/solado)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(totalFondo) },
            new() { Partida = "Área de taludes (perfilado)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(totalTalud), Nota = "incluye bermas" },
            new() { Partida = "Área de terreno natural removido", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(totalTerreno) },
        };
        if (zPlane.HasValue)
            rows.Add(new MetradoRow { Partida = "Cota de terreno de referencia (ref. punto base)", Unidad = "m",
                Cantidad = RevitHelpers.ToUserMeters(doc, zPlane.Value, ElevationReference.ProjectBasePoint),
                Nota = CommandSupport.DescribeLevelPlane(s.TerrainLevel!, s.TerrainLevelOffsetM) });
        if (results.Count > 1)
            foreach (var (name, _, r, zb, buried) in results)
                rows.Add(new MetradoRow { Partida = $"   · {name}: excavación", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(r.CutVolume), Nota = $"fondo {RevitHelpers.ToUserMeters(doc, zb, ElevationReference.ProjectBasePoint):0.00} m; cim. {Medidas.Ft3ToM3(Math.Min(buried, r.CutVolume)):0.00} m³" });

        bool anySolid = results.Any(x => x.r.CutSolids.Count > 0);
        var res = new ResultadosWindow($"Excavación por cimentación '{s.GroupName}' ({foundations.Count} elemento(s))", rows, warnings, s.CreateSolids && anySolid);
        RevitHelpers.SetOwner(res, uiapp);
        if (res.ShowDialog() != true) { group.RollBack(); return Result.Succeeded; }

        // 4. Sólidos
        var createWarnings = new List<string>();
        var created = new List<DirectShape>();
        using (var t = new Transaction(doc, "Excavación por cimentación - sólidos"))
        {
            t.Start();
            foreach (var (name, fuente, r, zb, _) in results)
            {
                string shapeName = results.Count == 1 ? $"EXC Cimentación - {s.GroupName}" : $"EXC Cimentación - {s.GroupName} - {name}";
                DirectShape? ds = CommandSupport.CreateShape(doc, shapeName, "Excavación cimentación", s.GroupName, r.CutSolids,
                    r.CutVolume, r.CutBottomArea, r.CutSlopeArea, r.CutTerrainArea, slope.Label, zb, s.SwellFactor, fuente, createWarnings);
                if (ds != null) created.Add(ds);
            }
            t.Commit();
        }

        if (s.TryExcavateToposolid && created.Count > 0)
        {
            if (zPlane.HasValue) createWarnings.Add("ExcavateBy: no aplica cuando el terreno es un plano en un nivel (no hay Toposolid).");
            else TryExcavate(doc, terrainElements, created, createWarnings);
        }

        group.Assimilate();
        if (createWarnings.Count > 0) CommandSupport.ShowError(string.Join("\n", createWarnings));
        return Result.Succeeded;
    }

    private static void TryExcavate(Document doc, List<Element> terrainElements, List<DirectShape> shapes, List<string> warnings)
    {
        var topos = terrainElements.OfType<Toposolid>().ToList();
        if (topos.Count == 0) { warnings.Add("ExcavateBy: el terreno seleccionado no es un Toposolid."); return; }
        using var t = new Transaction(doc, "Excavación por cimentación - aplicar al Toposolid");
        t.Start();
        int ok = 0;
        foreach (Toposolid topo in topos)
            foreach (DirectShape ds in shapes)
            {
                try { topo.ExcavateBy(ds.Id); ok++; }
                catch (Exception ex) { warnings.Add($"ExcavateBy falló para '{ds.Name}': {ex.Message}"); }
            }
        if (ok > 0) t.Commit(); else t.RollBack();
        if (ok == 0) warnings.Add("Revit no aceptó los sólidos de excavación como elemento de excavación del Toposolid (limitación del API); los sólidos y el metrado se mantienen.");
    }

    private static string Describe(Element e)
    {
        string type = e.Category?.Name ?? e.GetType().Name;
        string name = e.Name;
        try { Parameter? mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK); if (mark != null && !string.IsNullOrEmpty(mark.AsString())) name += $" [{mark.AsString()}]"; } catch { }
        return $"{type}: {name} (id {e.Id.Value})";
    }
}
