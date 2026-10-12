## Purpose

Administra el ciclo de vida financiero de los cobros y reintegros del gimnasio: registro de pagos con separación entre medio de pago y proveedor, máquina de estados explícita, aplicación validada a una obligación comercial (`Membership` o `ClassSession`), devoluciones parciales o totales (`PaymentRefund`), consulta de saldo de membresías, idempotencia, control transaccional bajo concurrencia en PostgreSQL, autorización contextual por rol y auditoría.

## ADDED Requirements

### Requirement: Modelo de dominio, separación de medio y proveedor e integridad de importes
El sistema SHALL representar cada operación financiera mediante la entidad `Payment`, separando el medio de pago (`PaymentMethod`: `Cash`, `BankTransfer`, `CreditCard`, `DebitCard`, `DigitalWallet`, `Other`) del proveedor procesador (`PaymentProvider`, cuyo único valor es `Manual` mientras no se integre una pasarela externa).
- Cada `Payment` SHALL registrar al pagador (`PayerPersonId`), importe bruto recibido (`Amount > 0`), importe acumulado devuelto (`RefundedAmount`, con `0 <= RefundedAmount <= Amount`), moneda explícita en formato ISO 4217 (`Currency`, por defecto la moneda configurada del gimnasio), estado (`Status`), referencia externa opcional (`ExternalTransactionId`), marcas temporales en UTC (`CreatedAtUtc`, `PaidAtUtc`, `ConfirmedAtUtc`, `CancelledAtUtc`), usuario operador (`RegisteredByUserId`), clave de idempotencia (`IdempotencyKey`), notas y versión de concurrencia optimista (`Version` / `xmin`).
- El importe neto del pago SHALL ser `NetAmount = Amount - RefundedAmount`.
- El sistema no SHALL almacenar números completos de tarjetas (PAN) ni códigos de seguridad (CVV/CVC) en la base de datos, las respuestas de API ni los logs.
- Ningún importe monetario SHALL representarse con tipos de punto flotante; todos SHALL persistirse con precisión decimal exacta (`numeric(18,2)`).
- Un pago no SHALL eliminarse físicamente.

#### Scenario: Registro de pago conserva moneda, medio, proveedor e importes exactos
- **WHEN** un secretario registra un cobro presencial con tarjeta de débito por `25000.00 ARS`
- **THEN** el pago persiste `Amount = 25000.00`, `Currency = "ARS"`, `Method = DebitCard`, `Provider = Manual`, `RefundedAmount = 0.00` y `NetAmount = 25000.00`

#### Scenario: Rechazo de importe inválido
- **WHEN** se intenta registrar un pago con `Amount <= 0` o con una moneda que no es un código de 3 letras
- **THEN** el sistema rechaza la operación con `400 Bad Request` sin persistir el pago

### Requirement: Máquina de estados del pago
El sistema SHALL gestionar explícitamente los siguientes estados en `PaymentStatus`:
- `Pending = 1`: operación registrada a la espera de confirmación.
- `Completed = 2`: cobro confirmado sin devoluciones (`RefundedAmount == 0`).
- `Failed = 3`: operación rechazada antes de su confirmación.
- `Refunded = 4`: cobro confirmado devuelto en su totalidad (`RefundedAmount == Amount`).
- `Cancelled = 5`: operación pendiente anulada antes de completarse.
- `PartiallyRefunded = 6`: cobro confirmado con devolución parcial (`0 < RefundedAmount < Amount`).

Las transiciones permitidas SHALL ser únicamente:
- Creación → `Pending` o `Completed` (cobro confirmado por el operador en el acto).
- `Pending → Completed`: confirmación por un operador autorizado. Registra `ConfirmedAtUtc`.
- `Pending → Failed`: rechazo con motivo obligatorio.
- `Pending → Cancelled`: anulación con motivo obligatorio. Registra `CancelledAtUtc`.
- `Completed → PartiallyRefunded` o `Refunded`, y `PartiallyRefunded → PartiallyRefunded` o `Refunded`: exclusivamente al registrarse una devolución (`PaymentRefund`), según el acumulado devuelto sea menor o igual a `Amount`.

