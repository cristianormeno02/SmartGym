## MODIFIED Requirements

### Requirement: Gestión de Pagos Multimétodo
El sistema SHALL registrar transacciones de pago vinculadas a membresías, clases individuales u otros conceptos, admitiendo múltiples medios (efectivo, transferencia, tarjeta, Mercado Pago), múltiples pagos o pagos parciales asociados a una misma membresía y estados de transacción `Pending` (PENDIENTE), `Completed` (PAGADO), `Failed` (RECHAZADO) y `Refunded` (ANULADO). El pago parcial no SHALL ser un estado de la transacción: se deriva del saldo pendiente del concepto cobrado (suma de pagos `Completed` menor al importe acordado). Un pago SHALL poder registrarse como `Completed` (cobro en mostrador) o como `Pending` a la espera de confirmación, y un pago `Pending` SHALL poder confirmarse (`Completed`) o rechazarse (`Failed`). Cuando el registro o la confirmación de un pago en `Payments` cubre la condición de activación de una membresía en estado `PENDING`, SHALL coordinarse de forma transaccional e idempotente su activación y la acreditación inicial de créditos en `memberships`.

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

### Requirement: Notificaciones Multicanal Basadas en Eventos
El sistema SHALL emitir notificaciones internas y preparar el despacho por canales externos (correo electrónico, push o WhatsApp) ante eventos clave del sistema (suspensión de clases, cambio de instructor/sala, recordatorios de inicio de clase y eventos de ciclo de vida de membresía: activación, suspensión, aviso de vencimiento próximo y vencimiento efectivo), garantizando idempotencia para no enviar avisos duplicados ante ejecuciones repetidas de procesos programados.

#### Scenario: Notificación por suspensión de clase
- **WHEN** se cancela o suspende una clase concreta
- **THEN** el sistema genera automáticamente notificaciones dirigidas a todos los alumnos inscriptos detallando el motivo y la restitución de créditos

#### Scenario: Aviso único de membresía próxima a vencer
- **WHEN** el proceso programado detecta que una membresía activa vence dentro de la ventana de preaviso (por defecto 7 días)
- **THEN** `Notifications` registra una única notificación `MembershipExpiring` para el alumno y marca el envío para no repetirlo en ejecuciones subsiguientes

#### Scenario: Sin aviso para membresías ya renovadas o de corta duración
- **WHEN** el proceso programado evalúa una membresía que ya tiene una renovación no cancelada, o cuya duración total no supera la ventana de preaviso
- **THEN** no se registra la notificación `MembershipExpiring`
