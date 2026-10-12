## Why

La implementación actual de reservas (`Domain/Entities/Reservations/Reservation.cs`, `ReservationService`, `ReservationsController`) es un prototipo con brechas operativas, de seguridad y de trazabilidad:

- **Máquina de estados ambigua y sin historial**: `ReservationStatus` mezcla `Reserved` y `Confirmed` sin transición real entre ambos, no hay tabla de cambios de estado ni registro de quién creó o canceló cada reserva, del canal de origen, ni token de concurrencia.
- **Derecho de acceso sin tipificar**: la reserva no indica qué modalidad habilitó el lugar ni referencia el pago o el canje promocional, y el mismo pase individual puede respaldar varias reservas.
- **Sin protección en base de datos**: sólo existe un índice no único sobre `(ClassSessionId, StudentId)`; nada impide dos reservas activas del mismo alumno bajo concurrencia ni un doble consumo ante un reintento HTTP.
- **Lista de espera y políticas rígidas**: la ventana de cancelación está fija en el código (2 horas), no hay ventanas de anticipación, tope de lista de espera, oferta temporal de cupo ni excepción administrativa auditada. La suspensión o cancelación de una membresía libera cupos sin promover la lista de espera.
- **Brechas de autorización**: cualquier usuario autenticado puede cancelar una reserva ajena por su `Id`, y cualquier instructor puede ver el historial de cualquier alumno o las reservas de clases ajenas. Faltan el listado paginado, el detalle, "mis reservas" y la consulta de clases disponibles con elegibilidad.
- **Notificaciones acopladas a la transacción**: los eventos de dominio se despachan antes de guardar, por lo que una notificación fallida revertiría la operación de negocio.

Este cambio especifica y diseña el módulo **Reservations** (`reservations`), delimitando sus responsabilidades respecto de `ClassSessions`, `Memberships`, `MembershipCredits`, `Payments`, `Promotions` y `Notifications` dentro del monolito modular.

## What Changes

- **BREAKING – Máquina de estados (`ReservationStatus`)**: `Confirmed = 1` (absorbe a `Reserved = 1` y al antiguo `Confirmed = 2`, cuyo valor se retira), `Cancelled = 3`, `Attended = 4`, `NoShow = 5`, `Waitlisted = 6` (antes `WaitList`) y el nuevo `Pending = 7`, reservado a ofertas temporales de cupo desde la lista de espera. Los valores ya persistidos no se renumeran. `Cancelled`, `Attended` y `NoShow` son terminales.
- **BREAKING – Modelo `Reservation`**: suma `Origin`, `AccessType`, `SingleClassTicketMovementId`, `PaymentId`, `PromotionRedemptionId`, `RequestedPromotionCode`, `CreatedByUserId`, `CancelledByUserId`, `CancellationSource`, `IsPolicyOverride`, `PolicyOverrideReason`, `WaitlistEnteredAtUtc`, `WaitlistPromotedAtUtc`, `WaitlistOfferExpiresAtUtc`, `AffectedBySessionChange`, `RescheduledToSessionId`, `Notes`, `IdempotencyKey` y `Version` (`xmin`); y la tabla de sólo inserción `ReservationStatusChanges`.
- **BREAKING – Rutas y contratos (`/api/reservations`)**:
  - Consultas: `GET /` (paginado), `GET /{id}`, `GET /student/{studentId}`, `GET /my`, `GET /my/upcoming`, `GET /available-classes`, `GET /class-session/{classSessionId}` (convive con `/session/{classSessionId}`), `GET /class-session/{classSessionId}/waitlist` y el resumen de asistencia.
  - Operaciones: `POST /` (con header `Idempotency-Key`), `POST /{id}/cancel`, `POST /{id}/accept-waitlist-offer`, `POST /{id}/decline-waitlist-offer`, `POST /class-session/{classSessionId}/waitlist`, `DELETE /class-session/{classSessionId}/waitlist/me`.
  - Asistencia: `POST /attendance` y `POST /{id}/no-show` se mantienen, ahora con ventana temporal, bloqueo de la clase y ámbito del instructor.
  - Respuestas de error con código estable (`CLASS_FULL`, `ALREADY_RESERVED`, `NO_ACCESS_RIGHT`, …).
