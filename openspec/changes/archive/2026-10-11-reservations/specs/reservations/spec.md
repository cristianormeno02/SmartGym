## Purpose

Administra el ciclo de vida integral de las reservas de alumnos para instancias concretas de clases (`ClassSessions`): consulta de clases disponibles con evaluación de elegibilidad en el backend, máquina de estados explícita (`PENDING`, `CONFIRMED`, `WAITLISTED`, `CANCELLED`, `ATTENDED`, `NO_SHOW`) con historial auditable, modalidades de acceso (`MembershipCredit`, `UnlimitedMembership`, `CompensatoryCredit`, `SingleClassTicket`, `PromotionalBenefit`), prevención transaccional de sobreventa y duplicidad en PostgreSQL, idempotencia, políticas configurables de anticipación y cancelación, gestión de listas de espera con promoción automática u oferta temporal, coordinación ante cambios de clases y de membresías, y autorización contextual por rol y ámbito.

## ADDED Requirements

### Requirement: Modelo de dominio, modalidades de acceso e historial de reservas
El sistema SHALL representar cada reserva mediante la entidad `Reservation`, vinculando a un alumno (`StudentId`) con una instancia concreta de clase (`ClassSessionId`), sin duplicar datos personales ni datos operativos de la clase. Cada reserva SHALL registrar:
- Su estado (`Status`), el canal por el que se creó (`Origin`: `StudentSelfService`, `StaffDesk` o `Legacy` para reservas anteriores a este cambio) y la modalidad de acceso (`AccessType`: `MembershipCredit`, `UnlimitedMembership`, `CompensatoryCredit`, `SingleClassTicket`, `PromotionalBenefit`).
- Las referencias al derecho utilizado: `MembershipId`, `CreditMovementId` (débito en el ledger), `RefundCreditMovementId` (devolución), `SingleClassTicketMovementId` (pase individual o crédito compensatorio autónomo), `PaymentId`, `PromotionRedemptionId` y `RequestedPromotionCode` (código solicitado mientras la reserva aún no confirmó).
- Trazabilidad de actores y tiempos en UTC: `CreatedByUserId`, `ReservedAtUtc`, `ConfirmedAtUtc`, `CancelledAtUtc`, `CancelledByUserId`, `AttendedAtUtc`, `CheckedInByUserId`, `WaitlistEnteredAtUtc`, `WaitlistPromotedAtUtc`, `WaitlistOfferExpiresAtUtc`.
- Datos de cancelación y excepciones: `CancellationReason`, `CancellationSource`, `IsLateCancellation`, `IsPolicyOverride`, `PolicyOverrideReason`, `AffectedBySessionChange`.
- `WaitListPosition`, `Notes`, `IdempotencyKey` y la versión de concurrencia optimista (`Version` / `xmin`).

Las reservas no SHALL eliminarse físicamente ni reactivarse, y cada transición de estado SHALL quedar registrada en la tabla `ReservationStatusChanges`, que sólo admite inserciones, con estado anterior, estado nuevo, motivo, usuario responsable (nulo si fue un proceso automático), disparador (`Trigger`) y fecha UTC.

#### Scenario: Trazabilidad de reserva creada por personal administrativo
- **WHEN** un secretario registra una reserva en mostrador para un alumno utilizando su membresía con créditos
- **THEN** la reserva persiste `StudentId` del alumno beneficiario, `CreatedByUserId` del secretario, `Origin = StaffDesk`, `AccessType = MembershipCredit`, `MembershipId`, `CreditMovementId` y una entrada inicial en `ReservationStatusChanges`

#### Scenario: Prohibición de reactivar una reserva cancelada
- **WHEN** se intenta cambiar una reserva en estado `CANCELLED` a `CONFIRMED`, `WAITLISTED` o `PENDING`
- **THEN** el sistema rechaza la transición con `409 Conflict`, exigiendo crear una nueva reserva sujeta a las validaciones vigentes

### Requirement: Máquina de estados de la reserva
El sistema SHALL gestionar los siguientes estados en `ReservationStatus`, conservando los valores numéricos ya persistidos:
- `CONFIRMED` (`Confirmed = 1`): reserva válida que ocupa un cupo (`ReservedCount`) y tiene su derecho de acceso consumido. Absorbe a los estados anteriores `Reserved` (1) y `Confirmed` (2); el valor 2 queda retirado.
- `CANCELLED` (`Cancelled = 3`), `ATTENDED` (`Attended = 4`) y `NO_SHOW` (`NoShow = 5`): estados terminales.
- `WAITLISTED` (`Waitlisted = 6`): alumno en lista de espera; no ocupa cupo ni consume su derecho de acceso.
- `PENDING` (`Pending = 7`): oferta temporal de cupo emitida a un alumno de la lista de espera. Retiene el cupo hasta `WaitlistOfferExpiresAtUtc` sin consumir todavía el derecho de acceso. Es el único origen del estado `PENDING`.

