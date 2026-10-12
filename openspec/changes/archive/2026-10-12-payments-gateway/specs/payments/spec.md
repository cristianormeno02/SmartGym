## ADDED Requirements

### Requirement: Proveedores externos y prohibición de confirmación manual
El sistema SHALL admitir pagos procesados por un proveedor externo (`PaymentProvider.MercadoPago`) además de los manuales, desacoplando la integración mediante el contrato `IPaymentProvider`.
- Un pago de proveedor externo sólo SHALL crearse mediante el checkout y siempre en estado `Pending`. `POST /api/payments` sólo SHALL registrar pagos `Manual`.
- Un pago de proveedor externo sólo SHALL confirmarse a partir de información verificada del proveedor (webhook o conciliación). `POST /api/payments/{id}/confirm` y `POST /api/payments/{id}/fail` sobre un pago externo SHALL rechazarse con `409 Conflict` (`EXTERNAL_PAYMENT_MANUAL_CONFIRM_FORBIDDEN`).
- La base de datos SHALL impedir dos pagos del mismo proveedor externo con el mismo `ExternalTransactionId`.
- Las credenciales y secretos del proveedor no SHALL almacenarse en la base de datos ni exponerse en respuestas o logs.
- Si se solicita un proveedor no configurado, el sistema SHALL responder `409 Conflict` (`PROVIDER_NOT_SUPPORTED`).

#### Scenario: Confirmación manual de un pago externo
- **WHEN** un secretario ejecuta `POST /api/payments/{id}/confirm` sobre un pago `Pending` con `Provider = MercadoPago`
- **THEN** el sistema rechaza la operación con `409 Conflict` (`EXTERNAL_PAYMENT_MANUAL_CONFIRM_FORBIDDEN`) y el pago permanece `Pending`

### Requirement: Checkout externo de una membresía
El sistema SHALL permitir iniciar el pago en línea de una membresía mediante `POST /api/payments/checkout`, indicando la membresía y el proveedor:
- El importe SHALL determinarse en el servidor como el saldo pendiente de la membresía; la solicitud no SHALL poder indicar un importe. Si el saldo pendiente es `0`, se rechaza con `409 Conflict` (`OBLIGATION_OVERPAID`).
- SHALL aplicarse las mismas validaciones de obligación que al registrar un pago (existencia, no `CANCELLED`, titularidad y moneda).
- El sistema SHALL crear un pago `Pending` del proveedor aplicado a la membresía, con fecha de vencimiento (`ExpiresAtUtc`), y devolver la URL de pago del proveedor.
- SHALL existir a lo sumo un checkout abierto (pago externo `Pending` no vencido) por membresía; un segundo intento se rechaza con `409 Conflict` (`CHECKOUT_ALREADY_OPEN`).
- El checkout SHALL admitir la cabecera `Idempotency-Key` con las mismas reglas que el registro de pagos, devolviendo el mismo pago y la misma URL ante un reintento.
- Si el proveedor no responde o rechaza la creación, el pago SHALL quedar `Failed` y el sistema SHALL responder `502 Bad Gateway` (`PROVIDER_UNAVAILABLE`).
- El checkout SHALL poder iniciarlo un usuario con `PAYMENTS_CREATE` o el alumno titular de la membresía. El alumno titular SHALL poder cancelar su propio checkout `Pending`.

#### Scenario: Alumno inicia el checkout de su membresía pendiente
- **WHEN** un alumno con una membresía `PENDING` de `60000 ARS` y un pago `Completed` de `20000 ARS` ejecuta `POST /api/payments/checkout` para esa membresía
- **THEN** se crea un pago `Pending` con `Provider = MercadoPago` y `Amount = 40000`, y la respuesta incluye la URL de pago

#### Scenario: Alumno intenta iniciar el checkout de una membresía ajena
- **WHEN** un alumno ejecuta `POST /api/payments/checkout` para una membresía de otro alumno
- **THEN** el sistema responde `403 Forbidden` sin crear ningún pago

#### Scenario: Segundo checkout con otro abierto
- **WHEN** se solicita un checkout para una membresía que ya tiene un pago externo `Pending` no vencido, sin repetir su `Idempotency-Key`
- **THEN** el sistema responde `409 Conflict` (`CHECKOUT_ALREADY_OPEN`)

#### Scenario: Proveedor no disponible
- **WHEN** el proveedor no responde al crear el checkout
- **THEN** el pago queda `Failed` y el sistema responde `502 Bad Gateway` (`PROVIDER_UNAVAILABLE`)

### Requirement: Vencimiento de checkouts pendientes
Un proceso programado SHALL cancelar los pagos externos `Pending` cuya fecha `ExpiresAtUtc` haya pasado, registrando el motivo de vencimiento sin usuario responsable y la entrada correspondiente en `AuditLogs`. El proceso SHALL ser idempotente y seguro ante ejecuciones simultáneas.

