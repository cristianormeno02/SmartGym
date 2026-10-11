> **Disciplina TDD**: cada tarea marcada con 🔴 comienza escribiendo las pruebas indicadas en `backend/tests/SmartGym.Domain.UnitTests` (único proyecto de pruebas del backend) y verificando que fallen por el motivo esperado (Red). Recién entonces se escribe el código mínimo de producción para ponerlas en verde (Green) y se refactoriza manteniendo la suite en verde (Refactor). Las restricciones de PostgreSQL (`CHECK`, índices únicos parciales, FKs `Restrict` y `xmin`) se verifican con tests de configuración del modelo EF Core y de inspección de la migración.
>
> **Trazabilidad**: "Criterio N" refiere a la tabla *Criterios de aceptación* de `design.md`.

## 0. Preparación

- [x] 0.1 Confirmar que el trabajo en curso del módulo de actividades (incluida la migración `ActivitiesCatalogRefactor` modificada en el árbol de trabajo) está commiteado y que `dotnet build` y `dotnet test` pasan en verde antes de empezar, para que la migración de este cambio parta de un snapshot estable.
- [x] 0.2 Agregar opciones de configuración con sus valores por defecto en `appsettings.json` y su registro en DI: `MembershipsOptions` (`ExpirationCheckInterval = 1h`, `ExpiringWarningDays = 7`, `PendingGraceDays = 7`, `MaxSuspensionDays = 90`), `GymOptions.DefaultCurrency = "ARS"` y `GymOptions.RequireValidMedicalCertificateForAccess = false`.

## 1. Dominio (`SmartGym.Domain`)

- [x] 1.1 🔴 `Memberships/MembershipDomainCreationTests.cs`: creación con snapshot comercial (`PurchasedAtUtc`, `DurationDaysSnapshot`, `InitialCredits`, `ExtraCreditsGranted`, precios, `Currency`, `IsUnlimited`, `AppliesToAllActivities`, copia de actividades), vigencia inclusiva (`EndDate = StartDate.AddDays(DurationDaysSnapshot - 1)`), estado inicial `Pending` con su fila en `MembershipStatusChanges`, y aislamiento ante cambios posteriores del plan (Criterio 2).
  → Refactorizar `Membership` y `MembershipStatus` (`Pending = 0, Active = 1, Expired = 2, Suspended = 3, Cancelled = 4`, sin `Depleted`); agregar `MembershipAllowedActivity` y `MembershipStatusChange`; `IHasDomainEvents` y `uint Version` en `Membership`.

- [x] 1.2 🔴 `Memberships/MembershipStateMachineTests.cs`: transiciones válidas (`Pending → Active`, `Pending → Cancelled`, `Active → Suspended`, `Suspended → Active`, `Active/Suspended → Expired`, `Active/Suspended → Cancelled`), rechazo con `InvalidOperationException` desde estados terminales o hacia el mismo estado, registro de cada transición y motivo obligatorio en cancelación.
  → Implementar `Activate`, `Cancel`, `Expire` y los eventos `MembershipActivatedEvent`, `MembershipCancelledEvent`, `MembershipExpiredEvent`.

- [x] 1.3 🔴 `Memberships/MembershipSuspensionTests.cs`: suspensión sólo desde `Active` vigente, una única suspensión abierta, `EndDate` obligatorio con extensión y acotado por `MaxSuspensionDays`; reactivación sin extensión conserva `EndDate`; con extensión aplica `ExtensionDaysApplied = (reactivationDate - suspension.StartDate).Days` (casos `10/11 → 15/11` = 5 días y suspensión que supera el vencimiento original).
  → Agregar `MembershipSuspension`, `Suspend`, `Reactivate(reactivationDate, …)`, `MembershipSuspendedEvent`.

- [x] 1.4 🔴 `Memberships/MembershipEffectiveStatusTests.cs`: `GetEffectiveStatus(today)` devuelve `Expired` para `Active` vencida y para `Suspended` vencida sin extensión, y `Suspended` para una suspendida con extensión pactada aunque `today > EndDate` (Criterio 5); `AllowsActivity`, `HasAvailableCredit`, `IsDepleted` y `HasUsableOrPendingCredits` (incluye `Pending` con créditos aún no acreditados).
  → Implementar esos métodos.