Las transiciones permitidas SHALL ser únicamente:
- Creación → `CONFIRMED` (hay cupo y el derecho se consume en el acto) o `WAITLISTED` (no hay cupo y la lista de espera lo admite).
- `WAITLISTED → CONFIRMED`: promoción automática al liberarse un cupo.
- `WAITLISTED → PENDING`: oferta temporal cuando el modo de promoción es `OfferWithConfirmation`.
- `PENDING → CONFIRMED`: aceptación de la oferta antes de su vencimiento.
- `PENDING → CANCELLED`: rechazo o vencimiento de la oferta, o cambio de la clase o de la membresía.
- `WAITLISTED → CANCELLED`: salida voluntaria, cancelación por el personal, cambio de la clase o de la membresía, o inicio de la clase sin haber sido promovido.
- `CONFIRMED → CANCELLED`: cancelación del alumno o del personal antes del inicio de la clase, o cambio de la clase o de la membresía.
- `CONFIRMED → ATTENDED` y `CONFIRMED → NO_SHOW`: exclusivamente mediante las operaciones de asistencia.

`CANCELLED`, `ATTENDED` y `NO_SHOW` SHALL ser terminales: cualquier operación sobre una reserva en esos estados SHALL rechazarse con `409 Conflict`.

#### Scenario: Rechazo de cancelación sobre reserva ya asistida o con ausencia registrada
- **WHEN** se intenta cancelar una reserva cuyo estado es `ATTENDED` o `NO_SHOW`
- **THEN** el sistema rechaza la operación con `409 Conflict` indicando que la reserva ya posee un estado final de asistencia

#### Scenario: Reserva en espera al iniciar la clase
- **WHEN** una clase comienza y aún tiene una reserva `WAITLISTED` y otra `PENDING` con oferta sin aceptar
- **THEN** el proceso periódico pasa ambas a `CANCELLED` (`CancellationSource = WaitlistExpired` y `WaitlistOfferExpired` respectivamente), libera el cupo retenido por la oferta y no consume ningún derecho de acceso

### Requirement: Consulta eficiente de clases disponibles y evaluación de elegibilidad
El sistema SHALL exponer `GET /api/reservations/available-classes` para que el alumno autenticado (o el personal indicando `studentId`) consulte las clases `Open` futuras de actividades activas en un rango de fechas locales (`from`, `to`, como máximo `AvailableClassesMaxRangeDays`, por defecto 31 días), con filtros opcionales por `activityId`, `roomId` e `instructorId`. Los cupos, la lista de espera y la reserva propia SHALL calcularse en base de datos, y la elegibilidad SHALL evaluarse cargando una sola vez los derechos del alumno, sin consultas por clase. Por cada clase el sistema SHALL informar:
- Identificador, actividad, sala, instructor efectivo, horario (`StartsAtUtc`, `StartsAtLocal`, `EndsAtLocal`), `Capacity`, `AvailableSpots` y `WaitlistCount`.
- Si está dentro de la ventana de anticipación (`MinAdvanceBookingMinutes`, `MaxAdvanceBookingDays`).
- Si el alumno ya posee una reserva activa (`CONFIRMED`, `PENDING` o `WAITLISTED`), con su identificador, estado y posición en espera.
- `CanReserve`, `CanJoinWaitlist`, `SuggestedAccessType`, `SelectedMembershipId`, los créditos utilizables y, cuando no sea elegible, `IneligibilityReasons` con código y mensaje.

La consulta no SHALL consumir créditos ni modificar estado, no SHALL exponer datos de otros alumnos y el backend SHALL revalidar todas las condiciones al crear la reserva.

#### Scenario: Consulta de clases disponibles no consume créditos
- **WHEN** un alumno con 1 crédito disponible consulta `GET /api/reservations/available-classes` varias veces
- **THEN** el sistema devuelve las clases abiertas con `CanReserve = true` y 1 crédito utilizable, y su saldo en `MembershipCredits` permanece en 1 sin nuevos movimientos

#### Scenario: Información de inelegibilidad y reserva existente
- **WHEN** un alumno consulta las clases disponibles, ya tiene reserva confirmada en la primera y la segunda corresponde a una actividad no permitida por su membresía
- **THEN** la primera informa `HasActiveReservation = true` y `CanReserve = false` con motivo `ALREADY_RESERVED`, y la segunda informa `CanReserve = false` con motivo `ACTIVITY_NOT_ALLOWED`

#### Scenario: Rango de consulta excedido
- **WHEN** se solicita `available-classes` con un rango mayor a `AvailableClassesMaxRangeDays`
- **THEN** el sistema responde `400 Bad Request`

### Requirement: Validaciones para crear una reserva y ventanas de anticipación
Antes de crear una reserva o inscribir en lista de espera, el sistema SHALL validar en el backend:
1. La persona existe, está en estado `ACTIVA`, posee rol `Student` activo, cumple el rango etario de la actividad cuando registra fecha de nacimiento (si no la registra, la restricción etaria no se aplica) y cumple las condiciones documentales configuradas (`MEDICAL_CERTIFICATE_REQUIRED`, `UNDERAGE_EMERGENCY_CONTACT_REQUIRED`). Estos bloqueos aplican a todas las modalidades de acceso.
2. La clase existe, su estado persistido es `Open`, su actividad está `ACTIVE` y aún no comenzó (`UtcNow < StartsAtUtc`).
3. Faltan al menos `MinAdvanceBookingMinutes` (por defecto 0) para `StartsAtUtc` y la clase no inicia más allá de `MaxAdvanceBookingDays` (por defecto 30; 0 = sin límite). El personal con permiso `RESERVATIONS_OVERRIDE_POLICY` SHALL poder omitir esta ventana indicando `OverrideBookingWindow = true` y un motivo obligatorio, que se registra en la reserva (`IsPolicyOverride`, `PolicyOverrideReason`) y en auditoría. La excepción nunca SHALL permitir reservar una clase no abierta o ya iniciada.
4. El alumno no posee otra reserva activa (`PENDING`, `CONFIRMED` o `WAITLISTED`) para la misma clase.
5. El alumno dispone de un derecho de acceso válido, resuelto según el requisito "Resolución del derecho de acceso".

