> **Disciplina TDD**: cada tarea marcada con 🔴 empieza escribiendo los tests indicados y verificando que fallan por el motivo esperado. Recién entonces se escribe el código mínimo para ponerlos en verde, y después se refactoriza. Los tests viven en `backend/tests/SmartGym.Domain.UnitTests`. EF InMemory no aplica índices únicos, CHECK, exclusiones, `xmin` ni transacciones, así que esas garantías se verifican con tests de configuración del modelo y de migración (como `ActivityModelConfigurationTests` y `ActivitiesCatalogMigrationTests`) y con chequeos explícitos en los servicios. Los tiempos se controlan con un `TimeProvider` falso.

## 0. Prerrequisitos

- [x] 0.1 Cerrar la tarea 2.4 de `activities-catalog` y archivar ese change (`/opsx-archive`), para que `activities-schedule` y `reservations-attendance` contengan el texto del que parten los deltas de este change.

## 1. Fundamentos transversales

- [x] 1.1 🔴 `Common/GymCalendarTests.cs`: `ToUtc`/`ToLocal` para `America/Argentina/San_Juan`; `Today` según la zona del gimnasio cerca de la medianoche UTC; `LocalDayRange`; hora inexistente o ambigua con una zona con DST (p. ej. `America/Santiago`). → `GymOptions` (sección `Gym`, `ValidateOnStart`), `IGymCalendar`/`GymCalendar` sobre `TimeProvider`, registro de `TimeProvider.System` y sección `Gym` en `appsettings.json`.
- [x] 1.2 🔴 `Infrastructure/DomainEventDispatchTests.cs`:
  - Los eventos de entidades rastreadas se despachan antes de persistir.
  - Los cambios hechos por handlers se guardan en el mismo `SaveChanges`.
  - Los eventos en cascada se procesan.
  - Una excepción en un handler impide persistir.
  - Más de 5 rondas lanza.
  - Los eventos se limpian tras despacharse.

  → `IDomainEvent`, `IHasDomainEvents` (Domain), `IDomainEventHandler<T>` (Application) y override de `SmartGymDbContext.SaveChangesAsync`.

## 2. Dominio

- [x] 2.1 🔴 `Activities/ClassSessionTests.cs` (reescritura), sobre la creación:
  - Creación con `EndsAtUtc > StartsAtUtc` y duración ≤ 24 h.
  - El efectivo se inicializa igual al programado.
  - `Capacity > 0`.

  → Refactorizar `ClassSession` según `design.md` (setters privados, factoría `CreateFromSchedule`/`CreateExtraordinary`, `Version` con `xmin`, `IHasDomainEvents`) y `ClassSessionStatus` con los valores 1, 4, 5, 6 y 7.
- [x] 2.2 🔴 Tests de transiciones: tabla de la Decision 3 (`Open`, `Suspend`, `Cancel`, `MarkRescheduled`), con sus condiciones temporales y el rechazo desde los estados terminales. Motivo `Other` sin observaciones → rechazo. `Suspend`/`Cancel`/`MarkRescheduled` registran su evento y sus datos (fecha, responsable, motivo). → Implementar las transiciones y los enums `ClassSessionSuspensionReason`/`ClassSessionCancellationReason`.
- [x] 2.3 🔴 Tests de estados derivados: precedencia terminal → `Finished` → `InProgress` → `Full` → persistido; `Scheduled` vencida → `Finished`; `AvailableSpots` nunca negativo. → `GetDisplayStatus(nowUtc)` y `ClassSessionDisplayStatus`.
- [x] 2.4 🔴 Tests de staff y sala:
  - `ReplaceInstructor`/`ReplaceSecretary` conservan el programado y generan un `ClassSessionStaffChange`.
  - Rechazan un valor igual al efectivo y las sesiones cerradas, y permiten el reemplazo durante el curso de la clase.
  - `ChangeRoom` emite `ClassSessionRoomChanged`.
  - Toda operación manual marca `IsDetachedFromSchedule`.

  → Implementar los métodos y la entidad `ClassSessionStaffChange`.
