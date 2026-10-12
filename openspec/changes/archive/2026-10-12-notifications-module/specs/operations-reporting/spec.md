## MODIFIED Requirements

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
