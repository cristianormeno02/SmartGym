## Context

Actualmente las notificaciones viven parcialmente bajo `Operations`:
- `SmartGym.Domain/Entities/Operations/NotificationMessage.cs` almacena en una única fila el mensaje in-app con un campo `Channel` (`Email = 1`, `InApp = 2`, `WhatsApp = 3`), sin tabla de entregas, sin preferencias, sin plantillas, sin `ReadAtUtc` y sin `IdempotencyKey`.
- `NotifyStudentOnReservationLifecycleHandler` ya utiliza eventos post-commit (`IPostCommitDomainEvent`), pero inserta directamente en `_dbContext.NotificationMessages`.
- `NotifyStudentsOnSessionChangeHandler` escucha eventos pre-commit (`IDomainEvent`) de `ClassSession`, usa `NotificationType.ClassSuspended` para los cuatro eventos (suspensión, cancelación, reprogramación y cambio de sala) y escribe en el `DbContext` dentro de la misma transacción de la clase.
- `MembershipActivationService.TryActivateAsync` y `MembershipService.SuspendAsync` / `ProcessExpirationsAndWarningsAsync` instancian `NotificationMessage` directamente dentro de las transacciones de `Memberships`.
- `NotificationsController` expone `GET /api/notifications/my`, `POST /api/notifications` y `POST /api/notifications/{id}/read` (donde `MarkAsReadAsync` no verifica titularidad del recurso).
- En Angular, `NotificationItem` (`gym.models.ts`) espera `{ id, title, message, isRead, createdAtUtc, readAtUtc }`, que no coincide con `NotificationDto` (`body`, `sentAtUtc`), y `NavbarComponent` no cuenta con campana ni centro de notificaciones.

Ver `proposal.md` para la motivación completa.

## Goals / Non-Goals

**Goals:**
- Separar la notificación lógica e in-app (`NotificationMessage`) de los intentos de entrega por canal externo (`NotificationDelivery`).
- Garantizar aislamiento post-commit, idempotencia en PostgreSQL y preservación histórica de notificaciones.
- Implementar preferencias por categoría y canal (`NotificationPreference`) y plantillas versionadas con renderizado seguro (`NotificationTemplate`).
- Procesar entregas externas en segundo plano con reclamación concurrente segura (`FOR UPDATE SKIP LOCKED`), recuperación tras reinicios y backoff exponencial con jitter.
- Cerrar la brecha IDOR en lectura/marcado de notificaciones e incorporar permisos granulares, auditoría y centro de notificaciones en Angular.

**Non-Goals:**
- Introducir un broker externo (RabbitMQ, Kafka) o WebSockets/SignalR.
- Implementar campañas de marketing masivas, constructores de workflows o proveedores productivos de WhatsApp/Push sin credenciales.
- Modificar las reglas de negocio de `Reservations`, `ClassSessions`, `Memberships`, `Payments`, `Promotions` o `People`.

## Decisions

### Decision 1 – Identidad del destinatario (`RecipientPersonId`) y evolución de `"NotificationMessages"`

En Smart-Gym (`people-management` e `identity-access`), `Person` es el registro maestro de cada individuo (`Persona ≠ Usuario`) y todos los módulos operativos (`Reservations.StudentId`, `Memberships.StudentId`, `ClassSessions.ScheduledInstructorId`, `Payments.PayerPersonId`) referencian `Person.Id`. Asimismo, el token JWT incluye siempre el claim `person_id` (`ICurrentUserService.PersonId`).

Por coherencia arquitectónica y compatibilidad con los datos e integraciones existentes:
- Se conserva la entidad `NotificationMessage` (tabla `"NotificationMessages"`) y su clave foránea `RecipientPersonId` hacia `People.Id` como identificador estable del destinatario (equivalente al `RecipientUserId` orientativo de la spec funcional).
- En `NotificationPreference` se utiliza `PersonId` (`FK -> People.Id`).
- Para trazabilidad de acciones de operadores/administradores (quien crea una plantilla o solicita un reintento manual) se utiliza `UserId` (`ICurrentUserService.UserId`), siguiendo el patrón de `AuditLog` y `PaymentRefund`.
- La FK `NotificationMessages.RecipientPersonId -> People.Id` cambia de `DeleteBehavior.Cascade` a `DeleteBehavior.Restrict` para evitar la pérdida de historial si una persona se desactiva o bloquea.

### Decision 2 – Separación entre `NotificationMessage` (lógica/in-app) y `NotificationDelivery` (canal externo)

