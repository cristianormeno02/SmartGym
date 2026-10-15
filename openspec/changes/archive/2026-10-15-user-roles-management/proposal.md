## Why

Actualmente, aunque el modelo de datos de SmartGym separa estrictamente `Persona`, `Usuario` y `Rol`, no existe un flujo operativo completo en el sistema para que los administradores puedan aprovisionar cuentas de acceso a personas registradas en el padrón, asignar o revocar sus roles funcionales, suspender o reactivar cuentas ante contingencias, y restablecer contraseñas administrativas desde el Portal Administrativo o la Ficha de Persona. Esta capacidad es indispensable para delegar funciones a secretarios e instructores y otorgar acceso a los alumnos de manera segura y controlada.

Además, el código actual presenta huecos que este flujo dejaría expuestos:
- `Person.AddRole` agrega una fila nueva aunque exista una `PersonRole` inactiva para el mismo rol, lo que viola la clave primaria compuesta `(PersonId, RoleId)` al reasignar un rol previamente revocado.
- El inicio de sesión no consulta `User.IsActive`, por lo que no existe hoy un estado de "cuenta suspendida" efectivo; la vinculación por email en el login con Google tampoco consulta `IsLockedOut`.
- Los JWT son stateless (120 minutos, roles embebidos) y no existe ningún mecanismo para invalidar sesiones emitidas.

## What Changes

- **Aprovisionamiento administrativo de cuentas**: Permitir a administradores crear credenciales locales (`User`) vinculadas a una `Person` existente, no fallecida y sin cuenta previa, que posea email cargado y no utilizado como `Username` por otra cuenta, con roles iniciales asignados.
- **Gestión dinámica de roles**: Habilitar la asignación y revocación idempotente de roles (`Administrator`, `Secretary`, `Instructor`, `Student`) para cualquier persona, reactivando la `PersonRole` existente al reasignar, y con protección contra la pérdida del último administrador operativo.
- **Ciclo de vida de cuentas de acceso**: Permitir la suspensión (`User.IsActive = false`) y reactivación (`User.IsActive = true`) de cuentas de usuario, bloqueando el inicio de sesión local y con Google a cuentas suspendidas sin alterar datos personales.
- **Restablecimiento administrativo de contraseñas**: Permitir a administradores asignar una nueva contraseña provisoria a un usuario existente.
- **Invalidación de sesiones**: Incorporar un sello de seguridad (`User.SecurityStamp`) incluido como claim en el JWT y validado en cada request; se rota al suspender, cambiar contraseña o modificar roles, de modo que los tokens previos dejan de ser aceptados de inmediato.
- **Protección del último administrador en todos los caminos**: revocar el rol `Administrator`, suspender la cuenta y cambiar el estado de la Persona (`PATCH /api/people/{id}/status`) quedan bloqueados si dejarían al sistema sin administradores operativos.
- **Integración visual en Ficha de Persona y Directorio de Usuarios**:
  - En `/portal/personas/{id}`: tarjeta de "Cuenta de Acceso y Roles" (solo para administradores) con estado de cuenta, roles asignados y acciones rápidas (crear cuenta, gestionar roles, restablecer contraseña, suspender/reactivar).
  - En `/portal/admin` (pestaña Usuarios): acceso directo a la ficha de cada persona y a la gestión de sus roles.

## Capabilities

### Modified Capabilities
- `identity-access`: Incorpora aprovisionamiento de cuentas locales para personas existentes, asignación y revocación dinámica de roles con salvaguarda del último administrador, suspensión y reactivación de cuentas, restablecimiento administrativo de contraseñas, invalidación de sesiones por sello de seguridad, e interfaces de gestión en la Ficha de Persona y Directorio de Usuarios.
- `people-management`: El cambio de estado de una Persona se rechaza si dejaría al sistema sin administradores operativos.

## Impact

- **Backend**:
  - Nuevo `IUserAccountService` en `SmartGym.Application.Modules.Identity` y endpoints en `UsersController`, todos identificados por `personId`: `GET /api/users/{personId}/account`, `POST /api/users/{personId}/account`, `PUT /api/users/{personId}/roles`, `PATCH /api/users/{personId}/status`, `POST /api/users/{personId}/reset-password`.
  - Corrección de `Person.AddRole` para reactivar la `PersonRole` inactiva existente.
  - `AuthService`: rechazo de cuentas suspendidas en login local y en las tres ramas del login con Google.
  - Nueva columna `Users.SecurityStamp` (migración no destructiva), claim en `JwtTokenGenerator` y validación en `JwtBearerEvents.OnTokenValidated`.
  - Validador compartido de complejidad de contraseña para aprovisionamiento y reseteo.
  - Auditoría mediante el `AuditService` existente (`AuditLog`), registrando el administrador que ejecuta cada operación.
  - `PeopleService.ChangeStatusAsync` consulta la guarda del último administrador.
- **Frontend**:
  - `UserManagementService` y tarjeta "Cuenta de Acceso y Roles" con modales en `person-detail.ts`.
  - Accesos directos en la pestaña Usuarios de `admin-portal.ts`.
  - Manejo de 401 por sesión invalidada (redirección a login).
- **Seguridad**:
  - Endpoints restringidos exclusivamente a la política `RequireAdministrator`.
  - Cada request autenticado consulta el `SecurityStamp` vigente del usuario (una lectura por clave primaria).

## Non-Goals

- Cambiar la política de contraseñas del registro público (`POST /api/auth/register`) o de `AdminSeed`; se propone como seguimiento.
- Reseteo de contraseña self-service por correo.
