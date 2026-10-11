# Design Document — Reservations (`reservations`)

## Context

Smart-Gym es un Monolito Modular en .NET 10 (ASP.NET Core Web API) con EF Core + PostgreSQL (Npgsql) y frontend Angular 22. La motivación del cambio está en [proposal.md](proposal.md) y los requisitos normativos en [specs/reservations/spec.md](specs/reservations/spec.md).

### Estado actual del código

1. **Máquina de estados ambigua.** `ReservationStatus` es `{ Reserved = 1, Confirmed = 2, Cancelled = 3, Attended = 4, NoShow = 5, WaitList = 6 }`. `ReservationService.CreateReservationAsync` siempre persiste `Reserved`; `Reservation.Confirm()` nunca se invoca.
2. **Sin trazabilidad.** `Reservation` no registra canal, modalidad de acceso, actores, ni historial de transiciones. `ReservationService` no audita ni emite eventos.
3. **Sin defensa en base de datos.** `ReservationConfiguration` sólo tiene un índice no único sobre `(ClassSessionId, StudentId)`. La búsqueda del pase individual toma el primer movimiento `SingleClassTicket` de la clase sin verificar si otra reserva ya lo usa.
4. **Brechas de autorización.** `ReservationsController.CancelReservation` no valida propiedad; `GetByStudentId` y `GetByClassSession` permiten a cualquier instructor ver datos ajenos.
5. **Handlers de membresía incompletos.** `CancelReservationsOnMembershipSuspendedHandler` y `CancelFutureReservationsOnMembershipCancelledHandler` decrementan `ReservedCount` sin bloquear la clase y sin promover la lista de espera.
6. **Despacho de eventos pre-commit.** `SmartGymDbContext.SaveChangesAsync` despacha los eventos de dominio antes de `base.SaveChangesAsync`, dentro de la transacción. Un handler que agrega un `NotificationMessage` lo persiste en el mismo guardado: si esa inserción falla, falla todo.
7. **No existe un módulo `Attendance`.** Los únicos datos de asistencia son columnas de `Reservation`.
8. **El ledger no referencia reservas.** `MembershipCreditMovement` no tiene `ReservationId`, ni vencimiento, ni alcance de actividad; la idempotencia de consumo se infiere contando débitos y devoluciones por `(MembershipId, ClassSessionId)`.
9. **Pruebas sólo en memoria.** El único proyecto de tests (`SmartGym.Domain.UnitTests`) usa EF InMemory y sobrescribe los métodos `Lock*ForUpdateAsync`; ningún test ejercita `FOR UPDATE` ni índices parciales reales.

---

## Goals / Non-Goals

**Goals:**
- Máquina de estados explícita con historial de sólo inserción, canal de origen, modalidad de acceso, idempotencia y concurrencia optimista.
- Cinco modalidades de acceso trazables: `MembershipCredit`, `UnlimitedMembership`, `CompensatoryCredit`, `SingleClassTicket`, `PromotionalBenefit`.
- Sin sobreventa ni duplicados en PostgreSQL: bloqueo pesimista en orden único + índices únicos parciales.
- `GET /api/reservations/available-classes` con elegibilidad evaluada en servidor sin consultas por clase.
- Políticas configurables de anticipación, cancelación y lista de espera (automática o por oferta).
- Autorización contextual, auditoría y notificaciones que no pueden revertir una reserva.

**Non-Goals:**
- Crear o administrar horarios o clases (`ClassSessions`).
- Modificar saldos fuera de `ICreditLedgerService`.
- Procesar cobros o reintegros monetarios (`Payments`). El reintegro de un pago de un pase ya usado en una reserva confirmada se resuelve operativamente: el personal cancela la reserva.
- Un motor de reglas promocionales propio (`Promotions`). Los descuentos parciales sobre clases pagas se aplican en la compra del pase, no aquí.
- Un módulo `Attendance` separado, la reversión de una asistencia ya registrada y la asistencia sin reserva previa. Este último punto es un requisito preexistente de `reservations-attendance` sin implementación; este cambio no lo implementa ni lo retira.
- Reubicar administrativamente una reserva en otra clase: el alumno o el personal crea una reserva nueva.

---

## Decisions

### Decision 1: Numeración de `ReservationStatus` sin renumerar valores persistidos
- **Adoptada**: `Confirmed = 1`, `Cancelled = 3`, `Attended = 4`, `NoShow = 5`, `Waitlisted = 6`, `Pending = 7`. El valor `2` queda retirado.
  - `Reserved (1)` se renombra a `Confirmed (1)` y el antiguo `Confirmed (2)` se fusiona en `1`. Como `Reservation.Confirm()` nunca se invocó, no se espera ninguna fila con `Status = 2`; la migración igualmente ejecuta `UPDATE ... SET "Status" = 1 WHERE "Status" = 2`.
  - `WaitList (6)` sólo cambia de nombre a `Waitlisted (6)`.
  - Conjuntos usados en índices y consultas: **activas** = `(1, 6, 7)`; **ocupan cupo** = `(1, 7)`.
- **Alternativa descartada**: `Pending = 0, Confirmed = 1, Waitlisted = 2`. Obliga a remapear `6→2` y `2→1` en la misma columna (un error de orden corrompe datos), deja a `Pending` como valor por defecto del enum —una reserva sin inicializar sería una oferta pendiente— y rompe la convención del dominio de enums que empiezan en 1.

### Decision 2: `PENDING` es exclusivamente una oferta de lista de espera
- **Adoptada**: una reserva sólo llega a `Pending` desde `Waitlisted`, en modo `OfferWithConfirmation`, con `WaitlistOfferExpiresAtUtc` obligatorio. Retiene cupo (`ReservedCount++`) y no consume el derecho de acceso.
  - No existe creación directa en `Pending` ni endpoint `POST /{id}/confirm`.
  - Un pase individual sin pago confirmado se rechaza con `409 PAYMENT_NOT_CONFIRMED`; no genera una reserva pendiente.
