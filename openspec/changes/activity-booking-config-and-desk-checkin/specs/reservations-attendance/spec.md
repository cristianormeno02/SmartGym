## MODIFIED Requirements

### Requirement: Independencia entre Reserva y Asistencia
El sistema SHALL modelar la asistencia como un hecho operativo independiente de la creación de la reserva, permitiendo que el personal o el instructor asignado registre la presencia efectiva (`ATTENDED`) o la ausencia (`NO_SHOW`) de un alumno con reserva `CONFIRMED`, o que el personal registre asistencia sin reserva previa mediante el Check-in Directo en mostrador cuando las reglas del gimnasio y el cupo lo permitan. Para un alumno con reserva, el hecho de asistencia SHALL persistirse en la propia reserva (`Status`, `AttendedAtUtc`, `AttendanceSource`, `CheckedInByUserId`), que es su única fuente de verdad; esos datos sólo SHALL modificarse mediante las operaciones de asistencia (registro de presencia, registro de ausencia, Check-in Directo y anulación del presente) y nunca mediante las operaciones de reserva.
- La presencia SHALL poder registrarse desde `CheckInOpensMinutesBeforeStart` (por defecto 30) minutos antes del inicio y hasta `AttendanceRegistrationHoursAfterEnd` (por defecto 24) horas después del fin de la clase.
- La ausencia SHALL poder registrarse desde el fin de la clase y dentro del mismo plazo posterior.
- Ambas operaciones SHALL exigir que la clase no esté suspendida, cancelada ni reprogramada, y SHALL ejecutarse bajo el bloqueo de la clase para que no compitan con cancelaciones ni promociones de lista de espera.
- Un instructor sólo SHALL registrar asistencia en las clases donde es instructor programado o efectivo; para una clase ajena la operación se rechaza como recurso no encontrado.
- `NO_SHOW` SHALL ser un estado terminal. `ATTENDED` SHALL ser terminal para toda operación excepto la anulación del presente, reservada al personal.

#### Scenario: Asistencia de alumno con reserva
- **WHEN** el secretario o el instructor asignado registra la presencia de un alumno con reserva `CONFIRMED` dentro del plazo de registro
- **THEN** la reserva pasa a `ATTENDED`, se registran `AttendedAtUtc`, `AttendanceSource`, `CheckedInByUserId` y la transición en `ReservationStatusChanges`, y se incrementa `AttendedCount` de la clase

#### Scenario: Registro de No-Show
- **WHEN** finalizó una clase y el personal o el instructor asignado registra la inasistencia de un alumno con reserva `CONFIRMED`
- **THEN** la reserva pasa a `NO_SHOW` registrando la transición en `ReservationStatusChanges` y el derecho de acceso consumido no se restituye

#### Scenario: Registro de asistencia fuera de plazo
- **WHEN** se intenta registrar la presencia de un alumno 2 horas antes del inicio de la clase, o su ausencia antes de que la clase termine
- **THEN** el sistema rechaza la operación con `409 Conflict`

#### Scenario: Instructor registra asistencia en una clase ajena
- **WHEN** un instructor sin rol de personal intenta registrar asistencia en una clase donde no es instructor programado ni efectivo
- **THEN** la operación es rechazada como recurso no encontrado

## ADDED Requirements

### Requirement: Check-in Directo en Mostrador (Drop-in)
El sistema SHALL exponer `POST /api/reservations/desk-drop-in`, restringido a usuarios `Secretary` o `Administrator`, para registrar en una única transacción atómica el ingreso presencial de un alumno sin reserva previa en una clase, tanto de actividades con `RequiresBooking == false` como de actividades con reserva previa que tengan cupo. La solicitud SHALL identificar la clase (`ClassSessionId`) y el alumno (`StudentId`) y SHALL admitir la cabecera `Idempotency-Key` con la misma semántica que la creación de reservas.

