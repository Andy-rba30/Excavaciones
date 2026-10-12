using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Excavaciones.Core;
using Excavaciones.UI;

namespace Excavaciones.Commands;

/// <summary>Resumen de metrado de todas las excavaciones creadas por el plugin en el proyecto.</summary>
[Transaction(TransactionMode.ReadOnly)]
[Regeneration(RegenerationOption.Manual)]
public class CmdMetrado : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiapp = commandData.Application;
        Document doc = uiapp.ActiveUIDocument.Document;

        var shapes = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>()
            .Where(d => d.ApplicationId == ExcavationParams.AppId).ToList();
        if (shapes.Count == 0)
        {
            TaskDialog.Show("Excavaciones", "El proyecto no tiene excavaciones creadas con este plugin.\nUsa 'Plataforma' o 'Cimentación' para generarlas.");
            return Result.Succeeded;
        }

        var rows = new List<MetradoRow>();
        foreach (var byGroup in shapes.GroupBy(d => ExcavationParams.GetString(d, ExcavationParams.Grupo)).OrderBy(g => g.Key))
        {
            foreach (DirectShape d in byGroup.OrderBy(x => ExcavationParams.GetString(x, ExcavationParams.Tipo)))
            {
                string tipo = ExcavationParams.GetString(d, ExcavationParams.Tipo);
                rows.Add(new MetradoRow
                {
                    Partida = $"{byGroup.Key} · {tipo}",
                    Unidad = "m³",
                    Cantidad = Medidas.Ft3ToM3(ExcavationParams.GetDouble(d, ExcavationParams.Volumen)),
                    Nota = $"talud {ExcavationParams.GetString(d, ExcavationParams.Talud)}; fondo {Medidas.Ft2ToM2(ExcavationParams.GetDouble(d, ExcavationParams.AreaFondo)):0.0} m²; taludes {Medidas.Ft2ToM2(ExcavationParams.GetDouble(d, ExcavationParams.AreaTalud)):0.0} m² (id {d.Id.Value})"
                });
            }
        }
        foreach (var byTipo in shapes.GroupBy(d => ExcavationParams.GetString(d, ExcavationParams.Tipo)).OrderBy(g => g.Key))
        {
            rows.Add(new MetradoRow
            {
                Partida = $"TOTAL {byTipo.Key}",
                Unidad = "m³",
                Cantidad = Medidas.Ft3ToM3(byTipo.Sum(d => ExcavationParams.GetDouble(d, ExcavationParams.Volumen))),
                Nota = $"esponjado: {Medidas.Ft3ToM3(byTipo.Sum(d => ExcavationParams.GetDouble(d, ExcavationParams.VolumenEsponjado))):N2} m³"
            });
        }

        var win = new ResultadosWindow($"Metrado de excavaciones del proyecto ({shapes.Count} sólido(s))", rows,
            new List<string> { "Los parámetros EXC_* (Volumen, Área fondo, Área talud, Talud, Grupo, Tipo…) están disponibles en tablas de planificación de Modelos genéricos." }, false);
        RevitHelpers.SetOwner(win, uiapp);
        win.ShowDialog();
        return Result.Succeeded;
    }
}
