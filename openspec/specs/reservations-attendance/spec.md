## Purpose

Administra el ciclo de vida de las reservas de clases con garantía estricta de cupos bajo concurrencia, el registro independiente de asistencias y las políticas de cancelación y restitución de créditos.

## Requirements

### Requirement: Reserva de clases con control estricto de cupos
El sistema SHALL validar que la clase esté en estado ABIERTA y aún no haya comenzado, que el alumno posea una membresía elegible en la fecha local de la clase según la evaluación de elegibilidad de `memberships` (persona en estado `ACTIVA` con rol `Student`, membresía en estado `ACTIVE`, fecha de la clase dentro de `[StartDate, EndDate]`, membresía no suspendida, actividad de la clase en estado `ACTIVE` e incluida en el snapshot de actividades permitidas de la membresía, y saldo de créditos utilizables o modalidad ilimitada) o un ticket individual válido, que la edad del alumno esté dentro del rango etario de la actividad cuando su fecha de nacimiento es conocida, y que la clase cuente con cupo disponible, garantizando transaccionalmente que nunca se exceda el cupo máximo aún bajo solicitudes concurrentes. Cuando el alumno posee más de una membresía elegible, SHALL usarse la indicada en la solicitud o, si no se indica, la que resulte del orden de selección definido en `memberships` (ilimitadas primero, luego la de vencimiento más próximo).

#### Scenario: Reserva exitosa con saldo y cupo
- **WHEN** un alumno con membresía elegible y créditos disponibles solicita reservar una clase ABIERTA con cupos disponibles
- **THEN** se crea la reserva en estado RESERVADA, se registra el movimiento de débito a través de `MembershipCredits` y se decrementa el cupo disponible de la clase concreta

#### Scenario: Intento de reserva en clase completa
- **WHEN** un alumno intenta reservar una clase cuyo cupo ya alcanzó el máximo permitido
- **THEN** el sistema rechaza la reserva por falta de cupo o propone el ingreso a LISTA_ESPERA si está habilitada

#### Scenario: Reserva en clase no abierta
- **WHEN** un alumno intenta reservar una clase PROGRAMADA, SUSPENDIDA, CANCELADA o REPROGRAMADA, o una clase que ya comenzó
- **THEN** el sistema rechaza la reserva indicando que la clase no admite reservas

#### Scenario: Reserva con membresía pendiente, suspendida o vencida
- **WHEN** un alumno cuya membresía está en estado `PENDING`, `SUSPENDED`, `EXPIRED` (por estado o por `EndDate < classDate`) o `CANCELLED` intenta reservar una clase sin contar con ticket individual
- **THEN** el sistema rechaza la reserva indicando el motivo de inelegibilidad de la membresía

#### Scenario: Reserva fuera del rango etario
- **WHEN** un alumno de 15 años con fecha de nacimiento registrada intenta reservar una clase de una actividad con edad máxima 12
- **THEN** el sistema rechaza la reserva indicando que la actividad no admite su edad

#### Scenario: Alumno sin fecha de nacimiento
- **WHEN** un alumno sin fecha de nacimiento registrada reserva una clase de una actividad con rango etario definido
- **THEN** la restricción etaria no se aplica y la reserva continúa con las demás validaciones

#### Scenario: Reserva en actividad no activa
- **WHEN** un alumno intenta reservar una clase cuya actividad está en estado `INACTIVE` o `ARCHIVED`
- **THEN** el sistema rechaza la reserva indicando que la actividad no está disponible

#### Scenario: Reserva indicando la membresía a utilizar
- **WHEN** un alumno con dos membresías elegibles para la clase indica en la solicitud cuál desea usar
- **THEN** el crédito se consume de la membresía indicada, y si ésta no es elegible la reserva se rechaza con su motivo de inelegibilidad

### Requirement: Independencia entre Reserva y Asistencia
El sistema SHALL modelar la asistencia como un registro independiente de la reserva, permitiendo que un alumno asista habiendo reservado previamente, o asista sin reserva previa cuando las reglas del gimnasio y el cupo lo permitan.

#### Scenario: Asistencia de alumno con reserva
- **WHEN** el secretario o instructor registra la presencia del alumno en la clase concreta
- **THEN** el estado de la reserva se actualiza a ASISTIO y se confirma el movimiento de consumo en su membresía

#### Scenario: Registro de No-Show
- **WHEN** finaliza una clase concreta y un alumno con reserva confirmada no concurrió
- **THEN** el sistema marca la reserva como NO_SHOW y aplica la política de penalización o pérdida de crédito correspondiente

### Requirement: Políticas configurables de cancelación y restitución
El sistema SHALL evaluar el tiempo de antelación respecto del instante de inicio de la clase (`StartsAtUtc`, independiente de la zona horaria) para determinar si una cancelación por parte del alumno califica para devolución de crédito o se considera cancelación tardía sin devolución.

#### Scenario: Cancelación anticipada dentro del plazo permitido
- **WHEN** un alumno cancela su reserva con más de 2 horas de anticipación al inicio de la clase
- **THEN** la reserva cambia a CANCELADA, se libera el cupo en la clase y se restituye el crédito a su membresía con un movimiento de DEVOLUCION

#### Scenario: Cancelación tardía fuera de término
- **WHEN** un alumno cancela su reserva faltando 30 minutos para el inicio de la clase
- **THEN** la reserva cambia a CANCELADA, se libera el cupo pero no se restituye el crédito consumido

### Requirement: Cancelación de reservas por anulación de la clase
Cuando una clase concreta se suspende, se cancela o se reprograma, el sistema SHALL cancelar en la misma transacción todas sus reservas activas (RESERVADA, CONFIRMADA y LISTA_ESPERA), sin considerarlas cancelaciones tardías. SHALL restituir el crédito de las reservas cubiertas por membresía y dejar identificadas, para compensación administrativa, las cubiertas por ticket individual.

#### Scenario: Suspensión con reservas activas
- **WHEN** se suspende una clase con 8 reservas por membresía y 2 en lista de espera, faltando 30 minutos para el inicio
- **THEN** las 10 reservas pasan a CANCELADA, se registran 8 movimientos de DEVOLUCION y ninguna se marca como cancelación tardía

#### Scenario: Reserva con ticket individual
- **WHEN** se cancela una clase en la que un alumno reservó con un ticket individual
- **THEN** la reserva pasa a CANCELADA y queda identificada para compensación administrativa