Los estados `Failed`, `Cancelled` y `Refunded` SHALL ser terminales. Cualquier otra transición SHALL rechazarse con `409 Conflict` (`PAYMENT_INVALID_TRANSITION`).

#### Scenario: Prohibición de rechazar o cancelar un pago confirmado
- **WHEN** se intenta ejecutar `POST /api/payments/{id}/fail` o `POST /api/payments/{id}/cancel` sobre un pago en estado `Completed` o `PartiallyRefunded`
- **THEN** el sistema rechaza la operación con `409 Conflict` (`PAYMENT_INVALID_TRANSITION`) y conserva el estado e importes del pago

#### Scenario: Anulación de un pago pendiente
- **WHEN** un operador autorizado ejecuta `POST /api/payments/{id}/cancel` con un motivo sobre un pago en estado `Pending`
- **THEN** el pago pasa a `Cancelled`, registra `CancelledAtUtc` y el motivo, y no activa ninguna membresía ni habilita ninguna reserva

#### Scenario: Confirmación de un pago que no está pendiente
- **WHEN** se intenta ejecutar `POST /api/payments/{id}/confirm` sobre un pago en estado `Completed`, `Failed`, `Cancelled`, `PartiallyRefunded` o `Refunded`
- **THEN** el sistema rechaza la operación con `409 Conflict` (`PAYMENT_INVALID_TRANSITION`)

### Requirement: Aplicación de un pago a una obligación comercial
Un pago SHALL aplicarse a lo sumo a una obligación comercial, identificada por `Payment.MembershipId` o `Payment.ClassSessionId` (nunca ambos), y siempre por su importe completo. La aplicación SHALL poder indicarse al registrar el pago o posteriormente mediante `POST /api/payments/{id}/apply` sobre un pago `Pending` o `Completed` aún no aplicado. Una vez aplicado, el pago no SHALL reasignarse a otra obligación.

Al aplicar un pago a una **membresía**, el sistema SHALL validar:
- Que la membresía existe (`404`, `OBLIGATION_NOT_FOUND`) y no está `CANCELLED` (`409`, `OBLIGATION_CANCELLED`). Una membresía `EXPIRED` con saldo pendiente SHALL poder recibir pagos.
- Que pertenece al pagador (`Membership.StudentId == Payment.PayerPersonId`; `409`, `OBLIGATION_STUDENT_MISMATCH`).
- Que la moneda coincide (`Membership.Currency == Payment.Currency`; `409`, `CURRENCY_MISMATCH`).
- Que el importe del pago no supera el saldo pendiente de la membresía (`409`, `OBLIGATION_OVERPAID`). El saldo pendiente SHALL calcularse como `PurchasePrice` menos el importe neto de los pagos `Completed` y `PartiallyRefunded` aplicados a ella. Los pagos `Pending` no SHALL reservar saldo; la validación SHALL repetirse al confirmar un pago `Pending`, de modo que una confirmación que supere el saldo pendiente se rechaza con `409 Conflict` (`OBLIGATION_OVERPAID`) y el pago permanece `Pending`.

El precio exigible de una membresía SHALL provenir exclusivamente del snapshot comercial preservado por `Memberships` (`PurchasePrice`); `Payments` no SHALL recalcular precios de planes ni promociones. Una misma membresía SHALL poder cubrirse mediante múltiples pagos parciales.

Al aplicar un pago a una **clase**, el sistema SHALL validar que la clase existe (`404`, `OBLIGATION_NOT_FOUND`) y no está cancelada (`409`, `OBLIGATION_CANCELLED`). Las clases no tienen precio de catálogo, por lo que no se valida saldo.

#### Scenario: Cobertura de una membresía mediante dos pagos parciales
- **WHEN** una membresía `PENDING` con `PurchasePrice = 60000 ARS` recibe un pago `Completed` en efectivo de `30000 ARS` y luego un segundo pago `Completed` por transferencia de `30000 ARS`
- **THEN** tras el primer pago la membresía sigue `PENDING` con saldo pendiente `30000`; tras el segundo el saldo pendiente es `0` y la membresía se activa por única vez

#### Scenario: Rechazo de pago que supera el saldo pendiente
- **WHEN** se intenta registrar un pago de `20000 ARS` aplicado a una membresía cuyo saldo pendiente es `10000 ARS`
- **THEN** el sistema rechaza la operación con `409 Conflict` (`OBLIGATION_OVERPAID`) sin persistir el pago

