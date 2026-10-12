using System.Globalization;
using System.Windows;
using Autodesk.Revit.DB;
using Excavaciones.Core;

namespace Excavaciones.UI;

public partial class PlataformaWindow : Window
{
    private readonly Document _doc;
    public PlatformSettings Settings { get; } = new();

    public PlataformaWindow(Document doc)
    {
        InitializeComponent();
        _doc = doc;
        UiHelpers.FillTerrainCombo(TerrainCombo, TerrainInfo, doc);
        foreach (Level l in RevitHelpers.Levels(doc))
            LevelCombo.Items.Add(new LevelChoice { Level = l, Label = $"{l.Name}  ({Medidas.FtToM(l.ProjectElevation):0.000} m)" });
        if (LevelCombo.Items.Count > 0) LevelCombo.SelectedIndex = 0;
        ReferenceCombo.Items.Add("Punto base del proyecto");
        ReferenceCombo.Items.Add("Punto de levantamiento (coord. compartidas)");
        ReferenceCombo.Items.Add("Origen interno");
        ReferenceCombo.SelectedIndex = 0;
        ElevationHint.Text = UiHelpers.TerrainRangeHint(doc);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Settings.TerrainId = (TerrainCombo.SelectedItem as TerrainChoice)?.Id;
            Settings.UseLevel = ByLevel.IsChecked == true;
            Settings.Level = (LevelCombo.SelectedItem as LevelChoice)?.Level;
            Settings.LevelOffsetM = UiHelpers.ParseDouble(LevelOffset.Text, "Desfase");
            Settings.Reference = (ElevationReference)ReferenceCombo.SelectedIndex;
            Settings.ElevationM = UiHelpers.ParseDouble(Elevation.Text, "Cota");
            Settings.CutRatioH = UiHelpers.ParseDouble(CutRatio.Text, "Talud de corte", min: 0);
            Settings.FillRatioH = UiHelpers.ParseDouble(FillRatio.Text, "Talud de relleno", min: 0);
            Settings.CutBermHeightM = UiHelpers.ParseDouble(BermHeight.Text, "Altura entre bermas", min: 0);
            Settings.CutBermWidthM = UiHelpers.ParseDouble(BermWidth.Text, "Ancho de berma", min: 0);
            Settings.SwellFactor = UiHelpers.ParseDouble(Swell.Text, "Factor de esponjamiento", min: 0.5);
            Settings.CreateSolids = CreateSolids.IsChecked == true;
            Settings.GroupName = string.IsNullOrWhiteSpace(GroupName.Text) ? "Plataforma" : GroupName.Text.Trim();
            if (Settings.UseLevel && Settings.Level == null) throw new FormatException("El proyecto no tiene niveles; usa una cota.");
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Dato inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

internal static class UiHelpers
{
    public static double ParseDouble(string text, string field, double? min = null)
    {
        text = text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            throw new FormatException($"'{field}' debe ser un número.");
        if (min.HasValue && v < min.Value) throw new FormatException($"'{field}' debe ser mayor o igual a {min.Value}.");
        return v;
    }

    public static void FillTerrainCombo(System.Windows.Controls.ComboBox combo, System.Windows.Controls.TextBlock info, Document doc)
    {
        var topos = RevitHelpers.Toposolids(doc);
        foreach (Toposolid t in topos)
            combo.Items.Add(new TerrainChoice { Id = t.Id, Label = $"Toposolid: {t.Name} (id {t.Id.Value})" });
        combo.Items.Add(new TerrainChoice { Id = null, Label = "Seleccionar en pantalla… (Toposolid, suelo, masa, DirectShape o importación)" });
        combo.SelectedIndex = 0;
        info.Text = topos.Count == 0
            ? "No hay Toposolids en el proyecto; se pedirá seleccionar el terreno en pantalla."
            : "La superficie superior del elemento se usa como terreno natural.";
    }

    public static string TerrainRangeHint(Document doc)
    {
        var topos = RevitHelpers.Toposolids(doc);
        if (topos.Count == 0) return string.Empty;
        try
        {
            BoundingBoxXYZ? bb = topos[0].get_BoundingBox(null);
            if (bb == null) return string.Empty;
            double pbp = RevitHelpers.ElevationOffset(doc, ElevationReference.ProjectBasePoint);
            return $"Referencia: el Toposolid '{topos[0].Name}' va de {Medidas.FtToM(bb.Min.Z - pbp):0.00} m a {Medidas.FtToM(bb.Max.Z - pbp):0.00} m respecto al punto base del proyecto.";
        }
        catch { return string.Empty; }
    }
}
