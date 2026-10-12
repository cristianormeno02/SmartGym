## Context

`Payments` vive actualmente dentro del espacio `Operations` (`SmartGym.Domain/Entities/Operations/Payment.cs`, `SmartGym.Application/Modules/Operations/Services/PaymentService.cs`, `SmartGym.Infrastructure/Persistence/Configurations/Operations/PaymentConfiguration.cs` y `SmartGym.WebApi/Controllers/PaymentsController.cs`). Es consumido directamente por:
- `MembershipService` y `MembershipActivationService` (contratación con pago inicial o `PaymentId` vinculado, activación por evento pre-commit `PaymentCompletedEvent` en `ActivateMembershipOnPaymentCompletedHandler`, y cálculo de `TotalPaidAmount` / `PendingBalance`).
- `ReservationAccessResolver` y `MembershipEligibilityService` (verificación de que los pases individuales `SingleClassTicket` tengan un pago confirmado).
- `DashboardService` (cálculo de `MonthlyRevenue`).

Todos ellos leen `Payment.MembershipId` / `Payment.ClassSessionId` y filtran por `Status == Completed`. Este cambio endurece el núcleo manual de `Payments` sin cambiar esa forma de consultar. La pasarela externa queda para `payments-gateway`.

## Goals / Non-Goals

**Goals:**
- Máquina de estados estricta y devoluciones parciales/múltiples (`PaymentRefund`) sin perder el importe original.
- Aplicación validada de un pago a una única obligación, con activación de la membresía tanto al confirmar como al aplicar.
- Invariantes monetarios garantizados en PostgreSQL bajo concurrencia.
- Idempotencia en registro de pagos y devoluciones, también ante solicitudes concurrentes.
- Códigos de error estables, permisos granulares y auditoría en la misma transacción.

**Non-Goals:**
- Pasarela externa, webhooks, conciliación y discrepancias (`payments-gateway`).
- Repartir un pago entre varias obligaciones (`PaymentAllocation`).
- Precio de catálogo para clases sueltas, arqueo de caja y facturación fiscal.

## Decisions

### Decision 1 – Una obligación por pago, sin tabla `PaymentAllocation`

Un pago se aplica a lo sumo a una obligación y por su importe completo. La aplicación **es** el par `Payment.MembershipId` / `Payment.ClassSessionId` que ya existe; no se agrega tabla ni columna `AllocatedAmount`.

*Por qué:* `MembershipActivationService`, `MembershipService`, `ReservationAccessResolver`, `MembershipEligibilityService` y `DashboardService` ya consultan esas columnas. Una tabla de aplicaciones paralela crearía dos fuentes de verdad y obligaría a reescribir todos esos cálculos para sumar por aplicación en lugar de por pago. Hoy cada cobro corresponde a un único concepto, así que el reparto no tiene caso de uso.

*Consecuencias:*
- Un pago cuyo importe supera el saldo pendiente de la membresía se rechaza (`OBLIGATION_OVERPAID`) en lugar de aplicarse parcialmente. El operador registra el importe exacto.
- Se agrega `CK_Payments_SingleTarget` (`"MembershipId" IS NULL OR "ClassSessionId" IS NULL`).
- Las FK de `Payment` a `Memberships` y `ClassSessions` pasan de `SetNull` a `Restrict`: un pago no debe perder su concepto.

*Alternativa descartada:* `PaymentAllocation` con sincronización de las columnas "cuando hay una única aplicación" (propuesta original). Deja indefinido qué ven los consumidores cuando hay más de una. Si aparece el caso de uso, se introduce en un cambio propio migrando una fila por pago aplicado.

### Decision 2 – Obligación `ClassSession` sin saldo

Las clases no tienen precio de catálogo: el precio del pase individual lo indica el operador en `PurchaseSingleClassTicketRequest.Price` y se convierte directamente en el importe del pago. Por eso, para `ClassSessionId` sólo se valida existencia y que la clase no esté cancelada; no hay `OBLIGATION_OVERPAID` ni endpoint de saldo. La moneda es la del gimnasio (`GymOptions.DefaultCurrency`).