#### Scenario: Confirmación tardía de un pago pendiente que ya no cabe en el saldo
- **WHEN** una membresía de `60000 ARS` tiene un pago `Pending` de `60000 ARS`, se registra otro pago `Completed` de `60000 ARS` y luego se intenta confirmar el primero
- **THEN** el sistema rechaza la confirmación con `409 Conflict` (`OBLIGATION_OVERPAID`) y el primer pago permanece `Pending`

#### Scenario: Rechazo de vinculación simultánea a membresía y clase
- **WHEN** una solicitud indica `MembershipId` y `ClassSessionId` a la vez
- **THEN** el sistema rechaza la operación con `400 Bad Request`

#### Scenario: Rechazo de aplicación a una membresía ajena
- **WHEN** se intenta aplicar un pago a una membresía de un alumno distinto de `PayerPersonId`
- **THEN** el sistema rechaza la operación con `409 Conflict` (`OBLIGATION_STUDENT_MISMATCH`)

#### Scenario: Rechazo de reasignación de un pago ya aplicado
- **WHEN** se ejecuta `POST /api/payments/{id}/apply` sobre un pago que ya tiene `MembershipId` o `ClassSessionId`
- **THEN** el sistema rechaza la operación con `409 Conflict` (`PAYMENT_ALREADY_APPLIED`)

### Requirement: Consulta de saldo de una membresía
El sistema SHALL exponer `GET /api/payments/obligations/membership/{membershipId}` devolviendo: precio acordado (`PurchasePrice`) y moneda, total neto cobrado (`TotalPaidAmount`: suma de `NetAmount` de los pagos `Completed` y `PartiallyRefunded`), total en pagos `Pending` (`PendingPaymentsAmount`), saldo pendiente (`PendingBalance = max(0, PurchasePrice - TotalPaidAmount)`) y el detalle de pagos y devoluciones vinculados.

#### Scenario: Saldo tras una devolución parcial
- **WHEN** una membresía de `60000 ARS` tiene un pago `Completed` de `60000 ARS` y se le devuelven `10000 ARS`
- **THEN** la consulta informa `TotalPaidAmount = 50000`, `PendingBalance = 10000` y lista el pago en `PartiallyRefunded` con su devolución

### Requirement: Coordinación desacoplada con Memberships y Reservations e inmunidad ante fallos de notificación
El módulo `Payments` SHALL registrar el hecho financiero y emitir eventos de dominio sin mutar directamente el estado de membresías, reservas ni saldos de créditos:
- Al confirmarse un pago (`Completed`) aplicado a una membresía, `Payments` SHALL emitir `PaymentCompletedEvent` dentro de la transacción para que `Memberships` evalúe la activación y la carga inicial de créditos.
- Al aplicarse a una membresía un pago que ya estaba `Completed`, `Payments` SHALL emitir `PaymentAppliedEvent` dentro de la transacción, con el mismo efecto.
- Un pago `Pending`, `Failed` o `Cancelled` nunca SHALL activar una membresía ni habilitar una reserva con pase individual.
- Las notificaciones al alumno originadas por pagos (confirmación, rechazo, devolución) SHALL procesarse después de confirmada la transacción, de modo que cualquier falla en `Notifications` se registre en el log operativo sin revertir el pago ni la activación de la membresía.

#### Scenario: Aplicar un pago ya confirmado activa la membresía
- **WHEN** existe un pago `Completed` de `25000 ARS` sin aplicar y se lo aplica mediante `POST /api/payments/{id}/apply` a una membresía `PENDING` del mismo alumno con `PurchasePrice = 25000 ARS`
- **THEN** el pago queda vinculado a la membresía y ésta pasa a `ACTIVE` con sus créditos iniciales acreditados por única vez

#### Scenario: Falla en el servicio de notificaciones no revierte un cobro confirmado
- **WHEN** se confirma un pago que completa el saldo de una membresía y el registro de la notificación falla
- **THEN** el pago permanece en `Completed` y la membresía en `ACTIVE`, registrándose el error en el log operativo

