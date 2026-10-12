## Why

La implementación actual de `Membership` (`Domain/Entities/Memberships/Membership.cs`, `MembershipService`, `MembershipsController`) es un prototipo inicial acoplado que no cubre las necesidades operativas ni de auditoría comercial de un gimnasio real:

- **Ausencia de trazabilidad comercial (snapshot histórico)**: `Membership` no almacena `PurchasedAtUtc`, `PurchasePrice`, `Currency`, `OriginalPrice`, `DiscountAmount`, `PromotionId`, `PromotionRedemptionId`, `IsUnlimitedSnapshot` ni las actividades permitidas o condiciones pactadas al momento de contratar. Si un `MembershipPlan` cambia su precio, duración o modalidad, se pierde la referencia histórica de lo contratado.
- **Acoplamiento con el libro de créditos (`MembershipCredits`)**: `MembershipService` y métodos directos en `Membership` manipulan `AvailableCredits` en línea e instancian `MembershipCreditMovement` directamente (incluso ajustando a 0 en `CancelMembershipAsync`), violando la separación de responsabilidades donde `MembershipCredits` (`ICreditLedgerService`) debe ser el único propietario de los movimientos y del saldo proyectado.
- **Máquina de estados incompleta y sin trazabilidad**: `MembershipStatus` carece del estado `Pending` (necesario cuando la activación depende de la confirmación de pago o inicio diferido), mezcla el agotamiento de saldo (`Depleted`) con el ciclo de vida de vigencia, no modela suspensiones temporales con ventana y motivo (`MembershipSuspension`), ni registra quién, cuándo y por qué ejecutó cada transición de estado (`MembershipStatusChange`).
- **Falta de integración formal con Pagos, Promociones y Elegibilidad**: `AssignMembershipAsync` activa la membresía inmediatamente sin coordinar con `Promotions` ni `Payments`, no posee protección de idempotencia ante reintentos HTTP, carece de control de concurrencia optimista (`xmin`), no expone un servicio/endpoint de elegibilidad sin consumo, ni cuenta con un proceso periódico idempotente para transicionar vencimientos y emitir alertas a `Notifications`.

Este cambio introduce la especificación y el diseño integral del módulo **Memberships** (`memberships`), delimitando sus responsabilidades y contratos dentro del monolito modular.

## What Changes

- **BREAKING – Modelo de dominio `Membership` e historial comercial**:
  - Se incorporan atributos de contratación y snapshot comercial inmutable: `PurchasedAtUtc`, `InitialCredits`, `IsUnlimited`, `AppliesToAllActivities`, `PurchasePrice`, `OriginalPrice`, `DiscountAmount`, `Currency` (ISO 4217, por defecto `ARS`), `PromotionId`, `PromotionRedemptionId`, `RenewedFromMembershipId`, `CreatedByUserId` y `Notes`.
  - Se preserva el snapshot de actividades permitidas al contratar (`MembershipAllowedActivities`) para aislar membresías vigentes de cambios futuros en `MembershipPlan`.
  - Concurrencia optimista mediante `xmin` (`Version`) y auditoría de cada transición administrativa en `MembershipStatusChanges` y `MembershipSuspensions`.
- **BREAKING – Máquina de estados explícita y separación de saldo**:
  - Estados persistidos del ciclo de vida: `Pending` (`PENDING`), `Active` (`ACTIVE`), `Suspended` (`SUSPENDED`), `Expired` (`EXPIRED`) y `Cancelled` (`CANCELLED`).
  - Se retira `Depleted` como estado de vigencia: quedarse sin créditos disponibles es una condición del saldo (`AvailableCredits == 0 && !IsUnlimited`) informada en el resumen de créditos y en la elegibilidad, sin alterar el estado `Active` de la membresía dentro de su vigencia ni requerir mutaciones artificiales de estado al consumir o devolver créditos.
  - Estado efectivo en consultas y autorizaciones: una membresía `Active` cuyo `EndDate < Today` (en la zona horaria del gimnasio `IGymCalendar`) se evalúa como vencida (`EXPIRED`) inmediatamente aunque el worker periódico aún no haya persistido la transición.
- **BREAKING – Separación estricta entre `Memberships` y `MembershipCredits`**:
  - `Memberships` deja de insertar registros en `MembershipCreditMovements` y de mutar `AvailableCredits` por su cuenta. Todas las cargas iniciales, bonificaciones por promoción, consumos, devoluciones y caducidades por cancelación/vencimiento se ejecutan a través de operaciones de `ICreditLedgerService` dentro de la misma transacción.
