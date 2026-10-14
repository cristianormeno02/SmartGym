## Context

El backend ASP.NET Core expone controllers REST para el catálogo: `RoomsController`, `ActivitiesController`, `SchedulesController` y `MembershipPlansController`. Las mutaciones están protegidas bajo la política `RequireStaff` (`Administrator` y `Secretary`); `ActivitiesController` exige `RequireStaff` incluso para lectura.
En el frontend (Angular standalone con Tailwind CSS y Signals), las vistas públicas (`/actividades`, `/horarios`, `/planes`) y el portal de administración (`/portal/admin`) están construidos, pero carecen de flujos de creación, modificación y cambio de estado. Hoy `/portal/admin` solo admite `Administrator` (`app.routes.ts`).

Hechos del backend relevantes para el diseño:
- `UpdateActivityRequest` y `ChangeActivityStatusRequest` exigen `Version` (concurrencia optimista); una versión desactualizada produce `409`.
- Transiciones de estado de actividad permitidas por el dominio: `Active ↔ Inactive` e `Inactive ↔ Archived`. `Active → Archived` directo no es válido. Inactivar con horarios vigentes o clases futuras produce `409` con `details` (`ActiveSchedules`, `FutureSessions`).
- `GET /api/public/activities` solo devuelve actividades activas, sin `id` ni `version`, y responde con `Cache-Control: public, max-age=300`.
- `DELETE /api/schedules/{id}`, `DELETE /api/rooms/{id}` y `DELETE /api/membershipplans/{id}` son **bajas lógicas** (`IsActive = false`). La baja de horario además sincroniza y cancela las clases futuras candidatas; nunca devuelve `409`.
- `POST`/`PUT /api/schedules` devuelven `409` ante colisión de sala (mismo día y rango horario superpuesto) y `400` si `endTime <= startTime`, `validTo < validFrom`, recursos inexistentes/inactivos, o si no se indica cupo y la actividad no tiene `defaultCapacity`.
- `DayOfWeek` y `ActivityStatus` se serializan como números (`DayOfWeek`: 0 = Domingo … 6 = Sábado; `ActivityStatus`: 1 Active, 2 Inactive, 3 Archived). `TimeOnly` como `"HH:mm:ss"`, `DateOnly` como `"yyyy-MM-dd"`.
- `MembershipPlanService` solo valida el nombre; el resto de reglas de planes no se valida en servidor.
- No existe un endpoint para listar instructores; `GET /api/people` no filtra por rol.
- `GET /api/schedules` solo devuelve horarios activos (`Where(s => s.IsActive)`): un horario dado de baja no puede volver a consultarse ni reactivarse desde la UI.

Ver `proposal.md` para la motivación y los deltas en `specs/` para el comportamiento.

## Goals / Non-Goals

**Goals:**
- Métodos de mutación en `GymApiService` y DTOs tipados en `gym.models.ts` para Salas, Actividades, Horarios y Planes.
- Abrir `/portal/admin` a `Secretary` con visibilidad de pestañas por rol.
- Modales con validación reactiva en cliente para Salas, Actividades, Horarios y Planes.
- Acciones contextuales para staff en `/actividades`, `/horarios`, `/planes`.
- Pestañas "Salas y Espacios" y "Gestión de Catálogo" (sub-secciones Actividades, Horarios, Planes) en `/portal/admin`.
- Filtro opcional `role` en `GET /api/people` para alimentar el selector de instructores.

**Non-Goals:**
- Cambios de esquema de base de datos o migraciones.
- Cambios en `RoomsController`, `ActivitiesController`, `SchedulesController` o `MembershipPlansController` (los endpoints existentes cubren los casos de uso). La única extensión de backend es el filtro `role` en la búsqueda de personas.
- Modificar el `code` de una actividad ya creada.
- Eliminación física de actividades (`DELETE /api/activities/{id}`) y gestión de medios de actividad (logo/galería).
- Gestión de reservas, asistencia o clases concretas (`ClassSessions`).
- Asignación de `SecretaryId` en horarios (se envía `null`).

