## Why

Una base de datos nueva (o recién limpiada) no tiene ningún usuario. El registro público sólo otorga el rol `Student` y los roles privilegiados se asignan desde administración, así que no existe forma de obtener el primer `Administrator` sin escribir SQL a mano. Hace falta un mecanismo reproducible para crear ese primer administrador en desarrollo y en un despliegue nuevo, sin guardar credenciales en el repositorio.

## What Changes

- **Seed del administrador inicial al arrancar la API**: un servicio idempotente crea la `Person`, el `User` (con contraseña hasheada) y el rol `Administrator` cuando no existe ningún administrador activo.
- **Credenciales por configuración**: sección `AdminSeed` (`Email`, `Password`, y opcionalmente `FirstName` / `LastName`) leída de user-secrets o variables de entorno. Sin esa configuración el seed no hace nada.
- **Sin efectos sobre cuentas existentes**: si ya hay un administrador, o el email configurado ya pertenece a otra cuenta o persona, no se crea ni se modifica nada (no cambia contraseñas ni otorga roles).

### Fuera de alcance

- Alta de otros usuarios o roles iniciales, y datos de demostración.
- Rotación o recuperación de la contraseña del administrador mediante configuración.
- Aplicar migraciones automáticamente al arrancar.

## Capabilities

### New Capabilities

Ninguna.

### Modified Capabilities

- `identity-access`: agrega el requisito de creación del administrador inicial por configuración.

## Impact

- `SmartGym.Application`: `AdminSeedOptions`, `IAdminUserSeeder` / `AdminUserSeeder`.
- `SmartGym.Infrastructure`: `AdminUserSeedHostedService` registrado como hosted service.
- `SmartGym.WebApi`: enlace de la sección de configuración `AdminSeed`.
- Sin cambios de esquema ni migraciones.
