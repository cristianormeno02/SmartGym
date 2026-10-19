## Why

Actualmente, la carga de horarios recurrentes se realiza de manera unitaria (un registro por cada combinación de día, sala, instructor y rango horario). Cuando una actividad tiene una grilla densa (por ejemplo, Entrenamiento Funcional con clases Lunes, Miércoles y Viernes 09:30; Martes, Jueves y Viernes 16:00; Martes, Jueves y Viernes 19:00; Lunes, Miércoles y Viernes 21:00), el usuario debe repetir una y otra vez la selección de actividad, sala, instructor, cupo y vigencia, tornando el proceso lento y tedioso.

Asimismo, la visualización actual de horarios (tanto en el portal administrativo como en la grilla pública `/horarios`) se limita a pestañas aisladas por día con tarjetas, dificultando una visión integral y comparativa de los horarios de todas las actividades. Se necesita un flujo ágil de carga masiva por sala e instructor para una actividad dada, conservando la capacidad de edición individual posterior, complementado con una vista de calendario semanal con filtro multi-actividad y detalle al hacer clic en cada turno.

## What Changes

- **Carga en lote de horarios recurrentes**:
  - Modal de programación masiva con cabecera común (Actividad, Sala, Instructor, Secretario opcional, vigencia y cupo) y uno o varios grupos de horario (días de la semana + hora de inicio + hora de fin).
  - Previsualización de los turnos resultantes antes de confirmar.
  - Nuevo endpoint `POST /api/schedules/batch` que valida y crea todos los horarios (y sus clases concretas) en una única transacción: si una sola franja es inválida o colisiona, no se persiste ninguna.
  - Respuesta `409` estructurada con **todas** las franjas en conflicto, para que el modal las marque sin perder lo cargado.
- **Detección de colisiones de instructor**: además de la colisión de sala existente, la creación y edición de horarios recurrentes (unitaria y en lote) rechaza que un mismo instructor tenga dos horarios superpuestos en día, horario y vigencia. **BREAKING** (de comportamiento): ediciones que hoy se aceptan con instructor superpuesto pasarán a responder `409`.
- **Edición individual preservada**: cada horario generado en lote es un `RecurringSchedule` independiente, editable y dable de baja individualmente mediante los endpoints existentes.
- **Calendario semanal de horarios**:
  - Componente de calendario compartido (Lunes a Domingo × franja horaria) usado en el portal administrativo y en la grilla pública `/horarios`, con alternancia calendario / tarjetas.
  - Navegación por semana: el calendario muestra los horarios cuya vigencia abarca la semana visible.
  - Filtro "Todas las actividades" o selección de una o varias actividades.
  - Clic en un bloque abre el detalle del turno; las acciones Editar y Dar de baja solo se muestran a `Administrator` / `Secretary`.

## Capabilities

### New Capabilities
<!-- Ninguna -->

### Modified Capabilities
- `activities-schedule`: se agrega la carga en lote de horarios recurrentes y la vista de calendario semanal con filtro multi-actividad y detalle; se extiende la detección de colisiones al instructor; se actualizan los requerimientos de control de acceso y de gestión de horarios en la interfaz para incorporar la carga masiva y el calendario.

## Impact

- **Backend**:
  - `RecurringScheduleDtos`: nuevos records `CreateBatchRecurringScheduleRequest`, `BatchScheduleSlotRequest`, `ScheduleConflictDto`.
  - `IRecurringScheduleService` / `RecurringScheduleService`: método `CreateBatchAsync`; validación de colisión de instructor en `CreateAsync`, `UpdateAsync` y `CreateBatchAsync`; extracción de la validación de referencias (actividad, sala, instructor, secretario) a un helper compartido.
  - Nueva excepción de aplicación `ScheduleConflictException` que transporta la lista de conflictos.
  - `SchedulesController`: endpoint `POST /api/schedules/batch` (`RequireStaff`) y mapeo de `ScheduleConflictException` a `409` con cuerpo estructurado.
  - Pruebas unitarias (`SmartGym.Domain.UnitTests`) y de integración (`SmartGym.IntegrationTests`) para la atomicidad real sobre PostgreSQL.
  - Sin cambios de esquema ni migraciones.
- **Frontend**:
  - `gym.models.ts` y `GymApiService.createBatchSchedules(request)`.
  - Nuevo `ScheduleBatchModalComponent` (`features/catalog/schedule-batch-modal`).
  - Nuevo `ScheduleCalendarComponent` (`features/catalog/schedule-calendar`) y `ScheduleDetailModalComponent` (`features/catalog/schedule-detail-modal`), reutilizados por `SchedulesManagementComponent` y `SchedulesComponent` (`/horarios`).
  - Pruebas unitarias con Vitest (`ng test`).
- **Riesgo de datos**: horarios existentes con instructor superpuesto no se modifican, pero su edición posterior será rechazada hasta resolver el solapamiento.
