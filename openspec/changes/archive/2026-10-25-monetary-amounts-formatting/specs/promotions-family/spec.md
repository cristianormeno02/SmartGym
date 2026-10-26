## MODIFIED Requirements

### Requirement: Motor de Condiciones y Beneficios
El sistema SHALL modelar las promociones como un conjunto de condiciones configurables (alumno nuevo, pertenencia a grupo familiar, alumno referido, plan, actividad, horario, fecha) que al cumplirse disparan beneficios específicos (descuento porcentual, descuento fijo, clase gratis, 2x1, segundo alumno gratis o créditos adicionales). Cuando un beneficio corresponda a un importe monetario (descuento de monto fijo), la interfaz SHALL mostrarlo con 2 decimales, coma (`,`) como separador de decimales y punto (`.`) como separador de miles (ej. `$5.000,00 de descuento`).

#### Scenario: Evaluación de promoción Primera Clase Gratis
- **WHEN** un alumno sin historial previo de asistencias ni membresías adquiere su primera clase de prueba
- **THEN** el motor de promociones aplica un beneficio de 100% de descuento y registra el uso de la promoción

#### Scenario: Aplicación de promoción 2x1
- **WHEN** dos alumnos reservan una clase sujeta a una promoción 2x1
- **THEN** el sistema calcula el cobro de una sola plaza y genera el derecho de acceso para ambos alumnos

#### Scenario: Visualización de promoción con beneficio de descuento fijo
- **WHEN** el personal consulta en el panel de administración una promoción cuyo beneficio es de descuento fijo por un valor de `5000`
- **THEN** la interfaz muestra el beneficio con formato monetario `$5.000,00 de descuento`
