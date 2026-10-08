# Design Document — Activities Module (`activities-catalog`)

## Context

Smart-Gym es un sistema web de gestión de gimnasios desarrollado bajo una arquitectura de **Monolito Modular** en .NET 10 (ASP.NET Core Web API), Entity Framework Core con PostgreSQL, autenticación JWT/OIDC y frontend en Angular 22.

Estado actual relevante (verificado en el código):
- `Activity` tiene `DefaultRoomId` (FK `SetNull`), `MinCapacity`/`MaxCapacity` obligatorios, `LogoUrl` y `ImageUrls` (JSON serializado), `Summary`, y `ActivityStatus { Enabled, Disabled, Archived }` con `IsActive` independiente.
- `RecurringScheduleService` y `ClassSessionService` usan `Activity.MaxCapacity` como cupo por defecto.
- `ReservationService` aplica `Activity.IsAgeAllowed(age)` cuando el alumno tiene fecha de nacimiento.
- `MembershipPlanActivity → Activity` está configurada con `OnDelete(Cascade)`.
- `GET /api/activities` es anónimo y lo consume la vista pública del frontend (`gym-api.service.ts`).
- Existe un único `IFileStorageService` (S3/R2 o local) apuntando a un bucket privado (`smartgym-assets`), y un `ProfileImageProcessor` que re-codifica a WebP con SkiaSharp.
- `Person` ya implementa `IsActive` derivado de `Status` y concurrencia optimista con `xmin` (`Version`).

Para la motivación, véase [proposal.md](proposal.md).

## Goals / Non-Goals

### Goals
- Desacoplar estructuralmente `Activity` de `Room`, `Schedule`, `ClassSession` e `Instructor`.
- Incorporar una clave de negocio única, canónica e inmutable: `Code`.
- Implementar `ActivityMedia` con almacenamiento en un bucket público dedicado.
- Exponer una API administrativa (`RequireStaff`) y una API pública (`/api/public/activities`).
- Definir una máquina de estados explícita y proteger la integridad histórica ante inactivación y borrado.
- Migrar los datos existentes sin pérdida.

### Non-Goals
- No implementar gestión de salas, grilla horaria ni calendario de clases (solo se ajusta la resolución de cupo y el chequeo de estado en los servicios existentes).
- No modificar reglas de acceso de membresías, créditos ni la lógica de reservas más allá del chequeo de estado/edad.
- No soportar video ni otros formatos que no sean imagen estática.
- No incorporar categorías ni etiquetas.
- No reintroducir un "cupo mínimo para dictar la clase"; si se requiere en el futuro, pertenecerá a `RecurringSchedule`/`ClassSession`.
- No migrar automáticamente los medios legacy a `ActivityMedia` (ver Decisión 11).

---

## Architectural & Data Model Design

### Conceptual Model & Boundaries

```text
┌────────────────────────────────────────────────────────┐
│                        Activities                      │
│  ┌──────────────────────┐ 1..N ┌────────────────────┐  │
│  │       Activity       │──────│   ActivityMedia    │──┼──► Bucket R2 público
│  └──────────────────────┘      └────────────────────┘  │
└────────────┬───────────────────────────────────────────┘
             │ (Referenced by Guid ActivityId, FK Restrict)
             ▼
┌─────────────────────────┐           ┌────────────────────────┐
│        Schedules        │           │    MembershipPlans     │
│  (RecurringSchedule)    │           │ (MembershipPlanActivity│
└────────────┬────────────┘           └────────────────────────┘
             ▼
┌─────────────────────────┐
│      ClassSessions      │──► Reservations (valida Status y edad)
└─────────────────────────┘
```

---

## Relational Schema (PostgreSQL)