Los rechazos de negocio SHALL responder `409 Conflict` con un código estable (`CLASS_NOT_OPEN`, `CLASS_ALREADY_STARTED`, `BOOKING_WINDOW_CLOSED`, `ALREADY_RESERVED`, `NO_ACCESS_RIGHT`, `CLASS_FULL`, `WAITLIST_FULL`, o los códigos de inelegibilidad de `memberships`), y los errores de formato `400 Bad Request`.

#### Scenario: Alumno elegible reserva una clase abierta con cupo disponible
- **WHEN** un alumno activo con membresía elegible y saldo disponible solicita `POST /api/reservations` para una clase `Open` futura con cupo
- **THEN** el sistema crea la reserva en `CONFIRMED`, registra el débito en `MembershipCredits` referenciando la reserva, incrementa `ReservedCount` y devuelve `201 Created`

#### Scenario: Rechazo de reserva sin derecho de acceso válido
- **WHEN** un alumno sin membresía elegible, sin pase individual, sin crédito compensatorio vigente y sin promoción intenta reservar
- **THEN** el sistema rechaza con `409 Conflict` (`NO_ACCESS_RIGHT`) detallando los motivos de inelegibilidad y no modifica el cupo

#### Scenario: Rechazo de reserva en clase no abierta o ya iniciada
- **WHEN** un alumno intenta reservar una clase en estado `Scheduled`, `Suspended`, `Cancelled` o `Rescheduled`, o cuyo `StartsAtUtc <= UtcNow`
- **THEN** el sistema rechaza con `409 Conflict` (`CLASS_NOT_OPEN` o `CLASS_ALREADY_STARTED`)

#### Scenario: Rechazo por fuera de ventana de anticipación
- **WHEN** un alumno intenta reservar una clase que inicia más allá de `MaxAdvanceBookingDays` o dentro de los `MinAdvanceBookingMinutes` previos al inicio
- **THEN** el sistema rechaza con `409 Conflict` (`BOOKING_WINDOW_CLOSED`)

#### Scenario: Reserva fuera del rango etario
- **WHEN** un alumno de 15 años con fecha de nacimiento registrada intenta reservar una clase de una actividad con edad máxima 12, con cualquier modalidad de acceso
- **THEN** el sistema rechaza la reserva con `AGE_NOT_ALLOWED`

#### Scenario: Alumno sin fecha de nacimiento
- **WHEN** un alumno sin fecha de nacimiento registrada reserva una clase de una actividad con rango etario definido
- **THEN** la restricción etaria no se aplica y la reserva continúa con las demás validaciones

#### Scenario: Reserva en actividad no activa
- **WHEN** un alumno intenta reservar una clase cuya actividad está `INACTIVE` o `ARCHIVED`
- **THEN** el sistema rechaza la reserva con `ACTIVITY_NOT_ACTIVE`

#### Scenario: Excepción administrativa a la ventana de anticipación
- **WHEN** un secretario con permiso `RESERVATIONS_OVERRIDE_POLICY` reserva para un alumno una clase abierta que inicia en 40 días, indicando `OverrideBookingWindow = true` y un motivo
- **THEN** la reserva se crea con `IsPolicyOverride = true` y el motivo queda en la reserva y en `AuditLogs`

### Requirement: Resolución del derecho de acceso
El sistema SHALL determinar la modalidad de acceso de una reserva de forma determinista. Una selección explícita en la solicitud SHALL usarse sin alternativa: si no es válida, la reserva se rechaza.
1. `PromotionCode` informado → `PromotionalBenefit`.
2. `SingleClassTicketMovementId` informado → `SingleClassTicket` o `CompensatoryCredit`, según el tipo del movimiento.
3. `MembershipId` informado → `MembershipCredit` o `UnlimitedMembership`, según la membresía.
4. Sin selección explícita, en este orden: membresía elegible según el orden de selección de `memberships`; pase individual del alumno para esa clase; crédito compensatorio autónomo vigente y aplicable a la actividad (primero el de vencimiento más próximo).

El derecho SHALL consumirse sólo al pasar la reserva a `CONFIRMED`. Una reserva `WAITLISTED` o `PENDING` SHALL conservar la modalidad y las referencias solicitadas, y el derecho SHALL revalidarse y resolverse nuevamente al confirmarla.

#### Scenario: Reserva indicando la membresía a utilizar
- **WHEN** un alumno con dos membresías elegibles indica en la solicitud cuál desea usar
- **THEN** el crédito se consume de la membresía indicada, y si ésta no es elegible la reserva se rechaza con su motivo de inelegibilidad sin probar otras modalidades