### Decision 3 – Separación de `PaymentMethod` y `PaymentProvider`

- `PaymentMethod`: `Cash = 1`, `BankTransfer = 2`, `CreditCard = 3`, `DebitCard = 4`, `DigitalWallet = 5` (reemplaza a `MercadoPago = 5` conservando el entero), `Other = 6`.
- `PaymentProvider`: `Manual = 1`. `payments-gateway` agrega `MercadoPago = 2`.
- Las filas existentes con `Method = 5` quedan como `Method = DigitalWallet`, `Provider = Manual`: fueron confirmadas a mano por un operador y no tienen una transacción real de pasarela contra la que conciliar.
- No se crea índice único sobre `ExternalTransactionId`: para pagos manuales es texto libre del operador (número de comprobante). La unicidad por proveedor externo la agrega `payments-gateway`.

### Decision 4 – Máquina de estados y `PaymentRefund`

| Valor | Estado | Descripción |
|---|---|---|
| 1 | `Pending` | Pendiente de confirmación |
| 2 | `Completed` | Confirmado sin devoluciones |
| 3 | `Failed` | Rechazado (terminal) |
| 4 | `Refunded` | Devuelto en su totalidad (terminal) |
| 5 | `Cancelled` | Pendiente anulado (terminal) |
| 6 | `PartiallyRefunded` | Confirmado con devolución parcial |

Métodos de dominio en `Payment`: `Complete(nowUtc)`, `Fail(reason, nowUtc)`, `Cancel(reason, nowUtc)`, `ApplyToMembership(id)` / `ApplyToClassSession(id)` y `RegisterRefund(amount)`. Cada uno valida la transición y lanza una excepción de dominio que `PaymentService` traduce a `PAYMENT_INVALID_TRANSITION`, `PAYMENT_ALREADY_APPLIED` o `REFUND_AMOUNT_EXCEEDED`. `Complete` deja de ser idempotente sobre un pago ya `Completed`; la idempotencia se resuelve por `Idempotency-Key`.

`PaymentRefund`: `Id`, `PaymentId`, `Amount`, `Currency`, `Reason` (obligatorio, máx. 500), `Notes`, `RequestedByUserId` (`Guid?`: nulo sólo en filas migradas), `CreatedAtUtc`, `IdempotencyKey`. Como todos los pagos son manuales, una devolución es efectiva en el momento en que se registra; no tiene estado propio. `payments-gateway` le agrega `Status` y `ExternalRefundId`.

### Decision 5 – Activación de la membresía al aplicar un pago confirmado

`PaymentCompletedEvent` sólo se emite en la transición `Pending → Completed`. Un pago ya `Completed` que se aplica después a una membresía no dispararía la activación. Se agrega `PaymentAppliedEvent(PaymentId, PayerPersonId, MembershipId, ClassSessionId, Amount, AppliedByUserId, OccurredAtUtc)` como evento pre-commit, emitido por `ApplyToMembership` cuando el pago está `Completed`, y un handler `ActivateMembershipOnPaymentAppliedHandler` que llama a `IMembershipActivationService.TryActivateAsync` igual que el de confirmación.

La ruta de `MembershipService.CreateAsync` que hoy escribe `linkedPayment.MembershipId` directamente pasa a llamar a `IPaymentService.ApplyPaymentAsync`, para compartir validaciones y auditoría.

### Decision 6 – Importe neto en los consumidores

- `MembershipActivationService.CalculateTotalCompletedPaymentsAsync` y `MembershipService` (`GetCompletedPaidAmountsByMembershipAsync` y el detalle): suman `Amount - RefundedAmount` de los pagos en `Completed` o `PartiallyRefunded`.
- `ReservationAccessResolver.ValidateSingleClassTicketPaymentAsync` y `MembershipEligibilityService`: consideran confirmado un pago en `Completed` o `PartiallyRefunded`.
- `DashboardService.MonthlyRevenue`: suma de `Amount` de los pagos con `ConfirmedAtUtc` en el período (estados `Completed`, `PartiallyRefunded`, `Refunded`) menos la suma de `PaymentRefund.Amount` con `CreatedAtUtc` en el período. Así una devolución no modifica los ingresos de meses anteriores.

