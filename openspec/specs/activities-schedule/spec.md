## Purpose

Gestiona la definición de actividades físicas, salas del establecimiento, plantillas de horarios recurrentes con vigencias y la generación e incidencias operativas de clases concretas.

## Requirements

### Requirement: Definición desacoplada de Actividades y Salas
El sistema SHALL mantener entidades independientes para Actividades (con descripción, resumen, cupos mínimo/máximo, edades e imágenes) y Salas (indoor, outdoor o configurables), permitiendo asociar cualquier actividad a una sala sin cambios estructurales.

#### Scenario: Creación de nueva actividad
- **WHEN** un administrativo registra una actividad con nombre, resumen, cupo mínimo, cupo máximo y sala por defecto
- **THEN** la actividad queda registrada en estado Habilitada lista para asignarse a horarios recurrentes

### Requirement: Horarios recurrentes con vigencia temporal
El sistema SHALL permitir definir horarios recurrentes para una actividad especificando día de la semana, hora de inicio, hora de fin, sala, instructor principal y rango de fechas de vigencia (inicio y fin), manteniendo historial inmutable de vigencias.

#### Scenario: Creación de horario recurrente
- **WHEN** se programa una actividad para los días Lunes de 08:30 a 09:30 en Sala Indoor con vigencia desde el 01/10/2026 hasta el 31/12/2026
- **THEN** el sistema persiste el horario recurrente y lo habilita para la generación de clases concretas comprendidas en ese intervalo

### Requirement: Generación y estados de Clase Concreta
El sistema SHALL instanciar clases concretas a partir de los horarios recurrentes para fechas específicas, registrando actividad, fecha, horario, sala, cupo, instructor programado, instructor efectivo, secretario programado, secretario efectivo y estado operativo (PROGRAMADA, ABIERTA, COMPLETA, EN_CURSO, FINALIZADA, SUSPENDIDA, CANCELADA, REPROGRAMADA).

#### Scenario: Instanciación de clase concreta
- **WHEN** el proceso de generación de calendario procesa un horario recurrente para una fecha determinada
- **THEN** se crea una clase concreta con estado PROGRAMADA con los instructores y secretarios asignados

### Requirement: Gestión de suspensiones de clases
El sistema SHALL permitir suspender una clase concreta registrando motivo (clima, feriado, enfermedad, mantenimiento, otro), observaciones y usuario responsable, sin alterar el horario recurrente original y disparando las acciones de compensación y notificación correspondientes.

#### Scenario: Suspensión de clase por clima
- **WHEN** un administrativo suspende una clase concreta indicando motivo "clima" y observaciones
- **THEN** el estado de la clase cambia a SUSPENDIDA, las reservas asociadas se cancelan y se restituyen o compensan los créditos consumidos a los alumnos

### Requirement: Trazabilidad de reemplazos de instructores y secretarios
El sistema SHALL registrar independientemente quién fue el instructor/secretario programado y quién dictó/controló efectivamente la clase, junto con el motivo del reemplazo y la auditoría correspondiente.

#### Scenario: Reemplazo de instructor en clase concreta
- **WHEN** se asigna un instructor efectivo diferente al instructor programado indicando el motivo de reemplazo
- **THEN** la clase conserva ambos identificadores (programado y efectivo) y registra el evento para reportes y estadísticas de dictado
