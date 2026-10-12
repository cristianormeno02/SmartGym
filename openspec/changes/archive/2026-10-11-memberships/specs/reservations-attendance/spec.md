## MODIFIED Requirements

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
