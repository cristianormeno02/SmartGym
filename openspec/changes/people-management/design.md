## Context

Smart-Gym es un Monolito Modular sobre .NET 10 (ASP.NET Core Web API) con EF Core y PostgreSQL, y un frontend Angular 22 (Standalone Components y Signals). El principio rector es:

```text
Persona ≠ Usuario ≠ Rol
```

**Estado actual (punto de partida, no greenfield):**

- `Person` (`SmartGym.Domain/Entities/Identity/Person.cs`, tabla `People`, columnas PascalCase) hereda de `BaseEntity` (`Id`, `CreatedAtUtc`, `UpdatedAtUtc`, `IsActive`) y contiene: `FirstName`, `LastName`, `Dni?` (único filtrado, sin normalizar), `Email` (obligatorio, único), `PhoneNumber?`, `BirthDate?`, `PhotoUrl?` (URL de Google), campos de certificado médico, contacto de emergencia (`EmergencyContactName/Phone/Relationship`) y la colección `PersonRoles`.
- `User` referencia a `Person` mediante `PersonId` (1 a 0..1). `User.Username` es el email normalizado de la persona.
- `AuthService` crea personas en el registro local (exige `Dni`) y en el login con Google (sin `Dni`, con `LastName` posiblemente vacío y `PhotoUrl` = foto de Google). Vincula cuentas Google buscando `Person.Email`.
- `Person.IsActive` ya se usa como criterio de elegibilidad en Activities (instructores) y Memberships (alumnos).
- `IFileStorageService` expone `UploadFileAsync` (devuelve la clave: `{folder}/{guid}_{fileName}` en S3, `/storage/{folder}/{guid}_{fileName}` en local), `DownloadFileAsync` y `DeleteFileAsync`. No ofrece URLs de acceso.
- La autorización usa policies: `RequireAdministrator`, `RequireSecretary`, `RequireStaff` (Administrador + Secretario), `RequireTeachingAndStaff`, `RequireStudent`.
- Los controllers usan la ruta `api/[controller]` (sin versión).

## Goals / Non-Goals

**Goals:**
- Evolucionar `Person` a registro maestro biográfico y de contacto, preservando los datos existentes mediante migración de esquema y datos.
- Permitir personas sin email, sin documento y sin cuenta de usuario.
- Identificación por documento tipado, con país emisor, normalización canónica determinista y unicidad garantizada en base de datos.
- Ciclo de vida con transiciones explícitas, auditoría del cambio y efecto definido sobre los demás módulos.
- Fotos de perfil en R2 privado, saneadas y entregadas mediante URL firmada.
- Búsqueda paginada insensible a mayúsculas y acentos, con índices adecuados desde el inicio.
- API administrativa y de autoservicio con autorización explícita.
- Frontend Angular para la administración de personas.

**Non-Goals:**
- Contraseñas, JWT, refresh tokens y OIDC (módulo `Auth`). El cambio de email de una persona con cuenta de usuario queda fuera de alcance (requiere un flujo de Auth con re-verificación).
- Asignación de roles y permisos (módulo `Auth`).
- Membresías, créditos y planes (módulo `Memberships`).
- Grupos familiares y parentescos (módulo `Promotions`), más allá del contacto de emergencia/tutor que ya vive en `Person`.
- Certificados médicos: los campos existentes permanecen en `Person` sin cambios; su eventual traslado a `Operations` es un cambio aparte.
- Detección difusa de duplicados para personas sin documento (posible mejora futura: advertencia por coincidencia de nombre + fecha de nacimiento).

## Decisions

### 1. Modelo de dominio de `Person`

`Person` sigue siendo la raíz de agregado y conserva su `Id` (`Guid` generado por `BaseEntity`). Las propiedades pasan a tener setters privados y se modifican mediante métodos de dominio que validan invariantes.

