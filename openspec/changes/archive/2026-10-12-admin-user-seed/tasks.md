Las rutas son relativas a `backend/src/` salvo indicación; `tests/...` abrevia `backend/tests/SmartGym.Domain.UnitTests`. Las tareas marcadas 🔴 escriben primero la prueba que falla.

## 1. Seeder del administrador inicial

- [x] 1.1 🔴 Crear `tests/.../Identity/AdminUserSeederTests.cs` (EF InMemory, `PasswordHasher` real) cubriendo: creación con rol `Administrator`, hash verificable y `IsEmailConfirmed`; `NotConfigured` sin tocar la base; `InvalidConfiguration` (email inválido, contraseña corta); `AdministratorAlreadyExists` sin modificar la cuenta existente; `EmailAlreadyInUse` sin otorgar el rol; idempotencia en una segunda ejecución (Decision 3).
- [x] 1.2 Crear `SmartGym.Application/Common/AdminSeedOptions.cs` y `SmartGym.Application/Modules/Identity/Services/AdminUserSeeder.cs` (`IAdminUserSeeder`, `AdminSeedOutcome`); registrarlo en `SmartGym.Application/DependencyInjection.cs`.

## 2. Ejecución al arrancar

- [x] 2.1 🔴 Crear `tests/.../Identity/AdminUserSeedHostedServiceTests.cs`: `StartAsync` invoca al seeder una vez y no propaga sus excepciones (Decision 4).
- [x] 2.2 Crear `SmartGym.Infrastructure/Services/Auth/AdminUserSeedHostedService.cs`, registrarlo en `SmartGym.Infrastructure/DependencyInjection.cs` y enlazar la sección `AdminSeed` en `SmartGym.WebApi/Program.cs`.

## 3. Verificación

- [x] 3.1 Ejecutar `dotnet test backend/tests/SmartGym.Domain.UnitTests` y comprobar que `dotnet ef migrations has-pending-model-changes` no informa cambios.
