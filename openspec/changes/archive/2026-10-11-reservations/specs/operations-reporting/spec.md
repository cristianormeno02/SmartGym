## MODIFIED Requirements

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
