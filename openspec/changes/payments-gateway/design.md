## Context

`payments-core` deja `Payments` con máquina de estados estricta, una obligación por pago (`Payment.MembershipId` / `ClassSessionId`), devoluciones (`PaymentRefund`), idempotencia por `Idempotency-Key`, orden de bloqueo persona → clase → membresía → pago y `PaymentProvider` con el único valor `Manual`. Este cambio agrega el primer proveedor externo sobre ese modelo. Requiere que `payments-core` esté implementado y archivado.

Reglas del núcleo que condicionan este diseño:
- Un pago `Pending` no reserva saldo de la membresía.
- La confirmación manual que supera el saldo se rechaza (`OBLIGATION_OVERPAID`). Un cobro ya hecho por la pasarela no se puede rechazar.
- Toda operación sobre un pago lee su estado después de bloquearlo.

## Goals / Non-Goals

**Goals:**
- Pago en línea de membresías con activación automática, sin confiar en el frontend ni en el contenido de los webhooks.
- Que ningún cobro real se pierda ni se oculte: lo que no encaja queda como discrepancia visible y resoluble.
- Que ninguna llamada HTTP a la pasarela ocurra con bloqueos de base de datos tomados.

**Non-Goals:**
- Checkout de pases individuales, suscripciones, otros proveedores.
- Revertir automáticamente en el proveedor los cobros discrepantes.

## Decisions

### Decision 1 – Contrato `IPaymentProvider`

En `SmartGym.Application/Modules/Operations/Interfaces/`:
- `PaymentProvider Provider { get; }`
- `Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutContext context, CancellationToken ct)` — recibe `PaymentId` (se envía como referencia externa), importe, moneda, descripción y vencimiento; devuelve `ExternalReference` y `CheckoutUrl`.
- `Task<ExternalPaymentSnapshot?> GetPaymentAsync(string externalTransactionId, CancellationToken ct)` — estado normalizado (`Approved`, `Rejected`, `Pending`, `Refunded`, `ChargedBack`, `Unknown`), importe, moneda, importe devuelto y referencia externa.
- `Task<ExternalPaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken ct)` — para conciliar un pago que aún no tiene `ExternalTransactionId`.
- `Task<ExternalRefundResult> RequestRefundAsync(string externalTransactionId, decimal amount, string idempotencyKey, CancellationToken ct)`
- `ExternalWebhookEvent? VerifyAndParseWebhook(WebhookRequest request)` — `WebhookRequest` lleva cuerpo crudo, cabeceras y query string; devuelve `null` si la firma no es válida, o `ExternalEventId`, `ExternalTransactionId` y tipo de evento.

`IPaymentProviderResolver` resuelve por `PaymentProvider` y lanza `PROVIDER_NOT_SUPPORTED` si no hay implementación configurada. `MercadoPagoPaymentProvider` vive en `SmartGym.Infrastructure` y lee `MercadoPagoOptions` (`AccessToken`, `WebhookSecret`, `NotificationUrl`, `BackUrls`, `CheckoutExpirationMinutes` por defecto 60, `WebhookToleranceSeconds`) desde user-secrets o variables de entorno. Si las credenciales no están configuradas, el proveedor no se registra.

*La verificación recibe la solicitud completa y no sólo el cuerpo* porque la firma de Mercado Pago se calcula sobre datos de la query string y de las cabeceras además de una marca de tiempo. El formato exacto del manifiesto se confirma contra la documentación vigente al implementar (tarea 3.1).

### Decision 2 – Modelo de datos

- `PaymentProvider.MercadoPago = 2`.
- `Payment`: `ExternalReference` (`varchar(150)`, identificador del checkout en el proveedor), `CheckoutUrl` (`varchar(500)`), `ExpiresAtUtc` (`timestamptz NULL`).
- `PaymentRefund`: `Status` (`PaymentRefundStatus`: `Pending = 1`, `Completed = 2`, `Failed = 3`; las filas existentes quedan en `Completed`), `ExternalRefundId`, `CompletedAtUtc` (se rellena con `CreatedAtUtc` para las existentes), `FailureReason`.
- `PaymentWebhookEvent`: `Id`, `Provider`, `ExternalEventId`, `ExternalTransactionId`, `EventType`, `Status` (`Received`, `Processed`, `Ignored`), `PaymentId?`, `ReceivedAtUtc`, `ProcessedAtUtc`, `Detail`. No guarda el cuerpo completo de la notificación.
- `PaymentDiscrepancy`: `Id`, `PaymentId?`, `Provider`, `Type`, `Status` (`Open`, `Resolved`), `InternalAmount`, `InternalCurrency`, `InternalStatus`, `ExternalAmount`, `ExternalCurrency`, `ExternalStatus`, `ExternalTransactionId`, `DetectedAtUtc`, `ResolvedAtUtc`, `ResolvedByUserId`, `ResolutionNotes`.

