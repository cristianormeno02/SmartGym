## Purpose

Gestiona la oferta comercial de planes, membresías adquiridas por alumnos, saldo en unidades de créditos de consumo, reglas de acceso a actividades y el libro mayor (ledger) inmutable de movimientos de créditos.

## ADDED Requirements

### Requirement: Separación entre Plan y Membresía Adquirida
El sistema SHALL diferenciar la definición de un Plan de Membresía (nombre, precio, duración, créditos otorgados, actividades permitidas) de la Membresía Adquirida por un Alumno (fechas de inicio y vencimiento, créditos consumidos, saldo disponible y estado: PENDIENTE, ACTIVA, VENCIDA, SUSPENDIDA, CANCELADA).

#### Scenario: Contratación de plan por alumno
- **WHEN** un alumno adquiere un plan "Mensual 8 clases" con pago registrado
- **THEN** se crea una membresía en estado ACTIVA con vigencia de 30 días y 8 créditos iniciales asignados

### Requirement: Reglas de acceso de membresía a actividades
El sistema SHALL permitir configurar para cada plan qué actividades pueden ser consumidas con sus créditos (e.g., todas las actividades o un subconjunto restringido), impidiendo que el alumno reserve actividades no permitidas por su plan.

#### Scenario: Intento de reserva de actividad no incluida en el plan
- **WHEN** un alumno con membresía "Mensual Running" intenta reservar una clase de "Zumba"
- **THEN** el sistema rechaza la reserva indicando que la actividad no está permitida para su plan

### Requirement: Libro mayor (ledger) inmutable de movimientos de créditos
El sistema SHALL registrar cada alteración de saldo de créditos como un movimiento auditable e inmutable con tipo (CARGA_INICIAL, CONSUMO, DEVOLUCION, AJUSTE_MANUAL, BONIFICACION, COMPENSACION, VENCIMIENTO), cantidad, saldo resultante, motivo, fecha y usuario responsable.

#### Scenario: Consumo de crédito por reserva confirmada o asistencia
- **WHEN** se confirma el consumo de un crédito para una clase de Ejercicio Funcional
- **THEN** el sistema descuenta 1 unidad del saldo de la membresía y crea un registro de movimiento de tipo CONSUMO con el nuevo saldo

### Requirement: Créditos compensatorios y clases individuales
El sistema SHALL permitir la emisión de créditos compensatorios con vigencia y alcance de actividades específico (por ejemplo, ante suspensiones de clases), así como el derecho a clase individual para alumnos sin membresía activa.

#### Scenario: Emisión de crédito compensatorio por clase suspendida
- **WHEN** una clase de Zumba es suspendida por el gimnasio
- **THEN** los alumnos inscriptos reciben 1 crédito compensatorio válido por 15 días aplicable a actividades alternativas autorizadas
