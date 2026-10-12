## Context

`Promotions` es un módulo del monolito modular, repartido en las capas existentes: `SmartGym.Domain/Entities/Promotions`, `SmartGym.Application/Modules/Promotions`, `SmartGym.Infrastructure/Persistence/Configurations/Promotions` y `SmartGym.WebApi/Controllers/PromotionsController.cs`. Lo consumen `MembershipService` (canje al contratar, anulación al cancelar una membresía nunca activada) y `ReservationAccessResolver` / `ReservationService` (canje al confirmar una reserva, anulación al cancelarla con restitución).

Convenciones del proyecto que este cambio respeta: servicios de aplicación sobre `ISmartGymDbContext` (sin MediatR ni mapeadores), validación con FluentValidation, concurrencia optimista con `xmin`, bloqueo pesimista mediante los métodos `Lock…ForUpdateAsync` del contexto, índices únicos parciales en PostgreSQL, errores de negocio con `ConflictException.WithCode`, auditoría con `IAuditService`, pruebas unitarias con el proveedor InMemory y pruebas de integración con Testcontainers.

## Goals / Non-Goals

**Goals:**
- Garantizar los topes de uso bajo concurrencia sin cambiar el flujo de `Memberships` ni de `Reservations`.
- Cerrar las brechas de autorización y retirar el canje independiente.
- Dar a la promoción un ciclo de vida explícito, edición segura y validación de alta.
- Auditar las operaciones y unificar los códigos de error.

**Non-Goals:**
- Reemplazar `Promotion` / `PromotionRedemption` por un motor de condiciones y beneficios múltiples (JSONB, prioridad, acumulación).
- Implementar las condiciones `Referral` y `TwoForOne`, el umbral de integrantes del grupo familiar o la aplicación automática sin código.
- Publicar eventos de dominio de promociones: hoy no tienen consumidores.
- Outbox, colas o aislamiento `SERIALIZABLE`.

## Decisions

### Decision 1 – Bloqueo de la fila de la promoción para los topes

Se agrega `LockPromotionForUpdateAsync(Guid promotionId)` a `ISmartGymDbContext`, con la misma forma que los bloqueos existentes (`SELECT … FOR UPDATE` en PostgreSQL, sin efecto en InMemory). `RedeemPromotionAsync`, `RedeemPromotionForReservationAsync` y `VoidRedemptionAsync` lo toman antes de leer `CurrentUsesTotal` y de contar los canjes de la persona, y vuelven a cargar la promoción después del bloqueo.

Como todos los canjes de una misma promoción quedan serializados, el mismo bloqueo protege el tope general y el tope por persona; no hace falta un índice único por persona (que además no puede expresar topes mayores que 1).

**Transacción:** el bloqueo exige una transacción. `PromotionService` usa la de quien llama (contratación, reserva, cancelación) y, si no hay ninguna, abre y confirma la propia. Tras bloquear relee la promoción (y el canje, al anular), salvo que tenga cambios pendientes de esta misma transacción.

**Orden de bloqueo:** la promoción se bloquea siempre al final, después de las clases y las membresías, de acuerdo con el orden común definido en `reservations`. Ninguna operación que tenga bloqueada una promoción toma luego otro bloqueo.

*Alternativas descartadas:* `SERIALIZABLE` (obliga a reintentos en flujos que hoy no los tienen y no se usa en el proyecto); sólo concurrencia optimista con `xmin` (convierte la contención legítima sobre una promoción popular en errores `CONCURRENCY_CONFLICT` para el alumno).

### Decision 2 – Defensas en la base de datos

- `Promotion.Version` (`xmin`, `IsRowVersion`) para la edición administrativa.
- `CHECK ("CurrentUsesTotal" >= 0 AND ("MaxUsesTotal" IS NULL OR "CurrentUsesTotal" <= "MaxUsesTotal"))` como última barrera si algún camino omitiera el bloqueo.
- Índice único parcial `UX_PromotionRedemptions_Membership` sobre `MembershipId` con filtro `"MembershipId" IS NOT NULL AND "IsActive"`, análogo al existente `UX_PromotionRedemptions_Reservation`.

