using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Excavaciones.Core;
using Excavaciones.UI;

namespace Excavaciones.Commands;

/// <summary>Pasos comunes a los comandos de excavación.</summary>
internal static class CommandSupport
{
    public const string CutMaterialName = "EXC - Corte";
    public const string FillMaterialName = "EXC - Relleno";

    /// <summary>Elementos de terreno: el Toposolid elegido o una selección en pantalla. Null si el usuario cancela.</summary>
    public static List<Element>? ResolveTerrain(UIDocument uidoc, ElementId? terrainId)
    {
        Document doc = uidoc.Document;
        if (terrainId != null)
        {
            Element? e = doc.GetElement(terrainId);
            return e == null ? null : new List<Element> { e };
        }
        try
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element,
                new PredicateSelectionFilter(SelectionFilters.IsTerrain),
                "Selecciona el/los elementos que representan el terreno natural y pulsa Finalizar");
            return refs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList()!;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
    }

    public static TerrainModel? BuildTerrain(Document doc, List<Element> elements, out string error)
    {
        error = string.Empty;
        var model = TerrainModel.FromElements(elements, doc.Application.ShortCurveTolerance);
        if (model.IsEmpty)
        {
            error = "No se encontró una superficie superior en los elementos de terreno seleccionados.";
            return null;
        }
        return model;
    }

    /// <summary>Crea materiales y vincula parámetros compartidos (en una transacción propia).</summary>
    public static (ElementId cut, ElementId fill) Prepare(Document doc)
    {
        using var t = new Transaction(doc, "Excavaciones - preparar");
        t.Start();
        ElementId cut = RevitHelpers.EnsureMaterial(doc, CutMaterialName, 176, 112, 64, 25);
        ElementId fill = RevitHelpers.EnsureMaterial(doc, FillMaterialName, 222, 190, 96, 25);
        try { ExcavationParams.EnsureBound(doc); }
        catch (Exception ex)
        {
            TaskDialog.Show("Excavaciones", "No se pudieron crear los parámetros compartidos EXC_*:\n" + ex.Message + "\n\nLos sólidos se crearán sin parámetros de metrado.");
        }
        t.Commit();
        return (cut, fill);
    }

    public static DirectShape? CreateShape(Document doc, string name, string tipo, string grupo, IList<Solid> solids,
        double volume, double areaFondo, double areaTalud, double areaTerreno, string talud, double cotaFondo, double swell, string fuente, List<string> warnings)
    {
        DirectShape? ds = RevitHelpers.CreateDirectShape(doc, name, solids, warnings);
        if (ds == null) return null;
        ExcavationParams.Set(ds, ExcavationParams.Tipo, tipo);
        ExcavationParams.Set(ds, ExcavationParams.Grupo, grupo);
        ExcavationParams.Set(ds, ExcavationParams.Volumen, volume);
        ExcavationParams.Set(ds, ExcavationParams.AreaFondo, areaFondo);
        ExcavationParams.Set(ds, ExcavationParams.AreaTalud, areaTalud);
        ExcavationParams.Set(ds, ExcavationParams.AreaTerreno, areaTerreno);
        ExcavationParams.Set(ds, ExcavationParams.Talud, talud);
        ExcavationParams.Set(ds, ExcavationParams.CotaFondo, cotaFondo);
        ExcavationParams.Set(ds, ExcavationParams.VolumenEsponjado, volume * swell);
        ExcavationParams.Set(ds, ExcavationParams.Fecha, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        ExcavationParams.Set(ds, ExcavationParams.Fuente, fuente);
        Parameter? comments = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (comments != null && !comments.IsReadOnly)
            comments.Set($"{tipo} {grupo}: {Medidas.M3(volume)} | talud {talud}");
        return ds;
    }

    public static void ShowError(string message) => TaskDialog.Show("Excavaciones", message);
}
