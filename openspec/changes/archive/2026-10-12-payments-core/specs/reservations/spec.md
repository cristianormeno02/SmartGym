## MODIFIED Requirements

### Requirement: Reservas con pases individuales, créditos compensatorios y promociones
El sistema SHALL permitir reservar con derechos distintos de una membresía:
- **Pase individual (`SingleClassTicket`)**: movimiento `SingleClassTicket` activo del alumno para esa clase. Si existen pagos en `Payments` vinculados al alumno y la clase, al menos uno SHALL estar confirmado con importe neto positivo (estado `Completed` o `PartiallyRefunded`); en caso contrario (sólo pagos `Pending`, `Failed`, `Cancelled` o `Refunded`) se rechaza con `409` (`PAYMENT_NOT_CONFIRMED`). La reserva SHALL registrar en `PaymentId` el pago confirmado más reciente.
- **Crédito compensatorio autónomo (`CompensatoryCredit`)**: movimiento `ClassSuspensionCompensation` sin membresía, vigente en la fecha local de la clase y aplicable a su actividad. Permite reservar aun sin membresía activa.
- En ambos casos el derecho se vincula a la reserva mediante `SingleClassTicketMovementId` y un índice único parcial SHALL impedir que el mismo movimiento respalde más de una reserva que lo retiene (`PENDING`, `CONFIRMED`, `ATTENDED`, `NO_SHOW` o `CANCELLED` tardía).
- **Promoción (`PromotionalBenefit`)**: aplica sólo cuando la promoción cubre el acceso completo a la clase (clase gratis o 100% de descuento); si no, se rechaza con `409` (`PROMOTION_NOT_APPLICABLE`). La evaluación y el canje se delegan en `Promotions`, el canje se registra al confirmar la reserva y queda vinculado a ella (`PromotionRedemptionId`), y un canje SHALL respaldar una única reserva. Los descuentos parciales sobre una clase paga se aplican en la compra del pase individual, fuera de este módulo.

#### Scenario: Reserva con pase individual pagado e intento de reutilización
- **WHEN** un alumno sin membresía adquiere un pase individual con pago `Completed`, reserva la clase y luego intenta respaldar otra reserva con el mismo pase
- **THEN** la primera reserva queda `CONFIRMED` con `AccessType = SingleClassTicket` y la segunda se rechaza con `409 Conflict`

#### Scenario: Pase individual con pago sin confirmar
- **WHEN** un alumno intenta reservar con un pase cuyo único pago vinculado está `Pending`
- **THEN** el sistema rechaza con `409 Conflict` (`PAYMENT_NOT_CONFIRMED`)

#### Scenario: Pase individual con pago parcialmente devuelto
- **WHEN** un alumno intenta reservar con un pase cuyo único pago vinculado está `PartiallyRefunded`
- **THEN** la reserva queda `CONFIRMED` con `AccessType = SingleClassTicket`

#### Scenario: Pase individual con pago totalmente devuelto
- **WHEN** un alumno intenta reservar con un pase cuyo único pago vinculado está `Refunded`
- **THEN** el sistema rechaza con `409 Conflict` (`PAYMENT_NOT_CONFIRMED`)

#### Scenario: Reserva con crédito compensatorio autónomo
- **WHEN** un alumno sin membresía activa, con un crédito compensatorio autónomo vigente y aplicable a la actividad, reserva una clase abierta con cupo
- **THEN** la reserva queda `CONFIRMED` con `AccessType = CompensatoryCredit` y el crédito no puede respaldar otra reserva mientras ésta lo retenga

#### Scenario: Crédito compensatorio vencido
- **WHEN** un alumno sin otro derecho intenta reservar una clase cuya fecha es posterior al vencimiento de su crédito compensatorio
- **THEN** el sistema rechaza con `409 Conflict` (`NO_ACCESS_RIGHT`)

#### Scenario: Reserva con beneficio promocional
- **WHEN** un alumno aplica un código promocional válido de clase gratis
- **THEN** `Promotions` registra el canje, la reserva queda `CONFIRMED` con `AccessType = PromotionalBenefit` y `PromotionRedemptionId`, y un reintento idempotente no vuelve a canjear la promoción
