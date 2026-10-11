# Design Document — Class Sessions (`class-sessions`)

## Context

Smart-Gym es un Monolito Modular en .NET 10 (ASP.NET Core Web API) con EF Core + PostgreSQL (Npgsql) y frontend Angular. La motivación está en [proposal.md](proposal.md).

Estado actual relevante (verificado en el código):
- `ClassSession : BaseEntity` usa `Date` (`DateOnly`), `StartTime`/`EndTime` (`TimeOnly`), `MaxCapacity`, `ReservedCount`, `AttendedCount`, `InstructorId`, `OriginalInstructorId`, `SubstituteInstructorId`, `Status`, `CancellationReason` (usado también para suspensiones) y `SuspendedAtUtc`. La tabla es `"ClassSessions"`, la FK a horario es `RecurringScheduleId` (`SetNull`) y existe un índice **no único** `(RecurringScheduleId, Date)`.
- `ClassSessionStatus { Scheduled = 1, InProgress = 2, Finished = 3, Suspended = 4, Cancelled = 5 }` se persiste como `int`.
- `ClassSessionService` genera sesiones bajo demanda (`POST /generate`) y resuelve el cupo como `schedule.MaxCapacity ?? activity.DefaultCapacity` (alineado con `activities-catalog`).
- `ReservationService` serializa las modificaciones de cupo con `SmartGymDbContext.LockClassSessionForUpdateAsync` (`SELECT ... FOR UPDATE` dentro de una transacción). Mantiene `ReservedCount`/`AttendedCount`, solo admite reservas en `Scheduled` y calcula la cancelación tardía con `session.Date.ToDateTime(session.StartTime)` contra `DateTime.UtcNow` (**bug**: trata la hora local como UTC).
- `RecurringSchedule` tiene `InstructorId`, sala, día, horas, vigencia (`ValidFrom`/`ValidTo`) y `MaxCapacity?`, pero **no** tiene secretario.
- La autorización es por roles y políticas (`RolesAndPolicies.cs`: `RequireStaff` = Administrator+Secretary, `RequireTeachingAndStaff` agrega Instructor, más `AllAuthenticated`). No hay un sistema de permisos granulares.
- `Person` y `Activity` usan concurrencia optimista con `xmin` (`uint Version`, `IsRowVersion()`) y responden **409** con `ConflictException`.
- No hay MediatR, outbox ni `BackgroundService` en la solución. La auditoría se registra explícitamente con `IAuditService`.
- Los tests viven en `backend/tests/SmartGym.Domain.UnitTests` y usan EF InMemory. Los índices únicos, los CHECK, las exclusiones y `xmin` se verifican con tests de configuración del modelo y de migración (mismo enfoque que `ActivityModelConfigurationTests` y `ActivitiesCatalogMigrationTests`).

## Goals / Non-Goals

### Goals
- Refactorizar `ClassSession` a un modelo temporal UTC con zona horaria del gimnasio configurable.
- Separar el staff programado del efectivo con historial auditable para instructor y secretario.
- Definir una máquina de estados con estados persistidos y derivados, y retirar `/start` y `/finish`.
- Generar el calendario de forma automática e idempotente (ventana móvil) y propagar los cambios de horario a las sesiones aún no comprometidas.
- Implementar suspensión, cancelación y reprogramación con efectos reales sobre reservas, créditos y notificaciones in-app.
- Garantizar en la base de datos la ausencia de solapamientos de sala y de instructor.
- Aplicar concurrencia optimista (`xmin`) y autorización contextual por roles existentes.
- Migrar los datos existentes sin pérdida.

### Non-Goals
- Lista de espera avanzada ni migración automática de reservas a la sesión reprogramada (las reservas se cancelan con devolución íntegra y el alumno vuelve a reservar).
- Marcado automático de NO_SHOW al finalizar la clase (sigue siendo manual, con `RecordNoShowAsync`).
- Horarios recurrentes que cruzan la medianoche (`RecurringSchedule.SetTimeSlot` sigue exigiendo `EndTime > StartTime`). Las clases extraordinarias sí pueden cruzarla.
- Validar el cupo contra `Room.Capacity`.
- Relación "instructor habilitado para la actividad".
- Sistema de permisos granulares, MediatR, outbox o notificaciones por email, push o WhatsApp.
- Cambios en el frontend (solo se documenta el contrato).

---

## Decisions