- [x] 1.5 Agregar los nuevos valores de `CreditMovementType` (`PromotionBonus`, `CancellationForfeiture`, `Expiration`) y `NotificationType` (`MembershipActivated`, `MembershipSuspended`, `MembershipExpired`); `Payment` implementa `IHasDomainEvents`, emite `PaymentCompletedEvent` en `Complete()` y agrega `Fail(reason)`. Cubrir con tests de dominio de `Payment`.

## 2. Persistencia y Migración (`SmartGym.Infrastructure`)

- [x] 2.1 🔴 `Memberships/MembershipModelConfigurationTests.cs`: tablas `"Memberships"`, `"MembershipAllowedActivities"`, `"MembershipStatusChanges"`, `"MembershipSuspensions"`; precisión `numeric(18,2)`, longitudes máximas, `xmin` como `IsRowVersion()`; los cinco `CHECK`; índice único parcial `UX_Memberships_Student_IdempotencyKey` e índices operacionales; FKs `Restrict` (incluida `MembershipCreditMovements.MembershipId`).
  → Actualizar/crear las configuraciones EF, exponer los nuevos `DbSet` y `LockPersonForUpdateAsync` / `LockMembershipForUpdateAsync` en `ISmartGymDbContext` y `SmartGymDbContext`.

- [x] 2.2 🔴 `Memberships/RefactorMembershipsMigrationTests.cs`: la migración crea tablas y columnas, puebla el snapshot desde `MembershipPlans` (dejando en `Notes` la marca de precio estimado), remapea `Status = 5`, **cierra las ya vencidas** (estado `Expired`, movimiento `Expiration` por el remanente, marcas de notificación enviadas), marca el aviso de las que vencen en la ventana, inserta la fila inicial de `MembershipStatusChanges`, usa la fecha local del gimnasio, cambia la FK del ledger a `Restrict` y provee un `Down` funcional.
  → Crear la migración `RefactorMembershipsModule`.

## 3. Libro de Créditos (`CreditLedgerService`)

- [x] 3.1 🔴 `Memberships/CreditLedgerInitialLoadTests.cs`: `RecordInitialCreditsAsync` acredita `InitialPurchase` y `PromotionBonus`, actualiza `TotalCredits` / `AvailableCredits`, marca `InitialCreditsLoaded` y es idempotente en una segunda invocación; ilimitadas con movimiento informativo de `Amount = 0`.
- [x] 3.2 🔴 `Memberships/CreditLedgerConsumptionTests.cs`: `ConsumeCreditForReservationAsync` y `RefundCreditForReservationAsync` mantienen la membresía `Active` con saldo `0`, soportan ilimitadas y evitan doble consumo para la misma `(MembershipId, ClassSessionId)`; `AddCompensatoryCreditAsync` deja de depender de `Depleted` y usa `IGymCalendar.Today`.
- [x] 3.3 🔴 `Memberships/CreditLedgerClosingAndSummaryTests.cs`: `ForfeitRemainingCreditsOnCancelAsync` y `ExpireRemainingCreditsAsync` dejan `BalanceAfter = 0`; `GetMembershipCreditsSummaryAsync` devuelve el desglose exacto.
  → Ampliar `ICreditLedgerService` / `CreditLedgerService` y `CreditMovementDtos.cs`; quitar de `Membership` los métodos `DeductCredit`, `RefundCredit` y `AddCompensatoryCredit` y toda creación de `MembershipCreditMovement` fuera del ledger (Criterio 8).

## 4. Módulos vecinos: Promociones y Pagos

- [x] 4.1 🔴 `Promotions/PromotionRedemptionLifecycleTests.cs`: vigencia evaluada con `IGymCalendar.Today`; `AttachRedemptionToMembershipAsync` vincula el canje; `VoidRedemptionAsync` lo inactiva, devuelve el uso al contador y deja de contar para `MaxUsesPerPerson`.
  → Ajustar `IPromotionService` / `PromotionService`.
