## Purpose

Administra el ciclo de vida completo de las membresías adquiridas por los alumnos: contratación atómica con snapshot comercial e idempotencia, máquina de estados (`PENDING`, `ACTIVE`, `SUSPENDED`, `EXPIRED`, `CANCELLED`), renovaciones trazables, suspensiones temporales, cancelaciones coordinadas, evaluación de elegibilidad y acceso sin consumo, integración transaccional con planes, créditos, promociones, pagos y reservas, autorización contextual y procesamiento periódico de vencimientos.

## Requirements

### Requirement: Contratación de membresía e identidad del alumno
El sistema SHALL permitir registrar la contratación de un plan de membresía para una persona existente que se encuentre en estado `ACTIVA` en el módulo `People` y posea el rol `Student` activo. El módulo `Memberships` SHALL referenciar al alumno únicamente por `StudentId` sin duplicar sus datos personales (nombre, documento, email, teléfono), consultándolos a través de la integración con `People`. El plan seleccionado SHALL existir y encontrarse activo (`IsActive = true`) para nuevas contrataciones.

#### Scenario: Contratación exitosa por un administrativo para un alumno válido
- **WHEN** un administrador o secretario contrata un plan activo para una persona en estado `ACTIVA` con rol `Student` activo
- **THEN** el sistema registra la membresía asociada a `StudentId` y `MembershipPlanId` con fecha/hora de contratación `PurchasedAtUtc` y `CreatedByUserId`

#### Scenario: Rechazo por persona inactiva, bloqueada o sin rol de alumno
- **WHEN** se intenta contratar una membresía para una persona inexistente, en estado `INACTIVA` o `BLOQUEADA`, o que no posee el rol `Student` activo
- **THEN** el sistema rechaza la operación con un error de validación o conflicto de negocio (`400`/`409`) sin crear la membresía

#### Scenario: Rechazo por plan inactivo o inexistente
- **WHEN** se intenta contratar un plan que fue desactivado comercialmente o no existe
- **THEN** el sistema rechaza la operación indicando que el plan no está disponible para nuevas contrataciones

### Requirement: Preservación del historial y snapshot comercial en la contratación
Cada membresía contratada SHALL preservar de forma inmutable las condiciones comerciales vigentes al momento de su adquisición: precio de lista (`OriginalPrice`), descuento aplicado (`DiscountAmount`), precio final acordado (`PurchasePrice`), moneda (`Currency`, ISO 4217), duración aplicada (`DurationDaysSnapshot`), créditos iniciales (`InitialCredits`), modalidad ilimitada (`IsUnlimited`), alcance de actividades (`AppliesToAllActivities`) y el conjunto de actividades permitidas (`MembershipAllowedActivities`). Modificaciones posteriores o la baja comercial del `MembershipPlan` no SHALL alterar retroactivamente el precio, la vigencia, los créditos ni las actividades permitidas de las membresías ya contratadas.

#### Scenario: Cambio de precio o condiciones del plan no afecta membresías vigentes
- **WHEN** un alumno contrata un plan a `PurchasePrice = 25000 ARS` con 8 créditos y acceso a "CrossFit", y posteriormente el administrador cambia el precio del plan a `32000 ARS`, reduce sus créditos a 4 o quita "CrossFit"
- **THEN** la membresía ya adquirida conserva `PurchasePrice = 25000 ARS`, sus 8 créditos iniciales y el derecho de acceso a "CrossFit" durante toda su vigencia

#### Scenario: Desactivación comercial del plan con membresías asociadas
- **WHEN** un plan se desactiva para nuevas ventas o se intenta eliminar teniendo membresías históricas asociadas
- **THEN** el sistema impide la eliminación física del plan y las membresías activas continúan operando normalmente bajo las condiciones contratadas

### Requirement: Atomicidad e idempotencia en la contratación y carga inicial
La contratación de la membresía, el canje de la promoción (si aplica), la vinculación del pago inicial (si se informa) y la carga de créditos iniciales (cuando la membresía nace en estado `ACTIVE`) SHALL ejecutarse dentro de una única transacción atómica. Si cualquiera de los pasos falla, no SHALL persistirse la membresía ni movimientos huérfanos. La operación SHALL admitir una clave de idempotencia (`IdempotencyKey`) por alumno para prevenir contrataciones o cargas de créditos duplicadas ante reintentos HTTP o solicitudes concurrentes.

#### Scenario: Reintento HTTP con la misma clave de idempotencia
- **WHEN** se envía dos veces la solicitud de contratación con el mismo `IdempotencyKey` para el mismo alumno (en forma secuencial o concurrente)
- **THEN** el sistema crea una única membresía y una sola carga inicial de créditos en `MembershipCredits`, devolviendo en el segundo intento la misma membresía registrada

#### Scenario: Clave de idempotencia reutilizada con otra solicitud
- **WHEN** se envía una contratación con un `IdempotencyKey` ya usado por el mismo alumno pero con un plan o una fecha de inicio distintos
- **THEN** el sistema rechaza la operación con `409 Conflict` sin crear una nueva membresía

