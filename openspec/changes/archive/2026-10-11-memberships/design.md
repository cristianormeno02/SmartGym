# Design Document — Memberships (`memberships`)

## Context

Smart-Gym es un Monolito Modular en .NET 10 (ASP.NET Core Web API) con EF Core + PostgreSQL (Npgsql) y frontend Angular 22. La motivación del cambio se detalla en [proposal.md](proposal.md) y los requisitos normativos en [specs/memberships/spec.md](specs/memberships/spec.md).

### Estado actual e inconsistencias identificadas en el repositorio

Al inspeccionar el código y las especificaciones actuales se detectaron las siguientes inconsistencias que este diseño resuelve:

1. **`Membership` carece de historial comercial y atributos obligatorios**:
   - Actualmente `Membership` (`Domain/Entities/Memberships/Membership.cs`) sólo tiene `StudentId`, `MembershipPlanId`, `StartDate`, `EndDate`, `TotalCredits`, `AvailableCredits` y `Status`. No registra `PurchasedAtUtc`, `PurchasePrice`, `OriginalPrice`, `DiscountAmount`, `Currency`, `PromotionId`, `PromotionRedemptionId`, `CreatedByUserId`, `Notes`, `IsUnlimited` ni el snapshot de actividades permitidas.
   - Si un `MembershipPlan` modifica su precio, duración o actividades permitidas, las membresías históricas cambian su comportamiento o pierden su referencia de precio contratado.
2. **Violación de límites con `MembershipCredits`**:
   - `MembershipService.AssignMembershipAsync` y `CancelMembershipAsync` instancian `MembershipCreditMovement` directamente y sobrescriben `membership.AvailableCredits = 0`. Asimismo, `Membership.DeductCredit`, `RefundCredit` y `AddCompensatoryCredit` viven acoplados a la entidad `Membership` y mutan `Status = MembershipStatus.Depleted`.
3. **Estado `Depleted` mezclado con el ciclo de vida de vigencia y ausencia de `Pending`**:
   - `MembershipStatus` define `{ Active = 1, Expired = 2, Suspended = 3, Cancelled = 4, Depleted = 5 }` pero no `Pending`. Cuando un alumno consume su último crédito, el estado pasa a `Depleted`, lo que rompe las consultas de membresías vigentes (`Status == Active`) en `DashboardService` y `MembershipService.GetActiveMembershipAsync` aunque el período pagado aún no haya vencido.
4. **Cálculo de fechas sin `IGymCalendar` y `EndDate` con un día extra**:
   - `MembershipService` usa `DateOnly.FromDateTime(DateTime.UtcNow)` en lugar de `_gymCalendar.Today` (desfasaje de 3 horas en `America/Argentina/San_Juan`), y calcula `endDate = startDate.AddDays(plan.DurationDays)` (sumando 31 días inclusivos para un plan de 30 días).
5. **Falta de validación de `Person.Status` en `People`**:
   - `MembershipService` sólo verifica `p.IsActive` pero no `p.Status == PersonStatus.Active` (definido en `people-management`, permitiendo contratar a personas en estado `Blocked`, `Inactive` o `Deceased`).
6. **Sin integración con `Promotions`, `Payments`, `Audit` ni `Notifications`**:
   - `AssignMembershipAsync` no evalúa ni canjea promociones, no vincula pagos, no audita en `AuditLogs` ni protege contra reintentos (`IdempotencyKey`) o ediciones concurrentes (`xmin`).

---

## Goals / Non-Goals

**Goals:**
- Modelar `Membership` con snapshot comercial inmutable, auditoría de cambios de estado (`MembershipStatusChange`), registro de suspensiones (`MembershipSuspension`) y concurrencia optimista (`xmin`).
- Establecer una máquina de estados clara (`Pending`, `Active`, `Suspended`, `Expired`, `Cancelled`) separando el estado de vigencia del agotamiento de créditos (`IsDepleted` como propiedad calculada del saldo).
- Centralizar toda lectura/escritura del libro de movimientos y proyección de saldo en `ICreditLedgerService` (`MembershipCredits`), garantizando atomicidad transaccional.
- Implementar el servicio `IMembershipEligibilityService` para evaluar acceso y elegibilidad en el backend sin efectos secundarios ni consumo de créditos.
- Coordinar contrataciones y renovaciones con `MembershipPlans`, `Promotions` y `Payments`, con protección de idempotencia y bloqueo por alumno para evitar carreras concurrentes.
- Implementar `MembershipExpirationWorker` (`BackgroundService`) para transicionar vencimientos y emitir notificaciones in-app de forma idempotente.
- Migrar sin pérdida las membresías existentes en PostgreSQL.

**Non-Goals:**
- Rediseñar el catálogo de planes comerciales (`MembershipPlans`), el motor de reglas promocionales (`Promotions`) o la pasarela de cobros (`Payments`), que sólo se consumen mediante sus contratos. Se admiten únicamente los ajustes mínimos que la integración necesita y que la Decision 8 enumera (anulación y vinculación de canjes, fecha local en promociones, confirmación y rechazo de pagos).
- Suspensiones programadas a futuro o retroactivas.
- Contratación o renovación por autogestión del alumno (todas las escrituras son de Staff).
- Vigencia propia de los créditos compensatorios.
- Implementar reembolsos monetarios automáticos al cancelar una membresía (los reintegros corresponden a `Payments` bajo decisión administrativa).
- Envío directo de emails o WhatsApp desde `Memberships` (se delega en `Notifications` mediante eventos/mensajes internos).

---

## Decisions

### Decision 1: Modelo de dominio `Membership` y Snapshot Comercial Inmutable
- **Adoptada**: Al contratar o renovar un plan, `Membership` copia de `MembershipPlan` y de la evaluación de `Promotions` los datos comerciales que rigen ese contrato:
  - `PurchasedAtUtc` (`DateTime` UTC), `StartDate` (`DateOnly` local), `EndDate` (`DateOnly` local).
  - `DurationDaysSnapshot` (`int`): duración en días tomada del plan al contratar. Regla de cálculo de vigencia inclusiva: `EndDate = StartDate.AddDays(DurationDaysSnapshot - 1)` (un plan de 30 días que inicia el `01/04` vence el `30/04` inclusive; un pase diario de 1 día vence el mismo día).
  - `InitialCredits` (`int`): créditos base del plan (`0` si `IsUnlimited == true`).
  - `ExtraCreditsGranted` (`int`): créditos adicionales otorgados por promoción al contratar (`0` si no aplica).
  - `TotalCredits` (`int`, columna existente que se conserva): total acumulado acreditado en la membresía (iniciales + promoción + compensatorios). Sólo lo actualiza `MembershipCredits`.
  - `AvailableCredits` (`int`): proyección transaccional del saldo utilizable mantenida exclusivamente por `MembershipCredits` (`0` si `IsUnlimited == true` y `0` mientras `InitialCreditsLoaded == false`).
  - `ActivatedAtUtc` (`DateTime?`), `CancelledAtUtc` (`DateTime?`), `CancelledByUserId` (`Guid?`, `null` en cancelaciones automáticas) y `CancellationReason` (`string?`, máx. 500).
  - **Vigencia fija desde la contratación**: `StartDate` y `EndDate` se fijan al contratar y **no** se recalculan al activar. Una membresía `Pending` que se activa tarde no recupera los días transcurridos; para evitarlo el personal dispone de la activación inmediata (Decision 8). En contrataciones nuevas se valida `StartDate >= Today` (no se admiten contrataciones retroactivas).
  - `IsUnlimited` (`bool`) y `AppliesToAllActivities` (`bool`): snapshot de la modalidad de consumo del plan.
  - `OriginalPrice` (`decimal(18,2)`), `DiscountAmount` (`decimal(18,2)`), `PurchasePrice` (`decimal(18,2)`), `Currency` (`string`, ISO 4217 de 3 letras, por defecto `"ARS"` desde `GymOptions.DefaultCurrency`).
  - `PromotionId` (`Guid?`) y `PromotionRedemptionId` (`Guid?`): referencias opcionales a la promoción aplicada y su canje en `Promotions`.
  - `RenewedFromMembershipId` (`Guid?`): referencia a la membresía antecedente cuando proviene de una renovación.
  - `CreatedByUserId` (`Guid?`), `Notes` (`string?`, máx. 1000 caracteres), `IdempotencyKey` (`string?`, máx. 100 caracteres), `InitialCreditsLoaded` (`bool`), `ExpiringNotificationSentAtUtc` (`DateTime?`), `ExpiredNotificationSentAtUtc` (`DateTime?`) y `Version` (`uint`, `xmin`).
  - Tabla hija `MembershipAllowedActivities (MembershipId, ActivityId)`: copia las actividades permitidas del plan cuando `AppliesToAllActivities == false`.