#### Scenario: Selección automática prioriza la membresía
- **WHEN** un alumno con una membresía elegible y un crédito compensatorio autónomo vigente reserva sin indicar modalidad
- **THEN** la reserva usa la membresía (`AccessType = MembershipCredit`) y el crédito compensatorio permanece disponible

### Requirement: Prevención de sobreventa, unicidad de reservas activas e idempotencia bajo concurrencia
El sistema SHALL garantizar en PostgreSQL que las reservas que ocupan cupo (`CONFIRMED` y `PENDING`) nunca superen `Capacity`, y que un alumno nunca tenga más de una reserva activa (`PENDING`, `CONFIRMED` o `WAITLISTED`) para la misma clase:
- Toda operación que cree, cancele, promueva o registre asistencia de reservas SHALL adquirir bloqueos pesimistas en un orden único: primero las filas de `ClassSessions` involucradas (por `Id` ascendente) y después las membresías de los alumnos (por `StudentId` ascendente). Las operaciones iniciadas desde una membresía (suspensión o cancelación) SHALL respetar el mismo orden. Un interbloqueo o conflicto de versión detectado por la base SHALL responderse como `409 Conflict` (`CONCURRENCY_CONFLICT`), sin efectos parciales.
- La base SHALL imponer el índice único parcial `UX_Reservations_Active_Session_Student` sobre `("ClassSessionId", "StudentId")` para los estados activos. Una reserva `CANCELLED` no SHALL impedir una nueva reserva para la misma clase.
- La creación SHALL admitir una clave de idempotencia (header `Idempotency-Key`) única por alumno. La clave SHALL buscarse antes de cualquier validación de negocio: si ya existe para el mismo alumno y la misma clase, se devuelve la reserva registrada con `200 OK`, cualquiera sea su estado actual; si existe para otra clase, se responde `409 Conflict` (`IDEMPOTENCY_KEY_MISMATCH`).
- Si falla la validación de acceso, el débito de créditos o el canje promocional, la transacción completa SHALL revertirse sin persistir la reserva ni alterar `ReservedCount`.

#### Scenario: Dos solicitudes concurrentes por el último lugar no producen sobreventa
- **WHEN** una clase tiene `Capacity = 10` y `ReservedCount = 9`, y dos alumnos elegibles envían simultáneamente `POST /api/reservations` con `AllowWaitlist = false`
- **THEN** exactamente una reserva queda `CONFIRMED` con `ReservedCount = 10` y la otra recibe `409 Conflict` (`CLASS_FULL`)

#### Scenario: Imposibilidad de duplicar reserva activa
- **WHEN** un alumno con una reserva `CONFIRMED`, `PENDING` o `WAITLISTED` para una clase envía otra solicitud para la misma clase, secuencial o concurrente
- **THEN** el sistema rechaza la segunda con `409 Conflict` (`ALREADY_RESERVED`) sin duplicar la reserva ni descontar otro crédito

#### Scenario: Nueva reserva tras haber cancelado
- **WHEN** un alumno canceló su reserva para una clase y vuelve a reservar la misma clase cumpliendo las condiciones
- **THEN** el sistema crea un nuevo registro y conserva intacta la reserva cancelada

#### Scenario: Reintento HTTP con la misma clave de idempotencia
- **WHEN** el cliente reintenta `POST /api/reservations` con la misma `Idempotency-Key` para el mismo alumno y la misma clase
- **THEN** el sistema devuelve la reserva ya creada con `200 OK`, sin segundo registro, segundo consumo ni eventos duplicados; si la clave se envía para otra clase, responde `409 Conflict` (`IDEMPOTENCY_KEY_MISMATCH`)

#### Scenario: Reversión atómica ante fallo en el consumo de créditos
- **WHEN** durante la creación de una reserva falla el registro del movimiento en `MembershipCredits`
- **THEN** la transacción se revierte, no queda ninguna reserva persistida y el cupo de la clase no cambia

### Requirement: Coordinación idempotente de consumo y devolución de créditos
El módulo `Reservations` no SHALL modificar `Membership.AvailableCredits` ni insertar filas en `MembershipCreditMovements` fuera de `ICreditLedgerService`.
- Al confirmar una reserva con membresía, `Reservations` SHALL solicitar el consumo al ledger vinculando el movimiento a la reserva (`ReservationId`). Con una membresía ilimitada el movimiento SHALL registrarse con cantidad 0, como traza, sin alterar el saldo.
- Ingresar en `WAITLISTED` o recibir una oferta (`PENDING`) no SHALL generar movimientos.
- Cada devolución SHALL ejecutarse mediante el ledger, que SHALL garantizar a lo sumo un débito y una devolución por reserva.
- En `NO_SHOW` y en cancelaciones tardías sin excepción, el crédito SHALL permanecer consumido.

#### Scenario: Cancelación repetida no devuelve créditos dos veces
- **WHEN** se intenta cancelar por segunda vez una reserva ya cancelada, o un handler interno procesa dos veces el mismo evento
- **THEN** la segunda cancelación se rechaza con `409 Conflict` (o se ignora en el handler) y el saldo del alumno no recibe una segunda devolución

#### Scenario: Reserva con membresía ilimitada
- **WHEN** un alumno con membresía ilimitada elegible reserva y luego cancela con anticipación
- **THEN** el ledger registra un débito y una devolución de cantidad 0 vinculados a la reserva y el saldo de la membresía no cambia