- **Alternativa descartada**: `Pending` genérico "a la espera de una condición". No tenía condición definida ni vencimiento, y retendría cupo indefinidamente.

### Decision 3: Modelo de dominio

`Reservation : BaseEntity, IHasDomainEvents`:

| Grupo | Atributos |
|---|---|
| Identidad | `Id`, `StudentId` (FK `People`), `ClassSessionId` (FK `ClassSessions`) |
| Estado | `Status`, `Origin` (`ReservationOrigin`), `AccessType` (`ReservationAccessType`) |
| Derecho de acceso | `MembershipId`, `CreditMovementId` (débito), `RefundCreditMovementId` (devolución), `SingleClassTicketMovementId` (pase individual **o** crédito compensatorio autónomo), `PaymentId`, `PromotionRedemptionId`, `RequestedPromotionCode` (`string?`, máx. 50) |
| Creación | `ReservedAtUtc`, `ConfirmedAtUtc`, `CreatedByUserId` |
| Lista de espera | `WaitListPosition`, `WaitlistEnteredAtUtc`, `WaitlistPromotedAtUtc`, `WaitlistOfferExpiresAtUtc` |
| Cancelación | `CancelledAtUtc`, `CancelledByUserId`, `CancellationReason` (máx. 500), `CancellationSource`, `IsLateCancellation`, `IsPolicyOverride`, `PolicyOverrideReason` (máx. 500), `AffectedBySessionChange`, `RescheduledToSessionId` |
| Asistencia | `AttendedAtUtc`, `CheckedInByUserId`, `AttendanceSource` |
| Técnicos | `Notes` (máx. 1000), `IdempotencyKey` (máx. 100), `Version` (`xmin`) |

Todas las FKs son `Restrict`. `SingleClassTicketMovementId` conserva su nombre aunque también referencia créditos compensatorios autónomos: ambos son movimientos del ledger sin membresía que se consumen por vinculación.

Enumeraciones nuevas:
- `ReservationOrigin { StudentSelfService = 1, StaffDesk = 2, Legacy = 3 }`. Canal de creación; no cambia durante la vida de la reserva. `Legacy` identifica las filas anteriores a este cambio, cuyo canal no se registró.
- `ReservationAccessType { MembershipCredit = 1, UnlimitedMembership = 2, CompensatoryCredit = 3, SingleClassTicket = 4, PromotionalBenefit = 5 }`.
- `ReservationCancellationSource { Student = 1, Staff = 2, ClassSessionChange = 3, MembershipChange = 4, WaitlistOfferExpired = 5, WaitlistDeclined = 6, WaitlistExpired = 7 }`.
- `ReservationChangeTrigger { StudentAction = 1, StaffAction = 2, WaitlistPromotion = 3, WaitlistWorker = 4, ClassSessionChange = 5, MembershipChange = 6, Attendance = 7, DataMigration = 8 }`.
- `WaitlistPromotionMode { Automatic = 1, OfferWithConfirmation = 2 }`.

`ReservationStatusChange` (sólo inserción): `Id`, `ReservationId` (FK `Restrict`), `PreviousStatus?`, `NewStatus`, `Reason` (máx. 500), `ChangedByUserId?`, `Trigger`, `ChangedAtUtc`.

Los métodos de dominio (`Confirm`, `OfferSpot`, `AcceptOffer`, `Cancel`, `MarkAttended`, `MarkNoShow`) validan la transición, reciben `nowUtc` por parámetro, agregan el `ReservationStatusChange` y lanzan `InvalidOperationException` ante una transición inválida.

- **Alternativa descartada**: reutilizar la fila `Cancelled` cuando el alumno vuelve a reservar. Impide auditar cada intento y su movimiento de crédito.

### Decision 4: Máquina de estados

| Origen | Operación | Destino | Condiciones | Actor |
|---|---|---|---|---|
| — | `Create` con cupo | `Confirmed` | Clase `Open`, no iniciada, ventana válida, derecho resuelto; consume derecho, `ReservedCount++` | Alumno dueño o personal |
| — | `Create` / `JoinWaitlist` sin cupo | `Waitlisted` | Lista habilitada y con lugar; derecho resoluble; sin consumo | Alumno dueño o personal |
| `Waitlisted` | `Promote` (modo `Automatic`) | `Confirmed` | Cupo libre, umbral previo al inicio, derecho revalidado y consumido; `ReservedCount++` | Sistema |
| `Waitlisted` | `OfferSpot` (modo `OfferWithConfirmation`) | `Pending` | Igual, sin consumir; fija vencimiento; `ReservedCount++` | Sistema |
| `Pending` | `AcceptOffer` | `Confirmed` | Oferta vigente, derecho revalidado y consumido | Alumno dueño o personal |
| `Pending` | `DeclineOffer` / `Cancel` / vencimiento / cambio de clase o membresía | `Cancelled` | `ReservedCount--`, promueve al siguiente | Alumno, personal, worker o handler |
| `Waitlisted` | `Cancel` / `LeaveWaitlist` / inicio de la clase / cambio de clase o membresía | `Cancelled` | Recompacta posiciones | Alumno, personal, worker o handler |
| `Confirmed` | `Cancel` | `Cancelled` | `UtcNow < StartsAtUtc` (salvo handler de suspensión de clase en curso); política de restitución; `ReservedCount--`; promueve | Alumno, personal o handler |
| `Confirmed` | `RecordAttendance` | `Attended` | Ventana de registro; clase no anulada; `AttendedCount++` | Instructor asignado o personal |
| `Confirmed` | `RecordNoShow` | `NoShow` | `UtcNow >= EndsAtUtc`, dentro del plazo posterior | Instructor asignado o personal |

`Cancelled`, `Attended` y `NoShow` son terminales (`409`).

### Decision 5: Bloqueos, unicidad e idempotencia