### Requirement: Devoluciones parciales y totales con trazabilidad e invariante de tope
El sistema SHALL gestionar las devoluciones mediante la entidad `PaymentRefund` (`POST /api/payments/{id}/refunds`), vinculada al `Payment` original:
- Sólo SHALL admitirse devoluciones sobre pagos en estado `Completed` o `PartiallyRefunded` (`409`, `PAYMENT_INVALID_TRANSITION` en otro caso).
- Toda devolución SHALL exigir un importe `Amount > 0` y un motivo obligatorio (`Reason`), y registrar el usuario que la solicita y la fecha.
- La suma de devoluciones de un pago nunca SHALL superar `Payment.Amount`; una devolución que la supere se rechaza con `409 Conflict` (`REFUND_AMOUNT_EXCEEDED`). Al registrarse una devolución, `Payment.RefundedAmount` se incrementa y el estado pasa a `PartiallyRefunded` o `Refunded`.
- La devolución no SHALL borrar ni modificar `Payment.Amount`, ni desvincular el pago de su obligación.
- Una devolución sobre un pago de una membresía ya `ACTIVE` no SHALL cancelar ni suspender la membresía ni descontar créditos; el saldo pendiente de la membresía reflejará la diferencia.
- `GET /api/payments/{id}/refunds` SHALL listar las devoluciones del pago.

#### Scenario: Devolución parcial conserva el pago original
- **WHEN** un administrador registra una devolución de `10000 ARS` con motivo "Descuento comercial post-venta" sobre un pago `Completed` de `30000 ARS`
- **THEN** se crea un `PaymentRefund` por `10000 ARS`, el pago conserva `Amount = 30000`, actualiza `RefundedAmount = 10000` y pasa a `PartiallyRefunded`

#### Scenario: Devolución por el saldo restante
- **WHEN** sobre ese mismo pago se registra una segunda devolución de `20000 ARS`
- **THEN** el pago queda con `RefundedAmount = 30000` en estado `Refunded` y conserva ambas devoluciones en su historial

#### Scenario: Dos devoluciones concurrentes no pueden superar el importe disponible
- **WHEN** un pago de `20000 ARS` ya tiene `5000 ARS` devueltos y se envían dos solicitudes simultáneas de devolución por `10000 ARS` cada una con distintas claves de idempotencia
- **THEN** el bloqueo de fila serializa ambas operaciones: la primera deja `RefundedAmount = 15000` y la segunda se rechaza con `409 Conflict` (`REFUND_AMOUNT_EXCEEDED`)

### Requirement: Idempotencia en cobros y devoluciones
`POST /api/payments` y `POST /api/payments/{id}/refunds` SHALL admitir la cabecera `Idempotency-Key` (hasta 100 caracteres):
- En el registro de pagos la clave es única por pagador. Si se repite con los mismos datos (`MembershipId`, `ClassSessionId`, `Amount`, `Method` y, cuando se informa, `Currency`), el sistema SHALL devolver `200 OK` con el pago ya registrado, sin crear otro.
- En las devoluciones la clave es única por pago. Si se repite con el mismo `Amount` y `Reason`, el sistema SHALL devolver `200 OK` con la devolución ya registrada.
- Si la clave se repite con datos distintos, el sistema SHALL responder `409 Conflict` (`IDEMPOTENCY_KEY_MISMATCH`).
- El comportamiento SHALL ser el mismo ante solicitudes secuenciales o concurrentes.

#### Scenario: Reintento HTTP al registrar un pago con la misma clave
- **WHEN** el operador envía dos veces `POST /api/payments` con la misma `Idempotency-Key` e idénticos datos de cobro
- **THEN** se persiste un único registro en `Payments` y ambas respuestas devuelven el mismo `PaymentDto`

#### Scenario: Clave reutilizada con otro importe
- **WHEN** se envía `POST /api/payments` con una `Idempotency-Key` ya usada por el mismo pagador pero con otro `Amount`
- **THEN** el sistema responde `409 Conflict` (`IDEMPOTENCY_KEY_MISMATCH`) sin crear un pago

#### Scenario: Dos registros concurrentes con la misma clave
- **WHEN** llegan a la vez dos `POST /api/payments` idénticos con la misma `Idempotency-Key` para una membresía `PENDING`
- **THEN** se persiste un único pago, la membresía se activa una sola vez y ninguna de las dos solicitudes responde con error

