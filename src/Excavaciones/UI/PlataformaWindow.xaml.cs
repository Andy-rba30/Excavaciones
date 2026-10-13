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
        UiHelpers.FillLevelCombo(TerrainLevelCombo, doc);
        UiHelpers.FillTerrainCombo(TerrainCombo, doc);
        UiHelpers.FillLevelCombo(LevelCombo, doc);
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
            var terrainChoice = TerrainCombo.SelectedItem as TerrainChoice;
            Settings.TerrainId = terrainChoice?.Id;
            Settings.TerrainFromLevel = terrainChoice?.LevelPlane == true;
            Settings.TerrainLevel = (TerrainLevelCombo.SelectedItem as LevelChoice)?.Level;
            Settings.TerrainLevelOffsetM = UiHelpers.ParseDouble(TerrainLevelOffset.Text, "Desfase del nivel de terreno");
            if (Settings.TerrainFromLevel && Settings.TerrainLevel == null) throw new FormatException("El proyecto no tiene niveles; elige un Toposolid como terreno.");
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

    private void TerrainCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => UiHelpers.UpdateTerrainPanel(TerrainCombo, TerrainLevelPanel, TerrainInfo);
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

    public static void FillLevelCombo(System.Windows.Controls.ComboBox combo, Document doc)
    {
        foreach (Level l in RevitHelpers.Levels(doc))
            combo.Items.Add(new LevelChoice { Level = l, Label = $"{l.Name}  ({Medidas.FtToM(l.ProjectElevation):0.000} m)" });
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    public static void FillTerrainCombo(System.Windows.Controls.ComboBox combo, Document doc)
    {
        var topos = RevitHelpers.Toposolids(doc);
        foreach (Toposolid t in topos)
            combo.Items.Add(new TerrainChoice { Id = t.Id, Label = $"Toposolid: {t.Name} (id {t.Id.Value})" });
        combo.Items.Add(new TerrainChoice { Id = null, LevelPlane = true, Label = "Nivel: plano horizontal (el nivel es la parte superior de la excavación)" });
        combo.Items.Add(new TerrainChoice { Id = null, Label = "Seleccionar en pantalla… (Toposolid, suelo, masa, DirectShape o importación)" });
        combo.SelectedIndex = 0;
    }

    /// <summary>Muestra u oculta el panel de nivel según la opción de terreno elegida y actualiza el texto de ayuda.</summary>
    public static void UpdateTerrainPanel(System.Windows.Controls.ComboBox combo, System.Windows.UIElement levelPanel, System.Windows.Controls.TextBlock info)
    {
        var choice = combo.SelectedItem as TerrainChoice;
        bool plane = choice?.LevelPlane == true;
        levelPanel.Visibility = plane ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (plane)
            info.Text = "El terreno se toma como un plano horizontal a la cota del nivel (+ desfase). Desde ahí hacia abajo se excava con el talud indicado; no se usa topografía.";
        else if (choice != null && choice.Id == null)
            info.Text = "Se pedirá seleccionar en pantalla el/los elementos de terreno; se usa su superficie superior.";
        else
            info.Text = "La superficie superior del elemento se usa como terreno natural.";
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
