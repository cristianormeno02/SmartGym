## MODIFIED Requirements

### Requirement: Separación entre Plan y Membresía Adquirida
El sistema SHALL diferenciar la definición comercial de un Plan de Membresía (`MembershipPlans`: nombre, precio de referencia, duración, créditos iniciales, modalidad ilimitada, actividades permitidas y disponibilidad comercial) de la Membresía Adquirida por un Alumno (administrada por la capability `memberships` con su snapshot comercial, estado, vigencia y trazabilidad) y del Libro Mayor de Créditos (`MembershipCredits`).

#### Scenario: Contratación de plan por alumno
- **WHEN** un alumno adquiere un plan "Mensual 8 clases" con pago registrado
- **THEN** se crea una membresía en estado ACTIVA con vigencia de 30 días, snapshot comercial del plan y 8 créditos iniciales asignados en `MembershipCredits`

#### Scenario: Modificación de plan comercial sin impacto retroactivo
- **WHEN** un administrador actualiza el precio o la cantidad de créditos de un `MembershipPlan` existente
- **THEN** el cambio aplica únicamente a las nuevas contrataciones y las membresías ya adquiridas conservan el snapshot comercial registrado en `memberships`

### Requirement: Libro mayor (ledger) inmutable de movimientos de créditos
El sistema SHALL registrar cada alteración de saldo de créditos como un movimiento auditable e inmutable en `MembershipCredits` con tipo (`InitialPurchase` / CARGA_INICIAL, `PromotionBonus` / BONIFICACION, `ClassReservationDebit` / CONSUMO, `ClassCancellationRefund` / DEVOLUCION, `ClassSuspensionCompensation` / COMPENSACION, `ManualAdjustment` / AJUSTE_MANUAL, `SingleClassTicket` / PASE_INDIVIDUAL, `CancellationForfeiture` / BAJA_POR_CANCELACION, `Expiration` / VENCIMIENTO), cantidad, saldo resultante (`BalanceAfter`), motivo, fecha UTC y usuario responsable. Ningún otro módulo SHALL modificar el saldo de una membresía al margen de las operaciones de `MembershipCredits`, y el agotamiento del saldo (`AvailableCredits == 0`) no SHALL alterar el estado de vigencia `ACTIVE` de la membresía.

#### Scenario: Consumo de crédito por reserva confirmada o asistencia
- **WHEN** se confirma el consumo de un crédito para una clase de Ejercicio Funcional
- **THEN** `MembershipCredits` descuenta 1 unidad del saldo disponible de la membresía y crea un registro de movimiento de tipo `ClassReservationDebit` con el nuevo `BalanceAfter`, manteniendo la membresía en estado `ACTIVE` aun cuando el saldo llegue a 0

#### Scenario: Cierre de saldo por vencimiento o cancelación de membresía
- **WHEN** una membresía con 2 créditos remanentes pasa a `EXPIRED` o `CANCELLED`
- **THEN** `MembershipCredits` registra un movimiento de tipo `Expiration` o `CancellationForfeiture` por `-2` con `BalanceAfter = 0` y motivo auditable

### Requirement: Reglas de acceso de membresía a actividades
El sistema SHALL permitir configurar para cada plan qué actividades pueden ser consumidas con sus créditos (todas las actividades o un subconjunto restringido). Al contratar, esa configuración SHALL copiarse a la membresía (snapshot administrado por `memberships`), y el acceso del alumno SHALL evaluarse contra el snapshot de su membresía y no contra la configuración vigente del plan, impidiendo que reserve actividades no incluidas en lo que contrató.

#### Scenario: Intento de reserva de actividad no incluida en el plan
- **WHEN** un alumno con membresía "Mensual Running" intenta reservar una clase de "Zumba"
- **THEN** el sistema rechaza la reserva indicando que la actividad no está permitida para su membresía

#### Scenario: Actividad quitada del plan después de contratar
- **WHEN** un alumno contrató un plan que incluía "CrossFit" y luego el administrador quita "CrossFit" del plan
- **THEN** el alumno sigue pudiendo reservar "CrossFit" con esa membresía durante su vigencia
