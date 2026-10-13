Las rutas son relativas a `backend/src/` salvo indicación; `tests/...` abrevia `backend/tests/SmartGym.Domain.UnitTests`. Las tareas marcadas 🔴 escriben primero la prueba que falla (TDD); la tarea siguiente la pone en verde. "Decision N" refiere a `design.md`.

## 1. Dominio: estados y edición

- [x] 1.1 🔴 Ampliar `tests/.../Promotions/PromotionTests.cs`: transiciones válidas e inválidas de Decision 3, `IsValidOn` falso para `Paused` y `Archived`, `Update` con y sin canjes (campos bloqueados de Decision 4) y rechazo de topes por debajo de lo consumido.
- [x] 1.2 Crear `SmartGym.Domain/Enums/PromotionStatus.cs` (`Active = 1`, `Paused = 2`, `Archived = 3`) y agregar a `Promotion.cs` `Status`, `Version` y los métodos `Pause`, `Activate`, `Archive` y `Update` con `nowUtc` por parámetro; `IsValidOn` exige `Status == Active`.

## 2. Persistencia y migración

- [x] 2.1 🔴 Crear `tests/.../Promotions/PromotionModelConfigurationTests.cs` (patrón `UseNpgsql` sin conexión) verificando `Status`, `xmin`, el `CHECK` de `CurrentUsesTotal` y el filtro del índice `UX_PromotionRedemptions_Membership`.
- [x] 2.2 Actualizar `PromotionConfiguration.cs` y `PromotionRedemptionConfiguration.cs` según Decision 2.
- [x] 2.3 Agregar `LockPromotionForUpdateAsync` a `ISmartGymDbContext` y `SmartGymDbContext`, con la misma forma que `LockMembershipForUpdateAsync`.
- [x] 2.4 Generar la migración `HardenPromotionsModule` y completar a mano los pasos de datos del "Migration Plan" y el `Down`.
- [x] 2.5 🔴→🟢 Crear `backend/tests/SmartGym.IntegrationTests/Promotions/HardenPromotionsMigrationIntegrationTests.cs`: sembrar con el esquema anterior una promoción desactivada, una con contador desviado y una membresía con dos canjes vigentes; aplicar la migración y verificar cada paso.
- [x] 2.6 Actualizar los datos de demostración si el seed crea promociones.

## 3. Errores con código y refactor de la evaluación

- [x] 3.1 🔴 Ampliar `tests/.../Promotions/PromotionRedemptionLifecycleTests.cs`: cada rechazo de evaluación y canje devuelve el código de `PromotionErrorCodes` esperado; una promoción `Referral` o `TwoForOne` existente se rechaza con `PROMOTION_NOT_APPLICABLE`; una promoción `Paused` o `Archived` no es aplicable.
- [x] 3.2 Crear `Modules/Promotions/PromotionErrorCodes.cs`, extraer la lógica común de `EvaluatePromotionAsync` y `EvaluatePromotionForReservationAsync` a un método privado (Decision 6) y reemplazar `ArgumentException` / `InvalidOperationException` por las excepciones de Decision 7.
- [x] 3.3 Quitar de `MembershipService` la traducción de `ArgumentException` / `InvalidOperationException` del canje y dejar en verde `tests/.../Memberships/MembershipPromotionIntegrationTests.cs` y `tests/.../Reservations/ReservationAccessResolutionTests.cs`.

## 4. Topes bajo concurrencia

- [x] 4.1 🔴 Crear `backend/tests/SmartGym.IntegrationTests/Promotions/PromotionConcurrencyIntegrationTests.cs` (patrón de `ReservationConcurrencyIntegrationTests`): último uso general disputado por dos alumnos; tope por persona disputado por el mismo alumno; canje y anulación simultáneos dejan `CurrentUsesTotal` igual a la cantidad de canjes vigentes.
- [x] 4.2 Tomar `LockPromotionForUpdateAsync` y recargar la promoción en `RedeemPromotionAsync`, `RedeemPromotionForReservationAsync` y `VoidRedemptionAsync`; rechazar con `PROMOTION_LIMIT_REACHED` (Decision 1).
- [x] 4.3 🔴→🟢 Tests de integración del `CHECK` de `CurrentUsesTotal` y del índice `UX_PromotionRedemptions_Membership`; test unitario de que `VoidRedemptionAsync` repetido no decrementa dos veces.