#### Scenario: Reversión atómica ante fallo en carga de créditos o promoción
- **WHEN** durante la contratación falla la validación/canje de la promoción o el registro del movimiento inicial en `MembershipCredits`
- **THEN** la transacción completa se revierte y no queda ninguna membresía creada ni saldo acreditado

### Requirement: Integración con Promociones en la contratación
Cuando se solicita aplicar una promoción al contratar o renovar, `Memberships` SHALL delegar la evaluación y el registro del canje en el módulo `Promotions`, sin implementar un motor promocional propio. La membresía SHALL almacenar `PromotionId`, `PromotionRedemptionId`, `OriginalPrice`, `DiscountAmount`, `PurchasePrice` y `ExtraCreditsGranted` para explicar históricamente el precio final y los beneficios recibidos. Si la promoción otorga créditos adicionales, el movimiento `PromotionBonus` SHALL registrarse en `MembershipCredits` al activarse la membresía.

#### Scenario: Contratación con promoción de descuento económico
- **WHEN** se contrata un plan de `20000 ARS` aplicando un código promocional válido del 20% de descuento
- **THEN** `Promotions` registra el canje y la membresía persiste `OriginalPrice = 20000`, `DiscountAmount = 4000`, `PurchasePrice = 16000` junto con `PromotionId` y `PromotionRedemptionId`

#### Scenario: Contratación con promoción de créditos adicionales
- **WHEN** se contrata y activa un plan de 8 créditos con una promoción que otorga 2 créditos extra
- **THEN** `MembershipCredits` registra el movimiento de carga inicial por 8 créditos y el movimiento de bonificación promocional (`PromotionBonus`) por 2 créditos, quedando un saldo disponible de 10 créditos

### Requirement: Integración con Pagos y política de activación
El sistema SHALL modelar la relación de una membresía con uno o múltiples registros en `Payments` (pagos totales, pagos parciales o reembolsos). `Memberships` no SHALL marcar una membresía como pagada ni activarla por indicación no verificada del frontend; la condición de pago SHALL determinarse a partir del total pagado (`TotalPaidAmount`), definido como la suma del importe neto (`Amount - RefundedAmount`) de los pagos en estado `Completed` o `PartiallyRefunded` aplicados a la membresía en `Payments`, o de la política de activación autorizada:
- Si al contratar el total pagado cubre el precio acordado (`TotalPaidAmount >= PurchasePrice`) o `PurchasePrice == 0`, la membresía se crea en estado `ACTIVE` y se acreditan sus créditos iniciales.
- Si el pago queda pendiente (`Pending`) o es insuficiente, la membresía se crea en estado `PENDING` sin acreditar créditos utilizables hasta que se cubra el precio o se ejecute la activación autorizada.
- La activación con saldo pendiente (al contratar con `ActivateImmediately = true` o mediante `POST /api/memberships/{id}/activate`) SHALL requerir un usuario `Administrator` y un motivo obligatorio (`ActivationReason`), que queda registrado en el historial de estados y en auditoría.
- Cuando `Payments` confirma un pago, o aplica a la membresía un pago ya confirmado, y con ello se completa el precio acordado, la membresía pasa de `PENDING` a `ACTIVE` y solicita a `MembershipCredits` la carga inicial de créditos (una única vez, de forma idempotente, también ante pagos concurrentes).
- El pago inicial informado al contratar y el pago existente vinculado por `PaymentId` SHALL aplicarse a la membresía a través de `Payments`, con sus mismas validaciones de titularidad, moneda y saldo pendiente.
- La vigencia (`StartDate`, `EndDate`) SHALL fijarse al contratar y no recalcularse al activar. Las contrataciones nuevas SHALL cumplir `StartDate >= Today`.
- El reembolso total o parcial de un pago de una membresía ya `ACTIVE` no SHALL cambiar su estado ni sus créditos; el detalle SHALL informar el saldo pendiente resultante.

#### Scenario: Membresía pendiente por falta de pago confirmado
- **WHEN** se registra una contratación cuyo pago queda en estado `PENDING`
- **THEN** la membresía se guarda en estado `PENDING`, no habilita reservas y aún no acredita saldo utilizable

#### Scenario: Activación al confirmar el pago requerido
- **WHEN** se registra la confirmación del pago en `Payments` o un operador autorizado ejecuta `POST /api/memberships/{id}/activate` cumpliendo las condiciones de activación
- **THEN** la membresía transiciona de `PENDING` a `ACTIVE`, se registra la carga inicial de créditos en `MembershipCredits` por única vez y se audita la transición

#### Scenario: Pago rechazado o anulado en membresía pendiente
- **WHEN** el pago asociado a una membresía `PENDING` resulta `Failed` (rechazado) o `Cancelled` (anulado)
- **THEN** la membresía permanece en `PENDING` (no habilitada) hasta que se registre un pago válido o se cancele

#### Scenario: Pago parcial no activa la membresía
- **WHEN** se registra un pago `Completed` de `10000 ARS` para una membresía `PENDING` con `PurchasePrice = 25000 ARS`
- **THEN** la membresía permanece en `PENDING` e informa `TotalPaidAmount = 10000` y `PendingBalance = 15000`

