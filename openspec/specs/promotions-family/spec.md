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
El sistema SHALL registrar cada aplicación efectiva de una promoción (alumno beneficiado, promoción, beneficio concedido, fecha y entidad relacionada), impidiendo que se superen los límites máximos de utilización por persona o vigencia general. La vigencia de la promoción SHALL evaluarse con la fecha local del gimnasio. Cuando el canje corresponde a una membresía, SHALL quedar vinculado a ella, y si esa membresía se cancela sin haber llegado a activarse el canje SHALL anularse: se conserva para auditoría pero deja de contar contra los topes de uso por persona y general.

#### Scenario: Control de tope de usos por persona
- **WHEN** un alumno intenta aplicar nuevamente una promoción limitada a un único uso por persona
- **THEN** el sistema rechaza la aplicación de la promoción informando que el beneficio ya fue consumido con anterioridad

#### Scenario: Anulación del canje por membresía nunca activada
- **WHEN** una membresía `PENDING` contratada con una promoción de un único uso por persona se cancela por falta de pago
- **THEN** el canje queda anulado y el alumno puede volver a aplicar esa promoción en una nueva contratación

#### Scenario: Canje conservado al cancelar una membresía activada
- **WHEN** se cancela una membresía que ya estuvo `ACTIVE` y fue contratada con una promoción
- **THEN** el canje permanece vigente y sigue contando contra los topes de uso