`NotificationMessage` representa la notificación lógica para el destinatario y constituye, por sí misma, la notificación del canal interno (`InApp`):
- Campos de `NotificationMessage`: `Id`, `RecipientPersonId`, `Category` (`NotificationCategory`), `Type` (`NotificationType`), `Channel` (conservado con valor `InApp` por compatibilidad), `Title` (máx. 200), `Body` (máx. 4000), `RelatedEntityType` (`string?`, máx. 80), `RelatedEntityId` (`Guid?`), `ActionUrl` (`string?`, máx. 500, ruta relativa validada que comienza con `/` y rechaza `//`, `http:`, `https:`, `javascript:`), `IsRead` (`bool`), `ReadAtUtc` (`DateTime?`), `SentAtUtc` (`DateTime`), `ExpiresAtUtc` (`DateTime?`), `MetadataJson` (`string?`, máx. 2000, sólo datos no sensibles), `IdempotencyKey` (`string?`, máx. 180), `CorrelationId` (`string?`, máx. 100).

`NotificationDelivery` modela la entrega por cada canal externo (`Email`, `Push`, `WhatsApp`):
- Campos de `NotificationDelivery`: `Id`, `NotificationId` (`FK -> NotificationMessages.Id`, `Cascade`), `Channel` (`NotificationChannel`), `Provider` (`string`, máx. 80), `RecipientAddress` (`string?`, máx. 256), `Status` (`NotificationDeliveryStatus`), `AttemptCount` (`int`), `MaxAttempts` (`int`), `NextAttemptAtUtc` (`DateTime?`), `LastAttemptAtUtc` (`DateTime?`), `SentAtUtc` (`DateTime?`), `DeliveredAtUtc` (`DateTime?`), `ProviderMessageId` (`string?`, máx. 200), `LastErrorCode` (`string?`, máx. 80), `LastErrorMessage` (`string?`, máx. 1000, saneado), `RetriedByUserId` (`Guid?`), `RetryReason` (`string?`, máx. 500), `CreatedAtUtc`, `UpdatedAtUtc`.
- Índice único `UX_NotificationDeliveries_Notification_Channel` sobre `("NotificationId", "Channel")` para impedir dos entregas del mismo canal para una misma notificación lógica.
- Máquina de estados `NotificationDeliveryStatus`:
  - `Pending = 1`: encolada o esperando `NextAttemptAtUtc`.
  - `Processing = 2`: reclamada por un worker en curso.
  - `Sent = 3`: aceptada por el proveedor externo (p. ej. servidor SMTP aceptó el mensaje), sin confirmación final de recepción en casilla.
  - `Delivered = 4`: entrega confirmada explícitamente por el proveedor.
  - `Failed = 5`: error permanente o agotamiento de `MaxAttempts`.
  - `Cancelled = 6`: cancelada administrativamente o por expiración de la notificación.
  - `Skipped = 7`: omitida porque el usuario deshabilitó el canal o carece de dirección de contacto opcional sin ser un fallo del proveedor.

### Decision 3 – Catálogo de `NotificationType`, `NotificationCategory` y mapeo de eventos

Se introduce `NotificationCategory`:
1. `Reservations = 1` (Transaccional)
2. `ClassChanges = 2` (Transaccional)
3. `MembershipsAndCredits = 3` (Transaccional)
4. `Payments = 4` (Transaccional)
5. `Promotions = 5` (Promocional)
6. `Administrative = 6` (Transaccional)
7. `Security = 7` (Transaccional, crítica)

Se amplía `NotificationType` conservando los enteros existentes (`1..14`):

