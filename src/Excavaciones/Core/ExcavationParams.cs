using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace Excavaciones.Core;

/// <summary>Parámetros compartidos que se escriben en los sólidos de excavación para poder metrarlos en tablas de planificación.</summary>
public static class ExcavationParams
{
    public const string GroupName = "Excavaciones";
    public const string AppId = "Excavaciones";

    public static readonly Guid Tipo = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d01");
    public static readonly Guid Grupo = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d02");
    public static readonly Guid Volumen = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d03");
    public static readonly Guid AreaFondo = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d04");
    public static readonly Guid AreaTalud = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d05");
    public static readonly Guid AreaTerreno = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d06");
    public static readonly Guid Talud = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d07");
    public static readonly Guid CotaFondo = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d08");
    public static readonly Guid VolumenEsponjado = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d09");
    public static readonly Guid Fecha = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d0a");
    public static readonly Guid Fuente = new("6f6c1b1e-5b0e-4e2a-9c1a-1d2a3b4c5d0b");

    private static readonly (Guid guid, string name, ForgeTypeId spec)[] Definitions =
    {
        (Tipo, "EXC_Tipo", SpecTypeId.String.Text),
        (Grupo, "EXC_Grupo", SpecTypeId.String.Text),
        (Volumen, "EXC_Volumen", SpecTypeId.Volume),
        (AreaFondo, "EXC_Area_Fondo", SpecTypeId.Area),
        (AreaTalud, "EXC_Area_Talud", SpecTypeId.Area),
        (AreaTerreno, "EXC_Area_Terreno", SpecTypeId.Area),
        (Talud, "EXC_Talud", SpecTypeId.String.Text),
        (CotaFondo, "EXC_Cota_Fondo", SpecTypeId.Length),
        (VolumenEsponjado, "EXC_Volumen_Esponjado", SpecTypeId.Volume),
        (Fecha, "EXC_Fecha", SpecTypeId.String.Text),
        (Fuente, "EXC_Fuente", SpecTypeId.String.Text),
    };

    public static string SharedFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Excavaciones", "Excavaciones_ParametrosCompartidos.txt");

    /// <summary>Crea (si hace falta) y vincula los parámetros a Modelos genéricos. Debe llamarse dentro de una transacción.</summary>
    public static void EnsureBound(Document doc)
    {
        Application app = doc.Application;
        var cats = app.Create.NewCategorySet();
        cats.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel));

        bool allBound = Definitions.All(d => SharedParameterElement.Lookup(doc, d.guid) != null && IsBoundToGeneric(doc, d.guid));
        if (allBound) return;

        string previous = app.SharedParametersFilename;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SharedFilePath)!);
            if (!File.Exists(SharedFilePath)) File.WriteAllText(SharedFilePath, string.Empty);
            app.SharedParametersFilename = SharedFilePath;
            DefinitionFile? file = app.OpenSharedParameterFile();
            if (file == null) throw new InvalidOperationException("No se pudo abrir el archivo de parámetros compartidos: " + SharedFilePath);
            DefinitionGroup group = file.Groups.get_Item(GroupName) ?? file.Groups.Create(GroupName);

            foreach (var (guid, name, spec) in Definitions)
            {
                Definition? def = group.Definitions.get_Item(name);
                if (def == null)
                {
                    var opt = new ExternalDefinitionCreationOptions(name, spec) { GUID = guid, Visible = true, UserModifiable = false };
                    def = group.Definitions.Create(opt);
                }
                if (SharedParameterElement.Lookup(doc, guid) != null && IsBoundToGeneric(doc, guid)) continue;

                BindingMap map = doc.ParameterBindings;
                SharedParameterElement? existing = SharedParameterElement.Lookup(doc, guid);
                if (existing != null)
                {
                    // ya existe en el proyecto, pero no está vinculado a Modelos genéricos: ampliar categorías
                    InternalDefinition idef = existing.GetDefinition();
                    if (map.get_Item(idef) is InstanceBinding ib)
                    {
                        ib.Categories.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel));
                        map.ReInsert(idef, ib, GroupTypeId.General);
                    }
                    else
                    {
                        map.Insert(idef, app.Create.NewInstanceBinding(cats), GroupTypeId.General);
                    }
                }
                else
                {
                    map.Insert(def, app.Create.NewInstanceBinding(cats), GroupTypeId.General);
                }
            }
        }
        finally
        {
            app.SharedParametersFilename = previous;
        }
    }

    private static bool IsBoundToGeneric(Document doc, Guid guid)
    {
        SharedParameterElement? spe = SharedParameterElement.Lookup(doc, guid);
        if (spe == null) return false;
        if (doc.ParameterBindings.get_Item(spe.GetDefinition()) is not InstanceBinding ib) return false;
        foreach (Category c in ib.Categories)
            if (c.Id.Value == (long)BuiltInCategory.OST_GenericModel) return true;
        return false;
    }

    public static void Set(Element e, Guid guid, string value) { var p = e.get_Parameter(guid); if (p != null && !p.IsReadOnly) p.Set(value); }
    public static void Set(Element e, Guid guid, double value) { var p = e.get_Parameter(guid); if (p != null && !p.IsReadOnly) p.Set(value); }
    public static string GetString(Element e, Guid guid) => e.get_Parameter(guid)?.AsString() ?? string.Empty;
    public static double GetDouble(Element e, Guid guid) => e.get_Parameter(guid)?.AsDouble() ?? 0;
}