```text
Person (Aggregate Root, tabla "People")
├── Id, CreatedAtUtc, UpdatedAtUtc            (BaseEntity)
├── IsActive: bool                            (BaseEntity; DERIVADO: Status == Active, sincronizado por el dominio)
├── FirstName: string                         (obligatorio, trim, no vacío, max 100)
├── LastName: string                          (obligatorio, trim, no vacío, max 100)
├── SearchName: string                        (derivado: FirstName + " " + LastName normalizado, max 201)
├── Status: PersonStatus                      (Active | Inactive | Blocked | Deceased)
├── StatusReason: string?                     (max 500)
├── StatusChangedAtUtc: DateTime?
├── StatusChangedByUserId: Guid?
├── Document: IdentificationDocument?         (owned, columnas Document*)
│   ├── Type: DocumentType                    (Dni | Passport | IdentityCard | Other)
│   ├── IssuingCountry: string                (ISO 3166-1 alfa-2, 2 chars; "AR" forzado para DNI)
│   ├── Number: string                        (tal como se ingresó, trim, max 50)
│   └── NormalizedNumber: string              (max 50)
├── BirthDate: DateTime?                      (existente; se conserva el tipo)
├── Gender: Gender?                           (Male | Female | X | NotInformed)
├── Email: string?                            (trim + lower, max 256)
├── PrimaryPhone: string?                     (max 30; renombra PhoneNumber)
├── SecondaryPhone: string?                   (max 30)
├── Address: Address?                         (owned, columnas Address*)
│   ├── Street (150), Number (20), Floor (10), Apartment (10), PostalCode (20),
│   ├── City (100), StateProvince (100)
│   └── CountryCode (ISO 3166-1 alfa-2)
├── ProfileImage: ProfileImage?               (owned, columnas ProfileImage*)
│   ├── Key (500), ContentType (50), SizeBytes, UploadedAtUtc
├── ExternalAvatarUrl: string?                (max 500; foto provista por Google; renombra PhotoUrl)
├── EmergencyContact (Name 150, Phone 50, Relationship 50)   (columnas existentes)
├── MedicalCertificateUrl / MedicalCertificateExpiration     (sin cambios)
├── Version: uint                             (xmin de PostgreSQL, concurrencia optimista)
├── User?, PersonRoles                        (sin cambios)
```

- Los enums se serializan en la API como strings en mayúsculas en español mediante `[JsonStringEnumMemberName]` (`DNI`, `PASAPORTE`, `CI`, `OTRO`; `ACTIVA`, `INACTIVA`, `BLOQUEADA`, `FALLECIDA`; `MASCULINO`, `FEMENINO`, `X`, `NO_INFORMA`). En base se persisten como string (`HasConversion<string>()`) con el nombre del miembro C#.
- `Gender` incluye `X` para alinearse con el DNI argentino no binario (Ley 26.743).
- Los value objects owned usan nombres de columna explícitos en PascalCase (`DocumentType`, `DocumentIssuingCountry`, `DocumentNumber`, `DocumentNumberNormalized`, ...), siguiendo la convención existente.
- `IsUnderage()` y `HasValidEmergencyContact()` se conservan.

*Alternativa descartada*: tablas separadas `Student`, `Instructor`, `Staff` (duplicaría datos personales).

*Alternativa descartada*: eliminar `IsActive` de `Person`. Está en `BaseEntity` y ya se usa como criterio de elegibilidad en otros módulos; derivarlo del estado reutiliza esas reglas sin tocarlas.

### 2. Identificación y normalización de documentos

`DocumentNormalizer` (servicio de dominio puro, sin dependencias):

| Tipo | Caracteres aceptados en la entrada | Normalización | Validación del resultado |
| :--- | :--- | :--- | :--- |
| `DNI` | dígitos, `.`, `-`, espacios | quitar no dígitos; quitar ceros a la izquierda | 1 a 9 dígitos, distinto de `0` |
| `PASAPORTE`, `CI`, `OTRO` | letras, dígitos, `.`, `-`, `/`, espacios | quitar todo lo no alfanumérico ASCII; `ToUpperInvariant()` | 3 a 30 caracteres |

- Un DNI con letras se **rechaza** (no se descartan las letras silenciosamente).
- Un resultado vacío es un error de validación: nunca se persiste `""` en `DocumentNumberNormalized` (el índice parcial sólo excluye `NULL`).
- El documento es todo o nada: tipo y número se informan juntos o ninguno; el país emisor es obligatorio para `PASAPORTE`, `CI` y `OTRO`, y se fuerza a `AR` para `DNI`.
- Libreta de Enrolamiento / Libreta Cívica se cargan como `OTRO` con país `AR`.

Unicidad en PostgreSQL:

```sql
CREATE UNIQUE INDEX "IX_People_Document_Unique"
ON "People" ("DocumentType", "DocumentIssuingCountry", "DocumentNumberNormalized")
WHERE "DocumentNumberNormalized" IS NOT NULL;
```

- Se incluye el país emisor porque los números de pasaporte y cédula sólo son únicos dentro de cada país.
- La aplicación verifica duplicados antes de guardar para devolver un mensaje claro. Ante una carrera entre dos altas simultáneas, la violación del índice (`SqlState 23505`) se traduce a **409 Conflict** con el mismo mensaje.

*Alternativa descartada*: índice único sobre el número original (falla ante `12.345.678` vs `12345678`).

