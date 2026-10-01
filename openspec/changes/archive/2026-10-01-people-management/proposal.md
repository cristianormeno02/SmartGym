## Why

En Smart-Gym es fundamental desacoplar la identidad física de un individuo de sus credenciales de acceso al sistema y de sus roles funcionales (`Persona ≠ Usuario ≠ Rol`). La entidad `Person` ya existe (`SmartGym.Domain.Entities.Identity.Person`, tabla `People`), pero nació como un apéndice del módulo de autenticación y arrastra limitaciones que impiden usarla como registro maestro:

- `Email` es obligatorio y único, lo que impide registrar presencialmente a menores, personas sin correo o clientes atendidos por secretaría.
- La identificación se limita a un campo `Dni` en texto libre, sin tipo de documento, sin país emisor y sin normalización (`12.345.678` y `12345678` se consideran documentos distintos).
- No existe ciclo de vida propio: el estado se reduce al `IsActive` heredado de `BaseEntity`, sin motivo, fecha ni autor del cambio.
- La foto es una URL externa (`PhotoUrl`, provista por Google) sin posibilidad de carga propia ni metadata.
- No hay endpoints para que el personal administre personas, ni reglas explícitas sobre quién puede ver o modificar datos personales (protegidos por la Ley 25.326 de Protección de Datos Personales).

El módulo **People** convierte la entidad existente en el registro maestro de personas: fuente única de verdad para datos personales, identificación unívoca y normalizada, ciclo de vida auditable, fotos de perfil en object storage y una API administrativa con autorización explícita.

## What Changes

* **Refactor de la entidad `Person` existente (no greenfield)**: se evoluciona la tabla `People` mediante una migración de esquema **y de datos** (`Dni` → documento tipado y normalizado; `PhotoUrl` → `ExternalAvatarUrl`; `Email` pasa a opcional). Los campos de certificado médico y los roles permanecen sin cambios.
* **Identificación robusta de documentos**: tipos `DNI`, `PASAPORTE`, `CI`, `OTRO` con país emisor (ISO 3166-1 alfa-2), normalización canónica determinista y unicidad sobre `(tipo, país emisor, número normalizado)` mediante índice único parcial. Se admiten múltiples personas sin documento.
* **Email opcional pero único cuando existe**: índice único parcial sobre el email normalizado, preservando la vinculación segura de cuentas Google por correo.
* **Ciclo de vida auditable**: estados `ACTIVA`, `INACTIVA`, `BLOQUEADA`, `FALLECIDA` con transiciones explícitas, motivo obligatorio en las sensibles, registro de fecha y autor, y reversión controlada de `FALLECIDA` por un Administrador. `IsActive` pasa a ser un valor derivado (`Status == ACTIVA`), de modo que las reglas existentes de los demás módulos que filtran por `IsActive` aplican automáticamente.
* **Fotos de perfil en Object Storage privado**: carga mediante `IFileStorageService` (Cloudflare R2), reencodeo a WebP sin metadata EXIF, persistencia de sólo metadata en PostgreSQL y entrega mediante URLs firmadas de corta duración.
* **Búsqueda insensible a mayúsculas y acentos**: columna de búsqueda normalizada con índice trigram (`pg_trgm`), búsqueda multi-palabra y detección automática de términos que corresponden a documentos.
* **API REST con autorización explícita**: endpoints administrativos para personal (`RequireStaff`), operaciones restringidas a Administrador y endpoints de autoservicio (`/api/people/me`) para que cada persona gestione sus datos de contacto y su foto.

## Capabilities

### New Capabilities
- `people-management`: Registro maestro de personas, validación y normalización de documentos, ciclo de vida/estados de persona, búsqueda, autorización de acceso a datos personales y fotos de perfil en object storage.

### Modified Capabilities
- `identity-access`: La vinculación de cuentas Google y el registro local pasan a depender de las reglas de People (email opcional y único cuando existe; nombre y apellido obligatorios; documento normalizado). Las personas en estado `FALLECIDA` no pueden iniciar sesión.

## Impact

* **Backend (.NET 10 / ASP.NET Core Web API)**:
  - Dominio: refactor de `Person`, nuevos value objects (`IdentificationDocument`, `Address`, `ProfileImage`, `EmergencyContact`), enums y normalizadores.
  - Aplicación: nuevo módulo `Modules/People` (DTOs, validadores FluentValidation, servicio y consultas).
  - Adaptación de consumidores existentes de `Person.Dni`/`PhotoUrl`/`Email`: `AuthService`, `AuthDtos`, `UsersController`, `MembershipService`, `ReservationService`, `FamilyGroupService` y sus DTOs.
  - `IFileStorageService`: nuevo método para obtener URLs de acceso temporales (implementado en `S3StorageService` y `LocalStorageService`).
  - Nuevo `PeopleController` en `api/people`.
* **Base de datos (PostgreSQL)**: migración de esquema y datos sobre la tabla `People` con verificación previa de duplicados; extensiones `pg_trgm` y `unaccent`; índices únicos parciales para documento y email; índice GIN trigram para búsqueda; token de concurrencia optimista.
* **Almacenamiento (Cloudflare R2)**: bucket privado, prefijo `avatars/{personId}/`.
* **Frontend (Angular 22)**: nueva feature de administración de personas (listado, ficha, formulario, carga de foto) y adaptación de modelos/vistas que hoy usan `dni` y `photoUrl` (`auth.models.ts`, `gym.models.ts`, login y portales de alumno, staff y admin).
* **Otros módulos**: Activities, Memberships y Reservations no requieren cambios de spec; el criterio de elegibilidad sigue siendo `Person.IsActive`, ahora derivado del estado. Se audita que los puntos de entrada de reservas y check-in apliquen esa verificación.