### Decision 3 – `PromotionStatus` separado de `IsActive`

| Valor | Estado | Aplicable | Editable |
|---|---|---|---|
| 1 | `Active` | Sí, dentro de la vigencia | Sí |
| 2 | `Paused` | No | Sí |
| 3 | `Archived` | No | No (terminal) |

`Status` es la fuente de verdad para la aplicabilidad. `IsActive` conserva su significado de `BaseEntity` y queda en `true` para todas las promociones: una promoción archivada debe seguir siendo consultable. `Promotion.IsValidOn` pasa a exigir `Status == Active`. Las transiciones se implementan como métodos de dominio (`Pause`, `Activate`, `Archive`) que rechazan las inválidas.

No se agrega un estado `Draft`: una promoción con `ValidFrom` futura cumple esa función.

### Decision 4 – Campos bloqueados tras el primer canje

Con al menos un `PromotionRedemption` (vigente o anulado), sólo son editables `Name`, `Description`, `ValidTo`, `MaxUsesTotal` y `MaxUsesPerPerson`. Así el significado de los canjes ya registrados no cambia retroactivamente. `MaxUsesTotal` no puede quedar por debajo de `CurrentUsesTotal`; `MaxUsesPerPerson` no puede quedar por debajo del máximo de canjes vigentes de una misma persona.

### Decision 5 – Superficie de la API

| Ruta | Antes | Después |
|---|---|---|
| `GET /api/promotions` | `RequireStaff` | `PROMOTIONS_READ`; filtro `status` |
| `GET /api/promotions/{id}` | `RequireStaff` | `PROMOTIONS_READ` |
| `GET /api/promotions/code/{code}` | autenticado | `PROMOTIONS_READ` |
| `POST /api/promotions` | `RequireStaff` | `PROMOTIONS_MANAGE` |
| `PUT /api/promotions/{id}` | — | `PROMOTIONS_MANAGE`; `Version` en el cuerpo |
| `POST /api/promotions/{id}/pause` · `/activate` · `/archive` | — | `PROMOTIONS_MANAGE` |
| `GET /api/promotions/{id}/redemptions` | — | `PROMOTIONS_REDEMPTIONS_READ`; paginado; incluye anulados |
| `GET /api/promotions/redemptions/person/{personId}` | autenticado | `PROMOTIONS_REDEMPTIONS_READ` o titular |
| `POST /api/promotions/evaluate` | autenticado; `BasePrice` del cliente | autenticado; alumno sólo para sí; precio del plan |
| `POST /api/promotions/redeem` | autenticado | **eliminada** |

Los tres permisos se asignan a `Administrator` y `Secretary`, igual que el `RequireStaff` actual. La verificación "titular o staff" se hace en el servicio con `ICurrentUserService`, como en `Reservations`.

`ApplyPromotionRequest` se conserva como contrato interno entre `MembershipService` y `PromotionService` (con el precio que `MembershipService` lee del plan). El endpoint público usa un request nuevo, `EvaluatePromotionRequest(Code, PersonId, MembershipPlanId)`.

### Decision 6 – Condiciones soportadas

Se consideran verificables `NewStudent`, `FamilyGroup` y `CouponCode` (sin condición adicional al código). `Referral` y `TwoForOne` se rechazan en el validador de alta y, para filas ya existentes, en la evaluación con `PROMOTION_NOT_APPLICABLE`. La lista vive en un único lugar (`PromotionService`) para que el cambio posterior que implemente esas condiciones sólo tenga que ampliarla.

De paso se extrae a un método privado la lógica común que hoy está duplicada entre `EvaluatePromotionAsync` y `EvaluatePromotionForReservationAsync` (búsqueda por código, vigencia, condición, tope por persona).

### Decision 7 – Errores con código

