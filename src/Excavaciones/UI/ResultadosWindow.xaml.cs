using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace Excavaciones.UI;

public partial class ResultadosWindow : Window
{
    private readonly List<MetradoRow> _rows;

    public ResultadosWindow(string title, List<MetradoRow> rows, List<string> warnings, bool canCreate)
    {
        InitializeComponent();
        _rows = rows;
        Header.Text = title;
        ResultGrid.ItemsSource = rows;
        if (warnings.Count > 0) { Warnings.Text = string.Join(Environment.NewLine, warnings); WarningsExpander.IsExpanded = true; }
        else WarningsExpander.Visibility = Visibility.Collapsed;
        if (!canCreate) { OkButton.Visibility = Visibility.Collapsed; CancelButton.Content = "Cerrar"; }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Csv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "metrado_excavaciones.csv" };
        if (dlg.ShowDialog(this) != true) return;
        var sb = new StringBuilder();
        sb.AppendLine("Partida;Unidad;Cantidad;Nota");
        foreach (var r in _rows)
            sb.AppendLine($"{Escape(r.Partida)};{r.Unidad};{r.Cantidad.ToString("0.000", CultureInfo.InvariantCulture)};{Escape(r.Nota)}");
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show(this, "Metrado exportado a:\n" + dlg.FileName, "Excavaciones", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string Escape(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
