## MODIFIED Requirements

### Requirement: Programación y Gestión de Horarios Recurrentes (Schedules)
La aplicación SHALL proveer una grilla semanal para configurar turnos recurrentes vinculando Actividad (activa), Sala (activa), Instructor (persona activa con rol `Instructor`), Secretario opcional (persona activa con cuenta de usuario y rol `Secretary`), día de la semana, hora de inicio, hora de fin, vigencia (`validFrom` obligatorio, `validTo` opcional) y cupo (`maxCapacity` opcional), consumiendo `/api/schedules`. La interfaz SHALL mostrar los días de Lunes a Domingo y convertirlos al valor numérico de `DayOfWeek` del backend (0 = Domingo … 6 = Sábado). La grilla SHALL poder visualizarse como calendario semanal o como tarjetas por día.

En los formularios de programación de horarios (alta unitaria y alta por lote masivo) y en la visualización de detalles de horarios y listados administrativos, las fechas de vigencia (`validFrom`, `validTo`) SHALL ingresarse y exhibirse obligatoriamente en formato **`DD/MM/AAAA`**.

#### Scenario: Carga de vigencias en programación de horario unitario o por lote
- **WHEN** el usuario ingresa la vigencia desde `01/11/2026` y hasta `31/12/2026` en el formulario de horario
- **THEN** los campos validan y muestran las fechas con formato `DD/MM/AAAA` y transmiten los valores ISO correspondientes al backend

#### Scenario: Visualización de vigencia en el detalle del horario
- **WHEN** se consulta el detalle de un horario recurrente programado
- **THEN** la fecha de inicio y fin de vigencia se presentan al usuario como `DD/MM/AAAA`
