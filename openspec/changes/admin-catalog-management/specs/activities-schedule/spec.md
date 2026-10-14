## ADDED Requirements

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
