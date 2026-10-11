## Purpose

Centraliza la administración de transacciones de pago multimétodo, el envío de notificaciones multicanal, agendas y calendarios adaptados por rol, fichas médicas/menores, auditoría global y métricas gerenciales.

## Requirements

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
El sistema SHALL emitir notificaciones internas y preparar el despacho por canales externos (correo electrónico, push o WhatsApp) ante eventos clave del sistema (suspensión de clases, cambio de instructor/sala, recordatorios de inicio de clase; eventos de ciclo de vida de membresía: activación, suspensión, aviso de vencimiento próximo y vencimiento efectivo; y eventos de ciclo de vida de reservas: confirmación, cancelación, ingreso a lista de espera, promoción desde lista de espera, oferta de cupo y vencimiento de oferta), garantizando idempotencia para no enviar avisos duplicados ante ejecuciones repetidas de procesos programados. Las notificaciones de reservas SHALL generarse después de confirmada la transacción de la operación que las origina, de modo que un error al construirlas o persistirlas se registre en el log operativo sin revertir la reserva. Una cancelación de reserva originada en la suspensión, cancelación o reprogramación de su clase no SHALL generar un aviso adicional al de la clase.

#### Scenario: Notificación por suspensión de clase
- **WHEN** se cancela o suspende una clase concreta
- **THEN** el sistema genera automáticamente una única notificación por alumno inscripto detallando el motivo y la restitución de créditos

#### Scenario: Notificación por promoción desde lista de espera
- **WHEN** un alumno en estado `WAITLISTED` es promovido a `CONFIRMED`, o recibe una oferta temporal de cupo, al liberarse un lugar en la clase
- **THEN** `Notifications` registra una notificación in-app para el alumno informando la confirmación de su reserva o el plazo para aceptar la oferta

#### Scenario: Aislamiento de fallos de notificación al confirmar reserva
- **WHEN** ocurre un error al registrar la notificación asociada a una reserva recién confirmada
- **THEN** el error se registra en el log operativo y la reserva permanece confirmada

#### Scenario: Aviso único de membresía próxima a vencer
- **WHEN** el proceso programado detecta que una membresía activa vence dentro de la ventana de preaviso (por defecto 7 días)
- **THEN** `Notifications` registra una única notificación `MembershipExpiring` para el alumno y marca el envío para no repetirlo en ejecuciones subsiguientes

#### Scenario: Sin aviso para membresías ya renovadas o de corta duración
- **WHEN** el proceso programado evalúa una membresía que ya tiene una renovación no cancelada, o cuya duración total no supera la ventana de preaviso
- **THEN** no se registra la notificación `MembershipExpiring`

### Requirement: Calendarios segmentados por Rol
El sistema SHALL presentar vistas de calendario adaptadas al rol del usuario: para Alumnos (sus reservas y clases disponibles), para Instructores (sus clases asignadas, reemplazos y lista de inscriptos), para Secretarios (sus turnos de control asignados) y para Administradores (agenda global del gimnasio).

#### Scenario: Consulta de calendario de instructor
- **WHEN** un usuario con rol Instructor accede a su vista de calendario
- **THEN** visualiza únicamente las clases concretas donde está asignado como instructor programado o efectivo, junto a la cantidad de alumnos inscriptos

### Requirement: Control de Certificados Médicos y Menores de Edad
El sistema SHALL registrar la vigencia de certificados médicos (con archivo adjunto en S3) pudiendo bloquear reservas ante vencimiento según configuración del gimnasio, así como exigir la vinculación de un adulto responsable para alumnos menores de edad.

#### Scenario: Bloqueo de reserva por certificado médico vencido
- **WHEN** un alumno con certificado médico expirado intenta reservar una clase y el gimnasio tiene activa la regla de bloqueo
- **THEN** el sistema rechaza la solicitud solicitando la actualización del apto físico

### Requirement: Auditoría Global y Dashboard de Métricas
El sistema SHALL registrar en una bitácora inmutable las operaciones críticas del sistema (identificador de usuario, entidad afectada, fecha/hora, operación y valores anteriores/nuevos) y calcular métricas operativas y comerciales para el dashboard administrativo.

#### Scenario: Visualización de métricas en dashboard administrativo
- **WHEN** un administrativo ingresa al panel principal
- **THEN** el sistema presenta en tiempo real la cantidad de alumnos activos, membresías vigentes, asistencia del día, porcentaje de ocupación e ingresos del período seleccionado
