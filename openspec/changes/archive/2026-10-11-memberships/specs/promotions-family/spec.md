## MODIFIED Requirements

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
