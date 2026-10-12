## Why

El módulo de pagos actual (`Payment`, `PaymentService`, `PaymentsController` bajo `Operations`) es un esqueleto inicial que cumple sólo el caso feliz de membresías, pero carece de la solidez financiera y de los controles de concurrencia, seguridad e idempotencia exigidos por el negocio:

- **Devoluciones sin importe ni historial**: hoy `Payment.Refund(reason)` sobrescribe el estado del pago a `Refunded` sin importe ni validación de estado, perdiendo cuánto se devolvió e impidiendo devoluciones parciales o múltiples.
- **Vinculación sin validar**: un pago se vincula a un `MembershipId` o `ClassSessionId` sin comprobar existencia, titularidad, moneda ni saldo pendiente, y un pago ya confirmado que se vincula después a una membresía no dispara su activación.
- **Sin idempotencia ni protección de concurrencia**: no existe `IdempotencyKey` ni bloqueo pesimista (`SELECT … FOR UPDATE`) al registrar, confirmar o devolver; dos solicitudes simultáneas pueden duplicar un cobro, devolver más dinero del pagado o dejar sin activar una membresía cubierta por dos pagos parciales concurrentes.
- **Medio y proveedor mezclados**: `MercadoPago` es un valor de `PaymentMethod`, lo que impide integrar más adelante una pasarela sin romper el modelo.
- **Brechas de validación, permisos, consultas y auditoría**: `GetAllAsync` descarga toda la tabla `Payments` en memoria; no existen permisos granulares (`PAYMENTS_*`), códigos de error estables, moneda explícita (`Currency`), ni registros en `AuditLogs` para las operaciones financieras.

Este cambio endurece el **núcleo manual** de `Payments` manteniendo intacta la integración por eventos con `Memberships` (`PaymentCompletedEvent` → `ActivateMembershipOnPaymentCompletedHandler`) y la verificación de pago confirmado en `Reservations` (`SingleClassTicket`). La pasarela externa (checkout, webhooks y conciliación) se trata en el cambio posterior `payments-gateway`, que depende de éste.

## What Changes

- **Separación de medio (`PaymentMethod`) y proveedor (`PaymentProvider`)**: `PaymentMethod` queda en `Cash`, `BankTransfer`, `CreditCard`, `DebitCard`, `DigitalWallet`, `Other`; se introduce `PaymentProvider` con el único valor `Manual` (las pasarelas se agregan en `payments-gateway`). Se agregan a `Payment` `Currency` (ISO 4217, por defecto la moneda del gimnasio), `RefundedAmount`, `ConfirmedAtUtc`, `CancelledAtUtc`, `IdempotencyKey` y `Version` (`xmin`).
- **Máquina de estados explícita**: `PaymentStatus` incorpora `Cancelled` y `PartiallyRefunded` junto a `Pending`, `Completed`, `Failed` y `Refunded`, con transiciones estrictas en el dominio.
- **Devoluciones parciales y múltiples (`PaymentRefund`)**: nueva entidad que registra cada devolución con importe, motivo y responsable sin pisar el importe original del pago; actualiza `Payment.RefundedAmount` y deriva `PartiallyRefunded` / `Refunded`.
- **Una obligación por pago, validada**: un pago se aplica a lo sumo a **una** obligación comercial (`Membership` o `ClassSession`), representada por las columnas existentes `Payment.MembershipId` / `Payment.ClassSessionId`. No se introduce una tabla de aplicaciones: así no hay dos fuentes de verdad para los consumidores actuales. La aplicación valida existencia, titularidad, moneda y, para membresías, que no supere el saldo pendiente.
- **Aplicación diferida de un pago**: `POST /api/payments/{id}/apply` vincula un pago aún no aplicado a una obligación; si el pago ya está confirmado y completa el precio de una membresía `PENDING`, dispara su activación mediante el nuevo `PaymentAppliedEvent`.
- **Saldo de membresía**: `GET /api/payments/obligations/membership/{membershipId}` devuelve precio acordado, total neto cobrado, total en pagos pendientes, saldo pendiente y detalle de pagos y devoluciones. Las clases no tienen precio de catálogo (el precio del pase individual lo define el operador al emitirlo), por lo que no exponen saldo.
- **Idempotencia y concurrencia en PostgreSQL**: cabecera `Idempotency-Key` en el registro de pagos y devoluciones; `LockPaymentForUpdateAsync` y orden de bloqueo membresía → pago; restricciones `CHECK` e índices únicos parciales.
- **BREAKING – API REST**:
  - `GET /api/payments` pasa a devolver `PagedResult<PaymentDto>` con filtros en servidor.
  - `POST /api/payments/{id}/refund` (motivo como `string` crudo, devolución total implícita) se reemplaza por `POST /api/payments/{id}/refunds` con importe y motivo, más `GET /api/payments/{id}/refunds`.
  - Se agregan `POST /api/payments/{id}/cancel`, `POST /api/payments/{id}/apply`, `GET /api/payments/pending` y `GET /api/payments/obligations/membership/{membershipId}`.