#### Scenario: Pago parcialmente devuelto cuenta por su importe neto
- **WHEN** una membresía `PENDING` con `PurchasePrice = 25000 ARS` tiene un pago de `15000 ARS` al que se le devolvieron `5000 ARS` y se registra otro pago `Completed` de `10000 ARS`
- **THEN** la membresía informa `TotalPaidAmount = 20000` y `PendingBalance = 5000`, y permanece en `PENDING`

#### Scenario: Reembolso sobre membresía activa
- **WHEN** se devuelve parcialmente un pago de una membresía `ACTIVE`
- **THEN** la membresía permanece `ACTIVE` con los mismos créditos y su detalle informa el saldo pendiente resultante

#### Scenario: Activación con saldo pendiente por un secretario
- **WHEN** un secretario intenta activar una membresía `PENDING` con saldo pendiente
- **THEN** el sistema rechaza la operación con `403 Forbidden` y la membresía permanece en `PENDING`

#### Scenario: Activación con saldo pendiente autorizada por un administrador
- **WHEN** un administrador activa una membresía `PENDING` con saldo pendiente indicando un motivo
- **THEN** la membresía pasa a `ACTIVE`, se acreditan sus créditos iniciales y el motivo queda registrado en el historial de estados y en auditoría

### Requirement: Separación estricta de responsabilidades sobre el saldo de créditos
El módulo `Memberships` no SHALL modificar `AvailableCredits` de forma libre ni insertar filas directamente en la tabla de movimientos sin pasar por las operaciones de dominio/aplicación de `MembershipCredits` (`ICreditLedgerService`). Cualquier actualización administrativa de la membresía no SHALL permitir alterar el saldo numérico. El saldo proyectado en `Membership.AvailableCredits` SHALL mantenerse en estricta sincronía transaccional con el libro mayor de `MembershipCredits`, y los planes ilimitados (`IsUnlimited = true`) SHALL autorizar el consumo por modalidad ilimitada sin depender de un saldo numérico artificialmente alto.

#### Scenario: Imposibilidad de alterar el saldo desde actualización de membresía
- **WHEN** un operador ejecuta operaciones sobre `Memberships` (creación, edición de notas, suspensión, reactivación)
- **THEN** ningún endpoint de `Memberships` acepta un campo para sobrescribir `AvailableCredits` directamente, y toda variación de créditos ocurre únicamente mediante movimientos auditados en `MembershipCredits`

#### Scenario: Consulta de desglose de créditos de la membresía
- **WHEN** se consulta `GET /api/memberships/{id}/credits`
- **THEN** el sistema devuelve el resumen coordinado con `MembershipCredits` distinguiendo créditos iniciales contratados, bonificados por promoción, compensatorios, consumidos, devueltos, vencidos/dados de baja y el saldo disponible actual, indicando si el plan es ilimitado

### Requirement: Máquina de estados de la membresía
El sistema SHALL gestionar explícitamente los siguientes estados de ciclo de vida en `MembershipStatus`:
- `PENDING` (`Pending`): contratada pero aún no habilitada para su uso.
- `ACTIVE` (`Active`): habilitada para utilizarse dentro de su período de vigencia.
- `SUSPENDED` (`Suspended`): inhabilitada temporalmente por una suspensión administrativa vigente.
- `EXPIRED` (`Expired`): finalizó su período de vigencia (`EndDate < Today`).
- `CANCELLED` (`Cancelled`): dada de baja definitivamente antes o durante su vigencia.

Las transiciones válidas SHALL ser:
- `PENDING → ACTIVE`: cuando se cumplen las condiciones de pago/activación.
- `PENDING → CANCELLED`: por desistimiento (operación explícita) o automáticamente por falta de pago cuando se supera el plazo de gracia (`Today > StartDate + PendingGraceDays`, por defecto 7 días) o la vigencia (`Today > EndDate`).
- `ACTIVE → SUSPENDED`: mediante operación explícita de suspensión dentro del período de vigencia.
- `SUSPENDED → ACTIVE`: mediante operación explícita de reactivación, o automáticamente al finalizar la ventana de suspensión cuando ésta tiene fecha de fin.
- `ACTIVE → EXPIRED`: automáticamente al superarse `EndDate` (`Today > EndDate`).
- `SUSPENDED → EXPIRED`: automáticamente al superarse `EndDate`, salvo que la suspensión abierta tenga extensión de vigencia pactada, en cuyo caso la membresía no vence mientras dure la suspensión.
- `ACTIVE` o `SUSPENDED → CANCELLED`: mediante operación explícita de cancelación administrativa.

Los estados `EXPIRED` y `CANCELLED` SHALL ser terminales: una membresía vencida o cancelada no SHALL reactivarse (debiendo registrarse una renovación o nueva contratación). Cada cambio de estado SHALL quedar registrado en `MembershipStatusChanges` con estado anterior, estado nuevo, motivo, usuario responsable (`ChangedByUserId`, o `null` si fue por proceso automático de vencimiento) y timestamp UTC.

#### Scenario: Transición inválida desde estado terminal
- **WHEN** se intenta activar, suspender o reactivar una membresía en estado `CANCELLED` o `EXPIRED`
- **THEN** el sistema rechaza la operación con un conflicto de negocio (`409`)