- [x] 4.2 🔴 `Operations/PaymentLifecycleTests.cs`: registro con estado inicial `Completed` o `Pending`; `ConfirmPaymentAsync` y `FailPaymentAsync` validan el estado de origen (`409` si no es `Pending`); el evento `PaymentCompletedEvent` se emite una sola vez.
  → Ajustar `IPaymentService` / `PaymentService`, DTOs y `PaymentsController` (`POST /api/payments/{id}/confirm`, `/fail`).

## 5. Aplicación — Memberships (`SmartGym.Application`)

- [x] 5.1 🔴 `Memberships/MembershipEligibilityServiceTests.cs`: un test por código (`STUDENT_NOT_ACTIVE`, `MEMBERSHIP_PENDING`, `MEMBERSHIP_CANCELLED`, `MEMBERSHIP_EXPIRED`, `OUT_OF_VALIDITY_RANGE`, `MEMBERSHIP_SUSPENDED`, `ACTIVITY_NOT_ACTIVE`, `ACTIVITY_NOT_ALLOWED`, `NO_AVAILABLE_CREDITS`, `AGE_NOT_ALLOWED`, `MEDICAL_CERTIFICATE_REQUIRED`, `UNDERAGE_EMERGENCY_CONTACT_REQUIRED`), acumulación de varios motivos, caso elegible con créditos e ilimitado, y ausencia de efectos secundarios (Criterios 3, 4, 5, 6, 14).
  → Crear `IMembershipEligibilityService`, `MembershipEligibilityService` y `MembershipEligibilityDto`.
- [x] 5.2 🔴 `Memberships/MembershipSelectionTests.cs`: `SelectMembershipForAsync` elige ilimitadas primero, luego `EndDate` más próximo, luego contratación más antigua; respeta un `MembershipId` explícito; devuelve motivos cuando ninguna es elegible.
- [x] 5.3 🔴 `Memberships/MembershipActivationServiceTests.cs`: activa con precio cubierto o `PurchasePrice == 0`; con saldo pendiente exige `Administrator` y motivo (`403` para `Secretary`); pago parcial no activa; es idempotente y carga créditos una única vez; el handler `ActivateMembershipOnPaymentCompletedHandler` activa al confirmarse el pago y un pago `Failed` no activa (Criterio 13).
  → Crear `IMembershipActivationService`, su implementación y el handler.
- [x] 5.4 🔴 `Memberships/MembershipPurchaseTests.cs`: contratación para alumno `ACTIVA` con rol `Student` y plan activo (Criterio 1); rechazos por persona bloqueada/inactiva, plan inactivo y `StartDate < Today`; nace `Pending` o `Active` según la regla de activación; pago inicial registrado o `PaymentId` vinculado (rechazo si ya está vinculado a otro concepto); reversión atómica si falla un paso.
  → Reescribir `IMembershipService` / `MembershipService.CreateAsync` y `MembershipDtos.cs`.
- [x] 5.5 🔴 `Memberships/MembershipPromotionIntegrationTests.cs`: descuento (`OriginalPrice`, `DiscountAmount`, `PurchasePrice`), créditos extra (`PromotionBonus` al activar), canje evaluado antes de insertar la membresía y vinculado después (Criterio 12).
- [x] 5.6 🔴 `Memberships/MembershipIdempotencyTests.cs`: misma clave devuelve la misma membresía sin duplicar créditos; misma clave con otro plan o fecha responde `409`; solicitudes concurrentes del mismo alumno se serializan con `LockPersonForUpdateAsync` (Criterios 7 y 15).
- [x] 5.7 🔴 `Memberships/MembershipOverlapTests.cs`: rechazo por solapamiento con membresía con créditos, ilimitada o `Pending` con créditos por acreditar; admisión con actividades disjuntas, períodos consecutivos y membresía anterior agotada; el `409` informa la membresía superpuesta.
- [x] 5.8 🔴 `Memberships/MembershipRenewalTests.cs`: nueva entidad con `RenewedFromMembershipId`, inicio consecutivo en renovación anticipada, inicio hoy desde vencida, cambio de plan con precio vigente, rechazo al renovar `Pending` o `Cancelled` (Criterio 10).
  → Implementar `RenewAsync`.
