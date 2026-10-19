## Context

La capacidad `activities-schedule` maneja turnos recurrentes semanales (`RecurringSchedule`) vinculados a una actividad, sala, instructor y secretario opcional, con vigencia (`ValidFrom` / `ValidTo`) y cupo resuelto por `Activity.ResolveCapacity`. El alta unitaria (`ScheduleFormModalComponent` → `POST /api/schedules`) funciona bien para 1 o 2 clases semanales, pero es tediosa para actividades con grillas densas (por ejemplo Funcional con 12 turnos semanales en 4 franjas).

Estado actual relevante del código:
- `RecurringScheduleService.CreateAsync` / `UpdateAsync` validan actividad activa, sala activa, instructor con rol `Instructor` activo y secretario con rol `Secretary` o `Administrator`, y detectan colisión **solo de sala** contra otros `RecurringSchedule` activos (`HasRoomCollisionAsync`). No existe detección de colisión de instructor ni contra `ClassSession` manuales.
- Tras `SaveChangesAsync`, se invoca `IClassSessionScheduleSync.SyncAsync`, que genera/ajusta clases concretas y llama a `SaveChangesAsync` por su cuenta.
- Los DTOs son `record` con `DayOfWeek` (enum, 0 = Domingo) y `TimeOnly`.
- Los conflictos se señalizan con `InvalidOperationException` → `409 { message }` y las validaciones con `ArgumentException` → `400 { message }`.
- Hay dos vistas de horarios: la grilla pública `/horarios` (`features/public/schedules`, que además expone acciones al personal) y `SchedulesManagementComponent` dentro del portal administrativo. Ambas usan pestañas por día y tarjetas.
- Las pruebas unitarias del backend usan EF InMemory, que ignora transacciones (`TransactionIgnoredWarning`); las de integración corren sobre PostgreSQL. El frontend usa Vitest vía `ng test`.

## Goals / Non-Goals

**Goals:**
- Carga en lote de múltiples días y franjas compartiendo actividad, sala, instructor, secretario, cupo y vigencia en una sola acción.
- Atomicidad real: si cualquier franja es inválida o colisiona, no se persiste ningún horario ni clase concreta.
- Reportar todas las franjas en conflicto en una sola respuesta.
- Detectar colisiones de instructor de forma consistente en todas las vías de alta y edición.
- Cada turno generado sigue siendo un `RecurringSchedule` independiente, editable y dable de baja individualmente.
- Calendario semanal reutilizable, con filtro multi-actividad, navegación por semana y detalle al clic, en el portal y en `/horarios`.

**Non-Goals:**
- Cambios de esquema o migraciones (no se agrega "lote" ni color a las entidades).
- Edición o baja en lote de horarios ya creados.
- Cambiar la actividad de un horario existente: `UpdateRecurringScheduleRequest` sigue sin `ActivityId`, porque el horario ya tiene clases y reservas asociadas a esa actividad. Para cambiar de actividad se da de baja y se crea uno nuevo.
- Detectar colisiones contra `ClassSession` creadas manualmente (se mantiene el alcance actual: colisiones entre horarios recurrentes; las clases generadas siguen protegidas por las restricciones de solapamiento de la base).
- Eliminar el endpoint o modal unitario.
- Dependencias de calendario de terceros (FullCalendar u otras).

## Decisions

### D1: Colisión de instructor en todas las vías (decisión tomada por defecto, revisable)
Se agrega `HasInstructorCollisionAsync` con la misma semántica que la colisión de sala (mismo `DayOfWeek`, rangos horarios superpuestos `start < other.end && other.start < end`, vigencias superpuestas, solo horarios activos, excluyendo el propio en edición) y se aplica en `CreateAsync`, `UpdateAsync` (si `IsActive`) y `CreateBatchAsync`.
- *Por qué*: un instructor no puede dictar dos clases a la vez; validar solo en lote haría que el mismo horario se rechace en lote y se acepte en alta unitaria.
- *Alternativa descartada*: validar solo sala (comportamiento actual) también en lote. Más simple, pero deja pasar un error operativo real.

