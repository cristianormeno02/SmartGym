## Why

Actualmente, las fechas en la interfaz web de SmartGym se muestran e ingresan con inconsistencias:
1. **Disparidad en la visualización de fechas**: En varias vistas se utiliza el pipe `DatePipe` de Angular sin especificar un locale argentino (`es-AR`), lo que por defecto produce formatos del locale `en-US` como `M/d/yy` (ej. `10/25/26`), `MMM d, y` o cadenas ISO directas (`yyyy-MM-dd`).
2. **Ingreso de fechas sin formato uniforme DD/MM/AAAA**: En los formularios de carga y edición (fecha de nacimiento de personas, fechas de inicio y fin de vigencia de membresías y horarios, fechas de filtros y carga de apto médico) se utiliza `<input type="date">` nativo, cuyo formato visual depende del sistema operativo o navegador del usuario (mostrando a menudo `MM/DD/AAAA` o formatos anglosajones).
3. **Ausencia de filtros en las clases disponibles del Portal del Alumno**: En el panel del alumno (`StudentPortalComponent`), la sección de clases disponibles para reservar lista todas las clases abiertas de los próximos 7 días sin controles de filtrado ni búsqueda. A medida que aumenta el volumen de clases y disciplinas, resulta poco práctico para el alumno localizar clases de su interés sin poder filtrar por actividad, fecha, franja horaria (mañana, tarde, noche) o cupo disponible.

Estandarizar el formato de fechas a **`DD/MM/AAAA`** tanto para listar como para ingresar datos, garantizar que en cualquier reporte o bitácora los horarios y marcas temporales se muestren con horas y minutos (**`HH:mm`**) sin incluir segundos, junto con una barra de filtros interactiva para las clases disponibles del alumno, brinda una experiencia de usuario clara, predecible y adaptada a las convenciones locales.

## What Changes

- **Convención y Utilidades Compartidas de Formato de Fecha y Hora (`DD/MM/AAAA` y `HH:mm`)**:
  - Se configurará el locale `es-AR` en la aplicación Angular (`app.config.ts`) mediante `registerLocaleData(localeEsAr, 'es-AR')` y `LOCALE_ID`.
  - Se creará un módulo de utilidades puras `src/app/core/utils/date-format.ts` (`formatDateDisplay`, `formatDateTimeDisplay`, `formatTimeDisplay`, `parseDateInput`, `toIsoDateString`, `isValidDateString`) para manipular y formatear fechas en `DD/MM/AAAA` y horas en `HH:mm` (sin segundos) de manera determinista y testeable.
  - Se implementará el pipe standalone `AppDatePipe` (`appDate`) que transforma fechas (Date, string ISO o timestamp) en formato `DD/MM/AAAA` (o `DD/MM/AAAA HH:mm` si se especifica la opción con hora sin segundos), y soporte para formatear horarios puros de cadenas TimeOnly (ej. `"18:00:00"` → `"18:00"`).
  - En cualquier reporte, bitácora de auditoría (ej. `AdminPortalComponent`), listado gerencial o tarjeta de turnos, las marcas temporales se mostrarán sin segundos (`DD/MM/AAAA HH:mm`) y los horarios de inicio y fin como `HH:mm`.
- **Directiva / Control de Ingreso de Fecha en Formato DD/MM/AAAA (`DateInputDirective`)**:
  - Se implementará una directiva `ControlValueAccessor` (`appDateInput`) para campos de texto de fecha (`<input type="text" placeholder="DD/MM/AAAA" appDateInput>`):
    - Permite al usuario tipear o editar directamente en formato `DD/MM/AAAA` con inserción automática de separadores `/` y validación de fecha real válida.
    - Sincroniza bidireccionalmente con el formulario reactivo (`ReactiveFormsModule`) o de plantilla (`FormsModule`), exponiendo hacia el modelo el valor en formato ISO estándar (`yyyy-MM-dd` o `null`) para que las APIs REST y DTOs del backend no requieran modificaciones.
    - Maneja eventos de `blur` y pegado validando día (01-31), mes (01-12) y año coherente.
