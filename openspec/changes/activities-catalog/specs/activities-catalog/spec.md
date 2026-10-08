## Purpose

Gestiona el catálogo maestro de disciplinas y ofertas de actividad física del gimnasio (Activities): su identificación canónica e inmutable, metadatos descriptivos, restricciones etarias, notas de equipamiento, ciclo de vida con preservación histórica, gestión de medios en almacenamiento de objetos público y su disponibilidad para consulta pública y administración operativa.

## ADDED Requirements

### Requirement: Identificación canónica y unívoca de actividades
El sistema SHALL requerir para cada actividad un código (`Code`) único, no nulo e inmutable, normalizado a mayúsculas y compuesto por 3 a 50 caracteres entre letras, dígitos y guion bajo, junto con un nombre (`Name`) descriptivo editable de forma independiente del código.

#### Scenario: Creación exitosa de actividad con código válido
- **WHEN** un miembro del personal registra una actividad con código " funcional " y nombre "Entrenamiento Funcional"
- **THEN** la actividad queda registrada en estado `ACTIVE` con el código `FUNCIONAL`

#### Scenario: Rechazo por código duplicado
- **WHEN** se intenta crear una actividad con código "zumba" y ya existe una actividad con código `ZUMBA`
- **THEN** la operación SHALL ser rechazada con un error de conflicto (409)

#### Scenario: Rechazo por código con formato inválido
- **WHEN** se intenta crear una actividad con código "ZU" o "POWER-UP"
- **THEN** la operación SHALL ser rechazada con un error de validación (400)

#### Scenario: Actualización de nombre sin alterar el código
- **WHEN** un miembro del personal modifica el nombre de la actividad de "Zumba Inicial" a "Zumba Gold"
- **THEN** el nombre se actualiza y el código `ZUMBA` permanece intacto, ya que la operación de actualización no admite modificar el código

### Requirement: Desacoplamiento conceptual de salas, instructores y horarios
El sistema SHALL permitir la existencia de una actividad sin vinculación a salas, instructores ni horarios. La actividad representa exclusivamente la disciplina deportiva y no almacena sala por defecto ni capacidades mínima o máxima.

#### Scenario: Actividad sin horarios ni salas asociadas
- **WHEN** se crea la actividad `RUNNING` sin capacidad por defecto, sin salas y sin horarios
- **THEN** la actividad se registra en estado `ACTIVE` y queda disponible para ser referenciada por los módulos de programación

### Requirement: Capacidad por defecto sugerida
El sistema SHALL permitir configurar opcionalmente `DefaultCapacity` (entero positivo) como valor sugerido para los horarios y clases de la actividad. Modificarlo SHALL NOT alterar el cupo de horarios ni de clases ya existentes.

#### Scenario: Rechazo de capacidad no positiva
- **WHEN** se intenta definir `DefaultCapacity` igual a 0
- **THEN** la operación es rechazada con un error de validación

#### Scenario: Cambio de capacidad sin efecto retroactivo
- **WHEN** se cambia `DefaultCapacity` de 20 a 25 en una actividad que tiene horarios con cupo heredado de 20
- **THEN** los horarios y clases existentes conservan el cupo 20

### Requirement: Restricciones etarias
El sistema SHALL permitir configurar `MinAge` y `MaxAge` opcionales (enteros ≥ 0 con `MinAge` ≤ `MaxAge`) que restringen las reservas de alumnos cuya fecha de nacimiento es conocida.

#### Scenario: Validación de rangos etarios coherentes
- **WHEN** se define una actividad infantil con edad mínima 6 y edad máxima 12
- **THEN** el sistema persiste el rango

#### Scenario: Rechazo de rangos etarios incoherentes
- **WHEN** se intenta definir edad mínima 15 y edad máxima 10
- **THEN** la operación es rechazada con un error de validación indicando que la edad mínima no puede superar la edad máxima

#### Scenario: Filtrado por edad
- **WHEN** se consulta el catálogo indicando la edad 8
- **THEN** solo se devuelven actividades cuyo rango admite la edad 8, incluidas las que no definen límites

### Requirement: Presentación visual
El sistema SHALL permitir configurar un color identificatorio opcional en formato hexadecimal `#RRGGBB`, normalizado a mayúsculas, junto con un resumen (máximo 250 caracteres), una descripción (máximo 2000) y notas de equipamiento (máximo 500).