- [x] 2.5 🔴 Tests de inmutabilidad y cupo: sobre una sesión cerrada solo `UpdateNotes` es válido; `UpdateCapacity` rechaza valores menores que `ReservedCount` y sesiones iniciadas. → Implementar las guardas.
- [x] 2.6 🔴 `Activities/RecurringScheduleTests.cs`: `SecretaryId` opcional. → Agregar la propiedad a `RecurringSchedule`.
- [x] 2.7 Corregir la compilación del resto de la solución (servicios, DTOs, controllers y tests que referencian `Date`, `StartTime`, `MaxCapacity`, `InstructorId`, `InProgress` y `Finished`) sin agregar comportamiento nuevo. `dotnet build` en verde.

## 3. Persistencia

- [x] 3.1 🔴 `Activities/ClassSessionModelConfigurationTests.cs`:
  - Columnas y longitudes.
  - Los CHECK de `design.md`.
  - El índice único parcial `UX_ClassSessions_Schedule_StartsAt`.
  - Los índices operacionales.
  - `Version` como row version.
  - Los enums de motivo como string.
  - FKs `Restrict` (incluidas `RecurringScheduleId`, las de reprogramación y `ClassSessionStaffChanges`).
  - `RecurringSchedules.SecretaryId`.

  → Reescribir `ClassSessionConfiguration`, crear `ClassSessionStaffChangeConfiguration`, ajustar `RecurringScheduleConfiguration` y registrar `DbSet<ClassSessionStaffChange>` en `SmartGymDbContext` e `ISmartGymDbContext`.
- [x] 3.2 🔴 `Activities/ClassSessionsRefactorMigrationTests.cs`, que verifica que la migración contiene, en orden: la extensión `btree_gist`, la conversión `AT TIME ZONE`, el mapeo del staff y la inserción del historial, el remapeo de estados `1/2/3 → 6`, la conversión de motivos, la verificación previa de solapamientos con `RAISE EXCEPTION`, las exclusiones `EX_ClassSessions_*`, la eliminación de las columnas legacy y un `Down` no vacío. → Generar la migración `ClassSessionsRefactor` y completarla a mano según la Decision 12.
- [x] 3.3 Verificar la migración contra un PostgreSQL local con datos de muestra: sesiones con sustituto, suspendidas, canceladas, `InProgress`/`Finished` y un par solapado para comprobar el aborto. `dotnet ef migrations has-pending-model-changes` debe reportar que no hay cambios. Documentar el resultado en el PR.

## 4. Aplicación — generación y sincronización

- [x] 4.1 🔴 `Activities/ClassSessionGeneratorTests.cs`:
  - Ventana de 60 días.
  - Conversión local → UTC.
  - Staff y secretario copiados del horario.
  - Cupo `schedule ?? activity` y `NO_CAPACITY`.
  - Omisión de existentes en cualquier estado (incluida una cancelada).
  - Conflictos `ROOM_OVERLAP`/`INSTRUCTOR_OVERLAP` sin abortar.
  - Se ignoran horarios inactivos, actividades no `Active` y fechas fuera de vigencia.
  - Segunda ejecución sin cambios.
  - Reintento ante una `DbUpdateException` simulada de unicidad.

  → `ClassSessionGenerator` y `GenerationResultDto`.
- [x] 4.2 🔴 `Activities/ClassSessionScheduleSyncTests.cs`:
  - Cambio de hora, sala, instructor y cupo → actualización en el lugar de las candidatas.
  - Cambio de día o cierre de vigencia → cancelación `ScheduleEnded`.
  - Sesiones abiertas, con reservas o separadas del horario → intactas y devueltas como divergentes.
  - Colisión con un `(schedule, starts)` existente → la candidata se cancela.
  - Generación de las ocurrencias nuevas.

  → `ClassSessionScheduleSync`; integración en `RecurringScheduleService` (misma transacción, `SecretaryId`, `DivergentSessionIds` en la respuesta) y ajuste de `RecurringScheduleDtos`.