- **BREAKING – Rediseño de rutas y contratos API (`/api/memberships`)**:
  - `POST /api/memberships/assign` se reemplaza por `POST /api/memberships` (con soporte de `IdempotencyKey`, código de promoción opcional, modalidad de pago/activación y notas) y `POST /api/memberships/{id}/renew`.
  - Se agregan `GET /api/memberships` (listado paginado y filtrado en servidor), `GET /api/memberships/{id}` (detalle completo), `GET /api/memberships/student/{studentId}/current` (convive con `/active`, que se mantiene por compatibilidad con el frontend actual y queda marcada como obsoleta), `PATCH /api/memberships/{id}/notes`, `GET /api/memberships/{id}/eligibility`, `GET /api/memberships/{id}/credits`, `POST /api/memberships/{id}/activate`, `POST /api/memberships/{id}/suspend` y `POST /api/memberships/{id}/reactivate`.
- **Política de renovaciones, suspensiones y cancelaciones**:
  - Cada renovación crea un nuevo registro identificable de `Membership` vinculado mediante `RenewedFromMembershipId`, soportando renovación anticipada (inicio al día siguiente del vencimiento actual) o desde vencimiento, validando reglas de solapamiento.
  - Las suspensiones comienzan el día en que se registran, admiten fecha de fin (con reactivación automática) y registran motivo, usuario responsable y política explícita sobre la fecha de vencimiento: sin extensión por defecto; con extensión pactada la membresía no vence mientras está suspendida y recupera todos los días suspendidos al reactivarse. Al suspender se cancelan con devolución las reservas que caen dentro de la ventana.
  - La regla de solapamiento considera también los créditos aún no acreditados de una membresía `Pending`, y cuando un alumno tiene varias membresías utilizables la reserva elige de forma determinista (ilimitadas primero, luego la que vence antes) o usa la que se indique.
  - Las membresías `Pending` conservan la vigencia fijada al contratar y se cancelan automáticamente si siguen impagas tras el plazo de gracia (7 días por defecto), anulando el canje de promoción.
  - Activar una membresía con saldo pendiente queda reservado al `Administrator` y exige motivo.
  - La cancelación exige motivo, invalida el uso futuro, coordina con `Reservations` la cancelación de reservas futuras vinculadas a esa membresía y con `MembershipCredits` el cierre del saldo remanente, preservando intacto el historial comercial y financiero.
- **Servicio de elegibilidad y acceso (`IMembershipEligibilityService`)**:
  - Evalúa en el backend (sin consumir créditos) si un alumno y/o membresía está habilitado para una actividad y fecha/clase determinada, comprobando: estado de la persona en `People`, estado y vigencia de la membresía, ausencia de suspensión vigente, actividad permitida por el snapshot del plan, saldo de créditos utilizables y requisitos operativos (edad, apto médico cuando aplique).
- **Vencimientos y avisos programados (`MembershipExpirationWorker`)**:
  - Proceso periódico idempotente (`BackgroundService`) que reactiva suspensiones finalizadas, transiciona a `Expired` las membresías vencidas, expira los créditos remanentes en el ledger, cancela las `Pending` impagas y genera notificaciones in-app (`MembershipExpiring`, `MembershipExpired`) sin duplicar envíos ni avisar a pases cortos o membresías ya renovadas.
- **Ajustes mínimos en módulos vecinos**:
  - `Payments`: un pago puede registrarse `Pending` y luego confirmarse o rechazarse (`POST /api/payments/{id}/confirm`, `/fail`); al completarse emite un evento que activa la membresía. Los estados documentados pasan a ser los del código (`Pending`, `Completed`, `Failed`, `Refunded`): "ANULADO" equivale a `Refunded` y "PARCIAL" deja de ser un estado y se deriva del saldo pendiente.
  - `Promotions`: vinculación y anulación de canjes, y vigencia evaluada con la fecha local del gimnasio.

## Capabilities

### New Capabilities
- `memberships`: gestión integral del ciclo de vida de membresías contratadas por alumnos, incluyendo contratación atómica con snapshot comercial e idempotencia, máquina de estados (`PENDING`, `ACTIVE`, `SUSPENDED`, `EXPIRED`, `CANCELLED`), renovaciones trazables, suspensiones temporales, cancelaciones coordinadas, evaluación de elegibilidad sin consumo, integración con `MembershipPlans`, `MembershipCredits`, `Promotions`, `Payments` y `Reservations`, autorización contextual, auditoría y procesamiento periódico de vencimientos.