#### Scenario: Evaluación de vencimiento efectivo en tiempo real
- **WHEN** una membresía tiene estado persistido `ACTIVE` pero su `EndDate` es anterior a la fecha local actual del gimnasio (`Today`), y el proceso programado aún no corrió
- **THEN** el servicio de elegibilidad y las consultas informan su estado efectivo como `EXPIRED` e impiden cualquier nuevo uso o reserva

#### Scenario: Cancelación automática de membresía pendiente impaga
- **WHEN** una membresía `PENDING` con `StartDate = 01/10` sigue sin pago suficiente el `09/10` y el plazo de gracia es de 7 días
- **THEN** el proceso programado la pasa a `CANCELLED` con motivo de falta de pago y sin usuario responsable, anula el canje de promoción si existía y no genera movimientos de créditos

### Requirement: Evaluación de elegibilidad y reglas de acceso sin consumo
El backend SHALL ser la autoridad para determinar si una membresía puede utilizarse en una actividad y fecha (o `ClassSession`) determinadas mediante `IMembershipEligibilityService`. La consulta de elegibilidad (`GET /api/memberships/{id}/eligibility`) no SHALL consumir créditos ni modificar estado, y SHALL verificar todas las condiciones siguientes:
1. El alumno en `People` está en estado `ACTIVA` y posee rol `Student` activo.
2. El estado persistido de la membresía es `ACTIVE`.
3. La fecha de uso evaluada (fecha local de la clase o fecha consultada, por defecto `Today` en `IGymCalendar`) cumple `StartDate <= targetDate <= EndDate`.
4. La membresía no está suspendida (una membresía `SUSPENDED` no habilita ninguna fecha).
5. La actividad consultada está activa (`ActivityStatus.Active`) y está permitida por el snapshot de la membresía (`AppliesToAllActivities == true` o incluida en `MembershipAllowedActivities`).
6. La membresía es ilimitada (`IsUnlimited == true`) o posee saldo de créditos utilizables (`AvailableCredits > 0`).
7. Se cumplen las restricciones etarias de la actividad (cuando el alumno registra fecha de nacimiento) y las condiciones operativas/documentales configuradas (certificado médico vigente / contacto de emergencia para menores cuando la regla de bloqueo esté activa).

La respuesta de elegibilidad SHALL devolver `IsEligible` (`bool`) y la lista detallada de códigos y motivos de rechazo (`IneligibilityReasons`) cuando no sea elegible. Los códigos SHALL ser: `STUDENT_NOT_ACTIVE`, `MEMBERSHIP_PENDING`, `MEMBERSHIP_CANCELLED`, `MEMBERSHIP_EXPIRED` (vencida a la fecha actual), `OUT_OF_VALIDITY_RANGE` (no vencida, pero la fecha evaluada cae fuera de `[StartDate, EndDate]`), `MEMBERSHIP_SUSPENDED`, `ACTIVITY_NOT_ACTIVE`, `ACTIVITY_NOT_ALLOWED`, `NO_AVAILABLE_CREDITS`, `AGE_NOT_ALLOWED`, `MEDICAL_CERTIFICATE_REQUIRED` y `UNDERAGE_EMERGENCY_CONTACT_REQUIRED`.

Cuando un alumno posee más de una membresía elegible para la misma actividad y fecha y la reserva no indica cuál usar, el sistema SHALL elegir de forma determinista: primero las ilimitadas, luego la de `EndDate` más próximo y, a igualdad, la de contratación más antigua. La reserva SHALL poder indicar explícitamente otra membresía elegible.

#### Scenario: Membresía activa elegible para actividad y fecha permitidas
- **WHEN** se consulta elegibilidad para una membresía `ACTIVE`, vigente en la fecha de la clase, con créditos disponibles y cuya lista de actividades incluye la actividad consultada
- **THEN** el sistema devuelve `IsEligible = true` sin lista de bloqueos y sin descontar créditos del saldo

#### Scenario: Rechazo de elegibilidad por actividad no incluida o fecha fuera de vigencia
- **WHEN** se evalúa una membresía activa cuyo plan sólo permite "Pilates" para una sesión de "Boxeo", o para una clase posterior a `EndDate`
- **THEN** el sistema devuelve `IsEligible = false` indicando el código de motivo correspondiente (`ACTIVITY_NOT_ALLOWED` o `OUT_OF_VALIDITY_RANGE`), sin alterar los créditos

#### Scenario: Rechazo de elegibilidad por membresía pendiente o suspendida
- **WHEN** un alumno con membresía en estado `PENDING` o con una suspensión vigente intenta reservar o consulta elegibilidad
- **THEN** el sistema devuelve `IsEligible = false` con motivo `MEMBERSHIP_PENDING` o `MEMBERSHIP_SUSPENDED` respectivamente e impide la reserva

#### Scenario: Selección entre dos membresías elegibles
- **WHEN** un alumno con una membresía de créditos que vence el `15/10` y otra que vence el `30/10`, ambas elegibles para "Funcional", reserva una clase del `10/10` sin indicar membresía
- **THEN** el crédito se consume de la membresía que vence el `15/10`

