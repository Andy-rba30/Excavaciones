using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace Excavaciones;

/// <summary>Registra la pestaña "Excavaciones" en el ribbon de Revit.</summary>
public class App : IExternalApplication
{
    public const string TabName = "Excavaciones";

    public Result OnStartup(UIControlledApplication application)
    {
        try { application.CreateRibbonTab(TabName); } catch { /* ya existe */ }
        RibbonPanel panel = application.GetRibbonPanels(TabName).FirstOrDefault(p => p.Name == "Movimiento de tierras")
                            ?? application.CreateRibbonPanel(TabName, "Movimiento de tierras");
        string asm = Assembly.GetExecutingAssembly().Location;

        AddButton(panel, asm, "ExcPlataforma", "Plataforma", typeof(Commands.CmdExcavacionPlataforma),
            "Excavación tipo civil: plataforma a una cota dentro de un contorno, con taludes de corte y relleno, bermas y metrado de corte/relleno contra el terreno natural.",
            "plataforma");
        AddButton(panel, asm, "ExcCimentacion", "Cimentación", typeof(Commands.CmdExcavacionCimentacion),
            "Excavación por cimentación: huella de las cimentaciones + sobreexcavación lateral, profundidad adicional y taludes. Metra excavación, relleno con material propio y eliminación.",
            "cimentacion");
        AddButton(panel, asm, "ExcMetrado", "Metrado", typeof(Commands.CmdMetrado),
            "Resumen de metrado de todas las excavaciones del proyecto y exportación a CSV.",
            "metrado");
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private static void AddButton(RibbonPanel panel, string asm, string name, string text, Type cmd, string tooltip, string icon)
    {
        var data = new PushButtonData(name, text, asm, cmd.FullName) { ToolTip = tooltip };
        data.LargeImage = LoadImage(icon + "32.png");
        data.Image = LoadImage(icon + "16.png");
        panel.AddItem(data);
    }

    private static BitmapImage? LoadImage(string file)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Excavaciones;component/Resources/{file}", UriKind.Absolute);
            return new BitmapImage(uri);
        }
        catch { return null; }
    }
}
