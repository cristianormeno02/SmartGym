## Context

El sistema SmartGym modela la identidad separando estrictamente `Person` (datos biográficos y de contacto maestros), `User` (credenciales de autenticación) y `PersonRole` (relación N:M entre `Person` y `Role`, clave primaria compuesta `(PersonId, RoleId)` con `IsActive`).

Estado actual relevante:
- Existen `AuthController` (registro público, login local y Google), `AdminUserSeeder` y `UsersController` (`me`, `admin-overview`, `staff-desk`).
- `User` hereda `IsActive` de `BaseEntity` y además tiene `IsLockedOut`. `LoginAsync` sólo verifica `IsLockedOut` y personas fallecidas; nunca `IsActive`. La rama de vinculación por email de `LoginWithGoogleAsync` no verifica `IsLockedOut`.
- `Person.AddRole` sólo busca roles activos; si existe la fila inactiva agrega otra con la misma clave y la base rechaza el `INSERT`.
- El JWT dura 120 minutos (`Jwt:ExpirationMinutes`), incluye los roles como claims y no hay validación adicional (`OnTokenValidated`) ni sello de seguridad.
- `RoleType` (`SmartGym.Domain.Enums`) define `Administrator = 1`, `Secretary = 2`, `Instructor = 3`, `Student = 4`, `Public = 5`; los roles se siembran con `HasData`.
- `People.Email` tiene índice único filtrado; `Users.Username` guarda el email normalizado y tiene índice único.
- `PersonDetailDto` sólo expone `HasUserAccount`; no expone roles ni estado de la cuenta.
- La auditoría se registra con `AuditService` (`AuditLog`). No hay middleware global de excepciones: cada controller traduce `ConflictException`, `NotFoundException` y `ValidationException`.

Ver `proposal.md` para la justificación y `specs/` para los requerimientos normativos.

## Goals / Non-Goals

**Goals:**
- Endpoints administrativos protegidos por `RequireAdministrator` para consultar y aprovisionar cuentas, gestionar roles, cambiar estado de cuenta y restablecer contraseñas.
- Reglas de negocio: email obligatorio y libre como `Username`, complejidad de contraseña, salvaguarda del último administrador operativo en todos los caminos que pueden eliminarlo.
- Invalidación efectiva de sesiones al suspender, cambiar contraseña o modificar roles.
- Trazabilidad mediante `AuditLog`.
- UI en la Ficha de Persona y en el Directorio de Usuarios.

**Non-Goals:**
- Modificar el flujo de login más allá de rechazar cuentas suspendidas y validar el sello de seguridad.
- Cambiar la política de contraseñas del registro público o de `AdminSeed` (seguimiento).
- Reseteo de contraseña self-service por correo.

## Decisions

### 1. Endpoints identificados por `personId`
- **Decisión**: Todos los endpoints viven en `UsersController` y se identifican por `personId`, que es lo que la ficha y el directorio ya conocen:
  - `GET /api/users/{personId}/account` → `{ hasAccount, userId?, username?, isActive?, lastLoginUtc?, roles: string[] }`. Los roles se devuelven aunque la persona no tenga cuenta.
  - `POST /api/users/{personId}/account` → body `{ password, roleIds }`. 201 con el mismo DTO.
  - `PUT /api/users/{personId}/roles` → body `{ roleIds: int[] }`. 200 con el DTO.
  - `PATCH /api/users/{personId}/status` → body `{ isActive: bool }`. 200 con el DTO, para que la UI refleje el estado sin otra consulta.
  - `POST /api/users/{personId}/reset-password` → body `{ newPassword }`. 204.
- La lógica reside en un nuevo `IUserAccountService` en `SmartGym.Application.Modules.Identity`, no en `AuthService`, que queda dedicado a autenticación.
- Los roles asignables son exactamente `Administrator`, `Secretary`, `Instructor` y `Student` (ids 1–4). `Public` u otros ids devuelven 400. Una lista vacía es válida en `PUT /roles` (revoca todo, sujeto a la guarda) y en el aprovisionamiento.
- Mapeo de errores en el controller siguiendo el patrón `catch (Exception ex) when (TryMapException(ex, out var error))` de `PeopleController`: `ValidationException` → 400, `NotFoundException` → 404, `ConflictException` → 409.
- **Alternativas consideradas**:
  - *Endpoints en `PeopleController`*: descartado; credenciales y roles pertenecen a Identity.
  - *Identificar status/reset por `userId`*: descartado; obliga al frontend a resolver primero el usuario y mezcla identificadores.

