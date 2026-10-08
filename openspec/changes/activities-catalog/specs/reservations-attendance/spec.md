## MODIFIED Requirements

### Requirement: Reserva de clases con control estricto de cupos
El sistema SHALL validar que el alumno posea membresía activa, saldo de créditos suficiente, actividad permitida, que la actividad de la clase se encuentre en estado `ACTIVE`, que la edad del alumno esté dentro del rango etario de la actividad cuando su fecha de nacimiento es conocida, y que la clase cuente con cupo disponible, garantizando transaccionalmente que nunca se exceda el cupo máximo aún bajo solicitudes concurrentes.

#### Scenario: Reserva exitosa con saldo y cupo
- **WHEN** un alumno con membresía activa y créditos disponibles solicita reservar una clase con cupos disponibles
- **THEN** se crea la reserva en estado RESERVADA y se decrementa el cupo disponible de la clase concreta

#### Scenario: Intento de reserva en clase completa
- **WHEN** un alumno intenta reservar una clase cuyo cupo ya alcanzó el máximo permitido
- **THEN** el sistema rechaza la reserva por falta de cupo o propone el ingreso a LISTA_ESPERA si está habilitada

#### Scenario: Reserva fuera del rango etario
- **WHEN** un alumno de 15 años con fecha de nacimiento registrada intenta reservar una clase de una actividad con edad máxima 12
- **THEN** el sistema rechaza la reserva indicando que la actividad no admite su edad

#### Scenario: Alumno sin fecha de nacimiento
- **WHEN** un alumno sin fecha de nacimiento registrada reserva una clase de una actividad con rango etario definido
- **THEN** la restricción etaria no se aplica y la reserva continúa con las demás validaciones

#### Scenario: Reserva en actividad no activa
- **WHEN** un alumno intenta reservar una clase cuya actividad está en estado `INACTIVE` o `ARCHIVED`
- **THEN** el sistema rechaza la reserva indicando que la actividad no está disponible