### Decision 1: Modelo temporal UTC + zona horaria del gimnasio
- **Adoptada**: `StartsAtUtc`/`EndsAtUtc` (`DateTime` con `Kind=Utc`, columna `timestamptz`). Opciones `GymOptions { TimeZoneId = "America/Argentina/San_Juan" }` en la sección `Gym`, validadas al iniciar (`ValidateOnStart`: el id debe resolverse con `TimeZoneInfo.FindSystemTimeZoneById`).
- Servicio `IGymCalendar` (Application) sobre `TimeProvider` (registrado como `TimeProvider.System` e inyectable en tests):
  - `TimeZoneInfo TimeZone`, `DateTime UtcNow`, `DateOnly Today`.
  - `DateTime ToUtc(DateOnly date, TimeOnly time)`: una hora local inexistente (salto de DST) se corre hacia adelante; una ambigua usa la primera ocurrencia.
  - `DateTimeOffset ToLocal(DateTime utc)`.
  - `(DateTime fromUtc, DateTime toUtc) LocalDayRange(DateOnly from, DateOnly to)`.
- Las requests reciben `DateTimeOffset` ISO 8601 con offset, que se normaliza a UTC. Los DTOs devuelven `StartsAtUtc` y `StartsAtLocal`/`EndsAtLocal` (`DateTimeOffset` en la zona del gimnasio).
- Validaciones de dominio: `EndsAtUtc > StartsAtUtc` y duración ≤ 24 h.
- **Alternativa descartada**: mantener `Date` + `TimeOnly`. No soporta el cruce de medianoche ni ordena ni filtra por rango con un solo índice, y es la causa del bug de la ventana de cancelación.

### Decision 2: Contadores `ReservedCount` / `AttendedCount` — se conservan
- **Adoptada**: mantener los contadores persistidos. Los actualiza **solo** `Reservations` (y los handlers de eventos de este change), siempre después de `LockClassSessionForUpdateAsync` dentro de una transacción. Con el bloqueo de fila, el contador es consistente y evita un `COUNT` en cada consulta de agenda.
- `ClassSessions` expone `AvailableSpots = Max(0, Capacity - ReservedCount)` y el estado derivado COMPLETA.
- **Alternativa descartada**: calcular `reserved_count` con `COUNT(Reservations)`. Invierte la dependencia (`ClassSessions` leería tablas de `Reservations`), encarece la agenda y obliga a reescribir el control de cupo existente sin un beneficio real.

### Decision 3: Máquina de estados
`ClassSessionStatus` (persistido como `int`; se conservan los valores numéricos existentes para simplificar la migración):

| Valor | Código | Spec | Significado |
|---|---|---|---|
| 1 | `Scheduled` | PROGRAMADA | En calendario, aún no admite reservas |
| 6 | `Open` | ABIERTA | Admite reservas |
| 4 | `Suspended` | SUSPENDIDA | Anulada por causa operativa externa tipificada (clima, feriado, etc.) |
| 5 | `Cancelled` | CANCELADA | Anulada por decisión administrativa, o por el sistema al terminar la vigencia del horario |
| 7 | `Rescheduled` | REPROGRAMADA | Reemplazada por otra sesión (`RescheduledToSessionId`) |

Se retiran `InProgress = 2` y `Finished = 3` (ver la migración, Decision 12).

**Transiciones** (métodos de dominio que reciben `DateTime nowUtc`; un incumplimiento lanza `InvalidOperationException`, que el servicio traduce a `ConflictException` → 409):

| Operación | Estados origen | Condición temporal |
|---|---|---|
| `Open` | `Scheduled` | `now < StartsAtUtc` |
| `Suspend` | `Scheduled`, `Open` | `now < EndsAtUtc` (permite suspender una clase en curso) |
| `Cancel` | `Scheduled`, `Open` | `now < StartsAtUtc` |
| `MarkRescheduled` | `Scheduled`, `Open` | `now < StartsAtUtc` |

`Suspended`, `Cancelled` y `Rescheduled` son **terminales** y no se reabren. Para recuperar una clase suspendida se crea una clase extraordinaria. La diferencia entre suspender y cancelar es semántica y temporal: la suspensión responde a una causa externa (puede ocurrir durante la clase) y la cancelación, a una decisión administrativa previa al inicio. Ambas cancelan las reservas con devolución íntegra.

