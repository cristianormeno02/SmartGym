## ADDED Requirements

### Requirement: Asignación interactiva de membresías a alumnos desde la interfaz de gestión
La interfaz web de administración SHALL permitir a los usuarios con rol de personal administrativo (`Administrator` o `Secretary`) consultar el historial de membresías y realizar la asignación directa e inmediata de un plan de membresía a un alumno desde su ficha de detalle (`/portal/personas/:id`), consumiendo el servicio `POST /api/memberships/assign`.

#### Scenario: Visualización de membresías en la ficha de la persona
- **WHEN** un usuario administrativo o de secretaría accede a la ficha de una persona en `/portal/personas/:id`
- **THEN** la interfaz consulta las membresías del alumno a través de `GET /api/memberships/student/{studentId}` y muestra la sección de membresías con el estado, plan contratado, rango de fechas y créditos disponibles

#### Scenario: Apertura del modal de asignación y selección de plan
- **WHEN** el operador hace clic en "+ Asignar Membresía" para un alumno en estado `ACTIVA`
- **THEN** se abre un modal que lista los planes de membresía activos obtenidos de `GET /api/membershipplans?onlyActive=true`, mostrando para cada plan su precio con formato monetario (`$XX.XXX,XX`), duración en días, tipo de pase (créditos o ilimitado) y actividades permitidas, con la fecha de inicio precargada en la fecha actual

#### Scenario: Asignación exitosa con activación inmediata
- **WHEN** el operador selecciona un plan activo, confirma la fecha de inicio y presiona "Confirmar Asignación"
- **THEN** la aplicación envía `POST /api/memberships/assign` con el `studentId`, `membershipPlanId` y `startDate`, cierra el modal al recibir respuesta exitosa (`200 OK`), muestra un mensaje de éxito y actualiza de inmediato el listado de membresías reflejando la membresía en estado `ACTIVE`

#### Scenario: Restricción de asignación a personas sin rol de Alumno o inactivas
- **WHEN** se visualiza la ficha de una persona que no posee el rol `Student` o cuyo estado personal es distinto de `ACTIVA` (ej. `INACTIVA`, `BLOQUEADA` o `FALLECIDA`)
- **THEN** el botón de asignar membresía se presenta deshabilitado con un mensaje aclaratorio o el sistema rechaza la operación indicando la causa impeditiva sin generar movimientos huérfanos

#### Scenario: Validación de fecha de inicio
- **WHEN** el operador intenta ingresar una fecha de inicio anterior a la fecha actual
- **THEN** el selector de fecha del modal impide seleccionar fechas pasadas (`min` fijado en la fecha de hoy) y bloquea la confirmación de la asignación
