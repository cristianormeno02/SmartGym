## MODIFIED Requirements

### Requirement: Horarios recurrentes con vigencia temporal
El sistema SHALL permitir definir horarios recurrentes para una actividad especificando día de la semana, hora de inicio, hora de fin, sala, instructor principal, secretario opcional (persona activa con cuenta de usuario activa y rol `Secretary` activo), cupo opcional y rango de fechas de vigencia (inicio y fin), manteniendo historial inmutable de vigencias. Los cambios sobre un horario SHALL propagarse a sus clases concretas según la capacidad `class-sessions`, sin alterar las clases ya abiertas, con reservas o modificadas manualmente. Al crear o editar un horario activo, el sistema SHALL rechazar con `409 Conflict` la operación si existe otro horario recurrente activo, con vigencia superpuesta, el mismo día de la semana y un rango horario superpuesto, que use la misma sala o el mismo instructor. La respuesta SHALL incluir un mensaje descriptivo y la lista de conflictos indicando día, rango horario, tipo de conflicto (sala o instructor) y el horario existente con el que colisiona. Al consultar horarios recurrentes, el sistema SHALL incluir el color identificatorio de la actividad (`activityColorHex`) cuando la actividad lo tenga definido.

#### Scenario: Creación de horario recurrente
- **WHEN** se programa una actividad para los días Lunes de 08:30 a 09:30 en Sala Indoor con vigencia desde el 01/10/2026 hasta el 31/12/2026
- **THEN** el sistema persiste el horario recurrente y lo habilita para la generación de clases concretas comprendidas en ese intervalo

#### Scenario: Horario con secretario asignado
- **WHEN** se crea un horario recurrente indicando como secretario a una persona activa con cuenta de usuario activa y rol `Secretary` activo
- **THEN** el sistema persiste el horario y las clases concretas generadas desde ese horario tienen a ese secretario como secretario programado y efectivo

#### Scenario: Rechazo de secretario sin usuario activo o sin rol Secretary
- **WHEN** se intenta crear o actualizar un horario recurrente indicando como secretario a una persona sin cuenta de usuario activa o sin el rol `Secretary` activo (incluyendo personas que solo posean el rol `Administrator` u otros roles)
- **THEN** la operación es rechazada con `400 Bad Request` indicando que el secretario debe ser una persona activa con usuario asignado y rol `Secretary`

#### Scenario: Edición de horario con clases divergentes
- **WHEN** se modifica la hora de un horario que tiene clases futuras abiertas con reservas
- **THEN** el horario se actualiza y la respuesta informa las clases que no se modificaron por estar abiertas, tener reservas o tener ajustes manuales

#### Scenario: Colisión de sala
- **WHEN** se crea un horario el Lunes de 18:30 a 19:30 en Sala Indoor y ya existe un horario activo con vigencia superpuesta el Lunes de 18:00 a 19:00 en Sala Indoor
- **THEN** la operación es rechazada con `409 Conflict` indicando la colisión de sala y el horario existente

#### Scenario: Colisión de instructor
- **WHEN** se crea un horario el Martes de 16:00 a 17:00 en Sala Outdoor con el instructor "Prof. Juan" y ya existe un horario activo con vigencia superpuesta el Martes de 16:30 a 17:30 en Sala Indoor con el mismo instructor
- **THEN** la operación es rechazada con `409 Conflict` indicando la colisión de instructor y el horario existente

#### Scenario: Horarios contiguos o con vigencias disjuntas no colisionan
- **WHEN** se crea un horario en la misma sala e instructor que otro existente, pero comienza exactamente a la hora en que el otro termina, o su vigencia no se superpone con la del otro
- **THEN** el sistema persiste el horario sin reportar colisión

### Requirement: Programación y Gestión de Horarios Recurrentes (Schedules)
La aplicación SHALL proveer una grilla semanal para configurar turnos recurrentes vinculando Actividad (activa), Sala (activa), Instructor (persona activa con rol `Instructor`), Secretario opcional (persona activa con cuenta de usuario y rol `Secretary`), día de la semana, hora de inicio, hora de fin, vigencia (`validFrom` obligatorio, `validTo` opcional) y cupo (`maxCapacity` opcional), consumiendo `/api/schedules`. La interfaz SHALL mostrar los días de Lunes a Domingo y convertirlos al valor numérico de `DayOfWeek` del backend (0 = Domingo … 6 = Sábado). La grilla SHALL poder visualizarse como calendario semanal o como tarjetas por día.

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
- **WHEN** el servidor responde `409` porque ya existe un horario en la misma sala o con el mismo instructor, en el mismo día, rango horario y vigencia
- **THEN** el modal permanece abierto y muestra el mensaje del servidor para que el usuario ajuste sala, instructor, día u horario