### 3. Email

- Se normaliza con `Trim().ToLowerInvariant()`; una cadena vacía se convierte en `NULL`.
- Índice único parcial: `"IX_People_Email_Unique" ON "People" ("Email") WHERE "Email" IS NOT NULL` (reemplaza el índice único actual).
- **Por qué sigue siendo único**: el login con Google vincula la identidad externa con la Persona cuyo email coincide. Si hubiera dos personas con el mismo email, la vinculación sería ambigua y podría asociar la cuenta a la persona equivocada.
- Si la persona tiene un `User` vinculado, su email no se puede modificar desde People (el email es el `Username`): la operación se rechaza con 409. El cambio de email de cuentas queda para un flujo de Auth.

### 4. Ciclo de vida y transiciones

```text
            ┌──────────────┐
   alta ──► │    ACTIVA    │ ◄───────────────┐
            └──┬────────┬──┘                 │
               │        │                    │
               ▼        ▼                    │
        ┌──────────┐  ┌───────────┐          │
        │ INACTIVA │◄►│ BLOQUEADA │          │
        └────┬─────┘  └─────┬─────┘          │
             │  (y ambas ◄► ACTIVA)          │
             ▼              ▼                │
            ┌──────────────┐   reversión     │
            │  FALLECIDA   │ ── (sólo Admin, ┘
            └──────────────┘    con motivo)
```

| Desde \ Hacia | ACTIVA | INACTIVA | BLOQUEADA | FALLECIDA |
| :--- | :--- | :--- | :--- | :--- |
| ACTIVA | — | Staff | Staff (motivo) | Admin (motivo) |
| INACTIVA | Staff | — | Staff (motivo) | Admin (motivo) |
| BLOQUEADA | Staff | Staff | — | Admin (motivo) |
| FALLECIDA | Admin (motivo, corrección de error) | ✗ | ✗ | — |

- Una transición al mismo estado o no listada se rechaza con **409**.
- Toda transición registra `StatusChangedAtUtc`, `StatusChangedByUserId` (usuario autenticado) y `StatusReason` (obligatorio donde se indica, opcional en el resto; se limpia si no se informa).
- `FALLECIDA` deja el registro en sólo lectura: se rechazan la edición de datos y la carga o eliminación de foto (409). Sólo se admite la reversión por un Administrador, pensada para corregir errores de carga.
- `IsActive` se sincroniza en cada transición (`Status == Active`).

**Efecto sobre otros módulos:**

| Dimensión | Módulo | Relación con `Person.Status` |
| :--- | :--- | :--- |
| Elegibilidad para nuevas membresías, créditos, reservas y para dictar clases | Memberships, Reservations, Activities | Requiere `ACTIVA` (vía `IsActive`, criterio que esos módulos ya aplican) |
| Inicio de sesión | Auth | Se deniega sólo en `FALLECIDA`. `INACTIVA` y `BLOQUEADA` pueden iniciar sesión para consultar su historial |
| Estado de cuenta (`User.IsLockedOut`) | Auth | Independiente: un bloqueo de cuenta no cambia `Person.Status` y viceversa |
| Membresías vigentes / historial de reservas | Memberships, Reservations | Se preservan sin cambios ante cualquier transición; ningún cambio de pago o membresía modifica `Person.Status` |

### 5. Fotos de perfil

- Bucket R2 **privado**. La API nunca expone claves: devuelve `photoUrl` firmada con validez de 15 minutos.
- Nuevo método en `IFileStorageService`:
  ```csharp
  Task<string> GetAccessUrlAsync(string key, TimeSpan expiresIn, CancellationToken cancellationToken = default);
  ```
  `S3StorageService` genera una URL presignada (`GetPreSignedURL`); `LocalStorageService` devuelve la ruta `/storage/...` que ya usa como clave.
- Validación de la carga (en este orden, antes de tocar el almacenamiento):
  1. Tamaño ≤ 5 MB (5 × 1024 × 1024 bytes). El límite se configura también en el request (`RequestSizeLimit`).
  2. `Content-Type` declarado ∈ {`image/jpeg`, `image/png`, `image/webp`}.
  3. Firma mágica del archivo coherente con un formato admitido.
  4. El archivo se puede decodificar como imagen.