| Valor | `NotificationType` | `NotificationCategory` | Naturaleza | `RelatedEntityType` | `ActionUrl` por defecto |
|---|---|---|---|---|---|
| 1 | `ClassSuspended` | `ClassChanges` | Transaccional | `ClassSession` | `/portal/alumno` |
| 2 | `ClassReminder` | `Reservations` | Transaccional | `Reservation` | `/portal/alumno` |
| 3 | `MembershipExpiring` | `MembershipsAndCredits` | Transaccional | `Membership` | `/portal/alumno` |
| 4 | `PromotionAvailable` | `Promotions` | Promocional | `Promotion` | `/planes` |
| 5 | `PaymentConfirmed` | `Payments` | Transaccional | `Payment` | `/portal/alumno` |
| 6 | `MembershipActivated` | `MembershipsAndCredits` | Transaccional | `Membership` | `/portal/alumno` |
| 7 | `MembershipSuspended` | `MembershipsAndCredits` | Transaccional | `Membership` | `/portal/alumno` |
| 8 | `MembershipExpired` | `MembershipsAndCredits` | Transaccional | `Membership` | `/portal/alumno` |
| 9 | `ReservationConfirmed` | `Reservations` | Transaccional | `Reservation` | `/portal/alumno` |
| 10 | `ReservationCancelled` | `Reservations` | Transaccional | `Reservation` | `/portal/alumno` |
| 11 | `ReservationWaitlisted` | `Reservations` | Transaccional | `Reservation` | `/portal/alumno` |
| 12 | `ReservationPromotedFromWaitlist` | `Reservations` | Transaccional | `Reservation` | `/portal/alumno` |
| 13 | `WaitlistSpotOffered` | `Reservations` | Transaccional | `Reservation` | `/portal/alumno` |
| 14 | `WaitlistOfferExpired` | `Reservations` | Transaccional | `Reservation` | `/portal/alumno` |
| 15 | `ClassCancelled` | `ClassChanges` | Transaccional | `ClassSession` | `/portal/alumno` |
| 16 | `ClassRescheduled` | `ClassChanges` | Transaccional | `ClassSession` | `/portal/alumno` |
| 17 | `ClassRoomChanged` | `ClassChanges` | Transaccional | `ClassSession` | `/portal/alumno` |
| 18 | `ClassInstructorChanged` | `ClassChanges` | Transaccional | `ClassSession` | `/portal/alumno` |
| 19 | `MembershipReactivated` | `MembershipsAndCredits` | Transaccional | `Membership` | `/portal/alumno` |
| 20 | `CreditsCompensated` | `MembershipsAndCredits` | Transaccional | `Membership` | `/portal/alumno` |
| 21 | `PaymentFailed` | `Payments` | Transaccional | `Payment` | `/portal/alumno` |
| 22 | `PaymentRefunded` | `Payments` | Transaccional | `Payment` | `/portal/alumno` |
| 23 | `AdministrativeNotice` | `Administrative` | Transaccional | `Person` | `/portal/notificaciones` |
| 24 | `SecurityAlert` | `Security` | Transaccional | `User` | `/portal/mis-datos` |

### Decision 4 – Aislamiento post-commit y pipeline unificado (`INotificationDispatcher`)

Para evitar que un error al renderizar plantillas o persistir notificaciones revierta una operación de negocio:
- Todos los handlers de notificaciones se ejecutan a través de `INotificationDispatcher.DispatchAsync(NotificationDispatchRequest, CancellationToken)`, que encapsula resolución de destinatarios, verificación de idempotencia, evaluación de preferencias, renderizado de plantillas y persistencia de `NotificationMessage` + `NotificationDelivery`.
- `ClassSessionSuspended`, `ClassSessionCancelled`, `ClassSessionRescheduled` y `ClassSessionRoomChanged` continúan ejecutando la cancelación de reservas en pre-commit (`CancelReservationsOnSessionVoidedHandler`), y se agregan eventos post-commit (`IPostCommitDomainEvent`) o manejo aislado para la emisión de notificaciones en `NotifyStudentsOnSessionChangeHandler`, corrigiendo además el tipo (`ClassCancelled`, `ClassRescheduled`, `ClassRoomChanged`) y eliminando el método muerto `NotificationService.NotifyClassSuspendedAsync`.
- En `Memberships`, `MembershipActivatedEvent`, `MembershipSuspendedEvent`, `MembershipExpiredEvent` y el nuevo `MembershipReactivatedNotificationEvent` disparan la creación de notificaciones mediante `INotificationDispatcher` con captura y log de errores para no abortar la activación, suspensión, reactivación o el lote de vencimientos.
- En `Payments` (coordinado con `payments-core`), si los eventos post-commit `PaymentConfirmedNotificationEvent`, `PaymentFailedNotificationEvent` y `PaymentRefundedNotificationEvent` están presentes o se invocan al confirmar/fallar/reembolsar, el handler utiliza el mismo `INotificationDispatcher` sin incluir datos sensibles de tarjetas ni credenciales.

### Decision 5 – Resolución de destinatarios y responsables de menores (`INotificationRecipientResolver`)