**Estados derivados**: `ClassSession.GetDisplayStatus(DateTime nowUtc)` devuelve `ClassSessionDisplayStatus`. Se evalúa en este orden:
1. Si el estado es terminal (`Suspended`, `Cancelled`, `Rescheduled`), se devuelve tal cual.
2. Si `now >= EndsAtUtc` → `Finished` (FINALIZADA), tanto para `Scheduled` como para `Open`.
3. Si `StartsAtUtc <= now` → `InProgress` (EN_CURSO).
4. Si `Open` y `ReservedCount >= Capacity` → `Full` (COMPLETA).
5. En otro caso, el estado persistido.

**Apertura automática**: el worker (Decision 5) abre las sesiones `Scheduled` cuando `StartsAtUtc - AutoOpenLeadTime <= now < StartsAtUtc` (`ClassSessions:AutoOpenLeadTime`, por defecto 7 días; `null` desactiva la apertura automática). También se pueden abrir manualmente.

### Decision 4: Inmutabilidad histórica
Una sesión está **cerrada** si `now >= EndsAtUtc` o si su estado es terminal. Sobre una sesión cerrada solo se puede editar `Notes` (auditado). El resto de los cambios (cupo, sala, staff, horario, estado) se rechaza con 409. Las ediciones de cupo y la reprogramación exigen además `now < StartsAtUtc`.

### Decision 5: Generación automática idempotente (ventana móvil)
- **Adoptada**: `ClassSessionGenerator.GenerateAsync(DateOnly from, DateOnly to, Guid? scheduleId)`, invocado por:
  - `ClassSessionGenerationWorker : BackgroundService` (Infrastructure). Se ejecuta al iniciar y luego cada `ClassSessions:GenerationInterval` (por defecto 1 h) con un `PeriodicTimer`. Genera `[Today, Today + WindowDays]` (`ClassSessions:WindowDays`, por defecto 60) y después aplica la apertura automática. Los errores se registran en el log y no detienen el host.
  - `POST /api/class-sessions/generate` (Staff), para ejecuciones manuales.
  - `ClassSessionScheduleSync` (Decision 6), al crear o editar un horario.
- **Algoritmo** por horario (`IsActive`, actividad `Active`) y por fecha local `d` del rango con `schedule.OccursOn(d)`:
  1. `starts = ToUtc(d, StartTime)`, `ends = ToUtc(d, EndTime)`.
  2. Si ya existe una sesión con `(RecurringScheduleId, StartsAtUtc)` en **cualquier** estado → se omite. Así una sesión cancelada a mano no se regenera.
  3. Si la sala o el instructor se solapan con una sesión activa → se omite y se reporta como conflicto.
  4. Cupo `schedule.MaxCapacity ?? activity.DefaultCapacity`. Si no hay cupo resoluble → se omite y se reporta.
  5. Se crea en `Scheduled` con `ScheduledInstructorId = EffectiveInstructorId = schedule.InstructorId` y `ScheduledSecretaryId = EffectiveSecretaryId = schedule.SecretaryId`.
- Resultado: `GenerationResultDto(Created, Skipped, Conflicts[])`. Cada conflicto incluye horario, fecha y motivo (`ROOM_OVERLAP`, `INSTRUCTOR_OVERLAP`, `NO_CAPACITY`).
- **Idempotencia y concurrencia**: el chequeo de existencia en la aplicación es la vía normal. El índice único parcial `UX_ClassSessions_Schedule_StartsAt` sobre `(RecurringScheduleId, StartsAtUtc) WHERE "RecurringScheduleId" IS NOT NULL` es la red de seguridad. Ante una violación (`23505`) por ejecuciones concurrentes, el generador descarta los cambios pendientes, recarga y reintenta una sola vez. No se usa `ON CONFLICT DO NOTHING`: requiere SQL crudo y no es testeable con InMemory.
- **Alternativa descartada**: generar 1 o 2 años por adelantado. Multiplica filas y vuelve costosa la propagación de cambios de horario.

### Decision 6: Propagación de cambios del horario recurrente
- `ClassSessionScheduleSync.SyncAsync(scheduleId)` se invoca desde `RecurringScheduleService` en la **misma transacción** al editar un horario (hora, día, sala, instructor, secretario, cupo), cambiar su vigencia o desactivarlo.
- **Sesiones candidatas**: del horario, en `Scheduled`, con `StartsAtUtc > now`, `ReservedCount = 0` y `IsDetachedFromSchedule = false`. `IsDetachedFromSchedule` se marca en `true` ante cualquier operación manual sobre la sesión: cambio de sala, reemplazo de staff o edición de cupo o notas.
  - Si la fecha ya no corresponde al horario (otro día, fuera de vigencia, horario inactivo) → se cancela con `CancellationReason = ScheduleEnded` y `CancelledByUserId = null` (sistema).
  - En otro caso → se actualizan en el lugar `StartsAtUtc`, `EndsAtUtc`, `RoomId`, staff programado y efectivo, y `Capacity`. Si el nuevo `(RecurringScheduleId, StartsAtUtc)` ya existe, se cancela la candidata.
