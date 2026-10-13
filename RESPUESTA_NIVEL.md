# ¿Se puede usar un nivel como referencia para calcular corte y relleno?

**Sí, se puede.** Y en parte ya existe.

## Lo que ya existe
En **Plataforma** la ventana tiene la opción **Nivel + Desfase** (marcada por defecto).
El comando toma `Level.Elevation + desfase` como cota de fondo de corte y rasante de relleno.
Ver `src/Excavaciones/UI/PlataformaWindow.xaml` y `src/Excavaciones/Commands/CmdExcavacionPlataforma.cs` (línea 48).

## Lo que NO existe hoy
En **Cimentación** el fondo de excavación se toma de la **cara inferior de cada zapata** más la profundidad adicional.
No hay forma de decir "excava hasta tal nivel".

## Dos opciones posibles (dime cuál quieres)

### Opción 1: Fondo de excavación por nivel en Cimentación
- Añadir en la ventana de Cimentación un selector **Nivel + desfase** que reemplace la cota de la cara inferior.
- Útil para excavación masiva hasta un nivel de fondo común.
- Cambio **pequeño**: ventana XAML, `Settings.cs` y una línea en el cálculo de `zBase` en `CmdExcavacionCimentacion.cs`.

### Opción 2: Usar un nivel como terreno de referencia (en vez del Toposolid)
- Calcular corte y relleno contra un **plano horizontal** a la cota de un nivel.
- Útil cuando no hay topografía en el modelo o cuando el terreno ya está nivelado.
- Cambio **mediano**: crear un `TerrainModel` plano a partir del nivel y añadir la opción en el combo *Terreno natural* de ambas ventanas (Plataforma y Cimentación).

## Siguiente paso
Confirma si quieres la **opción 1**, la **opción 2** o **las dos**, y lo implemento en esta rama.