`INotificationRecipientResolver` recibe el `PrimaryRecipientPersonId` y la categoría/tipo del evento:
1. Verifica que la persona exista en `People` y no esté en estado `Deceased`.
2. Si la notificación es transaccional y la persona es menor de edad (`person.IsUnderage(utcNow)`), consulta si existe un `FamilyGroupMember` activo (`IsActive`) en un `FamilyGroup` activo (`IsActive`) cuyo `PrimaryContact` esté activo (`Status == PersonStatus.Active`), sea mayor de edad (`!PrimaryContact.IsUnderage(utcNow)`) y tenga `PrimaryContactId != person.Id`.
3. Para cada adulto responsable hallado, genera un destinatario resuelto adicional con su propia `IdempotencyKey` (`{baseIdempotencyKey}:guardian:{guardianPersonId}`) y contexto de plantilla indicando el nombre del menor (`studentName`), de modo que tanto el menor (en su vista in-app) como su adulto responsable reciban su propia notificación independiente sin cruzar datos de terceros.
4. Si el menor sólo tiene el value object `EmergencyContact` (nombre y teléfono de texto en `Person` sin cuenta ni `PersonId`), sólo se emite la notificación para el `RecipientPersonId` del menor, respetando el modelo de `people-management`.

### Decision 6 – Preferencias (`NotificationPreference`) y políticas por defecto

- Tabla `"NotificationPreferences"` con columnas `Id`, `PersonId`, `Category`, `Channel`, `Enabled`, `UpdatedAtUtc` e índice único `UX_NotificationPreferences_Person_Category_Channel` sobre `("PersonId", "Category", "Channel")`.
- **Política por defecto (sin filas previas en BD)**:
  - Categorías transaccionales (`Reservations`, `ClassChanges`, `MembershipsAndCredits`, `Payments`, `Administrative`, `Security`): `InApp = true` (bloqueado, no deshabilitable), `Email = true` (deshabilitable por el usuario salvo en `Security`), `Push = false`, `WhatsApp = false`.
  - Categoría promocional (`Promotions`): `InApp = true` (deshabilitable), `Email = false` (requiere opt-in explícito del usuario para correos comerciales), `Push = false`, `WhatsApp = false`.
- Si el usuario intenta poner `Enabled = false` en `InApp` para cualquier categoría transaccional, o en `Email` para `Security`, el servicio responde `409 Conflict` con código `PREFERENCE_MANDATORY_CHANNEL_LOCKED`.
- Si una comunicación es de categoría `Promotions` y el destinatario tiene `InApp = false` y `Email = false`, no se crea notificación ni entrega.

### Decision 7 – Plantillas versionadas y renderizado seguro (`NotificationTemplate` & `INotificationTemplateRenderer`)

- Tabla `"NotificationTemplates"` con columnas `Id`, `Code` (máx. 100), `Type` (`NotificationType`), `Channel` (`NotificationChannel`), `Locale` (máx. 10, por defecto `"es-AR"`), `SubjectTemplate` (máx. 200, requerido en `Email`), `BodyTemplate` (máx. 4000), `AllowedVariables` (lista separada por comas de nombres de variables permitidos, p. ej. `"studentName,activityName,date,time,reason"`), `Version` (`int >= 1`), `Status` (`NotificationTemplateStatus`: `Draft = 1`, `Active = 2`, `Inactive = 3`), `CreatedByUserId` (`Guid?`), `CreatedAtUtc`, `UpdatedAtUtc`, `VersionToken` (`xmin`).
- Índices únicos:
  - `UX_NotificationTemplates_Code_Channel_Locale_Version` sobre `("Code", "Channel", "Locale", "Version")`.
  - Índice único parcial `UX_NotificationTemplates_Active_Type_Channel_Locale` sobre `("Type", "Channel", "Locale") WHERE "Status" = 2 AND "IsActive" = TRUE`.
- **Seguridad del motor de plantillas (`NotificationTemplateRenderer`)**:
  - Extrae tokens con expresión regular estricta `\{\{\s*([a-zA-Z0-9_]+)\s*\}\}`.
  - Al crear o editar una plantilla, verifica que toda variable `{{var}}` presente en `SubjectTemplate` o `BodyTemplate` pertenezca a `AllowedVariables` y que no contenga etiquetas `<script>`, manejadores `on*=` ni URLs `javascript:`. De lo contrario falla con `400 Bad Request` (`TEMPLATE_INVALID_VARIABLES` / `TEMPLATE_UNSAFE_CONTENT`).
  - Al renderizar para `NotificationChannel.Email`, aplica `WebUtility.HtmlEncode` a cada valor de variable antes de interpolarlo, impidiendo inyección HTML/XSS. Para `NotificationChannel.InApp`, almacena texto plano saneado (Angular interpola texto escapado con `{{ n.body }}` sin `innerHTML`).
  - Si no existe una plantilla `Active` en base de datos para `(Type, Channel, Locale)` (porque fue desactivada), el despachador usa el catálogo interno de respaldo (`DefaultNotificationTemplates`) para las notificaciones transaccionales y registra una advertencia en el log; si es promocional o un envío basado exclusivamente en plantilla sin respaldo, registra el motivo sin romper la operación de origen.