- **Autorización granular y auditoría transaccional**: permisos `PAYMENTS_READ`, `PAYMENTS_CREATE`, `PAYMENTS_CONFIRM`, `PAYMENTS_CANCEL`, `PAYMENTS_ALLOCATE` y `PAYMENTS_REFUND`; registro en `AuditLogs` dentro de la misma transacción; notificaciones post-commit para que un fallo en `Notifications` nunca revierta un pago confirmado.
- **Importe neto en los consumidores**: `Memberships`, `Reservations` y el dashboard pasan a considerar el importe neto (`Amount - RefundedAmount`) y el nuevo estado `PartiallyRefunded`.

### Fuera de alcance

- **Pasarela externa** (`IPaymentProvider`, checkout, webhooks, conciliación y discrepancias): cambio `payments-gateway`.
- **Un pago repartido entre varias obligaciones** (tabla `PaymentAllocation`): se difiere hasta que exista un caso de uso; hoy cada cobro corresponde a un único concepto.
- **Precio de catálogo para clases sueltas**: el pase individual sigue llevando el precio que indica el operador al emitirlo.
- **Turnos y arqueo de caja física** (`CashRegister` / `CashSession`), **facturación fiscal electrónica** (AFIP / ARCA) y contabilidad de partida doble.

## Capabilities

### New Capabilities
- `payments`: modelo financiero de pagos manuales, máquina de estados, aplicación validada a una obligación comercial, devoluciones parciales y totales (`PaymentRefund`), consulta de saldo de membresías, idempotencia, concurrencia, autorización contextual y auditoría.

### Modified Capabilities
- `operations-reporting`: el requisito "Gestión de Pagos Multimétodo" referencia la máquina de estados ampliada, delega el detalle en `payments` y define los ingresos del período como cobros confirmados menos devoluciones del período.
- `memberships`: el requisito "Integración con Pagos y política de activación" pasa a calcular el total pagado con el importe neto de los pagos `Completed` y `PartiallyRefunded`, y contempla la activación al aplicar un pago ya confirmado.
- `reservations`: el requisito "Reservas con pases individuales, créditos compensatorios y promociones" define qué estados de pago habilitan un pase individual (`Completed` o `PartiallyRefunded`; un pago `Refunded` no lo habilita).

## Impact

- **Domain (`SmartGym.Domain`)**: evolución de `Payment`, `PaymentMethod` y `PaymentStatus`; nuevo enum `PaymentProvider`; nueva entidad `PaymentRefund`; nuevos eventos en `PaymentEvents.cs` (`PaymentCompletedEvent` conservado, `PaymentAppliedEvent` pre-commit y eventos de notificación post-commit).
- **Application (`SmartGym.Application`)**: ampliación de `IPaymentService` / `PaymentService`, validadores FluentValidation, `PaymentErrorCodes`, `LockPaymentForUpdateAsync` en `ISmartGymDbContext`, permisos y políticas en `RolesAndPolicies`. Actualización de `MembershipActivationService`, `MembershipService` (incluida la ruta de `PaymentId` vinculado), `MembershipEligibilityService`, `ReservationAccessResolver` y `DashboardService`.
- **Infrastructure (`SmartGym.Infrastructure`)**: `PaymentConfiguration` actualizada, nueva `PaymentRefundConfiguration`, migración `HardenPaymentsModule`, registro de políticas y handlers en `DependencyInjection.cs`.
- **WebApi (`SmartGym.WebApi`)**: reescritura de `PaymentsController` con las nuevas rutas, políticas por permiso, cabecera `Idempotency-Key` y traducción uniforme de códigos de error.
- **Frontend (`frontend/src/app`)**: actualización de `gym.models.ts` y `gym-api.service.ts`.
- **Tests (`backend/tests`)**: suites unitarias en `SmartGym.Domain.UnitTests/Operations` y pruebas de migración y concurrencia en PostgreSQL en `SmartGym.IntegrationTests/Payments`.
