## Purpose

El módulo `notifications` centraliza la generación, personalización por plantillas, evaluación de preferencias, entrega multicanal asíncrona, consulta in-app y auditoría de las comunicaciones del sistema para alumnos, instructores, secretarios, administradores y responsables de menores, garantizando idempotencia, aislamiento frente a fallos de proveedores externos y control de acceso estricto por destinatario.

## ADDED Requirements

### Requirement: Separación entre notificación lógica e-app y entrega por canal externo
El sistema SHALL modelar cada comunicación mediante una entidad lógica (`NotificationMessage`) asociada a una persona destinataria (`RecipientPersonId`) que representa de forma permanente la notificación interna (in-app), y cero o más registros de entrega (`NotificationDelivery`) para los canales externos habilitados (`Email`, y extensible a `Push` y `WhatsApp`). La notificación interna SHALL conservarse y poder consultarse en la aplicación aunque falle, se omita o no esté configurado el envío por cualquier canal externo. El módulo `notifications` no SHALL ejecutar reglas de negocio de otros módulos (reservas, clases, membresías, créditos, pagos, promociones o personas) ni revertir operaciones ya confirmadas.

#### Scenario: Persistencia de notificación interna cuando el canal de email falla
- **WHEN** se genera una notificación transaccional para un alumno y el proveedor de email rechaza el envío o no está disponible
- **THEN** la notificación lógica (`NotificationMessage`) permanece disponible como no leída en la bandeja in-app del destinatario y el fallo queda acotado al registro `NotificationDelivery` del canal `Email`

#### Scenario: Conservación histórica ante desactivación de persona o cambio de entidad relacionada
- **WHEN** cambia el nombre de la actividad o el estado de la clase/membresía que originó una notificación, o la persona destinataria pasa a estado `INACTIVA`
- **THEN** la notificación conserva el título, cuerpo renderizado, categoría, tipo de evento y metadatos no sensibles con los que fue emitida, sin eliminarse físicamente (`ON DELETE RESTRICT`)

#### Scenario: Validación de referencia de acción interna
- **WHEN** se registra una notificación con `ActionUrl`
- **THEN** el sistema valida que sea una ruta interna relativa permitida (que comience con `/` y no con `//` ni esquemas externos como `http:`, `https:` o `javascript:`), rechazando redirecciones abiertas

### Requirement: Catálogo tipado de eventos de negocio y aislamiento post-commit
El sistema SHALL generar notificaciones a partir de eventos confirmados post-commit (`IPostCommitDomainEvent`) o procesos programados de los módulos propietarios, cubriendo:
- **Reservas (`Reservations`)**: reserva confirmada (`ReservationConfirmed`), reserva cancelada (`ReservationCancelled`, salvo cuando `CancellationSource == ClassSessionChange`), ingreso a lista de espera (`ReservationWaitlisted`), oferta de cupo desde lista de espera (`WaitlistSpotOffered`, incluyendo fecha/hora de vencimiento en huso local del gimnasio y `ActionUrl` interna hacia la reserva), oferta de cupo vencida (`WaitlistOfferExpired`), promoción desde lista de espera (`ReservationPromotedFromWaitlist`) y recordatorio de clase próxima (`ClassReminder`).
- **Clases (`ClassSessions`)**: clase suspendida (`ClassSuspended`), clase cancelada (`ClassCancelled`), clase reprogramada (`ClassRescheduled`), cambio de sala (`ClassRoomChanged`) y cambio de instructor (`ClassInstructorChanged`), notificando a los alumnos con reserva alcanzada.
- **Membresías y créditos (`Memberships` / `MembershipCredits`)**: membresía activada (`MembershipActivated`), suspendida (`MembershipSuspended`), reactivada (`MembershipReactivated`), próxima a vencer (`MembershipExpiring`), vencida (`MembershipExpired`) y otorgamiento de créditos compensatorios (`CreditsCompensated`).
- **Pagos (`Payments`)**: pago confirmado (`PaymentConfirmed`), pago rechazado (`PaymentFailed`) y reembolso total o parcial confirmado (`PaymentRefunded`), sin incluir nunca números completos de tarjeta, credenciales ni secretos financieros.
- **Promociones (`Promotions`)**: beneficio promocional otorgado o promoción disponible (`PromotionAvailable`) determinada por el módulo propietario.
- **Seguridad y administración (`Identity` / `Operations`)**: alertas de seguridad (`SecurityAlert`) y avisos administrativos (`AdministrativeNotice`), sin enviar contraseñas ni tokens en texto plano.

