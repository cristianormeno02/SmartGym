## Why

El módulo de Actividades en Smart-Gym administra el catálogo de disciplinas y ofertas de entrenamiento físico (`Activities`). Actualmente, la entidad `Activity` contiene un diseño inicial simplificado que mezcla responsabilidades estructurales (como una clave foránea rígida a `DefaultRoomId`), carece de un código identificador canónico (`code`), guarda URLs de imágenes desestructuradas en arrays de cadenas, expone su listado sin autenticación desde el mismo endpoint que usa la administración, y no implementa un ciclo de vida granular con auditoría ni gestión de medios mediante `ActivityMedia`.

Para consolidar la arquitectura de Monolito Modular y garantizar que los módulos subsecuentes (`Rooms`, `Schedules`, `ClassSessions`, `MembershipPlans`, `Reservations`) operen sobre límites conceptuales limpios, se requiere desacoplar estrictamente:
- **QUÉ** se ofrece (`Activities`): catálogo, identificación (`code`), descripción pública, restricciones etarias, notas de equipamiento y galería multimedia.
- **DÓNDE** se realiza (`Rooms`): espacios físicos/outdoor independientes.
- **CUÁNDO** se ofrece (`Schedules`): horarios semanales con instructores, salas y cupo programados.
- **EJECUCIÓN CONCRETA** (`ClassSessions`): clases de calendario con fecha, hora, cupos efectivos, sustituciones de profesores y estado operativo.
- **PLANES DE ACCESO** (`MembershipPlans`): qué membresías habilitan qué actividades (tabla intermedia gestionada por membresías).

## What Changes

* **Identificación canónica de actividades**: Incorporación de un campo `Code` (ej. `FUNCIONAL`, `POWER_UP`, `ZUMBA`) obligatorio, normalizado (mayúsculas, alfanumérico y guiones bajos), inmutable y con restricción `UNIQUE` en PostgreSQL. El `Name` permanece modificable libremente sin quebrar referencias históricas.
* **Desacoplamiento de Rooms y Capacidades**: Eliminación de `DefaultRoomId`/`DefaultRoom`, `MinCapacity` y `MaxCapacity` de `Activity`. Una actividad no pertenece a ninguna sala. La capacidad pasa a ser `DefaultCapacity` (opcional, sugerida). El cupo efectivo se resuelve como `RecurringSchedule.MaxCapacity ?? Activity.DefaultCapacity` y, si ninguno está definido, la operación se rechaza. El concepto de cupo mínimo se elimina de Activity (no tiene consumidores actuales) y queda fuera de alcance.
* **Restricciones etarias vigentes**: `MinAge`/`MaxAge` se mantienen como **restricciones** (no meramente referenciales): la reserva sigue rechazando alumnos con fecha de nacimiento conocida fuera del rango, preservando el comportamiento actual de `ReservationService`.
* **Modelo estructurado de medios (`ActivityMedia`)**: Reemplazo de los campos `LogoUrl` y `ImageUrls` por una entidad dedicada `ActivityMedia` vinculada a `ActivityId`, con `Type` (`Logo`, `GalleryImage`), `ObjectKey`, metadatos técnicos, `SortOrder` e `IsPrimary`. A lo sumo un logo y una imagen principal por actividad; máximo 10 imágenes de galería.
* **Procesamiento seguro de imágenes**: El backend decodifica y re-codifica toda imagen a WebP (generalizando el `ProfileImageProcessor` existente), lo que valida el contenido real del archivo, elimina metadatos EXIF y garantiza que la extensión `.webp` de la clave sea veraz. Límites de entrada: 2 MB para logos, 5 MB para imágenes de galería.
* **Bucket público dedicado**: Los medios de actividades se almacenan en un bucket R2 **público** separado del bucket privado existente (fotos de perfil, documentos), ya que Cloudflare R2 habilita el acceso público por bucket, no por prefijo. Las URLs se componen con un `PublicBaseUrl` configurable (CDN / dominio propio) y nunca se persisten en PostgreSQL.
* **Ciclo de vida explícito**: Estados `Active`, `Inactive`, `Archived` con máquina de transiciones definida. `IsActive` se deriva de `Status` (mismo patrón que `Person`). La inactivación se rechaza (409) mientras existan horarios recurrentes vigentes o clases futuras no terminales. El borrado físico se rechaza (409) ante cualquier dependencia, incluidas las asignaciones a planes de membresía.
* **Endpoints y DTOs diferenciados (Administrativo vs Público)**:
  - Endpoint público `GET /api/public/activities` y `GET /api/public/activities/{code}` que retorna sólo datos comerciales.
  - API administrativa (`/api/activities`) protegida con `RequireStaff`. **BREAKING**: el listado administrativo deja de ser anónimo; el frontend público migra al endpoint público.
