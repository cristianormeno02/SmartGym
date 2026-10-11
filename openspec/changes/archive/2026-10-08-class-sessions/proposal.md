## Why

`ClassSession` ya existe (`Domain/Entities/Activities/ClassSession.cs`, `ClassSessionService`, `ClassSessionsController`, migración `AddClassSessions`), pero su modelo es insuficiente para operar el calendario real del gimnasio:

- **Modelo temporal ambiguo**: `Date` + `StartTime` + `EndTime` sin zona horaria. `ReservationService` calcula la ventana de cancelación tardía tratando la hora local como UTC (`session.Date.ToDateTime(session.StartTime)` contra `DateTime.UtcNow`), lo que desplaza la política 3 horas en Argentina. Tampoco se pueden representar clases que cruzan la medianoche.
- **Reemplazos destructivos**: `AssignSubstituteInstructor` sobrescribe `InstructorId`; no hay motivo, responsable, historial ni soporte para secretarios.
- **Estados mezclados**: `InProgress` y `Finished` se persisten mediante acciones manuales (`/start`, `/finish`) aunque dependen solo del tiempo; no existe un estado "abierta a reservas" ni la reprogramación.
- **Suspensión sin efectos**: `Suspend` cambia el estado pero no cancela reservas ni restituye créditos, incumpliendo el requisito vigente de `activities-schedule`. `NotificationService.NotifyClassSuspendedAsync` existe pero nadie lo invoca.
- **Sin protección de solapamientos** de sala o instructor, sin concurrencia optimista en ediciones administrativas y sin generación automática del calendario.

Este change **refactoriza** el modelo existente y separa la gestión de clases concretas en la capability `class-sessions`.

## What Changes

- **BREAKING – Modelo temporal**: `Date`/`StartTime`/`EndTime` se reemplazan por `StartsAtUtc`/`EndsAtUtc` (`timestamptz`). La zona horaria del gimnasio se configura en `Gym:TimeZoneId` (por defecto `America/Argentina/San_Juan`). Los DTOs exponen instantes UTC y la fecha/hora local.
- **BREAKING – Personal programado vs. efectivo**: `InstructorId`/`OriginalInstructorId`/`SubstituteInstructorId` se reemplazan por `ScheduledInstructorId`/`EffectiveInstructorId` (ambos obligatorios) y `ScheduledSecretaryId`/`EffectiveSecretaryId` (opcionales), con historial en la nueva tabla `ClassSessionStaffChanges`.
- **BREAKING – Máquina de estados**: estados persistidos `Scheduled` (PROGRAMADA), `Open` (ABIERTA), `Suspended` (SUSPENDIDA), `Cancelled` (CANCELADA), `Rescheduled` (REPROGRAMADA). `InProgress`/`Finished` pasan a ser estados **derivados** (EN_CURSO, FINALIZADA), junto con COMPLETA. Se eliminan los endpoints `/start` y `/finish`.
- **BREAKING – Reservas solo en clases ABIERTAS**: `ReservationService` deja de aceptar reservas en `Scheduled`. Las clases se abren manualmente o automáticamente al entrar en la ventana de apertura configurable.
- **BREAKING – Rutas**: `/manual` → `POST /api/class-sessions`; `/substitute` → `/replace-instructor`; `/generate` se conserva para ejecuciones manuales.
- **Renombrado**: `MaxCapacity` → `Capacity`. `ReservedCount`/`AttendedCount` **se conservan** como contadores mantenidos por `Reservations` bajo el bloqueo de fila existente (`LockClassSessionForUpdateAsync`).
- **Generación automática idempotente** en ventana móvil (por defecto 60 días) mediante un `BackgroundService`, con índice único parcial `(RecurringScheduleId, StartsAtUtc)`.
- **Propagación de cambios de horario**: al editar o cerrar la vigencia de un `RecurringSchedule`, las sesiones futuras que aún no se abrieron ni tienen reservas se actualizan o cancelan. Las demás se informan como divergentes.
- **Secretario en horarios**: `RecurringSchedule` incorpora `SecretaryId` opcional, que se copia a las sesiones generadas.
- **Suspensión, cancelación y reprogramación con efectos**: motivos tipificados, responsable y fecha. Las reservas activas se cancelan con devolución de crédito y se notifica a los alumnos mediante handlers de eventos de dominio in-process.
- **Reprogramación trazable**: crea una nueva sesión extraordinaria vinculada (`RescheduledFromSessionId`/`RescheduledToSessionId`).
- **Cambio de sala y reemplazos de staff** auditados, con validación de solapamientos garantizada por restricciones de exclusión en PostgreSQL (`btree_gist`).
- **Concurrencia optimista** con `xmin` (`Version`), como `Person` y `Activity`. Responde 409.
- **Eventos de dominio in-process**: infraestructura mínima propia (`IDomainEvent`, `IDomainEventHandler<T>`, despacho dentro de `SaveChangesAsync`), sin MediatR.