- Después se ejecuta `GenerateAsync` para el horario, con el fin de crear las ocurrencias nuevas.
- Las sesiones **no candidatas** (abiertas, con reservas o modificadas manualmente) **no se tocan**. Se devuelven como `DivergentSessionIds` en la respuesta de la edición del horario para que el administrativo las gestione.
- Actualizar en el lugar es aceptable en este caso: una sesión que nunca se abrió, sin reservas ni ajustes manuales, es una copia de la plantilla sin valor histórico propio.

### Decision 7: Clases extraordinarias
`RecurringScheduleId = NULL` en la misma tabla. Se requieren actividad (`Active`), sala, instructor, inicio y fin futuros; el cupo es `request.Capacity ?? activity.DefaultCapacity` (si no hay ninguno, 400). La secretaría y las notas son opcionales. Una tabla separada duplicaría reservas, asistencia y agenda.

### Decision 8: Staff programado vs. efectivo
- Columnas: `ScheduledInstructorId` (NOT NULL), `EffectiveInstructorId` (NOT NULL, inicializado igual al programado), `ScheduledSecretaryId` y `EffectiveSecretaryId` (NULL).
- Como `EffectiveInstructorId` siempre está informado, las consultas por instructor usan `EffectiveInstructorId = @id OR ScheduledInstructorId = @id` sin `COALESCE`, y la restricción de exclusión opera sobre el efectivo.
- `ReplaceInstructor(newId, reason, userId, nowUtc)` / `ReplaceSecretary(newId?, reason, ...)`: solo sobre sesiones no cerradas (sí se permite durante el curso de la clase). El nuevo valor debe diferir del efectivo actual. Volver al programado está permitido y queda auditado. Para el secretario, `null` significa "sin secretario". Cada reemplazo agrega un `ClassSessionStaffChange`.
- Validación: el instructor debe ser una `Person` activa con rol `Instructor` activo. El secretario, una `Person` activa con rol `Secretary` o `Administrator` activo.
- `ClassSessionStaffChanges`: `Id`, `ClassSessionId` (FK **Restrict**), `Role` (`Instructor`/`Secretary`, string), `PreviousPersonId?`, `NewPersonId?`, `Reason` (1–500), `ChangedByUserId?` (FK `Users`, Restrict; `null` solo en registros generados por el sistema o la migración), `ChangedAtUtc`.

### Decision 9: Sala y solapamientos
- `RoomId` directo y modificable con `ChangeRoom(newRoomId, userId, nowUtc)` (sesión no cerrada). Emite `ClassSessionRoomChanged`. No se duplican columnas programado/efectivo porque la sala no tiene impacto en honorarios.
- **Garantía en la base de datos** (extensión `btree_gist`):
  ```sql
  ALTER TABLE "ClassSessions" ADD CONSTRAINT "EX_ClassSessions_Room_NoOverlap"
    EXCLUDE USING gist ("RoomId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&)
    WHERE ("Status" IN (1, 6) AND "IsActive");
  ALTER TABLE "ClassSessions" ADD CONSTRAINT "EX_ClassSessions_Instructor_NoOverlap"
    EXCLUDE USING gist ("EffectiveInstructorId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&)
    WHERE ("Status" IN (1, 6) AND "IsActive");
  ```
  El rango semiabierto `[)` permite clases consecutivas (09:00–10:00 y 10:00–11:00).
- El servicio verifica antes (`StartsAtUtc < other.EndsAtUtc AND EndsAtUtc > other.StartsAtUtc`) para devolver un 409 descriptivo con los ids en conflicto. Una `PostgresException` con `SqlState = 23P01` se traduce también a 409 (carrera entre la verificación y el commit).
- Las exclusiones son `DEFERRABLE INITIALLY IMMEDIATE` (migración `ClassSessionOverlapConstraintsDeferrable`): se verifican por sentencia, salvo en la reprogramación, que las difiere con `SET CONSTRAINTS ... DEFERRED` porque EF inserta la sesión nueva antes de marcar la original como `Rescheduled`. Antes del `COMMIT` se fuerza `SET CONSTRAINTS ALL IMMEDIATE` para traducir una violación a 409. El chequeo previo del servicio excluye la sesión original.
- El secretario no tiene restricción de solapamiento, porque puede supervisar clases simultáneas.