- **Alternativa descartada**: Consultar siempre `MembershipPlan` en vivo para precios, modalidad ilimitada o actividades permitidas. Si el gimnasio edita un plan para nuevas ventas, alteraría ilegalmente contratos ya pagados por alumnos vigentes.

### Decision 2: Separación entre Estado de Vigencia (`MembershipStatus`) y Saldo de Créditos
- **Adoptada**:
  - `MembershipStatus` contiene únicamente los 5 estados del ciclo de vida del contrato:
    - `Pending = 0` (`PENDING`)
    - `Active = 1` (`ACTIVE`)
    - `Expired = 2` (`EXPIRED`)
    - `Suspended = 3` (`SUSPENDED`)
    - `Cancelled = 4` (`CANCELLED`)
  - Se elimina `Depleted = 5` del enum `MembershipStatus`. Una membresía vigente con `AvailableCredits == 0 && !IsUnlimited` sigue teniendo `Status = Active` (es un contrato vigente cuyo cupo de clases se consumió, informando `HasAvailableCredits = false` e `IsDepleted = true` en los DTOs). Si el alumno cancela anticipadamente una reserva o recibe un crédito compensatorio, `AvailableCredits` pasa a `1` sin necesidad de mutar el estado de la membresía.
  - **Estado efectivo vs. estado persistido**:
    - `SUSPENDED` y `CANCELLED` son estados persistidos por acciones explícitas.
    - `EXPIRED` se persiste mediante el worker periódico (`MembershipExpirationWorker`), pero además **se deriva en tiempo real** mediante `membership.GetEffectiveStatus(DateOnly today)`: si `Status` es `Active` y `today > EndDate`, o si `Status` es `Suspended`, `today > EndDate` y la suspensión abierta **no** tiene extensión pactada (`ExtendExpirationDate == false`), el estado efectivo devuelto en consultas y autorizaciones es `Expired`. De este modo, ningún retraso del proceso programado permite accesos fuera de fecha.
    - Una membresía `Suspended` cuya suspensión abierta tiene `ExtendExpirationDate == true` **no vence mientras dure la suspensión**: su `EndDate` queda congelado y se recalcula al reactivar (Decision 3). Su estado efectivo sigue siendo `Suspended` (no habilita reservas).
- **Alternativa descartada**: Mantener `Depleted` como estado de `MembershipStatus`. Provoca inconsistencias en métricas de alumnos activos, complica las transiciones hacia `Suspended`/`Expired` y acopla el estado del contrato a cada débito/reembolso de reservas.

### Decision 3: Máquina de Estados y Trazabilidad (`MembershipStatusChanges` y `MembershipSuspensions`)

| Estado Origen | Operación / Evento | Estado Destino | Condiciones de negocio | Permiso / Actor |
|---|---|---|---|---|
| — | `Create` / `Renew` (con pago o activación inmediata) | `Active` | Alumno `ACTIVA` + rol `Student`, plan activo, sin solapamiento inválido, pago confirmado o política de activación inmediata | `MEMBERSHIPS_CREATE` / `MEMBERSHIPS_RENEW` (Staff) |
| — | `Create` / `Renew` (esperando confirmación de pago) | `Pending` | Alumno y plan válidos, pago pendiente de confirmación | `MEMBERSHIPS_CREATE` / `MEMBERSHIPS_RENEW` (Staff) |
| `Pending` | `Activate` (manual o por confirmación de `Payments`) | `Active` | `Today <= EndDate`; pago requerido cubierto o autorización administrativa con motivo; acredita créditos iniciales (`InitialCreditsLoaded = true`) | `MEMBERSHIPS_ACTIVATE` (Staff / Handler de `Payments`) |
| `Pending` | `Cancel` (manual) | `Cancelled` | Motivo obligatorio (`1..500` caracteres); anula el canje de promoción si existía | `MEMBERSHIPS_CANCEL` (Staff) |
| `Pending` | `Cancel` (automático por falta de pago) | `Cancelled` | `Today > StartDate + PendingGraceDays` o `Today > EndDate`; anula el canje de promoción si existía | Sistema (`MembershipExpirationWorker`) |
| `Active` | `Suspend` | `Suspended` | `StartDate <= Today <= EndDate`; motivo obligatorio; registra `MembershipSuspension`; cancela con devolución las reservas futuras alcanzadas por la suspensión | `MEMBERSHIPS_SUSPEND` (Staff) |
| `Suspended` | `Reactivate` (manual) | `Active` | Sin extensión: `Today <= EndDate`. Con extensión: siempre permitido (se recalcula `EndDate`). Cierra la `MembershipSuspension` activa | `MEMBERSHIPS_REACTIVATE` (Staff) |
| `Suspended` | `Reactivate` (automático al finalizar la ventana) | `Active` | La suspensión abierta tiene `EndDate` y `Today > suspension.EndDate`; aplica la extensión si estaba pactada | Sistema (`MembershipExpirationWorker`) |
| `Active`, `Suspended` (sin extensión pactada) | `Expire` (automático por fecha) | `Expired` | `Today > EndDate`; cierra suspensión abierta si existía y expira créditos remanentes en el ledger | Sistema (`MembershipExpirationWorker`) |
| `Active`, `Suspended` | `Cancel` | `Cancelled` | Motivo obligatorio; da de baja créditos remanentes en el ledger y cancela reservas futuras vinculadas | `MEMBERSHIPS_CANCEL` (Staff) |

- Toda transición inserta una fila inmutable en `MembershipStatusChanges`:
  - `Id`, `MembershipId`, `PreviousStatus` (`MembershipStatus?`), `NewStatus` (`MembershipStatus`), `Reason` (`string`, máx. 500), `Notes` (`string?`, máx. 1000), `ChangedByUserId` (`Guid?`, `null` para el worker automático), `ChangedAtUtc` (`DateTime`).
