Requiere `payments-core` implementado y archivado. Las rutas son relativas a `backend/src/` salvo indicación; `tests/...` abrevia `backend/tests/SmartGym.Domain.UnitTests`. Las tareas marcadas 🔴 escriben primero la prueba que falla (TDD); la tarea siguiente la pone en verde. "Decision N" refiere a `design.md`.

## 1. Dominio y persistencia

- [ ] 1.1 🔴 Ampliar `tests/.../Operations/OperationsTests.cs`: `PaymentRefund` nace `Pending` o `Completed` y sus transiciones (`Complete`, `Fail`); `PaymentDiscrepancy.Resolve` exige notas y rechaza una ya resuelta; `Payment` expone si es externo y si su checkout está vencido. Crear `tests/.../Operations/PaymentGatewayModelConfigurationTests.cs` (patrón `UseNpgsql` sin conexión) verificando las columnas e índices de Decision 2.
- [ ] 1.2 Agregar `PaymentProvider.MercadoPago`; crear los enums `PaymentRefundStatus`, `PaymentDiscrepancyType`, `PaymentDiscrepancyStatus` y `PaymentWebhookEventStatus`; crear `PaymentWebhookEvent.cs` y `PaymentDiscrepancy.cs`; ampliar `Payment.cs` y `PaymentRefund.cs`; crear `PaymentWebhookEventConfiguration.cs` y `PaymentDiscrepancyConfiguration.cs` y actualizar las de `Payment` y `PaymentRefund`; registrar los `DbSet`; verificar que las pruebas de 1.1 pasen en verde.
- [ ] 1.3 Generar la migración `AddPaymentGateway` conforme al "Migration Plan".
- [ ] 1.4 🔴→🟢 Crear `backend/tests/SmartGym.IntegrationTests/Payments/AddPaymentGatewayMigrationIntegrationTests.cs` (base aislada migrada hasta `HardenPaymentsModule`): sembrar devoluciones, aplicar la migración y verificar `Status = Completed` y `CompletedAtUtc`; verificar que `UX_Payments_Provider_ExternalTx` no afecta a pagos manuales con el mismo `ExternalTransactionId` y que `UX_Payments_OpenCheckout` rechaza un segundo checkout abierto.

## 2. Contrato de proveedor, checkout y vencimiento

- [ ] 2.1 Crear `IPaymentProvider.cs`, `IPaymentProviderResolver.cs` y sus tipos (`PaymentCheckoutContext`, `PaymentCheckoutResult`, `ExternalPaymentSnapshot`, `ExternalRefundResult`, `WebhookRequest`, `ExternalWebhookEvent`) en `SmartGym.Application/Modules/Operations/Interfaces/` (Decision 1), y un `FakePaymentProvider` configurable en el proyecto de pruebas.
- [ ] 2.2 🔴 Crear `tests/.../Operations/PaymentCheckoutTests.cs`: importe igual al saldo pendiente calculado en el servidor; validaciones de obligación; `OBLIGATION_OVERPAID` con saldo cero; `CHECKOUT_ALREADY_OPEN`; reintento idempotente devuelve el mismo pago y URL; fallo del proveedor deja el pago `Failed` y responde `PROVIDER_UNAVAILABLE`; `PROVIDER_NOT_SUPPORTED`; confirmación o rechazo manual de un pago externo responde `EXTERNAL_PAYMENT_MANUAL_CONFIRM_FORBIDDEN`; el vencimiento cancela checkouts vencidos de forma idempotente y permite uno nuevo.
- [ ] 2.3 Implementar `CreateCheckoutAsync` en `PaymentService` (Decision 3), la restricción de confirmación manual, `PaymentProviderResolver` y `PaymentExpirationWorker` en `SmartGym.Infrastructure/` (Decision 6); agregar los códigos nuevos a `PaymentErrorCodes`; verificar que `PaymentCheckoutTests.cs` pase en verde.

## 3. Adaptador de Mercado Pago

- [ ] 3.1 🔴 Confirmar contra la documentación vigente de Mercado Pago el manifiesto de la firma de webhooks, la creación de preferencias con vencimiento y referencia externa, la consulta de pagos y el reintegro parcial. Crear `tests/.../Operations/MercadoPagoPaymentProviderTests.cs`: verificación de firma con un vector conocido, firma alterada, cabecera ausente y marca de tiempo fuera de tolerancia; mapeo de estados del proveedor al snapshot normalizado, con `HttpMessageHandler` simulado.
- [ ] 3.2 Implementar `MercadoPagoOptions` y `MercadoPagoPaymentProvider` en `SmartGym.Infrastructure/`, registrándolos en `DependencyInjection.cs` sólo cuando hay credenciales configuradas; documentar las claves de configuración en el README del backend; verificar que `MercadoPagoPaymentProviderTests.cs` pase en verde.

