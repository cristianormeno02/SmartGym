## Context

Ver `proposal.md` para la motivación y `specs/reservations/spec.md` (junto con las deltas en `people-management`, `memberships` y `activities-schedule`) para los requerimientos normativos.

Estado actual relevante del código frontend:
1. **Configuración de Locale**: En `app.config.ts`, no se provee `LOCALE_ID` ni se registran los datos de locale de Angular para español de Argentina (`@angular/common/locales/es-AR`), provocando que los pipes por defecto (`date:'shortDate'`, `date:'mediumDate'`, etc.) formateen en el locale `en-US` (`M/d/yy`, nombres en inglés).
2. **Visualización de Fechas**: Se utilizan pipes heterogéneos (`date:'shortDate'`, `date:'mediumDate'`, `date:'short'`, `date:'medium'`) que varían según el navegador y la vista, exhibiendo meses antes del día o nombres en inglés.
3. **Ingreso de Fechas en Formularios**: Se utiliza `<input type="date">` nativo en:
   - `person-form.ts` (`birthDate`)
   - `assign-membership-modal.ts` (`startDate`)
   - `schedule-form-modal.ts` (`validFrom`, `validTo`)
   - `schedule-batch-modal.ts` (`validFrom`, `validTo`)
   - `staff-portal.ts` (`filterFrom`, `filterTo`)
   - `student-portal.ts` (`uploadExpiryDate`)
   El elemento nativo `<input type="date">` muestra la fecha en el formato configurado por el sistema operativo o navegador del cliente, que frecuentemente no es `DD/MM/AAAA`.
4. **Portal del Alumno (`StudentPortalComponent`)**:
   - `loadAvailableSessions()` consulta las clases de los próximos 7 días con `gymApi.getAvailableClasses({ from: today, to: nextWeek })`.
   - Las clases se almacenan en el signal `availableClasses` y se renderizan directamente en la grilla sin ningún control de filtrado, franja horaria, actividad o búsqueda.

## Goals / Non-Goals

**Goals:**
- Configurar formalmente el locale `es-AR` en Angular.
- Desarrollar utilidades puras `date-format.ts` y un pipe standalone `AppDatePipe` (`appDate`) que garantice el formato `DD/MM/AAAA` (y `DD/MM/AAAA HH:mm` cuando se requiera hora) para listar fechas en cualquier parte del sistema, así como la supresión estricta de segundos (`HH:mm`) en cualquier reporte, bitácora o grilla de horarios.
- Desarrollar una directiva de control de formulario `DateInputDirective` (`appDateInput`) para inputs `<input type="text" placeholder="DD/MM/AAAA" appDateInput>` que permita cargar fechas con máscara/formato `DD/MM/AAAA`, validando la fecha ingresada y exponiendo el valor ISO `yyyy-MM-dd` al `FormControl` o `ngModel`.
- Actualizar de manera homogénea todos los formularios y vistas que listen o carguen fechas en el frontend.
- Implementar en la sección de clases disponibles de `StudentPortalComponent` una barra de filtros interactiva y completa que permita filtrar por:
  - Actividad (todas o actividad puntual).
  - Fecha (todas, hoy, mañana o fecha puntual `DD/MM/AAAA`).
  - Franja horaria (todas, mañana [<12:00], tarde [12:00-17:59], noche [>=18:00]).
  - Disponibilidad de cupos (todas vs solo con lugares disponibles).
  - Sala (todas las salas o sala puntual).
  - Búsqueda por texto (actividad o instructor).
  - Contador de resultados y botón de limpieza de filtros.

**Non-Goals:**
- Modificaciones en la base de datos o en los contratos de la API REST del backend: los endpoints continúan recibiendo y emitiendo `DateOnly` (`yyyy-MM-dd`) y `DateTime` UTC (ISO 8601).
- Modificar la lógica de negocio de asignación o reserva en el backend.

## Decisions

