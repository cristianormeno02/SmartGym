## MODIFIED Requirements

### Requirement: Auditoría Global y Dashboard de Métricas
El sistema SHALL registrar en una bitácora inmutable las operaciones críticas del sistema (identificador de usuario, entidad afectada, fecha/hora, operación y valores anteriores/nuevos) y calcular métricas operativas y comerciales para el dashboard administrativo. Todo importe monetario exhibido en el dashboard de métricas (como los ingresos del mes) SHALL mostrarse con 2 decimales, coma (`,`) como separador de decimales y punto (`.`) como separador de miles (ej. `$1.250.000,00`).

En cualquier reporte operativo, bitácora de auditoría, listado gerencial, grilla de turnos o agenda de control, todas las fechas y horas SHALL exhibirse con fechas en formato **`DD/MM/AAAA`** y las horas y horarios exclusivamente con horas y minutos en formato **`HH:mm`**, sin mostrar segundos (ej. `26/10/2026 18:30` y no `26/10/2026 18:30:00`; `09:30` y no `09:30:00`).

#### Scenario: Visualización de marcas temporales en bitácora de auditoría sin segundos
- **WHEN** un administrador consulta la bitácora de auditoría global del sistema
- **THEN** la columna de fecha y hora presenta las marcas temporales en formato `DD/MM/AAAA HH:mm` omitiendo los segundos (por ejemplo `25/10/2026 22:50`)

#### Scenario: Visualización de horarios en reportes operativos sin segundos
- **WHEN** se genera o visualiza un reporte o grilla de clases, turnos o asistencias
- **THEN** los horarios de inicio y fin se exhiben con formato `HH:mm` (por ejemplo `09:30` y `10:30`, en lugar de `09:30:00`)
