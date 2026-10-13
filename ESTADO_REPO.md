# Estado del repo `Andy-rba30/Excavaciones`

Fecha de revisión: 2026-10-13

## Rama y árbol de trabajo
- Rama actual: `claude/admiring-ride-c3nunt`. Sin cambios pendientes, árbol limpio.
- Esta rama está al mismo nivel que `claude/sharp-noether-71505s`: ambas apuntan al mismo commit, sin diferencias.
- No existe rama `main` ni `master`. Solo hay esas dos ramas `claude/*`, en local y en remoto.
- No hay pull requests abiertos ni cerrados.

## Historial
| Commit | Mensaje |
|---|---|
| `b6f01b7` | Mover README a la raíz del repositorio |
| `f0b520c` | Plugin de excavaciones para Revit 2027: plataforma con taludes, excavación por cimentación y metrado |

## Contenido del proyecto
- Add-in de **Revit 2027** en **.NET 10 / WPF**. Incluye `Excavaciones.sln`, `Excavaciones.addin`, `global.json` y `build/install.ps1`.
- Tres comandos: **Plataforma**, **Cimentación** y **Metrado**, con sus ventanas XAML e iconos.
- Núcleo de cálculo en `src/Excavaciones/Core` (EarthworkEngine, TerrainModel, SolidUtils, PolygonUtils, etc.).
- Workflow `.github/workflows/build.yml`: compila en Windows y sube el artefacto `Excavaciones-Revit2027`.

## CI (GitHub Actions)
- Último run (commit actual `b6f01b7`): **verde**. Compiló y empaquetó bien.
- Primer run (commit `f0b520c`): **falló** en el paso "Empaquetar", no en la compilación. El README aún no estaba en la raíz y el paso lo copiaba desde ahí. El segundo commit lo corrigió.

## Resumen
El repo está limpio, sincronizado con el remoto y con la compilación pasando.
Lo único llamativo es que no existe una rama principal; si quieres un `main`, habría que crearlo a partir de este commit.
