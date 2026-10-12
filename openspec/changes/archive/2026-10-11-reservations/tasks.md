Las rutas son relativas a `backend/src/` salvo indicación. Las tareas marcadas 🔴 escriben primero la prueba que falla (TDD); la tarea siguiente la pone en verde. "Criterio N" refiere a la tabla de criterios de aceptación de `design.md`.

## 1. Dominio: enums, entidad y máquina de estados

- [x] 1.1 🔴 Ampliar `backend/tests/SmartGym.Domain.UnitTests/Reservations/ReservationTests.cs` con las transiciones válidas e inválidas de la tabla de Decision 4, el registro de un `ReservationStatusChange` por transición, los estados terminales y los eventos emitidos.
- [x] 1.2 Actualizar `SmartGym.Domain/Enums/ReservationStatus.cs` (`Confirmed = 1`, `Cancelled = 3`, `Attended = 4`, `NoShow = 5`, `Waitlisted = 6`, `Pending = 7`) y crear `ReservationOrigin`, `ReservationAccessType`, `ReservationCancellationSource`, `ReservationChangeTrigger` y `WaitlistPromotionMode` en `SmartGym.Domain/Enums/`.
- [x] 1.3 Agregar a `SmartGym.Domain/Enums/NotificationType.cs` los valores 9 a 14 de Decision 11.
- [x] 1.4 Crear `SmartGym.Domain/Entities/Reservations/ReservationStatusChange.cs` y refactorizar `Reservation.cs` (`IHasDomainEvents`, atributos de Decision 3, métodos `Confirm`, `OfferSpot`, `AcceptOffer`, `Cancel`, `MarkAttended`, `MarkNoShow` con `nowUtc` por parámetro).
- [x] 1.5 Crear `SmartGym.Domain/Common/IPostCommitDomainEvent.cs` y `SmartGym.Domain/Events/Reservations/ReservationEvents.cs` con los seis eventos de Decision 11.
- [x] 1.6 Reemplazar los usos de `ReservationStatus.Reserved` y `WaitList` en `ReservationService`, los tres handlers de `Modules/Reservations/Handlers`, `NotifyStudentsOnSessionChangeHandler`, `NotificationService` y `DashboardService`, y dejar la suite existente en verde.

## 2. Persistencia y migración

- [x] 2.1 🔴 Crear `tests/.../Reservations/ReservationModelConfigurationTests.cs` (patrón `UseNpgsql` sin conexión) verificando columnas, FKs `Restrict`, restricciones `CHECK`, filtros de los cuatro índices únicos parciales, índices operacionales y `xmin`.
- [x] 2.2 Actualizar `SmartGym.Infrastructure/Persistence/Configurations/Reservations/ReservationConfiguration.cs`, crear `ReservationStatusChangeConfiguration.cs` y agregar `DbSet<ReservationStatusChange>` a `SmartGymDbContext` e `ISmartGymDbContext`.
- [x] 2.3 Agregar `ReservationId`, `ExpiresOn` y `ActivityId` a `MembershipCreditMovement` (con el índice `UX_CreditMovements_Reservation_Type`) y `ReservationId` a `PromotionRedemption`, con sus configuraciones.
- [x] 2.4 Generar la migración `RefactorReservationsModule` y completar a mano los pasos de datos del "Migration Plan" (fusión `2 → 1`, `Origin = Legacy`, `AccessType`, traslado del pase a `SingleClassTicketMovementId`, pases reutilizados, canceladas sin motivo, `ReservationId` en movimientos, fila inicial de historial) y el `Down`.
- [x] 2.5 Actualizar los datos de demostración (si el seed crea reservas) con los atributos nuevos.

## 3. Proyecto de integración con PostgreSQL