### Requirement: Suspensiones y reactivaciones explícitas
El sistema SHALL permitir al personal autorizado suspender temporalmente una membresía `ACTIVE` vigente (`POST /api/memberships/{id}/suspend`) y reactivar una membresía `SUSPENDED` (`POST /api/memberships/{id}/reactivate`).
- La suspensión SHALL comenzar el día en que se registra (no se admiten suspensiones a futuro ni retroactivas) y registrar fecha de inicio (`StartDate`), fecha de fin opcional (`EndDate`, último día suspendido), motivo obligatorio (`Reason`), observaciones opcionales, usuario responsable y si aplica extensión de vigencia (`ExtendExpirationDate`, por defecto `false`). Sólo SHALL existir una suspensión abierta por membresía.
- La fecha de fin, cuando se informa, no SHALL superar el máximo configurado (`MaxSuspensionDays`, por defecto 90 días) y SHALL ser obligatoria cuando `ExtendExpirationDate = true`.
- Cuando la suspensión tiene fecha de fin, el sistema SHALL reactivar la membresía automáticamente al día siguiente.
- La fecha de vencimiento `Membership.EndDate` no SHALL extenderse automáticamente al suspender o reactivar, salvo que el operador autorizado indique explícitamente la política de extensión (`ExtendExpirationDate = true`). En ese caso la membresía no SHALL vencer mientras dure la suspensión y, al reactivar (manual o automáticamente), se suman a `EndDate` todos los días suspendidos (desde el inicio de la suspensión hasta el día anterior a la reactivación), dejando registro auditado del valor anterior y nuevo.
- Al suspender, el sistema SHALL cancelar en la misma transacción, con devolución de crédito y sin considerarlas cancelaciones tardías, las reservas futuras de esa membresía cuya clase caiga dentro de la ventana de suspensión (o todas las futuras si la suspensión no tiene fecha de fin).
- Mientras la membresía esté en estado `SUSPENDED` no SHALL permitir nuevas reservas ni consumos.

#### Scenario: Suspensión sin extensión de vencimiento
- **WHEN** un secretario suspende una membresía activa con vencimiento `30/11` indicando motivo "Viaje laboral" y `ExtendExpirationDate = false`
- **THEN** la membresía pasa a `SUSPENDED`, se crea el registro en `MembershipSuspensions` y `MembershipStatusChanges`, conserva `EndDate = 30/11` e impide reservar clases mientras dure la suspensión

#### Scenario: Reactivación de membresía suspendida vigente
- **WHEN** un operador reactiva una membresía `SUSPENDED` cuya fecha de vencimiento aún no expiró
- **THEN** se cierra la suspensión activa, la membresía vuelve a `ACTIVE`, se registra la transición con motivo y responsable, y vuelve a estar habilitada para reservar

#### Scenario: Suspensión con extensión de vencimiento
- **WHEN** una membresía con vencimiento `30/11` se suspende el `10/11` con `ExtendExpirationDate = true` y fecha de fin `14/11`, y el proceso programado la reactiva el `15/11`
- **THEN** la membresía vuelve a `ACTIVE` con `EndDate = 05/12` (5 días devueltos) y la suspensión registra `ExtensionDaysApplied = 5`

#### Scenario: Suspensión con extensión que supera el vencimiento original
- **WHEN** una membresía con vencimiento `30/11` se suspende el `25/11` con `ExtendExpirationDate = true` y fecha de fin `09/12`
- **THEN** el `05/12` la membresía sigue `SUSPENDED` (no vence ni pierde créditos) y al reactivarse el `10/12` queda `ACTIVE` con `EndDate = 15/12`

#### Scenario: Suspensión cancela reservas dentro de la ventana
- **WHEN** se suspende del `10/11` al `14/11` una membresía con una reserva para el `12/11` y otra para el `20/11`
- **THEN** la reserva del `12/11` se cancela con devolución de crédito y liberación de cupo, y la del `20/11` se conserva

#### Scenario: Extensión sin fecha de fin
- **WHEN** se solicita una suspensión con `ExtendExpirationDate = true` sin indicar fecha de fin
- **THEN** el sistema rechaza la operación con un error de validación (`400`)

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

### Requirement: Renovaciones y nuevas contrataciones sin solapamiento inválido
El sistema SHALL modelar cada renovación (`POST /api/memberships/{id}/renew`) como una **nueva entidad identificable** `Membership` que referencia a la membresía origen mediante `RenewedFromMembershipId`, conservando intacto el historial comercial y de créditos de cada período contratado:
- **Renovación anticipada**: si la membresía origen está `ACTIVE` y vigente (`EndDate >= Today`), la fecha de inicio por defecto de la nueva membresía SHALL ser ` origen.EndDate.AddDays(1) ` (períodos consecutivos sin huecos ni solapamiento).
- **Renovación desde vencimiento o suspensión**: si la membresía origen está `EXPIRED` (o `EndDate < Today`), la fecha de inicio por defecto SHALL ser `Today` (o la `StartDate` futura solicitada).
- **Cambio de plan, precio o promoción**: la renovación SHALL permitir mantener el mismo plan o elegir un plan diferente, tomando el precio vigente del nuevo plan al momento de renovar y evaluando promociones elegibles para el nuevo período.
- **Regla de solapamiento**: el sistema SHALL impedir que un alumno tenga dos membresías no canceladas (`PENDING`, `ACTIVE` o `SUSPENDED`) cuyos períodos `[StartDate, EndDate]` se superpongan y compartan al menos una actividad permitida, cuando la existente es ilimitada, tiene créditos disponibles o tiene créditos aún no acreditados por estar `PENDING`. Si la membresía existente ya acreditó y agotó el 100% de sus créditos, la nueva contratación SHALL admitirse sin requerir ningún indicador adicional.
- **Renovación no permitida**: no SHALL poder renovarse una membresía `PENDING` ni `CANCELLED` (`409`).
- **Inmutabilidad de canceladas**: ni una renovación ni una nueva contratación SHALL reactivar o mutar silenciosamente una membresía `CANCELLED`.