## 5. Administración

- [x] 5.1 🔴 Crear `tests/.../Promotions/PromotionValidatorTests.cs` con `FluentValidation.TestHelper`: cada regla del requisito "Administración y validación de promociones" y el rechazo de `Referral` / `TwoForOne` en el alta.
- [x] 5.2 Crear `CreatePromotionRequestValidator` y `UpdatePromotionRequestValidator` en `Modules/Promotions/Validators/` y registrarlos.
- [x] 5.3 🔴 Crear `tests/.../Promotions/PromotionAdministrationTests.cs`: alta en `Active`, código duplicado sin distinguir mayúsculas, tope por persona 0 rechazado, plan inexistente, edición con campos bloqueados, extensión de vigencia, reducción de tope por debajo de lo consumido, versión desactualizada, edición de archivada, pausar / activar / archivar y transición inválida.
- [x] 5.4 Agregar a `IPromotionService` / `PromotionService` `UpdateAsync`, `PauseAsync`, `ActivateAsync` y `ArchiveAsync`; ajustar `CreateAsync` y `GetAllAsync` (filtro por `Status`); sumar `Status` y `Version` a `PromotionDto` y crear `UpdatePromotionRequest`.
- [x] 5.5 🔴→🟢 Agregar `GetRedemptionsByPromotionAsync` (paginado, incluye anulados, proyección en SQL) y el campo de estado del canje en `PromotionRedemptionDto`, con pruebas.

## 6. Autorización y API

- [x] 6.1 Agregar `PromotionsRead`, `PromotionsManage` y `PromotionsRedemptionsRead` a `Permissions` y `Policies` en `Common/Security/RolesAndPolicies.cs` y registrar las políticas en `SmartGym.Infrastructure/DependencyInjection.cs` (Decision 5).
- [x] 6.2 🔴 Ampliar `tests/.../Promotions/PromotionRedemptionLifecycleTests.cs`: la evaluación previa no modifica contadores y usa el precio del plan; un alumno no puede evaluar para otra persona ni leer canjes ajenos; el titular lee los suyos.
- [x] 6.3 Crear `EvaluatePromotionRequest`, implementar `PreviewForMembershipAsync` y la verificación "titular o staff" con `ICurrentUserService` en `PromotionService`.
- [x] 6.4 🔴 Crear `tests/.../Promotions/PromotionsControllerTests.cs` (patrón de `ReservationsControllerTests`): política de cada acción de la tabla de Decision 5, código HTTP y `code` de cada error, y ausencia de la acción `Redeem`.
- [x] 6.5 Reescribir `SmartGym.WebApi/Controllers/PromotionsController.cs`: rutas nuevas, políticas por permiso, eliminación de `POST /redeem` y traducción de excepciones con el patrón de `ReservationsController` (Decision 7).

## 7. Auditoría

- [x] 7.1 🔴 Crear `tests/.../Promotions/PromotionAuditTests.cs`: un registro por alta, edición (valores anteriores y nuevos), pausa, activación, archivo, canje y anulación, con el usuario operador.
- [x] 7.2 Inyectar `ICurrentUserService` en `PromotionService` y registrar las acciones de Decision 8 en el mismo guardado que la operación.

## 8. Frontend y cierre

- [x] 8.1 Revisar `frontend/src/app` (`gym-api.service.ts`, `gym.models.ts`, pestaña de promociones de `admin-portal.ts`) y adaptar los usos de `/redeem`, `/evaluate`, `/code/{code}` y del modelo de promoción (`status`, `version`).
- [x] 8.2 Ejecutar `dotnet test backend/SmartGym.slnx` (unitarias e integración) y `openspec validate promotions-module --strict`; verificar cada escenario del delta de `promotions-family` contra una prueba.
