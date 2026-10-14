## 1. Dominio y persistencia (TDD)

- [ ] 1.1 Corregir `Person.AddRole` para reactivar la `PersonRole` inactiva existente en lugar de agregar otra; cubrir con pruebas en `SmartGym.Domain.UnitTests/Identity/PersonDomainTests.cs` (asignar, revocar, reasignar y persona fallecida).
- [ ] 1.2 Agregar `User.SecurityStamp` (no nulo, inicializado con un valor aleatorio), su configuración EF y la migración que rellena las filas existentes; verificar con `PersonModelConfigurationTests` / `MigrationVerificationTests`.

## 2. Autenticación y sesiones (TDD)

- [ ] 2.1 Rechazar cuentas con `User.IsActive = false` en `LoginAsync` y en las dos ramas de `LoginWithGoogleAsync` que encuentran un usuario; agregar la verificación de `IsLockedOut` faltante en la rama de vinculación por email. Pruebas en `AuthServiceTests` y `GoogleAuthTests`.
- [ ] 2.2 Incluir el claim `sstamp` en `JwtTokenGenerator` (prueba en `JwtTokenGeneratorTests`).
- [ ] 2.3 Registrar `JwtBearerEvents.OnTokenValidated` que rechace tokens de usuarios inexistentes, suspendidos o con sello distinto al vigente (incluidos los tokens sin `sstamp`).

## 3. Servicio de cuentas y roles (TDD)

- [ ] 3.1 Crear `PasswordPolicyValidator` (mínimo 8 caracteres, mayúscula, dígito, carácter no alfanumérico) y los validadores de `ProvisionUserAccountRequest`, `UpdateUserRolesRequest`, `UpdateUserStatusRequest` y `AdminResetPasswordRequest` (roles asignables sólo 1–4); pruebas unitarias de validación.
- [ ] 3.2 Crear `ILastAdministratorGuard` (definición de administrador operativo: rol activo, persona `Active`, usuario activo) e `IIdentityLock` (implementación con `pg_advisory_xact_lock` e implementación nula para pruebas unitarias); pruebas unitarias del conteo excluyendo al target.
- [ ] 3.3 Implementar `IUserAccountService.GetAccountAsync` y `ProvisionAsync` con el orden de validaciones del design (404, fallecida 409, cuenta existente 409, sin email 400, username en uso 409, contraseña y roles 400), traducción de violaciones de índice único a 409 y auditoría `UserProvisioned`; pruebas de cada escenario.
- [ ] 3.4 Implementar `UpdateRolesAsync`: sincronización idempotente, guarda del último administrador dentro de transacción con lock, rotación del sello sólo si hubo cambios y auditoría `UserRolesChanged` con valores anterior y nuevo; pruebas de idempotencia, reasignación y bloqueo.
- [ ] 3.5 Implementar `UpdateStatusAsync` (guarda al suspender, rotación del sello, auditoría `UserSuspended` / `UserReactivated`) y `ResetPasswordAsync` (404 sin cuenta, hash, rotación del sello, auditoría `UserPasswordReset` sin contraseña); pruebas de servicio.
- [ ] 3.6 Invocar la guarda del último administrador desde `PeopleService.ChangeStatusAsync` al pasar a un estado distinto de `Active`; pruebas en el módulo People.

## 4. API

- [ ] 4.1 Exponer en `UsersController`, con la política `RequireAdministrator` y el patrón `TryMapException` (400/404/409): `GET` y `POST /api/users/{personId}/account`, `PUT /api/users/{personId}/roles`, `PATCH /api/users/{personId}/status`, `POST /api/users/{personId}/reset-password`; registrar los servicios en DI y verificar compilación limpia.
- [ ] 4.2 Pruebas de integración en `SmartGym.IntegrationTests` (PostgreSQL): 403 para `Secretary`; ciclo completo de cada endpoint; reasignación de un rol revocado contra la clave compuesta real; 409 por último administrador (roles, suspensión y estado de Persona); revocaciones concurrentes de los dos últimos administradores; token rechazado con 401 tras suspensión, reseteo de contraseña y cambio de roles; login local y con Google rechazado para cuenta suspendida.

## 5. Frontend

- [ ] 5.1 Crear `UserManagementService` en Angular para los cinco endpoints, con pruebas unitarias.
- [ ] 5.2 Ajustar el interceptor HTTP para tratar un 401 de sesión existente como sesión invalidada (descarta el token y redirige a login), con pruebas.
- [ ] 5.3 Integrar en `person-detail.ts` la tarjeta "Cuenta de Acceso y Roles", visible sólo para `Administrator`, con estados sin cuenta / activa / suspendida, chips de roles, botón de aprovisionamiento deshabilitado sin email o persona fallecida, y los diálogos Aprovisionar, Gestionar Roles, Restablecer Contraseña y Suspender/Reactivar (con aviso de cierre de sesión al modificarse a sí mismo y visualización de mensajes 409); pruebas de componente.
- [ ] 5.4 Agregar en la pestaña Usuarios de `admin-portal.ts` las acciones "Ver Ficha" y "Roles" por fila; pruebas de componente.

## 6. Verificación y regresión

- [ ] 6.1 Ejecutar `dotnet test` (unitarias e integración) y `ng test --watch=false`, garantizando cero regresiones y el cumplimiento de todos los escenarios de los specs del change.
