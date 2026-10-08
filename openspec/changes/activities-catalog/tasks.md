> **Disciplina TDD**: cada tarea marcada con 🔴 empieza escribiendo los tests indicados y verificando que fallan por el motivo esperado; recién entonces se escribe el código mínimo para ponerlos en verde, y después se refactoriza. Los tests viven en `backend/tests/SmartGym.Domain.UnitTests`. Como EF InMemory no aplica índices únicos, CHECK ni `xmin`, esas restricciones se verifican con tests de configuración del modelo (mismo enfoque que `PersonModelConfigurationTests`) y con chequeos explícitos en el servicio.

## 1. Dominio

- [x] 1.1 🔴 `Activities/ActivityTests.cs`: código normalizado (trim + mayúsculas), rechazo de formato inválido (`ZU`, `POWER-UP`, vacío), `NormalizedName` vía `TextNormalizer.NormalizeForSearch`, color normalizado a mayúsculas y rechazo de color inválido, `DefaultCapacity` ≤ 0 rechazado, rangos etarios (negativos, min > max), `IsAgeAllowed`, limpieza de textos vacíos a `null`. → Refactorizar `Activity` según `design.md` (setters privados, `SetCode` privado, eliminación de `DefaultRoomId`/`DefaultRoom`/`MinCapacity`/`MaxCapacity`/`LogoUrl`/`ImageUrls`, `Summary` → `ShortDescription`, `Version`) y eliminar `Room.Activities`.
- [x] 1.2 🔴 Tests de ciclo de vida: transiciones permitidas y prohibidas (`Active→Archived`, `Archived→Active`), no-op idempotente que devuelve `false`, `IsActive` derivado del estado y su setter lanza excepción. → Renombrar los miembros de `ActivityStatus` (`Enabled→Active`, `Disabled→Inactive`) e implementar `CanTransitionTo`/`ChangeStatus`. Actualizar `PeopleEnumSerializationTests` o equivalentes si serializan este enum.
- [x] 1.3 🔴 `Activities/ActivityMediaTests.cs`: un logo no puede ser principal, tamaño no positivo rechazado, `OriginalFileName` sin componentes de ruta. → Crear `ActivityMediaType` y `ActivityMedia`.
- [x] 1.4 Corregir la compilación del resto de la solución (servicios, DTOs, controller y tests existentes) que referencian miembros eliminados, sin agregar comportamiento nuevo todavía; `dotnet build` en verde.

## 2. Persistencia

- [x] 2.1 🔴 Tests de configuración de modelo (`Activities/ActivityModelConfigurationTests.cs`): índice único en `Code`, CHECK constraints (`Code`, `Capacity`, `Age`, `ColorHex`, `Status_IsActive`), `Version` como row version, índices únicos parciales de logo y de imagen principal, índice único de `ObjectKey`, y FK `MembershipPlanActivity → Activity` con `Restrict`. → Actualizar `ActivityConfiguration`, crear `ActivityMediaConfiguration` y modificar `MembershipPlanActivityConfiguration`.
- [x] 2.2 Registrar `DbSet<ActivityMedia> ActivityMedias` en `SmartGymDbContext` e `ISmartGymDbContext`.
- [x] 2.3 Generar la migración `ActivitiesCatalogRefactor` y completarla a mano con el script de datos de la Decisión 11, en este orden: crear `ActivityLegacyMediaBackup` y copiar `LogoUrl`/`ImageUrls` → renombrar `Summary` → agregar `Code` nullable y poblarlo desde `Name` con desduplicación → agregar `NormalizedName` y poblarlo (minúsculas, sin acentos, consistente con `TextNormalizer`) → copiar `MaxCapacity` a `DefaultCapacity` → recalcular `IsActive = (Status = 1)` → hacer `Code` NOT NULL y crear constraints e índices → eliminar las columnas legacy y `DefaultRoomId`. Incluir un `Down` funcional.
- [ ] 2.4 Verificar la migración contra un PostgreSQL local con datos de muestra (nombres con acentos, duplicados, nombres de menos de 3 caracteres, imágenes legacy); `dotnet ef migrations has-pending-model-changes` debe reportar que no hay cambios pendientes. Documentar el resultado en el PR. _(Pendiente: la estructura de la migración se cubre con `ActivitiesCatalogMigrationTests`, pero su SQL aún no se ejecutó contra PostgreSQL.)_