#### Scenario: Checkout abandonado
- **WHEN** un checkout no se paga antes de `ExpiresAtUtc` y corre el proceso programado
- **THEN** el pago pasa a `Cancelled` con motivo de vencimiento y la membresía admite un nuevo checkout

### Requirement: Procesamiento verificado e idempotente de webhooks
El endpoint `POST /api/payments/webhooks/{provider}` SHALL ser anónimo y:
- Verificar la firma del proveedor antes de cualquier otro procesamiento. Una notificación con firma ausente, inválida o con marca de tiempo fuera de tolerancia SHALL rechazarse con `401 Unauthorized` (`WEBHOOK_SIGNATURE_INVALID`) sin escribir en la base de datos; el intento se registra sólo en el log operativo.
- Responder `404 Not Found` para un proveedor desconocido.
- Registrar cada evento verificado en `PaymentWebhookEvents` con unicidad por `(Provider, ExternalEventId)`. Un evento ya registrado SHALL responder `200 OK` sin volver a procesarse, también cuando dos copias llegan simultáneamente.
- Antes de modificar un pago, consultar al proveedor el estado real, el importe y la moneda de la transacción; el contenido de la notificación no SHALL usarse como fuente de esos datos.
- Responder `200 OK` a todo evento verificado y procesado, haya producido o no un cambio, y `5xx` sólo ante fallos transitorios, para que el proveedor reintente.

Según el estado informado por el proveedor, el sistema SHALL:
- **Aprobado**, pago interno `Pending`, importe y moneda coincidentes: confirmar el pago (`Completed`), guardar `ExternalTransactionId` y `ConfirmedAtUtc`, y coordinar la activación de la membresía.
- **Aprobado**, pago interno ya `Completed` con el mismo `ExternalTransactionId`: no hacer nada.
- **Rechazado, pendiente o en proceso**: no cambiar el estado del pago, que permanece `Pending` hasta su aprobación o vencimiento, dado que el pagador puede reintentar en el mismo checkout.
- **Cualquier otra combinación**: registrar una discrepancia sin modificar importes ni estados confirmados (ver "Conciliación y discrepancias").

#### Scenario: Rechazo de webhook con firma inválida
- **WHEN** se recibe una solicitud en `POST /api/payments/webhooks/mercadopago` con firma inválida o ausente
- **THEN** el sistema responde `401 Unauthorized`, no modifica ningún pago y no registra ningún evento en `PaymentWebhookEvents`

#### Scenario: Confirmación legítima por webhook verificado
- **WHEN** el proveedor notifica la aprobación de un checkout `Pending` y, al consultarlo, informa el mismo importe y moneda
- **THEN** el pago pasa a `Completed` con `ExternalTransactionId` y `ConfirmedAtUtc`, el evento queda registrado como procesado y la membresía se activa si quedó cubierta

#### Scenario: Webhook de aprobación recibido dos veces
- **WHEN** el proveedor envía dos veces la misma notificación de aprobación, en forma secuencial o simultánea
- **THEN** el pago se confirma una sola vez, la membresía se activa una sola vez y ambas solicitudes reciben `200 OK`

#### Scenario: Rechazo de tarjeta no cierra el checkout
- **WHEN** el proveedor notifica un intento rechazado y luego la aprobación de un segundo intento sobre el mismo checkout
- **THEN** tras la primera notificación el pago sigue `Pending` y tras la segunda pasa a `Completed`

### Requirement: Devoluciones de pagos externos
Una devolución sobre un pago de proveedor externo SHALL ejecutarse contra el proveedor:
- La devolución SHALL registrarse primero en estado `Pending`, reservando su importe: el importe disponible para devolver SHALL calcularse como `Amount - RefundedAmount` menos las devoluciones `Pending` del pago, y una solicitud que lo supere se rechaza con `409 Conflict` (`REFUND_AMOUNT_EXCEEDED`).
- Luego SHALL solicitarse el reintegro al proveedor. Si lo acepta, la devolución pasa a `Completed`, guarda `ExternalRefundId` y `CompletedAtUtc`, y actualiza `Payment.RefundedAmount` y el estado del pago. Si lo rechaza o no responde, la devolución pasa a `Failed` con el motivo, el pago no se modifica y el sistema responde `502 Bad Gateway` (`PROVIDER_REFUND_FAILED`).
- Sólo las devoluciones `Completed` SHALL contar en `RefundedAmount` y en los ingresos del período (por `CompletedAtUtc`).
- Una devolución que quede `Pending` por una interrupción SHALL resolverse mediante la conciliación del pago.
- Las devoluciones de pagos `Manual` SHALL seguir registrándose directamente en `Completed`.

#### Scenario: Devolución parcial aceptada por el proveedor
- **WHEN** un administrador devuelve `10000 ARS` de un pago externo `Completed` de `40000 ARS` y el proveedor acepta el reintegro
- **THEN** la devolución queda `Completed` con `ExternalRefundId`, y el pago queda `PartiallyRefunded` con `RefundedAmount = 10000`

