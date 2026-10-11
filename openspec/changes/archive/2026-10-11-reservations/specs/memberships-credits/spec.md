## MODIFIED Requirements

### Requirement: Libro mayor (ledger) inmutable de movimientos de créditos
El sistema SHALL registrar cada alteración de saldo de créditos como un movimiento auditable e inmutable en `MembershipCredits` con tipo (`InitialPurchase` / CARGA_INICIAL, `PromotionBonus` / BONIFICACION, `ClassReservationDebit` / CONSUMO, `ClassCancellationRefund` / DEVOLUCION, `ClassSuspensionCompensation` / COMPENSACION, `ManualAdjustment` / AJUSTE_MANUAL, `SingleClassTicket` / PASE_INDIVIDUAL, `CancellationForfeiture` / BAJA_POR_CANCELACION, `Expiration` / VENCIMIENTO), cantidad, saldo resultante (`BalanceAfter`), motivo, fecha UTC, usuario responsable y, cuando el movimiento se origina en una reserva, la referencia a la clase (`ClassSessionId`) y a la reserva (`ReservationId`). Ningún otro módulo SHALL modificar el saldo de una membresía al margen de las operaciones de `MembershipCredits`. Cada reserva SHALL tener a lo sumo un movimiento de consumo y uno de devolución, garantizado por una restricción de unicidad en base de datos: solicitar nuevamente la devolución de una reserva ya devuelta SHALL devolver el movimiento existente sin alterar el saldo. Con una membresía ilimitada los movimientos de consumo y devolución SHALL registrarse con cantidad 0. El agotamiento del saldo (`AvailableCredits == 0`) no SHALL alterar el estado de vigencia `ACTIVE` de la membresía.

#### Scenario: Consumo de crédito por reserva confirmada o asistencia
- **WHEN** se confirma una reserva de una clase de Ejercicio Funcional con una membresía de créditos
- **THEN** `MembershipCredits` descuenta 1 unidad del saldo y crea un movimiento `ClassReservationDebit` vinculado a la reserva con el nuevo `BalanceAfter`, manteniendo la membresía en estado `ACTIVE` aun cuando el saldo llegue a 0

#### Scenario: Idempotencia en devolución de crédito por cancelación de reserva
- **WHEN** se solicita la devolución de crédito para una reserva que ya registra un movimiento de devolución
- **THEN** `MembershipCredits` devuelve el movimiento existente sin incrementar nuevamente `AvailableCredits`

#### Scenario: Cierre de saldo por vencimiento o cancelación de membresía
- **WHEN** una membresía con 2 créditos remanentes pasa a `EXPIRED` o `CANCELLED`
- **THEN** `MembershipCredits` registra un movimiento de tipo `Expiration` o `CancellationForfeiture` por `-2` con `BalanceAfter = 0` y motivo auditable

### Requirement: Créditos compensatorios y clases individuales
El sistema SHALL permitir la emisión de créditos compensatorios con vigencia y alcance de actividades específico (por ejemplo, ante suspensiones de clases), así como el derecho a clase individual para alumnos sin membresía activa.
- Un crédito compensatorio emitido a un alumno con membresía activa SHALL acreditarse en el saldo de esa membresía. Si el alumno no tiene membresía activa, SHALL emitirse como crédito autónomo, con fecha de vencimiento (`ExpiresOn`, por defecto 15 días desde la emisión según `CompensatoryCreditValidityDays`) y alcance de actividad (una actividad o todas).
- Un pase de clase individual (`SingleClassTicket`) SHALL emitirse para una clase concreta.
- Un crédito compensatorio autónomo o un pase individual se consume al quedar vinculado a una reserva, sin generar un nuevo movimiento. El mismo derecho no SHALL respaldar más de una reserva a la vez, y SHALL volver a estar disponible si esa reserva se cancela con derecho a restitución (cancelación anticipada, excepción administrativa o anulación de la clase, en el caso del crédito compensatorio). No SHALL volver a estar disponible tras una cancelación tardía, una asistencia o una ausencia.
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