1. **Orden único de bloqueo pesimista.** Toda escritura abre una transacción explícita y bloquea:
   1. Las filas de `ClassSessions` involucradas, por `Id` ascendente (`LockClassSessionForUpdateAsync`).
   2. Las membresías de los alumnos involucrados, por `StudentId` ascendente (`LockStudentMembershipsForUpdateAsync`).

   Aplica a `Create`, `Cancel`, `JoinWaitlist`, `LeaveWaitlist`, `AcceptOffer`, `DeclineOffer`, `RecordAttendance`, `RecordNoShow`, al worker y a los handlers.
   - **Operaciones iniciadas desde una membresía.** `MembershipService` (suspender, cancelar) hoy bloquea la membresía primero. Pasa a: leer sin bloqueo los `ClassSessionId` de las reservas activas futuras de la membresía, bloquear esas clases por `Id` ascendente y recién entonces bloquear la membresía. Si el handler encuentra una reserva activa cuya clase no fue bloqueada (creada en el intervalo), lanza `ConflictException` para que la operación se reintente.
   - **Red de seguridad.** Los errores PostgreSQL `40P01` (deadlock) y `DbUpdateConcurrencyException` (`xmin`) se traducen a `ConflictException` → `409 CONCURRENCY_CONFLICT`.
2. **Índices únicos parciales.**
   ```sql
   CREATE UNIQUE INDEX "UX_Reservations_Active_Session_Student"
     ON "Reservations" ("ClassSessionId", "StudentId")
     WHERE "Status" IN (1, 6, 7) AND "IsActive";

   CREATE UNIQUE INDEX "UX_Reservations_Student_IdempotencyKey"
     ON "Reservations" ("StudentId", "IdempotencyKey")
     WHERE "IdempotencyKey" IS NOT NULL;

   -- Un pase o crédito compensatorio autónomo queda retenido por la reserva que lo usa,
   -- incluida una cancelación tardía (no se restituye).
   CREATE UNIQUE INDEX "UX_Reservations_Held_AccessMovement"
     ON "Reservations" ("SingleClassTicketMovementId")
     WHERE "SingleClassTicketMovementId" IS NOT NULL AND "IsActive"
       AND ("Status" IN (1, 4, 5, 7) OR ("Status" = 3 AND "IsLateCancellation"));

   -- Un canje respalda una única reserva, para siempre.
   CREATE UNIQUE INDEX "UX_Reservations_PromotionRedemption"
     ON "Reservations" ("PromotionRedemptionId")
     WHERE "PromotionRedemptionId" IS NOT NULL;
   ```
   La retención del pase tras una cancelación tardía se expresa en el predicado del índice, sin modificar el movimiento del ledger (que es inmutable). Una cancelación con excepción administrativa deja `IsLateCancellation = false` y por lo tanto libera el pase.
3. **Idempotencia.** Con la clase ya bloqueada y antes de cualquier validación de negocio, se busca `(StudentId, IdempotencyKey)`:
   - Misma `ClassSessionId` → se devuelve la reserva existente con `200 OK`, en su estado actual.
   - Otra `ClassSessionId` → `409 IDEMPOTENCY_KEY_MISMATCH`.
   La clave se recibe por el header `Idempotency-Key` (también en `POST .../waitlist`).
4. **Unidad de trabajo.** Reserva, `ReservedCount`, movimiento del ledger, canje promocional, `ReservationStatusChange` y `AuditLog` se guardan en la misma transacción.
5. **Rol de `xmin`.** No se expone al cliente (no hay edición administrativa de reservas). Es una segunda barrera ante una escritura que no haya tomado el bloqueo de la clase.

- **Alternativa descartada**: sólo concurrencia optimista. Bajo `READ COMMITTED` dos inserciones concurrentes no tocan la misma fila y ambas confirmarían el último cupo.

### Decision 6: Resolución y consumo del derecho de acceso

`CreateReservationRequest`: `ClassSessionId`, `StudentId?`, `MembershipId?`, `SingleClassTicketMovementId?`, `PromotionCode?`, `AllowWaitlist` (por defecto `true`), `Notes?`, `OverrideBookingWindow` (por defecto `false`), `PolicyOverrideReason?`.

Los bloqueos de persona (`STUDENT_NOT_ACTIVE`, `AGE_NOT_ALLOWED`, `MEDICAL_CERTIFICATE_REQUIRED`, `UNDERAGE_EMERGENCY_CONTACT_REQUIRED`) y de actividad (`ACTIVITY_NOT_ACTIVE`) se evalúan primero y aplican a todas las modalidades.

Orden de resolución (`ReservationAccessResolver`):
1. `PromotionCode` → `IPromotionService.EvaluatePromotionForReservationAsync(studentId, classSessionId, activityId, code)`. Sólo procede si el beneficio cubre el acceso completo; si no, `409 PROMOTION_NOT_APPLICABLE`.
2. `SingleClassTicketMovementId` → el movimiento debe ser del alumno, de tipo `SingleClassTicket` (para esa clase) o `ClassSuspensionCompensation` autónomo (vigente y aplicable), y no estar retenido por otra reserva.
3. `MembershipId` → `IMembershipEligibilityService.SelectMembershipForAsync` con membresía explícita.
4. Sin selección: membresía elegible → pase individual de esa clase → crédito compensatorio autónomo vigente (menor `ExpiresOn` primero) → `409 NO_ACCESS_RIGHT` con los motivos.

Una selección explícita inválida rechaza la reserva sin probar las siguientes.