## 4. Webhooks, conciliación y discrepancias

- [ ] 4.1 🔴 Crear `tests/.../Operations/PaymentWebhookAndReconciliationTests.cs` cubriendo cada fila de la tabla de reglas de Decision 4 (confirmación, repetición, rechazo que no cierra el checkout, `AmountMismatch`, `CurrencyMismatch`, `ApprovedAfterTerminalState`, `Overpayment` con confirmación, `DuplicateCharge`, `ExternalRefund`, `UnknownPayment`); firma inválida no escribe en la base; conciliación de un pago `Pending` aprobado; conciliación repetida no duplica discrepancias; `PAYMENT_NOT_EXTERNAL`; listado y resolución de discrepancias (`DISCREPANCY_ALREADY_RESOLVED`, notas obligatorias).
- [ ] 4.2 Implementar `ApplyProviderSnapshot`, `ProcessWebhookAsync`, `ReconcileAsync`, `GetDiscrepanciesAsync` y `ResolveDiscrepancyAsync` en `PaymentService` (Decisions 4 y 7) con su auditoría; verificar que `PaymentWebhookAndReconciliationTests.cs` pase en verde.

## 5. Devoluciones externas

- [ ] 5.1 🔴 Crear `tests/.../Operations/PaymentExternalRefundTests.cs`: devolución aceptada (`Completed`, `ExternalRefundId`, pago `PartiallyRefunded`); devolución rechazada (`Failed`, pago intacto, `PROVIDER_REFUND_FAILED`); las devoluciones `Pending` reservan el disponible; la conciliación completa o falla una devolución `Pending`; las devoluciones de pagos manuales siguen en un paso; el dashboard resta sólo devoluciones `Completed` por `CompletedAtUtc`.
- [ ] 5.2 Ampliar `CreateRefundAsync` (Decision 5) y `DashboardService`; verificar que `PaymentExternalRefundTests.cs` pase en verde.

## 6. Concurrencia en PostgreSQL

- [ ] 6.1 🔴→🟢 Ampliar `backend/tests/SmartGym.IntegrationTests/Payments/PaymentConcurrencyIntegrationTests.cs`: dos copias simultáneas del mismo webhook (una confirmación, una activación, ambas `200`); webhook aprobado concurrente con un cobro manual que cubre la membresía (ambos pagos `Completed`, una activación, discrepancia `Overpayment`); webhook aprobado concurrente con la cancelación del pago (queda `Completed`, o `Cancelled` con discrepancia, sin estado intermedio); dos devoluciones externas concurrentes (una sola llamada al proveedor); dos checkouts concurrentes para la misma membresía (uno solo abierto).

## 7. API, autorización y verificación final

- [ ] 7.1 Agregar el permiso y la política `PaymentsReconcile` en `Common/Security/RolesAndPolicies.cs` y `SmartGym.Infrastructure/DependencyInjection.cs`.
- [ ] 7.2 🔴 Ampliar `tests/.../Operations/PaymentsControllerTests.cs` y `PaymentAuditAndAuthorizationTests.cs`: un alumno inicia y cancela sólo su propio checkout; un secretario no concilia ni ve discrepancias (`403`); el webhook es anónimo, responde `401` con firma inválida, `404` con proveedor desconocido y `200` con evento verificado; códigos HTTP de Decision 8; entradas de auditoría de cada operación.
- [ ] 7.3 Agregar a `SmartGym.WebApi/Controllers/PaymentsController.cs` las rutas de Decision 8, con lectura del cuerpo crudo y límite de tasa en el webhook; verificar que las pruebas de 7.2 pasen en verde.
- [ ] 7.4 Actualizar `frontend/src/app/core/models/gym.models.ts` y `frontend/src/app/core/services/gym-api.service.ts` con checkout, `checkoutUrl`, `expiresAtUtc`, conciliación y discrepancias; verificar que `npm run build` en `frontend/` compile sin errores.
- [ ] 7.5 Ejecutar `dotnet test backend/SmartGym.slnx` y `openspec validate payments-gateway --strict`, comprobando que todo pase sin errores.
