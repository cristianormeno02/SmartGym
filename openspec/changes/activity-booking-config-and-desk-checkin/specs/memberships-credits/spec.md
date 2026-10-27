## MODIFIED Requirements

### Requirement: Créditos compensatorios y clases individuales
El sistema SHALL permitir la emisión de créditos compensatorios con vigencia y alcance de actividades específico (por ejemplo, ante suspensiones de clases), así como el derecho a clase individual para alumnos sin membresía activa.
- Un crédito compensatorio emitido a un alumno con membresía activa SHALL acreditarse en el saldo de esa membresía. Si el alumno no tiene membresía activa, SHALL emitirse como crédito autónomo, con fecha de vencimiento (`ExpiresOn`, por defecto 15 días desde la emisión según `CompensatoryCreditValidityDays`) y alcance de actividad (una actividad o todas).
- Un pase de clase individual (`SingleClassTicket`) SHALL emitirse para una clase concreta, ya sea por compra previa o por cobro in situ en el Check-in Directo en mostrador.
- Un crédito compensatorio autónomo o un pase individual se consume al quedar vinculado a una reserva, sin generar un nuevo movimiento. El mismo derecho no SHALL respaldar más de una reserva a la vez, y SHALL volver a estar disponible si esa reserva se cancela con derecho a restitución (cancelación anticipada, excepción administrativa, anulación del presente con restitución o anulación de la clase, en el caso del crédito compensatorio). No SHALL volver a estar disponible tras una cancelación tardía, una asistencia vigente o una ausencia.
- Un crédito compensatorio autónomo vencido no SHALL habilitar reservas.

#### Scenario: Emisión de crédito compensatorio por clase suspendida
- **WHEN** una clase de Zumba es suspendida por el gimnasio
- **THEN** los alumnos inscriptos reciben 1 crédito compensatorio válido por 15 días aplicable a actividades alternativas autorizadas

#### Scenario: Uso de un crédito compensatorio autónomo
- **WHEN** un alumno sin membresía activa usa su crédito compensatorio autónomo vigente para reservar una clase
- **THEN** el crédito queda vinculado a esa reserva y no puede respaldar otra mientras la reserva lo retenga

#### Scenario: Liberación de pase de clase individual por cancelación anticipada
- **WHEN** un alumno que reservó con un `SingleClassTicket` cancela su reserva dentro del plazo permitido
- **THEN** el pase queda desvinculado de reservas que lo retienen y puede usarse nuevamente para esa clase

#### Scenario: Liberación de pase individual por anulación del presente
- **WHEN** el personal anula el presente de un drop-in respaldado por un `SingleClassTicket` cobrado in situ
- **THEN** el pase queda desvinculado de la reserva anulada y puede respaldar un nuevo ingreso del alumno a esa misma clase