### D2: Endpoint atómico `POST /api/schedules/batch`
```csharp
public record BatchScheduleSlotRequest(
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public record CreateBatchRecurringScheduleRequest(
    Guid ActivityId,
    Guid RoomId,
    Guid InstructorId,
    Guid? SecretaryId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxCapacity,
    IReadOnlyList<BatchScheduleSlotRequest> Slots);

public enum ScheduleConflictKind { Room, Instructor, WithinBatch }

public record ScheduleConflictDto(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    ScheduleConflictKind Kind,
    Guid? ConflictingScheduleId,
    string? ConflictingActivityName,
    TimeOnly? ConflictingStartTime,
    TimeOnly? ConflictingEndTime);
```

Procesamiento de `CreateBatchAsync`:
1. Validación estructural (`ArgumentException` → `400`): al menos un slot; cada slot con al menos un día; sin días repetidos dentro del slot; `EndTime > StartTime`; `ValidTo >= ValidFrom` si existe; total expandido ≤ **50** turnos.
2. Validación de referencias una sola vez (actividad activa, sala activa, instructor, secretario), reutilizando un helper privado extraído de `CreateAsync`/`UpdateAsync`. Cupo con `activity.ResolveCapacity(request.MaxCapacity)`.
3. Expansión de slots a tuplas `(DayOfWeek, StartTime, EndTime)`.
4. Detección de conflictos acumulando **todos** (no corta en el primero):
   - `WithinBatch`: dos tuplas del mismo día con horarios superpuestos.
   - `Room` / `Instructor`: contra `RecurringSchedule` activos con vigencia superpuesta. Se cargan en una sola consulta los horarios activos de la sala **o** del instructor para los días involucrados, y se evalúa en memoria.
5. Si hay conflictos → `ScheduleConflictException(conflicts)` → `409 { message, conflicts: ScheduleConflictDto[] }`.
6. Persistencia dentro de `await using var tx = await _dbContext.Database.BeginTransactionAsync(ct)` (patrón ya usado en `ClassSessionService`): agregar todas las entidades, `SaveChangesAsync`, invocar `SyncAsync` por cada horario creado, y recién entonces `tx.CommitAsync`. Cualquier excepción provoca rollback de horarios **y** clases generadas.
7. Respuesta `201 Created` con `RecurringScheduleDto[]` (incluye `DivergentSessionIds` por horario, normalmente vacío). Sin header `Location` individual.

La colisión del alta y la edición unitarias también pasa a lanzar `ScheduleConflictException` (con un único conflicto), de modo que el cuerpo `409` incluye `message` (compatibilidad con el frontend actual) y `conflicts`.

- *Alternativa descartada*: llamadas sucesivas al endpoint unitario desde el cliente; no es atómico (si falla la 5ª de 8 quedan 4 creadas) y multiplica las llamadas de red.
- *Riesgo de concurrencia*: la verificación previa no evita una carrera entre dos lotes simultáneos sobre la misma sala; se acepta (uso administrativo de baja concurrencia), igual que en el alta unitaria actual.

### D3: Modal de carga masiva (`ScheduleBatchModalComponent`)
- Ubicación: `frontend/src/app/features/catalog/schedule-batch-modal/`.
- Cabecera: Actividad, Sala, Instructor, Secretario opcional, Vigencia desde/hasta, Cupo (obligatorio si la actividad no tiene capacidad por defecto, igual que el modal unitario). Reutiliza los selectores y reglas del modal unitario (instructores desde `GET /api/people?role=Instructor`, solo salas y actividades activas).
- Grupos de horario dinámicos (`FormArray`): checkboxes Lun–Dom, hora de inicio, hora de fin. Al ingresar la hora de inicio, la de fin se autocompleta con la duración del grupo anterior (o 60 min para el primero) y es editable.
- Validaciones en cliente: grupo sin días, fin ≤ inicio, superposición entre grupos del mismo día, más de 50 turnos.
- Previsualización: lista ordenada por día y hora, con el texto "Se crearán N horarios".
- Ante `409`: el modal permanece abierto, conserva todos los datos y marca cada turno de la previsualización que figura en `conflicts`, mostrando el tipo (sala, instructor o dentro del lote) y la actividad/horario con el que choca.

