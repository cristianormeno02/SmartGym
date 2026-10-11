## MODIFIED Requirements

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

## REMOVED Requirements

### Requirement: Reserva de clases con control estricto de cupos
**Reason**: El ciclo de vida de las reservas, la evaluación de elegibilidad multi-acceso, la prevención de sobreventa y la idempotencia se trasladan a la capacidad dedicada `reservations`.
**Migration**: Ver los requisitos "Validaciones para crear una reserva y ventanas de anticipación", "Resolución del derecho de acceso" y "Prevención de sobreventa, unicidad de reservas activas e idempotencia bajo concurrencia" en `reservations`. El estado RESERVADA pasa a `CONFIRMED`.

### Requirement: Políticas configurables de cancelación y restitución
**Reason**: La política de cancelación anticipada, tardía, desde lista de espera y con excepción administrativa se traslada y amplía en `reservations`.
**Migration**: Ver el requisito "Política configurable de cancelación y excepciones administrativas" en `reservations`.

### Requirement: Cancelación de reservas por anulación de la clase
**Reason**: El tratamiento de reservas activas ante suspensión, cancelación o reprogramación de una clase se traslada y detalla en `reservations`.
**Migration**: Ver el requisito "Tratamiento ante suspensión, cancelación y reprogramación de clases" en `reservations`.
