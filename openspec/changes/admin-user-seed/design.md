## Context

`AuthService.RegisterAsync` crea `Person` + `User` y asigna sólo `Student`. Los roles están sembrados con `HasData` en `RoleConfiguration`. La API no ejecuta migraciones ni seeds al arrancar; los procesos de fondo existentes son `BackgroundService` registrados en `SmartGym.Infrastructure/DependencyInjection.cs`.

## Decisions

### Decision 1 – Seed al arrancar, no migración ni comando manual
Un `IHostedService` (`AdminUserSeedHostedService`) ejecuta `IAdminUserSeeder.SeedAsync` una vez en `StartAsync`. Una migración obligaría a versionar el hash de la contraseña y sería igual en todos los entornos; un comando manual hay que recordarlo en cada base nueva.

### Decision 2 – Credenciales sólo por configuración
`AdminSeedOptions` (sección `AdminSeed`): `Email`, `Password`, `FirstName` (por defecto `"Administrador"`), `LastName` (por defecto `"SmartGym"`). Se configuran con user-secrets o variables de entorno (`AdminSeed__Email`, `AdminSeed__Password`); no se agregan valores a `appsettings*.json`. La contraseña nunca se escribe en logs.

### Decision 3 – Reglas del seeder (en este orden)
1. Si `Email` o `Password` están vacíos → `NotConfigured`, sin consultar la base.
2. Si `Email` no es una dirección válida o `Password` tiene menos de 12 caracteres → `InvalidConfiguration` (se registra un error; la API arranca igual).
3. Si existe alguna `Person` con el rol `Administrator` activo → `AdministratorAlreadyExists`.
4. Si existe un `User` con ese `Username` o una `Person` con ese `Email` → `EmailAlreadyInUse` (no se promueve una cuenta existente: quien controla la configuración no debe poder apropiarse de la ficha de otra persona).
5. En otro caso crea `Person`, `User` (`Username` = email normalizado en minúsculas, `PasswordHash` con `IPasswordHasher`, `IsEmailConfirmed = true`) y asigna `Administrator` → `Created`.

### Decision 4 – Un fallo del seed no impide arrancar
`AdminUserSeedHostedService` captura cualquier excepción (base inaccesible, esquema sin migrar, carrera entre dos instancias resuelta por el índice único de `Username`) y la registra como error. La API sigue funcionando para el resto de los usuarios.

## Risks / Trade-offs

- **La contraseña vive en la configuración del entorno** → es la misma superficie que la cadena de conexión y el secreto JWT; se recomienda quitar `AdminSeed:Password` una vez creado el administrador. Como el seed no actúa si ya hay un administrador, dejarla no permite resetear ni crear cuentas.
- **Dos instancias arrancando a la vez** → la segunda falla por el índice único de `Username` y lo registra; no quedan dos administradores.