Consumo al confirmar:
- **Membresía**: `ICreditLedgerService.ConsumeCreditForReservationAsync(..., reservationId)`. Con plan ilimitado el ledger ya registra el movimiento con `Amount = 0`; se mantiene y `AccessType = UnlimitedMembership`.
- **Pase individual**: si existen `Payments` para `(StudentId, ClassSessionId)` y ninguno está `Completed` → `409 PAYMENT_NOT_CONFIRMED`. Se guarda `SingleClassTicketMovementId` y el `PaymentId` completado.
- **Crédito compensatorio autónomo**: se guarda `SingleClassTicketMovementId`. No se crea un movimiento nuevo: el consumo es la vinculación protegida por el índice.
- **Promoción**: `IPromotionService.RedeemPromotionForReservationAsync(..., reservationId)`; se guarda `PromotionRedemptionId`.

En `Waitlisted` y `Pending` la reserva guarda la modalidad prevista y las referencias solicitadas (`MembershipId`, `SingleClassTicketMovementId`, `RequestedPromotionCode`) y el resolver se ejecuta de nuevo al confirmar.

**Extensiones a otros módulos:**
- `MembershipCreditMovement`: `ReservationId` (`Guid?`, FK `Restrict`), `ExpiresOn` (`DateOnly?`) y `ActivityId` (`Guid?`, alcance; `null` = todas). Índice único `UX_CreditMovements_Reservation_Type` sobre `("ReservationId", "Type") WHERE "ReservationId" IS NOT NULL AND "Type" IN (2, 3)`: a lo sumo un débito y una devolución por reserva.
- `ICreditLedgerService`: `ConsumeCreditForReservationAsync` y `RefundCreditForReservationAsync` reciben `reservationId`; la devolución devuelve el movimiento existente si ya hay uno. `AddCompensatoryCreditAsync` fija `ExpiresOn = hoy + MembershipsOptions.CompensatoryCreditValidityDays` (15) y el alcance de actividad en los créditos autónomos.
- `PromotionRedemption`: `ReservationId` (`Guid?`, FK `Restrict`, índice único parcial). `IPromotionService`: `EvaluatePromotionForReservationAsync` y `RedeemPromotionForReservationAsync`; `VoidRedemptionAsync` no cambia.

### Decision 7: Políticas de anticipación y cancelación (`ReservationsOptions`)

Sección `"Reservations"` de `appsettings.json`:

| Opción | Defecto | Uso |
|---|---|---|
| `MinAdvanceBookingMinutes` | `0` | Minutos previos al inicio en los que ya no se reserva |
| `MaxAdvanceBookingDays` | `30` | Anticipación máxima (`0` = sin límite) |
| `FreeCancellationHoursBeforeStart` | `2.0` | Umbral de cancelación sin penalización |
| `EnableWaitlist` | `true` | Habilita la lista de espera |
| `MaxWaitlistSize` | `20` | Tope de inscriptos en espera por clase |
| `WaitlistPromotionMode` | `Automatic` | `Automatic` u `OfferWithConfirmation` |
| `WaitlistOfferExpirationMinutes` | `30` | Validez de una oferta |
| `MinMinutesBeforeStartForWaitlistPromotion` | `15` | Umbral para seguir promoviendo |
| `WaitlistWorkerIntervalSeconds` | `60` | Frecuencia del worker |
| `CheckInOpensMinutesBeforeStart` | `30` | Apertura del registro de presencia |
| `AttendanceRegistrationHoursAfterEnd` | `24` | Plazo posterior para registrar presencia o ausencia |
| `AvailableClassesMaxRangeDays` | `31` | Rango máximo de `available-classes` |

**Creación fuera de ventana**: `OverrideBookingWindow = true` exige `RESERVATIONS_OVERRIDE_POLICY` y `PolicyOverrideReason`; sólo omite la ventana de anticipación.

**Cancelación (`CancelReservationRequest`: `Reason?`, `OverridePolicy`, `PolicyOverrideReason?`)**:
- `UtcNow >= StartsAtUtc` → `409 CLASS_ALREADY_STARTED`, sin excepción posible por este endpoint.
- `Reason` vacío → `"Cancelada por el alumno"` o `"Cancelada por el personal"` (la restricción `CHECK` exige motivo).
- Para una reserva `Confirmed`: `isLate = horasRestantes < FreeCancellationHoursBeforeStart && !override`.
  - No tardía: devolución de crédito por el ledger; pase o crédito compensatorio liberado por el índice; canje anulado con `VoidRedemptionAsync`.
  - Tardía: sin restitución. `IsLateCancellation = true` mantiene retenido el pase; el canje sigue vigente.
- `Waitlisted` / `Pending`: sin penalización; `Pending` además libera el cupo retenido.
- Siempre: `ReservedCount--` si ocupaba cupo y `PromoteWaitlistAsync(session)`.

### Decision 8: Lista de espera

- **Ingreso**: bajo el bloqueo de la clase, `nextPosition = COUNT(Waitlisted) + 1` (las posiciones se mantienen compactas). Rechazos: `CLASS_FULL` (lista deshabilitada o `AllowWaitlist = false`), `WAITLIST_FULL`.
- **Compactación**: `CompactWaitlistPositions(session)` renumera `1..K` ordenando por `WaitListPosition, WaitlistEnteredAtUtc, Id`. No hay índice único sobre la posición, porque la renumeración lo violaría de forma transitoria.
- **Promoción (`PromoteWaitlistAsync(session)`)**: se ejecuta en la misma transacción que liberó cupo, mientras `AvailableSpots > 0` y falten al menos `MinMinutesBeforeStartForWaitlistPromotion`. Recorre los candidatos en orden; bloquea sus membresías; ejecuta el resolver; un candidato no elegible se omite y conserva su posición. Disparadores: cancelación de una reserva que ocupaba cupo, rechazo o vencimiento de una oferta, cancelación por cambio de membresía y **ampliación del cupo** (nuevo evento `ClassSessionCapacityIncreased` emitido por `ClassSession.UpdateCapacity`, atendido por `PromoteWaitlistOnCapacityIncreasedHandler`).
- **Oferta**: `WaitlistOfferExpiresAtUtc = min(nowUtc + WaitlistOfferExpirationMinutes, StartsAtUtc)`.
- **Aceptación**: si la oferta venció → `409 WAITLIST_OFFER_EXPIRED`. Si el alumno ya no es elegible → la oferta se cancela (`WaitlistOfferExpired`), se promueve al siguiente, se confirma esa transacción y se responde `409` con los motivos.
- **`ReservationWaitlistWorker : BackgroundService`** (junto a `MembershipExpirationWorker`): cada `WaitlistWorkerIntervalSeconds`, en una transacción por clase y con su bloqueo:
  1. Cancela las ofertas `Pending` con `WaitlistOfferExpiresAtUtc <= nowUtc` (`WaitlistOfferExpired`) y promueve al siguiente.
  2. Para las clases ya iniciadas, cancela las `Waitlisted` restantes (`WaitlistExpired`).
  Es idempotente: sólo actúa sobre filas que siguen en el estado esperado tras tomar el bloqueo.

