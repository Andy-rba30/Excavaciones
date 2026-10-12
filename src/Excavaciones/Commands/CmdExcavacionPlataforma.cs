using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Excavaciones.Core;
using Excavaciones.UI;

namespace Excavaciones.Commands;

/// <summary>
/// Excavación tipo civil: plataforma a una cota dada dentro de un contorno, con taludes de corte y relleno
/// (y bermas), calculando corte y relleno contra el terreno natural.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CmdExcavacionPlataforma : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;
        UIDocument uidoc = uiapp.ActiveUIDocument;
        Document doc = uidoc.Document;
        double tol = doc.Application.ShortCurveTolerance;

        var win = new PlataformaWindow(doc);
        RevitHelpers.SetOwner(win, uiapp);
        if (win.ShowDialog() != true) return Result.Cancelled;
        PlatformSettings s = win.Settings;

        // 1. Terreno
        List<Element>? terrainElements = CommandSupport.ResolveTerrain(uidoc, s.TerrainId);
        if (terrainElements == null) return Result.Cancelled;

        // 2. Contorno de la plataforma
        List<XYZ>? boundary;
        try
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element,
                new PredicateSelectionFilter(SelectionFilters.IsBoundary),
                "Selecciona el contorno de la plataforma (líneas cerradas, región rellena, suelo, área o habitación) y pulsa Finalizar");
            var picked = refs.Select(r => doc.GetElement(r)).ToList();
            boundary = Footprint.PlatformBoundary(doc, picked, tol, out string err);
            if (boundary == null) { CommandSupport.ShowError(err); return Result.Failed; }
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

        // 3. Cota de plataforma
        double zBase = s.UseLevel && s.Level != null
            ? s.Level.Elevation + Medidas.MToFt(s.LevelOffsetM)
            : RevitHelpers.ToInternalZ(doc, s.ElevationM, s.Reference);

        TerrainModel? terrain = CommandSupport.BuildTerrain(doc, terrainElements, out string terr);
        if (terrain == null) { CommandSupport.ShowError(terr); return Result.Failed; }

        using var group = new TransactionGroup(doc, "Excavación de plataforma");
        group.Start();
        var (cutMat, fillMat) = CommandSupport.Prepare(doc);

        var engine = new EarthworkEngine(terrain, tol, cutMat, fillMat);
        var cutSlope = new SlopeSpec { RatioH = s.CutRatioH, BermHeight = Medidas.MToFt(s.CutBermHeightM), BermWidth = Medidas.MToFt(s.CutBermWidthM) };
        var fillSlope = new SlopeSpec { RatioH = s.FillRatioH };
        var warnings = new List<string>();

        var progress = new ProgressWindow();
        RevitHelpers.SetOwner(progress, uiapp);
        progress.Show();
        EarthworkResult result;
        try
        {
            progress.Report("Construyendo envolventes de corte y relleno…", 0, 0);
            double zTop = engine.EstimateCutTopZ(boundary, zBase, cutSlope);
            double zBottom = engine.EstimateFillBottomZ(boundary, zBase, fillSlope);
            var cutSegs = engine.BuildCutEnvelope(boundary, zBase, zTop, cutSlope, warnings);
            var fillSegs = engine.BuildFillEnvelope(boundary, zBase, zBottom, fillSlope, warnings);
            result = engine.Compute(cutSegs, fillSegs, (i, n) => progress.Report("Intersectando con el terreno…", i, n));
        }
        catch (Exception ex)
        {
            progress.Close();
            group.RollBack();
            CommandSupport.ShowError("Error durante el cálculo:\n" + ex.Message);
            return Result.Failed;
        }
        progress.Close();
        warnings.AddRange(result.Warnings);
        if (result.CutSolids.Count == 0 && result.FillSolids.Count == 0)
            warnings.Add("La plataforma no intersecta el terreno seleccionado: revisa la cota, la referencia de cotas y el contorno.");

        // 4. Metrado
        double balance = result.CutVolume - result.FillVolume;
        double cotaUser = RevitHelpers.ToUserMeters(doc, zBase, ElevationReference.ProjectBasePoint);
        var rows = new List<MetradoRow>
        {
            new() { Partida = "Corte de terreno natural (volumen en banco)", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(result.CutVolume), Nota = $"talud {cutSlope.Label}" },
            new() { Partida = "Relleno compactado (volumen compactado)", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(result.FillVolume), Nota = $"talud {fillSlope.Label}" },
            new() { Partida = balance >= 0 ? "Eliminación de material excedente (esponjado)" : "Material de préstamo requerido (compactado)", Unidad = "m³",
                    Cantidad = balance >= 0 ? Medidas.Ft3ToM3(balance) * s.SwellFactor : Medidas.Ft3ToM3(-balance), Nota = balance >= 0 ? $"(corte − relleno) × {s.SwellFactor:0.00}" : "relleno − corte" },
            new() { Partida = "Corte esponjado (para acarreo)", Unidad = "m³", Cantidad = Medidas.Ft3ToM3(result.CutVolume) * s.SwellFactor, Nota = $"× {s.SwellFactor:0.00}" },
            new() { Partida = "Área de fondo de corte (plataforma en corte)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(result.CutBottomArea) },
            new() { Partida = "Área de taludes de corte (perfilado)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(result.CutSlopeArea), Nota = "incluye bermas" },
            new() { Partida = "Área de terreno natural removido (desbroce en corte)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(result.CutTerrainArea) },
            new() { Partida = "Área de plataforma sobre relleno", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(result.FillTopArea) },
            new() { Partida = "Área de taludes de relleno (perfilado)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(result.FillSlopeArea) },
            new() { Partida = "Área de contacto relleno–terreno (escarificado)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(result.FillTerrainArea) },
            new() { Partida = "Área de plataforma (contorno)", Unidad = "m²", Cantidad = Medidas.Ft2ToM2(PolygonUtils.Area(boundary)) },
            new() { Partida = "Cota de plataforma (ref. punto base)", Unidad = "m", Cantidad = cotaUser },
        };

        var res = new ResultadosWindow($"Plataforma '{s.GroupName}'", rows, warnings, s.CreateSolids && (result.CutSolids.Count + result.FillSolids.Count) > 0);
        RevitHelpers.SetOwner(res, uiapp);
        bool create = res.ShowDialog() == true;
        if (!create) { group.RollBack(); return Result.Succeeded; }

        // 5. Sólidos en el modelo
        var createWarnings = new List<string>();
        using (var t = new Transaction(doc, "Excavación de plataforma - sólidos"))
        {
            t.Start();
            string fuente = string.Join(",", terrainElements.Select(e => e.Id.Value));
            CommandSupport.CreateShape(doc, $"EXC Corte - {s.GroupName}", "Corte", s.GroupName, result.CutSolids,
                result.CutVolume, result.CutBottomArea, result.CutSlopeArea, result.CutTerrainArea, cutSlope.Label, zBase, s.SwellFactor, fuente, createWarnings);
            CommandSupport.CreateShape(doc, $"EXC Relleno - {s.GroupName}", "Relleno", s.GroupName, result.FillSolids,
                result.FillVolume, result.FillTopArea, result.FillSlopeArea, result.FillTerrainArea, fillSlope.Label, zBase, s.SwellFactor, fuente, createWarnings);
            t.Commit();
        }
        group.Assimilate();
        if (createWarnings.Count > 0) CommandSupport.ShowError(string.Join("\n", createWarnings));
        return Result.Succeeded;
    }
}