- [x] 4.3 🔴 Tests de la apertura automática: abre las `Scheduled` dentro de `AutoOpenLeadTime`, no abre las vencidas ni las terminales y `null` la desactiva. → `ClassSessionGenerator.AutoOpenAsync`, opciones `ClassSessionsOptions` (`WindowDays`, `GenerationInterval`, `AutoOpenLeadTime`) y la sección en `appsettings.json`.
- [x] 4.4 🔴 Test del worker con un generador simulado: ejecuta al iniciar y en cada intervalo, y una excepción se registra sin detener el loop. → `ClassSessionGenerationWorker : BackgroundService` en Infrastructure, registrado con `AddHostedService`.

## 5. Aplicación — comandos y consultas

- [x] 5.1 Definir los DTOs y requests de `design.md` en `Modules/Activities/Dtos/ClassSessionDtos.cs` (reemplazando los actuales), con `StartsAtUtc`, `StartsAtLocal`/`EndsAtLocal`, `DisplayStatus`, `AvailableSpots` y `Version`.
- [x] 5.2 🔴 `ClassSessionValidatorTests`: inicio/fin futuros y coherentes, cupo positivo, motivo `Other` con observaciones, longitudes (notas 2000, observaciones 1000, motivo de reemplazo 500), rango de consulta ≤ 92 días y `ScheduleEnded` rechazado en la API. → Validadores FluentValidation.
- [x] 5.3 🔴 `ClassSessionServiceTests` (alta y edición):
  - Clase extraordinaria (actividad no `Active` → rechazo; cupo resuelto o 400; solapamiento → `ConflictException` con ids).
  - `UpdateAsync` (cupo y notas, versión desactualizada → 409).
  - Auditoría `CLASS_SESSION_CREATED`/`_UPDATED`.

  → Reescribir `IClassSessionService`/`ClassSessionService`.
- [x] 5.4 🔴 Tests del ciclo de vida en el servicio:
  - `Open`, `Suspend`, `Cancel` y `Reschedule` invocan `LockClassSessionForUpdateAsync` dentro de una transacción (spy, como `ReservationServiceLockingTests`).
  - Transición inválida → 409.
  - La reprogramación crea una sesión extraordinaria vinculada con el staff efectivo y valida solapamientos.
  - Instructor ajeno que suspende → 404.
  - Auditoría por operación.

  → Implementar los comandos.
- [x] 5.5 🔴 Tests de staff y sala en el servicio: validación de roles (Instructor; Secretary o Administrator), solapamiento del instructor efectivo, versión, historial persistido y auditoría `_INSTRUCTOR_REPLACED`/`_SECRETARY_REPLACED`/`_ROOM_CHANGED`. → Implementar `ReplaceInstructorAsync`, `ReplaceSecretaryAsync` y `ChangeRoomAsync`.
- [x] 5.6 🔴 Tests de consultas:
  - Filtros (rango local, actividad, sala, instructor programado o efectivo, secretario, estado persistido o derivado), paginación y orden.
  - Un instructor solo ve sus sesiones.
  - Detalle con historial y cadena de reprogramación.
  - `/available`: solo `Open`, futuras y de actividad `Active`, con `AvailableSpots`/`IsFull`.

  → `GetPagedAsync`, `GetByIdAsync`, `GetAvailableAsync`.
- [x] 5.7 🔴 Test de traducción de errores de PostgreSQL: `PostgresException` `23P01` → `ConflictException`; `DbUpdateConcurrencyException` → `ConflictException`. → Helper compartido en `ClassSessionService`.

## 6. Integración con otros módulos

- [x] 6.1 🟢 `Reservations/ReservationSessionStateTests.cs`:
  - Reserva en `Scheduled`, en un estado terminal o en una clase ya iniciada → rechazo.
  - En `Open` → OK.
  - Ventana de cancelación tardía calculada sobre `StartsAtUtc` (caso que hoy falla por la zona horaria).
  - Membresía validada con la fecha local.

  → Ajustar `ReservationService`.