### Decision 8 – Cola persistente en PostgreSQL (`NotificationDeliveryWorker`), `FOR UPDATE SKIP LOCKED` y reintentos

No se utiliza una cola en memoria (`Channel<T>`) como fuente de verdad porque se perderían las entregas pendientes ante un reinicio del proceso. La propia tabla `"NotificationDeliveries"` actúa como cola persistente en PostgreSQL:

1. **Recuperación tras reinicio**: al comenzar cada ciclo, las filas con `Status = Processing` y `LastAttemptAtUtc < UtcNow - StaleProcessingTimeout` (por defecto 5 minutos) vuelven a `Status = Pending`, `NextAttemptAtUtc = UtcNow`.
2. **Reclamación atómica concurrente (`ClaimPendingDeliveriesAsync`)**:
   - En PostgreSQL ejecuta dentro de una transacción corta:
     ```sql
     SELECT "Id" FROM "NotificationDeliveries"
     WHERE "Status" = 1 AND "IsActive" = TRUE
       AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= @nowUtc)
     ORDER BY "NextAttemptAtUtc" NULLS FIRST, "CreatedAtUtc"
     LIMIT @batchSize
     FOR UPDATE SKIP LOCKED
     ```
   - Marca las filas reclamadas como `Status = Processing`, `LastAttemptAtUtc = UtcNow` y confirma la transacción de reclamación antes de realizar llamadas de red al proveedor externo (nunca se mantiene una transacción de BD abierta durante el I/O SMTP/HTTP).
   - En EF InMemory (pruebas unitarias) realiza la selección y actualización equivalente sin SQL crudo.
3. **Ejecución y clasificación del resultado**:
   - Verifica primero si la notificación expiró (`ExpiresAtUtc < UtcNow` → `Cancelled`) o si el usuario deshabilitó el canal mientras estaba en cola (`→ Skipped`).
   - Si el destinatario no tiene email registrado (`string.IsNullOrWhiteSpace(person.Email)`), marca `Skipped` (con código `RECIPIENT_ADDRESS_MISSING`) sin reintentar.
   - Invoca al `INotificationChannelProvider` correspondiente al `Channel`.
   - Si el proveedor responde éxito: pasa a `Sent` (o `Delivered` si el proveedor lo confirma), guarda `ProviderMessageId` y `SentAtUtc`.
   - Si responde error **transitorio** (`IsTransient == true`) y `AttemptCount + 1 < MaxAttempts`: incrementa `AttemptCount`, vuelve a `Pending` y calcula:
     $$\text{delaySeconds} = \min\left(\text{MaxRetryDelaySeconds},\ \text{BaseRetryDelaySeconds} \cdot 2^{\text{AttemptCount}-1}\right) + \text{jitterSeconds}$$
     fijando `NextAttemptAtUtc = UtcNow.AddSeconds(delaySeconds)`.
   - Si responde error **permanente** (`IsTransient == false`, p. ej. `PROVIDER_NOT_CONFIGURED`, `INVALID_RECIPIENT_ADDRESS`, `INVALID_CREDENTIALS`) o alcanza `MaxAttempts`: pasa a `Failed`, `NextAttemptAtUtc = null`, y guarda `LastErrorCode` y `LastErrorMessage` truncado y saneado (máx. 500 caracteres, redactando cualquier token/contraseña).

### Decision 9 – Abstracción de proveedores (`INotificationChannelProvider` y `IEmailNotificationProvider`)