El sistema SHALL validar, bajo el bloqueo de la clase y luego de las membresías del alumno (orden único de bloqueos):
1. La clase existe, está activa, su estado persistido es `Scheduled` u `Open` (no `Suspended`, `Cancelled` ni `Rescheduled`) y su actividad está `ACTIVE`; si no, `409 Conflict` (`CLASS_NOT_OPEN`).
2. `UtcNow` está entre `StartsAtUtc - CheckInOpensMinutesBeforeStart` (inclusive) y `EndsAtUtc` (exclusive); si no, `409 Conflict` (`DROP_IN_WINDOW_CLOSED`).
3. El alumno cumple las mismas condiciones de persona que exige la creación de reservas (estado `ACTIVA`, rol `Student` activo, rango etario y condiciones documentales).
4. El alumno no tiene en la clase una reserva activa (`PENDING`, `CONFIRMED` o `WAITLISTED`), lo que se rechaza con `409 Conflict` (`ALREADY_RESERVED`), ni una reserva `ATTENDED`, lo que se rechaza con `409 Conflict` (`ALREADY_CHECKED_IN`).
5. Hay cupo (`ReservedCount < Capacity`); si no, `409 Conflict` (`CLASS_FULL`). El drop-in no ingresa en lista de espera.

Luego SHALL resolver el acceso según uno de dos modos:
- **Con derecho de acceso vigente** (`ChargeSingleClassTicket = false`): el derecho se resuelve y consume con las mismas reglas de "Resolución del derecho de acceso" (membresía con crédito, membresía ilimitada con movimiento de cantidad 0, crédito compensatorio o pase individual existente). Sin derecho válido se rechaza con `409 Conflict` (`NO_ACCESS_RIGHT`) detallando los motivos.
- **Con cobro de pase individual** (`ChargeSingleClassTicket = true`): la solicitud SHALL indicar `SingleClassPlanId` y un `PaymentMethod` manual (`Cash`, `BankTransfer`, `CreditCard`, `DebitCard` u `Other`). El plan SHALL ser una opción de pase individual elegible para la clase según `memberships`; si no lo es, `409 Conflict` (`SINGLE_CLASS_PLAN_NOT_AVAILABLE`). El importe SHALL tomarse del precio del plan, nunca de la solicitud. El sistema SHALL registrar un pago `Completed` vinculado al alumno y a la clase, emitir el pase `SingleClassTicket` para esa clase y vincular ambos a la reserva (`PaymentId`, `SingleClassTicketMovementId`, `AccessType = SingleClassTicket`).

En ambos modos el sistema SHALL crear la reserva con `Origin = StaffDesk`, `IsDropIn = true`, `Status = ATTENDED`, `AttendedAtUtc`, `AttendanceSource = DeskDropIn`, `CheckedInByUserId` y `CreatedByUserId` del operador; registrar la transición en `ReservationStatusChanges`; incrementar `ReservedCount` y `AttendedCount`, y registrar `AuditLogs`, todo en la misma transacción. Cualquier fallo SHALL revertir pago, movimiento de créditos y reserva sin efectos parciales.

#### Scenario: Check-in directo de alumno con membresía activa y créditos
- **WHEN** un alumno con membresía activa y 3 créditos se presenta 10 minutos antes del inicio de la clase y el secretario registra su ingreso directo
- **THEN** el sistema crea la reserva con `Origin = StaffDesk`, `IsDropIn = true`, `Status = ATTENDED` y `AttendanceSource = DeskDropIn`, descuenta 1 crédito en el ledger vinculado a la reserva (saldo 2) e incrementa en 1 tanto `ReservedCount` como `AttendedCount`

#### Scenario: Check-in directo con membresía ilimitada
- **WHEN** un alumno con membresía ilimitada elegible ingresa por drop-in
- **THEN** la reserva queda `ATTENDED` con `AccessType = UnlimitedMembership` y el ledger registra un movimiento de cantidad 0 sin alterar el saldo

#### Scenario: Check-in directo con cobro de pase individual
- **WHEN** un alumno sin créditos se presenta en mostrador y el secretario elige el plan "Clase Suelta" ($5.000) y el medio `Cash`
- **THEN** el sistema registra un pago `Completed` de 5000 vinculado al alumno y a la clase, emite el `SingleClassTicket` y crea la reserva `ATTENDED` con `AccessType = SingleClassTicket`, `AttendanceSource = DeskDropIn`, `PaymentId` y `SingleClassTicketMovementId`

#### Scenario: Importe enviado por el cliente ignorado
- **WHEN** la solicitud de cobro in situ incluye un plan elegible cuyo precio es 5000
- **THEN** el pago se registra por 5000, independientemente de cualquier otro dato enviado por el cliente