- [x] 3.1 Crear `backend/tests/SmartGym.IntegrationTests` (xUnit, `Testcontainers.PostgreSql`), agregarlo a `backend/SmartGym.slnx` y escribir el fixture que levanta el contenedor y aplica las migraciones.
- [x] 3.2 🔴→🟢 Test de migración: sembrar filas con el esquema anterior (estados 1, 3 y 6, con y sin membresía, un pase compartido por dos reservas), aplicar `RefactorReservationsModule` y verificar el resultado de cada paso de datos.
- [x] 3.3 Tests de los índices únicos parciales: reserva activa duplicada, clave de idempotencia, pase retenido (incluida la cancelación tardía y su liberación con excepción), canje promocional.

## 4. Despacho post-commit de eventos

- [x] 4.1 🔴 Ampliar `tests/.../Infrastructure/DomainEventDispatchTests.cs`: un `IPostCommitDomainEvent` no se despacha antes del guardado, se despacha tras el commit, se descarta tras un rollback, y un handler que lanza no propaga la excepción ni impide a los demás.
- [x] 4.2 Implementar la cola post-commit en `SmartGymDbContext`, el `DbTransactionInterceptor` y la ejecución de cada handler en un `IServiceScope` propio con `try/catch` + `ILogger` (Decision 11). Verificar que los eventos existentes no cambian de comportamiento.

## 5. Extensiones a créditos, promociones y elegibilidad

- [x] 5.1 🔴 Ampliar `tests/.../Memberships/CreditLedgerConsumptionTests.cs`: débito y devolución vinculados a `ReservationId`, devolución repetida devuelve el movimiento existente, cantidad 0 en membresía ilimitada, crédito compensatorio autónomo con `ExpiresOn` y alcance.
- [x] 5.2 Extender `ICreditLedgerService` / `CreditLedgerService` (`reservationId` en consumo y devolución, idempotencia por reserva, vencimiento y alcance en `AddCompensatoryCreditAsync`) y agregar `CompensatoryCreditValidityDays` a `MembershipsOptions`.
- [x] 5.3 🔴→🟢 Agregar a `IPromotionService` / `PromotionService` `EvaluatePromotionForReservationAsync` y `RedeemPromotionForReservationAsync` (sólo beneficios de acceso completo; canje vinculado a `ReservationId`), con pruebas en `tests/.../Promotions`.
- [x] 5.4 🔴→🟢 Agregar `EvaluateForSessionsAsync` a `IMembershipEligibilityService` (carga única de los derechos del alumno, evaluación en memoria por clase), con pruebas en `tests/.../Memberships`.

## 6. Creación de reservas y resolución del derecho de acceso

- [x] 6.1 🔴 Crear `tests/.../Reservations/ReservationCreationTests.cs`: validaciones de persona, clase y ventana de anticipación (con y sin `OverrideBookingWindow`), reserva duplicada, reversión ante fallo del ledger, registro de operador y beneficiario, auditoría (Criterios 1, 2, 3, 4, 6, 16).
- [x] 6.2 🔴 Crear `tests/.../Reservations/ReservationAccessResolutionTests.cs`: orden de resolución, selección explícita sin alternativa, pase con pago sin confirmar, crédito compensatorio vigente / vencido / fuera de alcance, promoción de acceso completo y parcial, membresía ilimitada.
- [x] 6.3 🔴 Crear `tests/.../Reservations/ReservationIdempotencyTests.cs`: reintento con la misma clave, clave usada para otra clase, reintento sobre una reserva ya cancelada (Criterio 19).
- [x] 6.4 Crear `SmartGym.Application/Common/ReservationsOptions.cs` y registrarla; definir los DTOs y requests nuevos en `Modules/Reservations/Dtos/ReservationDtos.cs` y los códigos de error en `ReservationErrorCodes`.
- [x] 6.5 Implementar `Modules/Reservations/Services/ReservationAccessResolver.cs` y reescribir `ReservationService.CreateReservationAsync` (bloqueos en orden, idempotencia antes de validar, resolución y consumo, `ReservationStatusChange`, auditoría con `IAuditService`, eventos).