#### Scenario: Renovación anticipada genera período consecutivo
- **WHEN** un alumno con membresía activa que vence el `20/10` renueva anticipadamente el `18/10` por un plan de 30 días sin especificar fecha de inicio
- **THEN** se crea una nueva membresía vinculada por `RenewedFromMembershipId` con `StartDate = 21/10` y `EndDate = 19/11` (30 días inclusivos o conforme a la regla de duración), conservando la membresía anterior intacta con sus condiciones originales

#### Scenario: Rechazo de solapamiento de membresías vigentes con saldo
- **WHEN** se intenta contratar o renovar una membresía con rango `10/10` a `09/11` para un alumno que ya posee una membresía `ACTIVE` con créditos disponibles entre el `01/10` y el `31/10` para las mismas actividades
- **THEN** el sistema rechaza la operación con un conflicto de negocio (`409`) informando la membresía superpuesta

#### Scenario: Membresía pendiente bloquea una contratación superpuesta
- **WHEN** un alumno tiene una membresía `PENDING` de 8 créditos del `01/10` al `30/10` y se intenta contratar otra para las mismas actividades del `05/10` al `03/11`
- **THEN** el sistema rechaza la operación con `409` informando la membresía pendiente

#### Scenario: Nueva contratación tras agotar los créditos
- **WHEN** un alumno con una membresía `ACTIVE` vigente hasta el `31/10` y `AvailableCredits = 0` contrata el `15/10` un nuevo plan para las mismas actividades con inicio el `15/10`
- **THEN** el sistema crea la nueva membresía y conserva la anterior sin modificarla

### Requirement: Consultas, listado paginado y filtros en servidor
El sistema SHALL exponer consultas de membresías con paginación y ordenamiento ejecutados en PostgreSQL (sin cargar tablas completas en memoria), incluyendo:
- `GET /api/memberships`: listado paginado con filtros por `studentId`, `membershipPlanId`, `status` (incluyendo estado efectivo `EXPIRED`), rango de `startDate` (`startDateFrom`, `startDateTo`), rango de `endDate` (`endDateFrom`, `endDateTo`), rango de fecha de contratación (`purchasedFrom`, `purchasedTo`), `expiringWithinDays` (próximas a vencer), `hasAvailableCredits` (`true`/`false`) e indicadores de vencimiento.
- `GET /api/memberships/{id}`: detalle completo con snapshot comercial, resumen de créditos, pagos asociados, promoción aplicada, suspensiones e historial de cambios de estado.
- `GET /api/memberships/student/{studentId}`: historial cronológico de todas las membresías del alumno.
- `GET /api/memberships/student/{studentId}/current`: membresías actualmente vigentes o próximas (consecutivas) del alumno.
- `GET /api/memberships/student/{studentId}/active`: se conserva por compatibilidad y devuelve una única membresía utilizable en la fecha consultada, elegida con el orden de selección de elegibilidad.

La única edición directa admitida sobre una membresía SHALL ser la de sus notas (`PATCH /api/memberships/{id}/notes`), protegida por el token de concurrencia.

#### Scenario: Filtro de membresías próximas a vencer
- **WHEN** un secretario consulta `GET /api/memberships?expiringWithinDays=7&page=1&pageSize=20`
- **THEN** el servidor devuelve de forma paginada únicamente las membresías activas cuya `EndDate` se encuentra entre `Today` y `Today + 7 días`, con sus indicadores de días restantes y saldo disponible

### Requirement: Autorización contextual y permisos en Memberships
El sistema SHALL validar en el backend la identidad y el ámbito de acceso en cada operación de `Memberships`:
- Un **Alumno** (`Student`) sólo SHALL poder consultar sus propias membresías, su membresía actual, su elegibilidad y su propio resumen de créditos (`studentId == currentUser.PersonId`). Si intenta consultar datos de otro alumno o invocar operaciones administrativas, el backend SHALL responder `403 Forbidden`.
- Un **Instructor** (`Instructor` sin rol de Staff) no SHALL gestionar membresías; sólo accede a la información operativa de sus clases a través de `ClassSessions`/`Reservations`.
- Un **Secretario** (`Secretary`) y un **Administrador** (`Administrator`) SHALL poder consultar, contratar, activar, suspender, reactivar, cancelar y renovar membresías según las políticas y permisos del módulo (`MEMBERSHIPS_READ`, `MEMBERSHIPS_CREATE`, `MEMBERSHIPS_ACTIVATE`, `MEMBERSHIPS_SUSPEND`, `MEMBERSHIPS_REACTIVATE`, `MEMBERSHIPS_CANCEL`, `MEMBERSHIPS_RENEW`, `MEMBERSHIPS_MANAGE` para la edición de notas). La activación de una membresía con saldo pendiente SHALL estar reservada al **Administrador**.