- Las suspensiones se registran además en `MembershipSuspensions`:
  - `Id`, `MembershipId`, `StartDate` (`DateOnly`), `EndDate` (`DateOnly?`), `ReactivatedAtDate` (`DateOnly?`), `ExtendExpirationDate` (`bool`, por defecto `false`), `ExtensionDaysApplied` (`int`, por defecto `0`), `Reason` (`string`, máx. 500), `Notes` (`string?`, máx. 1000), `SuspendedByUserId` (`Guid`), `SuspendedAtUtc` (`DateTime`), `ReactivatedByUserId` (`Guid?`), `ReactivatedAtUtc` (`DateTime?`), `IsActiveSuspension` (`bool`).
  - **Alcance de la suspensión**: siempre comienza el día en que se registra (`suspension.StartDate = Today`); no se admiten suspensiones programadas a futuro ni retroactivas. `suspension.EndDate` (último día suspendido, inclusive) es opcional: si se informa, el worker reactiva automáticamente al día siguiente; si no, la suspensión queda abierta hasta la reactivación manual. Sólo puede haber una suspensión abierta por membresía (`IsActiveSuspension = true`).
  - **Límite de duración**: `suspension.EndDate`, cuando se informa, cumple `Today <= EndDate <= Today + MembershipsOptions.MaxSuspensionDays - 1` (por defecto `90`). Si `ExtendExpirationDate = true`, `suspension.EndDate` es **obligatorio**, para que una membresía con vencimiento congelado nunca quede suspendida indefinidamente.
  - **Política de impacto sobre `EndDate`**: No se extiende automáticamente. Sólo si el operador envía `ExtendExpirationDate = true`, al reactivar (manual o automáticamente) se devuelven todos los días suspendidos: `ExtensionDaysApplied = (reactivationDate - suspension.StartDate).Days` y `Membership.EndDate += ExtensionDaysApplied`, donde `reactivationDate` es el primer día en que la membresía vuelve a ser utilizable (`Today` en la reactivación manual, `suspension.EndDate + 1` en la automática). Ejemplo: vence el `30/11`, se suspende el `10/11` y se reactiva el `15/11` → días `10..14` suspendidos, `ExtensionDaysApplied = 5`, nuevo `EndDate = 05/12`. El valor anterior y el nuevo se registran en `AuditLogs`.
  - **Reservas existentes al suspender**: en la misma transacción se cancelan, con devolución de crédito y sin marcarlas como tardías, las reservas futuras (`Reserved`, `Confirmed`, `WaitList`) vinculadas a la membresía cuya fecha local de clase sea `<= suspension.EndDate` (o todas las futuras si la suspensión no tiene fecha de fin), mediante el handler `CancelReservationsOnMembershipSuspendedHandler`.
  - **Elegibilidad durante la suspensión**: mientras `Status == Suspended` la membresía no habilita ninguna reserva nueva, cualquiera sea la fecha de la clase.

### Decision 4: Frontera Transaccional con `MembershipCredits` (`ICreditLedgerService`)
- **Adoptada**:
  - `Membership` deja de instanciar `MembershipCreditMovement` directamente en `MembershipService` o `ReservationService`.
  - `ICreditLedgerService` expone comandos internos del módulo de créditos que operan sobre el mismo `ISmartGymDbContext` (participando de la misma transacción EF Core) y mantienen sincronizada la proyección `Membership.AvailableCredits`:
    - `RecordInitialCreditsAsync(Membership membership, Guid? performedByUserId, CancellationToken ct)`: verifica `!membership.InitialCreditsLoaded`. Si no es ilimitado, suma `InitialCredits` con un movimiento `CreditMovementType.InitialPurchase` (o `Amount = 0` informativo si es ilimitado). Si `ExtraCreditsGranted > 0` y no es ilimitado, registra un segundo movimiento `CreditMovementType.PromotionBonus` por `ExtraCreditsGranted`. Marca `membership.InitialCreditsLoaded = true`.
    - `ConsumeCreditForReservationAsync(Membership membership, Guid classSessionId, DateOnly sessionLocalDate, string reason, Guid? performedByUserId, CancellationToken ct)`: verifica elegibilidad temporal y saldo, descuenta 1 crédito (o `0` si `IsUnlimited`) con tipo `ClassReservationDebit`, previniendo doble consumo para la misma `(MembershipId, ClassSessionId)` activa.
    - `RefundCreditForReservationAsync(Membership membership, Guid classSessionId, string reason, Guid? performedByUserId, CancellationToken ct)`: restituye 1 crédito con tipo `ClassCancellationRefund`.
    - `ForfeitRemainingCreditsOnCancelAsync(Membership membership, string reason, Guid? performedByUserId, CancellationToken ct)`: si `!membership.IsUnlimited && membership.AvailableCredits > 0`, registra un movimiento `CreditMovementType.CancellationForfeiture` por `-membership.AvailableCredits` dejando `BalanceAfter = 0` y `AvailableCredits = 0`.
    - `ExpireRemainingCreditsAsync(Membership membership, string reason, CancellationToken ct)`: si `!membership.IsUnlimited && membership.AvailableCredits > 0`, registra un movimiento `CreditMovementType.Expiration` por `-membership.AvailableCredits` dejando `BalanceAfter = 0` y `AvailableCredits = 0`.
    - `GetMembershipCreditsSummaryAsync(Guid membershipId, CancellationToken ct)`: calcula y devuelve el desglose exacto desde el ledger (`InitialCredits`, `PromotionBonusCredits`, `CompensatoryCredits`, `ManualAdjustmentCredits`, `ConsumedCredits`, `RefundedCredits`, `ExpiredOrForfeitedCredits`, `AvailableCredits`, `IsUnlimited`).
- **Alternativa descartada**: Calcular `AvailableCredits` únicamente con `SUM(Amount)` en cada `SELECT` de listado sin columna proyectada. Obligaría a hacer un `GROUP BY` sobre `MembershipCreditMovements` en cada listado paginado y filtro `hasAvailableCredits`. Mantener `AvailableCredits` como proyección transaccional actualizada exclusivamente por `ICreditLedgerService` dentro de la misma transacción combina consistencia fuerte con consultas indexadas en `O(1)`.

### Decision 5: Reglas de Consumo y Coordinación con `Reservations`
- **Momento de consumo**:
  - Cuando el alumno confirma una reserva con cupo disponible en `Reservations` (`ReservationStatus.Reserved`), se consume el crédito inmediatamente a través de `ICreditLedgerService.ConsumeCreditForReservationAsync`.
  - Si ingresa en `WaitList`, **no** se consume crédito hasta que sea promovido a `Reserved`.
- **Protección contra doble consumo**:
  - Se protege transaccionalmente con el bloqueo de fila de la sesión (`LockClassSessionForUpdateAsync`) y de las membresías del alumno (`LockStudentMembershipsForUpdateAsync`), tomado **antes de leerlas** para elegir y consumir sobre saldos actualizados, más la validación de que no exista una reserva activa del mismo alumno en la sesión. Un conflicto de concurrencia residual (`xmin`) se traduce a `409 Conflict`.
- **Promoción desde la lista de espera**:
  - Al liberarse un cupo se promueve al primero de la lista que pueda usarlo. Si se anotó con una membresía que ya no es elegible para la clase (suspendida, vencida, sin créditos), conserva su lugar en la lista y se continúa con el siguiente; nunca se impide la cancelación que liberó el cupo.
- **Cancelación de reserva y `NO_SHOW`**:
  - Si el alumno cancela con antelación suficiente (`>= 2 horas` antes de `StartsAtUtc`), `Reservations` solicita a `ICreditLedgerService.RefundCreditForReservationAsync` la devolución del crédito.
  - Si es cancelación tardía (`< 2 horas`) o el alumno incurre en `NO_SHOW`, el crédito ya debitado al reservar queda consumido (o según la política configurable `ReservationsOptions:RefundCreditOnNoShow = false`).
  - Si la clase se suspende, cancela o reprograma por el gimnasio, el handler `CancelReservationsOnSessionVoidedHandler` restituye íntegramente el crédito a través de `ICreditLedgerService`.