Un fallo al construir, renderizar, persistir o despachar cualquier notificación SHALL registrarse en el log operativo sin revertir la transacción de negocio que le dio origen.

#### Scenario: Tipos de notificación diferenciados en cambios de sesión de clase
- **WHEN** una sesión de clase con alumnos inscriptos se cancela, se reprograma o cambia de sala
- **THEN** el sistema genera una notificación para cada alumno afectado con el `NotificationType` específico (`ClassCancelled`, `ClassRescheduled` o `ClassRoomChanged`) en lugar de reutilizar `ClassSuspended`, presentando las fechas y horas en la zona horaria del gimnasio

#### Scenario: Aislamiento de fallos post-commit en cambios de clase, membresía o pago
- **WHEN** ocurre una excepción en el procesador de notificaciones tras confirmarse una reserva, un cambio de clase, una transición de membresía o un pago
- **THEN** la operación de negocio original permanece confirmada en la base de datos y el error se registra con su `CorrelationId`

#### Scenario: Prohibición de datos sensibles en notificaciones de pagos y seguridad
- **WHEN** se emite una notificación de pago confirmado, reembolso o alerta de seguridad
- **THEN** el cuerpo y los metadatos no contienen contraseñas, tokens JWT, secretos de proveedor ni datos completos de medios de pago

### Requirement: Resolución de destinatarios y responsables de menores
El sistema SHALL resolver los destinatarios a partir del identificador estable de persona (`PersonId`) provisto por el evento o contrato interno. Cuando el destinatario directo sea un alumno menor de edad (`Person.IsUnderage()`) en una comunicación transaccional u operativa y el menor sea miembro activo (`FamilyGroupMember.IsActive`) de un grupo familiar activo (`FamilyGroup.IsActive`) cuyo titular (`PrimaryContact`) sea una persona adulta activa distinta del menor, el sistema SHALL generar una notificación individual adicional dirigida al `PrimaryContactId` identificando al menor vinculado. Las notificaciones originadas en eventos colectivos (como la suspensión de una clase) SHALL generarse de forma individual por destinatario, sin divulgar la identidad ni los datos de otros alumnos inscriptos.

#### Scenario: Notificación al adulto responsable de un alumno menor en grupo familiar
- **WHEN** se confirma o cancela una reserva, o se suspende una clase, en la que participa un alumno menor de edad que pertenece a un `FamilyGroup` activo con un `PrimaryContact` adulto activo
- **THEN** el sistema genera la notificación para el alumno menor y una notificación independiente para el `PrimaryContact` adulto indicando el nombre del menor y el detalle del evento

#### Scenario: Menor sin adulto vinculado como Persona en grupo familiar
- **WHEN** ocurre un evento para un alumno menor que sólo posee el contacto de emergencia textual en su ficha (`EmergencyContact`) pero no integra un `FamilyGroup` con un `PrimaryContact` registrado
- **THEN** el sistema genera la notificación in-app para el alumno sin fallar ni crear registros huérfanos

#### Scenario: Privacidad entre alumnos ante evento colectivo de clase
- **WHEN** se suspende una clase con múltiples alumnos inscriptos
- **THEN** cada alumno recibe únicamente su propia notificación sin incluir nombres, correos ni identificadores de los demás inscriptos

### Requirement: Idempotencia en la generación de notificaciones y recordatorios programados
Toda generación de notificación originada por un evento de negocio o un proceso programado SHALL incluir una clave de idempotencia determinística (`IdempotencyKey`) por destinatario (`RecipientPersonId`), respaldada por un índice único parcial en PostgreSQL (`UX_NotificationMessages_Recipient_IdempotencyKey`). Si un mismo evento o barrido programado se ejecuta más de una vez, el sistema SHALL omitir la creación de una segunda notificación lógica y de entregas duplicadas. El proceso programado de recordatorios de clases próximas (`ClassReminderWorker`) SHALL emitir a lo sumo un recordatorio `ClassReminder` por reserva confirmada dentro de la ventana de anticipación configurada, excluyendo clases canceladas, suspendidas o reprogramadas y reservas canceladas.

