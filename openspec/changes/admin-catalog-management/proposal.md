## Why

Actualmente, las operaciones de creación, edición y administración de Salas físicas (`Rooms`), Actividades deportivas (`Activities`), Horarios recurrentes (`Schedules`) y Planes de membresía (`MembershipPlans`) solo se encuentran disponibles a nivel de API backend o mediante herramientas de base de datos/seeds, careciendo de una interfaz gráfica (Frontend) que permita a los administradores (`Administrator`) y secretarios (`Secretary`) gestionar el catálogo operativo en tiempo real.

Esta propuesta dota al sistema de pantallas, modales y flujos interactivos integrados en Angular para gestionar integralmente el catálogo base, garantizando validaciones de negocio en cliente y sincronización inmediata con la API REST existente, restringiendo el acceso exclusivamente al personal autorizado mientras se preserva la experiencia de consulta pública para alumnos y visitantes.

## What Changes

- **Control de acceso y visibilidad administrativa en UI**: Exposición condicional de herramientas de gestión de catálogo en base a roles (`Administrator` y `Secretary`). Los usuarios sin autenticar o con rol `Student`/`Instructor` mantienen acceso de solo lectura en las vistas públicas.
- **Apertura del portal administrativo a `Secretary`**: La ruta `/portal/admin` pasa a admitir `Administrator` y `Secretary`. Las pestañas cuyo backend exige `RequireAdministrator` (Usuarios, Auditoría) se ocultan para `Secretary`; las pestañas de catálogo y salas son visibles para ambos roles.
- **Gestión de Salas físicas (Rooms)**: Vista administrativa para listar (incluyendo inactivas), dar de alta, editar y activar/desactivar salas, validando nombre y aforo positivo obligatorio (`GET /api/rooms`, `POST /api/rooms`, `PUT /api/rooms/{id}`).
- **Gestión de Actividades (Activities)**: Interfaz administrativa para listar con filtros de búsqueda y estado, crear actividades (normalizando código en mayúsculas, validando rangos etarios y color hexadecimal), editar metadatos con código inmutable y control de concurrencia optimista (`version`), y cambiar estados operativos respetando las transiciones válidas del dominio (`Active ↔ Inactive ↔ Archived`).
- **Gestión de Horarios Recurrentes (Schedules)**: Interfaz de grilla semanal para configurar turnos recurrentes vinculando Actividad, Sala, Instructor, día, horario, vigencia y cupo, con validaciones de coherencia horaria y de vigencia, manejo de colisión de sala (`409`) y baja lógica de horarios con advertencia de cancelación de clases futuras.
- **Gestión de Planes de Membresía (MembershipPlans)**: Panel administrativo para listar planes activos e inactivos, dar de alta planes ilimitados o por paquete de créditos (con selección múltiple de actividades permitidas), editar, desactivar y reactivar planes sin perjudicar membresías vigentes.
- **Listado de instructores para selectores**: Extensión mínima del backend: `GET /api/people` acepta un filtro opcional `role` para obtener las personas con un rol activo dado (p. ej. `Instructor`).
- **Puntos de integración y navegación contextual**:
  - Botones de acción rápida (`+ Nueva Actividad`, `+ Nuevo Horario`, `+ Nuevo Plan`) en las cabeceras de las vistas públicas (`/actividades`, `/horarios`, `/planes`) visibles únicamente para administradores y secretarios.
  - Pestañas "Gestión de Catálogo" y "Salas y Espacios" en `/portal/admin` para la administración centralizada del catálogo y espacios físicos.
- **Extensión del cliente API Frontend (`GymApiService`)**: Incorporación de métodos de mutación HTTP (`POST`, `PUT`, `PATCH`, `DELETE`) para salas, actividades, horarios y planes de membresía.

## Capabilities

### New Capabilities
<!-- Ninguna nueva capacidad requerida; se extienden las capacidades existentes. -->

### Modified Capabilities
- `activities-catalog`: Interfaz administrativa para alta, edición de metadatos con concurrencia optimista, código inmutable en mayúsculas, transiciones de estado válidas y accesos contextuales para roles autorizados.
- `activities-schedule`: Interfaz administrativa para Salas físicas (alta, edición, activación/desactivación) y Horarios recurrentes (programación semanal, vigencia, colisión de sala y baja lógica con advertencia).
- `memberships`: Interfaz administrativa para Planes de Membresía (alta ilimitada o por créditos, validaciones de cliente, edición, desactivación y reactivación) con visibilidad restringida por rol.
- `people-management`: La búsqueda paginada de personas admite filtro opcional por rol activo.
- `identity-access`: El portal administrativo admite el rol `Secretary` con visibilidad de pestañas por rol.

## Impact

- **Frontend Angular**:
  - `src/app/app.routes.ts`: `/portal/admin` admite `Secretary` además de `Administrator`.
  - `src/app/core/services/gym-api.service.ts` (+ nuevo `gym-api.service.spec.ts`): métodos de mutación para salas, actividades, horarios y planes.
  - `src/app/core/services/people.service.ts`: soporte del filtro `role`.
  - `src/app/core/models/gym.models.ts`: DTOs de mutación; `version` en el modelo de actividad; enum `ActivityStatus`.
  - `src/app/features/public/activities/activities.ts`, `schedules/schedules.ts`, `plans/plans.ts`: acciones contextuales para staff.
  - `src/app/features/portal/admin/admin-portal.ts` y componentes hijos: nuevas pestañas y visibilidad de pestañas por rol.
  - Componentes/diálogos de formulario para Salas, Actividades, Horarios y Planes.
- **Backend**:
  - Usa los endpoints existentes de `RoomsController`, `ActivitiesController`, `SchedulesController` y `MembershipPlansController` sin cambios.
  - `PeopleController` / `PeopleService`: nuevo parámetro opcional `role` en la búsqueda (cambio aditivo, sin migraciones).
- **Dependencias**:
  - No requiere librerías externas adicionales; se apoya en formularios reactivos, señales y componentes existentes de Angular.