### Decision 6: Servicio de Elegibilidad (`IMembershipEligibilityService`)
- **Adoptada**: Se separa la consulta de elegibilidad (`EvaluateEligibilityAsync`) de la operación transaccional de reserva.
- Recibe `MembershipId` (o `StudentId`), `DateOnly? targetDate` (por defecto `_gymCalendar.Today`), `Guid? activityId` y opcionalmente `Guid? classSessionId` (si se pasa `classSessionId`, toma la actividad y la fecha local de `session.StartsAtUtc`).
- Evalúa secuencialmente y acumula todos los motivos de bloqueo en `IReadOnlyList<EligibilityIssueDto(string Code, string Message)>`:
  1. `STUDENT_NOT_ACTIVE`: La persona no está en `PersonStatus.Active` o no tiene el rol `Student` activo.
  2. `MEMBERSHIP_PENDING`: `Status == Pending`.
  3. `MEMBERSHIP_CANCELLED`: `Status == Cancelled`.
  4. `MEMBERSHIP_EXPIRED`: `GetEffectiveStatus(Today) == Expired` (vencida hoy, por estado persistido o por fecha).
  5. `OUT_OF_VALIDITY_RANGE`: la membresía no está vencida hoy pero `targetDate < StartDate` o `targetDate > EndDate`.
  6. `MEMBERSHIP_SUSPENDED`: `Status == Suspended` (bloquea cualquier `targetDate`).
  7. `ACTIVITY_NOT_ACTIVE`: la actividad indicada no está en `ActivityStatus.Active`.
  8. `ACTIVITY_NOT_ALLOWED`: `!membership.AllowsActivity(activityId.Value)`.
  9. `NO_AVAILABLE_CREDITS`: `!membership.IsUnlimited && membership.AvailableCredits <= 0`.
  10. `AGE_NOT_ALLOWED`: el alumno tiene `BirthDate` y su edad a `targetDate` no cumple `activity.IsAgeAllowed(age)`.
  11. `MEDICAL_CERTIFICATE_REQUIRED` / `UNDERAGE_EMERGENCY_CONTACT_REQUIRED`: si el alumno es menor sin contacto de emergencia válido o si la política del gimnasio exige apto médico vigente (`GymOptions.RequireValidMedicalCertificateForAccess`, por defecto `false` para no bloquear gimnasios que dan período de gracia, configurable en `appsettings.json`).
- Estos códigos son el contrato único usado por el spec, el servicio, `ReservationService` y el frontend; no existe un código genérico `MEMBERSHIP_NOT_ACTIVE`.
- **Selección de membresía para un alumno (`SelectMembershipForAsync(studentId, activityId, targetDate)`)**: un alumno puede tener más de una membresía utilizable (Decision 7). Cuando la reserva no indica `MembershipId`, se elige entre las membresías elegibles para esa actividad y fecha con este orden determinista: (1) ilimitadas antes que con créditos (no consumen saldo); (2) `EndDate` más próximo (se usa primero lo que vence antes); (3) `PurchasedAtUtc` más antiguo. `CreateReservationRequest` admite un `MembershipId` opcional para que el alumno o el personal elijan otra membresía elegible. Si ninguna es elegible, se devuelven los motivos de la candidata mejor posicionada por ese mismo orden.

### Decision 7: Renovaciones, Contrataciones Consecutivas y Regla de Solapamiento
- **Adoptada**:
  - Cada renovación (`POST /api/memberships/{id}/renew`) crea un **nuevo registro `Membership`** con `RenewedFromMembershipId = id`.
  - No se permite renovar una membresía `Cancelled` ni `Pending` (responde `409 Conflict`).
  - **Cálculo de `StartDate` en renovación**:
    - Si la membresía origen tiene `EndDate >= Today` y el request no especifica `StartDate`, se asigna `StartDate = origen.EndDate.AddDays(1)` (renovación anticipada consecutiva).
    - Si la membresía origen ya venció (`EndDate < Today`) y el request no especifica `StartDate`, se asigna `StartDate = Today`.
  - **Regla de solapamiento**:
    - Un alumno puede tener una membresía actual activa (`01/10` al `30/10`) y una renovación futura consecutiva (`31/10` al `29/11`).
    - También puede tener dos membresías simultáneas si cubren conjuntos disjuntos de actividades (p. ej. un plan sólo para "Natación" y otro sólo para "Musculación").
    - Se **rechaza con `409 Conflict`** contratar o renovar si el alumno ya posee otra membresía en estado `Pending`, `Active` o `Suspended` cuyo rango `[StartDate, EndDate]` se solape (`StartDate <= other.EndDate && EndDate >= other.StartDate`), tenga intersección de actividades permitidas y posea créditos utilizables o pendientes de acreditar (`other.HasUsableOrPendingCredits()`):
      - `other.IsUnlimited`, o
      - `other.InitialCreditsLoaded && other.AvailableCredits > 0`, o
      - `!other.InitialCreditsLoaded && (other.InitialCredits + other.ExtraCreditsGranted) > 0` (una membresía `Pending` bloquea por los créditos que recibirá al activarse).
    - Si la membresía anterior ya cargó y agotó sus créditos (`!other.IsUnlimited && other.InitialCreditsLoaded && other.AvailableCredits == 0`), la nueva contratación se admite sin ningún indicador adicional en el request: pedir un `StartDate` solapado es en sí la solicitud de inicio inmediato.
    - La respuesta `409` incluye el `Id` y el rango de la membresía superpuesta.

### Decision 8: Integración con `Payments` y `Promotions`
- **Promociones**:
  - Si `CreateMembershipRequest` o `RenewMembershipRequest` incluye `PromotionCode`, `MembershipService` invoca `IPromotionService.RedeemPromotionAsync(request, membershipId: null)` dentro de la misma transacción y **antes** de insertar la membresía, de modo que la condición `NewStudent` se evalúe sin contar la membresía en curso y no se viole la FK `PromotionRedemptions.MembershipId`.
  - Se guarda en `Membership`: `OriginalPrice = plan.Price`, `DiscountAmount = redemption.DiscountAmount`, `PurchasePrice = redemption.FinalAmount`, `ExtraCreditsGranted = redemption.ExtraCreditsGranted`, `PromotionId = redemption.PromotionId`, `PromotionRedemptionId = redemption.Id`. Una vez insertada la membresía, `IPromotionService.AttachRedemptionToMembershipAsync(redemptionId, membershipId)` completa el vínculo inverso en la misma transacción.
  - **Anulación del canje**: al cancelar (manual o automáticamente) una membresía que nunca llegó a activarse (`Pending`), `IPromotionService.VoidRedemptionAsync(redemptionId)` marca el canje como inactivo y devuelve el uso al contador de la promoción. Los canjes inactivos no cuentan para `MaxUsesPerPerson`. Cancelar una membresía que ya estuvo `Active` **no** devuelve el canje.
  - **Ajustes mínimos en `Promotions`** (única excepción al non-goal de no rediseñar el módulo): `PromotionService` pasa a usar `IGymCalendar.Today` en lugar de `DateOnly.FromDateTime(DateTime.UtcNow)`, y se agregan `AttachRedemptionToMembershipAsync` y `VoidRedemptionAsync`. No cambia el motor de reglas.
- **Pagos y Activación**:
  - Al contratar/renovar, el request puede:
    1. Incluir los datos de un pago inmediato registrado por mostrador (`InitialPayment: { Amount, Method, ExternalTransactionId?, Notes? }`), que `IPaymentService` registra como `Completed` vinculado a `MembershipId`.
    2. Referenciar un `PaymentId` previamente registrado en `Payments` para vincularlo.
    3. No incluir pago aún (`ActivateImmediately = false`), quedando la membresía en `Pending`, o solicitar `ActivateImmediately = true`.
  - **Regla de activación** (única, usada al contratar, al renovar, en `POST /{id}/activate` y al confirmarse un pago):
    - `PurchasePrice == 0` o `TotalPaidAmount >= PurchasePrice` → se activa; puede ejecutarla cualquier usuario de Staff.
    - Saldo pendiente (`TotalPaidAmount < PurchasePrice`) → sólo se activa por autorización explícita (`ActivateImmediately = true` o `POST /{id}/activate`) de un `Administrator`, con `ActivationReason` obligatorio (`1..500`), que queda en `MembershipStatusChanges` y `AuditLogs`. Un `Secretary` recibe `403`. No existe otra "configuración comercial" de cuenta corriente o cortesía: esta regla la reemplaza.
  - Una membresía admite múltiples registros en `Payments` (pagos parciales/cuotas). El detalle de la membresía expone `TotalPaidAmount` (suma de pagos `Completed`; los `Refunded`, `Pending` y `Failed` no suman), `PendingBalance = Max(0, PurchasePrice - TotalPaidAmount)`, `HasOutstandingBalance` y la colección de pagos asociados. El "pago parcial" no es un estado de `Payment`: se deriva de `PendingBalance > 0`.
  - **Vinculación de un `PaymentId` existente**: se valida que el pago exista, no esté ya vinculado a otra membresía ni a una clase, y esté en `Pending` o `Completed`; en caso contrario `409`.
  - **Ciclo de vida del pago** (ampliación mínima de `Payments`, hoy `RegisterPaymentAsync` siempre crea `Completed`): `RegisterPaymentRequest` admite `Status` inicial `Completed` (por defecto, cobro en mostrador) o `Pending` (transferencia o pasarela a confirmar); se agregan `ConfirmPaymentAsync(id)` (`Pending → Completed`) y `FailPaymentAsync(id, reason)` (`Pending → Failed`), expuestos como `POST /api/payments/{id}/confirm` y `POST /api/payments/{id}/fail` (Staff).
  - **Sin dependencia circular entre servicios**: `MembershipService` depende de `IPaymentService` (para registrar o vincular el pago inicial), pero `PaymentService` **no** depende de `IMembershipService`. La activación vive en `IMembershipActivationService.TryActivateAsync(membershipId, authorizedByUserId?, reason?)`, que no depende de `IPaymentService` (lee `Payments` desde el `DbContext`), aplica la regla de activación y es idempotente (no hace nada si la membresía no está `Pending`; la carga de créditos se protege con `InitialCreditsLoaded`). Lo invocan:
    - `MembershipService`, tras persistir la membresía como `Pending` y registrar/vincular el pago;
    - el handler `ActivateMembershipOnPaymentCompletedHandler`, ante el evento de dominio `PaymentCompletedEvent` que `Payment` emite al pasar a `Completed` (`Payment` implementa `IHasDomainEvents`). El despacho ocurre dentro de `SaveChangesAsync`, por lo que comparte la transacción.
  - **Reembolso posterior a la activación**: reembolsar un pago de una membresía ya `Active` no cambia su estado ni sus créditos; el detalle refleja `HasOutstandingBalance = true` y la decisión de cancelarla queda en manos del personal (`POST /{id}/cancel`).