## Decisions

### 1. Estrategia dual: acciones contextuales + portal administrativo
- **Decisión**:
  1. En `/actividades`, `/horarios`, `/planes`, botones contextuales (`+ Nuevo`, `Editar`, cambio de estado) visibles si `authService.isStaff()`.
  2. En `/portal/admin`, pestañas de gestión del inventario completo (incluyendo entidades inactivas/archivadas y salas).
- **Razón**: Ajustes rápidos mientras se navega el catálogo, y un centro unificado para entidades no visibles públicamente.
- **Alternativas consideradas**: Solo portal (fricción para ajustes menores); solo vistas públicas (no permite gestionar inactivos ni salas).

### 2. Apertura de `/portal/admin` a `Secretary` con pestañas por rol
- **Decisión**: `roleGuard(['Administrator', 'Secretary'])` en la ruta. Cada pestaña declara los roles que la ven:
  - Ambos roles: KPIs (`/api/dashboard/metrics` es `RequireStaff`), Gestión de Catálogo, Salas y Espacios, y las demás pestañas cuyo backend sea `RequireStaff` o equivalente.
  - Solo `Administrator`: Usuarios (`/api/users/admin-overview`) y Auditoría (`/api/dashboard/audit-logs`).
  - Promociones y Notificaciones: visibles según la política real de sus endpoints, verificada en la tarea correspondiente; si un endpoint exige `Administrator`, la pestaña se oculta a `Secretary`.
  - La pestaña inicial es la primera visible para el rol actual.
- **Razón**: La gestión de salas no tiene vista pública y el Secretario debe poder operarla; ocultar pestañas evita que el Secretario dispare `403`.
- **Alternativa considerada**: Pestañas en `/portal/staff`. Descartada por decisión del usuario; mezclaría la operación diaria (asistencia) con la configuración del catálogo.

### 3. Modales con formularios reactivos
- **Decisión**: Modales con `FormBuilder` y `signal` para estado, como componentes standalone reutilizables desde la vista pública y desde el portal.
- **Razón**: No interrumpe la grilla ni las tarjetas; reutilización entre puntos de entrada.
- **Alternativa considerada**: Rutas dedicadas (`/portal/admin/actividades/nueva`). Descartada por fricción y pérdida de contexto de filtrado.

### 4. Validación de dominio en cliente
- Código de actividad: `trim()` + `toUpperCase()`; obligatorio en alta; solo lectura en edición.
- Color: `^#[0-9A-Fa-f]{6}$` cuando se informa.
- Capacidad de sala ≥ 1; `defaultCapacity` de actividad ≥ 1 cuando se informa.
- `minAge <= maxAge` cuando ambos se informan.
- Horario: `startTime < endTime`; `validFrom` obligatorio (por defecto hoy); `validTo >= validFrom` cuando se informa; `maxCapacity` ≥ 1 cuando se informa; si se deja vacío y la actividad seleccionada no tiene `defaultCapacity`, el campo pasa a ser obligatorio.
- Plan: nombre obligatorio; precio > 0; duración ≥ 1 día; si no es ilimitado, créditos ≥ 1; si no aplica a todas las actividades, al menos una actividad seleccionada. Al marcar ilimitado se envía `credits = 0`; al marcar "todas las actividades" se envía `allowedActivityIds = []`.
- **Razón**: Evitar roundtrips fallidos. Para planes es la única validación, porque el backend solo valida el nombre.

### 5. Concurrencia optimista en actividades
- **Decisión**: El modelo `Activity` del frontend incorpora `version`. Antes de editar o cambiar el estado se obtiene el detalle (`GET /api/activities/{id}`) y se envía su `version`. Ante `409` por versión, se muestra "La actividad fue modificada por otro usuario", se recarga el detalle y se conserva el modal abierto con los datos frescos.
- **Razón**: El backend rechaza cualquier `PUT`/`PATCH` sin la versión vigente.