- `INotificationChannelProvider`: interfaz base con `NotificationChannel Channel { get; }`, `string ProviderName { get; }` y `Task<NotificationProviderResult> SendAsync(NotificationProviderMessage message, CancellationToken ct)`.
- `IEmailNotificationProvider : INotificationChannelProvider`: implementación `ConfigurableEmailNotificationProvider` (en `SmartGym.Infrastructure/Services/Notifications/`) configurada mediante `EmailNotificationOptions` (`Enabled`, `ProviderName`, `Host`, `Port`, `UseSsl`, `FromAddress`, `FromName`, `Username`, `Password` desde configuración/variables de entorno).
  - Cuando `EmailNotificationOptions.Enabled == false` o `Host` está vacío (valor por defecto en desarrollo/CI sin servidor de correo), devuelve `NotificationProviderResult.PermanentFailure("PROVIDER_NOT_CONFIGURED", "El proveedor de correo electrónico no está configurado.")`. Nunca simula `Sent`.
- Para `WhatsApp` y `Push`, se registra `UnconfiguredChannelProvider` que devuelve `PermanentFailure("PROVIDER_NOT_CONFIGURED", ...)` hasta que se integre un adaptador concreto.

### Decision 10 – Recordatorios programados (`ClassReminderWorker` e `IClassReminderService`)

- `ClassReminderWorker` (`BackgroundService`) corre cada `NotificationsOptions.ReminderCheckInterval` (por defecto 15 minutos).
- Invoca `IClassReminderService.ProcessUpcomingClassRemindersAsync(CancellationToken)`:
  - Toma `nowUtc = _gymCalendar.UtcNow` y `windowEndUtc = nowUtc.AddHours(_options.ClassReminderLeadHours)` (por defecto 24 horas).
  - Busca reservas con `Status == ReservationStatus.Confirmed` y `IsActive == true` cuyas sesiones (`ClassSession`) estén en `Status == ClassSessionStatus.Open` y `StartsAtUtc > nowUtc && StartsAtUtc <= windowEndUtc`.
  - Excluye aquellas reservas que ya tengan una `NotificationMessage` con `IdempotencyKey == $"class-reminder:{r.ClassSessionId}:{r.Id}:{r.StudentId}"`.
  - Para cada reserva elegible, invoca `INotificationDispatcher.DispatchAsync` con `NotificationType.ClassReminder` y esa `IdempotencyKey`.

### Decision 11 – Superficie de API REST, permisos (`RolesAndPolicies`), códigos de error y auditoría

#### Endpoints del usuario autenticado (`NotificationsController` - `/api/notifications`)

| Método y Ruta | Política / Permiso | Comportamiento |
|---|---|---|
| `GET /api/notifications` | `NOTIFICATIONS_READ_OWN` | Lista paginada (`PagedResult<NotificationDto>`) del `PersonId` del JWT con filtros `isRead`, `category`, `type`, `fromUtc`, `toUtc`, `page`, `pageSize` |
| `GET /api/notifications/my` | `NOTIFICATIONS_READ_OWN` | Alias compatible para clientes existentes |
| `GET /api/notifications/unread-count` | `NOTIFICATIONS_READ_OWN` | Devuelve `{ unreadCount: int }` del `PersonId` autenticado |
| `GET /api/notifications/{id:guid}` | `NOTIFICATIONS_READ_OWN` | Detalle de una notificación propia; `403` si pertenece a otra persona, `404` si no existe |
| `PATCH /api/notifications/{id:guid}/read` | `NOTIFICATIONS_MARK_READ_OWN` | Marca como leída (`IsRead = true`, `ReadAtUtc = UtcNow`); valida titularidad estricta |
| `POST /api/notifications/{id:guid}/read` | `NOTIFICATIONS_MARK_READ_OWN` | Alias `POST` compatible con el frontend actual; misma validación de titularidad |
| `PATCH /api/notifications/{id:guid}/unread` | `NOTIFICATIONS_MARK_READ_OWN` | Marca como no leída (`IsRead = false`, `ReadAtUtc = null`); valida titularidad estricta |
| `POST /api/notifications/mark-all-read` | `NOTIFICATIONS_MARK_READ_OWN` | Marca todas las no leídas propias como leídas y retorna `{ markedCount: int }` |
| `GET /api/notifications/preferences` | `NOTIFICATIONS_PREFERENCES_MANAGE` | Devuelve la matriz efectiva de preferencias del usuario por categoría y canal |
| `PUT /api/notifications/preferences` | `NOTIFICATIONS_PREFERENCES_MANAGE` | Actualiza preferencias permitidas del usuario autenticado |

#### Endpoints administrativos (`AdminNotificationsController` y `AdminNotificationTemplatesController`)