No existe promoción manual por parte del personal: el orden de la lista es el único criterio.

### Decision 9: Cambios de clase y de membresía

- **`CancelReservationsOnSessionVoidedHandler`** (suspensión, cancelación, reprogramación): `ClassSessionService` ya bloquea la clase. El handler bloquea las membresías de los alumnos afectados por `StudentId` ascendente y, por cada reserva activa: `Cancel` con `CancellationSource = ClassSessionChange`, `AffectedBySessionChange = true`, `IsLateCancellation = false`; devolución de crédito si hubo débito; `VoidRedemptionAsync` si hubo canje; en reprogramación guarda `RescheduledToSessionId`. Las reservas con pase individual no generan movimiento y se listan con el filtro `affectedBySessionChange=true&accessType=SingleClassTicket` para que el personal emita la compensación. Al final `ResetReservedCount()`. No promueve lista de espera (la clase quedó anulada).
- **`CancelReservationsOnMembershipSuspendedHandler` y `CancelFutureReservationsOnMembershipCancelledHandler`**: con las clases ya bloqueadas por `MembershipService` (Decision 5), cancelan con `CancellationSource = MembershipChange`, decrementan `ReservedCount` y llaman a `PromoteWaitlistAsync` por cada clase afectada. La suspensión devuelve el crédito; la cancelación de la membresía no (el saldo se da de baja por `CancellationForfeiture`).
- **`NotifyStudentsOnSessionChangeHandler`** sigue siendo quien avisa los cambios de clase. Cuando la clase se anula, además de las reservas activas incluye las canceladas con `CancellationSource = ClassSessionChange`: el handler de cancelación corre antes y su auditoría (`AuditService.LogAsync`) ya guardó esas cancelaciones, por lo que una consulta sólo por estados activos no encontraría destinatarios.

### Decision 10: Asistencia

No se crea un módulo `Attendance`. Las columnas de asistencia de `Reservation` son la fuente de verdad del hecho para alumnos con reserva, y sólo las modifican `RecordAttendanceAsync` y `RecordNoShowAsync`:
- Ambas toman el bloqueo de la clase y validan que su estado persistido no sea `Suspended`, `Cancelled` ni `Rescheduled`.
- Presencia: `StartsAtUtc - CheckInOpensMinutesBeforeStart <= nowUtc <= EndsAtUtc + AttendanceRegistrationHoursAfterEnd`.
- Ausencia: `EndsAtUtc <= nowUtc <= EndsAtUtc + AttendanceRegistrationHoursAfterEnd`.
- Fuera de plazo → `409 ATTENDANCE_WINDOW_CLOSED`.
- Un instructor sin rol de personal sólo opera en clases donde es instructor programado o efectivo; en otro caso `404`.

### Decision 11: Eventos post-commit y notificaciones

- Se introduce la interfaz marcadora `IPostCommitDomainEvent : IDomainEvent`. `SmartGymDbContext.DispatchDomainEventsAsync` no despacha estos eventos dentro de la transacción: los acumula en una cola del contexto.
- La cola se despacha cuando la operación quedó confirmada: en `TransactionCommittedAsync` de un `DbTransactionInterceptor` cuando hay transacción explícita, o inmediatamente después de `base.SaveChangesAsync` cuando no la hay (o el proveedor no es relacional, como en los tests en memoria). En `TransactionRolledBackAsync` la cola se descarta.
- Cada handler post-commit se ejecuta en un `IServiceScope` nuevo (con su propio `DbContext`) dentro de un `try/catch` que registra el error con `ILogger` y continúa con el siguiente.
- Eventos de reserva (todos `IPostCommitDomainEvent`): `ReservationConfirmedEvent`, `ReservationWaitlistedEvent`, `ReservationCancelledEvent`, `ReservationPromotedFromWaitlistEvent`, `WaitlistOfferCreatedEvent`, `WaitlistOfferExpiredEvent`.
- `NotifyStudentOnReservationLifecycleHandler` (módulo `Operations`) crea el `NotificationMessage` in-app. Ignora `ReservationCancelledEvent` con `CancellationSource = ClassSessionChange`.
- `NotificationType` suma: `ReservationConfirmed = 9`, `ReservationCancelled = 10`, `ReservationWaitlisted = 11`, `ReservationPromotedFromWaitlist = 12`, `WaitlistSpotOffered = 13`, `WaitlistOfferExpired = 14`.
- **Trade-off aceptado**: si el proceso cae entre el commit y el despacho, esa notificación se pierde (entrega a lo sumo una vez). Un outbox persistente la garantizaría, pero no se justifica para avisos in-app.
- **Alternativa descartada**: `try/catch` dentro de un handler pre-commit. Sólo aísla errores al construir el aviso; la inserción viaja en el mismo `SaveChanges` que la reserva y un fallo allí revierte todo.

### Decision 12: Consulta `available-classes`

