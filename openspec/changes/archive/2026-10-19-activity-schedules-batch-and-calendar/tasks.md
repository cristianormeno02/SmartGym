## 1. Backend: Colisión de instructor y conflictos estructurados

- [x] 1.1 Agregar `ScheduleConflictKind`, `ScheduleConflictDto` y `ScheduleConflictException` (transporta la lista de conflictos) en `SmartGym.Application.Modules.Activities`.
- [x] 1.2 (Red) Escribir pruebas en `SmartGym.Domain.UnitTests/Activities` para `CreateAsync` y `UpdateAsync`: colisión de instructor en otra sala, ausencia de colisión con horarios contiguos o vigencias disjuntas, y que la colisión de sala siga detectándose, ahora como `ScheduleConflictException` con un conflicto.
- [x] 1.3 (Green) Implementar `HasInstructorCollisionAsync`, unificar la detección en un método que devuelva conflictos tipados y lanzar `ScheduleConflictException` desde `CreateAsync` y `UpdateAsync` (solo si `IsActive`). Extraer la validación de actividad, sala, instructor y secretario a un helper privado reutilizable.
- [x] 1.4 Mapear `ScheduleConflictException` en `SchedulesController` a `409 { message, conflicts }`, conservando `message` para compatibilidad con el frontend actual; ajustar las pruebas existentes que esperaban `InvalidOperationException`.

## 2. Backend: Carga en lote

- [x] 2.1 Agregar los records `BatchScheduleSlotRequest` y `CreateBatchRecurringScheduleRequest` (con `DayOfWeek` y `TimeOnly`) en `RecurringScheduleDtos.cs` y la firma `CreateBatchAsync` en `IRecurringScheduleService`.
- [x] 2.2 (Red) Escribir pruebas unitarias de `CreateBatchAsync`: creación exitosa de 6 horarios con cupo resuelto; validaciones `400` (sin grupos, grupo sin días, día repetido, fin ≤ inicio, vigencia inválida, más de 50 horarios); referencias inválidas; superposición dentro del lote; colisión de sala y de instructor con horarios existentes; reporte de todos los conflictos; ningún horario persistido ante conflicto.
- [x] 2.3 (Green) Implementar `CreateBatchAsync`: validación, expansión, detección acumulada de conflictos con una sola consulta, y persistencia de horarios más `SyncAsync` de cada uno dentro de `BeginTransactionAsync` con commit al final.
- [x] 2.4 Exponer `POST /api/schedules/batch` en `SchedulesController` con `[Authorize(Policy = Policies.RequireStaff)]`, respondiendo `201` con `RecurringScheduleDto[]`, `400` y `409 { message, conflicts }`.
- [x] 2.5 Escribir pruebas de integración en `SmartGym.IntegrationTests` (PostgreSQL): lote exitoso con clases generadas; rollback completo de horarios y clases ante conflicto o fallo durante la sincronización; `401`/`403` para usuarios no autorizados.

## 3. Frontend: Modelos, servicio y modal de carga masiva

- [x] 3.1 Agregar en `core/models/gym.models.ts` los tipos `BatchScheduleSlot`, `CreateBatchRecurringScheduleRequest` y `ScheduleConflict`, e implementar `GymApiService.createBatchSchedules(request)` con su prueba en `gym-api.service.spec.ts`.
- [x] 3.2 (Red) Escribir `schedule-batch-modal.spec.ts`: previsualización y conteo, autocompletado de la hora de fin, validaciones por grupo (sin días, fin ≤ inicio, superposición entre grupos, más de 50), cupo obligatorio sin capacidad por defecto, marcado de conflictos ante `409` conservando datos, y cierre con notificación ante `201`.
- [x] 3.3 (Green) Implementar `ScheduleBatchModalComponent` en `features/catalog/schedule-batch-modal/` reutilizando los selectores y reglas del modal unitario.
- [x] 3.4 Integrar la acción `Carga Masiva` (solo para personal autorizado) en `SchedulesManagementComponent` y en `SchedulesComponent` (`/horarios`), recargando los horarios tras el guardado exitoso.

## 4. Frontend: Calendario semanal, filtro y detalle

- [x] 4.1 (Red) Escribir `schedule-calendar.spec.ts`: posición y altura por hora y duración, rango horario dinámico, subcolumnas para horarios simultáneos, filtrado por vigencia de la semana visible y navegación entre semanas, color estable por actividad, emisión de `scheduleSelected` al clic, y vista por día en pantallas pequeñas.
- [x] 4.2 (Green) Implementar `ScheduleCalendarComponent` en `features/catalog/schedule-calendar/` con Tailwind y signals, sin dependencias nuevas.
- [x] 4.3 Implementar `ScheduleDetailModalComponent` en `features/catalog/schedule-detail-modal/` con su prueba: datos completos; Editar y Dar de baja solo con `canManage`; Editar abre `ScheduleFormModalComponent` con el horario; Dar de baja abre la confirmación existente.
- [x] 4.4 Agregar el filtro multi-actividad ("Todas" más chips por actividad, indicador de horarios visibles) y el conmutador Calendario / Tarjetas en `SchedulesManagementComponent` y `SchedulesComponent`, compartiendo la lógica de filtrado.
- [x] 4.5 Actualizar `schedules-management.spec.ts` y `schedules.spec.ts`: alternancia de vistas sin nueva consulta, filtro de una o varias actividades y vuelta a "Todas", apertura del detalle al clic, y ausencia de acciones de gestión para usuarios no autorizados.

## 5. Verificación

- [x] 5.1 Ejecutar `dotnet test` en `backend/`, incluidas las pruebas de integración, y confirmar que todo pasa.
- [x] 5.2 Ejecutar `npm test -- --watch=false` (Vitest) y `npm run build` en `frontend/` sin errores.
- [x] 5.3 Verificar manualmente en la app la carga de la grilla de Funcional (4 grupos, 12 horarios), un conflicto `409` marcado en el modal, la edición individual de uno de los horarios, y el calendario con filtro y detalle tanto en el portal como en `/horarios`.
- [x] 5.4 Archivar el cambio y sincronizar `openspec/specs/activities-schedule/spec.md`.
