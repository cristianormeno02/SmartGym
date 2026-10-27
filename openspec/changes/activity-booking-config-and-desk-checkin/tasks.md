# Tasks: Configuración de Reserva por Actividad y Check-in Rápido en Mostrador

## Fase 1: Dominio y Base de Datos (PostgreSQL + TDD)

- [x] 1.1 **(TDD Red)** Escribir pruebas unitarias en `SmartGym.Domain.UnitTests` para:
  - `Activity`: propiedad `RequiresBooking` (valor por defecto `true` en el constructor y `UpdateDetails`, y mutador `SetRequiresBooking(bool)`).
  - `MembershipPlan`: propiedad `IsSingleClassPass` (valor por defecto `false`) y método de evaluación `IsEligibleAsSingleClassPassFor(Guid activityId)` (`IsActive && Price > 0 && AllowsActivity(activityId) && (IsSingleClassPass || (!IsUnlimited && Credits == 1))`).
  - `Reservation`: fábrica `Reservation.CreateDropInAttended(...)` (`Origin = StaffDesk`, `IsDropIn = true`, `Status = Attended`, `AttendedAtUtc`, `AttendanceSource = DeskDropIn`, transición inicial en `StatusChanges`) y método `RevokeAttendance(AttendanceRevocationReason, string notes, Guid? byUserId, DateTime nowUtc)` que transiciona a `Confirmed` (si `!IsDropIn && StaffError`) o a `Cancelled` (`CancellationSource = AttendanceRevoked`, `IsLateCancellation = false`).
- [x] 1.2 **(TDD Green)** Implementar los cambios en `Activity`, `MembershipPlan`, `Reservation` y las enumeraciones:
  - `AttendanceSource.DeskDropIn = 4` (conservando `SelfCheckIn = 3`).
  - `ReservationCancellationSource.AttendanceRevoked`.
  - Nuevo `AttendanceRevocationReason { StaffError = 1, StudentWithdrew = 2 }`.
- [x] 1.3 Configurar Entity Framework Core y crear la migración para agregar:
  - `Activities.RequiresBooking BOOLEAN NOT NULL DEFAULT TRUE`.
  - `MembershipPlans.IsSingleClassPass BOOLEAN NOT NULL DEFAULT FALSE`.
  - `Reservations.IsDropIn BOOLEAN NOT NULL DEFAULT FALSE`, `AttendanceRevokedAtUtc`, `AttendanceRevokedByUserId`, `AttendanceRevocationReason` y `AttendanceRevocationNotes`.
- [x] 1.4 Ejecutar `dotnet test` sobre `SmartGym.Domain.UnitTests` y verificar que las pruebas de dominio pasen en verde.

## Fase 2: Servicios de Aplicación y APIs REST (.NET 9 + TDD)

- [x] 2.1 Actualizar DTOs y servicios de catálogo y planes:
  - `ActivityListItemDto`, `ActivityDetailDto`, `PublicActivityDto`, `CreateActivityRequest`, `UpdateActivityRequest` y `ActivityService` con `RequiresBooking`.
  - `MembershipPlanDto`, `CreateMembershipPlanRequest`, `UpdateMembershipPlanRequest` y `MembershipPlanService` con `IsSingleClassPass`.
- [x] 2.2 **(TDD Red)** Escribir pruebas de integración en `SmartGym.IntegrationTests` para:
  - Rechazo de `CreateReservationAsync` y `JoinWaitlistAsync` con `409 BOOKING_NOT_REQUIRED` cuando `Activity.RequiresBooking == false` (tanto para alumno como para personal), y preservación de reservas existentes al cambiar `RequiresBooking` de `true` a `false`.
  - `GetAvailableClassesAsync`: incluye `RequiresBooking = false`, `CanReserve = false`, `CanJoinWaitlist = false` y motivo `BOOKING_NOT_REQUIRED`.
  - `GetDeskEligibilityAsync`: ficha de elegibilidad con membresías vigentes, motivos de inelegibilidad, reserva existente, acciones habilitadas y opciones de planes de pase individual elegibles (con 1 crédito o `IsSingleClassPass = true`).
  - `CheckInDeskDropInAsync`:
    - Caso A con membresía de créditos (descuenta 1 crédito y deja `ATTENDED` con `IsDropIn = true` y `AttendanceSource = DeskDropIn`) y con membresía ilimitada (movimiento 0).
    - Caso B con cobro de pase individual (toma el precio del plan, registra `Payment` `Completed`, emite `SingleClassTicket`, crea reserva `ATTENDED` e ignora importes externos).
    - Rechazos: fuera de ventana `[StartsAtUtc - CheckInOpensMinutesBeforeStart, EndsAtUtc)` (`DROP_IN_WINDOW_CLOSED`), cupo lleno (`CLASS_FULL`), alumno ya `CONFIRMED` (`ALREADY_RESERVED`) o ya `ATTENDED` (`ALREADY_CHECKED_IN`), plan no elegible (`SINGLE_CLASS_PLAN_NOT_AVAILABLE`), clase suspendida/cancelada (`CLASS_NOT_OPEN`) y rol `Instructor` sin rol de personal (`403`).
    - Idempotencia con `Idempotency-Key` (reintento devuelve la misma reserva sin doble cobro) y rollback atómico si falla el pago o el ledger.
  - `RevokeAttendanceAsync`:
    - Drop-in con crédito → pasa a `CANCELLED`, devuelve el crédito y decrementa `ReservedCount` y `AttendedCount`.
    - Reserva previa con `StaffError` → vuelve a `CONFIRMED`, conserva el cupo y el derecho consumido, y decrementa `AttendedCount`.
    - Reserva previa con `StudentWithdrew` → pasa a `CANCELLED`, libera cupo y restituye el derecho.
    - Drop-in con pase cobrado → pasa a `CANCELLED`, libera el pase, conserva el pago `Completed` y devuelve `PaymentId`.
    - Rechazos fuera de plazo (`ATTENDANCE_REVOCATION_WINDOW_CLOSED`), sin `Notes` (`400`) o sobre estado distinto de `ATTENDED` (`INVALID_STATE_TRANSITION`).