Se agrega `PaymentStatusExtensions.IsConfirmed()` (o equivalente traducible por EF) para no repetir la lista de estados.

### Decision 7 – Transacciones, bloqueos y orden

`PaymentService` no abre transacciones hoy y se invoca también desde dentro de la transacción de `MembershipService.CreateAsync`. Cada operación de escritura usa la transacción ambiente si existe y, si no, abre una propia (mismo patrón que `PromotionService`).

Se agrega `LockPaymentForUpdateAsync(Guid paymentId, CancellationToken)` a `ISmartGymDbContext` (`SELECT 1 FROM "Payments" WHERE "Id" = @id FOR UPDATE`; no-op fuera de Npgsql).

**Orden global de bloqueo:** persona → clase → membresía → pago. Es compatible con el orden existente (`MembershipService.CreateAsync` bloquea la persona; reservas, suspensión y cancelación bloquean clases y luego membresía).

**Secuencia por operación:**
- *Registro:* bloquea la persona pagadora (serializa la idempotencia), busca la `Idempotency-Key`, bloquea la membresía destino si la hay, valida el saldo e inserta.
- *Confirmar, rechazar, cancelar, devolver:* lee `MembershipId` del pago sin seguimiento, bloquea esa membresía si existe, bloquea el pago y **recién entonces** carga la entidad con seguimiento. Si el `MembershipId` cargado difiere del leído al principio, responde `CONCURRENCY_CONFLICT`.
- *Aplicar:* bloquea la membresía destino, bloquea el pago y carga la entidad.

Leer después de bloquear es lo que permite que la segunda de dos devoluciones concurrentes vea el `RefundedAmount` actualizado y responda `REFUND_AMOUNT_EXCEEDED`; si se leyera antes fallaría por `xmin`. El bloqueo de la membresía antes de confirmar es lo que garantiza que dos pagos parciales concurrentes se vean entre sí al evaluar la activación: `TryActivateAsync` corre dentro de la misma transacción, con la membresía ya bloqueada.

`Version` (`xmin`) se mantiene como defensa para escrituras que no pasan por estos caminos; `DbUpdateConcurrencyException` se traduce a `CONCURRENCY_CONFLICT`.

**Restricciones en base de datos:**
- `CK_Payments_Amounts`: `"Amount" > 0 AND "RefundedAmount" >= 0 AND "RefundedAmount" <= "Amount"`.
- `CK_Payments_SingleTarget`: `"MembershipId" IS NULL OR "ClassSessionId" IS NULL`.
- `CK_PaymentRefunds_Amount`: `"Amount" > 0`.
- `UX_Payments_Payer_IdempotencyKey`: único sobre `("PayerPersonId", "IdempotencyKey")` donde `"IdempotencyKey" IS NOT NULL`.
- `UX_PaymentRefunds_Payment_IdempotencyKey`: único sobre `("PaymentId", "IdempotencyKey")` donde `"IdempotencyKey" IS NOT NULL`.

Los índices únicos son la última línea de defensa; el camino normal no los alcanza porque el bloqueo de persona o de pago serializa la búsqueda de la clave.

### Decision 8 – Idempotencia

- La clave llega sólo por la cabecera `Idempotency-Key`; el controlador la pasa al servicio. `MembershipService.CreateAsync` no envía clave para el pago inicial: la contratación ya es idempotente por su propia clave.
- Comparación de solicitud repetida: para pagos, `MembershipId`, `ClassSessionId`, `Amount`, `Method` y, si la solicitud la informa, `Currency`; para devoluciones, `Amount` y `Reason`. No se guarda un hash: los campos se comparan contra la fila existente. El estado inicial solicitado no se compara, porque el pago pudo confirmarse después y la fila ya no lo conserva.
- Repetición con los mismos datos → `200 OK` con el recurso existente; con datos distintos → `409` (`IDEMPOTENCY_KEY_MISMATCH`). La creación original responde `201 Created`.