- **Unificación de Fechas en Listados y Formularios de Toda la Aplicación**:
  - **Portal del Alumno (`StudentPortalComponent`)**:
    - Listado de "Mis Reservas Próximas" (`r.classDate` en `DD/MM/AAAA`, horarios y expiración de ofertas).
    - Tarjetas de "Clases Disponibles para Reservar" (`c.classDate` en `DD/MM/AAAA`).
    - Detalle de Membresía (`mem.startDate` y `mem.endDate` en `DD/MM/AAAA`).
    - Fechas de notificaciones y movimientos de créditos (`createdAtUtc` en `DD/MM/AAAA HH:mm`).
    - Campo de vencimiento al adjuntar certificado médico (`uploadExpiryDate`) con formato de ingreso `DD/MM/AAAA`.
  - **Portal de Secretaría / Staff (`StaffPortalComponent`)**:
    - Selectores de fecha para consultar clases del día (`filterFrom`, `filterTo`) con formato de ingreso `DD/MM/AAAA`.
    - Tablas de clases, reservas, listas de espera y vencimientos de ofertas en `DD/MM/AAAA` y `DD/MM/AAAA HH:mm`.
  - **Gestión de Personas (`PersonFormComponent`, `PersonDetailComponent`, `PeopleListComponent`, `MyProfileComponent`)**:
    - Campo `birthDate` en formulario de alta y edición con ingreso en formato `DD/MM/AAAA`.
    - Ficha de detalle y listado con fecha de nacimiento, fecha de alta, cambios de estado y último acceso en `DD/MM/AAAA` o `DD/MM/AAAA HH:mm`.
  - **Gestión y Asignación de Membresías (`AssignMembershipModalComponent`)**:
    - Campo de fecha de inicio (`startDate`) con ingreso en formato `DD/MM/AAAA`.
  - **Gestión y Calendario de Horarios (`ScheduleFormModalComponent`, `ScheduleBatchModalComponent`, `ScheduleDetailModalComponent`, `AdminPortalComponent`)**:
    - Campos de vigencia (`validFrom`, `validTo`) en formularios unitarios y masivos con ingreso en formato `DD/MM/AAAA`.
    - Vigencias en modales de detalle y tablas administrativas en `DD/MM/AAAA`.
  - **Centro de Notificaciones y Navbar (`NotificationsCenterComponent`, `NavbarComponent`)**:
    - Fechas de envío y lectura en `DD/MM/AAAA HH:mm`.
- **Filtros Interactivos en "Clases Disponibles para Reservar" del Portal del Alumno**:
  - En la sección correspondiente de `StudentPortalComponent`:
    - **Filtro por Actividad**: Selector desplegable o chips para ver "Todas las actividades" o filtrar por una actividad puntual (extraídas dinámicamente de las clases activas disponibles).
    - **Filtro por Fecha**: Controles rápidos para seleccionar "Todas las fechas", "Hoy", "Mañana" o una fecha específica en formato `DD/MM/AAAA`.
    - **Filtro por Franja Horaria**: Opciones para filtrar por "Todas las franjas", "Mañana" (inicio antes de las 12:00), "Tarde" (inicio entre las 12:00 y las 17:59) y "Noche" (inicio a partir de las 18:00).
    - **Filtro por Disponibilidad de Cupo**: Conmutador o selector para alternar entre "Todas las clases" y "Solo con cupos disponibles" (ocultando clases completas que solo admiten lista de espera).
    - **Filtro por Sala / Lugar**: Selector opcional de sala para consultar clases dictadas en un espacio específico (ej. "Sala Indoor", "Sala Spinning", etc.).
    - **Búsqueda por Texto**: Campo de búsqueda rápida que filtra en tiempo real por nombre de actividad o nombre del instructor.
    - **Contador y Restablecimiento**: Indicador visual con la cantidad de clases encontradas vs. total ("Mostrando X de Y clases disponibles") y botón para "Limpiar filtros" con un solo clic.