```sql
CREATE TABLE "Activities" (
    "Id" UUID PRIMARY KEY,
    "Code" VARCHAR(50) NOT NULL,
    "Name" VARCHAR(100) NOT NULL,
    "NormalizedName" VARCHAR(100) NOT NULL,   -- TextNormalizer.NormalizeForSearch(Name): minúsculas, sin diacríticos
    "ShortDescription" VARCHAR(250) NULL,     -- renombra "Summary"
    "Description" VARCHAR(2000) NULL,
    "EquipmentNotes" VARCHAR(500) NULL,
    "ColorHex" VARCHAR(7) NULL,               -- normalizado a mayúsculas
    "DefaultCapacity" INT NULL,
    "MinAge" INT NULL,
    "MaxAge" INT NULL,
    "Status" INT NOT NULL DEFAULT 1,          -- 1=Active, 2=Inactive, 3=Archived
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE, -- derivado de Status; se conserva porque otros módulos filtran por él
    "CreatedAtUtc" TIMESTAMPTZ NOT NULL,
    "UpdatedAtUtc" TIMESTAMPTZ NULL,
    -- xmin se usa como token de concurrencia (columna de sistema, no se declara)

    CONSTRAINT "UQ_Activities_Code" UNIQUE ("Code"),
    CONSTRAINT "CK_Activities_Code_Format" CHECK ("Code" ~ '^[A-Z0-9_]{3,50}$'),
    CONSTRAINT "CK_Activities_Capacity" CHECK ("DefaultCapacity" IS NULL OR "DefaultCapacity" > 0),
    CONSTRAINT "CK_Activities_Age" CHECK (
        ("MinAge" IS NULL OR "MinAge" >= 0) AND
        ("MaxAge" IS NULL OR "MaxAge" >= 0) AND
        ("MinAge" IS NULL OR "MaxAge" IS NULL OR "MinAge" <= "MaxAge")
    ),
    CONSTRAINT "CK_Activities_ColorHex" CHECK ("ColorHex" IS NULL OR "ColorHex" ~ '^#[0-9A-F]{6}$'),
    CONSTRAINT "CK_Activities_Status_IsActive" CHECK ("IsActive" = ("Status" = 1))
);

CREATE INDEX "IX_Activities_Status" ON "Activities" ("Status");

CREATE TABLE "ActivityMedias" (
    "Id" UUID PRIMARY KEY,
    "ActivityId" UUID NOT NULL,
    "Type" INT NOT NULL,                      -- 1=Logo, 2=GalleryImage
    "ObjectKey" VARCHAR(500) NOT NULL,
    "OriginalFileName" VARCHAR(255) NOT NULL,
    "ContentType" VARCHAR(100) NOT NULL,      -- siempre image/webp tras el procesamiento
    "SizeBytes" BIGINT NOT NULL,              -- tamaño del archivo procesado
    "Width" INT NOT NULL,
    "Height" INT NOT NULL,
    "SortOrder" INT NOT NULL DEFAULT 0,
    "IsPrimary" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAtUtc" TIMESTAMPTZ NOT NULL,
    "UpdatedAtUtc" TIMESTAMPTZ NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,

    CONSTRAINT "FK_ActivityMedias_Activities" FOREIGN KEY ("ActivityId")
        REFERENCES "Activities" ("Id") ON DELETE CASCADE,
    CONSTRAINT "CK_ActivityMedias_SizeBytes" CHECK ("SizeBytes" > 0),
    CONSTRAINT "CK_ActivityMedias_LogoNotPrimary" CHECK ("Type" = 2 OR "IsPrimary" = FALSE),
    CONSTRAINT "UQ_ActivityMedias_ObjectKey" UNIQUE ("ObjectKey")
);

CREATE INDEX "IX_ActivityMedias_ActivityId_SortOrder" ON "ActivityMedias" ("ActivityId", "SortOrder");
-- A lo sumo una imagen principal de galería por actividad
CREATE UNIQUE INDEX "UQ_ActivityMedias_Primary" ON "ActivityMedias" ("ActivityId")
    WHERE "IsPrimary" = TRUE AND "Type" = 2;
-- A lo sumo un logo por actividad
CREATE UNIQUE INDEX "UQ_ActivityMedias_Logo" ON "ActivityMedias" ("ActivityId")
    WHERE "Type" = 1;

-- Integridad referencial entrante: ningún borrado de Activity puede cascadear a datos de negocio
-- MembershipPlanActivities.ActivityId: Cascade → Restrict
-- RecurringSchedules.ActivityId / ClassSessions.ActivityId: Restrict (sin cambios)

-- Respaldo de medios legacy (ver Decisión 11)
CREATE TABLE "ActivityLegacyMediaBackup" (
    "ActivityId" UUID NOT NULL,
    "Kind" VARCHAR(10) NOT NULL,              -- 'logo' | 'image'
    "Url" VARCHAR(500) NOT NULL,
    "Position" INT NOT NULL
);
```