### Requirement: Consistencia bajo concurrencia
El sistema SHALL serializar en PostgreSQL las operaciones que modifican un mismo pago (confirmación, rechazo, cancelación, aplicación y devolución) mediante bloqueo de fila, leyendo el estado del pago después de adquirir el bloqueo. Las operaciones que afectan el saldo de una membresía SHALL bloquear primero la membresía y luego el pago. Las escrituras basadas en una versión desactualizada SHALL responder `409 Conflict` (`CONCURRENCY_CONFLICT`).

#### Scenario: Dos pagos parciales concurrentes activan la membresía una sola vez
- **WHEN** una membresía `PENDING` de `60000 ARS` tiene dos pagos `Pending` de `30000 ARS` y ambos se confirman simultáneamente
- **THEN** ambos pagos quedan `Completed` y la membresía queda `ACTIVE` con una única carga inicial de créditos

### Requirement: Consultas paginadas, autorización contextual y auditoría financiera
El sistema SHALL ejecutar las consultas de pagos en PostgreSQL con paginación y filtros en servidor:
- `GET /api/payments` SHALL devolver un resultado paginado con filtros `studentId`, `membershipId`, `classSessionId`, `status`, `method`, `fromUtc` y `toUtc`. `GET /api/payments/pending` SHALL devolver los pagos en `Pending`.
- `GET /api/payments/{id}` y `GET /api/payments/student/{studentId}` (con el alias `person/{personId}`) SHALL devolver el detalle y el historial del alumno, incluyendo `RefundedAmount` y `NetAmount`.

La autorización SHALL aplicarse por permiso (`PAYMENTS_READ`, `PAYMENTS_CREATE`, `PAYMENTS_CONFIRM`, `PAYMENTS_CANCEL`, `PAYMENTS_ALLOCATE`, `PAYMENTS_REFUND`):
- Un **Alumno** (`Student`) sólo SHALL poder consultar sus propios pagos, las devoluciones de éstos y el saldo de sus propias membresías. La consulta de datos de otro alumno, y cualquier otra operación que esta especificación no habilite expresamente al titular, SHALL responder `403 Forbidden`.
- Un **Instructor** (sin rol de personal) no SHALL acceder a endpoints de `Payments` (`403 Forbidden`).
- Un **Secretario** (`Secretary`) SHALL poder consultar, registrar, confirmar, rechazar, cancelar y aplicar pagos (`PAYMENTS_READ`, `PAYMENTS_CREATE`, `PAYMENTS_CONFIRM`, `PAYMENTS_CANCEL`, `PAYMENTS_ALLOCATE`), pero no emitir devoluciones (`403 Forbidden`).
- Un **Administrador** (`Administrator`) SHALL poseer todos los permisos del módulo, incluyendo `PAYMENTS_REFUND`.

Toda operación financiera (registro, confirmación, rechazo, cancelación, aplicación y devolución) SHALL registrar una entrada en `AuditLogs` dentro de la misma transacción con usuario operador, entidad, identificadores, importes, moneda, valores anteriores y nuevos y motivo.

#### Scenario: Alumno intenta consultar pagos de otro alumno
- **WHEN** un usuario autenticado con rol `Student` solicita `GET /api/payments/student/{otroStudentId}` o `GET /api/payments/{idDePagoAjeno}`
- **THEN** el backend rechaza la solicitud con `403 Forbidden`

#### Scenario: Secretario intenta registrar una devolución
- **WHEN** un usuario con rol `Secretary` envía `POST /api/payments/{id}/refunds`
- **THEN** el backend rechaza la solicitud con `403 Forbidden`

#### Scenario: Listado paginado con filtros
- **WHEN** un secretario consulta `GET /api/payments?status=Pending&page=1&pageSize=20`
- **THEN** el servidor devuelve a lo sumo 20 pagos en `Pending` junto con el total de resultados

#### Scenario: Auditoría de una devolución
- **WHEN** un administrador registra una devolución
- **THEN** existe una entrada `PAYMENT_REFUNDED` en `AuditLogs` con el importe, el motivo, el usuario y los valores anterior y nuevo de `RefundedAmount` y `Status`, y esa entrada no existe si la devolución se revierte