### Decision 10: Suspensión, cancelación y reprogramación
- **Motivos tipificados** (enums persistidos como string con `HasConversion<string>()` y CHECK):
  - `ClassSessionSuspensionReason { Weather, Holiday, NonWorkingDay, InstructorAbsence, InstructorIllness, BuildingIssue, Maintenance, Other }` (spec: CLIMA, FERIADO, DIA_NO_LABORABLE, AUSENCIA_INSTRUCTOR, ENFERMEDAD_INSTRUCTOR, PROBLEMA_EDILICIO, MANTENIMIENTO, OTRO).
  - `ClassSessionCancellationReason { AdministrativeDecision, LowDemand, DataEntryError, ScheduleEnded, Other }`. `ScheduleEnded` está reservado al sistema.
  - Si el motivo es `Other`, las observaciones son obligatorias (máximo 1000 caracteres).
- Columnas: `SuspendedAtUtc`, `SuspendedByUserId`, `SuspensionReason`, `SuspensionObservations`, `CancelledAtUtc`, `CancelledByUserId`, `CancellationReason`, `CancellationObservations`. Los responsables apuntan a `Users`, como `AuditLog.UserId`.
- **Serialización con las reservas**: suspender, cancelar y reprogramar ejecutan `LockClassSessionForUpdateAsync` dentro de una transacción antes de leer el estado. Así una reserva concurrente no puede colarse entre la transición y la cancelación de reservas.
- **Reprogramación**: `RescheduleAsync(id, startsAt, endsAt, roomId?, reason)` en una transacción:
  - Crea una sesión nueva **extraordinaria** (`RecurringScheduleId = NULL`) con la misma actividad, la sala indicada o la original, el staff programado igual al efectivo de la original, el mismo `Capacity` y `Notes`, estado `Scheduled` y `RescheduledFromSessionId = original`.
  - La original pasa a `Rescheduled` con `RescheduledToSessionId = nueva`. Conserva su `(RecurringScheduleId, StartsAtUtc)`, por lo que el generador no la recrea.
  - Validaciones: nuevo inicio futuro y solapamientos de sala e instructor.
  - Se permite reprogramar una sesión que ya es fruto de una reprogramación (cadena). Si la sesión destino se cancela después, la original queda en `Rescheduled`; la cadena se consulta desde el detalle.

### Decision 11: Eventos de dominio in-process (sin MediatR)
- **Infraestructura mínima** en `SmartGym.Domain.Common`:
  - `IDomainEvent` (marker).
  - `IHasDomainEvents { IReadOnlyCollection<IDomainEvent> DomainEvents; void ClearDomainEvents(); }`.
- En Application, `IDomainEventHandler<TEvent>`.
- `SmartGymDbContext.SaveChangesAsync`:
  1. Recolecta los eventos de las entidades rastreadas y los limpia.
  2. Resuelve los handlers desde `IServiceProvider` y los ejecuta en orden. Los handlers modifican el mismo `DbContext` y **no** llaman a `SaveChanges`.
  3. Repite mientras aparezcan eventos nuevos (máximo 5 rondas; si se excede, lanza).
  4. Llama a `base.SaveChangesAsync`.

  Todo ocurre en la misma transacción: si un handler falla, no se persiste nada.
- **Eventos** (solo los que tienen consumidores):
  - `ClassSessionSuspended(SessionId, Reason, Observations, ByUserId, AtUtc)`
  - `ClassSessionCancelled(SessionId, Reason, Observations, ByUserId?, AtUtc)`
  - `ClassSessionRescheduled(OriginalSessionId, NewSessionId, NewStartsAtUtc, NewEndsAtUtc, ByUserId)`
  - `ClassSessionRoomChanged(SessionId, PreviousRoomId, NewRoomId, ByUserId)`
- **Handlers**:
  - `Reservations/CancelReservationsOnSessionVoided` (Suspended, Cancelled, Rescheduled):
    - Cancela las reservas `Reserved`, `Confirmed` y `WaitList` con `isLateCancellation = false`.
    - Las reservas con membresía reciben `RefundCredit`. Las cubiertas por ticket individual quedan canceladas y marcadas para compensación administrativa mediante el `AddCompensatoryCreditAsync` existente.
    - Ajusta `ReservedCount`.
  - `Operations/NotifyStudentsOnSessionChange` (los cuatro eventos): crea `NotificationMessage` in-app para los alumnos con reservas activas. Para ello generaliza `NotifyClassSuspendedAsync` y muestra las horas locales.
