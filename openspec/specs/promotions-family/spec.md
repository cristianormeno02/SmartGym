## Purpose

Proporciona un motor flexible de reglas promocionales basado en condiciones y beneficios evaluados en el backend, junto con la gestión independiente de grupos familiares y auditoría de uso de beneficios.

## Requirements

### Requirement: Motor de Condiciones y Beneficios
El sistema SHALL modelar las promociones como un conjunto de condiciones configurables (alumno nuevo, pertenencia a grupo familiar, alumno referido, plan, actividad, horario, fecha) que al cumplirse disparan beneficios específicos (descuento porcentual, descuento fijo, clase gratis, 2x1, segundo alumno gratis o créditos adicionales).

#### Scenario: Evaluación de promoción Primera Clase Gratis
- **WHEN** un alumno sin historial previo de asistencias ni membresías adquiere su primera clase de prueba
- **THEN** el motor de promociones aplica un beneficio de 100% de descuento y registra el uso de la promoción

#### Scenario: Aplicación de promoción 2x1
- **WHEN** dos alumnos reservan una clase sujeta a una promoción 2x1
- **THEN** el sistema calcula el cobro de una sola plaza y genera el derecho de acceso para ambos alumnos

### Requirement: Gestión de Grupos Familiares
El sistema SHALL permitir agrupar múltiples Personas bajo una entidad Grupo Familiar de forma independiente a las promociones, permitiendo que las reglas comerciales consulten la cantidad de integrantes activos para activar beneficios familiares.

#### Scenario: Activación de descuento por grupo familiar
- **WHEN** un alumno perteneciente a una familia con 3 o más integrantes activos contrata un plan de membresía
- **THEN** el sistema detecta la condición familiar y aplica automáticamente el descuento estipulado para familias numerosas

### Requirement: Registro de uso de promociones y trazabilidad
El sistema SHALL registrar cada aplicación efectiva de una promoción (alumno beneficiado, promoción, beneficio concedido, fecha y entidad relacionada: membresía o reserva de clase), impidiendo que se superen los límites máximos de utilización por persona o vigencia general. La vigencia de la promoción SHALL evaluarse con la fecha local del gimnasio. Cuando el canje corresponde a una membresía, SHALL quedar vinculado a ella, y si esa membresía se cancela sin haber llegado a activarse el canje SHALL anularse: se conserva para auditoría pero deja de contar contra los topes de uso por persona y general. Cuando el canje habilita el acceso a una clase, SHALL registrarse al confirmarse la reserva y quedar vinculado a ella (`ReservationId`), respaldando una única reserva; SHALL anularse si la reserva se cancela con derecho a restitución (cancelación anticipada, excepción administrativa o suspensión, cancelación o reprogramación de la clase) y SHALL conservarse vigente si la cancelación es tardía o el alumno asistió o se ausentó.

#### Scenario: Control de tope de usos por persona
- **WHEN** un alumno intenta aplicar nuevamente una promoción limitada a un único uso por persona
- **THEN** el sistema rechaza la aplicación de la promoción informando que el beneficio ya fue consumido con anterioridad

#### Scenario: Anulación del canje por membresía nunca activada
- **WHEN** una membresía `PENDING` contratada con una promoción de un único uso por persona se cancela por falta de pago
- **THEN** el canje queda anulado y el alumno puede volver a aplicar esa promoción en una nueva contratación

#### Scenario: Canje conservado al cancelar una membresía activada
- **WHEN** se cancela una membresía que ya estuvo `ACTIVE` y fue contratada con una promoción
- **THEN** el canje permanece vigente y sigue contando contra los topes de uso

#### Scenario: Canje promocional en reserva de clase anulado por suspensión de la clase
- **WHEN** un alumno utiliza una promoción de "Primera Clase Gratis" para reservar una clase y luego el gimnasio suspende esa clase
- **THEN** el canje vinculado a la reserva se anula y el alumno puede utilizar su beneficio en otra clase

#### Scenario: Canje conservado tras cancelación tardía de la reserva
- **WHEN** un alumno que reservó con una promoción de un único uso cancela faltando 30 minutos para el inicio, sin excepción administrativa
- **THEN** el canje permanece vigente y el alumno no puede volver a aplicar la promoción