| Método y Ruta | Política / Permiso | Comportamiento |
|---|---|---|
| `GET /api/admin/notifications` | `NOTIFICATIONS_READ_ALL` | Consulta paginada global con filtros `recipientPersonId`, `category`, `type`, `isRead`, `deliveryStatus`, `channel`, `fromUtc`, `toUtc` |
| `GET /api/admin/notifications/{id:guid}/deliveries` | `NOTIFICATIONS_DELIVERY_READ` | Lista los intentos y estado de entrega (`NotificationDeliveryDto`) de una notificación |
| `POST /api/admin/notifications/deliveries/{id:guid}/retry` | `NOTIFICATIONS_RETRY_FAILED` | Reencola una entrega `Failed` o `Skipped` (con motivo opcional) y registra `AuditLog` |
| `POST /api/admin/notifications/send` | `NOTIFICATIONS_MANAGE` | Envío administrativo acotado a una persona existente (también mantenido en `POST /api/notifications` con permiso `NOTIFICATIONS_MANAGE`) |
| `GET /api/admin/notification-templates` | `NOTIFICATIONS_TEMPLATES_READ` | Lista plantillas filtradas por `type`, `channel`, `locale`, `status` |
| `POST /api/admin/notification-templates` | `NOTIFICATIONS_TEMPLATES_MANAGE` | Crea nueva plantilla o nueva versión validada y registra `AuditLog` |
| `PUT /api/admin/notification-templates/{id:guid}` | `NOTIFICATIONS_TEMPLATES_MANAGE` | Actualiza plantilla y registra `AuditLog` |
| `POST /api/admin/notification-templates/{id:guid}/activate` | `NOTIFICATIONS_TEMPLATES_MANAGE` | Activa versión (desactivando la anterior activa del mismo `Type + Channel + Locale`) y registra `AuditLog` |
| `POST /api/admin/notification-templates/{id:guid}/deactivate` | `NOTIFICATIONS_TEMPLATES_MANAGE` | Desactiva plantilla y registra `AuditLog` |

#### Mapa de permisos por rol en `RolesAndPolicies`

- `AllAuthenticated` (`Administrator`, `Secretary`, `Instructor`, `Student`): `NOTIFICATIONS_READ_OWN`, `NOTIFICATIONS_MARK_READ_OWN`, `NOTIFICATIONS_PREFERENCES_MANAGE`.
- `Staff` (`Administrator`, `Secretary`): suma `NOTIFICATIONS_READ_ALL`, `NOTIFICATIONS_DELIVERY_READ`, `NOTIFICATIONS_MANAGE`, `NOTIFICATIONS_TEMPLATES_READ`.
- `Administrator`: suma `NOTIFICATIONS_RETRY_FAILED`, `NOTIFICATIONS_TEMPLATES_MANAGE`.

#### Códigos de error estables (`NotificationErrorCodes`)
- `NOTIFICATION_NOT_FOUND` (404)
- `DELIVERY_NOT_FOUND` (404)
- `TEMPLATE_NOT_FOUND` (404)
- `RECIPIENT_NOT_FOUND` (404)
- `PREFERENCE_MANDATORY_CHANNEL_LOCKED` (409)
- `DELIVERY_INVALID_RETRY_STATE` (409)
- `TEMPLATE_DUPLICATE_VERSION` (409)
- `TEMPLATE_INVALID_VARIABLES` (400)
- `TEMPLATE_UNSAFE_CONTENT` (400)
- `INVALID_ACTION_URL` (400)
- `CONCURRENCY_CONFLICT` (409)

### Decision 12 – Integración Angular con Signals

- **`NotificationsService` (`frontend/src/app/core/services/notifications.service.ts`)**:
  - Mantiene signals reactivos `unreadCount`, `recentNotifications`, `loading` y `error`.
  - Refresca el contador al autenticarse y al abrir el menú desplegable o cambiar de vista principal (sin polling agresivo ni WebSockets).
  - Expone métodos `getNotifications(filter)`, `getUnreadCount()`, `markAsRead(id)`, `markAsUnread(id)`, `markAllAsRead()`, `getPreferences()`, `updatePreferences(request)` y los métodos administrativos de entregas y plantillas.
- **`NavbarComponent`**:
  - Agrega el icono de campana con badge numérico cuando `unreadCount() > 0`, un panel desplegable con las últimas notificaciones, botón "Marcar todas como leídas", navegación segura a `actionUrl` (sólo rutas internas de Angular Router) y enlaces a `/portal/notificaciones` y `/portal/notificaciones/preferencias`.