### Decision 9 – Eventos pre-commit y post-commit

- Pre-commit (`IDomainEvent`): `PaymentCompletedEvent` y `PaymentAppliedEvent`, porque la activación de la membresía debe ser atómica con el pago.
- Post-commit (`IPostCommitDomainEvent`): `PaymentConfirmedNotificationEvent`, `PaymentFailedNotificationEvent` y `PaymentRefundedNotificationEvent`, con handlers que registran la notificación in-app. `SmartGymDbContext` ya captura y registra en el log las excepciones de los handlers post-commit sin revertir la transacción. Se agregan a `NotificationType` los valores que falten (`PaymentConfirmed` ya existe).

### Decision 10 – Superficie de API, permisos y códigos de error

| Ruta | Permiso | Comportamiento |
|---|---|---|
| `GET /api/payments` | `PAYMENTS_READ` | Paginado con filtros |
| `GET /api/payments/pending` | `PAYMENTS_READ` | Paginado, sólo `Pending` |
| `GET /api/payments/{id}` | `PAYMENTS_READ` o titular | Detalle con `RefundedAmount` y `NetAmount` |
| `GET /api/payments/student/{studentId}` (alias `person/{personId}`) | `PAYMENTS_READ` o titular | Historial del alumno |
| `GET /api/payments/{id}/refunds` | `PAYMENTS_READ` o titular | Devoluciones del pago |
| `GET /api/payments/obligations/membership/{membershipId}` | `PAYMENTS_READ` o titular | Saldo de la membresía |
| `POST /api/payments` | `PAYMENTS_CREATE` | Registra pago (`Pending` o `Completed`) con `Idempotency-Key` |
| `POST /api/payments/{id}/confirm` | `PAYMENTS_CONFIRM` | `Pending → Completed` |
| `POST /api/payments/{id}/fail` | `PAYMENTS_CONFIRM` | `Pending → Failed`, motivo obligatorio |
| `POST /api/payments/{id}/cancel` | `PAYMENTS_CANCEL` | `Pending → Cancelled`, motivo obligatorio |
| `POST /api/payments/{id}/apply` | `PAYMENTS_ALLOCATE` | Aplica un pago sin obligación |
| `POST /api/payments/{id}/refunds` | `PAYMENTS_REFUND` | Devolución parcial o total con `Idempotency-Key` |

**Permisos por rol:** `Administrator` todos; `Secretary` todos salvo `PAYMENTS_REFUND`; `Student` sin permisos de staff, accede a lo propio por titularidad (`currentUser.PersonId == PayerPersonId` o `Membership.StudentId`); `Instructor` sin acceso. No se define `PAYMENTS_MANAGE`: ningún endpoint lo necesitaría.

**Códigos de error (`PaymentErrorCodes`):** `PAYMENT_NOT_FOUND` (404), `OBLIGATION_NOT_FOUND` (404), `PAYMENT_INVALID_TRANSITION`, `PAYMENT_ALREADY_APPLIED`, `OBLIGATION_OVERPAID`, `OBLIGATION_CANCELLED`, `OBLIGATION_STUDENT_MISMATCH`, `CURRENCY_MISMATCH`, `REFUND_AMOUNT_EXCEEDED`, `IDEMPOTENCY_KEY_MISMATCH`, `CONCURRENCY_CONFLICT` (todos 409). Los errores de forma (importe, moneda, motivo faltante, dos obligaciones a la vez) son `400` de FluentValidation.

### Decision 11 – Auditoría en la misma transacción

Como en `Promotions` y `Reservations`, `PaymentService` agrega los `AuditLog` directamente a `_dbContext.AuditLogs` antes de `SaveChangesAsync`, sin `IAuditService.LogAsync`. Acciones: `PAYMENT_REGISTERED`, `PAYMENT_CONFIRMED`, `PAYMENT_FAILED`, `PAYMENT_CANCELLED`, `PAYMENT_APPLIED` y `PAYMENT_REFUNDED`.