#### Scenario: Alumno intenta consultar membresías de otro alumno
- **WHEN** un usuario autenticado con rol `Student` solicita `GET /api/memberships/student/{otroStudentId}` o `GET /api/memberships/{idDeOtroAlumno}`
- **THEN** el backend rechaza la solicitud con `403 Forbidden`

### Requirement: Concurrencia optimista, auditoría y procesamiento periódico de vencimientos
El sistema SHALL proteger las modificaciones administrativas sobre `Membership` con un token de concurrencia optimista (`xmin` / `Version`), respondiendo `409 Conflict` ante escrituras basadas en una versión desactualizada. Toda operación crítica (contratación, activación, suspensión, reactivación, cancelación, renovación) SHALL registrarse en `AuditLogs` (`IAuditService`). Además, un proceso programado periódico (`MembershipExpirationWorker`) SHALL, de forma idempotente y segura ante ejecuciones simultáneas: reactivar las membresías cuya ventana de suspensión finalizó; transicionar a `EXPIRED` las membresías vencidas (`EndDate < Today`, exceptuando las suspendidas con extensión pactada) cerrando el saldo remanente en `MembershipCredits` con movimiento `Expiration`; cancelar las `PENDING` impagas fuera de plazo; y emitir notificaciones internas de vencimiento próximo (`MembershipExpiring`) y vencimiento efectivo (`MembershipExpired`) hacia `Notifications`, sin duplicar efectos ni envíos ante ejecuciones repetidas. El aviso de vencimiento próximo no SHALL emitirse para membresías cuya duración total no supera la ventana de preaviso ni para las que ya tienen una renovación no cancelada.

#### Scenario: Ejecución repetida del proceso de vencimientos y alertas
- **WHEN** el proceso programado `MembershipExpirationWorker` se ejecuta dos veces seguidas en el mismo día
- **THEN** en la primera ejecución transiciona las membresías vencidas a `EXPIRED`, registra el vencimiento de créditos remanentes y crea las notificaciones correspondientes, y en la segunda ejecución no genera movimientos ni notificaciones duplicadas

#### Scenario: Sin aviso de vencimiento para pases cortos o ya renovados
- **WHEN** el proceso programado evalúa un pase de 1 día recién contratado y una membresía mensual que vence en 3 días pero ya tiene una renovación contratada
- **THEN** no se genera la notificación `MembershipExpiring` para ninguna de las dos

#### Scenario: Edición con versión desactualizada
- **WHEN** dos operadores cargan la misma membresía y el segundo envía una suspensión o edición de notas con la `Version` anterior al cambio del primero
- **THEN** el sistema rechaza la segunda operación con `409 Conflict`

### Requirement: Control de acceso a la configuración de planes de membresía
La interfaz web SHALL exponer las acciones de creación, edición, desactivación y reactivación de Planes de Membresía exclusivamente a usuarios con rol `Administrator` o `Secretary`. Los alumnos y visitantes solo SHALL visualizar los planes activos.

#### Scenario: Restricción de herramientas de planes para usuarios comunes
- **WHEN** un alumno o usuario no autenticado navega a `/planes`
- **THEN** la interfaz muestra solo los planes activos, sin botones para crear, editar ni cambiar el estado de planes

#### Scenario: Visualización administrativa en el catálogo de planes
- **WHEN** un usuario con rol `Administrator` o `Secretary` visualiza `/planes`
- **THEN** la interfaz lista planes activos e inactivos (los inactivos marcados visualmente), expone la acción `+ Nuevo Plan` y opciones para editar y desactivar o reactivar cada plan

### Requirement: Configuración interactiva de Planes de Membresía (MembershipPlans)
La aplicación SHALL proveer una interfaz de administración para listar, dar de alta y actualizar planes consumiendo `/api/membershipplans`. Dado que el servidor solo valida el nombre, el formulario SHALL exigir: nombre no vacío, precio mayor a 0, duración de al menos 1 día, al menos 1 crédito cuando el plan no es ilimitado, y al menos una actividad seleccionada cuando el plan no aplica a todas las actividades. Un plan ilimitado SHALL enviarse con `credits = 0` y un plan que aplica a todas las actividades SHALL enviarse con `allowedActivityIds` vacío. El campo de ingreso del precio y todas las vistas donde se exhiban importes de planes de membresía o saldos de membresías (incluyendo inicio `/`, catálogo público `/planes`, panel administrativo de planes y detalle de membresía en el portal del alumno) SHALL mostrar y formatear los importes monetarios con exactamente 2 decimales, coma (`,`) como separador de decimales y punto (`.`) como separador de miles (ej. `35.000,00`), enviando al backend el valor numérico decimal equivalente.