### D1: Utilidades puras `date-format.ts`, pipe `appDate` y locale `es-AR`
- **Registro de Locale**:
  - En `app.config.ts`, registrar `localeEsAr` mediante `registerLocaleData(localeEsAr, 'es-AR')` y proveer `{ provide: LOCALE_ID, useValue: 'es-AR' }`.
- **Utilidades Puras (`src/app/core/utils/date-format.ts`)**:
  - `formatDateDisplay(value: Date | string | null | undefined): string`: Devuelve cadena en formato `DD/MM/AAAA` (ej. `"26/10/2026"`). Maneja cadenas ISO de solo fecha (`"2026-10-26"`) interpretándolas en hora local para evitar desfasajes de zona horaria, así como objetos `Date` y timestamps ISO completos.
  - `formatDateTimeDisplay(value: Date | string | null | undefined): string`: Devuelve cadena en formato `DD/MM/AAAA HH:mm` (ej. `"26/10/2026 18:30"`), suprimiendo siempre los segundos.
  - `formatTimeDisplay(value: Date | string | null | undefined): string`: Formatea horarios (cadena `"18:00:00"`, `"09:30"`, o fecha completa) estrictamente en formato `HH:mm` (ej. `"18:00"`), sin mostrar segundos bajo ninguna circunstancia.
  - `parseDateInput(value: string | null | undefined): string | null`: Recibe una cadena ingresada por el usuario (ej. `"26/10/2026"` o `"26102026"`), valida que corresponda a una fecha del calendario real válida y devuelve la cadena ISO `yyyy-MM-dd` (`"2026-10-26"`), o `null` si es incompleta o inválida.
  - `toDisplayDateString(isoDate: string | null | undefined): string`: Convierte de `"yyyy-MM-dd"` a `"DD/MM/AAAA"`.
  - `isValidDateParts(day: number, month: number, year: number): boolean`: Valida días por mes, años bisiestos y rangos razonables de año (ej. 1900 a 2100).
- **Pipe Standalone `AppDatePipe` (`src/app/shared/pipes/app-date.pipe.ts`) y `AppTimePipe`**:
  - `appDate`: `{{ val | appDate }}` para `DD/MM/AAAA`, o `{{ val | appDate:'time' }}` para `DD/MM/AAAA HH:mm` (sin segundos).
  - Admite el formateo de horarios puros (`{{ val | appDate:'timeOnly' }}` o pipe auxiliar de tiempo) formateando `HH:mm`.
  - En cualquier reporte, tabla de auditoría (`log.timestampUtc`) o vista operativa, se reemplaza `date:'medium'` por `appDate:'time'` garantizando que nunca se exhiban segundos.

### D2: Directiva de Ingreso `DateInputDirective` (`src/app/shared/directives/date-input.directive.ts`)
- Implementa `ControlValueAccessor` y `Validator` para integrarse de forma transparente con `formControlName`, `formControl` y `[(ngModel)]`.
- Selector: `input[appDateInput]`. Atributos por defecto: `type="text"`, `inputmode="numeric"`, `placeholder="DD/MM/AAAA"`, `maxlength="10"`.
- **Comportamiento en entrada de texto (`input` event)**:
  - Permite solo números.
  - Inserta automáticamente las barras `/` al tipear el segundo y el cuarto dígito (ej. si el usuario tipea `2610`, formatea como `26/10/`).
  - Al completar 10 caracteres (`DD/MM/AAAA`), valida la fecha; si es válida, emite al modelo reactivo el valor ISO `yyyy-MM-dd`. Si es incompleta o inválida, emite `null` y activa error de validación `invalidDate: true`.
- **Comportamiento al recibir valor del modelo (`writeValue`)**:
  - Si recibe un valor ISO `yyyy-MM-dd` (o `Date`), lo formatea a `DD/MM/AAAA` para su visualización en el `<input>`.
  - Si recibe `null` o vacío, limpia el input.
- **Comportamiento en desenfoque (`blur`)**:
  - Si el usuario dejó una fecha incompleta o con valores inválidos (ej. `31/02/2026`), marca el campo como `touched` e inválido.