> `ON DELETE CASCADE` en `ActivityMedias` solo aplica en el borrado físico permitido (actividad sin dependencias). El servicio borra además los objetos del bucket (ver Decisión 5).

---

## Decisions & Alternatives

### Decision 1: Identificación con `Code` canónico vs solo `Name`
- **A**: Usar solo `Name` como clave legible y única.
- **B**: Clave de negocio `Code` inmutable + `Name` descriptivo libre.
- **Adoptada: B**. Permite renombrar una actividad ("Zumba" → "Zumba Gold") sin alterar códigos ni contratos. El código se normaliza con `Trim().ToUpperInvariant()` antes de validar contra `^[A-Z0-9_]{3,50}$`; por eso el chequeo de duplicados es insensible a mayúsculas y espacios. `SetCode` es privado: el código solo se asigna en el constructor y `UpdateActivityRequest` no lo incluye.

### Decision 2: Separación de medios (`ActivityMedia`) vs campos directos
- **A**: Columnas `LogoUrl` y `ImageUrls` (JSON array).
- **B**: Tabla `ActivityMedia` con metadatos, flags y constraints.
- **Adoptada: B**. Reglas:
  - **Logo**: a lo sumo uno (índice único parcial). Subir un logo nuevo **reemplaza** al anterior: se inserta el nuevo y se elimina el previo en la misma transacción; el objeto previo se borra del bucket después del commit.
  - **Galería**: máximo **10** imágenes por actividad. `SortOrder` del nuevo medio = `max(SortOrder) + 1`.
  - **Imagen principal**: a lo sumo una (índice único parcial). La primera imagen de galería subida se marca principal automáticamente. Al marcar otra, la anterior se desmarca en la misma transacción. Si se elimina la principal, se promueve la de menor `SortOrder`.
  - **Reordenamiento**: `PATCH /media/sort` recibe la lista completa de IDs de galería en el orden deseado; si no coincide exactamente con el conjunto actual, se rechaza con 400.

### Decision 3: Categorías en el módulo
- **A**: Tablas `ActivityCategory` N:M.
- **B**: Sin categorías en el MVP.
- **Adoptada: B**. Para 10–25 actividades, el filtro por edad y la búsqueda textual son suficientes.

### Decision 4: Storage público dedicado y manejo de URLs
- **A**: Firmar todas las URLs con TTL en cada consulta, usando el bucket privado actual.
- **B**: URLs públicas por CDN sobre el **mismo** bucket.
- **C**: Bucket R2 **público dedicado** para activos comerciales, separado del bucket privado.
- **Adoptada: C**. Cloudflare R2 habilita el acceso público a nivel de bucket (dominio `r2.dev` o dominio propio), no por prefijo, por lo que B expondría fotos de perfil y documentos privados. Las URLs firmadas (A) invalidan el caché de navegadores/CDN.
  - Nueva abstracción `IPublicFileStorageService : IFileStorageService` con `string GetPublicUrl(string objectKey)`.
  - Configuración `PublicS3Storage` (`BucketName`, `ServiceUrl`, credenciales, `PublicBaseUrl`, `UseLocalStorage`). La implementación local guarda en `storage-public/` y la sirve por archivos estáticos en `/public-media/`.
  - Se persiste solo `ObjectKey`; la URL se compone en tiempo de lectura (`PublicBaseUrl + "/" + ObjectKey`).
  - Como las claves contienen un UUID y nunca se sobrescriben, los objetos se suben con `Cache-Control: public, max-age=31536000, immutable`.

### Decision 5: Estrategia ante Borrado (`DELETE`)
- **A**: Borrado físico con `CASCADE`.
- **B**: Borrado físico solo sin dependencias; en otro caso, inactivación o archivado.
- **Adoptada: B**. Dependencias que bloquean el borrado: `RecurringSchedules`, `ClassSessions` y `MembershipPlanActivities` (cuya FK pasa de `Cascade` a `Restrict` para que la base de datos lo garantice). Si existe alguna, se responde **409 Conflict** con el conteo por tipo de dependencia y la sugerencia de inactivar/archivar. Si no hay dependencias, se borra la actividad (las filas de `ActivityMedia` caen en cascada) y, tras el commit, se eliminan sus objetos del bucket en modo best-effort (los fallos se registran en log; un objeto huérfano en un bucket público de imágenes es tolerable).