- La auditoría (`IAuditService`) se registra explícitamente en `ClassSessionService` con estas acciones: `CLASS_SESSION_CREATED`, `_UPDATED`, `_OPENED`, `_SUSPENDED`, `_CANCELLED`, `_RESCHEDULED`, `_ROOM_CHANGED`, `_INSTRUCTOR_REPLACED`, `_SECRETARY_REPLACED`. No se audita la generación masiva fila por fila: se registra un único `CLASS_SESSIONS_GENERATED` con los conteos.
- **Alternativa descartada**: MediatR, por la licencia comercial desde la v13 y porque sería una dependencia nueva para cuatro eventos. Outbox: innecesario mientras todos los consumidores sean in-process; si en el futuro hay consumidores externos (email, WhatsApp), se agregará sobre `IDomainEvent`.

### Decision 12: Migración de datos existentes (`ClassSessionsRefactor`)
Orden dentro de la migración, con la zona horaria `America/Argentina/San_Juan` fija en el SQL y documentada:
1. `CREATE EXTENSION IF NOT EXISTS btree_gist;`
2. Agregar `StartsAtUtc`/`EndsAtUtc` como columnas nullable y poblarlas:
   - `StartsAtUtc = ("Date" + "StartTime") AT TIME ZONE 'America/Argentina/San_Juan'`.
   - `EndsAtUtc` se calcula de la misma forma, sumando un día si `EndTime <= StartTime`.
   - Después pasan a NOT NULL.
3. Staff: `ScheduledInstructorId = COALESCE("OriginalInstructorId", "InstructorId")`, `EffectiveInstructorId = "InstructorId"`. Por cada fila con sustituto, insertar un `ClassSessionStaffChanges` (`Reason = 'Migrado desde SubstituteInstructorId'`, `ChangedByUserId = NULL`, ya que no existe un usuario semilla, `ChangedAtUtc = COALESCE("UpdatedAtUtc", "CreatedAtUtc")`).
4. `MaxCapacity` → `Capacity` (rename).
5. Estados `1`, `2` y `3` → `6` (`Open`), porque hoy todas esas sesiones admiten reservas. Las pasadas aparecerán como FINALIZADA por derivación. Los estados `4` y `5` se conservan.
6. Motivos: si el estado es `4`, `SuspensionReason = 'Other'` y `SuspensionObservations = "CancellationReason"`. Si es `5`, `CancellationReason = 'Other'` y `CancellationObservations` toma el texto anterior.
7. Agregar `RecurringSchedules.SecretaryId` (nullable, FK Restrict), `IsDetachedFromSchedule` (default `false`) y las columnas de suspensión, cancelación y reprogramación. Cambiar la FK `RecurringScheduleId` a `Restrict`.
8. **Verificación previa** de solapamientos: un bloque `DO $$ ... RAISE EXCEPTION` lista los pares de sesiones activas que se solapan en la misma sala o con el mismo instructor y aborta con un mensaje claro para resolverlos manualmente.
9. Crear el índice único parcial, las exclusiones, los CHECK y los índices operacionales. Eliminar las columnas y los índices legacy (`Date`, `StartTime`, `EndTime`, `InstructorId`, `OriginalInstructorId`, `SubstituteInstructorId`, `(RecurringScheduleId, Date)`).
10. `Down` funcional: la conversión inversa pierde las observaciones tipificadas y el historial de staff (se documenta).

---

## Data Model