### 2. Reasignación de roles sin violar la clave compuesta
- **Decisión**: `Person.AddRole` reactiva la `PersonRole` existente (`IsActive = true`, `AssignedAtUtc = now`) si existe inactiva, y sólo agrega una fila si no existe ninguna. La sincronización de `PUT /roles` calcula el conjunto deseado contra los roles activos y llama a `AddRole` / `RemoveRole`; reenviar el mismo conjunto no produce cambios ni auditoría.
- Ambos métodos ya invocan `EnsureNotDeceased`; el servicio traduce esa excepción a 409.

### 3. Suspensión de cuenta con `User.IsActive`
- **Decisión**: La suspensión administrativa usa `User.IsActive` (ya existe, ya se muestra en `admin-overview`). `IsLockedOut` se conserva con su semántica actual y no se modifica en este cambio.
- `LoginAsync` y las dos ramas de `LoginWithGoogleAsync` que encuentran un `User` existente (por `GoogleSubjectId` y por vinculación de email) rechazan con `UnauthorizedAccessException("La cuenta se encuentra suspendida.")` si `!user.IsActive`, y la rama de vinculación por email también verifica `IsLockedOut`. La suspensión no vincula ni modifica `GoogleSubjectId`.
- **Alternativas consideradas**: *Reutilizar `IsLockedOut`*: descartado; su nombre sugiere bloqueo temporal automático y dejaría `IsActive` sin significado.

### 4. Sello de seguridad para invalidar sesiones
- **Decisión**: Nueva columna `Users.SecurityStamp` (`string`, no nula). La entidad la inicializa con un `Guid` aleatorio y la columna declara en el modelo un default SQL (`md5(random()::text || clock_timestamp()::text)`) con el que la migración completa las filas existentes. `JwtTokenGenerator` agrega el claim `sstamp`. En `AddJwtBearer` se registra `JwtBearerEvents.OnTokenValidated`, que lee el usuario por id (`AsNoTracking`, proyección de `IsActive` y `SecurityStamp`) y llama a `context.Fail(...)` si no existe, está suspendido o el sello no coincide.
- El sello se rota al suspender, al restablecer la contraseña y al modificar roles (porque los roles viajan en el token). El usuario afectado recibe 401 en su siguiente request y debe volver a iniciar sesión, momento en que obtiene los roles vigentes.
- Tokens emitidos antes del despliegue no tienen `sstamp`: se rechazan, forzando un único re-login general.
- **Alternativas consideradas**:
  - *Aceptar la ventana de 120 minutos*: descartado; una cuenta suspendida o una contraseña filtrada seguirían operativas hasta dos horas.
  - *Lista de revocación por `jti`*: más estado y limpieza sin beneficio frente al sello.
- **Costo**: una lectura por clave primaria por request autenticado; aceptable para el volumen del gimnasio. Si fuese necesario, se puede cachear en memoria por pocos segundos.

### 5. Guarda del último administrador operativo
- **Definición**: un administrador operativo es una Persona con `PersonRole` `Administrator` activa, `Status == Active` y un `User` existente con `IsActive == true`.
- **Decisión**: un `ILastAdministratorGuard` (Application/Identity) expone `EnsureAnotherOperationalAdministratorAsync(Guid targetPersonId)`, que cuenta administradores operativos excluyendo a `targetPersonId` y arroja `ConflictException` si el conteo es 0:
  ```csharp
  var others = await _dbContext.PersonRoles
      .Where(pr => pr.RoleId == (int)RoleType.Administrator && pr.IsActive
                && pr.PersonId != targetPersonId
                && pr.Person.Status == PersonStatus.Active
                && pr.Person.User != null && pr.Person.User.IsActive)
      .CountAsync(cancellationToken);
  ```
  Se invoca sólo cuando el target es hoy un administrador operativo y la operación le quita esa condición:
  - `PUT /roles` sin `Administrator` en el conjunto deseado.
  - `PATCH /status` con `isActive = false`.
  - `PeopleService.ChangeStatusAsync` hacia cualquier estado distinto de `Active`.
- **Concurrencia**: cada operación protegida corre en una transacción que primero toma `SELECT pg_advisory_xact_lock(<constante>)` vía `Database.ExecuteSqlRawAsync`, de modo que dos revocaciones simultáneas se serializan y la segunda ve el efecto de la primera. Se descarta `Serializable` porque en PostgreSQL produce fallos 40001 que requerirían reintentos.
- **Alternativas consideradas**: *Validar sólo en frontend*: inseguro ante llamadas directas a la API.