### Decision 6: `IsActive` derivado de `Status`
- **A**: Mantener ambos campos y sincronizarlos manualmente.
- **B**: `IsActive` se deriva de `Status`; asignarlo directamente lanza `InvalidOperationException`.
- **Adoptada: B**, idéntico al patrón de `Person`. La columna se conserva porque otros servicios ya filtran por `a.IsActive` (por ejemplo, `RecurringScheduleService` al crear horarios), y queda protegida por `CK_Activities_Status_IsActive`.

### Decision 7: Máquina de estados y guarda de inactivación
Transiciones permitidas:

```text
Active ──► Inactive ──► Archived
  ▲           │  ▲          │
  └───────────┘  └──────────┘
```

- `Active → Archived` directo **no** está permitido (debe pasar por `Inactive`).
- `Archived → Active` directo **no** está permitido (debe restaurarse a `Inactive` primero).
- Transiciones al mismo estado: no-op idempotente sin evento de auditoría.
- **Guarda de inactivación** (`Active → Inactive`): se rechaza con **409** si existen (a) horarios recurrentes con `ValidTo` nulo o ≥ hoy, o (b) clases concretas con fecha ≥ hoy en estado no terminal (`ClassSessionStatus.Scheduled` o `InProgress`). La respuesta incluye los conteos. El administrativo debe primero cerrar la vigencia de los horarios y suspender/cancelar las clases futuras mediante los flujos existentes (que ya gestionan la devolución de créditos). Así la inactivación nunca produce efectos ocultos sobre reservas o créditos.
- Semántica: `Inactive` = fuera de oferta pero visible por defecto en el listado administrativo para reactivación; `Archived` = histórico, oculto del listado administrativo por defecto (se ve con filtro explícito `status=Archived`) y de los selectores de los demás módulos.
- Solo las actividades `Active` pueden recibir nuevos horarios, nuevas clases manuales y nuevas reservas, y solo ellas aparecen en el catálogo público.

### Decision 8: Capacidad
- `DefaultCapacity` (opcional, > 0) es un valor sugerido.
- Resolución del cupo efectivo al crear o editar horarios y clases: `request.MaxCapacity ?? schedule.MaxCapacity ?? activity.DefaultCapacity`; si el resultado es nulo, se rechaza la operación con error de validación (se eliminan los fallbacks fijos actuales, como `?? 20`).
- Cambiar `DefaultCapacity` **no** modifica horarios ni clases existentes.
- `MinCapacity` se elimina sin reemplazo: no tiene consumidores fuera de la propia entidad.

### Decision 9: Restricciones etarias
- **A**: Edad meramente informativa.
- **B**: Edad restrictiva al reservar.
- **Adoptada: B**, preservando el comportamiento actual. `Activity.IsAgeAllowed(int age)` se conserva y `ReservationService` la sigue aplicando cuando el alumno tiene `BirthDate`. Si no tiene fecha de nacimiento, no se aplica la restricción (comportamiento actual, documentado explícitamente). Cambiar el rango no afecta reservas ya existentes.

### Decision 10: Búsqueda
- **A**: `pg_trgm` + `unaccent` con índice GIN.
- **B**: Columna `NormalizedName` calculada con el `TextNormalizer.NormalizeForSearch` existente (el mismo que usa `Person.SearchName`: minúsculas, sin diacríticos, espacios colapsados) y búsqueda `Contains` sobre `NormalizedName` con el término normalizado igual, más `Code` comparado contra el término en mayúsculas.
- **Adoptada: B**. Con 10–25 filas un índice trigram no aporta nada y agrega dos extensiones de PostgreSQL a la infraestructura. Se reutiliza el normalizador del módulo People por consistencia. La búsqueda **no** abarca `ShortDescription` en el MVP.

