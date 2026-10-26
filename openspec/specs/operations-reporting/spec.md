## Purpose

Centraliza la administración de transacciones de pago multimétodo, el envío de notificaciones multicanal, agendas y calendarios adaptados por rol, fichas médicas/menores, auditoría global y métricas gerenciales.

## Requirements

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

### Requirement: Notificaciones Multicanal Basadas en Eventos
El sistema SHALL emitir notificaciones internas (`NotificationMessage`) y encolar de forma persistente y asíncrona el despacho por canales externos (`NotificationDelivery` para correo electrónico, push o WhatsApp) conforme a la especificación `notifications`, ante eventos confirmados del sistema:
- Eventos de clases: suspensión (`ClassSuspended`), cancelación (`ClassCancelled`), reprogramación (`ClassRescheduled`), cambio de sala (`ClassRoomChanged`), cambio de instructor (`ClassInstructorChanged`) y recordatorios programados de inicio de clase (`ClassReminder`).
- Eventos de ciclo de vida de membresía y créditos: activación (`MembershipActivated`), suspensión (`MembershipSuspended`), reactivación (`MembershipReactivated`), aviso de vencimiento próximo (`MembershipExpiring`), vencimiento efectivo (`MembershipExpired`) y créditos compensatorios (`CreditsCompensated`).
- Eventos de ciclo de vida de reservas: confirmación (`ReservationConfirmed`), cancelación (`ReservationCancelled`), ingreso a lista de espera (`ReservationWaitlisted`), promoción desde lista de espera (`ReservationPromotedFromWaitlist`), oferta de cupo (`WaitlistSpotOffered`) y vencimiento de oferta (`WaitlistOfferExpired`).
- Eventos de pagos y promociones: pago confirmado (`PaymentConfirmed`), pago rechazado (`PaymentFailed`), reembolso (`PaymentRefunded`) y beneficio o promoción disponible (`PromotionAvailable`).

El procesamiento SHALL garantizar idempotencia por destinatario y evento (`IdempotencyKey`) para no generar notificaciones lógicas ni entregas duplicadas ante reprocesamiento o ejecuciones repetidas de procesos programados, aplicar las preferencias por categoría y canal en el backend y ejecutarse después de confirmada la transacción de negocio (post-commit) de modo que un error de notificación o de proveedor externo se registre en el log operativo sin revertir reservas, cambios de clase, membresías ni pagos. Una cancelación de reserva originada en la suspensión, cancelación o reprogramación de su clase (`CancellationSource == ClassSessionChange`) no SHALL generar un aviso adicional al de la clase.

#### Scenario: Notificación por suspensión de clase
- **WHEN** se suspende, cancela, reprograma o cambia de sala una clase concreta
- **THEN** tras confirmarse la transacción de la clase, `notifications` genera automáticamente una única notificación por alumno inscripto (y para el adulto responsable si el alumno es menor en un grupo familiar activo) con el `NotificationType` correspondiente y encola las entregas externas habilitadas sin que un fallo de envío revierta el cambio de clase

#### Scenario: Notificación por promoción desde lista de espera
- **WHEN** un alumno en estado `WAITLISTED` es promovido a `CONFIRMED`, o recibe una oferta temporal de cupo, al liberarse un lugar en la clase
- **THEN** `notifications` registra una notificación in-app idempotente para el alumno informando la confirmación de su reserva o el plazo local para aceptar la oferta junto con su `ActionUrl` interna

#### Scenario: Aislamiento de fallos de notificación al confirmar reserva
- **WHEN** ocurre un error al registrar la notificación o al despachar un canal externo tras confirmarse una reserva, una transición de membresía o un pago
- **THEN** el error se registra en el log operativo / estado de entrega y la operación de negocio permanece confirmada

#### Scenario: Aviso único de membresía próxima a vencer
- **WHEN** el proceso programado detecta que una membresía activa vence dentro de la ventana de preaviso (por defecto 7 días)
- **THEN** `notifications` registra una única notificación `MembershipExpiring` idempotente para el alumno y marca el envío para no repetirlo en ejecuciones subsiguientes

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
El sistema SHALL registrar en una bitácora inmutable las operaciones críticas del sistema (identificador de usuario, entidad afectada, fecha/hora, operación y valores anteriores/nuevos) y calcular métricas operativas y comerciales para el dashboard administrativo. Todo importe monetario exhibido en el dashboard de métricas (como los ingresos del mes) SHALL mostrarse con 2 decimales, coma (`,`) como separador de decimales y punto (`.`) como separador de miles (ej. `$1.250.000,00`).

#### Scenario: Visualización de métricas en dashboard administrativo
- **WHEN** un administrativo ingresa al panel principal
- **THEN** el sistema presenta en tiempo real la cantidad de alumnos activos, membresías vigentes, asistencia del día, porcentaje de ocupación e ingresos del período seleccionado formateados con 2 decimales, coma decimal y punto como separador de miles (por ejemplo `$150.000,00`)