#### Scenario: Evento de negocio procesado dos veces con la misma clave de idempotencia
- **WHEN** el despachador recibe dos veces el mismo evento de confirmación de reserva, cambio de clase o activación de membresía para el mismo `RecipientPersonId`
- **THEN** se persiste una única fila en `NotificationMessages` y no se duplican los trabajos en `NotificationDeliveries`

#### Scenario: Ejecuciones repetidas del worker de recordatorios de clase
- **WHEN** `ClassReminderWorker` se ejecuta múltiples veces dentro de la ventana previa al inicio de una clase `Open`
- **THEN** cada alumno con reserva `Confirmed` recibe un único `ClassReminder` para esa reserva y clase

#### Scenario: Clase suspendida o reserva cancelada antes de la ventana de recordatorio
- **WHEN** una clase pasa a `Suspended` o `Cancelled`, o el alumno cancela su reserva antes de que corra `ClassReminderWorker`
- **THEN** el worker no genera recordatorio `ClassReminder` para esa reserva

### Requirement: Preferencias de comunicación por categoría y distinción transaccional vs. promocional
El sistema SHALL gestionar preferencias de notificación por persona, categoría (`NotificationCategory`) y canal (`NotificationChannel`) mediante una restricción única `(PersonId, Category, Channel)`. El backend SHALL clasificar las categorías en:
- **Transaccionales / operativas esenciales**: `Reservations`, `ClassChanges`, `MembershipsAndCredits`, `Payments`, `Administrative` y `Security`.
- **Promocionales**: `Promotions`.

El canal `InApp` para las categorías transaccionales y las notificaciones de `Security` no SHALL poder deshabilitarse. El usuario autenticado SHALL poder habilitar o deshabilitar canales externos (`Email`, `Push`, `WhatsApp`) por categoría (salvo alertas críticas de `Security`) y habilitar o deshabilitar por completo la categoría `Promotions`. Antes de encolar una entrega o crear una comunicación promocional, el backend SHALL evaluar las preferencias vigentes del destinatario; si una preferencia cambia después de haberse encolado una entrega aún en `Pending`, el procesador de entregas SHALL reevaluarla antes de enviarla y marcarla como `Skipped` si el canal fue deshabilitado.

#### Scenario: Deshabilitación de comunicaciones promocionales bloquea envíos comerciales sin afectar avisos operativos
- **WHEN** un alumno deshabilita la categoría `Promotions` en sus preferencias y posteriormente se confirma una reserva suya y se emite una difusión `PromotionAvailable`
- **THEN** el sistema bloquea la notificación promocional pero genera y entrega normalmente la notificación transaccional `ReservationConfirmed`

#### Scenario: Rechazo al intentar deshabilitar el canal InApp de una categoría transaccional
- **WHEN** un usuario intenta actualizar sus preferencias deshabilitando el canal `InApp` para `Reservations`, `ClassChanges`, `MembershipsAndCredits`, `Payments`, `Administrative` o `Security`
- **THEN** el sistema rechaza la solicitud con `409 Conflict` (`PREFERENCE_MANDATORY_CHANNEL_LOCKED`)

#### Scenario: Preferencia de email deshabilitada antes del procesamiento de una entrega pendiente
- **WHEN** una entrega por `Email` se encuentra en estado `Pending` y el destinatario deshabilita ese canal para la categoría antes de que el worker la procese
- **THEN** el worker marca la entrega como `Skipped` sin invocar al proveedor de email

### Requirement: Plantillas versionadas y renderizado seguro
El sistema SHALL administrar plantillas de notificación (`NotificationTemplate`) identificadas por `Code`, `Type` (`NotificationType`), `Channel` (`NotificationChannel`), `Locale` y `Version`, con estados `Draft`, `Active` e `Inactive` y una única versión `Active` por combinación `(Type, Channel, Locale)`. El motor de renderizado (`INotificationTemplateRenderer`) SHALL sustituir únicamente marcadores de posición de la forma `{{variable}}` incluidos en la lista blanca `AllowedVariables` de la plantilla, sin ejecutar código arbitrario. Para el canal `Email`, todos los valores interpolados SHALL escaparse en HTML. Si se solicita renderizar una plantilla con variables requeridas faltantes o marcadores desconocidos no permitidos en su definición al validarla, el sistema SHALL rechazar la operación. La modificación o el versionado de una plantilla no SHALL alterar el título ni el cuerpo de las notificaciones ya emitidas.

