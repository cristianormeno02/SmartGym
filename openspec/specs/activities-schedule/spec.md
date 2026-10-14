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

### Requirement: Control de acceso a la gestión de salas y horarios
La interfaz web SHALL permitir crear, editar, activar/desactivar y dar de baja Salas físicas y Horarios recurrentes exclusivamente a usuarios autenticados con rol `Administrator` o `Secretary`.

#### Scenario: Visualización restringida de controles de programación
- **WHEN** un usuario anónimo o con rol `Student` o `Instructor` consulta `/horarios`
- **THEN** la interfaz muestra la grilla horaria pero no expone botones de alta, edición ni baja de turnos

#### Scenario: Acceso de personal autorizado a la grilla de gestión
- **WHEN** un usuario con rol `Administrator` o `Secretary` visualiza `/horarios`
- **THEN** la interfaz expone la acción `+ Nuevo Horario` y botones de edición y baja en cada turno programado

### Requirement: Gestión interactiva de Salas físicas (Rooms)
La aplicación SHALL proveer, en la pestaña "Salas y Espacios" del portal administrativo, una interfaz para listar todas las salas (activas e inactivas), darlas de alta, editarlas y activarlas o desactivarlas, consumiendo `GET /api/rooms`, `POST /api/rooms` y `PUT /api/rooms/{id}`. El nombre SHALL ser obligatorio y la capacidad SHALL ser un entero mayor o igual a 1.

#### Scenario: Alta exitosa de una sala
- **WHEN** el personal autorizado ingresa nombre "Sala de Spinning" y capacidad máxima 25
- **THEN** el sistema envía `POST /api/rooms`, actualiza la lista en pantalla y notifica la creación exitosa

#### Scenario: Validación de nombre y aforo
- **WHEN** el usuario intenta guardar una sala sin nombre o con capacidad menor a 1
- **THEN** el formulario bloquea el envío y muestra un mensaje de validación en el campo correspondiente

#### Scenario: Desactivación de una sala
- **WHEN** el personal autorizado desactiva una sala
- **THEN** el sistema envía `PUT /api/rooms/{id}` con `isActive = false`, la sala se muestra como inactiva en el listado y deja de ofrecerse en el selector de sala de nuevos horarios

### Requirement: Programación y Gestión de Horarios Recurrentes (Schedules)
La aplicación SHALL proveer una grilla semanal para configurar turnos recurrentes vinculando Actividad (activa), Sala (activa), Instructor (persona activa con rol `Instructor`), día de la semana, hora de inicio, hora de fin, vigencia (`validFrom` obligatorio, `validTo` opcional) y cupo (`maxCapacity` opcional), consumiendo `/api/schedules`. La interfaz SHALL mostrar los días de Lunes a Domingo y convertirlos al valor numérico de `DayOfWeek` del backend (0 = Domingo … 6 = Sábado).

#### Scenario: Creación de turno recurrente vinculando recursos
- **WHEN** el personal autorizado selecciona una Actividad, una Sala, un Instructor, el día Lunes, hora de inicio "18:00", hora de fin "19:00", vigencia desde hoy y cupo 20
- **THEN** el sistema envía `POST /api/schedules` con `dayOfWeek = 1`, `startTime = "18:00:00"`, `endTime = "19:00:00"` y la nueva franja se visualiza en la grilla

#### Scenario: Validación de coherencia horaria y de vigencia
- **WHEN** el usuario ingresa una hora de fin igual o anterior a la de inicio, o una fecha de fin de vigencia anterior a la de inicio
- **THEN** el formulario muestra el error de validación correspondiente e impide el envío

#### Scenario: Cupo obligatorio cuando la actividad no define capacidad por defecto
- **WHEN** el usuario selecciona una actividad sin capacidad por defecto y deja el cupo vacío
- **THEN** el formulario marca el cupo como obligatorio e impide el envío

#### Scenario: Colisión de sala
- **WHEN** el servidor responde `409` porque ya existe una clase en la misma sala, día y rango horario
- **THEN** el modal permanece abierto y muestra el mensaje del servidor para que el usuario ajuste sala, día u horario

#### Scenario: Selector de instructores
- **WHEN** el personal autorizado abre el formulario de horario
- **THEN** el selector de instructor lista solo personas activas con rol `Instructor` activo, obtenidas de `GET /api/people?role=Instructor`

#### Scenario: Edición de horario sin desactivación implícita
- **WHEN** el personal autorizado edita un horario existente
- **THEN** el formulario no ofrece desactivar el horario, envía `PUT /api/schedules/{id}` con `isActive = true` y conserva como opciones la sala y el instructor asignados aunque ya no estén activos

#### Scenario: Baja de un horario recurrente con advertencia
- **WHEN** el personal autorizado solicita dar de baja un horario
- **THEN** el diálogo de confirmación advierte que se cancelarán las clases futuras generadas por ese horario, y solo tras confirmar el sistema envía `DELETE /api/schedules/{id}`, retira el bloque de la grilla activa y muestra la confirmación de la baja