### D4: Calendario semanal reutilizable (`ScheduleCalendarComponent`)
- Ubicación: `frontend/src/app/features/catalog/schedule-calendar/`. Componente de presentación: recibe `schedules`, `canManage` y emite `scheduleSelected`.
- Usado por `SchedulesManagementComponent` (portal) y `SchedulesComponent` (`/horarios`), ambos con un conmutador **Calendario / Tarjetas**; Calendario es la vista por defecto en pantallas `md` o mayores.
- Grilla con 7 columnas (Lunes a Domingo) y eje horario cada 30 min. El rango visible va desde la hora de inicio más temprana hasta la hora de fin más tardía de los horarios visibles, redondeadas a la hora (por defecto 07:00–22:00 si no hay horarios).
- Bloques posicionados por hora de inicio y altura proporcional a la duración. Los bloques simultáneos del mismo día se reparten en subcolumnas (algoritmo de agrupamiento de solapes).
- Cada bloque muestra hora, actividad, sala e instructor (truncados según espacio).
- Color por actividad: se toma de una paleta fija de Tailwind indexada por un hash estable del `activityId`; no se persiste.
- Navegación por semana (anterior / actual / siguiente): se muestran solo los horarios cuya vigencia se superpone con la semana visible (`ValidFrom <= domingo` y `ValidTo` nulo o `>= lunes`). Así dos horarios con vigencias sucesivas en la misma franja no se superponen visualmente.
- Pantallas `< md`: selector de día con lista cronológica de bloques del día elegido.
- Sin dependencias nuevas; Tailwind + signals.

### D5: Filtro multi-actividad
- Barra superior con chip "Todas las actividades" y un chip por cada actividad presente en los horarios cargados (con el mismo color del calendario).
- Estado en signals: `selectedActivityIds: Set<Guid>` vacío = todas. Seleccionar "Todas" vacía la selección; desmarcar la última vuelve a "Todas".
- Indicador "N horarios visibles" según filtro y semana.
- El filtro aplica tanto a la vista calendario como a la de tarjetas.

### D6: Detalle al clic (`ScheduleDetailModalComponent`)
- Ubicación: `frontend/src/app/features/catalog/schedule-detail-modal/`.
- Muestra actividad, día, horario, sala, instructor, secretario (si existe), cupo y vigencia. No muestra clases asociadas (fuera de alcance).
- Si el usuario es `Administrator` o `Secretary`: botón **Editar** (abre `ScheduleFormModalComponent` en modo edición con ese turno) y **Dar de baja** (abre la confirmación existente con la advertencia de cancelación de clases futuras). Para el resto de los usuarios el detalle es de solo lectura.

## Risks / Trade-offs

- **[Riesgo] Horarios existentes con instructor superpuesto** → su edición pasará a responder `409` aunque solo se cambie el cupo. *Mitigación*: el mensaje indica el horario con el que choca; antes de desplegar se puede consultar la base para detectar estos casos.
- **[Riesgo] Atomicidad no verificable con EF InMemory** → *Mitigación*: prueba de integración sobre PostgreSQL que provoca un fallo después del guardado de horarios y verifica que no quedan horarios ni clases.
- **[Riesgo] Lotes grandes generan muchas clases concretas dentro de una sola transacción** → *Mitigación*: límite de 50 turnos por lote.
- **[Riesgo] Bloques simultáneos ilegibles en el calendario** → *Mitigación*: subcolumnas por solape, truncado de texto y detalle completo al clic.
- **[Trade-off] Calendario en `/horarios`** amplía el alcance a la vista pública, a cambio de una única implementación y una experiencia coherente. Si se decide limitarlo al portal, basta con no integrarlo en `SchedulesComponent` (tarea 4.3).
