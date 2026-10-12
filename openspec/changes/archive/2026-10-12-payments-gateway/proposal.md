## Why

Con `payments-core` el módulo `Payments` registra cobros manuales de forma sólida, pero todo cobro electrónico sigue dependiendo de que un operador lo confirme a mano. El gimnasio necesita que un alumno pueda pagar su membresía en línea y que el sistema la active sin intervención, sin confiar en lo que informe el frontend.

Integrar una pasarela introduce problemas que el núcleo manual no tiene y que hay que resolver explícitamente:

- **El dinero puede cobrarse aunque el sistema no lo espere**: un alumno puede pagar un checkout después de que secretaría cobró en efectivo, o después de que el pago interno se canceló o venció. Ese cobro no se puede rechazar; hay que registrarlo y dejarlo a la vista para resolverlo.
- **Las notificaciones llegan duplicadas, desordenadas o falsificadas**: el endpoint de webhooks es público y no puede confiar en el contenido que recibe.
- **Las llamadas a la pasarela no son transaccionales**: una devolución o un checkout pueden quedar a medio hacer si la llamada falla o el proceso se interrumpe.

**Depende de `payments-core`**: debe implementarse después de que ese cambio esté archivado, porque extiende su modelo y su especificación.

## What Changes

- **Proveedor externo**: `PaymentProvider` incorpora `MercadoPago`; contrato `IPaymentProvider` / `IPaymentProviderResolver` en `Application` y adaptador `MercadoPagoPaymentProvider` en `Infrastructure`, con credenciales sólo en configuración segura.
- **Checkout de membresías**: `POST /api/payments/checkout` crea un pago `Pending` del proveedor por el saldo pendiente de la membresía, calculado en el servidor, y devuelve la URL de pago. Lo puede iniciar el personal o el propio alumno. Los checkouts vencen y se cancelan automáticamente.
- **Webhooks verificados e idempotentes**: `POST /api/payments/webhooks/{provider}` verifica la firma, deduplica por evento, consulta el estado real en el proveedor y recién entonces confirma el pago. Un pago externo no puede confirmarse ni rechazarse manualmente.
- **Devoluciones contra el proveedor**: las devoluciones de pagos externos se reservan como `Pending`, se solicitan al proveedor y se completan o fallan según su respuesta, sin superar nunca el importe del pago.
- **Conciliación y discrepancias**: `POST /api/payments/{id}/reconcile` contrasta un pago externo con el proveedor; las diferencias (importe, moneda, cobro sobre un pago cancelado o vencido, sobrepago, cobro duplicado, devolución externa, pago desconocido) se registran en `PaymentDiscrepancies` con ciclo de vida abierto/resuelto, listado y resolución auditada.
- **Permiso `PAYMENTS_RECONCILE`** reservado al administrador.

### Fuera de alcance

- Checkout de pases individuales de clase (las clases no tienen precio de catálogo).
- Reversión automática en el proveedor de cobros discrepantes: el administrador la gestiona y deja constancia al resolver la discrepancia.
- Pagos recurrentes o suscripciones, y otros proveedores distintos de Mercado Pago.

## Capabilities

### New Capabilities

Ninguna.

### Modified Capabilities
- `payments`: agrega los requisitos de pasarela externa (checkout, vencimiento, webhooks, devoluciones contra el proveedor, conciliación y discrepancias) sobre el modelo definido en `payments-core`.

## Impact

- **Domain (`SmartGym.Domain`)**: `PaymentProvider.MercadoPago`; nuevas columnas en `Payment` (`ExternalReference`, `CheckoutUrl`, `ExpiresAtUtc`) y en `PaymentRefund` (`Status`, `ExternalRefundId`, `CompletedAtUtc`, `FailureReason`); nuevas entidades `PaymentWebhookEvent` y `PaymentDiscrepancy`; enums `PaymentRefundStatus`, `PaymentDiscrepancyType`, `PaymentDiscrepancyStatus` y `PaymentWebhookEventStatus`.
- **Application (`SmartGym.Application`)**: `IPaymentProvider`, `IPaymentProviderResolver`, ampliación de `PaymentService` (checkout, webhook, conciliación, devolución externa, discrepancias), nuevos códigos en `PaymentErrorCodes`, permiso `PAYMENTS_RECONCILE`.
- **Infrastructure (`SmartGym.Infrastructure`)**: `MercadoPagoPaymentProvider`, `MercadoPagoOptions`, `PaymentExpirationWorker`, configuraciones EF y migración `AddPaymentGateway`.
- **WebApi (`SmartGym.WebApi`)**: rutas de checkout, webhook (anónima, con cuerpo crudo y límite de tasa), conciliación y discrepancias en `PaymentsController`.
- **Frontend (`frontend/src/app`)**: modelos y métodos de servicio para checkout y discrepancias.
- **Operación**: requiere credenciales de Mercado Pago y una URL pública para el webhook.