## 3. Infraestructura de medios

- [x] 3.1 🔴 Extender `ProfileImageProcessorTests`/crear `ImageProcessorTests`: perfiles `ActivityLogo` (2 MB, 512 px) y `ActivityGallery` (5 MB, 1920 px), rechazo de contenido no decodificable con MIME válido, salida WebP y dimensiones informadas; el perfil `Profile` conserva el comportamiento actual. → Generalizar en `IImageProcessor` + `ImageProcessingProfile`, manteniendo `IProfileImageProcessor` como fachada o migrando su consumidor en `PeopleService`.
- [x] 3.2 🔴 Tests de `FileStorageTests` para el storage público local: `GetPublicUrl` compone `PublicBaseUrl + ObjectKey` y la carga escribe bajo `storage-public/`. → Crear `IPublicFileStorageService`, las opciones `PublicS3Storage` (incluido `PublicBaseUrl`), la implementación S3 (con `Cache-Control` inmutable) y la local, registrarlas en `DependencyInjection` y servir `/public-media/` como archivos estáticos en `Program.cs`. Agregar la sección `PublicS3Storage` a `appsettings.json` (sin credenciales).

## 4. Aplicación

- [x] 4.1 Definir los DTOs de `design.md` en `Modules/Activities/Dtos` (reemplazando los actuales) y agregar la propiedad opcional `Details` a `ConflictException`.
- [x] 4.2 🔴 `ActivityValidatorTests`: formato de código, longitudes de texto (100/250/2000/500), color, rangos etarios, capacidad. → Validadores FluentValidation para `CreateActivityRequest` y `UpdateActivityRequest`.
- [x] 4.3 🔴 `ActivityServiceTests` (CRUD y consultas): creación, duplicado de código insensible a mayúsculas → `ConflictException`, actualización sin cambio de código, conflicto de versión → `ConflictException`, listado paginado (sin filtro de estado excluye archivadas, búsqueda sin acentos, filtro por edad), detalle por id y por código. → Reescribir `IActivityService`/`ActivityService`.
- [x] 4.4 🔴 Tests de ciclo de vida en el servicio: guarda de inactivación (horario vigente, horario con `ValidTo` nulo, clase futura `Scheduled`/`InProgress` → 409 con conteos; horarios vencidos y clases pasadas → permitido), transición inválida, no-op sin auditoría, borrado con dependencias (horarios, clases, planes) → 409 con `ActivityDependenciesDto`, borrado sin dependencias que elimina medios del storage. → Implementar `ChangeStatusAsync` y `DeleteAsync`.
- [x] 4.5 🔴 Tests de medios con storage y procesador simulados: validaciones previas sin llamadas al storage, límite de 10 imágenes, primera imagen principal automática, reemplazo de logo con borrado post-commit del anterior, cambio de principal, promoción al eliminar la principal, reordenamiento con conjunto inválido → 400, compensación cuando falla `SaveChanges`, eliminación (primero BD, después storage). → Implementar las operaciones de medios en `ActivityService`.
- [x] 4.6 🔴 Tests de consulta pública: solo `Active`, orden por nombre, búsqueda por código sin distinguir mayúsculas, 404 para inactivas, sin `Id` ni metadatos internos, URLs compuestas con `PublicBaseUrl`. → Implementar `GetPublicCatalogAsync`/`GetPublicByCodeAsync`.
- [x] 4.7 🔴 Tests de auditoría: cada operación registra su evento (`ACTIVITY_CREATED`, `ACTIVITY_UPDATED` solo con campos modificados, `ACTIVITY_STATUS_CHANGED`, `ACTIVITY_DELETED`, `ACTIVITY_MEDIA_ADDED`, `ACTIVITY_MEDIA_DELETED`, `ACTIVITY_MEDIA_PRIMARY_CHANGED`, `ACTIVITY_MEDIA_REORDERED`) con valores previos y nuevos. → Integrar con `IAuditService`.