Índices y restricciones:
- `UX_Payments_Provider_ExternalTx`: único sobre `("Provider", "ExternalTransactionId")` donde `"Provider" <> 1 AND "ExternalTransactionId" IS NOT NULL`. Excluye los pagos manuales, cuyo valor es texto libre.
- `UX_Payments_OpenCheckout`: único sobre `("MembershipId")` donde `"Provider" <> 1 AND "Status" = 1`. Respalda `CHECKOUT_ALREADY_OPEN`; el vencimiento lo hace efectivo el worker, y el servicio cancela en el momento un checkout ya vencido antes de crear otro.
- `UX_PaymentWebhookEvents_Provider_ExternalEventId`: único sobre `("Provider", "ExternalEventId")`.
- `UX_PaymentDiscrepancies_Open`: único sobre `("PaymentId", "Type")` donde `"Status" = 1 AND "PaymentId" IS NOT NULL`; y sobre `("Provider", "ExternalTransactionId", "Type")` donde `"Status" = 1 AND "PaymentId" IS NULL` para `UnknownPayment`.

### Decision 3 – Checkout en tres pasos

El pago interno debe existir antes de llamar al proveedor, porque su `Id` es la referencia que el proveedor devolverá en el webhook. Por eso la llamada HTTP queda *entre* dos transacciones y no antes de la primera:

1. **Transacción 1:** bloquea persona y membresía, resuelve la idempotencia, valida la obligación, calcula el saldo pendiente, cancela un checkout vencido si lo hubiera, inserta el pago `Pending` con `ExpiresAtUtc` y confirma.
2. **Sin transacción:** `CreateCheckoutAsync`.
3. **Transacción 2:** bloquea el pago; si la llamada tuvo éxito guarda `ExternalReference` y `CheckoutUrl`; si falló lo pasa a `Failed` y responde `502` (`PROVIDER_UNAVAILABLE`).

Si el proceso se interrumpe entre 1 y 3 queda un pago `Pending` sin URL, que el worker de vencimiento cancela. Un reintento con la misma `Idempotency-Key` sobre un pago sin `CheckoutUrl` repite los pasos 2 y 3.

El importe se toma siempre del saldo pendiente calculado en el servidor; la solicitud sólo lleva `MembershipId` y `Provider`.

### Decision 4 – Flujo de webhook

1. El controlador lee el cuerpo crudo y arma `WebhookRequest`. Proveedor desconocido → `404`.
2. `VerifyAndParseWebhook`. Firma inválida o fuera de tolerancia → `401` (`WEBHOOK_SIGNATURE_INVALID`) y un `LogWarning`. **No se escribe nada en la base**: guardar intentos no verificados permitiría a un tercero ocupar un `ExternalEventId` legítimo o llenar las tablas desde un endpoint anónimo. El endpoint lleva además límite de tasa.
3. Si el evento ya figura como `Processed` o `Ignored` → `200`.
4. **Sin transacción:** `GetPaymentAsync(externalTransactionId)`. Un fallo transitorio responde `503` para que el proveedor reintente.
5. **Transacción:** localiza el pago por la referencia externa del snapshot; bloquea membresía y pago; inserta el evento con `ON CONFLICT DO NOTHING` — si no insertó fila, otra copia lo está procesando o ya lo procesó → `200`; aplica la tabla de reglas; marca el evento `Processed` o `Ignored`; confirma.

**Tabla de reglas** (compartida con la conciliación en un único método `ApplyProviderSnapshot`):