- Una consulta proyectada sobre `ClassSessions` (`Open`, futuras, actividad activa, rango y filtros) con `Capacity`, `ReservedCount`, conteo de `Waitlisted` y la reserva activa del alumno, resueltos con subconsultas agregadas en SQL. Reutiliza los criterios de la consulta de disponibilidad de `class-sessions` y la enriquece; aquella no cambia.
- Elegibilidad en lote: `IMembershipEligibilityService.EvaluateForSessionsAsync(studentId, sessions)` carga una vez la persona, sus membresías con actividades permitidas, sus pases y sus créditos compensatorios, y evalúa cada clase en memoria. Sin escrituras.
- Rango mayor a `AvailableClassesMaxRangeDays` → `400`.

---

## Persistencia PostgreSQL

```sql
-- "Reservations": columnas nuevas (las existentes se conservan)
"Origin" INT NOT NULL,                      -- 1 StudentSelfService, 2 StaffDesk, 3 Legacy
"AccessType" INT NOT NULL,                  -- 1..5
"SingleClassTicketMovementId" UUID NULL REFERENCES "MembershipCreditMovements"("Id") ON DELETE RESTRICT,
"PaymentId" UUID NULL REFERENCES "Payments"("Id") ON DELETE RESTRICT,
"PromotionRedemptionId" UUID NULL REFERENCES "PromotionRedemptions"("Id") ON DELETE RESTRICT,
"RequestedPromotionCode" VARCHAR(50) NULL,
"CreatedByUserId" UUID NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"WaitlistEnteredAtUtc" TIMESTAMPTZ NULL,
"WaitlistPromotedAtUtc" TIMESTAMPTZ NULL,
"WaitlistOfferExpiresAtUtc" TIMESTAMPTZ NULL,
"CancelledByUserId" UUID NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"CancellationSource" INT NULL,
"IsPolicyOverride" BOOLEAN NOT NULL DEFAULT FALSE,
"PolicyOverrideReason" VARCHAR(500) NULL,
"AffectedBySessionChange" BOOLEAN NOT NULL DEFAULT FALSE,
"RescheduledToSessionId" UUID NULL REFERENCES "ClassSessions"("Id") ON DELETE RESTRICT,
"Notes" VARCHAR(1000) NULL,
"IdempotencyKey" VARCHAR(100) NULL
-- "xmin" como token de concurrencia (Version)

-- Restricciones CHECK
"CK_Reservations_Status"          ("Status" IN (1, 3, 4, 5, 6, 7))
"CK_Reservations_Origin"          ("Origin" IN (1, 2, 3))
"CK_Reservations_AccessType"      ("AccessType" IN (1, 2, 3, 4, 5))
"CK_Reservations_WaitlistPosition" (("Status" = 6) = ("WaitListPosition" IS NOT NULL AND "WaitListPosition" >= 1))
"CK_Reservations_PendingOffer"    (("Status" = 7) = ("WaitlistOfferExpiresAtUtc" IS NOT NULL))
"CK_Reservations_Cancellation"    (("Status" = 3) = ("CancelledAtUtc" IS NOT NULL AND "CancellationReason" IS NOT NULL))
"CK_Reservations_PolicyOverride"  (NOT "IsPolicyOverride" OR "PolicyOverrideReason" IS NOT NULL)

-- Índices únicos parciales: ver Decision 5
-- Índices operacionales
"IX_Reservations_Student_Status_ReservedAt" ("StudentId", "Status", "ReservedAtUtc" DESC)
"IX_Reservations_Session_Status"            ("ClassSessionId", "Status")
"IX_Reservations_Session_Waitlist"          ("ClassSessionId", "WaitListPosition") WHERE "Status" = 6 AND "IsActive"
"IX_Reservations_PendingOffers"             ("WaitlistOfferExpiresAtUtc") WHERE "Status" = 7 AND "IsActive"

-- "ReservationStatusChanges"
"Id" UUID PRIMARY KEY,
"ReservationId" UUID NOT NULL REFERENCES "Reservations"("Id") ON DELETE RESTRICT,
"PreviousStatus" INT NULL,
"NewStatus" INT NOT NULL,
"Reason" VARCHAR(500) NOT NULL,
"ChangedByUserId" UUID NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"Trigger" INT NOT NULL,
"ChangedAtUtc" TIMESTAMPTZ NOT NULL

-- "MembershipCreditMovements": "ReservationId" UUID NULL, "ExpiresOn" DATE NULL, "ActivityId" UUID NULL
-- "PromotionRedemptions": "ReservationId" UUID NULL (índice único parcial)
```

`IsActive` se conserva en los predicados por coherencia con `BaseEntity`; las reservas nunca se dan de baja lógica.

---

## Contratos API (`/api/reservations`)