### Decision 9: Transacciones, Idempotencia y Concurrencia
- **Transacción única**: `SmartGymDbContext` comparte la base de datos PostgreSQL entre todos los módulos. Contratación, renovación, activación, suspensión, reactivación y cancelación abren una transacción explícita (`BeginTransactionAsync`) cuando coordinan múltiples operaciones o bloqueos pesimistas.
- **Idempotencia en contratación/renovación**:
  - Se admite `IdempotencyKey` (header `Idempotency-Key` o propiedad `IdempotencyKey` en el body).
  - Índice único parcial en PostgreSQL: `UX_Memberships_Student_IdempotencyKey` sobre `("StudentId", "IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL`.
  - Si llega una solicitud con el mismo `(StudentId, IdempotencyKey)`, devuelve la membresía ya creada (`200 OK`) sin duplicar cargos ni créditos. Si la clave coincide pero el `MembershipPlanId` o el `StartDate` solicitados difieren de los de la membresía registrada, responde `409 Conflict` (clave reutilizada con otra solicitud).
- **Serialización por alumno y por membresía**:
  - Al contratar o renovar para un `StudentId`, en PostgreSQL se ejecuta `LockPersonForUpdateAsync(studentId)` (`SELECT 1 FROM "People" WHERE "Id" = @id FOR UPDATE`) dentro de la transacción para serializar contrataciones concurrentes del mismo alumno y garantizar que la verificación de solapamiento y la carga inicial no sufran condiciones de carrera.
  - Al activar, suspender, reactivar o cancelar una membresía, se ejecuta `LockMembershipForUpdateAsync(membershipId)` (`SELECT 1 FROM "Memberships" WHERE "Id" = @id FOR UPDATE`).
- **Concurrencia optimista (`xmin`)**:
  - `Membership` incluye `uint Version` configurado con `.IsRowVersion()` sobre la columna de sistema `xmin` de PostgreSQL. Las actualizaciones administrativas (`UpdateNotes`, `Suspend`, `Reactivate`, `Cancel`) aceptan `Version?` y cualquier `DbUpdateConcurrencyException` se traduce a `ConflictException` (`409 Conflict`).

### Decision 10: Vencimientos y Tareas Programadas (`MembershipExpirationWorker`)
- **Adoptada**: `MembershipExpirationWorker : BackgroundService` (en `SmartGym.Infrastructure/Services/BackgroundJobs`), configurable mediante `MembershipsOptions`:
  - `ExpirationCheckInterval`: por defecto `1 hora`.
  - `ExpiringWarningDays`: por defecto `7 días`.
  - `PendingGraceDays`: por defecto `7 días`.
  - `MaxSuspensionDays`: por defecto `90 días`.
- En cada ciclo, llama a `IMembershipService.ProcessExpirationsAndWarningsAsync(CancellationToken)`, que ejecuta los pasos en este orden:
  1. **Reactivación automática de suspensiones finalizadas**: busca membresías `Suspended` cuya suspensión abierta tiene `EndDate < Today`. Las reactiva con `reactivationDate = suspension.EndDate + 1`, aplicando la extensión pactada (Decision 3). Si no había extensión y la membresía ya venció, queda para el paso 2.
  2. **Transición de vencidas**: busca membresías con `EndDate < _gymCalendar.Today` y `Status == Active`, o `Status == Suspended` cuya suspensión abierta no tiene extensión pactada. Por cada una:
     - Cierra cualquier suspensión activa.
     - Cambia `Status = Expired`, registra `MembershipStatusChange` (`ChangedByUserId = null`, motivo `"Vencimiento automático por finalización de vigencia"`).
     - Invoca `ICreditLedgerService.ExpireRemainingCreditsAsync`.
     - Si `ExpiredNotificationSentAtUtc == null`, crea la notificación `NotificationType.MembershipExpired` y marca `ExpiredNotificationSentAtUtc = UtcNow`.
  3. **Cancelación automática de pendientes impagas**: busca membresías `Pending` con `Today > StartDate.AddDays(PendingGraceDays)` o `Today > EndDate`. Las pasa a `Cancelled` (`CancelledByUserId = null`, motivo `"Cancelación automática por falta de pago"`) y anula el canje de promoción si existía. No genera movimientos de créditos (nunca se cargaron).
  4. **Avisos de próximo vencimiento**: busca membresías con `Status == Active`, `EndDate >= Today && EndDate <= Today.AddDays(ExpiringWarningDays)` y `ExpiringNotificationSentAtUtc == null`, excluyendo:
     - las de duración corta (`DurationDaysSnapshot <= ExpiringWarningDays`), para no avisar "por vencer" al comprar un pase diario o semanal;
     - las que ya tienen una renovación no cancelada (existe otra membresía con `RenewedFromMembershipId == Id` y `Status != Cancelled`).

     Por cada una crea la notificación `NotificationType.MembershipExpiring` y marca `ExpiringNotificationSentAtUtc = UtcNow`.
  5. Registra en `AuditLogs` un resumen `MEMBERSHIPS_EXPIRATION_BATCH` sólo cuando hubo cambios procesados (`ReactivatedCount`, `ExpiredCount`, `AutoCancelledCount` o `WarningsSentCount` mayores a cero).
- **Alcance transaccional**: cada membresía se procesa en su propia transacción corta tomando `LockMembershipForUpdateAsync` y **revalidando la condición después del bloqueo**; un fallo en una membresía se registra y no detiene el lote. Así el proceso es seguro frente a operaciones manuales simultáneas y frente a más de una instancia de la aplicación.
- **Idempotencia**: garantizada por la revalidación de estado tras el bloqueo y las marcas `ExpiringNotificationSentAtUtc` / `ExpiredNotificationSentAtUtc`.

---

## Modelo de Dominio y Persistencia PostgreSQL

