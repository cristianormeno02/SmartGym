## Purpose

Gestiona la definición de actividades físicas, salas del establecimiento, plantillas de horarios recurrentes con vigencias y la generación e incidencias operativas de clases concretas.

## Requirements

### Requirement: Definición desacoplada de Actividades y Salas
El sistema SHALL mantener entidades completamente desacopladas para Actividades (definición pura de la disciplina deportiva, identificada por código único, sin sala por defecto, sin instructores y sin capacidades mínima o máxima propias) y Salas (indoor, outdoor o configurables), permitiendo asociar cualquier actividad a salas y horarios en los módulos de programación sin acoplamiento estructural en la entidad Actividad.

#### Scenario: Creación de nueva actividad
- **WHEN** un administrativo registra una actividad con nombre, código canónico, resumen, descripción y capacidad por defecto sugerida
- **THEN** la actividad queda registrada en estado `ACTIVE` lista para ser referenciada por horarios recurrentes o clases concretas sin requerir la asignación de una sala

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

### Requirement: Resolución del cupo efectivo
El sistema SHALL determinar el cupo de un horario recurrente o de una clase concreta en este orden: el cupo indicado explícitamente en la operación; en su defecto, el cupo del horario recurrente de origen (para clases generadas); y en su defecto, la capacidad por defecto de la actividad. Si ninguno está definido, la operación SHALL rechazarse con un error de validación. El sistema SHALL NOT aplicar cupos fijos implícitos.

#### Scenario: Horario sin cupo explícito
- **WHEN** se crea un horario recurrente sin indicar cupo para una actividad con capacidad por defecto 20
- **THEN** el horario se crea con cupo 20

#### Scenario: Clase generada desde un horario con cupo propio
- **WHEN** se genera una clase concreta a partir de un horario con cupo 15 de una actividad con capacidad por defecto 20
- **THEN** la clase se crea con cupo 15

#### Scenario: Sin cupo resoluble
- **WHEN** se crea una clase concreta manual sin indicar cupo para una actividad sin capacidad por defecto
- **THEN** la operación es rechazada indicando que debe especificarse el cupo

### Requirement: Programación restringida a actividades activas
El sistema SHALL permitir crear horarios recurrentes y clases concretas manuales, y generar clases desde horarios, solo para actividades en estado `ACTIVE`.

#### Scenario: Programación de actividad inactiva
- **WHEN** se intenta crear un horario recurrente para una actividad en estado `INACTIVE` o `ARCHIVED`
- **THEN** la operación es rechazada indicando que la actividad no está activa
