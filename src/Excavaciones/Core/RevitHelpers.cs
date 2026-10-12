using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Excavaciones.Core;

/// <summary>Referencia de cotas que el usuario ve en pantalla.</summary>
public enum ElevationReference { ProjectBasePoint, SurveyPoint, Internal }

public static class RevitHelpers
{
    /// <summary>Desplazamiento a sumar a una cota "de usuario" para obtener Z interno.</summary>
    public static double ElevationOffset(Document doc, ElevationReference reference)
    {
        switch (reference)
        {
            case ElevationReference.ProjectBasePoint:
                return BasePoint.GetProjectBasePoint(doc)?.Position.Z ?? 0;
            case ElevationReference.SurveyPoint:
                return -doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation;
            default:
                return 0;
        }
    }

    public static double ToInternalZ(Document doc, double userMeters, ElevationReference reference)
        => Medidas.MToFt(userMeters) + ElevationOffset(doc, reference);

    public static double ToUserMeters(Document doc, double internalZ, ElevationReference reference)
        => Medidas.FtToM(internalZ - ElevationOffset(doc, reference));

    public static List<Toposolid> Toposolids(Document doc)
        => new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Cast<Toposolid>()
            .Where(t => t.HostTopoId == ElementId.InvalidElementId) // excluir subdivisiones
            .OrderBy(t => t.Name).ToList();

    public static List<Level> Levels(Document doc)
        => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();

    public static ElementId EnsureMaterial(Document doc, string name, byte r, byte g, byte b, int transparency)
    {
        Material? m = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (m != null) return m.Id;
        ElementId id = Material.Create(doc, name);
        m = (Material)doc.GetElement(id);
        m.Color = new Color(r, g, b);
        m.Transparency = transparency;
        m.UseRenderAppearanceForShading = false;
        return id;
    }

    /// <summary>Crea un DirectShape (Modelo genérico) con los sólidos indicados. Devuelve null si no se pudo.</summary>
    public static DirectShape? CreateDirectShape(Document doc, string name, IList<Solid> solids, List<string> warnings)
    {
        if (solids.Count == 0) return null;
        var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
        ds.ApplicationId = ExcavationParams.AppId;
        ds.ApplicationDataId = Guid.NewGuid().ToString();
        ds.Name = name;
        try
        {
            ds.SetShape(solids.Cast<GeometryObject>().ToList());
            return ds;
        }
        catch
        {
            // Reintento sólido a sólido descartando los que Revit rechace
            var ok = new List<GeometryObject>();
            int skipped = 0;
            foreach (Solid s in solids)
            {
                try { ds.SetShape(new List<GeometryObject> { s }); ok.Add(s); }
                catch { skipped++; }
            }
            if (skipped > 0) warnings.Add($"{name}: {skipped} pieza(s) no pudieron representarse como DirectShape (el volumen metrado no cambia).");
            if (ok.Count == 0) { doc.Delete(ds.Id); return null; }
            ds.SetShape(ok);
            return ds;
        }
    }

    public static void SetOwner(System.Windows.Window w, UIApplication uiapp)
    {
        try { new System.Windows.Interop.WindowInteropHelper(w).Owner = uiapp.MainWindowHandle; } catch { }
    }
}

/// <summary>Filtro de selección por predicado.</summary>
public sealed class PredicateSelectionFilter : ISelectionFilter
{
    private readonly Func<Element, bool> _pred;
    public PredicateSelectionFilter(Func<Element, bool> pred) => _pred = pred;
    public bool AllowElement(Element elem) => _pred(elem);
    public bool AllowReference(Reference reference, XYZ position) => true;
}

public static class SelectionFilters
{
    public static bool IsTerrain(Element e) => e is Toposolid || e is Floor || e is DirectShape || e is ImportInstance
        || (e.Category != null && e.Category.Id.Value == (long)BuiltInCategory.OST_Mass);

    public static bool IsBoundary(Element e) => e is ModelCurve || e is DetailCurve || e is FilledRegion || e is Floor
        || (e is Toposolid t && t.HostTopoId != ElementId.InvalidElementId) || e is Area || e is Room;

    public static bool IsFoundation(Element e)
    {
        if (e is Toposolid || e is ImportInstance) return false;
        if (e is DirectShape ds && ds.ApplicationId == ExcavationParams.AppId) return false;
        if (e.Category == null) return false;
        long c = e.Category.Id.Value;
        return c == (long)BuiltInCategory.OST_StructuralFoundation || c == (long)BuiltInCategory.OST_Floors
            || c == (long)BuiltInCategory.OST_Walls || c == (long)BuiltInCategory.OST_StructuralColumns
            || c == (long)BuiltInCategory.OST_StructuralFraming || c == (long)BuiltInCategory.OST_GenericModel
            || c == (long)BuiltInCategory.OST_Columns || c == (long)BuiltInCategory.OST_Mass || c == (long)BuiltInCategory.OST_PipeCurves
            || c == (long)BuiltInCategory.OST_Conduit || c == (long)BuiltInCategory.OST_DuctCurves;
    }
}
