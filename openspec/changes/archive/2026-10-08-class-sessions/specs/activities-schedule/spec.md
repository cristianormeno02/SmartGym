## MODIFIED Requirements

### Requirement: Horarios recurrentes con vigencia temporal
El sistema SHALL permitir definir horarios recurrentes para una actividad especificando día de la semana, hora de inicio, hora de fin, sala, instructor principal, secretario opcional, cupo opcional y rango de fechas de vigencia (inicio y fin), manteniendo historial inmutable de vigencias. Los cambios sobre un horario SHALL propagarse a sus clases concretas según la capacidad `class-sessions`, sin alterar las clases ya abiertas, con reservas o modificadas manualmente.

#### Scenario: Creación de horario recurrente
- **WHEN** se programa una actividad para los días Lunes de 08:30 a 09:30 en Sala Indoor con vigencia desde el 01/10/2026 hasta el 31/12/2026
- **THEN** el sistema persiste el horario recurrente y lo habilita para la generación de clases concretas comprendidas en ese intervalo

#### Scenario: Horario con secretario asignado
- **WHEN** se crea un horario recurrente indicando un secretario
- **THEN** las clases concretas generadas desde ese horario tienen a ese secretario como secretario programado y efectivo

#### Scenario: Edición de horario con clases divergentes
- **WHEN** se modifica la hora de un horario que tiene clases futuras abiertas con reservas
- **THEN** el horario se actualiza y la respuesta informa las clases que no se modificaron por estar abiertas, tener reservas o tener ajustes manuales

## REMOVED Requirements

### Requirement: Generación y estados de Clase Concreta
**Reason**: La generación, el modelo y la máquina de estados de las clases concretas pasan a la capacidad dedicada `class-sessions`, que redefine los estados (persistidos vs. derivados) y la generación automática idempotente.
**Migration**: Ver los requisitos "Generación automática e idempotente desde horarios recurrentes" y "Máquina de estados de la clase concreta" en `class-sessions`.

### Requirement: Gestión de suspensiones de clases
**Reason**: Se traslada a `class-sessions` con motivos tipificados y efectos transaccionales sobre reservas y notificaciones.
**Migration**: Ver el requisito "Suspensión de clase concreta" en `class-sessions`.

### Requirement: Trazabilidad de reemplazos de instructores y secretarios
**Reason**: Se traslada a `class-sessions` con historial explícito de reemplazos.
**Migration**: Ver el requisito "Personal programado vs. efectivo con historial" en `class-sessions`.