### 6. Fuente de datos para staff en vistas públicas
- **Decisión**: Si `isStaff()`:
  - `/actividades` carga desde `GET /api/activities` (incluye `id`, estado y actividades no activas, con un filtro de estado para staff) en lugar del endpoint público cacheado.
  - `/planes` carga `getMembershipPlans(false)` y marca visualmente los planes inactivos.
  - `/horarios` y la grilla del portal muestran los horarios que devuelve `GET /api/schedules` (solo activos).
  - Visitantes y alumnos mantienen exactamente las fuentes actuales.
- **Razón**: El endpoint público no expone `id`/`version`, omite inactivos y está cacheado 5 minutos, lo que impediría reflejar mutaciones al instante y reactivar entidades.

### 7. Refresco tras mutación (no optimista)
- **Decisión**: Tras una mutación exitosa se vuelve a consultar la lista o se reemplaza el elemento en la señal con la respuesta del servidor. `PATCH /status` devuelve `{ changed }`, así que tras él se vuelve a obtener el detalle.
- **Razón**: Consistencia con datos derivados por el backend (`allowedActivityNames`, `isActive`, nombres de recursos).

### 8. Selector de instructores
- **Decisión**: `GET /api/people?role=Instructor&status=ACTIVA&pageSize=100` (mismo endpoint `RequireStaff`). El filtro coincide con personas que tienen ese rol con `PersonRole.IsActive = true`. Nombre de rol inválido → `400`.
- **Razón**: Cambio aditivo y mínimo en un endpoint existente; reutiliza paginación, orden y autorización.
- **Alternativa considerada**: Endpoint nuevo `/api/instructors`. Descartado por duplicar búsqueda y autorización.

### 9. Baja de horario recurrente
- **Decisión**: El diálogo de confirmación advierte explícitamente que se cancelarán las clases futuras generadas por el horario. Tras confirmar se envía `DELETE /api/schedules/{id}` y el bloque desaparece de la grilla. La baja es el único modo de desactivar un horario: el formulario de edición no expone `isActive` y el `PUT` envía siempre `isActive = true`, porque un horario inactivo deja de ser consultable y no podría reactivarse. Reactivar horarios queda fuera de alcance.
- **Razón**: La baja es lógica y dispara la cancelación de clases candidatas; el usuario debe conocer ese efecto antes de confirmar.

## Risks / Trade-offs

- **[Riesgo]** Desincronización entre roles del token y políticas del backend.
  → **Mitigación**: `authService.isStaff()` deriva de los roles del usuario actual; el backend sigue siendo la autoridad y rechaza con `401`/`403`. La UI muestra un mensaje genérico de permiso ante `403`.
- **[Riesgo]** La baja de un horario cancela clases futuras con reservas.
  → **Mitigación**: Advertencia explícita en el diálogo (Decisión 9). Las notificaciones a alumnos dependen del flujo de sincronización existente y no se modifican aquí.
- **[Riesgo]** Colisión de sala al crear o editar horarios (`409`).
  → **Mitigación**: Mostrar el mensaje del servidor en el modal sin cerrarlo, para que el usuario ajuste sala, día u horario.
- **[Riesgo]** Inactivar una actividad con horarios vigentes o clases futuras (`409` con `details`).
  → **Mitigación**: Diálogo que informa `ActiveSchedules` y `FutureSessions` devueltos por la API.
- **[Riesgo]** Edición concurrente de actividades (`409` por versión).
  → **Mitigación**: Decisión 5.
- **[Trade-off]** El endpoint público de actividades sigue cacheado 5 minutos para visitantes: tras crear o editar, los visitantes pueden ver datos viejos hasta 5 minutos. Se acepta.
- **[Trade-off]** Las reglas de planes solo se validan en cliente; una llamada directa a la API podría crear planes incoherentes. Endurecer `MembershipPlanService` queda fuera de alcance y se registra como deuda.
