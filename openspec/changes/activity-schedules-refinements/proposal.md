## Why

Al programar horarios recurrentes (de forma unitaria o masiva), el selector de secretarios no restringe adecuadamente las opciones a personas con cuenta de usuario activa y rol exclusivo `Secretary`, exponiendo en la práctica un listado más amplio del esperado. Además, el calendario semanal de horarios requiere mejoras de usabilidad operativa: filtrar por sala (con opción de ver todas), acotar por defecto el eje horario únicamente a los bloques con actividades (con opción de ver el rango horario completo) y reflejar el color configurado en cada actividad (`colorHex`) en lugar de colores arbitrarios.

## What Changes

- **Restricción de secretarios en programación de horarios (Backend + Frontend)**:
  - **BREAKING**: `RecurringScheduleService` (`CreateAsync`, `UpdateAsync`, `CreateBatchAsync`) validará que el `secretaryId` opcional corresponda a una persona activa que posea cuenta de usuario activa (`User != null && User.IsActive`) y rol `Secretary` activo (dejando de aceptar personas sin usuario o solo con rol `Administrator`).
  - Los formularios de horario unitario (`ScheduleFormModalComponent`) y masivo (`ScheduleBatchModalComponent`) listarán en el selector de secretarios únicamente personas activas con rol `Secretary` y cuenta de usuario (`hasUserAccount === true`), preservando en edición el secretario previamente asignado si ya no estuviera en la lista.
- **Color de actividad en horarios (`ActivityColorHex`)**:
  - `RecurringScheduleDto` incluirá el campo opcional `ActivityColorHex` proveniente de `Activity.ColorHex`.
  - El calendario semanal (`ScheduleCalendarComponent`) y los chips de filtro de actividad mostrarán el color configurado en la actividad (`activityColorHex`), usando un color de respaldo si la actividad no tiene color asignado.
- **Filtro por sala en vistas de horarios**:
  - Se incorporará en la barra de filtros de `/horarios` y de la gestión administrativa de horarios un filtro por sala que permita ver "Todas las salas" o seleccionar una o varias salas, combinándose con el filtro de actividades tanto en la vista Calendario como en la vista Tarjetas.
- **Modo de visualización horaria en el calendario (Solo horarios con actividades vs. Rango completo)**:
  - Por defecto, el calendario mostrará únicamente las franjas horarias que contienen actividades programadas en la semana visible (compactando u omitiendo las horas vacías sin actividades).
  - Se proveerá un conmutador en la barra del calendario para alternar entre **"Solo con actividad"** (por defecto) y **"Rango horario completo"** (grilla continua de horas).

## Capabilities

### New Capabilities

<!-- None -->

### Modified Capabilities

- `activities-schedule`: Refina las reglas de elegibilidad y selección de secretario (persona activa con usuario activo y rol `Secretary`), agrega `activityColorHex` a los horarios recurrentes, incorpora el filtro por sala junto al de actividades, el uso del color configurado de cada actividad en el calendario y el modo por defecto de visualización acotado a horarios con actividades con opción de ver todo el rango horario.

## Impact

- **Backend (`SmartGym.Application`)**:
  - `RecurringScheduleDtos.cs`: nuevo campo `string? ActivityColorHex = null` en `RecurringScheduleDto`.
  - `RecurringScheduleService.cs`: ajuste en `ValidateResourcesAsync` para exigir rol `Secretary` activo y cuenta de usuario activa (`User != null && User.IsActive`), y mapeo de `s.Activity?.ColorHex` en `MapToDto`.
  - Pruebas unitarias e integración en `SmartGym.Domain.UnitTests` y `SmartGym.IntegrationTests`.
- **Frontend (`frontend/src/app`)**:
  - `gym.models.ts`: `activityColorHex?: string | null` en `RecurringSchedule`.
  - `schedule-secretary-options.ts`: consultar únicamente `role: 'Secretary'`, `status: PersonStatus.Active` y filtrar `p.hasUserAccount === true`.
  - `schedule-calendar.ts`: renderizado con `activityColorHex`, modo por defecto compactado a horarios con actividades y toggle para ver todo el rango horario continuo.
  - `schedules-management.ts` y `schedules.ts`: filtro por sala ("Todas las salas" + selección de salas) combinado con el filtro de actividades y colores de actividad.