#### Scenario: Normalización de color
- **WHEN** se registra el color "#ff8800"
- **THEN** se persiste como `#FF8800`

#### Scenario: Rechazo de color inválido
- **WHEN** se intenta registrar el color "naranja" o "#F80"
- **THEN** la operación es rechazada con un error de validación

### Requirement: Ciclo de vida con transiciones controladas
El sistema SHALL gestionar el estado de una actividad mediante `ACTIVE`, `INACTIVE` y `ARCHIVED`, permitiendo únicamente las transiciones `ACTIVE ↔ INACTIVE` e `INACTIVE ↔ ARCHIVED`. El indicador de actividad vigente SHALL derivarse del estado. Solo las actividades `ACTIVE` SHALL poder recibir nuevos horarios, clases o reservas.

#### Scenario: Transición no permitida
- **WHEN** se intenta pasar una actividad de `ACTIVE` a `ARCHIVED`
- **THEN** la operación es rechazada indicando que primero debe inactivarse

#### Scenario: Inactivación sin compromisos futuros
- **WHEN** un miembro del personal inactiva una actividad cuyos horarios tienen vigencia vencida y que no tiene clases futuras pendientes
- **THEN** el estado cambia a `INACTIVE` y su historial de horarios, clases y reservas permanece inalterado y consultable

#### Scenario: Rechazo de inactivación con compromisos futuros
- **WHEN** se intenta inactivar una actividad que tiene horarios con vigencia actual o futura, o clases con fecha igual o posterior a hoy en estado programada o en curso
- **THEN** el sistema rechaza la operación con un error de conflicto (409) informando la cantidad de horarios y clases pendientes, sin modificar reservas ni créditos

#### Scenario: Restauración de una actividad archivada
- **WHEN** un miembro del personal restaura una actividad `ARCHIVED`
- **THEN** la actividad pasa a `INACTIVE` y solo puede volver a ofrecerse mediante una activación explícita posterior

#### Scenario: Archivadas ocultas por defecto
- **WHEN** se consulta el listado administrativo sin filtro de estado
- **THEN** se devuelven las actividades `ACTIVE` e `INACTIVE` y se excluyen las `ARCHIVED`

### Requirement: Preservación histórica ante borrado
El sistema SHALL permitir el borrado físico de una actividad solo cuando no tenga horarios, clases ni asignaciones a planes de membresía, eliminando también sus medios del almacenamiento. En cualquier otro caso SHALL rechazar el borrado e indicar que la actividad debe inactivarse o archivarse.

#### Scenario: Rechazo de eliminación con dependencias
- **WHEN** se intenta eliminar una actividad que tiene clases históricas o está asignada a un plan de membresía
- **THEN** el sistema responde con un error de conflicto (409) que detalla las dependencias por tipo y no elimina ningún dato

#### Scenario: Eliminación de actividad sin dependencias
- **WHEN** se elimina una actividad recién creada por error, sin dependencias y con un logo cargado
- **THEN** se eliminan la actividad, sus registros de medios y los archivos correspondientes del almacenamiento

### Requirement: Gestión de medios de la actividad
El sistema SHALL administrar el logo y las imágenes de galería de una actividad mediante la entidad `ActivityMedia`, almacenando los archivos en un almacenamiento de objetos público dedicado y persistiendo en la base de datos únicamente la clave del objeto y sus metadatos. Toda imagen SHALL validarse por contenido y re-codificarse a WebP antes de almacenarse. Una actividad SHALL tener a lo sumo un logo, a lo sumo 10 imágenes de galería y a lo sumo una imagen principal.

#### Scenario: Carga exitosa de imagen de galería
- **WHEN** un miembro del personal sube una imagen JPEG de 1,5 MB a la galería de una actividad
- **THEN** el sistema la re-codifica a WebP, la almacena con una clave `activities/{activityId}/gallery/{uuid}.webp`, registra el medio al final del orden de la galería y devuelve su URL pública

#### Scenario: Rechazo de archivo no permitido
- **WHEN** se intenta subir un archivo `.exe`, un archivo cuyo contenido no es una imagen decodificable aunque declare `image/png`, o una imagen de galería de más de 5 MB
- **THEN** la carga es rechazada con un error de validación y no se escribe nada en el almacenamiento

#### Scenario: Límite de galería
- **WHEN** se intenta subir una imagen de galería a una actividad que ya tiene 10
- **THEN** la carga es rechazada con un error de validación