```sql
-- Tabla "Memberships" (refactorizada)
"Id" UUID PRIMARY KEY,
"StudentId" UUID NOT NULL REFERENCES "People"("Id") ON DELETE RESTRICT,
"MembershipPlanId" UUID NOT NULL REFERENCES "MembershipPlans"("Id") ON DELETE RESTRICT,
"RenewedFromMembershipId" UUID NULL REFERENCES "Memberships"("Id") ON DELETE RESTRICT,
"Status" INT NOT NULL, -- 0 Pending, 1 Active, 2 Expired, 3 Suspended, 4 Cancelled
"StartDate" DATE NOT NULL,
"EndDate" DATE NOT NULL,
"PurchasedAtUtc" TIMESTAMPTZ NOT NULL,
"ActivatedAtUtc" TIMESTAMPTZ NULL,
"DurationDaysSnapshot" INT NOT NULL,
"InitialCredits" INT NOT NULL,
"ExtraCreditsGranted" INT NOT NULL DEFAULT 0,
"TotalCredits" INT NOT NULL,
"AvailableCredits" INT NOT NULL,
"IsUnlimited" BOOLEAN NOT NULL DEFAULT FALSE,
"AppliesToAllActivities" BOOLEAN NOT NULL DEFAULT TRUE,
"InitialCreditsLoaded" BOOLEAN NOT NULL DEFAULT FALSE,
"OriginalPrice" NUMERIC(18,2) NOT NULL,
"DiscountAmount" NUMERIC(18,2) NOT NULL DEFAULT 0,
"PurchasePrice" NUMERIC(18,2) NOT NULL,
"Currency" VARCHAR(3) NOT NULL DEFAULT 'ARS',
"PromotionId" UUID NULL REFERENCES "Promotions"("Id") ON DELETE RESTRICT,
"PromotionRedemptionId" UUID NULL REFERENCES "PromotionRedemptions"("Id") ON DELETE RESTRICT,
"CreatedByUserId" UUID NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"CancelledAtUtc" TIMESTAMPTZ NULL,
"CancelledByUserId" UUID NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"CancellationReason" VARCHAR(500) NULL,
"Notes" VARCHAR(1000) NULL,
"IdempotencyKey" VARCHAR(100) NULL,
"ExpiringNotificationSentAtUtc" TIMESTAMPTZ NULL,
"ExpiredNotificationSentAtUtc" TIMESTAMPTZ NULL,
"CreatedAtUtc" TIMESTAMPTZ NOT NULL,
"UpdatedAtUtc" TIMESTAMPTZ NULL,
"IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
"xmin" XID -- Concurrencia optimista (Version)

-- Restricciones CHECK en "Memberships"
CHECK "CK_Memberships_Dates" ("EndDate" >= "StartDate" AND "DurationDaysSnapshot" >= 1)
CHECK "CK_Memberships_Status" ("Status" IN (0, 1, 2, 3, 4))
CHECK "CK_Memberships_Prices" ("OriginalPrice" >= 0 AND "DiscountAmount" >= 0 AND "PurchasePrice" >= 0)
CHECK "CK_Memberships_Credits" ("InitialCredits" >= 0 AND "ExtraCreditsGranted" >= 0 AND "TotalCredits" >= 0 AND "AvailableCredits" >= 0)
CHECK "CK_Memberships_Cancellation" (("Status" = 4) = ("CancelledAtUtc" IS NOT NULL AND "CancellationReason" IS NOT NULL))

-- Índices en "Memberships"
UNIQUE INDEX "UX_Memberships_Student_IdempotencyKey" ("StudentId", "IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL;
INDEX "IX_Memberships_Student_Status_EndDate" ("StudentId", "Status", "EndDate" DESC);
INDEX "IX_Memberships_Status_EndDate" ("Status", "EndDate");
INDEX "IX_Memberships_MembershipPlanId" ("MembershipPlanId");
INDEX "IX_Memberships_PurchasedAtUtc" ("PurchasedAtUtc" DESC);

-- Tabla "MembershipAllowedActivities" (snapshot de actividades contratadas)
"MembershipId" UUID NOT NULL REFERENCES "Memberships"("Id") ON DELETE CASCADE,
"ActivityId" UUID NOT NULL REFERENCES "Activities"("Id") ON DELETE RESTRICT,
PRIMARY KEY ("MembershipId", "ActivityId")

-- Tabla "MembershipStatusChanges" (historial inmutable de estados)
"Id" UUID PRIMARY KEY,
"MembershipId" UUID NOT NULL REFERENCES "Memberships"("Id") ON DELETE CASCADE,
"PreviousStatus" INT NULL,
"NewStatus" INT NOT NULL,
"Reason" VARCHAR(500) NOT NULL,
"Notes" VARCHAR(1000) NULL,
"ChangedByUserId" UUID NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"ChangedAtUtc" TIMESTAMPTZ NOT NULL

-- Tabla "MembershipSuspensions" (registro de suspensiones temporales)
"Id" UUID PRIMARY KEY,
"MembershipId" UUID NOT NULL REFERENCES "Memberships"("Id") ON DELETE CASCADE,
"StartDate" DATE NOT NULL,
"EndDate" DATE NULL,
"ReactivatedAtDate" DATE NULL,
"ExtendExpirationDate" BOOLEAN NOT NULL DEFAULT FALSE,
"ExtensionDaysApplied" INT NOT NULL DEFAULT 0,
"Reason" VARCHAR(500) NOT NULL,
"Notes" VARCHAR(1000) NULL,
"SuspendedByUserId" UUID NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"SuspendedAtUtc" TIMESTAMPTZ NOT NULL,
"ReactivatedByUserId" UUID NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
"ReactivatedAtUtc" TIMESTAMPTZ NULL,
"IsActiveSuspension" BOOLEAN NOT NULL DEFAULT TRUE
```

---

## Contratos API y DTOs (`/api/memberships`)

### Endpoints

| Método y Ruta | Permiso / Política | Descripción | Respuestas |
|---|---|---|---|
| `GET /api/memberships` | `MEMBERSHIPS_READ` (Staff) | Listado paginado con filtros en servidor (`studentId`, `membershipPlanId`, `status`, `startDateFrom`, `startDateTo`, `endDateFrom`, `endDateTo`, `purchasedFrom`, `purchasedTo`, `expiringWithinDays`, `hasAvailableCredits`, `sortBy`, `sortDir`, `page`, `pageSize`) | `200 OK` (`PagedResult<MembershipListItemDto>`) |
| `GET /api/memberships/{id:guid}` | Autenticado (Staff o Alumno dueño) | Detalle completo de la membresía con snapshot comercial, pagos, promoción, suspensiones e historial de estado | `200 OK` (`MembershipDetailDto`), `403`, `404` |
| `GET /api/memberships/student/{studentId:guid}` | Autenticado (Staff o Alumno dueño) | Historial de membresías de un alumno ordenado por `StartDate DESC, PurchasedAtUtc DESC` | `200 OK` (`IReadOnlyList<MembershipSummaryDto>`), `403` |
| `GET /api/memberships/student/{studentId:guid}/current` | Autenticado (Staff o Alumno dueño) | Membresías actualmente vigentes o próximas consecutivas del alumno | `200 OK` (`IReadOnlyList<MembershipSummaryDto>`), `403` |
| `GET /api/memberships/student/{studentId:guid}/active` | Autenticado (Staff o Alumno dueño) | **Se mantiene** por compatibilidad con el frontend actual (`gym-api.service.ts`), marcada como obsoleta en favor de `/current`: devuelve una única membresía utilizable para `date` (por defecto `Today`), elegida con el orden de selección de la Decision 6 | `200 OK` (`MembershipSummaryDto`), `403`, `404` |
| `GET /api/memberships/{id:guid}/eligibility` | Autenticado (Staff o Alumno dueño) | Evalúa elegibilidad sin consumir créditos para `date`, `activityId` o `classSessionId` | `200 OK` (`MembershipEligibilityDto`), `403`, `404` |
| `GET /api/memberships/{id:guid}/credits` | Autenticado (Staff o Alumno dueño) | Resumen y desglose del saldo de créditos y movimientos coordinado con `MembershipCredits` | `200 OK` (`MembershipCreditsSummaryDto`), `403`, `404` |
| `POST /api/memberships` | `MEMBERSHIPS_CREATE` (Staff) | Contrata una nueva membresía para un alumno (soporta header `Idempotency-Key` o propiedad en body) | `201 Created` (`MembershipDetailDto`), `400`, `404`, `409` |
| `PATCH /api/memberships/{id:guid}/notes` | `MEMBERSHIPS_MANAGE` (Staff) | Edita únicamente `Notes` (body `{ Notes, Version }`); no admite ningún otro campo | `200 OK` (`MembershipDetailDto`), `400`, `404`, `409` |
| `POST /api/memberships/{id:guid}/activate` | `MEMBERSHIPS_ACTIVATE` (Staff; con saldo pendiente sólo `Administrator` + `ActivationReason`) | Activa una membresía en estado `Pending` y acredita sus créditos iniciales | `200 OK` (`MembershipDetailDto`), `400`, `403`, `404`, `409` |
| `POST /api/memberships/{id:guid}/suspend` | `MEMBERSHIPS_SUSPEND` (Staff) | Suspende temporalmente una membresía `Active` | `200 OK` (`MembershipDetailDto`), `400`, `404`, `409` |
| `POST /api/memberships/{id:guid}/reactivate` | `MEMBERSHIPS_REACTIVATE` (Staff) | Reactiva una membresía `Suspended` vigente | `200 OK` (`MembershipDetailDto`), `400`, `404`, `409` |
| `POST /api/memberships/{id:guid}/cancel` | `MEMBERSHIPS_CANCEL` (Staff) | Cancela definitivamente una membresía, cierra créditos remanentes y cancela reservas futuras | `200 OK` (`MembershipDetailDto`), `400`, `404`, `409` |
| `POST /api/memberships/{id:guid}/renew` | `MEMBERSHIPS_RENEW` (Staff) | Renueva una membresía creando un nuevo período vinculado por `RenewedFromMembershipId` | `201 Created` (`MembershipDetailDto`), `400`, `404`, `409` |

