## Purpose

Administra el ciclo de vida de las reservas de clases con garantía estricta de cupos bajo concurrencia, el registro independiente de asistencias y las políticas de cancelación y restitución de créditos.

## ADDED Requirements

### Requirement: Reserva de clases con control estricto de cupos
El sistema SHALL validar que el alumno posea membresía activa, saldo de créditos suficiente, actividad permitida, y que la clase cuente con cupo disponible, garantizando transaccionalmente que nunca se exceda el cupo máximo aún bajo solicitudes concurrentes.

#### Scenario: Reserva exitosa con saldo y cupo
- **WHEN** un alumno con membresía activa y créditos disponibles solicita reservar una clase con cupos disponibles
- **THEN** se crea la reserva en estado RESERVADA y se decrementa el cupo disponible de la clase concreta

#### Scenario: Intento de reserva en clase completa
- **WHEN** un alumno intenta reservar una clase cuyo cupo ya alcanzó el máximo permitido
- **THEN** el sistema rechaza la reserva por falta de cupo o propone el ingreso a LISTA_ESPERA si está habilitada

### Requirement: Independencia entre Reserva y Asistencia
El sistema SHALL modelar la asistencia como un registro independiente de la reserva, permitiendo que un alumno asista habiendo reservado previamente, o asista sin reserva previa cuando las reglas del gimnasio y el cupo lo permitan.

#### Scenario: Asistencia de alumno con reserva
- **WHEN** el secretario o instructor registra la presencia del alumno en la clase concreta
- **THEN** el estado de la reserva se actualiza a ASISTIO y se confirma el movimiento de consumo en su membresía

#### Scenario: Registro de No-Show
- **WHEN** finaliza una clase concreta y un alumno con reserva confirmada no concurrió
- **THEN** el sistema marca la reserva como NO_SHOW y aplica la política de penalización o pérdida de crédito correspondiente

### Requirement: Políticas configurables de cancelación y restitución
El sistema SHALL evaluar el tiempo de antelación respecto a la hora de inicio de la clase para determinar si una cancelación por parte del alumno califica para devolución de crédito o se considera cancelación tardía sin devolución.

#### Scenario: Cancelación anticipada dentro del plazo permitido
- **WHEN** un alumno cancela su reserva con más de 2 horas de anticipación al inicio de la clase
- **THEN** la reserva cambia a CANCELADA, se libera el cupo en la clase y se restituye el crédito a su membresía con un movimiento de DEVOLUCION

#### Scenario: Cancelación tardía fuera de término
- **WHEN** un alumno cancela su reserva faltando 30 minutos para el inicio de la clase
- **THEN** la reserva cambia a CANCELADA, se libera el cupo pero no se restituye el crédito consumido