### Requirement: Política configurable de cancelación y excepciones administrativas
El sistema SHALL cancelar una reserva (`POST /api/reservations/{id}/cancel`) sólo antes del inicio de la clase (`UtcNow < StartsAtUtc`); después del inicio SHALL responder `409 Conflict` (`CLASS_ALREADY_STARTED`), también para el personal. El motivo es opcional para el alumno; si se omite se registra uno por defecto. La anticipación se evalúa contra `FreeCancellationHoursBeforeStart` (por defecto 2 horas):
- **Cancelación anticipada** (faltan al menos `FreeCancellationHoursBeforeStart` horas): la reserva pasa a `CANCELLED` con `IsLateCancellation = false`, libera el cupo, restituye el derecho de acceso (devolución de crédito, liberación del pase o crédito compensatorio, o anulación del canje promocional) y dispara la promoción de la lista de espera.
- **Cancelación tardía** (falta menos): pasa a `CANCELLED` con `IsLateCancellation = true`, libera el cupo y promueve la lista de espera, pero no restituye el derecho de acceso: el crédito queda consumido, el pase o crédito compensatorio queda utilizado y el canje promocional sigue vigente.
- **Cancelación de `WAITLISTED` o `PENDING`**: pasa a `CANCELLED` con `IsLateCancellation = false` en cualquier momento previo al inicio, sin penalización; libera el cupo retenido si era una oferta y recompone las posiciones de la lista.
- **Excepción administrativa** (`OverridePolicy = true`): un usuario con permiso `RESERVATIONS_OVERRIDE_POLICY` SHALL poder cancelar dentro de la ventana tardía restituyendo el derecho (`IsLateCancellation = false`, `IsPolicyOverride = true`), con `PolicyOverrideReason` obligatorio, que se registra en la reserva, en `ReservationStatusChanges` y en `AuditLogs`. Un usuario sin ese permiso que envíe `OverridePolicy = true` SHALL recibir `403 Forbidden`.

#### Scenario: Cancelación anticipada libera cupo y devuelve el crédito
- **WHEN** un alumno cancela su reserva `CONFIRMED` faltando 3 horas para el inicio, con ventana de 2 horas
- **THEN** la reserva pasa a `CANCELLED` con `IsLateCancellation = false`, se libera el cupo (o lo ocupa el siguiente en espera) y `MembershipCredits` registra un `ClassCancellationRefund` de `+1` asociado a la reserva

#### Scenario: Cancelación tardía libera cupo pero retiene el crédito
- **WHEN** un alumno cancela su reserva `CONFIRMED` faltando 45 minutos para el inicio
- **THEN** la reserva pasa a `CANCELLED` con `IsLateCancellation = true`, se libera el cupo y no se genera movimiento de devolución

#### Scenario: Cancelación tardía con excepción administrativa
- **WHEN** un secretario con permiso `RESERVATIONS_OVERRIDE_POLICY` cancela una reserva faltando 30 minutos indicando `OverridePolicy = true` y el motivo "Emergencia médica acreditada"
- **THEN** la reserva pasa a `CANCELLED` con `IsPolicyOverride = true`, se devuelve el crédito y se audita la excepción con el operador y el motivo

#### Scenario: Cancelación tardía de una reserva con pase individual
- **WHEN** un alumno que reservó con un pase individual cancela faltando 30 minutos, sin excepción administrativa
- **THEN** la reserva pasa a `CANCELLED` con `IsLateCancellation = true` y el pase no puede usarse en otra reserva

#### Scenario: Cancelación después del inicio de la clase
- **WHEN** un alumno o un administrativo intenta cancelar una reserva `CONFIRMED` de una clase que ya comenzó
- **THEN** el sistema rechaza con `409 Conflict` (`CLASS_ALREADY_STARTED`)

### Requirement: Lista de espera y promoción determinista de candidatos
El sistema SHALL soportar listas de espera por clase cuando `EnableWaitlist` esté habilitado, hasta `MaxWaitlistSize` inscriptos (por defecto 20):
- Cuando la clase no tiene cupo, el alumno elegible que solicite ingresar (`POST /api/reservations` con `AllowWaitlist = true`, valor por defecto, o `POST /api/reservations/class-session/{classSessionId}/waitlist`) SHALL quedar en `WAITLISTED` con `WaitlistEnteredAtUtc` y `WaitListPosition` correlativa, sin incrementar `ReservedCount` ni consumir su derecho. Si la lista está deshabilitada o `AllowWaitlist = false` se responde `409` (`CLASS_FULL`); si alcanzó su tope, `409` (`WAITLIST_FULL`).
- Las posiciones SHALL ser reconstruibles desde los datos persistidos (`WaitListPosition`, `WaitlistEnteredAtUtc`, `Id`) y SHALL recompactarse a `1..N` cuando un alumno sale o es promovido.
- Cada vez que haya cupo libre antes del inicio de la clase y falten al menos `MinMinutesBeforeStartForWaitlistPromotion` (por defecto 15) —por cancelación, rechazo o vencimiento de una oferta, o ampliación del cupo de la clase— el sistema SHALL recorrer la lista bajo el bloqueo de la clase, en orden de posición, hasta ocupar los cupos libres:
  1. Revalida la elegibilidad del candidato (persona activa y derecho de acceso vigente).
  2. Un candidato que perdió elegibilidad no SHALL bloquear la promoción: se omite, conserva su posición y se continúa con el siguiente.
  3. En modo `Automatic` (por defecto) el candidato elegible pasa a `CONFIRMED`, se consume su derecho, ocupa el cupo y se registra `WaitlistPromotedAtUtc`.
  4. En modo `OfferWithConfirmation` el candidato pasa a `PENDING` reteniendo el cupo, con `WaitlistOfferExpiresAtUtc = min(UtcNow + WaitlistOfferExpirationMinutes, StartsAtUtc)`. Si acepta (`POST /api/reservations/{id}/accept-waitlist-offer`) antes del vencimiento y sigue siendo elegible, se consume su derecho y pasa a `CONFIRMED`; si ya no es elegible, la aceptación se rechaza con `409`, la oferta se cancela y el cupo pasa al siguiente. Si rechaza (`POST /api/reservations/{id}/decline-waitlist-offer`) o la oferta vence, la reserva pasa a `CANCELLED` y el cupo se ofrece al siguiente candidato.