| Estado en el proveedor | Pago interno | Acción |
|---|---|---|
| `Approved`, importe y moneda iguales | `Pending` | `Complete`; si el total pagado de la membresía supera el precio, además discrepancia `Overpayment` |
| `Approved` | `Completed` / `PartiallyRefunded` / `Refunded`, mismo `ExternalTransactionId` | Nada |
| `Approved` | Confirmado con otro `ExternalTransactionId` | `DuplicateCharge` |
| `Approved`, importe o moneda distintos | `Pending` | `AmountMismatch` / `CurrencyMismatch`; sigue `Pending` |
| `Approved` | `Cancelled` / `Failed` | `ApprovedAfterTerminalState` |
| `Rejected` / `Pending` | cualquiera | Nada |
| `Refunded` / `ChargedBack` | Devuelto externo mayor que las devoluciones `Completed` internas | `ExternalRefund` |
| cualquiera | No encontrado | `UnknownPayment` (sin `PaymentId`) |

*Un rechazo no pasa el pago a `Failed`*: un checkout de Mercado Pago admite varios intentos, y `Failed` es terminal. El pago sale de `Pending` sólo por aprobación, cancelación o vencimiento.

*El sobrepago se confirma*: la regla `OBLIGATION_OVERPAID` del núcleo aplica a la confirmación manual. `Payment.Complete` no la evalúa; la evalúa `PaymentService` en el camino manual. En el camino de proveedor se omite y se registra la discrepancia, porque el dinero ya fue cobrado.

### Decision 5 – Devolución externa en tres pasos

1. **Transacción 1:** bloquea membresía y pago; resuelve la idempotencia; calcula el disponible como `Amount - RefundedAmount - Σ(devoluciones Pending)`; inserta el `PaymentRefund` en `Pending`; confirma. La reserva es lo que impide que dos devoluciones concurrentes se soliciten ambas al proveedor.
2. **Sin transacción:** `RequestRefundAsync` usando el `Id` de la devolución como clave de idempotencia ante el proveedor.
3. **Transacción 2:** bloquea el pago; éxito → devolución `Completed`, `Payment.RegisterRefund(amount)`; fallo → devolución `Failed` con `FailureReason` y respuesta `502` (`PROVIDER_REFUND_FAILED`).

Una devolución que quede `Pending` por una interrupción entre 2 y 3 la resuelve la conciliación: si el importe devuelto que informa el proveedor la cubre, se completa; si no, se marca `Failed`. Las devoluciones de pagos `Manual` conservan el camino de un solo paso de `payments-core`.

`DashboardService` pasa a restar sólo las devoluciones `Completed`, por `CompletedAtUtc`.

### Decision 6 – Vencimiento

`PaymentExpirationWorker` (mismo esquema que `MembershipExpirationWorker`) busca pagos con `Provider <> Manual`, `Status = Pending` y `ExpiresAtUtc < now`, y los cancela uno por uno bajo el bloqueo habitual, releyendo el estado tras bloquear. El vencimiento también se informa al proveedor al crear el checkout, pero el sistema no depende de que éste lo respete: una aprobación tardía cae en `ApprovedAfterTerminalState`.

### Decision 7 – Conciliación y discrepancias

`POST /api/payments/{id}/reconcile`: obtiene el snapshot fuera de transacción (`GetPaymentAsync` si hay `ExternalTransactionId`, si no `FindPaymentByReferenceAsync`), luego bloquea y aplica `ApplyProviderSnapshot` y la resolución de devoluciones `Pending`. Sobre un pago `Manual` → `409` (`PAYMENT_NOT_EXTERNAL`).

Las discrepancias se insertan con `ON CONFLICT DO NOTHING` contra `UX_PaymentDiscrepancies_Open`, lo que hace idempotentes webhook y conciliación. Resolver una discrepancia sólo registra la decisión (notas, usuario, fecha): la acción correctiva la ejecuta el administrador con las operaciones normales (una devolución desde el sistema para `Overpayment` y `DuplicateCharge`) o en el panel del proveedor cuando el pago interno no admite devolución (`ApprovedAfterTerminalState`, `UnknownPayment`).

### Decision 8 – API, permisos y errores