* **Búsqueda y paginación**: Búsqueda por nombre y código insensible a acentos y mayúsculas sobre una columna `NormalizedName` calculada en la aplicación, con filtros por estado y edad. Sin extensiones `pg_trgm`/`unaccent` (volumen esperado: 10–25 actividades).
* **Concurrencia optimista**: Token `xmin` en `Activity` (mismo patrón que `Person`) para evitar sobrescrituras silenciosas en ediciones concurrentes.
* **Trazabilidad y Auditoría**: Registro en `AuditLog` de creación, modificación, cambio de estado, borrado y operaciones sobre medios, con valores previos y nuevos.
* **Migración de datos existente**: Generación automática de `Code` a partir de `Name` para actividades existentes, rename del enum (`Enabled`→`Active`, `Disabled`→`Inactive`) y respaldo de URLs legacy de medios en una tabla de respaldo para su recarga manual.

## Capabilities

### New Capabilities
- `activities-catalog`: Catálogo maestro de actividades físicas del gimnasio, identificación canónica (`code`), metadatos de presentación, restricciones etarias, gestión de medios en bucket público mediante `ActivityMedia`, ciclo de vida con preservación histórica, consulta pública y endpoints de administración.

### Modified Capabilities
- `activities-schedule`: Desacoplamiento de `Activity` respecto a salas y capacidades rígidas; nueva regla de resolución del cupo efectivo; horarios y clases solo pueden crearse para actividades en estado `ACTIVE`.
- `reservations-attendance`: La validación de reserva incorpora explícitamente el estado `ACTIVE` de la actividad y el rango etario.

## Impact

* **Backend (.NET 10 / ASP.NET Core Web API)**:
  - Dominio: Refactor de `Activity` (`Code`, `NormalizedName`, `ShortDescription` (renombra `Summary`), `DefaultCapacity`, `EquipmentNotes`, `ColorHex`, `Version`; eliminación de `DefaultRoomId`/`MinCapacity`/`MaxCapacity`/`LogoUrl`/`ImageUrls`); nueva entidad `ActivityMedia`; enum `ActivityMediaType`; rename de valores de `ActivityStatus`. `Room.Activities` se elimina.
  - Aplicación: Nuevos DTOs, validadores FluentValidation, `IActivityService` reescrito, `IImageProcessor` generalizado, `IPublicFileStorageService`. Ajustes en `RecurringScheduleService`, `ClassSessionService` y `ReservationService` (resolución de cupo y estado de actividad).
  - Persistencia: `ActivityConfiguration`, nueva `ActivityMediaConfiguration`, FK `MembershipPlanActivity → Activity` pasa de `Cascade` a `Restrict`; migración con script de datos.
  - Infraestructura: segundo cliente de storage para el bucket público (`PublicS3Storage` en configuración) y equivalente local servido por archivos estáticos.
  - WebApi: `ActivitiesController` reescrito y nuevo `PublicActivitiesController`.
* **Almacenamiento (Cloudflare R2)**: Nuevo bucket público con claves `activities/{activityId}/logo/{uuid}.webp` y `activities/{activityId}/gallery/{uuid}.webp`.
* **Compatibilidad con otros módulos**:
  - `MembershipPlans`: continúa referenciando `ActivityId`; ya no se puede borrar físicamente una actividad asignada a un plan.
  - `Schedules` y `ClassSessions`: continúan referenciando `ActivityId`; el cupo se resuelve según la regla anterior.
* **Frontend (Angular 22)**:
  - El catálogo público consume `/api/public/activities` (tarjetas, filtro por edad, galería).
  - Las vistas `home` y `schedules` dejan de mostrar sala por defecto y cupos de la actividad.
  - La UI administrativa de actividades no existe hoy y queda fuera de alcance (cambio posterior); mientras tanto, la administración se hace por API.
