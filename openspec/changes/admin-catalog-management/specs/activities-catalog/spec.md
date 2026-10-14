## ADDED Requirements

### Requirement: Control de acceso y herramientas de gestión de actividades
La interfaz web SHALL exponer las acciones de creación, modificación y cambio de estado de Actividades exclusivamente a usuarios autenticados con rol `Administrator` o `Secretary`. Los usuarios no autenticados o con rol `Student` o `Instructor` solo SHALL acceder al catálogo público en modo de consulta, sin controles administrativos y con la misma fuente de datos pública actual.

#### Scenario: Personal autorizado accede a herramientas de gestión de actividades
- **WHEN** un usuario con rol `Administrator` o `Secretary` navega a `/actividades`
- **THEN** la cabecera muestra el botón `+ Nueva Actividad`, cada actividad muestra acciones de edición y cambio de estado, y el listado incluye actividades no activas identificadas con su badge de estado

#### Scenario: Restricción de herramientas de edición para alumnos, instructores y visitantes
- **WHEN** un usuario anónimo o con rol `Student` o `Instructor` navega a `/actividades`
- **THEN** la interfaz no muestra botones de creación, edición ni cambio de estado y solo lista las actividades del catálogo público

### Requirement: Gestión interactiva del Catálogo de Actividades
La aplicación SHALL proveer un panel y formularios de administración para listar con filtros (búsqueda de texto y estado operativo), registrar nuevas actividades y editar existentes consumiendo `/api/activities`. El formulario SHALL normalizar el código a mayúsculas sin espacios en los extremos, validar el color con el formato `#RRGGBB` cuando se informe, exigir `defaultCapacity >= 1` cuando se informe y exigir `minAge <= maxAge` cuando ambos se informen.

#### Scenario: Registro de nueva actividad con normalización de código
- **WHEN** el personal autorizado completa código " crossfit ", nombre "CrossFit WOD", color `#E11D48`, rango etario de 16 a 65 y capacidad sugerida 20
- **THEN** el sistema envía `POST /api/activities` con código `CROSSFIT`, la actividad queda registrada en estado `Active` y aparece en el listado

#### Scenario: Validaciones de cliente bloquean el envío
- **WHEN** el usuario ingresa color `E11D48` (sin `#`), edad mínima 30 y edad máxima 18
- **THEN** el formulario muestra un error en cada campo inválido y no envía la solicitud

#### Scenario: Código duplicado
- **WHEN** el servidor responde `409` al crear porque el código ya existe
- **THEN** el modal permanece abierto y muestra el mensaje devuelto por el servidor junto al campo código

#### Scenario: Edición de metadatos preservando el código
- **WHEN** el personal autorizado edita una actividad para actualizar nombre, descripción o notas de equipamiento
- **THEN** el campo `code` se muestra como solo lectura y el sistema envía `PUT /api/activities/{id}` incluyendo la `version` obtenida del detalle de la actividad

#### Scenario: Detalle no disponible al editar
- **WHEN** no se puede obtener el detalle vigente de la actividad (incluida su `version`)
- **THEN** la interfaz muestra un error y no abre el formulario de edición ni envía la actualización

#### Scenario: Conflicto de concurrencia al editar
- **WHEN** el servidor responde `409` porque la actividad fue modificada por otro usuario
- **THEN** la interfaz informa el conflicto, recarga el detalle actualizado de la actividad y mantiene el modal abierto con los datos vigentes

### Requirement: Cambio de estado de actividades respetando transiciones válidas
La interfaz SHALL ofrecer únicamente las transiciones de estado válidas del dominio para el estado actual de la actividad: desde `Active` solo pausar (a `Inactive`); desde `Inactive` reactivar (a `Active`) o archivar (a `Archived`); desde `Archived` solo desarchivar (a `Inactive`). El cambio SHALL enviarse con `PATCH /api/activities/{id}/status` incluyendo la `version` vigente.

#### Scenario: Acciones disponibles según el estado
- **WHEN** el personal autorizado abre las acciones de una actividad en estado `Active`
- **THEN** la interfaz ofrece "Pausar" y no ofrece "Archivar"

#### Scenario: Cambio de estado exitoso
- **WHEN** el personal autorizado pausa una actividad `Active` sin horarios vigentes ni clases futuras
- **THEN** el sistema envía `PATCH /api/activities/{id}/status`, vuelve a obtener el detalle y actualiza el badge a `Inactive` sin recargar la página

#### Scenario: Inactivación bloqueada por dependencias
- **WHEN** el servidor responde `409` al pausar una actividad porque posee horarios vigentes o clases futuras
- **THEN** la interfaz muestra un diálogo que informa la cantidad de horarios vigentes y de clases futuras devueltas por la API, y el estado permanece sin cambios