```sql
-- "ClassSessions" (refactor)
"Id" UUID PK,
"RecurringScheduleId" UUID NULL FK "RecurringSchedules" ON DELETE RESTRICT,
"ActivityId" UUID NOT NULL FK "Activities" RESTRICT,
"RoomId" UUID NOT NULL FK "Rooms" RESTRICT,
"StartsAtUtc" TIMESTAMPTZ NOT NULL,
"EndsAtUtc" TIMESTAMPTZ NOT NULL,
"Capacity" INT NOT NULL,
"ReservedCount" INT NOT NULL DEFAULT 0,
"AttendedCount" INT NOT NULL DEFAULT 0,
"Status" INT NOT NULL,                         -- 1 Scheduled, 4 Suspended, 5 Cancelled, 6 Open, 7 Rescheduled
"ScheduledInstructorId" UUID NOT NULL FK "People" RESTRICT,
"EffectiveInstructorId" UUID NOT NULL FK "People" RESTRICT,
"ScheduledSecretaryId" UUID NULL FK "People" RESTRICT,
"EffectiveSecretaryId" UUID NULL FK "People" RESTRICT,
"IsDetachedFromSchedule" BOOLEAN NOT NULL DEFAULT FALSE,
"Notes" VARCHAR(2000) NULL,
"SuspendedAtUtc" TIMESTAMPTZ NULL, "SuspendedByUserId" UUID NULL FK "Users",
"SuspensionReason" VARCHAR(30) NULL, "SuspensionObservations" VARCHAR(1000) NULL,
"CancelledAtUtc" TIMESTAMPTZ NULL, "CancelledByUserId" UUID NULL FK "Users",
"CancellationReason" VARCHAR(30) NULL, "CancellationObservations" VARCHAR(1000) NULL,
"RescheduledFromSessionId" UUID NULL FK "ClassSessions" RESTRICT,
"RescheduledToSessionId" UUID NULL FK "ClassSessions" RESTRICT,
"CreatedAtUtc", "UpdatedAtUtc", "IsActive"     -- BaseEntity
-- xmin como token de concurrencia (Version, IsRowVersion)

CHECK "CK_ClassSessions_Dates"        ("EndsAtUtc" > "StartsAtUtc" AND "EndsAtUtc" - "StartsAtUtc" <= INTERVAL '24 hours')
CHECK "CK_ClassSessions_Capacity"     ("Capacity" > 0 AND "ReservedCount" >= 0 AND "AttendedCount" >= 0)
CHECK "CK_ClassSessions_Status"       ("Status" IN (1, 4, 5, 6, 7))
CHECK "CK_ClassSessions_Suspension"   (("Status" = 4) = ("SuspendedAtUtc" IS NOT NULL AND "SuspensionReason" IS NOT NULL))
CHECK "CK_ClassSessions_Cancellation" (("Status" = 5) = ("CancelledAtUtc" IS NOT NULL AND "CancellationReason" IS NOT NULL))
CHECK "CK_ClassSessions_Rescheduled"  (("Status" = 7) = ("RescheduledToSessionId" IS NOT NULL))

UNIQUE INDEX "UX_ClassSessions_Schedule_StartsAt" ("RecurringScheduleId", "StartsAtUtc") WHERE "RecurringScheduleId" IS NOT NULL
INDEX ("StartsAtUtc"), ("Status", "StartsAtUtc"), ("ActivityId", "StartsAtUtc"), ("RoomId", "StartsAtUtc"),
      ("EffectiveInstructorId", "StartsAtUtc"), ("ScheduledInstructorId", "StartsAtUtc")
EXCLUDE "EX_ClassSessions_Room_NoOverlap", "EX_ClassSessions_Instructor_NoOverlap"   -- Decision 9

-- "ClassSessionStaffChanges" (nueva) — Decision 8
-- "RecurringSchedules" + "SecretaryId" UUID NULL FK "People" RESTRICT
```

---

## API (`/api/class-sessions`)

| Método y ruta | Política | Request | Notas |
|---|---|---|---|
| `GET /` | TeachingAndStaff | `from`, `to` (`DateOnly` local), `activityId`, `roomId`, `instructorId`, `secretaryId`, `status` (incluye derivados), `page`, `pageSize` | `PagedResult<ClassSessionListItemDto>`, orden cronológico. Rango máximo de 92 días |
| `GET /{id}` | TeachingAndStaff | — | `ClassSessionDetailDto` con staff programado/efectivo, historial de staff, cadena de reprogramación y `Version` |
| `GET /available` | AllAuthenticated | `from`, `to`, `activityId` | Solo `Open`, `StartsAtUtc > now`, actividad `Active`. `ClassSessionAvailabilityDto` con `AvailableSpots` e `IsFull` |
| `POST /` | Staff | `CreateClassSessionRequest(ActivityId, RoomId, InstructorId, SecretaryId?, StartsAt, EndsAt, Capacity?, Notes?)` | Clase extraordinaria. Responde `201` con `Location` |
| `PUT /{id}` | Staff | `UpdateClassSessionRequest(Capacity, Notes, Version)` | El cupo debe ser ≥ `ReservedCount` y la sesión no estar iniciada. Las notas se pueden editar siempre. No cambia horarios (para eso está `reschedule`) |
| `POST /{id}/open` | Staff | — | |
| `POST /{id}/suspend` | TeachingAndStaff | `SuspendClassSessionRequest(Reason, Observations?)` | Un instructor solo puede suspender sus propias sesiones (programado o efectivo) |
| `POST /{id}/cancel` | Staff | `CancelClassSessionRequest(Reason, Observations?)` | `ScheduleEnded` no se acepta desde la API |
| `POST /{id}/reschedule` | Staff | `RescheduleClassSessionRequest(StartsAt, EndsAt, RoomId?, Reason)` | Responde con el detalle de la nueva sesión |
| `POST /{id}/replace-instructor` | Staff | `ReplaceStaffRequest(PersonId, Reason, Version)` | |
| `POST /{id}/replace-secretary` | Staff | `ReplaceStaffRequest(PersonId?, Reason, Version)` | |
| `POST /{id}/change-room` | Staff | `ChangeRoomRequest(RoomId, Version)` | |
| `POST /generate` | Staff | `GenerateSessionsRequest(From, To, RecurringScheduleId?)` | Rango máximo de 92 días. Devuelve `GenerationResultDto` |