### Modified Capabilities
- `memberships-credits`: delimita la frontera entre la definición comercial (`MembershipPlans`), la contratación (`Memberships`, movida a `memberships`) y el libro mayor de créditos (`MembershipCredits`), estableciendo que toda modificación de saldo y registro de movimientos (`InitialPurchase`, `PromotionBonus`, `Expiration`, `CancellationForfeiture`, consumos y devoluciones) se centraliza exclusivamente en `MembershipCredits`.
- `reservations-attendance`: actualiza la validación de acceso al reservar para utilizar la evaluación de elegibilidad de `Memberships` (considerando el snapshot de actividades contratadas, estado `ACTIVE`, vigencia local y ausencia de suspensión) sin depender del estado retirado `Depleted`.
- `operations-reporting`: precisa la vinculación de múltiples pagos/pagos parciales con una membresía para determinar su activación conforme a la política de cobro, alinea los estados de pago con los implementados e incorpora la confirmación y el rechazo de pagos pendientes, y la emisión idempotente de notificaciones de vencimiento próximo y cambios de estado de membresía.
- `promotions-family`: el canje queda vinculado a la membresía y se anula si ésta se cancela sin haberse activado; la vigencia se evalúa con la fecha local del gimnasio.

## Impact

- **Domain**:
  - Refactorización de `Membership` y `MembershipStatus` (`Pending`, `Active`, `Suspended`, `Expired`, `Cancelled`).
  - Nuevas entidades de trazabilidad en el módulo `Memberships`: `MembershipAllowedActivity`, `MembershipStatusChange`, `MembershipSuspension`.
  - Nuevos valores en `CreditMovementType` (`PromotionBonus`, `CancellationForfeiture`, `Expiration`) y `NotificationType` (`MembershipActivated`, `MembershipSuspended`, `MembershipExpired`).
  - Eventos de dominio in-process para coordinar efectos entre módulos (`MembershipCancelled`, `MembershipActivated`, `MembershipSuspended`, `MembershipExpired`, `PaymentCompleted`).
- **Application**:
  - Rediseño de `IMembershipService` / `MembershipService` y sus DTOs, más validación de solicitudes y elegibilidad.
  - Ampliación de `ICreditLedgerService` / `CreditLedgerService` con operaciones transaccionales para carga inicial, bonificación promocional, cierre por cancelación/vencimiento y desglose de saldo (`MembershipCreditsSummaryDto`).
  - Ajuste de `ReservationService`, `DashboardService`, `PaymentService` y `PromotionService` para operar con la nueva máquina de estados y contratos de `Memberships`.
  - `IMembershipActivationService` como único punto de activación, sin dependencia circular entre `MembershipService` y `PaymentService`.
  - Nuevas opciones `MembershipsOptions` (`ExpirationCheckInterval`, `ExpiringWarningDays`, `PendingGraceDays`, `MaxSuspensionDays`), `GymOptions.DefaultCurrency` y `GymOptions.RequireValidMedicalCertificateForAccess`.
- **Infrastructure**:
  - Configuración EF Core (`MembershipConfiguration` y tablas asociadas), restricciones `CHECK`, índices compuestos por alumno/estado/vigencia/idempotencia, token de concurrencia `xmin` (`Version`), migración de base de datos y `MembershipExpirationWorker` (`BackgroundService`).
- **WebApi**:
  - Reescritura de `MembershipsController` con rutas RESTful estándar, códigos de estado semánticos (`201`, `200`, `400`, `403`, `404`, `409`) y autorización contextual por rol y ámbito.
  - Nuevos endpoints `POST /api/payments/{id}/confirm` y `POST /api/payments/{id}/fail`.
- **Frontend (Angular)**:
  - Actualización del modelo `Membership` y de `gym-api.service.ts` por la nueva numeración de `MembershipStatus` (`Pending = 0`, sin `Depleted`) y los nuevos campos; los portales que muestran el estado de la membresía pasan a usar el estado efectivo y el indicador de saldo agotado.
- **Tests**:
  - Pruebas unitarias de dominio, pruebas de configuración del modelo y migración, pruebas de servicios de aplicación (contratación atómica, idempotencia, elegibilidad, renovaciones, suspensiones, cancelaciones, worker) y pruebas de controlador/autorización.