- [x] 2.3 **(TDD Green)** Implementar en `ReservationErrorCodes`, `ReservationDtos`, `ReservationService` y módulos relacionados:
  - Validación de `RequiresBooking` en `CreateReservationAsync`, `JoinWaitlistAsync` y `GetAvailableClassesAsync`.
  - `GetDeskEligibilityAsync`, `CheckInDeskDropInAsync` y `RevokeAttendanceAsync`, coordinando `ReservationAccessResolver`, `ICreditLedgerService` y `IPaymentService` dentro de la misma transacción y respetando el orden único de bloqueos (`ClassSession` → `Memberships`).
  - Evitar que `NotifyStudentOnReservationLifecycleHandler` envíe notificaciones cuando `CancellationSource == AttendanceRevoked`.
- [x] 2.4 Exponer en `ReservationsController` con política `RequireStaff`:
  - `GET /api/reservations/desk-eligibility`
  - `POST /api/reservations/desk-drop-in`
  - `POST /api/reservations/{id}/revoke-attendance`
- [x] 2.5 Ejecutar `dotnet test` y verificar que todas las pruebas unitarias y de integración de backend pasen en verde.

## Fase 3: Frontend - Catálogo de Actividades y Planes de Membresía (Angular + TDD)

- [x] 3.1 Actualizar los modelos en `frontend/src/app/core/models/gym.models.ts` (`Activity`, `MembershipPlan`, `ReservableClassSession`, `DeskEligibility`, `DeskDropInRequest`, `RevokeAttendanceRequest`) y los métodos en `GymApiService` (`getDeskEligibility`, `deskDropInCheckIn`, `revokeAttendance`).
- [x] 3.2 **(TDD Red → Green)** En `ActivityFormModalComponent`, `activities-management.ts` y `activities.ts`:
  - Agregar pruebas en `activity-form-modal.spec.ts` y `activities-management.spec.ts` para el control `requiresBooking` (inicializado en `true`) y la insignia de modalidad.
  - Implementar el checkbox *"¿Requiere reserva previa online?"* y las insignias *"Con reserva previa"* / *"Acceso libre sin reserva"*.
- [x] 3.3 **(TDD Red → Green)** En la vista de administración de planes de membresía:
  - Agregar prueba e implementar el checkbox *"Disponible como pase de clase individual en recepción"* (`isSingleClassPass`), con texto de ayuda indicando que los planes no ilimitados de 1 crédito también se ofrecen automáticamente.

## Fase 4: Frontend - Recepción, Check-in Directo y Anulación del Presente (Angular + TDD)

- [x] 4.1 **(TDD Red)** Crear `frontend/src/app/features/portal/staff/staff-portal.spec.ts` cubriendo:
  - Ausencia total de inputs de `Guid` (alumno y membresía).
  - Buscador de alumnos con debounce sobre `searchPeople({ role: 'Student', search: '...' })` y carga de `getDeskEligibility` al seleccionar un alumno.
  - Acción **"✓ Dar Presente"**: usa `recordAttendance` si el alumno ya tiene reserva `CONFIRMED`, o `deskDropInCheckIn` si no tiene reserva previa.
  - Acción **"💳 Cobrar Pase y Dar Presente"**: preselecciona el plan si hay una sola opción o permite elegir entre varias opciones con su precio, selecciona el medio de pago y envía `Idempotency-Key`.
  - Acción **"📋 Inscribir Reserva"** sólo visible cuando `canEnrollReservation` es `true`.
  - Acción **"Anular presente"** en filas `ATTENDED` con motivo (`StaffError` / `StudentWithdrew`) y observación obligatoria, y refresco inmediato de la nómina y contadores.
  - Ocultación de los controles de mostrador cuando el usuario es únicamente `Instructor`.
- [x] 4.2 **(TDD Green)** Implementar los cambios en `StaffPortalComponent` hasta que todas las pruebas de `staff-portal.spec.ts` pasen en verde.

## Fase 5: Frontend - Portal del Alumno (Angular + TDD)

- [x] 5.1 **(TDD Red)** Actualizar `frontend/src/app/features/portal/student/student-portal.spec.ts` para verificar que, en clases con `requiresBooking === false`, se oculten los botones de reserva y lista de espera y se muestre la insignia *"ℹ️ Acceso libre por orden de llegada (ingreso en recepción)"*.
- [x] 5.2 **(TDD Green)** Implementar la adaptación en `StudentPortalComponent` y verificar las pruebas en verde.

## Fase 6: Verificación Integral

- [x] 6.1 Ejecutar la suite completa de backend (`dotnet test`).
- [x] 6.2 Ejecutar la suite completa de frontend (`npx ng test --watch=false`).
- [x] 6.3 Verificar que no queden campos de `Guid` en `StaffPortalComponent` y que todos los escenarios de las specs delta se cumplan.