#### Scenario: Selector de instructores
- **WHEN** el personal autorizado abre el formulario de horario
- **THEN** el selector de instructor lista solo personas activas con rol `Instructor` activo, obtenidas de `GET /api/people?role=Instructor`

#### Scenario: Selector de secretarios restringido a usuarios con rol Secretary
- **WHEN** el personal autorizado abre el formulario de horario (unitario o masivo)
- **THEN** el selector de secretario lista únicamente personas activas que poseen cuenta de usuario asignada (`hasUserAccount = true`) y rol `Secretary` activo, excluyendo personas sin cuenta de usuario o con otros roles

#### Scenario: Edición de horario sin desactivación implícita
- **WHEN** el personal autorizado edita un horario existente
- **THEN** el formulario no ofrece desactivar el horario, envía `PUT /api/schedules/{id}` con `isActive = true` y conserva como opciones la sala, el instructor y el secretario asignados aunque ya no estén en el listado activo

#### Scenario: Baja de un horario recurrente con advertencia
- **WHEN** el personal autorizado solicita dar de baja un horario
- **THEN** el diálogo de confirmación advierte que se cancelarán las clases futuras generadas por ese horario, y solo tras confirmar el sistema envía `DELETE /api/schedules/{id}`, retira el bloque de la grilla activa y muestra la confirmación de la baja

#### Scenario: Alternancia entre calendario y tarjetas
- **WHEN** el usuario cambia entre la vista Calendario y la vista Tarjetas
- **THEN** la grilla muestra los mismos horarios con los filtros de actividades y salas vigentes, sin volver a consultar el servidor

### Requirement: Carga en lote de horarios recurrentes
El sistema SHALL permitir registrar, mediante `POST /api/schedules/batch`, múltiples horarios recurrentes para una misma actividad, sala, instructor, secretario opcional, vigencia y cupo en una única operación. La solicitud SHALL contener uno o más grupos de horario, cada uno con uno o más días de la semana, una hora de inicio y una hora de fin; el sistema SHALL expandir cada grupo en un horario recurrente por día. El cupo SHALL resolverse con las mismas reglas que el alta unitaria. La operación SHALL ser atómica: si cualquier horario resultante es inválido o colisiona (con horarios existentes por sala o instructor, o con otro horario del mismo lote), el sistema SHALL rechazar la solicitud completa sin persistir horarios ni clases concretas, informando todos los conflictos detectados. Si la validación es exitosa, el sistema SHALL persistir cada horario como un `RecurringSchedule` independiente, generar sus clases concretas y responder `201 Created` con la lista de horarios creados. Un lote SHALL generar como máximo 50 horarios.

#### Scenario: Carga masiva exitosa con múltiples días y horarios
- **WHEN** el personal autorizado envía un lote para la actividad "Funcional", sala "Indoor", instructor "Prof. Juan", cupo 20 y vigencia desde hoy, con los grupos Lunes, Miércoles y Viernes 09:30–10:30 y Martes, Jueves y Viernes 16:00–17:00
- **THEN** el sistema persiste 6 horarios recurrentes independientes, genera sus clases concretas y responde `201 Created` con los 6 horarios creados

#### Scenario: Rechazo atómico ante colisión con un horario existente
- **WHEN** en un lote de 6 horarios, el del Viernes 16:00–17:00 en sala "Indoor" colisiona con un horario activo existente de "Spinning" el Viernes 16:30–17:30 en la misma sala con vigencia superpuesta
- **THEN** el sistema responde `409 Conflict`, no persiste ninguno de los 6 horarios ni clases concretas, y la respuesta incluye un conflicto de tipo sala para el Viernes 16:00–17:00 que identifica el horario de "Spinning" 16:30–17:30

#### Scenario: Reporte de todos los conflictos del lote
- **WHEN** un lote contiene dos horarios que colisionan con horarios existentes (uno por sala y otro por instructor)
- **THEN** la respuesta `409` incluye ambos conflictos, cada uno con su día, rango horario y tipo

#### Scenario: Superposición dentro del mismo lote
- **WHEN** un lote contiene los grupos Lunes 09:00–10:00 y Lunes 09:30–10:30
- **THEN** el sistema responde `409 Conflict` con un conflicto de tipo "dentro del lote" para el Lunes y no persiste ningún horario

#### Scenario: Validación estructural del lote
- **WHEN** el lote no contiene grupos, contiene un grupo sin días, repite un día dentro de un grupo, tiene un grupo con hora de fin igual o anterior a la de inicio, tiene una vigencia con fin anterior al inicio o generaría más de 50 horarios
- **THEN** el sistema responde `400 Bad Request` con un mensaje descriptivo y no persiste ningún horario

#### Scenario: Referencias inválidas en el lote
- **WHEN** el lote referencia una actividad no activa, una sala inactiva, una persona sin rol `Instructor` activo o un secretario sin cuenta de usuario activa o sin rol `Secretary` activo
- **THEN** el sistema rechaza el lote con el mismo error que el alta unitaria y no persiste ningún horario

