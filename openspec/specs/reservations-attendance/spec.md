## Purpose

Administra el ciclo de vida de las reservas de clases con garantía estricta de cupos bajo concurrencia, el registro independiente de asistencias y las políticas de cancelación y restitución de créditos.

## Requirements

### Requirement: Independencia entre Reserva y Asistencia
El sistema SHALL modelar la asistencia como un hecho operativo independiente de la creación de la reserva, permitiendo que el personal o el instructor asignado registre la presencia efectiva (`ATTENDED`) o la ausencia (`NO_SHOW`) de un alumno con reserva `CONFIRMED`, o registre asistencia sin reserva previa cuando las reglas del gimnasio y el cupo lo permitan. Para un alumno con reserva, el hecho de asistencia SHALL persistirse en la propia reserva (`Status`, `AttendedAtUtc`, `AttendanceSource`, `CheckedInByUserId`), que es su única fuente de verdad; esos datos sólo SHALL modificarse mediante las operaciones de asistencia y nunca mediante las operaciones de reserva.
- La presencia SHALL poder registrarse desde `CheckInOpensMinutesBeforeStart` (por defecto 30) minutos antes del inicio y hasta `AttendanceRegistrationHoursAfterEnd` (por defecto 24) horas después del fin de la clase.
- La ausencia SHALL poder registrarse desde el fin de la clase y dentro del mismo plazo posterior.
- Ambas operaciones SHALL exigir que la clase no esté suspendida, cancelada ni reprogramada, y SHALL ejecutarse bajo el bloqueo de la clase para que no compitan con cancelaciones ni promociones de lista de espera.
- Un instructor sólo SHALL registrar asistencia en las clases donde es instructor programado o efectivo; para una clase ajena la operación se rechaza como recurso no encontrado.
- `ATTENDED` y `NO_SHOW` SHALL ser estados terminales.

#### Scenario: Asistencia de alumno con reserva
- **WHEN** el secretario o el instructor asignado registra la presencia de un alumno con reserva `CONFIRMED` dentro del plazo de registro
- **THEN** la reserva pasa a `ATTENDED`, se registran `AttendedAtUtc`, `AttendanceSource`, `CheckedInByUserId` y la transición en `ReservationStatusChanges`, y se incrementa `AttendedCount` de la clase

#### Scenario: Registro de No-Show
- **WHEN** finalizó una clase y el personal o el instructor asignado registra la inasistencia de un alumno con reserva `CONFIRMED`
- **THEN** la reserva pasa a `NO_SHOW` registrando la transición en `ReservationStatusChanges` y el derecho de acceso consumido no se restituye

#### Scenario: Registro de asistencia fuera de plazo
- **WHEN** se intenta registrar la presencia de un alumno 2 horas antes del inicio de la clase, o su ausencia antes de que la clase termine
- **THEN** el sistema rechaza la operación con `409 Conflict`

#### Scenario: Instructor registra asistencia en una clase ajena
- **WHEN** un instructor sin rol de personal intenta registrar asistencia en una clase donde no es instructor programado ni efectivo
- **THEN** la operación es rechazada como recurso no encontrado
