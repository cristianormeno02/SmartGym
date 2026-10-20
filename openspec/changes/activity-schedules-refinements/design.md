## Context

Ver `proposal.md` para la motivación y `specs/activities-schedule/spec.md` para los requerimientos normativos.

Estado actual relevante del código:
- `RecurringScheduleService.ValidateResourcesAsync` acepta como secretario a cualquier `Person` activa que tenga `PersonRole` activa con `Role.Name == "Secretary"` o `Role.Name == "Administrator"`, sin verificar si posee cuenta de usuario (`User`).
- `loadSecretaryCandidates` en `frontend/src/app/features/catalog/schedule-secretary-options.ts` consulta `GET /api/people` tanto con `role: 'Secretary'` como con `role: 'Administrator'` y no filtra por `hasUserAccount`.
- `RecurringScheduleDto` (`RecurringScheduleDtos.cs`) y `RecurringSchedule` (`gym.models.ts`) exponen `ActivityId` y `ActivityName`, pero no `ActivityColorHex` (aunque `RecurringScheduleService` ya hace `.Include(s => s.Activity)`).
- `ScheduleCalendarComponent` (`schedule-calendar.ts`) calcula un rango horario continuo (`Math.min(minH, 7)` a `Math.max(maxH, 22)`, mostrando siempre al menos de 07:00 a 22:00) y asigna colores desde un arreglo `PALETTES` de clases Tailwind indexado por `hashString(activityId)`.
- `SchedulesManagementComponent` y `SchedulesComponent` (`/horarios`) tienen una barra de filtrado únicamente por actividades (`selectedActivityIds`).

## Goals / Non-Goals

**Goals:**
- Restringir en backend y frontend los candidatos y asignaciones de secretario en horarios recurrentes a personas activas con cuenta de usuario activa y rol `Secretary` activo.
- Exponer `ActivityColorHex` en `RecurringScheduleDto` y usarlo en los bloques del calendario (escritorio y móvil) y en los chips de actividades.
- Incorporar un filtro por sala combinable con el de actividades tanto en la vista Calendario como en la vista Tarjetas (`SchedulesManagementComponent` y `SchedulesComponent`).
- Hacer que el calendario muestre por defecto únicamente las horas con actividades programadas en la semana visible, con un conmutador para ver todo el rango horario completo.

**Non-Goals:**
- Modificar las reglas de reemplazo de secretario en `ClassSessionService` (capacidad `class-sessions`, fuera del alcance de programación de horarios recurrentes).
- Cambios de esquema o migraciones en la base de datos (`Activity.ColorHex`, `User` y `PersonRole` ya existen).

## Decisions

### D1: Validación y carga de secretarios con usuario activo y rol `Secretary`
- **Backend (`RecurringScheduleService.ValidateResourcesAsync`)**:
  - Al validar `secretaryId`, incluir `.Include(p => p.User)` además de `.Include(p => p.PersonRoles).ThenInclude(pr => pr.Role)`.
  - Exigir que `secretary != null`, `secretary.IsActive`, `secretary.User != null && secretary.User.IsActive`, y `secretary.PersonRoles.Any(pr => pr.IsActive && pr.Role.Name == RoleType.Secretary.ToString())`.
  - Si no cumple, lanzar `ArgumentException("El secretario especificado no existe, no posee cuenta de usuario activa o no tiene el rol de Secretario asignado.")` (mapeado a `400 Bad Request`).
- **Frontend (`schedule-secretary-options.ts`)**:
  - `loadSecretaryCandidates(peopleService)` realizará una única llamada `peopleService.search({ role: 'Secretary', status: PersonStatus.Active, pageSize: 100 })` y filtrará los ítems con `p.hasUserAccount === true`.
  - En modo edición (`ScheduleFormModalComponent`), si el horario ya tenía un `secretaryId` que no aparece entre las opciones activas, se conserva en el selector para no romper la edición de horarios históricos.

### D2: Transporte y renderizado del color de la actividad (`ActivityColorHex`)
- **Backend**:
  - Agregar `string? ActivityColorHex = null` a `RecurringScheduleDto` (con valor por defecto `null` para mantener compatibilidad posicional en constructores de pruebas existentes si fuera necesario, o posicionado junto a `ActivityName` / como parámetro opcional). Posicionarlo como parámetro opcional al final o después de `DivergentSessionIds` evita romper llamadas posicionales existentes, o bien actualizar el record y su `MapToDto`:
    ```csharp
    public record RecurringScheduleDto(
        Guid Id,
        Guid ActivityId,
        string ActivityName,
        Guid RoomId,
        string RoomName,
        Guid InstructorId,
        string InstructorName,
        Guid? SecretaryId,
        string? SecretaryName,
        DayOfWeek DayOfWeek,
        TimeOnly StartTime,
        TimeOnly EndTime,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        int? MaxCapacity,
        bool IsActive,
        IReadOnlyList<Guid>? DivergentSessionIds = null,
        string? ActivityColorHex = null
    );
    ```
  - En `RecurringScheduleService.MapToDto`, pasar `s.Activity?.ColorHex`.