- Un proceso periódico (`ReservationWaitlistWorker`) SHALL, de forma idempotente, cancelar las ofertas vencidas y, al iniciar la clase, cancelar las reservas `WAITLISTED` y `PENDING` restantes.

#### Scenario: Ingreso a lista de espera no ocupa cupo ni consume crédito
- **WHEN** un alumno ingresa a la lista de espera de una clase completa (`Capacity = 15`, `ReservedCount = 15`)
- **THEN** la reserva se crea en `WAITLISTED` con `WaitListPosition = 1`, `ReservedCount` permanece en 15 y su saldo no se descuenta

#### Scenario: Lista de espera completa
- **WHEN** un alumno intenta ingresar a una lista de espera que ya tiene `MaxWaitlistSize` inscriptos
- **THEN** el sistema rechaza con `409 Conflict` (`WAITLIST_FULL`)

#### Scenario: Liberación de cupo promueve al candidato correcto sin doble asignación
- **WHEN** se cancela una reserva `CONFIRMED` en una clase completa con dos alumnos elegibles en espera (posiciones 1 y 2), incluso bajo operaciones concurrentes
- **THEN** el de posición 1 pasa a `CONFIRMED` consumiendo su crédito, el de posición 2 pasa a posición 1 y `ReservedCount` se mantiene igual a `Capacity`

#### Scenario: Candidato que perdió elegibilidad no bloquea al siguiente
- **WHEN** se libera un cupo, el candidato en posición 1 ya agotó sus créditos y el de posición 2 tiene una membresía elegible con saldo
- **THEN** el sistema omite al primero sin abortar la operación y promueve al segundo a `CONFIRMED`

#### Scenario: Oferta de cupo aceptada
- **WHEN** en modo `OfferWithConfirmation` se libera un cupo y el primer candidato acepta la oferta dentro del plazo
- **THEN** la reserva pasa de `PENDING` a `CONFIRMED`, se consume su crédito en ese momento y `ReservedCount` no cambia respecto del cupo ya retenido

#### Scenario: Oferta de cupo vencida
- **WHEN** una oferta en `PENDING` supera `WaitlistOfferExpiresAtUtc` sin respuesta
- **THEN** el proceso periódico la pasa a `CANCELLED` con `CancellationSource = WaitlistOfferExpired`, sin consumir crédito, y ofrece el cupo al siguiente candidato elegible

#### Scenario: Oferta cercana al inicio de la clase
- **WHEN** se emite una oferta faltando 20 minutos para el inicio con `WaitlistOfferExpirationMinutes = 30`
- **THEN** la oferta vence al inicio de la clase y no después

#### Scenario: Ampliación del cupo promueve la lista de espera
- **WHEN** un administrativo amplía de 10 a 12 el cupo de una clase completa con tres alumnos elegibles en espera
- **THEN** los dos primeros son promovidos y el tercero queda en posición 1

### Requirement: Tratamiento ante suspensión, cancelación y reprogramación de clases
Cuando `ClassSessions` suspende, cancela o reprograma una clase, `Reservations` SHALL procesar en la misma transacción todas sus reservas activas (`PENDING`, `CONFIRMED` y `WAITLISTED`):
- Cada reserva pasa a `CANCELLED` con `IsLateCancellation = false`, `AffectedBySessionChange = true` y `CancellationSource = ClassSessionChange`, registrando la transición en `ReservationStatusChanges`.
- Si consumió un crédito de membresía, se devuelve íntegramente de forma idempotente.
- Si usó un crédito compensatorio autónomo, éste queda liberado para otra reserva dentro de su vigencia. Si usó un canje promocional, el canje se anula para que el alumno conserve su beneficio.
- Si usó un pase individual, que está emitido para esa clase, la reserva queda identificada para compensación administrativa (emisión de un crédito compensatorio o reintegro en `Payments`).
- Ante una **reprogramación**, las reservas no SHALL trasladarse a la nueva clase: se cancelan con restitución del derecho, el detalle de la reserva expone la clase destino (`RescheduledToSessionId`) y el alumno es notificado para que reserve el nuevo horario si lo desea.

