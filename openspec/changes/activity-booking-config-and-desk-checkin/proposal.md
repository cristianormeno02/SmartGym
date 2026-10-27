# Proposal: Configuración de Reserva por Actividad y Check-in Rápido en Mostrador

## Why

Actualmente, el sistema SmartGym asume de forma uniforme que todas las actividades y clases requieren reserva previa obligatoria por parte del alumno para poder ingresar, y que el personal de recepción gestiona inscripciones ingresando manualmente el identificador interno del alumno (`Guid` / UUID).

Esta dinámica presenta fricciones críticas en la operación diaria del gimnasio:

1. **Inflexibilidad operativa en modelos de reserva**:
   - En muchos gimnasios, disciplinas como *Musculación*, *Sala Libre*, *Spinning libre* o ciertas clases masivas funcionan bajo la modalidad libre o de asistencia directa (*Drop-in*), sin requerir ni permitir que el alumno bloquee cupos con días de anticipación.
   - En esas disciplinas, el alumno asiste presencialmente y se registra en mostrador en el momento de ingresar.
   - Si la actividad no requiere reserva, el portal del alumno no debería ofrecer reserva previa online, sino informar que el acceso es por orden de llegada con membresía o pase vigente.

2. **Fricción extrema en mostrador al utilizar identificadores GUID**:
   - Para registrar a un alumno en el portal de recepción (`StaffPortalComponent`), la interfaz exige copiar y pegar el `Guid` del alumno (y, para créditos compensatorios, el `Guid` de la membresía).
   - El GUID es un dato interno imposible de recordar, solicitar o dictar verbalmente.
   - En el horario pico (5 a 10 minutos previos al inicio de una clase) se forman filas en recepción. El personal necesita localizar instantáneamente al alumno por **DNI, Nombre, Apellido o Correo Electrónico**.

3. **Inexistencia de un flujo unificado de ingreso ágil (Check-in Directo, Cobro In Situ y Corrección)**:
   - **Caso A (Alumno con derecho de acceso vigente)**: buscar por DNI o apellido, ver su saldo o membresía ilimitada y presionar **"Dar Presente"**, registrando asistencia y consumo en un único paso.
   - **Caso B (Alumno sin membresía o con créditos agotados)**: elegir el **plan de pase de clase individual** configurado (si hay más de uno, elegir entre las opciones), registrar el pago y en el mismo acto dar el **Presente**.
   - **Caso C (Corrección)**: el secretario puede haberse equivocado de alumno, o el alumno puede decidir no tomar la clase después de haber dado el presente. Hoy `ATTENDED` es terminal y no hay forma de revertirlo.

## What Changes

- **Configuración de Reserva por Actividad (`RequiresBooking`)**:
  - La entidad `Activity` incorpora `RequiresBooking` (por defecto `true`), expuesto en DTOs administrativos y públicos.
  - Cuando `RequiresBooking == false`:
    - No se admiten reservas previas ni lista de espera para sus clases, ni por autoservicio ni por mostrador (`409 BOOKING_NOT_REQUIRED`).
    - `GET /api/reservations/available-classes` informa `RequiresBooking = false`, `CanReserve = false` y `CanJoinWaitlist = false` con el motivo `BOOKING_NOT_REQUIRED`; el portal del alumno muestra la clase como informativa ("Acceso libre por orden de llegada").
    - El ingreso se registra exclusivamente por Check-in Directo en mostrador.
  - Las reservas ya existentes no se alteran al cambiar el valor.
  - El formulario de actividades incorpora el control *"¿Requiere reserva previa online?"*.

- **Planes habilitados como Pase de Clase Individual**:
  - `MembershipPlan` incorpora `IsSingleClassPass` (por defecto `false`).
  - Un plan es **opción de pase individual** para una clase si está activo, permite la actividad de la clase, tiene precio mayor a 0 y además cumple `IsSingleClassPass == true` **o** es un plan no ilimitado con exactamente 1 crédito.
  - El precio del pase lo determina el backend a partir del plan elegido; el cliente nunca envía el importe.
  - Si existe más de una opción, recepción elige entre ellas; si existe una sola, se preselecciona; si no existe ninguna, el cobro in situ no está disponible.

- **Buscador de Alumnos en Mostrador y Ficha de Elegibilidad**:
  - Se elimina todo ingreso de GUID en `StaffPortalComponent` (alumno y membresía para créditos compensatorios).
  - Autocompletado (debounce 300 ms, máx. 10 resultados) sobre `GET /api/people?role=Student&search=...` por DNI, apellido, nombre o email.
  - Al seleccionar un alumno, se consulta `GET /api/reservations/desk-eligibility` para la clase elegida: derecho de acceso utilizable, membresías vigentes con saldo y vencimiento, si ya está registrado en la clase, opciones de pase individual con su precio y acciones disponibles.