Se eliminan `POST /manual`, `/start`, `/finish` y `/substitute`.

**Autorización contextual**: un usuario con rol Instructor y sin rol de Staff solo ve y suspende sesiones donde es instructor programado o efectivo. Las demás responden 404 (no se revela su existencia). Los alumnos solo acceden a `/available`; sus clases reservadas se consultan desde `Reservations`.

**Errores**: validación → 400. Inexistente → 404. Transición inválida, sesión cerrada, solapamiento (`Details.conflictingSessionIds`), versión desactualizada (`DbUpdateConcurrencyException`) o `23P01` → 409 con `ConflictException`.

**Versión**: requerida en `PUT`, `replace-*` y `change-room`, porque son ediciones sobre datos que el operador vio. Las transiciones de estado no la exigen: se serializan con el bloqueo de fila y validan el estado actual. Cualquier escritura de reservas incrementa `xmin`, por lo que el cliente debe recargar ante un 409.

---

## Impacto en módulos existentes

- **Reservations**:
  - Reserva solo si `Status == Open` y `now < StartsAtUtc`.
  - La ventana de cancelación tardía se calcula como `StartsAtUtc - now` (corrige el bug de zona horaria).
  - La validación de membresía por fecha usa la fecha local de `StartsAtUtc`.
  - Se agrega el handler de la Decision 11.
- **Memberships / CreditLedger**: `DeductCredit(..., date)` y las descripciones usan la fecha local derivada.
- **Operations / Dashboard**: "clases de hoy" pasa a ser el rango UTC del día local (`LocalDayRange`).
- **Operations / Notifications**: textos con fecha y hora locales; se agrega el handler de la Decision 11.
- **Activities / RecurringScheduleService**: soporte de `SecretaryId` e invocación de `ClassSessionScheduleSync`. La respuesta de edición incluye `DivergentSessionIds`.
- **Activities / ActivityService**: la guarda de inactivación (Decision 7 de `activities-catalog`) pasa de "fecha ≥ hoy en `Scheduled`/`InProgress`" a "`EndsAtUtc > now` en `Scheduled`/`Open`".

---

## Risks / Trade-offs

- **Las exclusiones pueden abortar la migración si ya hay solapamientos** → la verificación previa (Decision 12, paso 8) aborta con un listado accionable. Se debe probar contra una copia de los datos antes de desplegar.
- **Los handlers in-process alargan la transacción de suspensión** (una clase con N reservas genera N devoluciones y N notificaciones) → N está acotado por el cupo de la clase (decenas), por lo que es aceptable.
- **`xmin` cambia con cada reserva** → las ediciones administrativas pueden recibir 409 en clases muy concurridas. Es el comportamiento correcto (el cupo visto estaba desactualizado), y la UI debe recargar y reintentar.
- **El worker corre en cada instancia del host** → la idempotencia la garantizan la verificación en la aplicación y el índice único con reintento. Si se escala a varias instancias, se puede agregar `pg_try_advisory_lock` sin cambiar el contrato.
- **Zona horaria fija en el SQL de migración** → si el gimnasio usa otra zona, hay que ajustar la constante antes de aplicar la migración (se documenta en el PR).
- **Cambios de horario con sesiones divergentes** → no se propagan automáticamente. Se informan en la respuesta para la gestión manual; resolverlos automáticamente podría afectar reservas.