#### Scenario: Suspensión de clase restituye derechos de todas las reservas afectadas
- **WHEN** se suspende una clase con 5 reservas `CONFIRMED` con membresía, 1 `CONFIRMED` con promoción de primera clase gratis y 2 `WAITLISTED`
- **THEN** las 8 pasan a `CANCELLED` con `AffectedBySessionChange = true` e `IsLateCancellation = false`, se devuelven los 5 créditos, se anula el canje promocional y `ReservedCount` queda en 0

#### Scenario: Clase cancelada con reserva por pase individual
- **WHEN** se cancela una clase en la que un alumno reservó con un pase individual
- **THEN** la reserva pasa a `CANCELLED` con `AffectedBySessionChange = true` y queda identificada para compensación administrativa

#### Scenario: Reprogramación no traslada la reserva del alumno
- **WHEN** una clase del martes a las 18:00 con reservas activas se reprograma al jueves a las 20:00
- **THEN** las reservas del martes se cancelan con devolución de créditos, no se crean reservas en la clase del jueves y los alumnos son notificados del nuevo horario

### Requirement: Tratamiento ante suspensión y cancelación de membresías
Cuando `Memberships` suspende o cancela una membresía, `Reservations` SHALL cancelar en la misma transacción las reservas activas futuras asociadas a ella (las que caen dentro de la ventana de suspensión, o todas las futuras si la suspensión es abierta o la membresía se cancela), con `CancellationSource = MembershipChange` e `IsLateCancellation = false`, liberando los cupos y promoviendo la lista de espera de cada clase afectada. En una suspensión el crédito SHALL devolverse; en una cancelación de membresía no SHALL devolverse, porque el saldo remanente se da de baja.

#### Scenario: Suspensión de membresía libera el cupo para la lista de espera
- **WHEN** se suspende una membresía con una reserva `CONFIRMED` en una clase completa que tiene un alumno elegible en espera
- **THEN** la reserva se cancela con devolución del crédito y el alumno en espera es promovido al cupo liberado

### Requirement: Reservas con pases individuales, créditos compensatorios y promociones
El sistema SHALL permitir reservar con derechos distintos de una membresía:
- **Pase individual (`SingleClassTicket`)**: movimiento `SingleClassTicket` activo del alumno para esa clase. Si existen pagos en `Payments` vinculados al alumno y la clase, al menos uno SHALL estar `Completed`; en caso contrario se rechaza con `409` (`PAYMENT_NOT_CONFIRMED`).
- **Crédito compensatorio autónomo (`CompensatoryCredit`)**: movimiento `ClassSuspensionCompensation` sin membresía, vigente en la fecha local de la clase y aplicable a su actividad. Permite reservar aun sin membresía activa.
- En ambos casos el derecho se vincula a la reserva mediante `SingleClassTicketMovementId` y un índice único parcial SHALL impedir que el mismo movimiento respalde más de una reserva que lo retiene (`PENDING`, `CONFIRMED`, `ATTENDED`, `NO_SHOW` o `CANCELLED` tardía).
- **Promoción (`PromotionalBenefit`)**: aplica sólo cuando la promoción cubre el acceso completo a la clase (clase gratis o 100% de descuento); si no, se rechaza con `409` (`PROMOTION_NOT_APPLICABLE`). La evaluación y el canje se delegan en `Promotions`, el canje se registra al confirmar la reserva y queda vinculado a ella (`PromotionRedemptionId`), y un canje SHALL respaldar una única reserva. Los descuentos parciales sobre una clase paga se aplican en la compra del pase individual, fuera de este módulo.

#### Scenario: Reserva con pase individual pagado e intento de reutilización
- **WHEN** un alumno sin membresía adquiere un pase individual con pago `Completed`, reserva la clase y luego intenta respaldar otra reserva con el mismo pase
- **THEN** la primera reserva queda `CONFIRMED` con `AccessType = SingleClassTicket` y la segunda se rechaza con `409 Conflict`

#### Scenario: Pase individual con pago sin confirmar
- **WHEN** un alumno intenta reservar con un pase cuyo único pago vinculado está `Pending`
- **THEN** el sistema rechaza con `409 Conflict` (`PAYMENT_NOT_CONFIRMED`)

#### Scenario: Reserva con crédito compensatorio autónomo
- **WHEN** un alumno sin membresía activa, con un crédito compensatorio autónomo vigente y aplicable a la actividad, reserva una clase abierta con cupo
- **THEN** la reserva queda `CONFIRMED` con `AccessType = CompensatoryCredit` y el crédito no puede respaldar otra reserva mientras ésta lo retenga

#### Scenario: Crédito compensatorio vencido
- **WHEN** un alumno sin otro derecho intenta reservar una clase cuya fecha es posterior al vencimiento de su crédito compensatorio
- **THEN** el sistema rechaza con `409 Conflict` (`NO_ACCESS_RIGHT`)

#### Scenario: Reserva con beneficio promocional
- **WHEN** un alumno aplica un código promocional válido de clase gratis
- **THEN** `Promotions` registra el canje, la reserva queda `CONFIRMED` con `AccessType = PromotionalBenefit` y `PromotionRedemptionId`, y un reintento idempotente no vuelve a canjear la promoción

