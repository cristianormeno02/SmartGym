## MODIFIED Requirements

### Requirement: Máquina de estados de la reserva
El sistema SHALL gestionar los siguientes estados en `ReservationStatus`, conservando los valores numéricos ya persistidos:
- `CONFIRMED` (`Confirmed = 1`): reserva válida que ocupa un cupo (`ReservedCount`) y tiene su derecho de acceso consumido. Absorbe a los estados anteriores `Reserved` (1) y `Confirmed` (2); el valor 2 queda retirado.
- `CANCELLED` (`Cancelled = 3`) y `NO_SHOW` (`NoShow = 5`): estados terminales.
- `ATTENDED` (`Attended = 4`): estado terminal salvo para la anulación del presente definida en `reservations-attendance`.
- `WAITLISTED` (`Waitlisted = 6`): alumno en lista de espera; no ocupa cupo ni consume su derecho de acceso.
- `PENDING` (`Pending = 7`): oferta temporal de cupo emitida a un alumno de la lista de espera. Retiene el cupo hasta `WaitlistOfferExpiresAtUtc` sin consumir todavía el derecho de acceso. Es el único origen del estado `PENDING`.

Las transiciones permitidas SHALL ser únicamente:
- Creación → `CONFIRMED` (hay cupo y el derecho se consume en el acto) o `WAITLISTED` (no hay cupo y la lista de espera lo admite).
- Creación → `ATTENDED`: exclusivamente mediante el Check-in Directo en mostrador (`IsDropIn = true`).
- `WAITLISTED → CONFIRMED`: promoción automática al liberarse un cupo.
- `WAITLISTED → PENDING`: oferta temporal cuando el modo de promoción es `OfferWithConfirmation`.
- `PENDING → CONFIRMED`: aceptación de la oferta antes de su vencimiento.
- `PENDING → CANCELLED`: rechazo o vencimiento de la oferta, o cambio de la clase o de la membresía.
- `WAITLISTED → CANCELLED`: salida voluntaria, cancelación por el personal, cambio de la clase o de la membresía, o inicio de la clase sin haber sido promovido.
- `CONFIRMED → CANCELLED`: cancelación del alumno o del personal antes del inicio de la clase, o cambio de la clase o de la membresía.
- `CONFIRMED → ATTENDED` y `CONFIRMED → NO_SHOW`: exclusivamente mediante las operaciones de asistencia.
- `ATTENDED → CONFIRMED` (error del operador sobre una reserva previa) y `ATTENDED → CANCELLED` (drop-in anulado, o desistimiento del alumno): exclusivamente mediante la anulación del presente.

Cualquier otra operación sobre una reserva `CANCELLED`, `ATTENDED` o `NO_SHOW` SHALL rechazarse con `409 Conflict`.

#### Scenario: Rechazo de cancelación sobre reserva ya asistida o con ausencia registrada
- **WHEN** se intenta cancelar una reserva cuyo estado es `ATTENDED` o `NO_SHOW` mediante la operación de cancelación
- **THEN** el sistema rechaza la operación con `409 Conflict` indicando que la reserva ya posee un estado final de asistencia

#### Scenario: Reserva en espera al iniciar la clase
- **WHEN** una clase comienza y aún tiene una reserva `WAITLISTED` y otra `PENDING` con oferta sin aceptar
- **THEN** el proceso periódico pasa ambas a `CANCELLED` (`CancellationSource = WaitlistExpired` y `WaitlistOfferExpired` respectivamente), libera el cupo retenido por la oferta y no consume ningún derecho de acceso

#### Scenario: Única salida de ATTENDED mediante anulación del presente
- **WHEN** el personal anula el presente de una reserva `ATTENDED` con un motivo válido dentro del plazo
- **THEN** la reserva pasa a `CONFIRMED` o `CANCELLED` según las reglas de anulación del presente, registrando la transición en `ReservationStatusChanges`

## ADDED Requirements

### Requirement: Rechazo de reservas en actividades sin reserva previa
El sistema SHALL rechazar la creación de reservas (`POST /api/reservations`) y el ingreso en lista de espera para clases cuya actividad tenga `RequiresBooking == false`, cualquiera sea el origen (autoservicio del alumno o mostrador), con `409 Conflict` y el código `BOOKING_NOT_REQUIRED`, informando que el ingreso es directo en recepción por orden de llegada. La validación SHALL ejecutarse con la clase bloqueada, después de la verificación de idempotencia y antes de resolver el derecho de acceso, sin consumir créditos ni alterar cupos.

`GET /api/reservations/available-classes` SHALL informar por cada clase el campo `RequiresBooking`. Para las clases con `RequiresBooking == false` SHALL informar `CanReserve = false` y `CanJoinWaitlist = false`, e incluir en `IneligibilityReasons` el código `BOOKING_NOT_REQUIRED`. En el Portal del Alumno esas clases SHALL mostrarse como informativas: sin acciones de reserva ni de lista de espera y con la insignia "Acceso libre por orden de llegada (ingreso en recepción)".

El botón "Inscribir Reserva" del portal de recepción sólo SHALL ofrecerse para clases futuras `Open` de actividades con `RequiresBooking == true`.

#### Scenario: Alumno intenta reservar una actividad sin reserva previa
- **WHEN** un alumno elegible envía `POST /api/reservations` para una clase de "Musculación" con `RequiresBooking = false`
- **THEN** el sistema rechaza con `409 Conflict` (`BOOKING_NOT_REQUIRED`) sin consumir créditos ni modificar `ReservedCount`

#### Scenario: Personal intenta inscribir por adelantado en una actividad sin reserva previa
- **WHEN** un secretario intenta crear una reserva o inscribir en lista de espera a un alumno para una clase de una actividad con `RequiresBooking = false`
- **THEN** el sistema rechaza con `409 Conflict` (`BOOKING_NOT_REQUIRED`)

#### Scenario: Clases disponibles informan la modalidad
- **WHEN** un alumno consulta `GET /api/reservations/available-classes` y el rango incluye clases de "Spinning" (`RequiresBooking = true`) y de "Sala Libre" (`RequiresBooking = false`)
- **THEN** las clases de "Sala Libre" informan `RequiresBooking = false`, `CanReserve = false`, `CanJoinWaitlist = false` y el motivo `BOOKING_NOT_REQUIRED`, mientras que las de "Spinning" mantienen la evaluación habitual

#### Scenario: Portal del alumno muestra la clase como informativa
- **WHEN** el alumno visualiza una clase con `requiresBooking = false` en la sección de clases disponibles
- **THEN** no se muestran los botones "Reservar" ni "Lista de espera" y se muestra la insignia de acceso libre por orden de llegada