### Decision 11: Migración de datos existentes
- **Code**: se genera en SQL a partir de `Name`: mayúsculas → transliteración de `ÁÉÍÓÚÜÑ` → reemplazo de todo carácter fuera de `[A-Z0-9]` por `_` → colapso y recorte de `_` → relleno a mínimo 3 caracteres con `_` → truncado a 46. Los duplicados reciben sufijo `_2`, `_3`, … (por `row_number()` ordenado por `CreatedAtUtc`). La migración termina con una verificación de unicidad antes de crear el constraint.
- **Summary → ShortDescription**: rename de columna.
- **Status**: los valores int no cambian (1/2/3); solo se renombran los miembros del enum. `IsActive` se recalcula como `Status = 1` antes de crear el CHECK.
- **MaxCapacity → DefaultCapacity**: se copia el valor para no perder la sugerencia. `MinCapacity` se descarta.
- **Medios legacy**: `LogoUrl` e `ImageUrls` apuntan a objetos del bucket **privado** y no tienen metadatos (tamaño, dimensiones), por lo que no pueden convertirse en `ActivityMedia` válidos ni servirse públicamente. Se copian a `ActivityLegacyMediaBackup` (una fila por URL) y luego se eliminan las columnas. Recargarlos mediante la API nueva es un paso operativo manual. La tabla de respaldo se elimina en un cambio posterior.
- **DefaultRoomId**: se elimina la columna (el dato no tiene consumidores).

### Decision 12: Rutas públicas y administrativas
- Público: `GET /api/public/activities` y `GET /api/public/activities/{code}`. La búsqueda por código no distingue mayúsculas (el valor se normaliza antes de consultar). No se expone búsqueda por ID en la API pública: las URLs públicas usan el código, y así se evita la ambigüedad GUID/código.
- Administrativo: `GET /api/activities/{id:guid}` y `GET /api/activities/by-code/{code}`.

### Decision 13: Procesamiento de imágenes
- Se generaliza `ProfileImageProcessor` en `IImageProcessor.ProcessAsync(stream, declaredContentType, declaredLength, ImageProcessingProfile profile)`, con perfiles predefinidos:
  - `Profile`: 5 MB, 1024 px (comportamiento actual, sin cambios).
  - `ActivityLogo`: 2 MB, 512 px.
  - `ActivityGallery`: 5 MB, 1920 px.
- Primero se valida el tamaño declarado y la lista blanca de MIME (sin tocar storage). Luego se decodifica la imagen (si falla → 400), se reescala respetando la relación de aspecto y se re-codifica a WebP con calidad 85, lo que también elimina el EXIF. `SizeBytes`, `Width` y `Height` reflejan el resultado procesado.

### Decision 14: Concurrencia optimista
`Activity` incorpora `Version` mapeado a `xmin` (`IsRowVersion()`), como `Person`. `PUT` y `PATCH /status` responden **409** ante `DbUpdateConcurrencyException`. Los DTOs de detalle exponen `Version` y las requests de mutación la reciben.

---

## Domain Entities & Enums

```csharp
namespace SmartGym.Domain.Enums;

public enum ActivityStatus
{
    Active = 1,   // antes: Enabled
    Inactive = 2, // antes: Disabled
    Archived = 3
}

public enum ActivityMediaType
{
    Logo = 1,
    GalleryImage = 2
}
```

