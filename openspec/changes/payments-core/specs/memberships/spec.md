## MODIFIED Requirements

### Requirement: Integración con Pagos y política de activación
El sistema SHALL modelar la relación de una membresía con uno o múltiples registros en `Payments` (pagos totales, pagos parciales o reembolsos). `Memberships` no SHALL marcar una membresía como pagada ni activarla por indicación no verificada del frontend; la condición de pago SHALL determinarse a partir del total pagado (`TotalPaidAmount`), definido como la suma del importe neto (`Amount - RefundedAmount`) de los pagos en estado `Completed` o `PartiallyRefunded` aplicados a la membresía en `Payments`, o de la política de activación autorizada:
- Si al contratar el total pagado cubre el precio acordado (`TotalPaidAmount >= PurchasePrice`) o `PurchasePrice == 0`, la membresía se crea en estado `ACTIVE` y se acreditan sus créditos iniciales.
- Si el pago queda pendiente (`Pending`) o es insuficiente, la membresía se crea en estado `PENDING` sin acreditar créditos utilizables hasta que se cubra el precio o se ejecute la activación autorizada.
- La activación con saldo pendiente (al contratar con `ActivateImmediately = true` o mediante `POST /api/memberships/{id}/activate`) SHALL requerir un usuario `Administrator` y un motivo obligatorio (`ActivationReason`), que queda registrado en el historial de estados y en auditoría.
- Cuando `Payments` confirma un pago, o aplica a la membresía un pago ya confirmado, y con ello se completa el precio acordado, la membresía pasa de `PENDING` a `ACTIVE` y solicita a `MembershipCredits` la carga inicial de créditos (una única vez, de forma idempotente, también ante pagos concurrentes).
- El pago inicial informado al contratar y el pago existente vinculado por `PaymentId` SHALL aplicarse a la membresía a través de `Payments`, con sus mismas validaciones de titularidad, moneda y saldo pendiente.
- La vigencia (`StartDate`, `EndDate`) SHALL fijarse al contratar y no recalcularse al activar. Las contrataciones nuevas SHALL cumplir `StartDate >= Today`.
- El reembolso total o parcial de un pago de una membresía ya `ACTIVE` no SHALL cambiar su estado ni sus créditos; el detalle SHALL informar el saldo pendiente resultante.

#### Scenario: Membresía pendiente por falta de pago confirmado
- **WHEN** se registra una contratación cuyo pago queda en estado `PENDING`
- **THEN** la membresía se guarda en estado `PENDING`, no habilita reservas y aún no acredita saldo utilizable

#### Scenario: Activación al confirmar el pago requerido
- **WHEN** se registra la confirmación del pago en `Payments` o un operador autorizado ejecuta `POST /api/memberships/{id}/activate` cumpliendo las condiciones de activación
- **THEN** la membresía transiciona de `PENDING` a `ACTIVE`, se registra la carga inicial de créditos en `MembershipCredits` por única vez y se audita la transición

#### Scenario: Pago rechazado o anulado en membresía pendiente
- **WHEN** el pago asociado a una membresía `PENDING` resulta `Failed` (rechazado) o `Cancelled` (anulado)
- **THEN** la membresía permanece en `PENDING` (no habilitada) hasta que se registre un pago válido o se cancele

#### Scenario: Pago parcial no activa la membresía
- **WHEN** se registra un pago `Completed` de `10000 ARS` para una membresía `PENDING` con `PurchasePrice = 25000 ARS`
- **THEN** la membresía permanece en `PENDING` e informa `TotalPaidAmount = 10000` y `PendingBalance = 15000`

#### Scenario: Pago parcialmente devuelto cuenta por su importe neto
- **WHEN** una membresía `PENDING` con `PurchasePrice = 25000 ARS` tiene un pago de `15000 ARS` al que se le devolvieron `5000 ARS` y se registra otro pago `Completed` de `10000 ARS`
- **THEN** la membresía informa `TotalPaidAmount = 20000` y `PendingBalance = 5000`, y permanece en `PENDING`

#### Scenario: Reembolso sobre membresía activa
- **WHEN** se devuelve parcialmente un pago de una membresía `ACTIVE`
- **THEN** la membresía permanece `ACTIVE` con los mismos créditos y su detalle informa el saldo pendiente resultante

#### Scenario: Activación con saldo pendiente por un secretario
- **WHEN** un secretario intenta activar una membresía `PENDING` con saldo pendiente
- **THEN** el sistema rechaza la operación con `403 Forbidden` y la membresía permanece en `PENDING`

#### Scenario: Activación con saldo pendiente autorizada por un administrador
- **WHEN** un administrador activa una membresía `PENDING` con saldo pendiente indicando un motivo
- **THEN** la membresía pasa a `ACTIVE`, se acreditan sus créditos iniciales y el motivo queda registrado en el historial de estados y en auditoría
