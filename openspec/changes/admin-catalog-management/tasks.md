> Cada tarea sigue Red-Green-Refactor: escribir primero la prueba que falla, luego el código mínimo, luego refactorizar con las pruebas en verde.

## 1. Backend: filtro por rol en búsqueda de personas

- [x] 1.1 Escribir pruebas (unitarias de `PeopleService` e integración de `GET /api/people`) para el filtro `role`: retorna solo personas con el rol activo, excluye roles revocados, se combina con `status`, y un rol inexistente responde `400`.
- [x] 1.2 Implementar el parámetro opcional `role` en `PeopleController.Search` y `PeopleService` hasta que las pruebas pasen, sin migraciones.

## 2. Modelos y cliente API (Frontend)

- [x] 2.1 Crear `gym-api.service.spec.ts` con pruebas (`HttpTestingController`) para cada método nuevo: verbo, URL y cuerpo esperados.
- [x] 2.2 Extender `gym.models.ts`: DTOs `CreateRoomRequest`, `UpdateRoomRequest`, `CreateActivityRequest`, `UpdateActivityRequest`, `ChangeActivityStatusRequest`, `CreateRecurringScheduleRequest`, `UpdateRecurringScheduleRequest`, `CreateMembershipPlanRequest`, `UpdateMembershipPlanRequest`; enum `ActivityStatus` (1 Active, 2 Inactive, 3 Archived); `version` en el detalle de actividad; tipado de `getActivities` como resultado paginado.
- [x] 2.3 Implementar en `GymApiService`: `createRoom`, `updateRoom`, `createActivity`, `updateActivity`, `changeActivityStatus`, `createSchedule`, `updateSchedule`, `deleteSchedule`, `createMembershipPlan`, `updateMembershipPlan`, hasta que pasen las pruebas de 2.1.
- [x] 2.4 Agregar el filtro `role` en `people.service.ts`, con prueba en `people.service.spec.ts`.
- [x] 2.5 Crear utilidades de mapeo probadas: día de UI (Lunes..Domingo) ↔ `DayOfWeek` numérico (0 = Domingo), y `"HH:mm"` ↔ `"HH:mm:ss"`.

## 3. Acceso al portal administrativo

- [x] 3.1 Escribir pruebas: `/portal/admin` admite `Secretary` y `Administrator` y rechaza `Student`/`Instructor`; las pestañas Usuarios y Auditoría solo son visibles para `Administrator`; la pestaña inicial es la primera visible para el rol.
- [x] 3.2 Cambiar `roleGuard` de la ruta en `app.routes.ts` y declarar los roles visibles por pestaña en `admin-portal.ts`. Verificar la política real de los endpoints de Promociones y Notificaciones y ocultar a `Secretary` las pestañas cuyo backend la rechace.

## 4. Salas físicas (Rooms)

- [x] 4.1 Pruebas del componente de formulario de sala: nombre obligatorio, capacidad ≥ 1, envío en alta y edición.
- [x] 4.2 Implementar el modal de alta/edición de sala, con el conmutador activa/inactiva en edición.
- [x] 4.3 Pruebas e implementación de la pestaña "Salas y Espacios": listado de activas e inactivas, alta, edición y desactivación, con refresco del listado tras cada mutación.

## 5. Catálogo de Actividades (Activities)

- [x] 5.1 Pruebas del formulario de actividad: normalización de código (`trim` + mayúsculas), código solo lectura en edición, color `#RRGGBB`, `defaultCapacity ≥ 1`, `minAge <= maxAge`, envío de `version` en edición, manejo de `409` por código duplicado y por concurrencia (recarga del detalle, modal abierto).
- [x] 5.2 Implementar el modal de alta/edición de actividad.
- [x] 5.3 Pruebas e implementación de las acciones de estado: solo transiciones válidas por estado, `PATCH` con `version`, recarga del detalle y badge actualizado, diálogo con `ActiveSchedules`/`FutureSessions` ante `409`.
- [x] 5.4 Pruebas e implementación en `/actividades`: para staff, carga desde `/api/activities` con filtro de estado, botón `+ Nueva Actividad` y acciones por actividad; para anónimos, `Student` e `Instructor`, sin cambios en fuente ni controles.
- [x] 5.5 Sub-sección Actividades en la pestaña "Gestión de Catálogo": listado paginado con búsqueda y filtro de estado, reutilizando los modales de 5.2 y 5.3.

## 6. Horarios Recurrentes (Schedules)

- [x] 6.1 Pruebas del formulario de horario: `startTime < endTime`, `validFrom` obligatorio, `validTo >= validFrom`, cupo obligatorio si la actividad no tiene `defaultCapacity`, selectores solo con actividades activas, salas activas e instructores de `GET /api/people?role=Instructor&status=ACTIVA`, y `409` por colisión mostrado sin cerrar el modal.
- [x] 6.2 Implementar el modal de alta/edición de horario (en edición la actividad no se modifica; `secretaryId` se envía `null`).
- [x] 6.3 Pruebas e implementación de la baja: diálogo que advierte la cancelación de clases futuras, `DELETE /api/schedules/{id}` solo tras confirmar, y bloque retirado de la grilla activa.
- [x] 6.4 Pruebas e implementación en `/horarios`: para staff, botón `+ Nuevo Horario` y acciones de edición/baja por bloque; sin controles para el resto.
- [x] 6.5 Sub-sección Horarios en la pestaña "Gestión de Catálogo": grilla semanal (solo horarios activos; sin conmutador de inactivos ni `isActive` en el formulario), reutilizando los modales de 6.2 y 6.3.

## 7. Planes de Membresía (MembershipPlans)

- [x] 7.1 Pruebas del formulario de plan: nombre, precio > 0, duración ≥ 1, créditos ≥ 1 si no es ilimitado, al menos una actividad si no aplica a todas, `credits = 0` al marcar ilimitado, `allowedActivityIds = []` al marcar "todas las actividades".
- [x] 7.2 Implementar el modal de alta/edición de plan con el selector múltiple de actividades.
- [x] 7.3 Pruebas e implementación de desactivar/reactivar con `PUT` e `isActive`.
- [x] 7.4 Pruebas e implementación en `/planes`: para staff, carga con `onlyActive = false`, planes inactivos marcados, `+ Nuevo Plan` y acciones; para el resto, solo planes activos y sin controles.
- [x] 7.5 Sub-sección Planes en la pestaña "Gestión de Catálogo", reutilizando los modales de 7.2 y 7.3.

## 8. Verificación final

- [x] 8.1 Ejecutar la suite del frontend (`npm test -- --watch=false`) y `npm run build` con compilación limpia.
- [x] 8.2 Ejecutar las pruebas del backend afectadas (`dotnet test`, incluidas las de integración de personas).
- [x] 8.3 Verificación manual en navegador con un usuario `Secretary` y uno `Administrator`: portal, pestañas visibles y un flujo completo de alta/edición/estado por entidad.
