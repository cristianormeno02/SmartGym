## MODIFIED Requirements

### Requirement: Cancelación de membresía y preservación histórica
El sistema SHALL permitir cancelar una membresía `PENDING`, `ACTIVE` o `SUSPENDED` (`POST /api/memberships/{id}/cancel`) exigiendo un motivo obligatorio. Al cancelar:
1. La membresía pasa a estado terminal `CANCELLED` y registra `CancelledAtUtc`, `CancelledByUserId` y `CancellationReason` junto con su entrada en `MembershipStatusChanges`.
2. Si la membresía poseía `AvailableCredits > 0`, coordina con `MembershipCredits` un movimiento de baja por cancelación (`CancellationForfeiture`) que deja `AvailableCredits = 0` manteniendo trazabilidad en el ledger.
3. Si existen reservas activas futuras (`PENDING`, `CONFIRMED` o `WAITLISTED` con `StartsAtUtc > UtcNow`) asociadas a la membresía cancelada, coordina en la misma transacción su cancelación a través de `reservations` (sin devolución de crédito y sin considerarlas cancelaciones tardías), liberando los cupos en `ClassSessions`, promoviendo la lista de espera de cada clase afectada y registrando la cantidad de reservas afectadas.
4. No SHALL eliminar físicamente ni ocultar pagos, movimientos de créditos pasados ni reservas históricas, y cualquier reintegro económico SHALL gestionarse por separado en `Payments`.
5. Si la membresía cancelada nunca llegó a activarse (`PENDING`) y tenía una promoción canjeada, SHALL coordinar con `Promotions` la anulación del canje para que no cuente contra los topes de uso. La cancelación de una membresía que ya estuvo `ACTIVE` no SHALL devolver el canje.

La suspensión y la cancelación de una membresía SHALL adquirir los bloqueos en el mismo orden que las operaciones de reserva (primero las clases de las reservas afectadas, después la membresía), para no interbloquearse con reservas o cancelaciones concurrentes.

#### Scenario: Cancelación con créditos restantes y reservas futuras
- **WHEN** un administrador cancela una membresía `ACTIVE` con 3 créditos disponibles y 2 reservas para clases de la semana siguiente
- **THEN** la membresía pasa a `CANCELLED`, se registra el movimiento de baja de créditos en `MembershipCredits`, las 2 reservas futuras se cancelan liberando cupo, y todos los pagos y consumos pasados permanecen intactos en el historial

#### Scenario: Cancelación de membresía libera cupo para la lista de espera
- **WHEN** se cancela una membresía con una reserva `CONFIRMED` en una clase completa que tiene un alumno elegible en lista de espera
- **THEN** la reserva se cancela y el alumno en espera es promovido al cupo liberado
