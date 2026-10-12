using System.Windows;
using Autodesk.Revit.DB;

namespace Excavaciones.UI;

public partial class CimentacionWindow : Window
{
    public FoundationSettings Settings { get; } = new();

    public CimentacionWindow(Document doc)
    {
        InitializeComponent();
        UiHelpers.FillTerrainCombo(TerrainCombo, TerrainInfo, doc);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Settings.TerrainId = (TerrainCombo.SelectedItem as TerrainChoice)?.Id;
            Settings.LateralM = UiHelpers.ParseDouble(Lateral.Text, "Sobreexcavación lateral", min: 0);
            Settings.ExtraDepthM = UiHelpers.ParseDouble(ExtraDepth.Text, "Profundidad adicional", min: 0);
            Settings.RatioH = UiHelpers.ParseDouble(Ratio.Text, "Talud", min: 0);
            Settings.BermHeightM = UiHelpers.ParseDouble(BermHeight.Text, "Altura entre bermas", min: 0);
            Settings.BermWidthM = UiHelpers.ParseDouble(BermWidth.Text, "Ancho de berma", min: 0);
            Settings.SwellFactor = UiHelpers.ParseDouble(Swell.Text, "Factor de esponjamiento", min: 0.5);
            Settings.Merge = Merge.IsChecked == true;
            Settings.SubtractFoundation = Subtract.IsChecked == true;
            Settings.CreateSolids = CreateSolids.IsChecked == true;
            Settings.TryExcavateToposolid = Excavate.IsChecked == true;
            Settings.GroupName = string.IsNullOrWhiteSpace(GroupName.Text) ? "Cimentación" : GroupName.Text.Trim();
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Dato inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