#### Scenario: Plan no elegible como pase individual
- **WHEN** el secretario envía un `SingleClassPlanId` correspondiente a un plan mensual de 8 créditos sin `IsSingleClassPass`, o a un plan que no permite la actividad de la clase
- **THEN** el sistema rechaza con `409 Conflict` (`SINGLE_CLASS_PLAN_NOT_AVAILABLE`) sin registrar pago ni reserva

#### Scenario: Rechazo de Check-in directo por cupo colmado
- **WHEN** un alumno se presenta para ingresar a una clase con `ReservedCount == Capacity`
- **THEN** la operación es rechazada con `409 Conflict` (`CLASS_FULL`) y no se registra pago, consumo ni reserva

#### Scenario: Check-in fuera de la ventana de ingreso
- **WHEN** el secretario intenta registrar un drop-in 45 minutos antes del inicio (con `CheckInOpensMinutesBeforeStart = 30`) o una vez finalizada la clase
- **THEN** el sistema rechaza con `409 Conflict` (`DROP_IN_WINDOW_CLOSED`)

#### Scenario: Check-in durante la clase en curso
- **WHEN** la clase comenzó hace 20 minutos y aún no terminó, y el secretario registra el drop-in de un alumno elegible
- **THEN** la reserva se crea en `ATTENDED`

#### Scenario: Alumno ya registrado en la clase
- **WHEN** el secretario intenta registrar un drop-in para un alumno que ya tiene una reserva `ATTENDED` en la clase, o una reserva `CONFIRMED`
- **THEN** el sistema rechaza con `409 Conflict` (`ALREADY_CHECKED_IN` o `ALREADY_RESERVED`, respectivamente) sin consumir un segundo derecho

#### Scenario: Check-in en clase suspendida o cancelada
- **WHEN** el secretario intenta registrar un drop-in en una clase `Suspended`, `Cancelled` o `Rescheduled`
- **THEN** el sistema rechaza con `409 Conflict` (`CLASS_NOT_OPEN`)

#### Scenario: Reversión ante fallo del pago
- **WHEN** durante un cobro in situ falla el registro del pago o la emisión del pase
- **THEN** la transacción completa se revierte: no queda pago, movimiento ni reserva, y los contadores de la clase no cambian

#### Scenario: Reintento con la misma clave de idempotencia
- **WHEN** recepción reenvía el cobro in situ con la misma `Idempotency-Key` para el mismo alumno y la misma clase
- **THEN** el sistema devuelve la reserva ya creada con `200 OK`, sin un segundo pago ni un segundo consumo

#### Scenario: Instructor intenta registrar un drop-in
- **WHEN** un usuario con rol `Instructor` sin rol de personal invoca `POST /api/reservations/desk-drop-in`
- **THEN** el backend responde `403 Forbidden`

### Requirement: Anulación del presente
El sistema SHALL exponer `POST /api/reservations/{id}/revoke-attendance`, restringido a usuarios `Secretary` o `Administrator`, para anular una asistencia registrada (`ATTENDED`), ya sea por error del operador (`Reason = StaffError`) o porque el alumno decidió no tomar la clase (`Reason = StudentWithdrew`). La observación (`Notes`, máx. 500 caracteres) SHALL ser obligatoria.
- Sólo SHALL aplicarse a reservas `ATTENDED`; en otro estado se rechaza con `409 Conflict` (`INVALID_STATE_TRANSITION`).
- `StudentWithdrew` SHALL admitirse sólo mientras `UtcNow < EndsAtUtc`. `StaffError` SHALL admitirse hasta `EndsAtUtc + AttendanceRegistrationHoursAfterEnd`. Fuera de plazo se rechaza con `409 Conflict` (`ATTENDANCE_REVOCATION_WINDOW_CLOSED`).
- Efectos según el tipo de reserva:
  - **Reserva de drop-in** (`IsDropIn = true`), con cualquier motivo: pasa a `CANCELLED` con `CancellationSource = AttendanceRevoked` e `IsLateCancellation = false`, libera el cupo y restituye el derecho de acceso.
  - **Reserva previa con `StaffError`**: vuelve a `CONFIRMED`, se limpian `AttendedAtUtc`, `AttendanceSource` y `CheckedInByUserId`, conserva su cupo y la reserva sigue reteniendo su derecho de acceso.
  - **Reserva previa con `StudentWithdrew`**: pasa a `CANCELLED` con `CancellationSource = AttendanceRevoked` e `IsLateCancellation = false`, libera el cupo y restituye el derecho de acceso.
