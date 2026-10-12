## MODIFIED Requirements

### Requirement: Gestión de Pagos Multimétodo
El sistema SHALL registrar transacciones de pago vinculadas a membresías o clases individuales conforme a la especificación `payments`, separando medio de pago (`Cash`, `BankTransfer`, `CreditCard`, `DebitCard`, `DigitalWallet`, `Other`) y proveedor, admitiendo múltiples pagos parciales sobre una misma membresía, devoluciones parciales y totales mediante `PaymentRefund`, y los estados de transacción `Pending` (PENDIENTE), `Completed` (PAGADO), `Failed` (RECHAZADO), `Cancelled` (CANCELADO), `PartiallyRefunded` (DEVUELTO PARCIALMENTE) y `Refunded` (DEVUELTO TOTALMENTE). El pago parcial de una membresía no SHALL ser un estado de la transacción: se deriva de su saldo pendiente (importe neto de los pagos confirmados menor al importe acordado). Un pago SHALL poder registrarse como `Completed` (cobro en mostrador) o como `Pending` a la espera de confirmación, y un pago `Pending` SHALL poder confirmarse (`Completed`), rechazarse (`Failed`) o anularse (`Cancelled`). Cuando el registro, la confirmación o la aplicación de un pago en `Payments` cubre la condición de activación de una membresía en estado `PENDING`, SHALL coordinarse de forma transaccional e idempotente su activación y la acreditación inicial de créditos en `memberships`. Los ingresos de un período SHALL calcularse como la suma de los pagos confirmados en el período (`ConfirmedAtUtc`) menos las devoluciones registradas en el período, de modo que una devolución no altere los ingresos de períodos anteriores.

#### Scenario: Registro exitoso de pago de membresía
- **WHEN** un secretario registra un pago de membresía en efectivo con importe total
- **THEN** el sistema registra el comprobante de pago en estado PAGADO (`Completed`), vincula la transacción a la membresía, la activa (`ACTIVE`) si se encontraba en `PENDING` y acredita sus créditos iniciales por única vez

#### Scenario: Confirmación de un pago pendiente
- **WHEN** un secretario confirma una transferencia registrada como `Pending` que completa el importe de una membresía `PENDING`
- **THEN** el pago pasa a `Completed`, la membresía se activa y se acreditan sus créditos iniciales por única vez

#### Scenario: Rechazo de un pago pendiente
- **WHEN** un pago `Pending` vinculado a una membresía `PENDING` se rechaza
- **THEN** el pago pasa a `Failed` y la membresía permanece en `PENDING`

#### Scenario: Pago parcial de membresía
- **WHEN** se registra un pago `Completed` por un importe menor al precio acordado de una membresía `PENDING`
- **THEN** el pago queda vinculado, la membresía permanece en `PENDING` y su saldo pendiente refleja la diferencia

#### Scenario: Ingresos del período con una devolución de un cobro anterior
- **WHEN** en septiembre se confirma un pago de `30000 ARS` y en octubre se confirman pagos por `50000 ARS` y se devuelven `10000 ARS` del pago de septiembre
- **THEN** los ingresos de septiembre siguen siendo `30000 ARS` y los de octubre son `40000 ARS`