- **Check-in Directo en Mostrador (`POST /api/reservations/desk-drop-in`)**:
  - Ventana: desde `CheckInOpensMinutesBeforeStart` minutos antes del inicio **hasta el fin de la clase**.
  - Clases `Scheduled` u `Open` (no suspendidas, canceladas ni reprogramadas), con cupo disponible, para actividades con o sin reserva previa.
  - **"Dar Presente"**: resuelve el derecho de acceso con el resolver existente, lo consume y crea la reserva `ATTENDED` (`Origin = StaffDesk`, `IsDropIn = true`, `AttendanceSource = DeskDropIn`) en una transacción atómica.
  - **"Cobrar Pase y Dar Presente"**: con un plan de pase individual elegido y un medio de pago manual, registra el pago `Completed`, emite el `SingleClassTicket` y crea la reserva `ATTENDED` con `AccessType = SingleClassTicket`, todo en la misma transacción.
  - Admite `Idempotency-Key` para evitar doble cobro por doble clic.

- **Anulación del Presente (`POST /api/reservations/{id}/revoke-attendance`)**:
  - Motivo obligatorio: `StaffError` (el secretario se equivocó) o `StudentWithdrew` (el alumno decide no tomar la clase), más una observación.
  - Reservas de drop-in: pasan a `CANCELLED`, liberan el cupo y restituyen el derecho de acceso (devolución del crédito o liberación del pase).
  - Reservas con reserva previa: `StaffError` las devuelve a `CONFIRMED`; `StudentWithdrew` las cancela restituyendo el derecho.
  - Un pase individual cobrado no se reembolsa automáticamente: la devolución del dinero se gestiona con el flujo de reembolsos de `payments`.

- **Inscripción anticipada en mostrador**: el botón "Inscribir Reserva" se mantiene sólo para clases futuras de actividades con `RequiresBooking = true`.

- **Auditoría**: cada drop-in, cobro in situ y anulación registra el operador, la fecha/hora UTC y el detalle en `AuditLogs` y en `ReservationStatusChanges`.

## Capabilities

### New Capabilities

- Ninguna. Se extienden las capacidades existentes.

### Modified Capabilities

- `activities-catalog`: nuevo requisito de modalidad de reserva por actividad (`RequiresBooking`).
- `reservations`: rechazo de reservas y lista de espera en actividades sin reserva previa, exposición en clases disponibles, y máquina de estados que admite la anulación del presente.
- `reservations-attendance`: Check-in Directo en mostrador, ficha de elegibilidad, anulación del presente y búsqueda de alumnos sin GUID.
- `memberships`: planes habilitados como pase de clase individual (`IsSingleClassPass`) y su configuración.
- `memberships-credits`: el pase individual se libera también al anularse el presente.

## Impact

- **Backend**:
  - Migraciones: `Activities.RequiresBooking BOOLEAN NOT NULL DEFAULT TRUE`; `MembershipPlans.IsSingleClassPass BOOLEAN NOT NULL DEFAULT FALSE`; en `Reservations`, `IsDropIn` y los campos de anulación del presente.
  - Enums: `AttendanceSource.DeskDropIn = 4` (el valor 3 ya es `SelfCheckIn`), `ReservationCancellationSource.AttendanceRevoked`, nuevo `AttendanceRevocationReason`.
  - `Activity`, `MembershipPlan`, DTOs y servicios de actividades y planes.
  - `ReservationService`: validación de `RequiresBooking` en `CreateReservationAsync` y `JoinWaitlistAsync`, `GetDeskEligibilityAsync`, `CheckInDeskDropInAsync` y `RevokeAttendanceAsync`. Se coordinan `ReservationAccessResolver`, `ICreditLedgerService` y `IPaymentService` dentro de una única transacción.
  - `ReservationsController`: `GET desk-eligibility`, `POST desk-drop-in`, `POST {id}/revoke-attendance` con política `RequireStaff`.
- **Frontend**:
  - `ActivityFormModalComponent` y vistas de catálogo: control e insignia de modalidad.
  - Formulario de planes: control "Disponible como pase de clase individual".
  - `StaffPortalComponent`: buscador de alumnos, ficha de elegibilidad, acciones "Dar Presente", "Cobrar Pase y Dar Presente" (con selector de plan y medio de pago), "Inscribir Reserva" y "Anular presente" en la nómina; reemplazo del GUID de membresía por un selector.
  - `StudentPortalComponent`: insignia informativa y sin botón de reserva para clases que no la requieren.