## Capabilities

### New Capabilities

<!-- Ninguna capacidad nueva de dominio en el backend; se refinan capacidades existentes de reservas y convención global de UI. -->

### Modified Capabilities

- `reservations`: Refina la sección de clases disponibles para reservar en el autoservicio del alumno con filtros dinámicos (actividad, fecha, franja horaria, disponibilidad, sala y búsqueda de texto), y estandariza la presentación e ingreso de fechas de reservas, vigencias de membresías y certificados médicos en formato `DD/MM/AAAA`.
- `people-management`: Los campos y columnas de fecha (nacimiento, alta, modificación, último acceso) en formulario de persona, listado, perfil y ficha de detalle se ingresan y muestran en formato `DD/MM/AAAA` (o `DD/MM/AAAA HH:mm`).
- `memberships`: Las fechas de inicio, expiración y vigencia de membresías (en modal de asignación, portal del alumno y detalle de persona) se ingresan y muestran en formato `DD/MM/AAAA`.
- `activities-schedule`: Las fechas de inicio y fin de vigencia (`validFrom`, `validTo`) en la programación unitaria y masiva de horarios se ingresan y visualizan con formato `DD/MM/AAAA`.
- `operations-reporting`: En cualquier reporte operativo, bitácora de auditoría, listado gerencial o agenda de control, las marcas de fecha se presentan en `DD/MM/AAAA` y las horas/horarios se exhiben estrictamente con horas y minutos en formato `HH:mm`, sin mostrar segundos.

## Impact

- **Frontend (`frontend/src/app`)**:
  - `src/app/app.config.ts`: provisión de `LOCALE_ID` con `'es-AR'` y registro de datos de locale.
  - `src/app/core/utils/date-format.ts` (+ `.spec.ts`): utilidades puras para formateo, parseo y validación de fechas `DD/MM/AAAA`.
  - `src/app/shared/pipes/app-date.pipe.ts` (+ `.spec.ts`): pipe standalone `appDate` para templates.
  - `src/app/shared/directives/date-input.directive.ts` (+ `.spec.ts`): directiva `ControlValueAccessor` para inputs de fecha con máscara y validación `DD/MM/AAAA`.
  - `src/app/features/portal/student/student-portal.ts` (+ `.spec.ts`): barra de filtros completa sobre clases disponibles y formateo de fechas en `DD/MM/AAAA`.
  - Actualización de vistas y formularios con fechas:
    - `src/app/features/people/person-form.ts` (+ `.spec.ts`)
    - `src/app/features/people/person-detail.ts` (+ `.spec.ts`)
    - `src/app/features/people/people-list.ts` (+ `.spec.ts`)
    - `src/app/features/people/my-profile.ts`
    - `src/app/features/catalog/assign-membership-modal/assign-membership-modal.ts` (+ `.spec.ts`)
    - `src/app/features/catalog/schedule-form-modal/schedule-form-modal.ts` (+ `.spec.ts`)
    - `src/app/features/catalog/schedule-batch-modal/schedule-batch-modal.ts` (+ `.spec.ts`)
    - `src/app/features/catalog/schedule-detail-modal/schedule-detail-modal.ts` (+ `.spec.ts`)
    - `src/app/features/portal/staff/staff-portal.ts`
    - `src/app/features/portal/admin/admin-portal.ts` (+ `.spec.ts`)
    - `src/app/features/portal/notifications/notifications-center.ts`
    - `src/app/shared/components/navbar/navbar.ts`
- **Backend**:
  - Sin cambios en los contratos de la API ni en las bases de datos. Los endpoints continúan recibiendo y emitiendo `DateOnly` (`yyyy-MM-dd`) y `DateTime` UTC estándar en formato ISO 8601.
