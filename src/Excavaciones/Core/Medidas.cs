using Autodesk.Revit.DB;

namespace Excavaciones.Core;

/// <summary>Conversión entre unidades internas de Revit (pies) y unidades métricas de obra.</summary>
public static class Medidas
{
    public static double MToFt(double meters) => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
    public static double FtToM(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    public static double Ft2ToM2(double ft2) => UnitUtils.ConvertFromInternalUnits(ft2, UnitTypeId.SquareMeters);
    public static double Ft3ToM3(double ft3) => UnitUtils.ConvertFromInternalUnits(ft3, UnitTypeId.CubicMeters);

    public static string M3(double ft3) => Ft3ToM3(ft3).ToString("N2") + " m³";
    public static string M2(double ft2) => Ft2ToM2(ft2).ToString("N2") + " m²";
    public static string M(double ft) => FtToM(ft).ToString("N3") + " m";
}
