## Why

El módulo de promociones ya existe (`Promotion`, `PromotionRedemption`, `PromotionService`, `PromotionsController`) y está integrado con `Memberships` y `Reservations`, pero conserva brechas de integridad, seguridad y administración propias de un prototipo:

- **Topes de uso sin protección de concurrencia**: `Promotion` no tiene token de concurrencia y `IncrementUse()` es leer y luego escribir; el tope por persona es un `COUNT` seguido de un `INSERT`. Dos canjes simultáneos pueden superar `MaxUsesTotal` o `MaxUsesPerPerson`.
- **Canje expuesto por API con datos del cliente**: `POST /api/promotions/redeem` permite a cualquier usuario autenticado canjear para un `PersonId` arbitrario con un `BasePrice` enviado por el cliente, sin membresía ni reserva que lo respalde y sin idempotencia.
- **Brechas de autorización en consultas**: `POST /evaluate`, `GET /code/{code}` y `GET /redemptions/person/{personId}` sólo exigen estar autenticado; un alumno puede ver los canjes de otro y los contadores internos de cualquier promoción.
- **Beneficios concedidos sin verificar la condición**: sólo se evalúan `NewStudent` y `FamilyGroup`. Una promoción `Referral` o `TwoForOne` se concede a cualquiera.
- **Sin ciclo de vida ni administración**: no se puede editar, pausar ni archivar una promoción; `IsActive` mezcla "pausada" y "eliminada"; no hay validación de alta (un porcentaje de 150 % o un `MaxUsesPerPerson` de 0 se aceptan o se corrigen en silencio), ni listado de canjes por promoción, ni auditoría.
- **Errores sin código estable**: el servicio lanza `ArgumentException` / `InvalidOperationException` que cada consumidor traduce por su cuenta.

Este cambio endurece y completa el módulo existente. No lo reemplaza: se conservan las entidades, las tablas y los contratos internos que ya consumen `Memberships` y `Reservations`.

## What Changes

- **Integridad de topes bajo concurrencia**: todo canje y toda anulación bloquean la fila de la promoción (`LockPromotionForUpdateAsync`, último en el orden de bloqueo común) antes de verificar y actualizar los topes; `Promotion` suma `Version` (`xmin`); restricción `CHECK` sobre `CurrentUsesTotal` e índice único parcial de un canje vigente por membresía.
- **BREAKING – Se elimina `POST /api/promotions/redeem`**: el canje sólo ocurre dentro de la contratación de una membresía o de la confirmación de una reserva, que ya son idempotentes.
- **BREAKING – `POST /api/promotions/evaluate` pasa a ser una vista previa**: el precio base se toma del plan en el servidor (se retira `BasePrice` del request); un alumno sólo puede evaluar para sí mismo.
- **BREAKING – Autorización**: `GET /code/{code}` queda restringido a staff; `GET /redemptions/person/{personId}` a staff o al titular. Nuevos permisos `PROMOTIONS_READ`, `PROMOTIONS_MANAGE` y `PROMOTIONS_REDEMPTIONS_READ`.
- **Ciclo de vida**: nuevo `PromotionStatus` (`Active`, `Paused`, `Archived`), con `POST /{id}/pause`, `POST /{id}/activate` y `POST /{id}/archive`. `Archived` es terminal. Sólo una promoción `Active` es aplicable; los canjes ya registrados no se ven afectados.
- **Edición (`PUT /api/promotions/{id}`)** con concurrencia optimista: una promoción con canjes sólo admite cambios en nombre, descripción, vigencia final y topes (nunca por debajo de lo ya consumido).
- **Validación de alta y edición** con FluentValidation, y rechazo de tipos de condición que el sistema todavía no sabe verificar (`Referral`, `TwoForOne`), tanto al crear como al evaluar.
- **Consulta de canjes por promoción** (`GET /api/promotions/{id}/redemptions`, paginada, incluye anulados).
- **Auditoría** de alta, edición, cambios de estado, canje y anulación, en la misma transacción que la operación.
- **Errores con código estable** (`PROMOTION_NOT_FOUND`, `PROMOTION_NOT_APPLICABLE`, `PROMOTION_LIMIT_REACHED`, `PROMOTION_CODE_TAKEN`, `PROMOTION_INVALID_TRANSITION`, `PROMOTION_LOCKED_FIELDS`, `CONCURRENCY_CONFLICT`).

### Fuera de alcance

Quedan para un cambio posterior, porque requieren decisiones de negocio y de modelo que este cambio no necesita: condiciones múltiples por promoción (plan + actividad + horario), condición de referido, beneficio 2x1 y segundo alumno gratis, umbral de integrantes del grupo familiar, aplicación automática sin código, prioridad y acumulación de promociones. Los escenarios del requisito "Motor de Condiciones y Beneficios" que dependen de ellos siguen sin cumplirse y este cambio no los modifica.

## Capabilities

### New Capabilities
- *(ninguna)*

### Modified Capabilities
- `promotions-family`: el registro de uso garantiza los topes bajo concurrencia y sólo se origina en una membresía o una reserva; se agregan el ciclo de vida de la promoción, la administración con validación, la autorización contextual de consultas y evaluación, el rechazo de condiciones no verificables y la auditoría.

## Impact

- **Domain**: `Promotion` (`Status`, `Version`, métodos `Pause`, `Activate`, `Archive`, `Update`); nuevo enum `PromotionStatus`.
- **Application**: `IPromotionService` / `PromotionService` (bloqueo, estados, edición, canjes por promoción, errores con código, auditoría); `PromotionDtos`; validadores `CreatePromotionRequestValidator` y `UpdatePromotionRequestValidator`; `PromotionErrorCodes`; `LockPromotionForUpdateAsync` en `ISmartGymDbContext`; permisos en `RolesAndPolicies`. `MembershipService` y `ReservationAccessResolver` ajustan la traducción de errores; su flujo no cambia.
- **Infrastructure**: `PromotionConfiguration`, `PromotionRedemptionConfiguration`, migración `HardenPromotionsModule` (columna `Status` con datos migrados desde `IsActive`, `CHECK`, índice único parcial), políticas en `DependencyInjection`.
- **WebApi**: `PromotionsController` (rutas nuevas, eliminación de `/redeem`, políticas por permiso).
- **Frontend (Angular)**: pestaña de promociones de `admin-portal` y `gym-api.service.ts` si consumen las rutas modificadas.
- **Tests**: ampliación de `SmartGym.Domain.UnitTests/Promotions` y nuevos tests de concurrencia y migración en `SmartGym.IntegrationTests/Promotions`.