### D3: Arquitectura de Filtros de Clases Disponibles en `StudentPortalComponent`
- En `StudentPortalComponent`:
  - `availableClasses = signal<ReservableClassSession[]>([])`: clases recuperadas de la API para la ventana de días (por defecto próximos 7 días).
  - Signals para el estado de los filtros:
    - `filterActivityId = signal<string>('ALL')`
    - `filterDate = signal<string>('ALL')` (puede ser `'ALL'`, `'TODAY'`, `'TOMORROW'`, o una fecha ISO `'YYYY-MM-DD'`)
    - `filterTimeSlot = signal<string>('ALL')` (`'ALL'`, `'MORNING'` [<12:00], `'AFTERNOON'` [12:00-17:59], `'NIGHT'` [>=18:00])
    - `filterOnlyAvailableSpots = signal<boolean>(false)` (cuando es `true`, filtra `c.availableSpots > 0`)
    - `filterRoomId = signal<string>('ALL')`
    - `filterSearchText = signal<string>('')`
  - Computeds derivados:
    - `availableActivities = computed(() => ...)`: lista deduplicada de `{ id, name }` a partir de `availableClasses()`, ordenada alfabéticamente.
    - `availableRooms = computed(() => ...)`: lista deduplicada de salas `{ id, name }`.
    - `availableDateOptions = computed(() => ...)`: lista de fechas únicas presentes en las clases disponibles, formateadas como `{ iso, display: 'DD/MM/AAAA' }`.
    - `filteredAvailableClasses = computed(() => ...)`: filtra `availableClasses()` aplicando en memoria todos los criterios activos de forma instantánea.
    - `hasActiveFilters = computed(() => ...)`: boolean que indica si algún filtro difiere de su estado por defecto.
  - Métodos de control:
    - `resetFilters()`: restablece todos los signals de filtro a sus valores predeterminados.
  - Template:
    - Barra de filtros responsiva encima de la grilla de clases.
    - Chips/selectores estilizados con Tailwind acordes al diseño oscuro (`bg-slate-950`, bordes `border-slate-800`).
    - Indicador visual: "Mostrando X de Y clases disponibles".
    - Estado vacío con sugerencia de "Limpiar filtros" cuando `filteredAvailableClasses().length === 0` pero `availableClasses().length > 0`.

### D4: Reemplazo Uniforme en Vistas de Listado y Carga de Fechas
- Reemplazar en plantillas todas las combinaciones ambiguas de `| date:'shortDate'`, `| date:'mediumDate'`, `| date:'short'` y `| date:'medium'` por `| appDate` o `| appDate:'time'`.
- Reemplazar en los formularios los `<input type="date">` por `<input type="text" appDateInput ...>` o aplicar la directiva manteniendo las etiquetas y mensajes claros con `DD/MM/AAAA`.

## Risks / Trade-offs

- **[Riesgo] Manejo de zona horaria al parsear fechas ISO de solo fecha (`yyyy-MM-dd`)**:
  - *Mitigación*: `new Date("2026-10-26")` en JavaScript interpreta en UTC a medianoche, lo que en huso horario `UTC-3` puede mostrar el día anterior (25/10). Las utilidades en `date-format.ts` parsearán cadenas `yyyy-MM-dd` separando componentes `[year, month, day]` para construir la fecha local exacta sin desplazamientos.
- **[Riesgo] Fechas ingresadas manualmente de forma incompleta durante la escritura**:
  - *Mitigación*: La directiva formatea dinámicamente agregando `/` solo al tipear, y no dispara errores prematuros hasta que se hayan ingresado los 10 caracteres o el usuario abandone el foco (`blur`).
- **[Riesgo] Rendimiento al filtrar clases en memoria**:
  - *Mitigación*: La cantidad de clases en una ventana de 7 a 14 días típicamente ronda entre 20 y 80 elementos; el cálculo en el computed signal de Angular es de costo despreciable (< 1ms) y ofrece respuesta instantánea sin peticiones HTTP redundantes.