| Ruta | Permiso | Comportamiento |
|---|---|---|
| `POST /api/payments/checkout` | `PAYMENTS_CREATE` o titular | Checkout con `Idempotency-Key` |
| `POST /api/payments/{id}/cancel` | `PAYMENTS_CANCEL`, o titular si es su checkout `Pending` | Amplía la regla del núcleo |
| `POST /api/payments/webhooks/{provider}` | `[AllowAnonymous]` + firma + límite de tasa | Decision 4 |
| `POST /api/payments/{id}/reconcile` | `PAYMENTS_RECONCILE` | Decision 7 |
| `GET /api/payments/discrepancies` | `PAYMENTS_RECONCILE` | Paginado, filtro por estado |
| `POST /api/payments/discrepancies/{id}/resolve` | `PAYMENTS_RECONCILE` | Notas obligatorias |

`PAYMENTS_RECONCILE` sólo para `Administrator`. `PaymentDto` agrega `checkoutUrl` y `expiresAtUtc`. `GET /api/payments` agrega el filtro `provider`.

Códigos nuevos en `PaymentErrorCodes`: `EXTERNAL_PAYMENT_MANUAL_CONFIRM_FORBIDDEN`, `PROVIDER_NOT_SUPPORTED`, `CHECKOUT_ALREADY_OPEN`, `PAYMENT_NOT_EXTERNAL`, `DISCREPANCY_NOT_FOUND` (404), `DISCREPANCY_ALREADY_RESOLVED` (409), `WEBHOOK_SIGNATURE_INVALID` (401), `PROVIDER_UNAVAILABLE` y `PROVIDER_REFUND_FAILED` (502).

Auditoría (misma transacción, como en el núcleo): `PAYMENT_CHECKOUT_INITIATED`, `PAYMENT_WEBHOOK_PROCESSED`, `PAYMENT_EXPIRED`, `PAYMENT_RECONCILED`, `PAYMENT_DISCREPANCY_DETECTED`, `PAYMENT_DISCREPANCY_RESOLVED`, `PAYMENT_REFUND_REQUESTED`, `PAYMENT_REFUND_FAILED`.

## Risks / Trade-offs

- **[Riesgo] Cobro aprobado sobre un pago cancelado o vencido: el alumno pagó y su membresía no se activa** → queda como discrepancia abierta y auditada; el administrador devuelve el dinero desde el panel del proveedor o registra el cobro manualmente. Automatizarlo exigiría reabrir un estado terminal; se prefiere la intervención explícita.
- **[Riesgo] Estados intermedios por interrupciones entre transacciones** (checkout sin URL, devolución `Pending`) → cada uno tiene un mecanismo de cierre: reintento idempotente, worker de vencimiento o conciliación.
- **[Riesgo] Un único checkout abierto por membresía obliga a esperar el vencimiento o cancelar para generar otro** → el titular y el personal pueden cancelarlo; el vencimiento por defecto es de 60 minutos.
- **[Riesgo] Endpoint anónimo expuesto** → verificación de firma antes de tocar la base, tolerancia de marca de tiempo contra repeticiones, límite de tasa y ninguna escritura para solicitudes no verificadas.
- **[Riesgo] Dependencia del formato de firma y de los estados de Mercado Pago** → aislados en `MercadoPagoPaymentProvider`; el resto del sistema sólo conoce el snapshot normalizado, y las pruebas usan un proveedor falso.

## Migration Plan

Migración `AddPaymentGateway` (posterior a `HardenPaymentsModule`):
1. Agregar a `"Payments"`: `ExternalReference`, `CheckoutUrl`, `ExpiresAtUtc`.
2. Agregar a `"PaymentRefunds"`: `Status` (`integer NOT NULL DEFAULT 2`), `ExternalRefundId`, `CompletedAtUtc`, `FailureReason`; `UPDATE "PaymentRefunds" SET "CompletedAtUtc" = "CreatedAtUtc"`.
3. Crear `"PaymentWebhookEvents"` y `"PaymentDiscrepancies"` con sus FK e índices.
4. Crear `UX_Payments_Provider_ExternalTx` y `UX_Payments_OpenCheckout`.
5. `Down`: eliminar tablas, índices y columnas. Sólo es seguro si no existen pagos con `Provider = 2`.

Despliegue: configurar `MercadoPagoOptions` y registrar la URL pública del webhook en el proveedor. Sin credenciales, el proveedor no se registra y el checkout responde `PROVIDER_NOT_SUPPORTED`, de modo que el cambio puede desplegarse apagado.