#### Scenario: Edición individual tras la carga en lote
- **WHEN** después de una carga en lote el personal autorizado cambia el instructor o la hora solo del horario del Viernes 16:00 mediante `PUT /api/schedules/{id}`
- **THEN** solo ese horario y sus clases concretas se modifican; los demás horarios creados en el mismo lote no cambian

### Requirement: Calendario semanal de horarios con filtro por actividad
La interfaz web SHALL proveer, en `/horarios` y en la gestión de horarios del portal administrativo, una vista de calendario semanal (Lunes a Domingo) que ubique cada horario recurrente activo en su día, con posición y altura proporcionales a su hora de inicio y duración, utilizando el color seleccionado en cada actividad (`activityColorHex`, o un color por defecto si no estuviera definido). El calendario SHALL permitir navegar entre semanas y SHALL mostrar solo los horarios cuya vigencia se superpone con la semana visible. Los horarios simultáneos en un mismo día SHALL mostrarse uno al lado del otro sin ocultarse. Por defecto, el calendario SHALL mostrar únicamente las franjas horarias que contienen actividades programadas en la semana visible, omitiendo las horas sin actividad, y SHALL ofrecer una opción para alternar a la visualización de todo el rango horario completo. La vista SHALL incluir filtros combinables que permitan filtrar por actividad (ver todas o seleccionar una o varias) y por sala (ver todas las salas o seleccionar una o varias). Al hacer clic en un horario, la interfaz SHALL mostrar su detalle completo.

#### Scenario: Visualización de todas las actividades
- **WHEN** un usuario abre el calendario con los filtros "Todas las actividades" y "Todas las salas"
- **THEN** el calendario muestra todos los horarios vigentes en la semana visible, organizados por día y hora, pintando cada bloque con el color `activityColorHex` configurado para su actividad

#### Scenario: Filtrado por una o varias actividades
- **WHEN** el usuario selecciona en el filtro las actividades "Funcional" y "Spinning"
- **THEN** el calendario muestra únicamente los horarios de esas dos actividades y el indicador refleja la cantidad de horarios visibles

#### Scenario: Volver a todas las actividades
- **WHEN** el usuario selecciona "Todas las actividades" o desmarca la última actividad seleccionada
- **THEN** el calendario vuelve a mostrar los horarios de todas las actividades

#### Scenario: Filtrado por sala y combinación con actividades
- **WHEN** el usuario selecciona una sala específica (por ejemplo "Sala Indoor") o varias salas en el filtro de salas
- **THEN** la grilla muestra únicamente los horarios que pertenecen a las salas seleccionadas (y a las actividades seleccionadas si hubiera filtro de actividad activo), permitiendo volver a ver todas las salas al elegir "Todas las salas" o desmarcar la última sala

#### Scenario: Visualización por defecto solo de horarios con actividades y opción de rango completo
- **WHEN** el usuario abre el calendario semanal y existen horarios únicamente de 08:00 a 10:00 y de 18:00 a 20:00
- **THEN** por defecto el calendario muestra únicamente las franjas horarias con actividad (08:00–10:00 y 18:00–20:00) sin desplegar las horas intermedias vacías, y al activar la opción de ver todo el rango horario pasa a mostrar la grilla continua completa

#### Scenario: Horarios simultáneos
- **WHEN** dos horarios de distintas salas se dictan el Lunes de 19:00 a 20:00
- **THEN** ambos bloques se muestran lado a lado en la columna del Lunes y ambos son seleccionables

#### Scenario: Vigencia y navegación por semana
- **WHEN** un horario tiene vigencia desde el 01/11/2026 y el usuario visualiza la semana del 19/10/2026
- **THEN** ese horario no aparece en el calendario, y aparece al navegar a una semana que incluya fechas a partir del 01/11/2026

#### Scenario: Detalle del horario para personal autorizado
- **WHEN** un usuario con rol `Administrator` o `Secretary` hace clic sobre un bloque del calendario
- **THEN** se abre un modal con actividad, día, horario, sala, instructor, secretario (si existe), cupo y vigencia, con las acciones Editar (abre el formulario de edición de ese horario) y Dar de baja (abre la confirmación de baja)

#### Scenario: Detalle del horario en solo lectura
- **WHEN** un usuario anónimo o con rol `Student` o `Instructor` hace clic sobre un bloque del calendario
- **THEN** se abre el modal de detalle sin acciones de edición ni baja

#### Scenario: Calendario en pantallas pequeñas
- **WHEN** el calendario se visualiza en una pantalla de ancho menor al breakpoint `md`
- **THEN** la interfaz muestra un selector de día y la lista cronológica de horarios de ese día con el color de su actividad, conservando los filtros de actividad y sala y el acceso al detalle