## 5. Integración con Schedules, ClassSessions y Reservations

- [x] 5.1 🔴 `RecurringScheduleTests`/servicio: cupo explícito > `DefaultCapacity`; sin ninguno → error de validación; actividad no `Active` → rechazo. → Ajustar `RecurringScheduleService` (líneas que hoy usan `Activity.MaxCapacity`).
- [x] 5.2 🔴 `ClassSessionTests`/servicio: clase generada usa el cupo del horario y, en su defecto, `DefaultCapacity`; clase manual sin cupo resoluble → error; se elimina el fallback fijo `?? 20`; no se generan ni crean clases para actividades no `Active`. → Ajustar `ClassSessionService`.
- [x] 5.3 🔴 `Reservations`: reserva en actividad no `Active` → rechazo; fuera de rango etario → rechazo; alumno sin fecha de nacimiento → sin restricción. → Agregar el chequeo de estado en `ReservationService` (el chequeo de edad ya existe; solo se cubre con tests).

## 6. WebApi

- [x] 6.1 🔴 `ActivitiesControllerTests`: `[Authorize(Policy = RequireStaff)]` a nivel de clase, mapeo de excepciones (400/404/409 con `Details`), `201 Created` con `Location`. → Reescribir `ActivitiesController` con las rutas de `design.md`, incluidos `by-code` y `PATCH media/sort`, y eliminar la dependencia directa del controller con `IFileStorageService`.
- [x] 6.2 🔴 `PublicActivitiesControllerTests`: `[AllowAnonymous]`, ruta `api/public/activities`, filtro `age`, cabecera `Cache-Control`. → Crear `PublicActivitiesController`.
- [x] 6.3 Prueba end-to-end en memoria (mismo enfoque que `PeopleEndToEndTests`): crear actividad → subir logo y galería → consultar catálogo público → inactivar → verificar que desaparece del público.

## 7. Frontend (Angular 22)

- [x] 7.1 Actualizar `core/models/gym.models.ts`: nuevo `PublicActivity` (código, textos, edades, color, URLs) y quitar `minCapacity`/`maxCapacity`/`imageUrls`/`defaultRoom*` del modelo de actividad.
- [x] 7.2 `gym-api.service.ts`: `getPublicActivities(age?)` y `getPublicActivity(code)` contra `/api/public/activities`; eliminar el uso anónimo de `/api/activities`.
- [x] 7.3 Adaptar `features/public/activities` y `features/public/home`: usar imagen principal/logo y color, reemplazar las referencias a sala y cupo por edades y notas de equipamiento, y agregar el filtro por edad. Revisar `features/public/schedules` (hoy muestra `maxCapacity || 30`).
- [x] 7.4 `ng build` sin errores y verificación visual del catálogo público con datos de prueba.
- [x] 7.5 La UI administrativa de actividades (formulario, carga de medios, cambio de estado) **queda fuera de alcance**: hoy no existe en el frontend y se abordará en un cambio posterior. Hasta entonces, la administración se hace por API/Swagger.

## 8. Verificación final

- [x] 8.1 `dotnet test` completo en verde, sin tests omitidos.
- [x] 8.2 Revisar en Swagger/OpenAPI los esquemas de DTOs y que los endpoints públicos aparezcan sin requisito de seguridad y los administrativos con él.
- [x] 8.3 Recorrer cada escenario de las specs delta (`activities-catalog`, `activities-schedule`, `reservations-attendance`) y verificar que existe al menos un test que lo cubra.
