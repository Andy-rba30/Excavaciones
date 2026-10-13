using Autodesk.Revit.DB;
using Excavaciones.Core;

namespace Excavaciones.UI;

public sealed class TerrainChoice
{
    public ElementId? Id { get; init; }
    /// <summary>True: el terreno es un plano horizontal en un nivel (+ desfase), no un elemento.</summary>
    public bool LevelPlane { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

public sealed class LevelChoice
{
    public required Level Level { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

public sealed class PlatformSettings
{
    public ElementId? TerrainId { get; set; }          // null => seleccionar en pantalla
    public bool TerrainFromLevel { get; set; }         // true => plano horizontal en TerrainLevel + desfase
    public Level? TerrainLevel { get; set; }
    public double TerrainLevelOffsetM { get; set; }
    public bool UseLevel { get; set; }
    public Level? Level { get; set; }
    public double LevelOffsetM { get; set; }
    public ElevationReference Reference { get; set; } = ElevationReference.ProjectBasePoint;
    public double ElevationM { get; set; }
    public double CutRatioH { get; set; } = 1.0;
    public double FillRatioH { get; set; } = 1.5;
    public double CutBermHeightM { get; set; }
    public double CutBermWidthM { get; set; } = 1.0;
    public double SwellFactor { get; set; } = 1.25;
    public bool CreateSolids { get; set; } = true;
    public string GroupName { get; set; } = "Plataforma 1";
}

public sealed class FoundationSettings
{
    public ElementId? TerrainId { get; set; }
    public bool TerrainFromLevel { get; set; }
    public Level? TerrainLevel { get; set; }
    public double TerrainLevelOffsetM { get; set; }
    public double LateralM { get; set; } = 0.50;
    public double ExtraDepthM { get; set; } = 0.10;
    public double RatioH { get; set; } = 0.5;
    public double BermHeightM { get; set; }
    public double BermWidthM { get; set; } = 1.0;
    public bool Merge { get; set; } = true;
    public bool SubtractFoundation { get; set; } = true;
    public double SwellFactor { get; set; } = 1.25;
    public bool CreateSolids { get; set; } = true;
    public bool TryExcavateToposolid { get; set; }
    public string GroupName { get; set; } = "Cimentación";
}

public sealed class MetradoRow
{
    public string Partida { get; init; } = string.Empty;
    public string Unidad { get; init; } = string.Empty;
    public double Cantidad { get; init; }
    public string CantidadTexto => Cantidad.ToString("N2");
    public string Nota { get; init; } = string.Empty;
}
