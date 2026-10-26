## MODIFIED Requirements

### Requirement: Consulta eficiente de clases disponibles y evaluación de elegibilidad
El sistema SHALL exponer `GET /api/reservations/available-classes` para que el alumno autenticado (o el personal indicando `studentId`) consulte las clases `Open` futuras de actividades activas en un rango de fechas locales (`from`, `to`, como máximo `AvailableClassesMaxRangeDays`, por defecto 31 días), con filtros opcionales por `activityId`, `roomId` e `instructorId`. Los cupos, la lista de espera y la reserva propia SHALL calcularse en base de datos, y la elegibilidad SHALL evaluarse cargando una sola vez los derechos del alumno, sin consultas por clase. Por cada clase el sistema SHALL informar:
- Identificador, actividad, sala, instructor efectivo, horario (`StartsAtUtc`, `StartsAtLocal`, `EndsAtLocal`), `Capacity`, `AvailableSpots` y `WaitlistCount`.
- Si está dentro de la ventana de anticipación (`MinAdvanceBookingMinutes`, `MaxAdvanceBookingDays`).
- Si el alumno ya posee una reserva activa (`CONFIRMED`, `PENDING` o `WAITLISTED`), con su identificador, estado y posición en espera.
- `CanReserve`, `CanJoinWaitlist`, `SuggestedAccessType`, `SelectedMembershipId`, los créditos utilizables y, cuando no sea elegible, `IneligibilityReasons` con código y mensaje.

En la interfaz web del Portal del Alumno (`StudentPortalComponent`), la sección de clases disponibles SHALL presentar una barra interactiva de filtros que permita al alumno combinar:
1. **Filtro por Actividad**: Seleccionar "Todas las actividades" o una actividad específica entre las disciplinas que cuentan con clases en la ventana consultada.
2. **Filtro por Fecha**: Seleccionar "Todas las fechas", accesos directos rápidos ("Hoy", "Mañana") o una fecha específica seleccionable en formato `DD/MM/AAAA`.
3. **Filtro por Franja Horaria**: Filtrar por "Todas las franjas", "Mañana" (inicio anterior a las 12:00), "Tarde" (inicio entre las 12:00 y las 17:59) o "Noche" (inicio a partir de las 18:00).
4. **Filtro por Disponibilidad de Cupo**: Alternar entre ver todas las clases o únicamente aquellas con cupos libres disponibles (`availableSpots > 0`), ocultando las que estén completas.
5. **Filtro por Sala**: Seleccionar "Todas las salas" o una sala específica.
6. **Búsqueda por Texto**: Filtrar en tiempo real por coincidencia parcial de texto en el nombre de la actividad o el nombre del instructor.
7. **Acción de Limpiar Filtros e Indicador de Resultados**: Proveer un botón accesible para restablecer todos los filtros a sus valores predeterminados y un indicador textual de la cantidad de clases visibles sobre el total (ej. "Mostrando 3 de 12 clases"). Cuando ningún resultado coincida, la vista SHALL mostrar un mensaje orientativo con opción directa para restablecer los filtros.

Todas las fechas de clases, reservas próximas y ofertas temporales mostradas en la interfaz SHALL exhibirse con formato `DD/MM/AAAA` (y `DD/MM/AAAA HH:mm` cuando incluya hora del día).

#### Scenario: Filtrado por actividad en el panel del alumno
- **WHEN** el alumno se encuentra en la sección de clases disponibles y selecciona la actividad "Spinning"
- **THEN** la lista muestra únicamente las clases de "Spinning" de los próximos días y el contador refleja la cantidad correspondiente

#### Scenario: Filtrado por franja horaria "Mañana"
- **WHEN** el alumno selecciona la franja horaria "Mañana"
- **THEN** la lista muestra únicamente las clases cuya hora de inicio es anterior a las 12:00

#### Scenario: Filtrado por franja horaria "Noche"
- **WHEN** el alumno selecciona la franja horaria "Noche"
- **THEN** la lista muestra únicamente las clases cuya hora de inicio es igual o posterior a las 18:00

#### Scenario: Filtrado por fecha puntual con formato DD/MM/AAAA
- **WHEN** el alumno elige una fecha específica ingresada o seleccionada en formato `DD/MM/AAAA` (ej. `26/10/2026`)
- **THEN** la lista filtra y muestra exclusivamente las clases programadas para esa fecha

#### Scenario: Filtrado solo con lugares disponibles
- **WHEN** el alumno activa la opción de ver "Solo con lugares disponibles"
- **THEN** se ocultan las clases que tienen cupo agotado (`availableSpots == 0`), mostrando únicamente clases con cupo libre para reserva inmediata

#### Scenario: Combinación múltiple de filtros
- **WHEN** el alumno selecciona la actividad "Funcional", la franja "Tarde" y activa "Solo con lugares disponibles"
- **THEN** se muestran únicamente las clases que cumplen simultáneamente todos los criterios seleccionados

#### Scenario: Restablecimiento de filtros
- **WHEN** el alumno hace clic en "Limpiar filtros"
- **THEN** todos los controles de filtro vuelven a su estado inicial y se muestran nuevamente todas las clases disponibles de la semana

#### Scenario: Visualización de fechas en formato DD/MM/AAAA en reservas y clases
- **WHEN** el alumno visualiza una clase del 27 de octubre de 2026 o una reserva activa en su panel
- **THEN** la fecha se presenta como `27/10/2026` y no en formato anglosajón (`10/27/2026` ni `Oct 27, 2026`)

#### Scenario: Ingreso de vencimiento de certificado médico en formato DD/MM/AAAA
- **WHEN** el alumno ingresa la fecha de vencimiento de su apto médico para adjuntarlo
- **THEN** el campo solicita e interpreta la fecha en formato `DD/MM/AAAA` antes de enviarla al servicio