```csharp
namespace SmartGym.Domain.Entities.Activities;

public class Activity : BaseEntity
{
    public const int MaxGalleryImages = 10;
    private static readonly Regex CodePattern = new("^[A-Z0-9_]{3,50}$", RegexOptions.Compiled);
    private static readonly Regex ColorPattern = new("^#[0-9A-F]{6}$", RegexOptions.Compiled);

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? ShortDescription { get; private set; }
    public string? Description { get; private set; }
    public string? EquipmentNotes { get; private set; }
    public string? ColorHex { get; private set; }
    public int? DefaultCapacity { get; private set; }
    public int? MinAge { get; private set; }
    public int? MaxAge { get; private set; }
    public ActivityStatus Status { get; private set; } = ActivityStatus.Active;
    public uint Version { get; private set; } // xmin

    // Derivado del estado (mismo patrón que Person). La columna se conserva porque otros módulos filtran por IsActive.
    public override bool IsActive
    {
        get => Status == ActivityStatus.Active;
        set => throw new InvalidOperationException("IsActive de una actividad se deriva de su estado; use ChangeStatus.");
    }

    public ICollection<ActivityMedia> Media { get; private set; } = new List<ActivityMedia>();

    private Activity() { }

    public Activity(string code, string name, string? shortDescription = null, string? description = null,
        string? equipmentNotes = null, string? colorHex = null, int? defaultCapacity = null, int? minAge = null, int? maxAge = null)
    {
        SetCode(code);
        UpdateDetails(name, shortDescription, description, equipmentNotes, colorHex, defaultCapacity, minAge, maxAge);
        UpdatedAtUtc = null;
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("El código de la actividad no puede estar vacío.", nameof(code));

        var normalized = NormalizeCode(code);
        if (!CodePattern.IsMatch(normalized))
            throw new ArgumentException("El código debe tener entre 3 y 50 caracteres: letras, números o guion bajo.", nameof(code));

        Code = normalized;
    }

    public void UpdateDetails(string name, string? shortDescription, string? description, string? equipmentNotes,
        string? colorHex, int? defaultCapacity, int? minAge, int? maxAge)
    {
        SetName(name);
        ShortDescription = Clean(shortDescription);
        Description = Clean(description);
        EquipmentNotes = Clean(equipmentNotes);
        SetColorHex(colorHex);
        SetDefaultCapacity(defaultCapacity);
        SetAgeRange(minAge, maxAge);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre de la actividad no puede estar vacío.", nameof(name));

        Name = name.Trim();
        NormalizedName = TextNormalizer.NormalizeForSearch(Name);
    }

    private void SetDefaultCapacity(int? value)
    {
        if (value is <= 0)
            throw new ArgumentException("La capacidad por defecto debe ser un entero positivo.", nameof(value));
        DefaultCapacity = value;
    }

    private void SetAgeRange(int? minAge, int? maxAge)
    {
        if (minAge is < 0) throw new ArgumentException("La edad mínima no puede ser negativa.", nameof(minAge));
        if (maxAge is < 0) throw new ArgumentException("La edad máxima no puede ser negativa.", nameof(maxAge));
        if (minAge.HasValue && maxAge.HasValue && minAge > maxAge)
            throw new ArgumentException("La edad mínima no puede ser superior a la edad máxima.", nameof(minAge));
        MinAge = minAge;
        MaxAge = maxAge;
    }

    private void SetColorHex(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex)) { ColorHex = null; return; }
        var normalized = colorHex.Trim().ToUpperInvariant();
        if (!ColorPattern.IsMatch(normalized))
            throw new ArgumentException("El color debe tener formato hexadecimal (#RRGGBB).", nameof(colorHex));
        ColorHex = normalized;
    }

    public bool IsAgeAllowed(int age) =>
        (!MinAge.HasValue || age >= MinAge.Value) && (!MaxAge.HasValue || age <= MaxAge.Value);

    public bool CanTransitionTo(ActivityStatus target) => (Status, target) switch
    {
        (ActivityStatus.Active, ActivityStatus.Inactive) => true,
        (ActivityStatus.Inactive, ActivityStatus.Active) => true,
        (ActivityStatus.Inactive, ActivityStatus.Archived) => true,
        (ActivityStatus.Archived, ActivityStatus.Inactive) => true,
        _ => Status == target
    };

    /// <summary>Devuelve false si el estado ya era el solicitado (no-op).</summary>
    public bool ChangeStatus(ActivityStatus target)
    {
        if (Status == target) return false;
        if (!CanTransitionTo(target))
            throw new InvalidOperationException($"Transición de estado no permitida: {Status} → {target}.");
        Status = target;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

> La guarda de inactivación (horarios vigentes y clases futuras) se evalúa en `ActivityService` porque requiere consultar otros agregados. La entidad solo valida la transición.

```csharp
namespace SmartGym.Domain.Entities.Activities;