#### Scenario: Escape de caracteres HTML en variables de plantilla de email
- **WHEN** se renderiza una plantilla de `Email` cuyo contexto contiene `<script>alert(1)</script>` en una variable como `{{reason}}` o `{{studentName}}`
- **THEN** el valor interpolado se escapa como `&lt;script&gt;alert(1)&lt;/script&gt;` en el cuerpo renderizado

#### Scenario: Rechazo de plantilla con sintaxis o variables no declaradas
- **WHEN** un administrador intenta crear o actualizar una plantilla cuyo `SubjectTemplate` o `BodyTemplate` referencia un marcador `{{secretToken}}` ausente en `AllowedVariables`
- **THEN** el sistema rechaza la solicitud con error de validación (`400 Bad Request` / `TEMPLATE_INVALID_VARIABLES`)

#### Scenario: Activación de nueva versión desactiva la versión previa activa
- **WHEN** un administrador activa una nueva versión de plantilla para un mismo `(Type, Channel, Locale)`
- **THEN** la versión previamente `Active` pasa a `Inactive`, la nueva versión queda `Active` en la misma transacción, se registra la acción en `AuditLogs` y las notificaciones históricas ya creadas conservan su texto original

### Requirement: Procesamiento asíncrono de entregas externas, reintentos y recuperación
Las entregas por canales externos (`Email`, `Push`, `WhatsApp`) SHALL encolarse de forma persistente en PostgreSQL (`NotificationDeliveries`) con estado inicial `Pending` y procesarse de manera asíncrona mediante un worker (`NotificationDeliveryWorker`) con reclamación concurrente segura (`FOR UPDATE SKIP LOCKED`), evitando que dos instancias procesen la misma entrega simultáneamente. Al iniciar o en cada ciclo, el worker SHALL recuperar entregas que hayan quedado en estado `Processing` más allá del umbral de bloqueo (`StaleProcessingTimeout`) tras un cierre abrupto o reinicio. El proveedor de canal SHALL distinguir entre aceptación de envío (`Sent`) y confirmación de entrega (`Delivered`), y clasificar las fallas en:
- **Errores transitorios** (fallos temporales de red, timeouts, límite de tasa del proveedor): se reprograman en estado `Pending` incrementando `AttemptCount` y calculando `NextAttemptAtUtc` con backoff exponencial y jitter hasta alcanzar `MaxAttempts`, momento en que pasan a `Failed`.
- **Errores permanentes o de configuración** (dirección inválida o ausente, credenciales inválidas, proveedor no configurado `PROVIDER_NOT_CONFIGURED`, mensaje inválido): pasan inmediatamente a `Failed` sin reintentos automáticos y registrando `LastErrorCode` y `LastErrorMessage` saneados (sin exponer secretos ni trazas internas).

El sistema no SHALL simular un envío exitoso (`Sent` o `Delivered`) cuando el proveedor de un canal externo no esté configurado.

#### Scenario: Reintento con backoff exponencial ante fallo transitorio del proveedor de email
- **WHEN** `IEmailNotificationProvider` devuelve un error transitorio en el primer intento de una entrega `Pending` (con `AttemptCount < MaxAttempts`)
- **THEN** la entrega incrementa `AttemptCount` a 1, conserva estado `Pending`, registra `LastErrorCode` y fija `NextAttemptAtUtc` en el futuro según la política de backoff exponencial

#### Scenario: Fallo permanente o proveedor no configurado sin reintentos automáticos
- **WHEN** se procesa una entrega externa cuyo canal no tiene proveedor configurado o cuya dirección de destino es permanentemente inválida
- **THEN** la entrega pasa directamente a estado `Failed` con `NextAttemptAtUtc = null` y código `PROVIDER_NOT_CONFIGURED` o `INVALID_RECIPIENT_ADDRESS`, sin programar nuevos intentos automáticos