## 7. Cancelación y lista de espera

- [x] 7.1 🔴 Crear `tests/.../Reservations/ReservationCancellationTests.cs`: anticipada, tardía, con excepción (permiso y motivo), después del inicio, motivo por defecto, restitución por modalidad, cancelación repetida (Criterios 8, 9, 10).
- [x] 7.2 🔴 Crear `tests/.../Reservations/ReservationWaitlistTests.cs`: ingreso sin consumo, lista deshabilitada y lista completa, compactación, promoción en orden, salto de candidato inelegible, umbral previo al inicio, modo oferta (aceptar, rechazar, aceptar ya inelegible, vencimiento acotado al inicio) (Criterios 11, 12, 13, 21).
- [x] 7.3 Implementar `CancelReservationAsync`, `JoinWaitlistAsync`, `LeaveWaitlistAsync`, `AcceptWaitlistOfferAsync`, `DeclineWaitlistOfferAsync`, `PromoteWaitlistAsync` y `CompactWaitlistPositions` en `ReservationService`.
- [x] 7.4 🔴→🟢 Implementar `ProcessWaitlistAsync` (ofertas vencidas y listas de clases iniciadas, idempotente) y `SmartGym.Infrastructure/Services/BackgroundJobs/ReservationWaitlistWorker.cs`, con pruebas en `tests/.../Reservations/ReservationWaitlistProcessTests.cs`.

## 8. Cambios de clase y de membresía

- [x] 8.1 🔴 Ampliar `tests/.../Reservations/CancelReservationsOnSessionVoidedTests.cs`: `AffectedBySessionChange`, `CancellationSource`, devolución idempotente, anulación del canje, liberación del crédito compensatorio, pase identificado para compensación, `RescheduledToSessionId`, sin reservas en la clase destino (Criterios 14, 15).
- [x] 8.2 Actualizar `CancelReservationsOnSessionVoidedHandler` (bloqueo de membresías por `StudentId`, métodos de dominio, auditoría).
- [x] 8.3 🔴 Crear `tests/.../Reservations/ReservationMembershipChangeTests.cs`: suspensión y cancelación de membresía cancelan con `MembershipChange`, liberan cupo y promueven la lista de espera; la suspensión devuelve crédito y la cancelación no (Criterio 22).
- [x] 8.4 Actualizar `CancelReservationsOnMembershipSuspendedHandler` y `CancelFutureReservationsOnMembershipCancelledHandler`, y reordenar los bloqueos de `MembershipService` en suspender y cancelar (clases de las reservas afectadas por `Id` ascendente, luego la membresía).
- [x] 8.5 🔴→🟢 Emitir `ClassSessionCapacityIncreased` desde `ClassSession.UpdateCapacity` y crear `PromoteWaitlistOnCapacityIncreasedHandler`, con pruebas.
- [x] 8.6 Traducir el error PostgreSQL `40P01` y `DbUpdateConcurrencyException` a `ConflictException` (`CONCURRENCY_CONFLICT`) en las operaciones de reserva.

## 9. Asistencia

- [x] 9.1 🔴 Crear `tests/.../Reservations/ReservationAttendanceTests.cs`: ventanas de presencia y de ausencia, clase anulada, estados terminales, instructor de clase ajena, `AttendedCount`, historial (Criterio 20).
- [x] 9.2 Actualizar `RecordAttendanceAsync` y `RecordNoShowAsync` (bloqueo de la clase, ventanas de `ReservationsOptions`, ámbito del instructor, auditoría).

## 10. Consultas

- [x] 10.1 🔴 Crear `tests/.../Reservations/ReservationQueriesTests.cs`: listado paginado con cada filtro, detalle con historial, historial del alumno, próximas reservas, lista de espera ordenada, `available-classes` (cupos, reserva propia, elegibilidad, motivos, sin movimientos de crédito, rango máximo, sin datos de otros alumnos) (Criterio 7).
- [x] 10.2 Implementar `GetPagedAsync`, `GetByIdAsync`, `GetByStudentAsync`, `GetMyUpcomingAsync`, `GetWaitlistAsync` y `GetAvailableClassesAsync` con proyecciones en SQL.