#### Scenario: Reemplazo de logo
- **WHEN** se sube un logo (máximo 2 MB) a una actividad que ya tiene uno
- **THEN** el nuevo logo reemplaza al anterior y el archivo anterior se elimina del almacenamiento

#### Scenario: Primera imagen como principal
- **WHEN** se sube la primera imagen de galería de una actividad
- **THEN** la imagen queda marcada como principal automáticamente

#### Scenario: Cambio de imagen principal
- **WHEN** se designa como principal una imagen de galería distinta de la actual
- **THEN** la imagen previa deja de ser principal y en ningún momento existen dos imágenes principales para la actividad

#### Scenario: Eliminación de la imagen principal
- **WHEN** se elimina la imagen principal de una galería que tiene otras imágenes
- **THEN** la imagen con menor orden pasa a ser la principal

#### Scenario: Reordenamiento de galería
- **WHEN** se envía el orden completo de los identificadores de las imágenes de galería
- **THEN** el sistema actualiza el orden; si la lista no coincide exactamente con las imágenes existentes, rechaza la operación

#### Scenario: Falla al registrar el medio
- **WHEN** el archivo se almacenó correctamente pero falla la persistencia del registro en la base de datos
- **THEN** el sistema elimina el archivo recién almacenado y responde con error

### Requirement: Consulta y catálogo público
El sistema SHALL exponer endpoints públicos sin autenticación que devuelvan únicamente la información comercial de las actividades `ACTIVE`, consultables en listado o individualmente por código sin distinguir mayúsculas, sin exponer identificadores internos, estado, capacidad, auditoría ni metadatos de archivos. Los endpoints administrativos SHALL requerir un rol de personal.

#### Scenario: Consulta pública por visitantes no autenticados
- **WHEN** un visitante anónimo consulta el catálogo público
- **THEN** el sistema retorna las actividades `ACTIVE` ordenadas por nombre con código, nombre, resumen, descripción, edades, notas de equipamiento, color y URLs públicas de logo, imagen principal y galería

#### Scenario: Ficha pública por código
- **WHEN** un visitante consulta la actividad con código "zumba"
- **THEN** el sistema devuelve la ficha pública de la actividad `ZUMBA`

#### Scenario: Ocultamiento de actividades no activas
- **WHEN** una actividad se encuentra en estado `INACTIVE` o `ARCHIVED`
- **THEN** es excluida del listado público y su ficha pública responde como no encontrada (404)

#### Scenario: Acceso anónimo a la API administrativa
- **WHEN** un usuario no autenticado consulta el listado administrativo de actividades
- **THEN** el sistema responde 401

### Requirement: Búsqueda administrativa
El sistema SHALL ofrecer un listado administrativo paginado con búsqueda por nombre o código insensible a mayúsculas y acentos, y con filtros por estado y edad admitida.

#### Scenario: Búsqueda sin acentos
- **WHEN** se busca "pilates aereo"
- **THEN** el resultado incluye la actividad "Pilates Aéreo"

### Requirement: Concurrencia optimista en ediciones
El sistema SHALL detectar ediciones concurrentes de una misma actividad y rechazar la modificación basada en una versión desactualizada.

#### Scenario: Edición con versión desactualizada
- **WHEN** dos miembros del personal cargan la misma actividad, el primero guarda cambios y luego el segundo intenta guardar los suyos
- **THEN** la operación del segundo es rechazada con un error de conflicto (409) sin sobrescribir los cambios del primero

### Requirement: Auditoría y trazabilidad de cambios
El sistema SHALL registrar eventos de auditoría para la creación, edición, cambio de estado y eliminación de actividades y para las operaciones sobre sus medios (alta, baja, cambio de imagen principal y reordenamiento), incluyendo el usuario, la fecha y hora UTC, la entidad y los valores previos y nuevos.

#### Scenario: Registro de auditoría al cambiar estado
- **WHEN** un miembro del personal cambia el estado de una actividad de `ACTIVE` a `INACTIVE`
- **THEN** se registra un evento `ACTIVITY_STATUS_CHANGED` con el identificador del usuario, la fecha y hora UTC, la actividad afectada y los estados previo y nuevo

#### Scenario: Cambio de estado idempotente
- **WHEN** se solicita cambiar a `INACTIVE` una actividad que ya está `INACTIVE`
- **THEN** la operación se completa sin cambios y sin registrar un evento de auditoría
