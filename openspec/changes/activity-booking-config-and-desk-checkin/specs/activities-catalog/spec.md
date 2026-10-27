## ADDED Requirements

### Requirement: Modalidad de reserva por actividad
El sistema SHALL permitir configurar para cada actividad si sus clases requieren reserva previa (`RequiresBooking`, booleano, por defecto `true`). El valor SHALL persistirse en la actividad, exponerse en los DTOs administrativos (`ActivityListItemDto`, `ActivityDetailDto`) y públicos (`PublicActivityDto`), y aceptarse como opcional en `CreateActivityRequest` y `UpdateActivityRequest` (si se omite al crear, se asume `true`).
- Cuando `RequiresBooking == false`, la actividad opera por ingreso directo en recepción por orden de llegada (*Drop-in*): sus clases no admiten reservas previas ni lista de espera, y la asistencia se registra mediante el Check-in Directo en mostrador definido en `reservations-attendance`.
- Cambiar el valor SHALL NOT alterar reservas existentes (históricas ni futuras ya confirmadas); sólo afecta a las nuevas operaciones de reserva.
- En la administración del catálogo (`ActivityFormModalComponent`), el personal autorizado SHALL poder activar o desactivar la opción mediante un control explícito ("¿Requiere reserva previa online?"), inicializado en `true` al crear.
- Las vistas de catálogo administrativo y público SHALL mostrar una insignia con la modalidad ("Con reserva previa" / "Acceso libre sin reserva").
- El cambio de modalidad SHALL registrarse en la auditoría de la actividad.

#### Scenario: Creación de actividad sin requerimiento de reserva previa
- **WHEN** el administrador crea la actividad "Musculación y Sala Libre" con `RequiresBooking = false`
- **THEN** la actividad queda en estado `ACTIVE` con `RequiresBooking = false`, valor que se expone tanto en los DTOs administrativos como en los públicos

#### Scenario: Valor por defecto de RequiresBooking
- **WHEN** se crea una nueva actividad sin especificar `RequiresBooking`
- **THEN** el sistema asigna `RequiresBooking = true`

#### Scenario: Cambio de modalidad sin efecto sobre reservas existentes
- **WHEN** el administrador cambia `RequiresBooking` de `true` a `false` en la actividad "Spinning", que tiene reservas `CONFIRMED` para clases futuras
- **THEN** la actividad persiste el nuevo valor, las reservas existentes permanecen `CONFIRMED` y las nuevas solicitudes de reserva o lista de espera para sus clases son rechazadas con `BOOKING_NOT_REQUIRED`

#### Scenario: Actividades existentes tras la migración
- **WHEN** se aplica la migración que agrega `RequiresBooking`
- **THEN** todas las actividades existentes quedan con `RequiresBooking = true` y conservan su comportamiento actual