- **Frontend**:
  - Agregar `activityColorHex?: string | null` en la interfaz `RecurringSchedule` (`gym.models.ts`).
  - En `ScheduleCalendarComponent`, aplicar estilos dinámicos basados en `schedule.activityColorHex` (normalizado o fallback `#06B6D4` cyan si la actividad no tiene `colorHex`):
    - Fondo translúcido (`backgroundColor: hexWithAlpha(color, 0.18)`), borde (`borderColor: hexWithAlpha(color, 0.55)`) e indicador/acento lateral o texto claro para garantizar contraste sobre fondo oscuro `slate-900`.
  - En la barra de filtros de actividades (`SchedulesManagementComponent` y `SchedulesComponent`), mostrar un punto/indicador con el color `colorHex` de cada actividad.

### D3: Filtro por Sala combinado con el de Actividades
- En `SchedulesManagementComponent` y `SchedulesComponent`:
  - Computar `uniqueRooms` a partir de los horarios cargados (y/o salas disponibles): `Array<{ id: string; name: string }>` ordenado por nombre.
  - Agregar signal `selectedRoomIds = signal<Set<string>>(new Set())` donde el conjunto vacío representa **"Todas las salas"** (`isAllRoomsSelected`).
  - Ofrecer controles de filtro por sala ("Todas las salas" + chips/botones por cada sala; al desmarcar la última sala vuelve a "Todas las salas").
  - `filteredSchedules` filtrará por `selectedActivityIds` **y** `selectedRoomIds`:
    ```ts
    return all.filter(s =>
      (selectedActivities.size === 0 || selectedActivities.has(s.activityId)) &&
      (selectedRooms.size === 0 || selectedRooms.has(s.roomId))
    );
    ```
  - Este `filteredSchedules()` alimenta tanto a `<app-schedule-calendar>` como a la vista de tarjetas por día.

### D4: Calendario compactado a horarios con actividades por defecto + opción de rango completo
- **Problema actual**: `hourRange` fuerza `Math.min(minH, 7)` y `Math.max(maxH, 22)` y dibuja todas las horas intermedias aunque solo haya clases a las 08:00–09:00 y a las 20:00–21:00.
- **Diseño**:
  - Agregar en `ScheduleCalendarComponent` el signal `showFullHourRange = signal<boolean>(false)` con un botón/switch en la barra de herramientas del calendario (ej. `"Solo horarios con actividad"` / `"Ver rango completo"`).
  - **Modo por defecto (`showFullHourRange() === false` — Solo horarios con actividad)**:
    - A partir de `visibleSchedules()`, determinar qué horas enteras `[h, h+1)` están ocupadas por al menos un horario vigente en la semana (`startMin < (h + 1) * 60 && endMin > h * 60`), ordenadas de menor a mayor (`activeHours: number[]`).
    - Cada hora activa ocupa una banda vertical de `HOUR_HEIGHT_PX` (60 px) en la grilla. Si un horario abarca varias horas (ej. 08:00 a 10:00 o 08:30 a 09:30), las horas que toca (`8` y `9`) son contiguas y el bloque se posiciona usando el índice de su hora de inicio dentro de `activeHours` más el desplazamiento de minutos dentro de esa hora.
    - De esta manera, las horas vacías (sin ninguna actividad en toda la semana visible) no se renderizan y los bloques quedan compactados únicamente a las franjas con actividad.
  - **Modo rango completo (`showFullHourRange() === true`)**:
    - Muestra la secuencia continua de horas `startHour..endHour` cubriendo el rango horario completo del día (por ejemplo de `Math.min(minH, 7)` a `Math.max(maxH, 22)`).

## Risks / Trade-offs

- **[Riesgo] Horarios que cruzan más de una hora con minutos parciales en vista compacta** → *Mitigación*: como toda hora `[h, h+1)` tocada por el intervalo `[startMin, endMin)` de cualquier horario se incluye en `activeHours`, todas las horas que abarca un mismo horario son siempre consecutivas en `activeHours`, por lo que su altura `(duration / 60) * HOUR_HEIGHT_PX` y su posición `topPx` son exactas y sin cortes.
- **[Riesgo] Horarios existentes con un secretario que perdió su usuario o rol** → *Mitigación*: en el formulario de edición se mantiene el secretario actual en el selector si no estuviera en la lista; si se intenta guardar con un secretario inválido en backend, el backend devuelve `400` con mensaje claro para que el operador elija uno válido o "Sin secretario asignado".