- En todos los casos SHALL decrementarse `AttendedCount` y SHALL registrarse `AttendanceRevokedAtUtc`, `AttendanceRevokedByUserId`, `AttendanceRevocationReason` y `AttendanceRevocationNotes`, la transición en `ReservationStatusChanges` y `AuditLogs`, en la misma transacción y bajo el orden único de bloqueos.
- Restituir el derecho SHALL equivaler a lo que ocurre en una cancelación anticipada: devolución del crédito mediante el ledger (a lo sumo una devolución por reserva; con membresía ilimitada, movimiento de cantidad 0), liberación del pase individual o del crédito compensatorio, o anulación del canje promocional.
- La anulación no SHALL promover la lista de espera ni reembolsar automáticamente pagos. Si la reserva tiene `PaymentId`, la respuesta SHALL informarlo para que el reembolso se gestione mediante las devoluciones de `payments`.
- La anulación no SHALL notificar al alumno.

#### Scenario: Anulación por error de un drop-in con crédito
- **WHEN** el secretario registró por error el drop-in de un alumno con membresía de créditos y anula el presente con `Reason = StaffError` y una observación
- **THEN** la reserva pasa a `CANCELLED` (`AttendanceRevoked`), se devuelve el crédito en el ledger, `ReservedCount` y `AttendedCount` disminuyen en 1, y la anulación queda en `ReservationStatusChanges` y `AuditLogs` con operador, motivo y observación

#### Scenario: Alumno desiste después del presente con reserva previa
- **WHEN** un alumno con reserva previa `ATTENDED` decide no tomar la clase antes de que termine y el secretario anula con `Reason = StudentWithdrew`
- **THEN** la reserva pasa a `CANCELLED` (`AttendanceRevoked`, `IsLateCancellation = false`), se restituye el derecho de acceso y se libera el cupo

#### Scenario: Error del operador sobre una reserva previa
- **WHEN** el secretario marcó presente por error a un alumno con reserva `CONFIRMED` y anula con `Reason = StaffError`
- **THEN** la reserva vuelve a `CONFIRMED` sin datos de asistencia, conserva su cupo y su derecho consumido, y `AttendedCount` disminuye en 1

#### Scenario: Anulación de un drop-in con pase cobrado
- **WHEN** se anula el presente de un drop-in con `AccessType = SingleClassTicket` y pago `Completed`
- **THEN** la reserva pasa a `CANCELLED`, el pase queda liberado, el pago permanece `Completed` y la respuesta informa el `PaymentId` para que se gestione el reembolso desde `payments`

#### Scenario: Anulación fuera de plazo
- **WHEN** se intenta anular con `StudentWithdrew` una vez terminada la clase, o con `StaffError` pasadas `AttendanceRegistrationHoursAfterEnd` horas desde el fin
- **THEN** el sistema rechaza con `409 Conflict` (`ATTENDANCE_REVOCATION_WINDOW_CLOSED`)

#### Scenario: Anulación sin observación o sobre reserva no asistida
- **WHEN** se solicita anular sin `Notes`, o sobre una reserva `CONFIRMED` o `NO_SHOW`
- **THEN** el sistema responde `400 Bad Request` o `409 Conflict` (`INVALID_STATE_TRANSITION`), respectivamente, sin modificar la reserva

#### Scenario: Instructor intenta anular un presente
- **WHEN** un usuario con rol `Instructor` sin rol de personal invoca la anulación
- **THEN** el backend responde `403 Forbidden`