### Permisos y Autorización (`RolesAndPolicies.cs`)
- Se definen las constantes de permisos y políticas en `SmartGym.Application.Common.Security`:
  - `Permissions.MembershipsRead = "MEMBERSHIPS_READ"`
  - `Permissions.MembershipsCreate = "MEMBERSHIPS_CREATE"`
  - `Permissions.MembershipsActivate = "MEMBERSHIPS_ACTIVATE"`
  - `Permissions.MembershipsSuspend = "MEMBERSHIPS_SUSPEND"`
  - `Permissions.MembershipsReactivate = "MEMBERSHIPS_REACTIVATE"`
  - `Permissions.MembershipsCancel = "MEMBERSHIPS_CANCEL"`
  - `Permissions.MembershipsRenew = "MEMBERSHIPS_RENEW"`
  - `Permissions.MembershipsManage = "MEMBERSHIPS_MANAGE"`
- En el esquema actual basado en roles (`Administrator`, `Secretary`, `Instructor`, `Student`), `Administrator` y `Secretary` (`Policies.RequireStaff`) satisfacen las políticas operativas de gestión de membresías. Las constantes nombran una política por operación para que el controlador no dependa del rol; hoy todas resuelven a `RequireStaff` y no se agregan claims `permission` al JWT en este cambio. `MEMBERSHIPS_MANAGE` cubre la edición de notas.
- Única diferencia entre roles: activar una membresía con saldo pendiente exige `Administrator` y `ActivationReason` (Decision 8).
- Queda fuera de alcance la autogestión: un alumno no contrata ni renueva por sí mismo; todas las operaciones de escritura son de Staff.
- Autorización contextual en consultas por `{id}` o `{studentId}`: si el usuario no es `Administrator` ni `Secretary`, el backend verifica que `_currentUserService.PersonId == membership.StudentId`; en caso contrario responde `403 Forbidden`.

---

## Integración con otros módulos

1. **`Auth`**: Provee `ICurrentUserService` (`UserId`, `PersonId`, `Email`, roles y claims de permisos).
2. **`People`**: Se valida que la persona exista, tenga `IsActive == true`, `Status == PersonStatus.Active` y posea el rol `Student` activo. Nunca se duplican datos personales en la tabla `Memberships`.
3. **`MembershipPlans`**: Provee la definición comercial al contratar/renovar. La FK `MembershipPlanId` usa `OnDelete(DeleteBehavior.Restrict)`, impidiendo el borrado físico de planes con historial.
4. **`MembershipCredits`**: Propietario único de `MembershipCreditMovements` y de la actualización de `Membership.AvailableCredits` mediante `ICreditLedgerService`.
5. **`Reservations`**: `ReservationService.CreateReservationAsync` utiliza `IMembershipEligibilityService` (incluida la selección de membresía cuando el request no indica `MembershipId`) y delega el débito/devolución en `ICreditLedgerService`. Al cancelarse o suspenderse una membresía, los eventos `MembershipCancelledEvent` y `MembershipSuspendedEvent` (despachados dentro de `SaveChangesAsync`, misma transacción) cancelan las reservas futuras alcanzadas liberando cupos en `ClassSessions`.
6. **`ClassSessions`**: Provee `ActivityId` y `StartsAtUtc` cuando la elegibilidad se consulta para una clase concreta.
7. **`Payments`**: Registra los cobros vinculados por `Payment.MembershipId`. Soporta múltiples pagos por membresía y emite `PaymentCompletedEvent`; la activación la resuelve `IMembershipActivationService` (Decision 8).
8. **`Promotions`**: Evalúa y redime códigos promocionales mediante `IPromotionService.EvaluatePromotionAsync` / `RedeemPromotionAsync`, y vincula o anula canjes con `AttachRedemptionToMembershipAsync` / `VoidRedemptionAsync`.
9. **`Notifications`**: Recibe las notificaciones generadas por eventos de membresía (`MembershipActivated`, `MembershipSuspended`, `MembershipExpiring`, `MembershipExpired`).
10. **`Audit`**: Registra todas las operaciones críticas mediante `IAuditService` (`MEMBERSHIP_CREATED`, `MEMBERSHIP_ACTIVATED`, `MEMBERSHIP_SUSPENDED`, `MEMBERSHIP_REACTIVATED`, `MEMBERSHIP_CANCELLED`, `MEMBERSHIP_RENEWED`, `MEMBERSHIP_NOTES_UPDATED`, `MEMBERSHIP_END_DATE_EXTENDED`).

---

## Estrategia de Pruebas

1. **Pruebas Unitarias de Dominio (`SmartGym.Domain.UnitTests/Memberships`)**:
   - Creación de `Membership` con snapshot comercial y cálculo de `EndDate` inclusivo.
   - Máquina de estados completa: transiciones válidas e inválidas (`Pending`, `Active`, `Suspended`, `Expired`, `Cancelled`), derivación de `GetEffectiveStatus(today)`.
   - Suspensiones con y sin extensión de fecha de vencimiento (`ExtendExpirationDate`).
   - Snapshot de actividades permitidas (`AllowsActivity` aislado de cambios posteriores en `MembershipPlan`).
2. **Pruebas de Configuración de Modelo y Migración**:
   - Verificación de tabla `Memberships`, `MembershipAllowedActivities`, `MembershipStatusChanges`, `MembershipSuspensions`, claves foráneas `Restrict`, restricciones `CHECK`, índice único de idempotencia y token `xmin` (`Version`).
3. **Pruebas de Aplicación e Integración (`MembershipServiceTests`, `MembershipEligibilityServiceTests`, `CreditLedgerServiceTests`)**:
   - Cubren los **16 criterios de aceptación** de la tabla siguiente, además de las reglas de suspensión con extensión, cancelación automática de pendientes y selección entre varias membresías.
4. **Pruebas de API / Controlador (`MembershipsControllerTests`)**:
   - Autorización contextual (un alumno no puede ver membresías de otro alumno → `403`), códigos HTTP (`201`, `200`, `400`, `404`, `409`) y contratos de DTOs.

### Criterios de aceptación (trazabilidad con `specs/memberships/spec.md`)

