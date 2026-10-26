## MODIFIED Requirements

### Requirement: Auditoría Global y Dashboard de Métricas
El sistema SHALL registrar en una bitácora inmutable las operaciones críticas del sistema (identificador de usuario, entidad afectada, fecha/hora, operación y valores anteriores/nuevos) y calcular métricas operativas y comerciales para el dashboard administrativo. Todo importe monetario exhibido en el dashboard de métricas (como los ingresos del mes) SHALL mostrarse con 2 decimales, coma (`,`) como separador de decimales y punto (`.`) como separador de miles (ej. `$1.250.000,00`).

#### Scenario: Visualización de métricas en dashboard administrativo
- **WHEN** un administrativo ingresa al panel principal
- **THEN** el sistema presenta en tiempo real la cantidad de alumnos activos, membresías vigentes, asistencia del día, porcentaje de ocupación e ingresos del período seleccionado formateados con 2 decimales, coma decimal y punto como separador de miles (por ejemplo `$150.000,00`)