- Procesamiento: se reencodea a WebP, redimensionando a un máximo de 1024 × 1024 manteniendo la proporción y descartando toda metadata (EXIF, incluida la geolocalización). Librería: **SkiaSharp** (licencia MIT). La metadata persistida describe la imagen procesada (`image/webp`).
- Clave: `UploadFileAsync(stream, "avatar.webp", "image/webp", $"avatars/{personId}")` → `avatars/{personId}/{guid}_avatar.webp`.
- Reemplazo:
  1. Subir la nueva imagen.
  2. Guardar la metadata en base. Si falla, se borra la imagen recién subida (compensación) y se propaga el error.
  3. Borrar la imagen anterior en modo *best-effort*: un fallo se registra en el log como objeto huérfano, pero no hace fallar la operación.
- Eliminación: se limpia la metadata en base y luego se borra el objeto (*best-effort*).
- `ExternalAvatarUrl` (foto de Google) se conserva aparte. La foto mostrada es la propia si existe; si no, la externa. Eliminar la foto propia no borra la externa.

### 6. Búsqueda

- `SearchName` se calcula en el dominio con un `TextNormalizer`: descomposición Unicode (NFD), eliminación de marcas diacríticas, minúsculas y espacios colapsados (`"José  Pérez"` → `"jose perez"`). Se recalcula cada vez que cambian el nombre o el apellido.
- Índice: `CREATE INDEX "IX_People_SearchName_Trgm" ON "People" USING gin ("SearchName" gin_trgm_ops)` (extensión `pg_trgm`), además de un B-tree sobre `("LastName", "FirstName")` para ordenar.
- Consulta `GET /api/people?search=&status=&documentType=&pageNumber=&pageSize=`:
  - `search` se normaliza con el mismo `TextNormalizer` y se divide en palabras. Cada palabra debe estar contenida en `SearchName` (AND), lo que hace que "juan perez" encuentre "Pérez, Juan".
  - Si el término, normalizado como documento genérico, contiene al menos un dígito y tiene 5 o más caracteres, también se buscan coincidencias exactas de `DocumentNumberNormalized`, aplicando además la normalización de DNI (sin ceros a la izquierda). Los resultados se combinan (OR).
  - Paginación: `pageNumber` (por defecto 1) y `pageSize` (por defecto 20, máximo 100). Orden por defecto: `LastName`, `FirstName`, `Id`.
  - La respuesta incluye `items`, `pageNumber`, `pageSize` y `totalCount`.

*Alternativa descartada*: `unaccent()` en tiempo de consulta. No se puede indexar sin un wrapper `IMMUTABLE` y no es testeable en memoria. La columna derivada resuelve ambos problemas.

### 7. API REST y autorización

Controller `PeopleController` con ruta `api/people` (convención `api/[controller]` existente).

| Método y ruta | Policy | Descripción |
| :--- | :--- | :--- |
| `GET /api/people` | `RequireStaff` | Búsqueda paginada |
| `GET /api/people/{id}` | `RequireStaff` | Ficha completa |
| `POST /api/people` | `RequireStaff` | Alta |
| `PUT /api/people/{id}` | `RequireStaff` | Actualización de datos personales y de contacto (con `version`) |
| `PATCH /api/people/{id}/status` | `RequireStaff`; Admin para `FALLECIDA` y su reversión | Cambio de estado |
| `PUT /api/people/{id}/photo` | `RequireStaff` | Carga/reemplazo de foto (multipart) |
| `DELETE /api/people/{id}/photo` | `RequireStaff` | Eliminación de foto |
| `GET /api/people/me` | autenticado con Persona | Ficha propia |
| `PUT /api/people/me/contact` | autenticado con Persona | Teléfonos, domicilio y contacto de emergencia propios |
| `PUT /api/people/me/photo`, `DELETE /api/people/me/photo` | autenticado con Persona | Foto propia |

- No existe `DELETE /api/people/{id}`: la baja es siempre lógica (`INACTIVA`).
- El autoservicio no permite modificar nombre, apellido, documento, fecha de nacimiento, email ni estado.
- Los instructores no acceden a la ficha completa. Siguen viendo los datos mínimos que ya exponen los módulos de actividades y reservas.
- Respuestas: 400 (validación), 403, 404, 409 (documento o email duplicado, transición inválida, persona `FALLECIDA`, conflicto de versión, cambio de email con cuenta vinculada), con el formato de error estándar del proyecto.
- Concurrencia optimista: `PUT` exige el `version` leído; si no coincide con `xmin`, responde 409.
- Menores de edad: si `BirthDate` indica menos de 18 años, el alta o la edición exigen contacto de emergencia completo (nombre, teléfono y vínculo), reutilizando `HasValidEmergencyContact()`.

### 8. Adaptación de `Auth`