| Método y ruta | Autorización | Descripción | Respuestas |
|---|---|---|---|
| `GET /` | `RESERVATIONS_READ` | Listado paginado con filtros en servidor (`studentId`, `classSessionId`, `activityId`, `status`, `accessType`, `origin`, `from`, `to`, `isLateCancellation`, `affectedBySessionChange`, `page`, `pageSize`) | `200` |
| `GET /{id}` | Personal, alumno dueño o instructor de la clase | Detalle con derecho de acceso e historial de estados | `200`, `403`, `404` |
| `GET /student/{studentId}` | Personal o alumno dueño | Historial del alumno (`status`, `from`, `to`) | `200`, `403` |
| `GET /my`, `GET /my/upcoming` | Alumno | Historial y próximas reservas activas del alumno autenticado | `200` |
| `GET /available-classes` | Alumno, o personal con `studentId` | Clases reservables con cupos y elegibilidad | `200`, `400`, `403` |
| `GET /class-session/{classSessionId}` (alias `/session/{classSessionId}`) | Personal o instructor de la clase | Reservas de una clase | `200`, `404` |
| `GET /class-session/{classSessionId}/waitlist` | `RESERVATIONS_WAITLIST_READ` (instructor: sólo su clase) | Lista de espera ordenada | `200`, `404` |
| `GET /class-session/{classSessionId}/attendance-summary` (alias `/session/...`) | Personal o instructor de la clase | Resumen de asistencia | `200`, `404` |
| `POST /` | `RESERVATIONS_CREATE` | Crea la reserva o inscribe en lista de espera. Header `Idempotency-Key` | `201`, `200` (reintento), `400`, `403`, `404`, `409` |
| `POST /{id}/cancel` | `RESERVATIONS_CANCEL` (`OverridePolicy` exige `RESERVATIONS_OVERRIDE_POLICY`) | Cancela aplicando la política | `200`, `400`, `403`, `404`, `409` |
| `POST /{id}/accept-waitlist-offer` | Alumno dueño o `RESERVATIONS_MANAGE` | Acepta una oferta vigente | `200`, `403`, `404`, `409` |
| `POST /{id}/decline-waitlist-offer` | Alumno dueño o `RESERVATIONS_MANAGE` | Rechaza una oferta | `200`, `403`, `404`, `409` |
| `POST /class-session/{classSessionId}/waitlist` | `RESERVATIONS_CREATE` | Ingresa a la lista de espera de una clase completa | `201`, `400`, `403`, `404`, `409` |
| `DELETE /class-session/{classSessionId}/waitlist/me` | Alumno | Sale de la lista de espera | `200`, `404` |
| `POST /attendance` | Personal o instructor de la clase | Registra presencia | `200`, `404`, `409` |
| `POST /{id}/no-show` | Personal o instructor de la clase | Registra ausencia | `200`, `404`, `409` |

**Errores**: `{ "code": "...", "message": "...", "reasons": [{ "code", "message" }] }`. `400` formato/validación de entrada, `403` ámbito, `404` inexistente o clase ajena para un instructor, `409` regla de negocio o concurrencia. Códigos propios: `CLASS_NOT_OPEN`, `CLASS_ALREADY_STARTED`, `BOOKING_WINDOW_CLOSED`, `ALREADY_RESERVED`, `NO_ACCESS_RIGHT`, `CLASS_FULL`, `WAITLIST_FULL`, `WAITLIST_OFFER_EXPIRED`, `PAYMENT_NOT_CONFIRMED`, `PROMOTION_NOT_APPLICABLE`, `ACCESS_RIGHT_IN_USE`, `IDEMPOTENCY_KEY_MISMATCH`, `INVALID_STATE_TRANSITION`, `ATTENDANCE_WINDOW_CLOSED`, `CONCURRENCY_CONFLICT`; más los códigos de inelegibilidad de `memberships`.

**Permisos** (`RolesAndPolicies.cs`, registrados en `DependencyInjection.cs` como las políticas de `Memberships`):

| Permiso | Roles |
|---|---|
| `RESERVATIONS_READ` | Administrator, Secretary |
| `RESERVATIONS_CREATE` | Administrator, Secretary, Student |
| `RESERVATIONS_CANCEL` | Administrator, Secretary, Student |
| `RESERVATIONS_MANAGE` | Administrator, Secretary |
| `RESERVATIONS_WAITLIST_READ` | Administrator, Secretary, Instructor |
| `RESERVATIONS_OVERRIDE_POLICY` | Administrator, Secretary |

El permiso habilita el endpoint; el ámbito (propiedad del alumno, clase del instructor) se valida en el servicio con `ICurrentUserService`. Nunca se confía en `request.StudentId` para identificar al usuario: un alumno sin rol de personal opera sobre su `PersonId`, y enviar otro produce `403`. Las respuestas a alumnos no incluyen datos de otros alumnos.

---

## Estrategia de pruebas

1. **Unitarias de dominio** (`SmartGym.Domain.UnitTests/Reservations`, sin base): transiciones válidas e inválidas, registro de `ReservationStatusChange`, eventos emitidos.
2. **Servicios con EF InMemory** (mismo proyecto, patrón actual): validaciones de creación, resolución del derecho, políticas de cancelación, lista de espera, worker, handlers de clase y de membresía, asistencia, autorización de ámbito, despacho post-commit.
3. **Modelo** (patrón `*ModelConfigurationTests` con `UseNpgsql` sin conexión): columnas, FKs `Restrict`, `CHECK`, filtros de índices parciales, `xmin`.
4. **Integración con PostgreSQL real** (proyecto nuevo `SmartGym.IntegrationTests`, `Testcontainers.PostgreSql`; requiere Docker): migración con datos previos, índices únicos parciales, carrera por el último cupo, reserva duplicada concurrente, reintento idempotente concurrente, promoción concurrente sin doble asignación, orden de bloqueo entre cancelación de reserva y suspensión de membresía.
5. **Controlador** (`ReservationsControllerTests`): códigos HTTP, `403`/`404` por ámbito, exigencia de permiso y motivo en las excepciones, header `Idempotency-Key`.

### Criterios de aceptación