## Capabilities

### New Capabilities
- `class-sessions`: ciclo de vida de clases concretas. Comprende la generación idempotente y la propagación de cambios desde horarios, las clases extraordinarias, el staff programado vs. efectivo, la sala, el cupo, los estados persistidos y derivados, la suspensión, la cancelación, la reprogramación, la inmutabilidad histórica, la concurrencia y las consultas de agenda y disponibilidad.

### Modified Capabilities
- `activities-schedule`: los horarios recurrentes incorporan un secretario opcional y propagan sus cambios a sesiones aún no abiertas. Los requisitos de clase concreta, suspensión y reemplazos se **mueven** a `class-sessions`.
- `reservations-attendance`: las reservas exigen que la clase esté ABIERTA y no haya comenzado. Las ventanas de cancelación se calculan sobre `StartsAtUtc`. Las reservas activas se cancelan con devolución íntegra cuando la clase se suspende, se cancela o se reprograma.

## Prerequisites

- **Archivar `activities-catalog` antes de aplicar este change**: ambos modifican `activities-schedule` y `reservations-attendance`. Los deltas de este change parten del texto ya sincronizado por `activities-catalog` (resolución del cupo efectivo, chequeo de actividad `ACTIVE` y rango etario). Su tarea pendiente 2.4 (verificación de la migración contra PostgreSQL) debe cerrarse primero.

## Impact

- **Domain**: `ClassSession` (refactor), `ClassSessionStatus` (valores nuevos y retirados), nuevos `ClassSessionStaffChange`, `ClassSessionSuspensionReason`, `ClassSessionCancellationReason`, `StaffRole`, infraestructura `IDomainEvent`. Se modifica `RecurringSchedule` (`SecretaryId`).
- **Application**: reescritura de `ClassSessionService` y de sus DTOs. Se agregan `ClassSessionGenerator`, `ClassSessionScheduleSync` y los handlers de eventos en `Reservations` y `Operations`. Se ajustan `ReservationService`, `DashboardService`, `NotificationService`, `CreditLedgerService` y `RecurringScheduleService`.
- **Infrastructure**: configuración EF, despacho de eventos en `SmartGymDbContext.SaveChangesAsync`, `ClassSessionGenerationWorker` (`BackgroundService`), extensión `btree_gist` y migración `ClassSessionsRefactor` con conversión de datos.
- **WebApi**: `ClassSessionsController` reescrito, nuevas secciones `Gym` y `ClassSessions` en `appsettings.json`.
- **Base de datos**: conversión de los datos existentes (las fechas locales pasan a UTC, el instructor se mapea a programado/efectivo, los estados `InProgress`/`Finished` se remapean). La migración tiene `Down` funcional.
- **Frontend**: los consumidores de `/api/class-sessions` deben adaptarse a los nuevos campos y rutas (fuera del alcance de este change; se documenta el contrato).
- **Fuera de alcance**: lista de espera y migración automática de reservas a la sesión reprogramada, marcado automático de NO_SHOW, notificaciones por email/push/WhatsApp, sistema de permisos granulares.