`PromotionService` deja de lanzar `ArgumentException` / `InvalidOperationException` y usa `NotFoundException`, `ValidationException`, `ForbiddenException` y `ConflictException.WithCode` con las constantes de `PromotionErrorCodes`. `MembershipService` deja de capturar y traducir esas excepciones genéricas. En la evaluación y el canje, un código inexistente, pausado, archivado, fuera de vigencia o con condición no verificable devuelve `PROMOTION_NOT_APPLICABLE`, y un tope agotado `PROMOTION_LIMIT_REACHED`; `PROMOTION_NOT_FOUND` queda para las operaciones por identificador. Editar una promoción archivada devuelve `PROMOTION_LOCKED_FIELDS`. No hay manejo global de errores en `WebApi`: `PromotionsController` traduce estas excepciones a respuestas HTTP con el mismo patrón que `ReservationsController` (404, 400, 403 y 409 con `code`).

### Decision 8 – Auditoría

Se registra un `AuditLog` para `PROMOTION_CREATED`, `PROMOTION_UPDATED` (valores anteriores y nuevos en JSON), `PROMOTION_PAUSED`, `PROMOTION_ACTIVATED`, `PROMOTION_ARCHIVED`, `PROMOTION_REDEEMED` y `PROMOTION_REDEMPTION_VOIDED`, con `entityName = "Promotion"` o `"PromotionRedemption"`. No se usa `IAuditService.LogAsync` porque guarda por su cuenta: dentro de la transacción de una reserva adelantaría el guardado de quien llama. El registro se agrega al contexto y se persiste en el mismo guardado (y transacción) que la operación auditada. El canje y la anulación se auditan dentro de `PromotionService`, de modo que los flujos de `Memberships` y `Reservations` quedan cubiertos sin tocarlos.

## Migration Plan

Migración `HardenPromotionsModule`:

1. Agregar `Promotions.Status` (`integer NOT NULL DEFAULT 1`).
2. Datos: `UPDATE "Promotions" SET "Status" = 2, "IsActive" = TRUE WHERE "IsActive" = FALSE`. Las promociones hoy desactivadas pasan a `Paused`, que es reversible; el personal decide luego cuáles archivar.
3. Datos: si una membresía tuviera más de un canje vigente, conservar el que la membresía referencia (`PromotionRedemptionId`) o, en su defecto, el más reciente, y desactivar el resto. Va antes del recálculo porque cambia la cantidad de canjes vigentes.
4. Datos: recalcular `CurrentUsesTotal` como la cantidad de canjes con `IsActive = TRUE` de cada promoción, y elevar `MaxUsesTotal` a ese valor donde quede por debajo, para que el `CHECK` pueda crearse.
5. Crear los `CHECK` de `Status` y de `CurrentUsesTotal` y el índice `UX_PromotionRedemptions_Membership` (reemplaza al índice no único `IX_PromotionRedemptions_MembershipId`).
6. `Down`: eliminar índice y `CHECK`, restaurar `IsActive = FALSE` donde `Status <> 1` y eliminar `Status`.

`xmin` no requiere columna nueva.

## Risks / Trade-offs

- **Contención en promociones masivas**: todos los canjes de una promoción se serializan. El bloqueo se toma al final de la transacción y dura lo que el `INSERT` del canje; con el volumen de un gimnasio es despreciable.
- **Ruptura de contrato**: se elimina `/redeem` y cambian los permisos de dos consultas. `MembershipService` y `ReservationAccessResolver` usan el servicio, no la API; hay que revisar el frontend (tarea 8.1).
- **Promociones heredadas `Referral` / `TwoForOne`**: dejan de concederse. Es el comportamiento correcto, pero conviene avisar al personal si existen filas así en producción.
- **Paso 3 de la migración**: corrige contadores que la falta de bloqueo pudo haber desviado; si eleva algún `MaxUsesTotal`, queda registrado en la salida de la migración.

## Open Questions

- ¿`PROMOTIONS_MANAGE` debe quedar sólo para `Administrator`? El diseño mantiene a `Secretary` porque hoy puede crear promociones.