- [x] 5.9 🔴 `Memberships/MembershipSuspendReactivateServiceTests.cs`: validaciones de estado y vigencia, registro de `MembershipSuspension` y `MembershipStatusChange`, auditoría del cambio de `EndDate`, y cancelación con devolución de las reservas dentro de la ventana (o todas las futuras si es abierta) conservando las posteriores.
  → Implementar `SuspendAsync`, `ReactivateAsync` y `CancelReservationsOnMembershipSuspendedHandler`.
- [x] 5.10 🔴 `Memberships/MembershipCancelServiceTests.cs`: motivo obligatorio, baja de créditos por el ledger, cancelación de reservas futuras liberando cupo, historial intacto (Criterio 9); cancelar una `Pending` anula el canje de promoción y cancelar una ya activada no.
  → Implementar `CancelAsync` y `CancelFutureReservationsOnMembershipCancelledHandler`.
- [x] 5.11 🔴 `Memberships/MembershipQueriesTests.cs`: `GetPagedAsync` con todos los filtros (estado efectivo `Expired` y `Active`, fechas, `expiringWithinDays`, `hasAvailableCredits`), detalle con pagos (`TotalPaidAmount`, `PendingBalance`, `HasOutstandingBalance`), historial, `current` y `active`; `UpdateNotesAsync` sólo cambia `Notes`; `Version` desactualizada produce `ConflictException`.

## 6. Proceso programado, integración transversal y WebApi

- [x] 6.1 🔴 `Memberships/MembershipExpirationProcessTests.cs`: reactivación automática de suspensiones finalizadas (con y sin extensión), vencimiento de `Active` y de `Suspended` sin extensión, no vencimiento de `Suspended` con extensión, cancelación automática de `Pending` impagas, avisos excluyendo pases cortos y membresías ya renovadas, y ejecución repetida sin duplicados (Criterio 16).
  → Implementar `ProcessExpirationsAndWarningsAsync` (una transacción por membresía con bloqueo y revalidación) y `MembershipExpirationWorker : BackgroundService`, registrado en DI.
- [x] 6.2 🔴 `Reservations/ReservationMembershipIntegrationTests.cs`: `ReservationService` usa `IMembershipEligibilityService` y la selección de membresía, acepta `MembershipId` opcional en `CreateReservationRequest`, bloquea por cada motivo de inelegibilidad y consume/devuelve créditos por `ICreditLedgerService`.
  → Actualizar `ReservationService`, `CancelReservationsOnSessionVoidedHandler` y `DashboardService` (métricas con estado efectivo, sin `Depleted`).
- [x] 6.3 🔴 `Memberships/MembershipsControllerTests.cs`: autorización contextual (alumno propio vs. ajeno → `403`, Criterio 11), instructor sin acceso de gestión, activación con saldo pendiente sólo `Administrator`, mapeo de códigos HTTP en todos los endpoints incluido `PATCH /notes` y lectura del header `Idempotency-Key`.
  → Actualizar `RolesAndPolicies.cs`, reescribir `MembershipsController.cs` y registrar servicios y handlers en DI.

## 7. Frontend (Angular)

- [x] 7.1 Actualizar `core/models/gym.models.ts` (`Membership` con los nuevos campos, estado efectivo, `isDepleted`, numeración `0..4` de estados) y `core/services/gym-api.service.ts` (`/current`, detalle, elegibilidad y créditos; `/active` se sigue usando hasta migrar las vistas).
- [x] 7.2 Ajustar los portales `admin`, `staff` y `student` que muestran membresías para usar el estado efectivo, el indicador de saldo agotado y el saldo pendiente; verificar con `ng build` y las pruebas existentes del frontend.

## 8. Verificación integral

- [x] 8.1 Ejecutar `dotnet build` y `dotnet test` sobre toda la solución y `ng build` en el frontend, verificando que no quedan referencias a `MembershipStatus.Depleted` y que los 16 criterios de aceptación de `design.md` tienen al menos un test en verde.
- [x] 8.2 Aplicar la migración sobre una copia de la base con datos existentes y comprobar que el primer ciclo del worker no genera notificaciones ni movimientos para membresías históricas.
- [x] 8.3 Ejecutar `openspec validate memberships --strict`.