### Requirement: Ficha de elegibilidad en mostrador
El sistema SHALL exponer `GET /api/reservations/desk-eligibility?classSessionId={id}&studentId={id}`, restringido a `Secretary` o `Administrator` y sin efectos sobre el estado, que informe para la combinación alumno–clase:
- Datos del alumno (nombre completo, documento, foto y estado de la persona).
- Membresías vigentes con nombre del plan, créditos disponibles o condición de ilimitada, y vencimiento.
- Si posee un derecho de acceso utilizable para la clase, el tipo sugerido y, cuando no lo posee, los motivos de inelegibilidad con código y mensaje.
- Si ya tiene una reserva en la clase, con su identificador y estado.
- Si la clase está dentro de la ventana de drop-in, si tiene cupo y si su actividad requiere reserva.
- Las acciones habilitadas: `CanCheckIn`, `CanChargeSingleClass` y `CanEnrollReservation` (clase futura `Open`, actividad con `RequiresBooking = true` y alumno elegible).
- Las opciones de pase individual elegibles para la clase (`PlanId`, nombre y precio).

La evaluación SHALL usar las mismas reglas que el Check-in Directo y la creación de reservas, y el backend SHALL revalidarlas al ejecutar cada acción.

#### Scenario: Alumno sin créditos con dos planes de pase disponibles
- **WHEN** el secretario consulta la ficha de un alumno con créditos agotados para una clase de "Funcional", y existen los planes elegibles "Clase Suelta" ($5.000) y "Clase Suelta Estudiante" ($3.500)
- **THEN** la ficha informa `CanCheckIn = false` con el motivo de inelegibilidad, `CanChargeSingleClass = true` y ambas opciones con su precio

#### Scenario: Alumno con reserva confirmada
- **WHEN** el secretario consulta la ficha de un alumno que ya tiene una reserva `CONFIRMED` en la clase
- **THEN** la ficha informa la reserva existente, y la acción "Dar Presente" de la interfaz registra la asistencia sobre esa reserva en lugar de crear un drop-in

#### Scenario: Sin planes de pase configurados
- **WHEN** no existe ningún plan elegible como pase individual para la actividad de la clase
- **THEN** la ficha informa `CanChargeSingleClass = false` y una lista de opciones vacía

### Requirement: Búsqueda ágil y selección de alumnos en mostrador
En el portal de recepción (`StaffPortalComponent`), el sistema SHALL prohibir el ingreso manual o pegado de identificadores técnicos (`Guid`) para seleccionar alumnos o membresías. En su lugar SHALL proveer un buscador con autocompletado que consulte `GET /api/people` filtrando por rol `Student`, y que permita localizar alumnos tipeando fragmentos de:
- Documento (**DNI**).
- **Apellido**.
- **Nombre**.
- **Correo electrónico**.

El autocompletado SHALL consultar con debounce de 300 ms a partir de 2 caracteres, mostrar como máximo 10 sugerencias con foto, nombre completo, DNI y estado de la persona, y permitir la navegación con teclado. Al seleccionar un alumno con una clase seleccionada, SHALL cargar la ficha de elegibilidad y ofrecer las acciones habilitadas con un solo clic: "Dar Presente", "Cobrar Pase y Dar Presente" (con selección del plan cuando haya más de una opción, preseleccionado cuando haya una sola, y del medio de pago) e "Inscribir Reserva". La selección de membresía para otorgar créditos compensatorios SHALL hacerse eligiendo entre las membresías del alumno seleccionado.

La nómina de la clase SHALL ofrecer, en cada reserva `ATTENDED`, la acción "Anular presente" con motivo y observación obligatoria, y SHALL refrescarse junto con los contadores tras cada acción sin recargar la página. Las acciones de mostrador (búsqueda, drop-in, cobro y anulación) SHALL ocultarse para usuarios `Instructor` sin rol de personal.

#### Scenario: Localización inmediata de alumno por DNI en mostrador
- **WHEN** el secretario ingresa los dígitos del DNI de un alumno en el buscador de recepción
- **THEN** el sistema muestra la sugerencia coincidente, y al seleccionarla despliega su ficha con el estado de su membresía y las acciones habilitadas ("Dar Presente", "Cobrar Pase y Dar Presente", "Inscribir Reserva")

#### Scenario: Búsqueda por apellido parcial
- **WHEN** el secretario tipea "gonz"
- **THEN** después de 300 ms sin escribir se muestran hasta 10 alumnos cuyo nombre, apellido, documento o email coinciden

#### Scenario: Ausencia de campos GUID
- **WHEN** el personal abre el portal de recepción
- **THEN** no existe ningún campo que solicite el identificador técnico de un alumno o de una membresía
