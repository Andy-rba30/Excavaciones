# Nuevo: nivel como plano de terreno (parte superior de la excavación)

Commit `726a372` en la rama `claude/admiring-ride-c3nunt`. Compilación en GitHub Actions: **verde**.
Paquete listo para descargar: Actions → run "Permitir usar un nivel como plano de terreno…" → artefacto `Excavaciones-Revit2027`.

## Cómo se usa
En **Plataforma** y en **Cimentación**, en el combo **Terreno natural** aparece la opción:

> **Nivel: plano horizontal (el nivel es la parte superior de la excavación)**

Al elegirla se muestra un panel con **Nivel** y **Desfase (m)**.
El nivel (+ desfase) se toma como superficie del terreno. Desde esa cota hacia abajo se construye la excavación con el **talud** y las **bermas** indicados, igual que con un Toposolid.

- **Plataforma**: si la cota de plataforma está por debajo del nivel → corte con talud de corte. Si está por encima → relleno con talud de relleno.
- **Cimentación**: la excavación empieza en el nivel y baja con el talud hasta el fondo de cada zapata (+ profundidad adicional), con la sobreexcavación lateral.

## Qué cambió en el código
| Archivo | Cambio |
|---|---|
| `Core/TerrainModel.cs` | `FromPlane(z, …)`: terreno plano de dos triángulos que cubre la zona de excavación. |
| `Core/EarthworkEngine.cs` | `SlopeSpec.Reach(h)`: alcance horizontal del talud (con bermas), usado para dimensionar el plano. |
| `Commands/CommandSupport.cs` | `PlaneTerrain(...)` y `DescribeLevelPlane(...)`. |
| `Commands/CmdExcavacionPlataforma.cs` | Si el terreno es por nivel, crea el plano y añade la fila "Cota de terreno de referencia" al metrado. |
| `Commands/CmdExcavacionCimentacion.cs` | Igual; dimensiona el plano con las cajas de las cimentaciones. `ExcavateBy` se omite (no hay Toposolid). |
| `UI/*Window.xaml(.cs)`, `UI/Settings.cs` | Opción en el combo, panel Nivel + desfase, nuevos campos `TerrainFromLevel`, `TerrainLevel`, `TerrainLevelOffsetM`. |
| `README.md`, `COMO_FUNCIONA.md` | Documentación actualizada. |

## Notas
- El parámetro `EXC_Fuente` de los sólidos guarda el nombre del nivel usado (p. ej. `Nivel Terreno +0.50 m`).
- Si la cota de plataforma coincide con el nivel, el metrado avisa que no hay corte ni relleno.
- No se pudo compilar en este contenedor (no hay .NET); la verificación es la del workflow en Windows, que pasó.