## Risks / Trade-offs

- **[Riesgo] Un cobro por más que el saldo pendiente se rechaza** → es el comportamiento buscado para pagos manuales (el operador corrige el importe). El pago inicial de una contratación queda sujeto a la misma regla.
- **[Riesgo] Un pago `Pending` no reserva saldo, por lo que su confirmación puede rechazarse por `OBLIGATION_OVERPAID`** → el operador lo cancela. Es preferible a que un pendiente abandonado bloquee el cobro en mostrador.
- **[Riesgo] Pagos históricos marcados `Refunded` que nunca se cobraron** → el `Refund()` actual no valida el estado previo y no queda rastro de cuál era. La migración los trata como cobrados y devueltos por el total (efecto neto cero). La tarea 2.3 incluye una consulta de control para revisarlos antes de aplicar la migración en producción.
- **[Riesgo] Cambio en el cálculo de ingresos del mes** → pasa de `PaidAtUtc` de pagos `Completed` a `ConfirmedAtUtc` menos devoluciones del período. Para datos migrados `ConfirmedAtUtc = PaidAtUtc`, así que los meses pasados sólo cambian donde había pagos `Refunded`, que antes desaparecían del total.
- **[Riesgo] Ruptura en consumidores de `GET /api/payments` y `POST /api/payments/{id}/refund`** → se actualiza `gym-api.service.ts` y las pruebas existentes en el mismo cambio; se conserva `GET /api/payments/person/{personId}`.
- **[Riesgo] Las FK pasan a `Restrict`** → la compensación en memoria de `MembershipService.CompensateInMemoryOnFailureAsync` ya elimina los pagos antes que la membresía; se verifica en las pruebas existentes.

## Migration Plan

Migración `HardenPaymentsModule`:
1. Agregar a `"Payments"`: `Provider` (`integer NOT NULL DEFAULT 1`), `Currency` (`character varying(3) NOT NULL DEFAULT 'ARS'`), `RefundedAmount` (`numeric(18,2) NOT NULL DEFAULT 0`), `ConfirmedAtUtc` y `CancelledAtUtc` (`timestamp with time zone NULL`), `IdempotencyKey` (`character varying(100) NULL`).
2. Migrar datos:
   - `UPDATE "Payments" SET "ConfirmedAtUtc" = "PaidAtUtc" WHERE "Status" IN (2, 4);`
   - `UPDATE "Payments" SET "RefundedAmount" = "Amount" WHERE "Status" = 4;`
   - `UPDATE "Payments" p SET "Currency" = m."Currency" FROM "Memberships" m WHERE p."MembershipId" = m."Id";`
   - Las filas con `"Method" = 5` no se tocan: el entero pasa a significar `DigitalWallet` y `Provider` queda en `1`.
   - Si existieran filas con `MembershipId` y `ClassSessionId` a la vez, la migración falla con un mensaje explícito para corregirlas a mano (no se espera ninguna).
3. Crear `"PaymentRefunds"` e insertar una fila por cada pago con `"Status" = 4`: `Amount = Payment.Amount`, `Currency = Payment.Currency`, `Reason = 'Devolución registrada antes de la migración'`, `RequestedByUserId = NULL`, `CreatedAtUtc = COALESCE(Payment.UpdatedAtUtc, Payment.PaidAtUtc)`.
4. Agregar `CK_Payments_Amounts`, `CK_Payments_SingleTarget`, `CK_PaymentRefunds_Amount`, los dos índices únicos de idempotencia, un índice sobre `"ConfirmedAtUtc"` y otro sobre `"ClassSessionId"`.
5. Cambiar las FK de `Payments` a `Memberships` y `ClassSessions` a `ON DELETE RESTRICT`.
6. `Down`: restaurar las FK, eliminar `"PaymentRefunds"`, los índices, las restricciones y las columnas agregadas. Los pagos `PartiallyRefunded` (6) pasan a `Completed` (2) y los `Cancelled` (5) a `Failed` (3). El detalle de las devoluciones parciales se pierde.