## 11. Notificaciones

- [x] 11.1 🔴 Crear `tests/.../Operations/NotifyStudentOnReservationLifecycleTests.cs`: un aviso por evento, sin aviso para cancelaciones por cambio de clase, y una falla del handler no revierte la reserva ni se propaga (Criterio 18).
- [x] 11.2 Crear `SmartGym.Application/Modules/Operations/Handlers/NotifyStudentOnReservationLifecycleHandler.cs` y registrarlo.

## 12. API y autorización

- [x] 12.1 Agregar los seis permisos y sus políticas a `SmartGym.Application/Common/Security/RolesAndPolicies.cs` y registrarlas en `SmartGym.Infrastructure/DependencyInjection.cs` con los roles de la tabla de permisos de `design.md`.
- [x] 12.2 🔴 Crear `tests/.../Reservations/ReservationsControllerTests.cs`: códigos HTTP y contrato de error, `403` del alumno sobre reservas ajenas, `404` del instructor en clase ajena y `403` en historial de alumno, permiso y motivo en ambas excepciones, header `Idempotency-Key`, `201` frente a `200` en reintento (Criterio 17).
- [x] 12.3 Reescribir `SmartGym.WebApi/Controllers/ReservationsController.cs` con las rutas de la tabla de contratos de `design.md`, conservando el alias `/session/{classSessionId}`.

## 13. Concurrencia sobre PostgreSQL real (`SmartGym.IntegrationTests`)

- [x] 13.1 Carrera por el último cupo: N solicitudes simultáneas, exactamente una `CONFIRMED` (Criterio 5).
- [x] 13.2 Reserva duplicada concurrente del mismo alumno y reintento idempotente concurrente: un único registro y un único débito (Criterios 4, 19).
- [x] 13.3 Cancelaciones concurrentes de la misma reserva y de dos reservas de una clase con lista de espera: una sola devolución y ningún cupo asignado dos veces (Criterios 10, 12).
- [x] 13.4 Cancelación de reserva concurrente con suspensión de la membresía que la respalda: sin interbloqueo y con estado final consistente.

## 14. Frontend Angular

- [x] 14.1 Actualizar `frontend/src/app/core/models/gym.models.ts` (`ReservationStatus` con los valores 1, 3, 4, 5, 6, 7; `ReservationOrigin`, `ReservationAccessType`, `ReservationDetail`, `ReservationStatusChange`, `ReservableClassSession`, `WaitlistEntry`, contrato de error).
- [x] 14.2 Actualizar `frontend/src/app/core/services/gym-api.service.ts` con los métodos de las rutas nuevas y el envío de `Idempotency-Key` al crear.
- [x] 14.3 Actualizar `frontend/src/app/features/portal/student/student-portal.ts`: clases disponibles con elegibilidad y motivos, mis reservas, cancelación con aviso de penalización, lista de espera y respuesta a ofertas de cupo.
- [x] 14.4 Actualizar `frontend/src/app/features/portal/staff/staff-portal.ts`: listado filtrable, reserva en nombre de un alumno, cancelación con excepción y motivo, lista de espera por clase, filtro de reservas afectadas por cambio de clase.

## 15. Verificación integral

- [x] 15.1 Ejecutar `dotnet test` en `backend/` (unitarias e integración, con Docker activo) y confirmar los 22 criterios en verde.
- [x] 15.2 Ejecutar `npm run build` y `npm test` en `frontend/`.
- [ ] 15.3 Aplicar la migración sobre una copia de la base de desarrollo y revisar las reservas con `Notes` de pase compartido.
- [x] 15.4 Ejecutar `openspec validate reservations --strict`.