- **Sobreventa, unicidad e idempotencia en PostgreSQL**: orden único de bloqueo pesimista (clases, luego membresías) también para las operaciones iniciadas desde `Memberships`; índices únicos parciales para reserva activa por alumno y clase, clave de idempotencia, pase o crédito compensatorio retenido y canje promocional.
- **Modalidades de acceso**: resolución determinista entre membresía con créditos, membresía ilimitada, pase individual, crédito compensatorio autónomo y promoción de acceso completo; el derecho se consume sólo al confirmar.
- **Políticas configurables (`ReservationsOptions`)**: ventanas de anticipación, umbral de cancelación sin penalización, excepción administrativa con permiso `RESERVATIONS_OVERRIDE_POLICY` y motivo obligatorio, lista de espera con tope, promoción automática u oferta con vencimiento, y `ReservationWaitlistWorker` para vencer ofertas y cerrar listas al iniciar la clase.
- **Autorización contextual y auditoría**: permisos `RESERVATIONS_READ`, `RESERVATIONS_CREATE`, `RESERVATIONS_CANCEL`, `RESERVATIONS_MANAGE`, `RESERVATIONS_WAITLIST_READ` y `RESERVATIONS_OVERRIDE_POLICY`; el alumno sólo opera sus reservas, el instructor sólo ve sus clases y toda operación se audita.
- **Eventos post-commit**: los eventos de reserva se despachan después de confirmada la transacción, de modo que una falla de notificación no puede revertir una reserva.

## Capabilities

### New Capabilities
- `reservations`: ciclo de vida de las reservas de alumnos para clases concretas: consulta de clases disponibles con elegibilidad evaluada en servidor, máquina de estados con historial, modalidades de acceso, prevención transaccional de sobreventa y duplicidad, idempotencia, políticas de anticipación y cancelación, lista de espera, respuesta ante cambios de clases y de membresías, autorización contextual y auditoría.

### Modified Capabilities
- `reservations-attendance`: queda acotada al registro de asistencia. La presencia y la ausencia de un alumno con reserva se persisten en la reserva y sólo se modifican por las operaciones de asistencia, con ventana temporal y ámbito del instructor. Sus requisitos de reserva, cancelación y anulación de clase se trasladan a `reservations`.
- `memberships-credits`: los movimientos de consumo y devolución referencian la reserva (`ReservationId`) con unicidad en base de datos; los créditos compensatorios autónomos registran vencimiento y alcance de actividad, y junto con los pases individuales se consumen por vinculación a una reserva.
- `memberships`: la cancelación de una membresía cancela sus reservas activas futuras a través de `reservations`, promoviendo la lista de espera, y respeta el orden de bloqueo común.
- `operations-reporting`: suma las notificaciones del ciclo de vida de reservas, generadas después del commit.
- `promotions-family`: el canje promocional puede vincularse a una reserva de clase y se anula o conserva según cómo termine esa reserva.

## Impact

- **Domain**: `Reservation` y `ReservationStatus`; nuevos `ReservationStatusChange`, `ReservationOrigin`, `ReservationAccessType`, `ReservationCancellationSource`, `ReservationChangeTrigger`, `WaitlistPromotionMode`; eventos de reserva e `IPostCommitDomainEvent`; evento `ClassSessionCapacityIncreased`; nuevos valores de `NotificationType`; `ReservationId`, `ExpiresOn` y `ActivityId` en `MembershipCreditMovement`; `ReservationId` en `PromotionRedemption`.
- **Application**: rediseño de `IReservationService` / `ReservationService` y sus DTOs; `ReservationAccessResolver`; `ReservationsOptions`; extensión de `ICreditLedgerService`, `IPromotionService` e `IMembershipEligibilityService` (evaluación en lote); actualización de los tres handlers de cancelación de reservas y de `MembershipService` (orden de bloqueo); handler de notificaciones de reservas y handler de ampliación de cupo; permisos en `RolesAndPolicies`.
- **Infrastructure**: `ReservationConfiguration`, `ReservationStatusChangeConfiguration`, migración `RefactorReservationsModule`, despacho post-commit en `SmartGymDbContext`, `ReservationWaitlistWorker`, políticas en `DependencyInjection`.
- **WebApi**: reescritura de `ReservationsController`.
- **Frontend (Angular)**: `gym.models.ts`, `gym-api.service.ts` y los portales `student` y `staff`.
- **Tests**: ampliación de `SmartGym.Domain.UnitTests/Reservations` y nuevo proyecto `SmartGym.IntegrationTests` con PostgreSQL real (Testcontainers) para migración, índices y concurrencia. Cubren los 22 criterios de aceptación de `design.md`.