### 6. Validación de contraseña
- **Decisión**: Un validador FluentValidation reutilizable (`PasswordPolicyValidator`) exige mínimo 8 caracteres, al menos una mayúscula, un dígito y un carácter no alfanumérico (`^(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{8,}$`). Se aplica al aprovisionamiento y al reseteo. El hash usa el `IPasswordHasher` existente. La contraseña nunca se escribe en auditoría ni en logs.

### 7. Aprovisionamiento
- Validaciones en orden: persona existe (404); no fallecida (409); no tiene `User` (409); tiene email (400, "complete el email en la ficha"); ningún `User.Username` coincide con el email normalizado (409); contraseña válida (400); `roleIds` válidos (400). Si se incluye `Administrator` no aplica la guarda.
- Crea `User { Username = email normalizado, PasswordHash, IsEmailConfirmed = false, IsActive = true, SecurityStamp nuevo }` y sincroniza roles con la misma lógica de la decisión 2.
- La violación del índice único de `Users.Username` o de `Users.PersonId` por carrera se traduce a 409.

### 8. Auditoría
- Cada operación registra un `AuditLog` mediante `AuditService` con el administrador actual (`ICurrentUserService`): `UserProvisioned`, `UserRolesChanged` (valores anterior y nuevo), `UserSuspended` / `UserReactivated`, `UserPasswordReset` (sin valores). `PersonRole` no incorpora un campo `AssignedBy`; la autoría queda en el log.

### 9. Frontend (Angular)
- `UserManagementService` consume los cinco endpoints.
- En `frontend/src/app/features/people/person-detail.ts`, la tarjeta "Cuenta de Acceso y Roles" se muestra sólo a usuarios con rol `Administrator` y se carga con `GET /account`:
  - Sin cuenta: "Sin cuenta de usuario creada" + botón "Crear Cuenta de Acceso" (deshabilitado con explicación si la persona no tiene email o está fallecida).
  - Con cuenta: estado Activa/Suspendida, chips de roles y acciones.
  - Modales: `ProvisionUserDialog` (contraseña + checkboxes de los cuatro roles), `ManageRolesDialog`, `ResetPasswordDialog` (indicador de requisitos), `ConfirmStatusDialog`.
  - Los 409 muestran el mensaje del backend (último administrador, email en uso, cuenta existente).
- En `frontend/src/app/features/portal/admin/admin-portal.ts`, cada fila de Usuarios agrega "Ver Ficha" (navega a `/portal/personas/{personId}`) y "Roles" (abre `ManageRolesDialog`).
- El interceptor HTTP trata un 401 en una sesión existente como sesión invalidada: limpia el token y redirige a login. Si el administrador se modifica a sí mismo (roles o contraseña) también será desconectado; la UI lo advierte en la confirmación.

## Risks / Trade-offs

- **[Lectura por request para validar el sello]** → Lectura por PK sin tracking; cache corto si el volumen lo exigiera.
- **[Re-login forzado al desplegar]** → Tokens sin `sstamp` se rechazan una única vez; se comunica en el despliegue.
- **[Administrador que se quita a sí mismo el rol o se suspende]** → Permitido si existe otro administrador operativo; la UI advierte que su sesión se cerrará.
- **[Email de la Persona editado después del aprovisionamiento]** → Ya mitigado: People rechaza con 409 el cambio de email de personas con cuenta (requerimiento "Email de personas con cuenta de usuario"), por lo que `Username` y email no se desincronizan.
- **[Advisory lock dependiente de PostgreSQL]** → El proveedor de producción y de las pruebas de integración es PostgreSQL. Las pruebas unitarias que usen un proveedor en memoria deben abstraer el lock detrás de una interfaz (`IIdentityLock`) con implementación nula.

## Migration Plan

- Migración no destructiva que agrega `Users.SecurityStamp` (no nula, con default SQL aleatorio declarado también en `UserConfiguration`, de modo que modelo y base coinciden).
- No cambian `Roles` ni `PersonRoles`; los roles ya están sembrados con `HasData`.
- Al desplegar, todas las sesiones vigentes se invalidan una vez (tokens sin `sstamp`).
- Rollback: revertir la migración y el código; la columna no es usada por versiones previas.
