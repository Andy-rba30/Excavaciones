# Excavaciones – plugin de movimiento de tierras para Revit 2027

Plugin (add-in) para **Autodesk Revit 2027** que añade una pestaña **Excavaciones** con dos herramientas
de excavación tipo civil y un resumen de metrado:

| Botón | Qué hace |
|---|---|
| **Plataforma** | Excavación/relleno de una plataforma a una cota dentro de un contorno, con **talud de corte**, **talud de relleno** y **bermas**. Calcula corte y relleno contra el terreno natural. |
| **Cimentación** | Lo que hace *Excavar* de Revit, pero con **sobreexcavación lateral**, **profundidad adicional bajo el fondo**, **taludes/bermas** y **metrado** (excavación, relleno con material propio, eliminación de excedente). |
| **Metrado** | Tabla con todas las excavaciones del proyecto, totales por tipo y exportación a CSV. |

Los resultados se materializan como sólidos (**Modelos genéricos / DirectShape**) con parámetros compartidos
`EXC_*` para que se puedan metrar en **tablas de planificación** de Revit.

---

## Instalación

### Opción A – paquete compilado (sin instalar nada más)
1. En GitHub → pestaña **Actions** → último run de *Compilar plugin* → descarga el artefacto `Excavaciones-Revit2027`.
2. Descomprime y ejecuta en PowerShell:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1 -SkipBuild
   ```
   Copia `Excavaciones.addin` y `Excavaciones\Excavaciones.dll` a `%APPDATA%\Autodesk\Revit\Addins\2027`.
3. Abre Revit 2027. Aparece la pestaña **Excavaciones**.

### Opción B – compilar desde el código
Requisitos: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (Revit 2027 corre sobre .NET 10).
```powershell
git clone https://github.com/Andy-rba30/Excavaciones.git
cd Excavaciones
powershell -ExecutionPolicy Bypass -File build\install.ps1
```
El script compila (`dotnet build -c Release`) y copia el resultado a la carpeta de add-ins. También puedes
abrir `Excavaciones.sln` en Visual Studio 2026/2022 (con el workload de .NET desktop) y compilar.

Instalación manual: copia `Excavaciones.addin` a `%APPDATA%\Autodesk\Revit\Addins\2027\` y
`build\bin\Release\Excavaciones.dll` a `%APPDATA%\Autodesk\Revit\Addins\2027\Excavaciones\`.

---

## Uso

### 1. Plataforma (excavación tipo civil)
1. Dibuja el contorno de la plataforma con **líneas de modelo** (o de detalle) cerradas, una **región rellena**,
   un **suelo**, un **área** o una **habitación**.
2. Pestaña *Excavaciones* → **Plataforma**.
3. En la ventana:
   - **Terreno natural**: el Toposolid del proyecto (o *Seleccionar en pantalla* para usar un suelo, masa,
     DirectShape o importación de superficie). Se usa su **cara superior**.
     También puede ser **Nivel: plano horizontal**: el nivel elegido (+ desfase) define la **parte superior de la
     excavación** y desde ahí hacia abajo se excava con el talud indicado, sin necesidad de topografía.
   - **Cota de plataforma**: por **nivel + desfase**, o por **cota en metros** referida al *punto base del
     proyecto*, al *punto de levantamiento* (coordenadas compartidas) o al *origen interno*.
   - **Talud de corte** y **talud de relleno** como *H : 1V* (p. ej. `1` = 1:1, `0.5` = 0.5:1, `0` = vertical).
   - **Bermas** en corte: altura entre bermas y ancho (0 = sin bermas).
   - **Factor de esponjamiento** para el acarreo/eliminación.
4. Acepta y selecciona el contorno en pantalla → *Finalizar*.
5. Se muestra el **metrado**; con *Crear sólidos en el modelo* se generan `EXC Corte - <grupo>` y
   `EXC Relleno - <grupo>`.

Partidas que entrega:

| Partida | Und. |
|---|---|
| Corte de terreno natural (volumen en banco) | m³ |
| Relleno compactado | m³ |
| Eliminación de material excedente (esponjado) **o** material de préstamo | m³ |
| Corte esponjado (acarreo) | m³ |
| Área de fondo de corte, área de taludes de corte (perfilado), área de terreno removido | m² |
| Área de plataforma sobre relleno, área de taludes de relleno, área de contacto relleno–terreno | m² |

### 2. Cimentación (excavación por volumen de cimentación, con taludes)
1. Pestaña *Excavaciones* → **Cimentación**.
2. Parámetros: terreno (Toposolid, selección en pantalla o **nivel como plano horizontal** que marca la parte
   superior de la excavación), **sobreexcavación lateral** (espacio de trabajo), **profundidad adicional** bajo el
   fondo (solado/mejoramiento), **talud** y **bermas**, *unir excavaciones adyacentes* (evita contar dos veces
   el volumen común de zapatas contiguas), *descontar el volumen enterrado de la cimentación*.
3. Selecciona las cimentaciones (zapatas, losas de cimentación, vigas de cimentación, muros, columnas,
   modelos genéricos, tuberías…) → *Finalizar*. La huella se toma de las **caras inferiores** de cada elemento.
4. Metrado y, opcionalmente, sólidos `EXC Cimentación - <grupo>`.

Partidas: excavación (banco), volumen de cimentación enterrada, **relleno compactado con material propio**
(= excavación − cimentación), **eliminación de material excedente** (= cimentación × esponjamiento),
excavación esponjada, área de fondo (solado), área de taludes, área de terreno removido. Con *unir* desactivado
se lista además el desglose por cimentación.

> Opción experimental *Intentar aplicar la excavación al Toposolid*: llama a `Toposolid.ExcavateBy` con el
> sólido generado. El API de Revit solo admite ciertos tipos de elemento (suelos, cubiertas, otros toposólidos),
> por lo que normalmente lo rechaza; en ese caso el plugin lo informa y conserva los sólidos y el metrado.

### 3. Metrado
Lista todos los sólidos creados por el plugin (agrupados por *grupo* y *tipo*), totales por tipo (en banco y
esponjado) y permite exportar a CSV.

### Tablas de planificación
Los sólidos son **Modelos genéricos** con estos parámetros compartidos de ejemplar (grupo *Excavaciones*):

| Parámetro | Tipo | Contenido |
|---|---|---|
| `EXC_Tipo` | Texto | Corte / Relleno / Excavación cimentación |
| `EXC_Grupo` | Texto | Nombre dado en la ventana |
| `EXC_Volumen` | Volumen | Volumen en banco |
| `EXC_Volumen_Esponjado` | Volumen | Volumen × factor de esponjamiento |
| `EXC_Area_Fondo` | Área | Fondo de excavación / plataforma |
| `EXC_Area_Talud` | Área | Taludes + bermas |
| `EXC_Area_Terreno` | Área | Superficie de terreno natural afectada |
| `EXC_Talud` | Texto | Talud usado (p. ej. `1H:1V + bermas…`) |
| `EXC_Cota_Fondo` | Longitud | Cota interna del fondo |
| `EXC_Fecha`, `EXC_Fuente` | Texto | Fecha de cálculo e ids de los elementos de origen |

Crea una tabla de *Modelos genéricos* filtrada por `EXC_Tipo` y suma `EXC_Volumen`. El archivo de parámetros
compartidos se guarda en `%APPDATA%\Excavaciones\Excavaciones_ParametrosCompartidos.txt` (GUIDs fijos, iguales
en todos los proyectos).

---

## Cómo calcula (metodología)

1. **Terreno**: la cara superior del Toposolid (o del elemento elegido) se triangula → campo de alturas.
   Con *Nivel: plano horizontal* el terreno es un rectángulo plano a la cota del nivel (+ desfase) que cubre la
   zona de excavación más el alcance de los taludes; el resto del cálculo es idéntico.
2. **Envolvente de corte**: desde el contorno de la plataforma (o la huella de la cimentación + sobreexcavación)
   en la cota de fondo, hacia arriba, el contorno se desplaza `H × Δz` por tramo (loft entre polígonos); las
   bermas son escalones horizontales. La altura se extiende automáticamente hasta que el talud *sale* al
   terreno (daylight).
3. **Envolvente de relleno** (plataforma): igual pero hacia abajo con el talud de relleno.
4. **Corte** = terreno ∩ envolvente de corte; **Relleno** = (aire sobre el terreno) ∩ envolvente de relleno.
   Se calcula triángulo a triángulo (columna vertical ∩ semiespacio bajo/sobre el plano del triángulo), lo que
   mantiene cada booleana pequeña y robusta. Los volúmenes son exactos para el terreno triangulado.
5. Las caras de cada pieza se clasifican (terreno natural / fondo / talud) para obtener las **áreas**.
6. Para cimentación, el **volumen enterrado** de cada elemento se obtiene intersectando sus sólidos con el
   terreno y se descuenta para el relleno con material propio.

Desfase de contornos: inglete propio → `CurveLoop.CreateViaOffset` de Revit → envolvente convexa (solo si los
anteriores fallan; se avisa, resultado conservador).

## Limitaciones conocidas
- El terreno se trata como campo de alturas (una sola superficie superior); no hay voladizos ni túneles.
- Las subdivisiones de Toposolid no se consideran (se usa el Toposolid anfitrión).
- `ExcavateBy` sobre el Toposolid es experimental (limitación del API); el plugin no modifica el terreno,
  genera sólidos de excavación independientes.
- Toposólidos muy densos (decenas de miles de triángulos) tardan más; se procesan solo los triángulos bajo la
  zona de excavación.
- Las unidades de entrada son metros; el metrado se muestra en m², m³.

## Estructura del código
```
Excavaciones.addin               manifiesto del add-in
src/Excavaciones/
  App.cs                         pestaña y botones del ribbon
  Commands/                      CmdExcavacionPlataforma, CmdExcavacionCimentacion, CmdMetrado
  Core/TerrainModel.cs           triangulación del terreno
  Core/PolygonUtils.cs           desfases (inglete, Revit, envolvente convexa), unión de curvas
  Core/SolidUtils.cs             extrusiones, lofts, semiespacios y booleanas tolerantes
  Core/EarthworkEngine.cs        envolventes con taludes/bermas, corte/relleno, áreas, unión de excavaciones
  Core/ExcavationParams.cs       parámetros compartidos EXC_*
  Core/Footprint.cs              contornos de plataforma y huellas de cimentación
  UI/                            ventanas WPF (parámetros, progreso, resultados)
build/install.ps1                instalación en %APPDATA%\Autodesk\Revit\Addins\2027
.github/workflows/build.yml      compila en Windows y publica el paquete
```
Objetivo: `net10.0-windows`, paquetes `Nice3point.Revit.Api.RevitAPI/RevitAPIUI 2027.3.0`.