| # | Criterio | Escenario en `specs/reservations/spec.md` | Nivel |
|---|---|---|---|
| 1 | Un alumno elegible reserva una clase con cupo | Alumno elegible reserva una clase abierta con cupo disponible | 2 |
| 2 | Sin derecho de acceso válido no se reserva | Rechazo de reserva sin derecho de acceso válido | 2 |
| 3 | No se reserva una clase no abierta o iniciada | Rechazo de reserva en clase no abierta o ya iniciada | 2 |
| 4 | Sin dos reservas activas del mismo alumno en la misma clase | Imposibilidad de duplicar reserva activa | 2 y 4 |
| 5 | Dos solicitudes por el último lugar no sobrevenden | Dos solicitudes concurrentes por el último lugar no producen sobreventa | 4 |
| 6 | Un fallo en créditos no deja una reserva confirmada | Reversión atómica ante fallo en el consumo de créditos | 2 |
| 7 | La consulta de disponibilidad no consume créditos | Consulta de clases disponibles no consume créditos | 2 |
| 8 | La cancelación permitida libera cupo y devuelve el crédito | Cancelación anticipada libera cupo y devuelve el crédito | 2 |
| 9 | La cancelación tardía respeta la política | Cancelación tardía libera cupo pero retiene el crédito / con excepción administrativa / con pase individual | 2 |
| 10 | La cancelación repetida no devuelve dos veces | Cancelación repetida no devuelve créditos dos veces | 2 y 4 |
| 11 | La lista de espera no ocupa cupo | Ingreso a lista de espera no ocupa cupo ni consume crédito | 2 |
| 12 | La liberación de cupo promueve al candidato correcto | Liberación de cupo promueve al candidato correcto sin doble asignación | 2 y 4 |
| 13 | Un candidato inelegible no bloquea la lista | Candidato que perdió elegibilidad no bloquea al siguiente | 2 |
| 14 | La suspensión de la clase restituye derechos | Suspensión de clase restituye derechos de todas las reservas afectadas | 2 |
| 15 | La reprogramación no traslada la reserva | Reprogramación no traslada la reserva del alumno | 2 |
| 16 | La reserva administrativa registra operador y beneficiario | Reserva creada por secretaría registra operador y beneficiario | 2 |
| 17 | Un alumno no accede a reservas ajenas | Alumno intenta consultar o cancelar la reserva de otro alumno | 5 |
| 18 | Una falla de notificación no revierte la reserva | Falla en Notifications no revierte una reserva confirmada | 2 |
| 19 | Los reintentos no duplican reservas ni consumos | Reintento HTTP con la misma clave de idempotencia | 2 y 4 |
| 20 | La asistencia sólo se modifica por sus operaciones | Escenarios de `specs/reservations-attendance/spec.md` | 2 |
| 21 | Las ofertas de cupo se aceptan, rechazan y vencen | Oferta de cupo aceptada / vencida / cercana al inicio | 2 |
| 22 | La suspensión de una membresía promueve la lista de espera | Suspensión de membresía libera el cupo para la lista de espera | 2 |

---

## Migration Plan

Migración `RefactorReservationsModule`, en este orden:

1. Agregar las columnas nuevas a `"Reservations"` (las `NOT NULL` con valor por defecto temporal), a `"MembershipCreditMovements"` y a `"PromotionRedemptions"`; crear `"ReservationStatusChanges"`.
2. Datos existentes de `"Reservations"`:
   - `UPDATE ... SET "Status" = 1, "ConfirmedAtUtc" = COALESCE("ConfirmedAtUtc", "ReservedAtUtc") WHERE "Status" IN (1, 2)`. El valor `6` no cambia; se completa `"WaitlistEnteredAtUtc" = "ReservedAtUtc"`.
   - `"Origin" = 3` (`Legacy`).
   - `"AccessType"`: con `"MembershipId"` → `2` si la membresía es ilimitada, `1` si no. Sin `"MembershipId"` → `4` (`SingleClassTicket`), moviendo `"CreditMovementId"` a `"SingleClassTicketMovementId"` y dejando `"CreditMovementId"` nulo (hoy esa columna guarda el pase en las reservas sin membresía).
   - Pases reutilizados: si dos filas que caerían en `UX_Reservations_Held_AccessMovement` comparten movimiento, se conserva el vínculo en la de `"ReservedAtUtc"` más antigua y en las demás se anula, dejando en `"Notes"` la leyenda `'Pase compartido con otra reserva antes de la migración'`.
   - Canceladas sin motivo: `"CancellationReason" = 'Cancelación previa a migración'`, `"CancelledAtUtc" = COALESCE("CancelledAtUtc", "UpdatedAtUtc", "ReservedAtUtc")`.
   - `"MembershipCreditMovements"."ReservationId"`: se completa para los movimientos tipo 2 y 3 cuyo `"Id"` figura en `"CreditMovementId"` o `"RefundCreditMovementId"` de una reserva.
   - Una fila inicial en `"ReservationStatusChanges"` por reserva (`PreviousStatus = NULL`, `Reason = 'Estado migrado'`, `Trigger = 8`).
3. Cambiar las FKs `"MembershipId"`, `"CreditMovementId"` y `"RefundCreditMovementId"` de `SetNull` a `Restrict`.
4. Quitar los valores por defecto temporales y crear las restricciones `CHECK`, los índices únicos parciales y los índices operacionales.

`Down` elimina tablas, columnas, índices y restricciones nuevos y restaura las FKs. La fusión `2 → 1` no se revierte (el valor `2` no tenía uso).

Despliegue: backend y frontend juntos; el frontend deja de interpretar el valor `2` y pasa a tratar `1` como confirmada.

---

## Risks / Trade-offs

- **[Riesgo] Contención por `FOR UPDATE` en una clase muy demandada** → la transacción es corta y sólo bloquea esa clase y las membresías del alumno; las lecturas de disponibilidad no bloquean.
- **[Riesgo] Reordenar los bloqueos de `MembershipService`** toca código recién estabilizado → cubierto por el test de integración de orden de bloqueo y por la traducción de `40P01` a `409`.
- **[Riesgo] El despacho post-commit cambia `SmartGymDbContext`**, que usan todos los módulos → sólo afecta a eventos marcados con `IPostCommitDomainEvent`; los eventos existentes conservan su comportamiento, verificado por `DomainEventDispatchTests`.
- **[Trade-off] Notificaciones a lo sumo una vez** (Decision 11).
- **[Trade-off] Los tests de integración requieren Docker** en desarrollo y CI. A cambio, los criterios 5, 12 y 19 se verifican contra PostgreSQL real y no contra bloqueos simulados.
- **[Trade-off] Promoción automática por defecto**: el alumno en espera pasa a `Confirmed` y consume su crédito sin intervención. `OfferWithConfirmation` queda disponible por configuración.
- **[Pendiente operativo] Compensación de pases individuales** ante clases anuladas: sigue siendo manual, apoyada en el filtro del listado.