#### Scenario: Alta de plan ilimitado por período
- **WHEN** el personal autorizado define el plan "Pase Libre Full", ingresa el precio `35.000,00`, duración de 30 días, marca ilimitado y "aplica a todas las actividades"
- **THEN** el sistema envía `POST /api/membershipplans` con `price = 35000`, `isUnlimited = true`, `credits = 0`, `appliesToAllActivities = true` y el plan queda disponible para contratación

#### Scenario: Alta de plan por paquete de créditos con actividades restringidas
- **WHEN** el personal autorizado crea "Bono 10 Clases Musculación", ingresa el precio `20.000,00`, duración de 60 días, 10 créditos, y selecciona únicamente la actividad "Musculación"
- **THEN** el sistema envía `POST /api/membershipplans` con `price = 20000`, `appliesToAllActivities = false` y `allowedActivityIds` conteniendo solo el identificador de "Musculación"

#### Scenario: Formato de importe en el ingreso y edición del precio del plan
- **WHEN** el personal autorizado ingresa un importe en el campo de precio del formulario de plan (por ejemplo `35000` o `35000,5`) o abre el formulario para editar un plan con `price = 35000`
- **THEN** el campo formatea los miles con punto (`.`) y los decimales con coma (`,`), mostrando `35.000,00` (o `35.000,50` al completar decimales) mientras mantiene el valor numérico `35000` (o `35000.5`) en el control del formulario

#### Scenario: Visualización de precios de planes y saldos con formato monetario
- **WHEN** se visualizan los precios de planes en la portada (`/`), en `/planes` o en la administración de catálogo, o el saldo pendiente de una membresía en el portal del alumno
- **THEN** cada importe monetario se muestra con 2 decimales, coma como separador decimal y punto como separador de miles (por ejemplo `$35.000,00` y `$15.000,00`)

#### Scenario: Validaciones de cliente del plan
- **WHEN** el usuario intenta guardar un plan no ilimitado con 0 créditos, o un plan restringido sin actividades seleccionadas, o con precio 0 (`0,00`)
- **THEN** el formulario muestra el error correspondiente e impide el envío

#### Scenario: Desactivación de un plan obsoleto
- **WHEN** el personal autorizado desactiva un plan
- **THEN** el sistema envía `PUT /api/membershipplans/{id}` con `isActive = false`; el plan deja de mostrarse a alumnos y visitantes en `/planes`, y las membresías ya contratadas con ese plan continúan vigentes hasta su expiración

#### Scenario: Reactivación de un plan
- **WHEN** el personal autorizado reactiva un plan inactivo
- **THEN** el sistema envía `PUT /api/membershipplans/{id}` con `isActive = true` y el plan vuelve a mostrarse en el catálogo público

### Requirement: Asignación interactiva de membresías a alumnos desde la interfaz de gestión
La interfaz web de administración SHALL permitir a los usuarios con rol de personal administrativo (`Administrator` o `Secretary`) consultar el historial de membresías y realizar la asignación directa e inmediata de un plan de membresía a un alumno desde su ficha de detalle (`/portal/personas/:id`), consumiendo el servicio `POST /api/memberships/assign`.

#### Scenario: Visualización de membresías en la ficha de la persona
- **WHEN** un usuario administrativo o de secretaría accede a la ficha de una persona en `/portal/personas/:id`
- **THEN** la interfaz consulta las membresías del alumno a través de `GET /api/memberships/student/{studentId}` y muestra la sección de membresías con el estado, plan contratado, rango de fechas y créditos disponibles

#### Scenario: Apertura del modal de asignación y selección de plan
- **WHEN** el operador hace clic en "+ Asignar Membresía" para un alumno en estado `ACTIVA`
- **THEN** se abre un modal que lista los planes de membresía activos obtenidos de `GET /api/membershipplans?onlyActive=true`, mostrando para cada plan su precio con formato monetario (`$XX.XXX,XX`), duración en días, tipo de pase (créditos o ilimitado) y actividades permitidas, con la fecha de inicio precargada en la fecha actual

#### Scenario: Asignación exitosa con activación inmediata
- **WHEN** el operador selecciona un plan activo, confirma la fecha de inicio y presiona "Confirmar Asignación"
- **THEN** la aplicación envía `POST /api/memberships/assign` con el `studentId`, `membershipPlanId` y `startDate`, cierra el modal al recibir respuesta exitosa (`200 OK`), muestra un mensaje de éxito y actualiza de inmediato el listado de membresías reflejando la membresía en estado `ACTIVE`

#### Scenario: Restricción de asignación a personas sin rol de Alumno o inactivas
- **WHEN** se visualiza la ficha de una persona que no posee el rol `Student` o cuyo estado personal es distinto de `ACTIVA` (ej. `INACTIVA`, `BLOQUEADA` o `FALLECIDA`)
- **THEN** el botón de asignar membresía se presenta deshabilitado con un mensaje aclaratorio o el sistema rechaza la operación indicando la causa impeditiva sin generar movimientos huérfanos

#### Scenario: Validación de fecha de inicio
- **WHEN** el operador intenta ingresar una fecha de inicio anterior a la fecha actual
- **THEN** el selector de fecha del modal impide seleccionar fechas pasadas (`min` fijado en la fecha de hoy) y bloquea la confirmación de la asignación