#### Scenario: Reclamación concurrente y recuperación tras reinicio
- **WHEN** dos instancias del procesador reclaman lotes pendientes simultáneamente, o existe una entrega en `Processing` cuyo `LastAttemptAtUtc` superó el timeout de bloqueo tras un reinicio del servicio
- **THEN** cada entrega pendiente es procesada por una sola instancia gracias a `FOR UPDATE SKIP LOCKED`, y la entrega estancada vuelve a ser elegible y se procesa sin perderse

#### Scenario: Reintento manual administrativo auditado
- **WHEN** un usuario con permiso `NOTIFICATIONS_RETRY_FAILED` solicita `POST /api/admin/notifications/deliveries/{id}/retry` sobre una entrega en estado `Failed`
- **THEN** el sistema restablece la entrega a `Pending` con `NextAttemptAtUtc = UtcNow`, registra en `AuditLogs` el usuario, motivo, fecha y estado previo, y rechaza con `409 Conflict` (`DELIVERY_INVALID_RETRY_STATE`) si la entrega ya estaba en `Sent` o `Delivered`

### Requirement: Control de acceso contextual, prevención de IDOR y API de centro de notificaciones
El sistema SHALL obtener la identidad del destinatario exclusivamente desde los claims validados del JWT (`person_id` / `sub`) en todos los endpoints del usuario autenticado (`/api/notifications*`), sin confiar en identificadores enviados por el cliente. Un usuario autenticado sólo SHALL poder listar, consultar el detalle, obtener el contador de no leídas y marcar como leídas o no leídas las notificaciones cuyo `RecipientPersonId` coincida con su `PersonId`. El intento de consultar o modificar una notificación perteneciente a otra persona desde los endpoints propios SHALL rechazarse sin revelar información ajena (`404 Not Found` o `403 Forbidden`). Los endpoints administrativos (`/api/admin/notifications*` y `/api/admin/notification-templates*`) SHALL exigir los permisos explícitos `NOTIFICATIONS_READ_ALL`, `NOTIFICATIONS_DELIVERY_READ`, `NOTIFICATIONS_RETRY_FAILED`, `NOTIFICATIONS_TEMPLATES_READ` o `NOTIFICATIONS_TEMPLATES_MANAGE` según la operación, y no SHALL exponer un endpoint abierto para enviar correos arbitrarios a destinatarios libres.

#### Scenario: Prevención de IDOR al consultar o marcar como leída una notificación ajena
- **WHEN** el alumno A invoca `GET /api/notifications/{id}`, `PATCH /api/notifications/{id}/read`, `POST /api/notifications/{id}/read` o `PATCH /api/notifications/{id}/unread` pasando el `id` de una notificación cuyo `RecipientPersonId` pertenece al alumno B
- **THEN** el sistema rechaza la operación con `403 Forbidden` (o `404 Not Found`), no modifica `IsRead` ni `ReadAtUtc` de la notificación de B y no devuelve su contenido

#### Scenario: Marcado de lectura, desmarcado y marcado masivo actualizan el contador propio
- **WHEN** el usuario autenticado marca una notificación propia como leída (`PATCH /api/notifications/{id}/read`), la vuelve a marcar como no leída (`PATCH /api/notifications/{id}/unread`) o marca todas como leídas (`POST /api/notifications/mark-all-read`)
- **THEN** el sistema actualiza `IsRead` y `ReadAtUtc` (`UtcNow` al leer, `null` al desmarcar) únicamente sobre las notificaciones del usuario y `GET /api/notifications/unread-count` refleja de inmediato la nueva cantidad

#### Scenario: Consulta paginada y filtrada de notificaciones propias
- **WHEN** el usuario autenticado consulta `GET /api/notifications` indicando `page`, `pageSize`, `isRead`, `category` y rango de fechas (`fromUtc`, `toUtc`)
- **THEN** el sistema retorna un `PagedResult<NotificationDto>` ordenado por fecha descendente que contiene únicamente notificaciones de su `PersonId` y presenta en el frontend Angular el centro de notificaciones (campana con contador, panel desplegable, vista completa y pantalla de preferencias) con fechas formateadas en el huso horario del gimnasio
