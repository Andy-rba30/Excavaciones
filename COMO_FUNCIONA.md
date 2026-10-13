# Cómo funciona el plugin Excavaciones

Add-in para **Autodesk Revit 2027** (.NET 10, WPF). Al abrir Revit aparece la pestaña **Excavaciones** con el panel *Movimiento de tierras* y tres botones: **Plataforma**, **Cimentación** y **Metrado**.

## 1. Qué hace cada botón

### Plataforma (excavación tipo civil)
Calcula el **corte y relleno** necesarios para dejar una plataforma plana a una cota dada dentro de un contorno.
1. Dibujas el contorno (líneas de modelo cerradas, región rellena, suelo, área o habitación).
2. Pulsas **Plataforma** y en la ventana eliges:
   - **Terreno natural**: el Toposolid del proyecto (o cualquier elemento con cara superior).
   - **Cota de plataforma**: nivel + desfase, o cota en metros respecto al punto base, punto de levantamiento u origen interno.
   - **Talud de corte** y **talud de relleno** en formato H:1V (`1` = 45°, `0` = vertical).
   - **Bermas** (altura entre bermas y ancho) y **factor de esponjamiento**.
3. Seleccionas el contorno en pantalla y pulsas *Finalizar*.
4. Se muestra el metrado (corte, relleno, excedente o préstamo, áreas de fondo, taludes y terreno removido). Si marcas *Crear sólidos*, se generan los sólidos `EXC Corte - <grupo>` y `EXC Relleno - <grupo>` en el modelo.

### Cimentación
Es como el comando *Excavar* de Revit, pero con criterios de obra:
- **Sobreexcavación lateral** (espacio de trabajo alrededor de la zapata).
- **Profundidad adicional** bajo el fondo (solado o mejoramiento).
- **Taludes y bermas** en las paredes.
- **Unir excavaciones adyacentes** para no contar dos veces el volumen compartido entre zapatas contiguas.
- **Descontar el volumen enterrado** de la cimentación.

Seleccionas zapatas, losas, vigas de cimentación, muros, columnas, etc. La huella se toma de la **cara inferior** de cada elemento. Entrega: excavación en banco, volumen de cimentación enterrada, relleno con material propio (excavación menos cimentación), eliminación de excedente (cimentación por esponjamiento) y áreas.

### Metrado
Lista todos los sólidos que creó el plugin, agrupados por grupo y tipo, con totales en banco y esponjados. Permite exportar a **CSV**.

## 2. Cómo se materializan los resultados
Los sólidos se crean como **Modelos genéricos (DirectShape)** con parámetros compartidos `EXC_*`:
`EXC_Tipo`, `EXC_Grupo`, `EXC_Volumen`, `EXC_Volumen_Esponjado`, `EXC_Area_Fondo`, `EXC_Area_Talud`, `EXC_Area_Terreno`, `EXC_Talud`, `EXC_Cota_Fondo`, `EXC_Fecha`, `EXC_Fuente`.

Con eso puedes hacer una **tabla de planificación** de Modelos genéricos filtrada por `EXC_Tipo` y sumar `EXC_Volumen`. El archivo de parámetros compartidos se guarda en `%APPDATA%\Excavaciones\` con GUIDs fijos, así que funciona igual en todos los proyectos.

## 3. Metodología de cálculo (lo que pasa por dentro)
1. **Terreno**: se toma la cara superior del Toposolid y se triangula. Queda un campo de alturas (una sola superficie, sin voladizos). `Core/TerrainModel.cs`
2. **Envolvente de corte**: desde el contorno en la cota de fondo, el polígono se desplaza hacia afuera `H × Δz` conforme sube, generando el talud por tramos (loft). Las bermas son escalones horizontales. La altura se extiende sola hasta que el talud sale a la superficie del terreno (*daylight*). `Core/EarthworkEngine.cs`
3. **Envolvente de relleno** (solo plataforma): igual pero hacia abajo con el talud de relleno.
4. **Corte** = terreno ∩ envolvente de corte. **Relleno** = (aire sobre el terreno) ∩ envolvente de relleno. Se calcula **triángulo a triángulo** del terreno, para que cada operación booleana sea pequeña y robusta. `Core/SolidUtils.cs`
5. Las caras de cada pieza se clasifican como terreno natural, fondo o talud para obtener las **áreas**.
6. En cimentación, el **volumen enterrado** de cada elemento se calcula intersectando sus sólidos con el terreno y se descuenta para el relleno con material propio.

Los desfases de contornos se intentan primero con un inglete propio, luego con `CurveLoop.CreateViaOffset` de Revit y, si ambos fallan, con la envolvente convexa (se avisa y el resultado es conservador). `Core/PolygonUtils.cs`

## 4. Flujo de un comando (ejemplo Plataforma)
`Commands/CmdExcavacionPlataforma.cs`
1. Abre la ventana WPF `PlataformaWindow` y recoge los parámetros.
2. Abre un `TransactionGroup` y pide al usuario seleccionar el contorno.
3. Construye el `TerrainModel`, las envolventes y llama a `EarthworkEngine.Compute` mostrando una ventana de progreso.
4. Muestra `ResultadosWindow` con el metrado y las advertencias.
5. Si el usuario acepta crear sólidos, abre una `Transaction`, crea los DirectShapes y escribe los parámetros `EXC_*`.

## 5. Estructura del código
```
Excavaciones.addin               manifiesto del add-in
src/Excavaciones/
  App.cs                         pestaña y botones del ribbon
  Commands/                      los tres comandos + utilidades comunes
  Core/TerrainModel.cs           triangulación del terreno
  Core/PolygonUtils.cs           desfases y unión de contornos
  Core/SolidUtils.cs             extrusiones, lofts y booleanas tolerantes
  Core/EarthworkEngine.cs        envolventes con taludes/bermas, corte/relleno, áreas
  Core/ExcavationParams.cs       parámetros compartidos EXC_*
  Core/Footprint.cs              contornos de plataforma y huellas de cimentación
  UI/                            ventanas WPF (parámetros, progreso, resultados)
build/install.ps1                instala en %APPDATA%\Autodesk\Revit\Addins\2027
.github/workflows/build.yml      compila en Windows y publica el paquete
```

## 6. Limitaciones
- El terreno es un campo de alturas: no hay túneles ni voladizos.
- No se consideran subdivisiones del Toposolid.
- *Aplicar al Toposolid* (`ExcavateBy`) es experimental; el API de Revit suele rechazarlo. El plugin no modifica el terreno, crea sólidos independientes.
- Toposólidos muy densos tardan más; solo se procesan los triángulos bajo la zona de excavación.
- Entradas en metros; salidas en m² y m³.

## 7. Instalación
- **Sin compilar**: descarga el artefacto `Excavaciones-Revit2027` desde GitHub Actions y ejecuta `install.ps1 -SkipBuild`.
- **Compilando**: con .NET SDK 10, ejecuta `build\install.ps1`. Copia el `.addin` y la DLL a `%APPDATA%\Autodesk\Revit\Addins\2027`.