### Requirement: Reservas gestionadas por personal administrativo
El sistema SHALL permitir que usuarios `Secretary` o `Administrator` creen y cancelen reservas, inscriban o retiren de la lista de espera y acepten o rechacen ofertas en nombre de cualquier alumno, registrando siempre el alumno beneficiario (`StudentId`), el operador (`CreatedByUserId` / `CancelledByUserId`), `Origin = StaffDesk` al crear y el motivo u observaciones. Estas reservas SHALL cumplir las mismas validaciones de estado de la clase, capacidad y derecho de acceso: el personal no SHALL sobrevender una clase ni reservar para un alumno sin derecho válido. Las únicas excepciones admitidas SHALL ser la de la ventana de anticipación y la de cancelación tardía, ambas con permiso `RESERVATIONS_OVERRIDE_POLICY`, motivo obligatorio y registro en `AuditLogs`.

#### Scenario: Reserva creada por secretaría registra operador y beneficiario
- **WHEN** un secretario autenticado crea una reserva en nombre de un alumno elegible
- **THEN** la reserva queda a nombre del alumno, almacena `CreatedByUserId` del secretario y `Origin = StaffDesk`, y genera un registro en `AuditLogs` con ambos identificadores

#### Scenario: El personal no puede sobrevender
- **WHEN** un administrador intenta reservar para un alumno en una clase completa con `AllowWaitlist = false`
- **THEN** el sistema rechaza con `409 Conflict` (`CLASS_FULL`)

### Requirement: Autorización contextual, privacidad y auditoría en Reservations
El sistema SHALL validar en el backend la identidad y el ámbito en cada operación, con los permisos `RESERVATIONS_READ`, `RESERVATIONS_CREATE`, `RESERVATIONS_CANCEL`, `RESERVATIONS_MANAGE`, `RESERVATIONS_WAITLIST_READ` y `RESERVATIONS_OVERRIDE_POLICY`:
- **Alumno**: posee `RESERVATIONS_CREATE` y `RESERVATIONS_CANCEL` limitados a sí mismo. Sólo SHALL crear, consultar, cancelar y responder ofertas de sus propias reservas. La identidad se toma del token: si omite `StudentId` se usa su `PersonId`; si envía el de otra persona o accede a una reserva ajena, el backend responde `403 Forbidden`.
- **Instructor** (sin rol de personal): posee `RESERVATIONS_WAITLIST_READ` limitado a sus clases. Sólo SHALL consultar las reservas, la lista de espera y el resumen de asistencia, y registrar asistencia, de las clases en las que es instructor programado o efectivo; para una clase ajena el backend responde `404 Not Found`. No SHALL consultar el historial de un alumno (`403 Forbidden`).
- **Secretario y Administrador**: poseen todos los permisos del módulo; `RESERVATIONS_MANAGE` habilita operar ofertas y listas de espera en nombre de alumnos, y `RESERVATIONS_READ` el listado paginado general (`GET /api/reservations`, con filtros y paginación en servidor) y el detalle de cualquier reserva.

Toda creación, cancelación, ingreso, salida o promoción de lista de espera, aceptación o rechazo de oferta, excepción administrativa y cancelación por cambio de clase o de membresía SHALL registrarse en `AuditLogs` dentro de la misma transacción.

#### Scenario: Alumno intenta consultar o cancelar la reserva de otro alumno
- **WHEN** un usuario con rol `Student` solicita `GET /api/reservations/{idDeOtro}`, `GET /api/reservations/student/{otroStudentId}` o `POST /api/reservations/{idDeOtro}/cancel`
- **THEN** el backend responde `403 Forbidden` sin exponer ni modificar la reserva ajena

#### Scenario: Instructor consulta reservas de una clase no asignada
- **WHEN** un instructor sin rol de personal solicita `GET /api/reservations/class-session/{classSessionId}` de una clase donde no es instructor programado ni efectivo
- **THEN** el backend responde `404 Not Found`

#### Scenario: Instructor consulta el historial de un alumno
- **WHEN** un instructor sin rol de personal solicita `GET /api/reservations/student/{studentId}`
- **THEN** el backend responde `403 Forbidden`

### Requirement: Publicación de eventos y aislamiento frente a fallos de notificación
El sistema SHALL emitir eventos de dominio por los cambios del ciclo de vida de la reserva (`ReservationConfirmed`, `ReservationWaitlisted`, `ReservationCancelled`, `ReservationPromotedFromWaitlist`, `WaitlistOfferCreated`, `WaitlistOfferExpired`) para que `Notifications` genere los avisos al alumno. Estos eventos SHALL despacharse después de confirmada la transacción de la operación, de modo que una falla al construir o persistir una notificación no pueda revertir ni invalidar la reserva, cancelación o promoción; la falla SHALL registrarse en el log operativo. Si la transacción se revierte, los eventos pendientes SHALL descartarse. Las cancelaciones originadas en un cambio de clase no SHALL generar un segundo aviso, porque `ClassSessions` ya notifica ese cambio a los alumnos afectados.

#### Scenario: Falla en Notifications no revierte una reserva confirmada
- **WHEN** un alumno crea una reserva válida y la persistencia de su notificación falla
- **THEN** la reserva queda `CONFIRMED` con su cupo ocupado y su crédito debitado, y la falla queda registrada en los logs

#### Scenario: Operación revertida no notifica
- **WHEN** la creación de una reserva se revierte por un fallo en el consumo de créditos
- **THEN** no se genera ninguna notificación de reserva