- [x] 6.2 🟢 `Reservations/CancelReservationsOnSessionVoidedTests.cs`:
  - Suspensión, cancelación y reprogramación cancelan `Reserved`/`Confirmed`/`WaitList` sin marcarlas como tardías.
  - Las reservas por membresía reciben `RefundCredit`.
  - Las de ticket individual quedan marcadas para compensación.
  - `ReservedCount` queda en 0.
  - Todo en el mismo `SaveChanges`.

  → Handler en `Modules/Reservations`.
- [x] 6.3 🟢 `Operations/NotifyStudentsOnSessionChangeTests.cs`: una notificación in-app por alumno con reserva activa, para cada uno de los cuatro eventos, con fecha y hora locales en el texto. → Handler en `Modules/Operations` y generalización de `NotifyClassSuspendedAsync`.
- [x] 6.4 🟢 Ajustar los tests de `DashboardService` ("clases de hoy" por rango local), de `CreditLedgerService` (fecha local en la descripción) y de la guarda de inactivación de `ActivityService` (`EndsAtUtc > now` en `Scheduled`/`Open`). → Adaptar los servicios.

## 7. WebApi

- [x] 7.1 🟢 `ClassSessionsControllerTests` (reescritura):
  - Políticas por endpoint según la tabla de `design.md`.
  - `201 Created` con `Location`.
  - Mapeo 400/404/409 con `Details`.
  - Rutas `/start`, `/finish`, `/manual` y `/substitute` eliminadas.

  → Reescribir `ClassSessionsController`.
- [x] 7.2 🟢 Registrar en DI los handlers de eventos, el generador, la sincronización, `IGymCalendar` y el worker. Verificar el arranque de la aplicación con `dotnet run` y probar manualmente en Swagger: generar, abrir, reservar, suspender (con reservas que se cancelan y créditos devueltos) y reprogramar.

## 8. Verificación final

- [x] 8.1 🟢 `dotnet test` en verde para toda la solución (486/486 tests pasando) y `dotnet build` sin warnings nuevos.
- [x] 8.2 🟢 Revisar que cada escenario de `specs/class-sessions`, `specs/activities-schedule` y `specs/reservations-attendance` esté cubierto por al menos un test, y anotar en el PR los que solo se verifican manualmente o contra PostgreSQL (exclusiones, `xmin`, migración).
- [x] 8.3 🟢 Documentar en el PR el contrato nuevo de `/api/class-sessions` para el frontend (campos y rutas eliminados y agregados).

## 9. Correcciones posteriores a la revisión de la spec

- [x] 9.1 🔴 `UpdateAsync` sobre una clase cerrada con el mismo cupo guarda las notas (escenario "Notas sobre una clase finalizada"); con un cupo distinto sigue rechazando. → El servicio solo invoca `UpdateCapacity` si el cupo cambia.
- [x] 9.2 🔴 Reprogramar a un horario que se superpone con el de la propia clase original no genera conflicto, y la reprogramación difiere las exclusiones dentro de su transacción. → `EnsureNoConflictsAsync` excluye la original; `DeferClassSessionOverlapConstraintsAsync` + `EnforceDeferredConstraintsAsync` (traducido a 409); migración `ClassSessionOverlapConstraintsDeferrable` (exclusiones `DEFERRABLE INITIALLY IMMEDIATE`).
- [x] 9.3 🔴 Las notificaciones solo llegan a alumnos con reserva `Reserved`/`Confirmed`/`WaitList`, también cuando el handler de cancelación corre primero. → Filtro por estado en `NotifyStudentsOnSessionChangeHandler`.
- [x] 9.4 🔴 Las reservas por ticket individual se cancelan sin movimiento automático y quedan identificadas (`MembershipId` y `RefundCreditMovementId` nulos) para compensación con `AddCompensatoryCreditAsync`. → Se quita la compensación automática del handler.
- [ ] 9.5 Aplicar `ClassSessionOverlapConstraintsDeferrable` en el PostgreSQL local y reprogramar manualmente una clase a un horario superpuesto con el original (verifica `SET CONSTRAINTS` contra la base real).