- **Registro local**: el `Dni` del request pasa por `DocumentNormalizer` (tipo `DNI`, país `AR`). La verificación de duplicados usa documento normalizado y email normalizado.
- **Login con Google sin apellido**: si el token no trae `family_name`, se intenta derivar el apellido del claim `name`. Si no es posible, se usa el valor fijo `Person.MissingLastNamePlaceholder = "Sin apellido"`, editable luego por la persona o el personal. Así se respeta la invariante de apellido no vacío.
- **Foto de Google**: se escribe en `ExternalAvatarUrl` (antes `PhotoUrl`).
- **Login de personas fallecidas**: el login (local y Google) se rechaza si `Person.Status == Deceased`.
- **DTOs y vistas**: `UsersController`, `AuthDtos` y los DTOs de Memberships, Reservations y FamilyGroups reemplazan `Dni` por `documentType` + `documentNumber`, y `PhotoUrl` por la URL de foto resuelta.

## Risks / Trade-offs

- **[Riesgo] Datos existentes que colisionan tras normalizar** (por ejemplo, `12.345.678` y `12345678`, o emails que difieren sólo en mayúsculas).
  → *Mitigación*: la migración ejecuta primero una verificación que aborta (`RAISE EXCEPTION`) listando los Ids en conflicto. Se resuelven manualmente antes de reintentar. Lo mismo aplica a valores de `Dni` con letras.
- **[Riesgo] Diferencias entre el `TextNormalizer` de .NET y `unaccent` usado en el backfill de `SearchName`.**
  → *Mitigación*: test que compara ambos para el alfabeto español (á, é, í, ó, ú, ü, ñ y mayúsculas). Cualquier fila con diferencias se corrige en su próxima edición.
- **[Riesgo] Fallo entre la subida a R2 y el guardado en base.**
  → *Mitigación*: compensación (borrado del objeto nuevo) y borrado *best-effort* del anterior, con log de huérfanos.
- **[Riesgo] Marcar por error a una persona como `FALLECIDA`.**
  → *Mitigación*: sólo un Administrador puede hacerlo, con motivo obligatorio, y puede revertirlo.
- **[Trade-off] Rollback de la migración**: el `Down` restaura `Dni`, `PhotoUrl` y `Email NOT NULL`. Si ya existen personas sin email, o documentos que no son DNI, el rollback falla o pierde datos. Es reversible sólo antes de que se carguen esos registros.
- **[Trade-off] SkiaSharp agrega binarios nativos** a la imagen de despliegue. Se acepta a cambio de sanear las imágenes del lado del servidor.

## Migration Plan

Migración EF Core **`EvolvePersonToPeopleModule`** sobre la tabla existente `People`:

1. **Verificación previa (SQL)**: aborta si existen:
   - valores de `Dni` con caracteres distintos de dígitos, `.`, `-` o espacios;
   - duplicados de `Dni` normalizado;
   - duplicados de `lower(trim("Email"))` no vacíos.
2. **Extensiones**: `CREATE EXTENSION IF NOT EXISTS pg_trgm; CREATE EXTENSION IF NOT EXISTS unaccent;`.
3. **Columnas nuevas**:
   - estado y auditoría (`Status` con default `'Active'`, `StatusReason`, `StatusChangedAtUtc`, `StatusChangedByUserId`);
   - documento, `Gender`, `SecondaryPhone`, domicilio, `ProfileImage*`, `SearchName`.
4. **Backfill**:
   - `Status = 'Inactive'` donde `IsActive = false`.
   - `Dni` → `DocumentType = 'Dni'`, `DocumentIssuingCountry = 'AR'`, `DocumentNumber = Dni`, `DocumentNumberNormalized = ltrim(regexp_replace(Dni, '\D', '', 'g'), '0')`.
   - `Email = NULLIF(lower(trim("Email")), '')`.
   - `SearchName = lower(regexp_replace(unaccent(trim("FirstName") || ' ' || trim("LastName")), '\s+', ' ', 'g'))`.
   - `LastName` vacío → `'Sin apellido'`.
5. **Renombres**: `PhoneNumber` → `PrimaryPhone`; `PhotoUrl` → `ExternalAvatarUrl` (todos los valores actuales provienen de Google).
6. **Restricciones e índices**:
   - `Email` pasa a admitir `NULL`;
   - se reemplaza el índice de `Email` por el único parcial y se elimina el de `Dni`;
   - se crean `IX_People_Document_Unique`, `IX_People_SearchName_Trgm` e `IX_People_LastName_FirstName`;
   - se agrega el check de consistencia del documento (todo o nada).
7. **Eliminación** de la columna `Dni`.
8. **Rollback**: ver la restricción del trade-off anterior.