#### Scenario: Devolución rechazada por el proveedor
- **WHEN** el proveedor rechaza el reintegro
- **THEN** la devolución queda `Failed`, el pago conserva su estado y `RefundedAmount`, y el sistema responde `502 Bad Gateway` (`PROVIDER_REFUND_FAILED`)

#### Scenario: Dos devoluciones externas concurrentes
- **WHEN** sobre un pago externo de `20000 ARS` se solicitan simultáneamente dos devoluciones de `15000 ARS`
- **THEN** sólo una se solicita al proveedor y la otra se rechaza con `409 Conflict` (`REFUND_AMOUNT_EXCEEDED`)

### Requirement: Conciliación y discrepancias
El sistema SHALL proveer `POST /api/payments/{id}/reconcile` (permiso `PAYMENTS_RECONCILE`) para contrastar un pago de proveedor externo con el estado informado por el proveedor, aplicando las mismas reglas que el procesamiento de webhooks y resolviendo las devoluciones `Pending` del pago. Sobre un pago `Manual` SHALL responder `409 Conflict` (`PAYMENT_NOT_EXTERNAL`).

Tanto el webhook como la conciliación SHALL registrar una entrada en `PaymentDiscrepancies`, con los valores internos y externos, sin modificar importes ni estados confirmados, en los siguientes casos:
- `AmountMismatch` / `CurrencyMismatch`: el proveedor informa un cobro aprobado por otro importe o moneda. El pago permanece `Pending`.
- `ApprovedAfterTerminalState`: el proveedor informa un cobro aprobado sobre un pago interno `Cancelled` o `Failed` (incluido el vencido).
- `Overpayment`: un cobro aprobado y coincidente se confirma, pero deja a la membresía con un total pagado mayor al precio acordado (por ejemplo, porque entretanto se cobró en mostrador). El pago SHALL confirmarse igualmente.
- `DuplicateCharge`: el proveedor informa un cobro aprobado con un `ExternalTransactionId` distinto del ya confirmado para ese pago.
- `ExternalRefund`: el proveedor informa una devolución o contracargo que no corresponde a una devolución registrada en el sistema.
- `UnknownPayment`: el proveedor informa una transacción cuya referencia no corresponde a ningún pago interno.

Cada discrepancia SHALL tener estado `Open` o `Resolved`. No SHALL existir más de una discrepancia `Open` del mismo tipo para el mismo pago, de modo que repetir un webhook o una conciliación no las duplique. `GET /api/payments/discrepancies` SHALL listarlas de forma paginada con filtro por estado, y `POST /api/payments/discrepancies/{id}/resolve` SHALL marcarlas como resueltas con notas obligatorias, usuario y fecha; ambas con permiso `PAYMENTS_RECONCILE`, reservado al **Administrador**. La detección y la resolución SHALL registrarse en `AuditLogs`.

#### Scenario: Conciliación confirma un pago cuyo webhook se perdió
- **WHEN** se concilia un pago externo `Pending` y el proveedor informa aprobación por el mismo importe y moneda
- **THEN** el pago pasa a `Completed` y la membresía se activa si quedó cubierta; una segunda conciliación no produce cambios

#### Scenario: Discrepancia de importe sin sobrescritura
- **WHEN** se concilia un pago interno de `25000 ARS` y el proveedor informa que la transacción se cobró por `20000 ARS`
- **THEN** el pago conserva su importe y su estado `Pending`, y se registra una discrepancia `AmountMismatch` abierta con ambos importes

#### Scenario: Cobro aprobado después de cancelar el pago
- **WHEN** llega la aprobación verificada de un checkout cuyo pago interno ya está `Cancelled`
- **THEN** el pago permanece `Cancelled`, la membresía no se activa y se registra una discrepancia `ApprovedAfterTerminalState`

#### Scenario: Cobro en línea después de un cobro en mostrador
- **WHEN** una membresía de `60000 ARS` tiene un checkout abierto por `60000 ARS`, secretaría registra un pago en efectivo `Completed` de `60000 ARS` y luego llega la aprobación verificada del checkout
- **THEN** el pago externo pasa a `Completed`, la membresía permanece `ACTIVE` y se registra una discrepancia `Overpayment`

#### Scenario: Conciliación repetida no duplica discrepancias
- **WHEN** se concilia dos veces un pago con una diferencia de importe
- **THEN** existe una única discrepancia `AmountMismatch` abierta para ese pago

#### Scenario: Secretario intenta conciliar
- **WHEN** un usuario con rol `Secretary` ejecuta `POST /api/payments/{id}/reconcile` o consulta `GET /api/payments/discrepancies`
- **THEN** el backend responde `403 Forbidden`

#### Scenario: Resolución de una discrepancia
- **WHEN** un administrador ejecuta `POST /api/payments/discrepancies/{id}/resolve` con notas
- **THEN** la discrepancia pasa a `Resolved` con usuario, fecha y notas, y queda registrada en `AuditLogs`