public class ActivityMedia : BaseEntity
{
    public Guid ActivityId { get; private set; }
    public Activity Activity { get; private set; } = null!;
    public ActivityMediaType Type { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    private ActivityMedia() { }

    public ActivityMedia(Guid activityId, ActivityMediaType type, string objectKey, string originalFileName,
        string contentType, long sizeBytes, int width, int height, int sortOrder)
    {
        if (sizeBytes <= 0) throw new ArgumentException("El tamaño debe ser positivo.", nameof(sizeBytes));
        ActivityId = activityId;
        Type = type;
        ObjectKey = objectKey;
        OriginalFileName = Path.GetFileName(originalFileName);
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        SortOrder = sortOrder;
    }

    public void SetPrimary(bool isPrimary)
    {
        if (isPrimary && Type != ActivityMediaType.GalleryImage)
            throw new InvalidOperationException("Solo una imagen de galería puede ser principal.");
        IsPrimary = isPrimary;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
```

---

## REST API Specification

### Endpoints Administrativos (`[Authorize(Policy = Policies.RequireStaff)]` a nivel de controlador)

| Método | Ruta | Respuestas |
|---|---|---|
| GET | `/api/activities?search&status&age&page&pageSize` | 200 `PagedResult<ActivityListItemDto>`. Sin `status` devuelve Active + Inactive. `age` filtra las actividades que admiten esa edad. |
| GET | `/api/activities/{id:guid}` | 200 `ActivityDetailDto` / 404 |
| GET | `/api/activities/by-code/{code}` | 200 / 404 |
| POST | `/api/activities` | 201 `ActivityDetailDto` / 400 / 409 (código duplicado) |
| PUT | `/api/activities/{id:guid}` | 200 / 400 / 404 / 409 (concurrencia) |
| PATCH | `/api/activities/{id:guid}/status` | 200 / 400 (transición inválida) / 404 / 409 (dependencias futuras o concurrencia) |
| DELETE | `/api/activities/{id:guid}` | 204 / 404 / 409 (dependencias, con conteos) |
| GET | `/api/activities/{id:guid}/media` | 200 `List<ActivityMediaDto>` |
| POST | `/api/activities/{id:guid}/media` | `multipart/form-data` (`file`, `type`). 201 / 400 (archivo inválido o límite de galería) / 404 |
| DELETE | `/api/activities/{id:guid}/media/{mediaId:guid}` | 204 / 404 |
| PATCH | `/api/activities/{id:guid}/media/{mediaId:guid}/primary` | 200 / 400 (no es galería) / 404 |
| PATCH | `/api/activities/{id:guid}/media/sort` | body `{ mediaIds: Guid[] }`. 200 / 400 (conjunto no coincide) |

Las excepciones de dominio se traducen así: `ArgumentException` → 400, `InvalidOperationException` de transición → 400, conflicto de unicidad/dependencias/concurrencia → 409 mediante la `ConflictException` existente, a la que se agrega una propiedad opcional `Details` (object) para transportar `ActivityDependenciesDto`.

### Endpoints Públicos (`[AllowAnonymous]`, `[Route("api/public/activities")]`)

- `GET /api/public/activities?age=` — actividades `Active` ordenadas por `Name`, con `PublicActivityDto`. Respuesta cacheable (`Cache-Control: public, max-age=300`).
- `GET /api/public/activities/{code}` — ficha pública; 404 si no existe o no está `Active`.

---

## DTO Models

```csharp
namespace SmartGym.Application.Modules.Activities.Dtos;

public record ActivityListItemDto(
    Guid Id, string Code, string Name, string? ShortDescription, string? ColorHex,
    int? DefaultCapacity, int? MinAge, int? MaxAge, ActivityStatus Status,
    string? LogoUrl, string? PrimaryImageUrl, DateTime CreatedAtUtc);

public record ActivityDetailDto(
    Guid Id, string Code, string Name, string? ShortDescription, string? Description,
    string? EquipmentNotes, string? ColorHex, int? DefaultCapacity, int? MinAge, int? MaxAge,
    ActivityStatus Status, uint Version, List<ActivityMediaDto> Media,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

public record PublicActivityDto(
    string Code, string Name, string? ShortDescription, string? Description, string? EquipmentNotes,
    string? ColorHex, int? MinAge, int? MaxAge,
    string? LogoUrl, string? PrimaryImageUrl, List<string> GalleryImageUrls);

public record ActivityMediaDto(
    Guid Id, ActivityMediaType Type, string Url, string OriginalFileName, string ContentType,
    long SizeBytes, int Width, int Height, int SortOrder, bool IsPrimary, DateTime CreatedAtUtc);

public record CreateActivityRequest(
    string Code, string Name, string? ShortDescription, string? Description, string? EquipmentNotes,
    string? ColorHex, int? DefaultCapacity, int? MinAge, int? MaxAge);

public record UpdateActivityRequest(
    string Name, string? ShortDescription, string? Description, string? EquipmentNotes,
    string? ColorHex, int? DefaultCapacity, int? MinAge, int? MaxAge, uint Version);

public record ChangeActivityStatusRequest(ActivityStatus Status, uint Version);

public record SortActivityMediaRequest(List<Guid> MediaIds);

public record ActivityDependenciesDto(int ActiveSchedules, int FutureSessions, int TotalSchedules, int TotalSessions, int MembershipPlans);
```

> `PublicActivityDto` no expone `Id`, estado, capacidad, versión ni metadatos de archivos. `ActivityMediaDto` no expone `ObjectKey`.

---

## File Upload & Storage Flow

1. **Validación previa (sin tocar storage)**: la actividad existe; el `type` es válido; el MIME declarado está en la lista blanca (`image/webp`, `image/png`, `image/jpeg`); el tamaño declarado respeta el límite del perfil; para galería, el conteo actual es menor a 10.
2. **Procesamiento**: `IImageProcessor` decodifica, reescala y re-codifica a WebP (Decisión 13). Si falla la decodificación → 400.
3. **Clave**: `activities/{activityId}/logo/{uuid}.webp` o `activities/{activityId}/gallery/{uuid}.webp`.
4. **Subida** al bucket público (`IPublicFileStorageService`) con `Cache-Control` inmutable.
5. **Persistencia** del `ActivityMedia` (y, si es un logo, eliminación del logo previo; si es la primera imagen de galería, se marca principal) en una transacción EF Core.
6. **Compensación**: si el paso 5 falla, se borra el objeto recién subido (best-effort, con log).
7. **Post-commit**: si se reemplazó un logo, se borra el objeto anterior del bucket (best-effort).

**Eliminación de un medio**: primero se borra la fila (y se promueve la nueva imagen principal si corresponde) y se hace commit; después se borra el objeto del bucket (best-effort). Así un fallo del storage deja como mucho un objeto huérfano, nunca una referencia rota.

---

## Auditoría

Eventos registrados en `AuditLog` (`EntityName = "Activity"`, `EntityId = activityId`, `OldValues`/`NewValues` en JSON):

`ACTIVITY_CREATED`, `ACTIVITY_UPDATED` (solo los campos modificados), `ACTIVITY_STATUS_CHANGED`, `ACTIVITY_DELETED`, `ACTIVITY_MEDIA_ADDED`, `ACTIVITY_MEDIA_DELETED`, `ACTIVITY_MEDIA_PRIMARY_CHANGED`, `ACTIVITY_MEDIA_REORDERED`.

---

## Risks / Trade-offs

- **[Cambio de contrato de `GET /api/activities`]** → El frontend público deja de funcionar si no se migra en el mismo cambio. Mitigación: la sección 7 de `tasks.md` migra el frontend antes de cerrar el cambio.
- **[Guarda de inactivación más estricta]** → El administrativo debe cerrar los horarios y suspender las clases futuras antes de inactivar. Es más trabajo operativo, pero evita cancelaciones masivas implícitas con impacto en créditos. La respuesta 409 incluye conteos para guiar la acción.
- **[Medios legacy no migrados automáticamente]** → Las actividades existentes quedan sin imágenes hasta su recarga manual. Mitigación: respaldo en `ActivityLegacyMediaBackup`.
- **[Renombrar una actividad]** → Clases y reportes referencian `ActivityId`, por lo que muestran el nombre vigente. Si en el futuro se requiere el nombre histórico, las sesiones podrán congelar una captura al finalizar.
- **[Objetos huérfanos en storage]** → Se acotan con compensación y borrado post-commit. Si fuera necesario, un job de reconciliación futuro podría comparar los prefijos del bucket con `ActivityMedias`.
- **[Concurrencia al marcar imagen principal o subir logo]** → Los índices únicos parciales hacen que la base de datos rechace estados inválidos. La violación se traduce en 409.