| # | Criterio | Escenario del spec |
|---|---|---|
| 1 | Personal autorizado contrata para un alumno válido | Contratación exitosa por un administrativo para un alumno válido |
| 2 | El precio y las condiciones contratadas no cambian si cambia el plan | Cambio de precio o condiciones del plan no afecta membresías vigentes |
| 3 | Una membresía `PENDING` no habilita reservas | Membresía pendiente por falta de pago confirmado |
| 4 | El acceso depende de la actividad y la fecha | Rechazo de elegibilidad por actividad no incluida o fecha fuera de vigencia |
| 5 | Una membresía vencida por fecha bloquea aunque el worker no haya corrido | Evaluación de vencimiento efectivo en tiempo real |
| 6 | Una membresía suspendida no habilita reservas | Suspensión sin extensión de vencimiento |
| 7 | Un reintento con la misma clave no duplica la contratación | Reintento HTTP con la misma clave de idempotencia |
| 8 | El saldo no puede alterarse desde `Memberships` | Imposibilidad de alterar el saldo desde actualización de membresía |
| 9 | Cancelar conserva el historial y cancela reservas futuras | Cancelación con créditos restantes y reservas futuras |
| 10 | La renovación es trazable y respeta el solapamiento | Renovación anticipada genera período consecutivo / Rechazo de solapamiento |
| 11 | Un alumno no ve membresías ajenas | Alumno intenta consultar membresías de otro alumno |
| 12 | Las promociones explican precio y créditos extra | Contratación con promoción de descuento / de créditos adicionales |
| 13 | El pago confirmado activa; el rechazado no | Activación al confirmar el pago requerido / Pago rechazado |
| 14 | Consultar elegibilidad no consume créditos | Membresía activa elegible para actividad y fecha permitidas |
| 15 | Solicitudes concurrentes del mismo alumno no duplican | Reintento HTTP con la misma clave de idempotencia (variante concurrente) |
| 16 | El worker es idempotente | Ejecución repetida del proceso de vencimientos y alertas |

---

## Migration Plan

1. En la migración `RefactorMembershipsModule`:
   - Agregar las nuevas columnas a `"Memberships"` con valores por defecto seguros para las filas existentes:
     - `PurchasedAtUtc = "CreatedAtUtc"`, `ActivatedAtUtc = "CreatedAtUtc"`.
     - `DurationDaysSnapshot = GREATEST(1, ("EndDate" - "StartDate") + 1)`.
     - `InitialCredits = "TotalCredits"`, `ExtraCreditsGranted = 0`, `InitialCreditsLoaded = TRUE`.
     - Poblar `OriginalPrice` y `PurchasePrice` desde `"MembershipPlans"."Price"`, `DiscountAmount = 0`, `Currency = 'ARS'`. Es una **aproximación** (el precio histórico real no se guardó): se deja constancia agregando a `Notes` el texto `'Precio estimado desde el plan vigente al migrar'`.
     - Copiar `IsUnlimited` y `AppliesToAllActivities` desde `"MembershipPlans"`, e insertar en `"MembershipAllowedActivities"` las filas de `"MembershipPlanActivities"` de cada membresía existente. En las ilimitadas, dejar `InitialCredits = 0` y `AvailableCredits = 0`.
     - "Hoy" en la migración es la fecha local del gimnasio: `(now() AT TIME ZONE 'America/Argentina/San_Juan')::date`, no `CURRENT_DATE` del servidor.
     - Remapear `Status = 5` (`Depleted`) a `Status = 1` (`Active`).
     - **Cerrar las ya vencidas** para que el primer ciclo del worker no las procese ni notifique en masa: toda membresía `Active` o `Suspended` con `EndDate < hoy` pasa a `Status = 2` (`Expired`); si tenía `AvailableCredits > 0`, se inserta un movimiento `Expiration` por el remanente (`BalanceAfter = 0`) y se deja `AvailableCredits = 0`; se marca `ExpiredNotificationSentAtUtc = now()` y `ExpiringNotificationSentAtUtc = now()`.
     - En las `Active` que vencen dentro de los próximos 7 días, marcar `ExpiringNotificationSentAtUtc = now()` (no se envían avisos retroactivos).
     - Si `Status = 4` (`Cancelled`), poblar `CancelledAtUtc = COALESCE("UpdatedAtUtc", "CreatedAtUtc")` y `CancellationReason = 'Cancelación previa a migración'`.
     - Insertar en `"MembershipStatusChanges"` una fila inicial por membresía existente (`PreviousStatus = NULL`, `NewStatus` = estado migrado, `Reason = 'Estado migrado'`, `ChangedByUserId = NULL`).
     - No se reescribe `EndDate` de las filas existentes (el cálculo anterior sumaba un día de más; se respeta lo ya contratado y `DurationDaysSnapshot` refleja ese rango).
   - En `"Payments"` no hay cambios de esquema; en `"PromotionRedemptions"` los canjes anulados usan la columna `IsActive` existente.
   - Cambiar la FK `"MembershipCreditMovements"."MembershipId"` de `Cascade` a `Restrict` para garantizar que nunca se borren movimientos financieros en cascada.
   - Crear las tablas `"MembershipAllowedActivities"`, `"MembershipStatusChanges"` y `"MembershipSuspensions"`, más los índices y restricciones `CHECK`.

---

## Risks / Trade-offs

- **[Riesgo] Retiro de `MembershipStatus.Depleted` en consumidores existentes** → *Mitigación*: Se actualizan en el mismo cambio `CreditLedgerService`, `ReservationService`, `DashboardService` y los tests existentes para verificar saldo mediante `AvailableCredits` / `HasAvailableCredit()` en lugar del estado `Depleted`.
- **[Riesgo] Consultas de listado con filtro por estado `Expired` cuando el worker aún no corrió** → *Mitigación*: El predicado IQueryable de `GET /api/memberships` traduce el filtro `status = Expired` a `m.Status == Expired || (m.EndDate < today && (m.Status == Active || (m.Status == Suspended && !tieneSuspensionAbiertaConExtension)))` y el filtro `status = Active` a `(m.Status == MembershipStatus.Active && m.EndDate >= today)`, garantizando exactitud total en la base de datos. Una membresía `Active` con `StartDate > today` (renovación anticipada) se lista como `Active` con el indicador `StartsInFuture = true`.
- **[Trade-off] Vigencia fija desde la contratación** → una `Pending` activada tarde pierde días. Se acepta por previsibilidad (los períodos consecutivos de renovación no se corren) y se mitiga con la activación inmediata autorizada y la cancelación automática a los `PendingGraceDays`.
- **[Trade-off] Créditos compensatorios** → se suman al saldo de la membresía y vencen con ella; la vigencia propia de 15 días del requisito "Créditos compensatorios y clases individuales" de `memberships-credits` sigue sin modelarse y queda fuera de este cambio.
- **[Riesgo] Cambio de numeración de `MembershipStatus` en el frontend** → `Pending = 0` es nuevo y `Depleted = 5` desaparece; el modelo `Membership` de Angular usa `status: number`. *Mitigación*: tareas de frontend en el mismo cambio (sección 6 de `tasks.md`).
- **[Riesgo] Los eventos de dominio sólo se despachan si el `DbContext` tiene `IServiceProvider`** → *Mitigación*: los efectos sobre reservas al suspender o cancelar se aplican únicamente desde los handlers (nunca invocados a mano desde el servicio), y las pruebas de esos flujos usan un contexto con los handlers registrados (`MembershipTestSupport`), igual que la aplicación.
- **[Riesgo] Bloqueos `FOR UPDATE` en pruebas con proveedor EF InMemory** → *Mitigación*: Al igual que `LockClassSessionForUpdateAsync`, los métodos `LockPersonForUpdateAsync` y `LockMembershipForUpdateAsync` verifican `if (!Database.IsNpgsql()) return;`, manteniendo compatibilidad total con la suite de pruebas rápidas e integridad real en PostgreSQL.