- **`StudentPortalComponent`**:
  - Se corrige el binding roto (`n.body` y `n.sentAtUtc` / `n.createdAtUtc`) y se enlaza con `NotificationsService`.

## Risks / Trade-offs

- **[Riesgo] Cambio de formato en `GET /api/notifications` (paginado) respecto a `GET /api/notifications/my` (lista)** → Mitigación: se mantiene `GET /api/notifications/my` devolviendo la lista reciente para cualquier consumidor legado mientras el nuevo `NotificationsService` consume `GET /api/notifications` paginado y normaliza `NotificationItem` con alias `body`/`message` y `sentAtUtc`/`createdAtUtc`.
- **[Riesgo] Entregas de email en entornos de desarrollo o pruebas sin servidor SMTP configurado** → Mitigación: `EmailNotificationOptions.Enabled` viene en `false` por defecto; las notificaciones in-app se crean siempre y las entregas de email quedan registradas honestamente con `PROVIDER_NOT_CONFIGURED` sin bloquear flujos ni simular envíos falsos.
- **[Riesgo] Contención en la tabla `"NotificationDeliveries"` con múltiples workers** → Mitigación: el índice filtrado sobre `("Status", "NextAttemptAtUtc")` y la cláusula `FOR UPDATE SKIP LOCKED` en transacciones cortas sólo durante la reclamación del lote eliminan bloqueos entre instancias y evitan mantener locks durante el envío por red.

## Migration Plan

Migración `HardenNotificationsModule`:
1. Alterar `"NotificationMessages"`:
   - Ampliar `"Body"` de `varchar(2000)` a `varchar(4000)`.
   - Agregar columnas `"Category"` (`integer NOT NULL DEFAULT 1`), `"RelatedEntityType"` (`varchar(80) NULL`), `"RelatedEntityId"` (`uuid NULL`), `"ActionUrl"` (`varchar(500) NULL`), `"ReadAtUtc"` (`timestamp with time zone NULL`), `"ExpiresAtUtc"` (`timestamp with time zone NULL`), `"MetadataJson"` (`varchar(2000) NULL`), `"IdempotencyKey"` (`varchar(180) NULL`) y `"CorrelationId"` (`varchar(100) NULL`).
   - Migrar `"Category"` existente a partir de `"Type"` (`Type IN (1)` → `ClassChanges (2)`, `Type IN (2,9,10,11,12,13,14)` → `Reservations (1)`, `Type IN (3,6,7,8)` → `MembershipsAndCredits (3)`, `Type = 4` → `Promotions (5)`, `Type = 5` → `Payments (4)`) y poblar `"ReadAtUtc" = COALESCE("UpdatedAtUtc", "SentAtUtc")` donde `"IsRead" = TRUE`.
   - Cambiar la FK `"FK_NotificationMessages_People_RecipientPersonId"` de `ON DELETE CASCADE` a `ON DELETE RESTRICT`.
   - Crear índices `(RecipientPersonId, SentAtUtc DESC)`, `(RecipientPersonId, Category, SentAtUtc DESC)` y el índice único parcial `UX_NotificationMessages_Recipient_IdempotencyKey` sobre `("RecipientPersonId", "IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL`.
2. Crear tabla `"NotificationDeliveries"` con FK a `"NotificationMessages"` (`ON DELETE CASCADE`), restricción `CK_NotificationDeliveries_Attempts` (`"AttemptCount" >= 0 AND "MaxAttempts" >= 1`), índice único `UX_NotificationDeliveries_Notification_Channel` e índice de cola `IX_NotificationDeliveries_Status_NextAttemptAtUtc` sobre `("Status", "NextAttemptAtUtc")`.
3. Crear tabla `"NotificationPreferences"` con FK a `"People"` (`ON DELETE RESTRICT`) e índice único `UX_NotificationPreferences_Person_Category_Channel` sobre `("PersonId", "Category", "Channel")`.
4. Crear tabla `"NotificationTemplates"` con columna de concurrencia `xmin`, índices únicos `UX_NotificationTemplates_Code_Channel_Locale_Version` y `UX_NotificationTemplates_Active_Type_Channel_Locale` (`WHERE "Status" = 2 AND "IsActive" = TRUE`), e insertar las plantillas semilla activas (`Version = 1`) en `"es-AR"` para `InApp` y `Email` de los tipos del catálogo.
5. `Down`: eliminar `"NotificationTemplates"`, `"NotificationPreferences"` y `"NotificationDeliveries"`, revertir índices/columnas de `"NotificationMessages"` y restaurar la FK.
