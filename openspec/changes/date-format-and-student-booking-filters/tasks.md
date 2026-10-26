## 1. Utilidades y Pipe de Formato de Fecha DD/MM/AAAA

- [x] 1.1 (TDD Red) Crear pruebas unitarias en `frontend/src/app/core/utils/date-format.spec.ts` para verificar `formatDateDisplay`, `formatDateTimeDisplay`, `formatTimeDisplay` (estrictamente `HH:mm` sin segundos a partir de Date, timestamp o TimeOnly), `parseDateInput`, `toDisplayDateString` e `isValidDateParts` ante valores nulos, fechas ISO puras (`yyyy-MM-dd`), cadenas con segundos (`"18:00:00"`), fechas completas con hora UTC, años bisiestos y entradas inválidas.
- [x] 1.2 (TDD Green) Implementar las utilidades en `frontend/src/app/core/utils/date-format.ts`, registrar `localeEsAr` y configurar `LOCALE_ID` con `'es-AR'` en `frontend/src/app/app.config.ts`.
- [x] 1.3 (TDD Red) Crear pruebas unitarias en `frontend/src/app/shared/pipes/app-date.pipe.spec.ts` verificando que formatee fechas a `DD/MM/AAAA` (y `DD/MM/AAAA HH:mm` con argumento `'time'`).
- [x] 1.4 (TDD Green) Implementar el pipe standalone `AppDatePipe` (`appDate`) en `frontend/src/app/shared/pipes/app-date.pipe.ts`.

## 2. Directiva de Ingreso de Fechas en Formato DD/MM/AAAA

- [x] 2.1 (TDD Red) Crear pruebas unitarias en `frontend/src/app/shared/directives/date-input.directive.spec.ts` cubriendo:
  - Formateo progresivo durante la escritura (inserción automática de separadores `/` tras día y mes).
  - Emisión del valor ISO `yyyy-MM-dd` al formulario cuando se completa una fecha válida.
  - Emisión de `null` y marcado de error `invalidDate` ante fechas incompletas o no válidas (ej. 31/02/2026).
  - Sincronización desde el modelo hacia la vista (`writeValue`) formateando `yyyy-MM-dd` a `DD/MM/AAAA`.
  - Comportamiento al perder el foco (`blur`).
- [x] 2.2 (TDD Green) Implementar `DateInputDirective` (`appDateInput`) en `frontend/src/app/shared/directives/date-input.directive.ts`.

## 3. Filtros en la Sección "Clases Disponibles para Reservar" del Portal del Alumno

- [x] 3.1 (TDD Red) Actualizar y extender pruebas en `frontend/src/app/features/portal/student/student-portal.spec.ts` para verificar:
  - Filtro por actividad (`filterActivityId`: ver todas o una actividad puntual).
  - Filtro por fecha (`filterDate`: todas, hoy, mañana, o fecha puntual en `DD/MM/AAAA`).
  - Filtro por franja horaria (`filterTimeSlot`: Mañana [<12:00], Tarde [12:00-17:59], Noche [>=18:00]).
  - Filtro por disponibilidad de cupos (`filterOnlyAvailableSpots`).
  - Filtro por sala y búsqueda por texto (actividad e instructor).
  - Contador de clases mostradas vs. total y botón de restablecimiento de filtros (`resetFilters`).
  - Exhibición de fechas de clases, reservas y membresías con formato `DD/MM/AAAA`.
- [x] 3.2 (TDD Green) Implementar la barra de filtros interactiva, signals reactivos, computeds (`filteredAvailableClasses`, `availableActivities`, `availableRooms`, etc.) y controles de plantilla en `StudentPortalComponent` (`frontend/src/app/features/portal/student/student-portal.ts`).

## 4. Estandarización de Fechas (DD/MM/AAAA) en Todas las Pantallas y Formularios

- [x] 4.1 Actualizar `PersonFormComponent`, `PersonDetailComponent`, `PeopleListComponent` y `MyProfileComponent` para que el campo `birthDate` use `appDateInput` (`DD/MM/AAAA`) y las fechas de listado, ficha y perfil se exhiban con `appDate` en `DD/MM/AAAA` (o `DD/MM/AAAA HH:mm`), actualizando sus respectivas pruebas.
- [x] 4.2 Actualizar `AssignMembershipModalComponent` para que el campo `startDate` use `appDateInput` (`DD/MM/AAAA`) y el detalle de membresía exhiba el rango con `appDate` (`DD/MM/AAAA`), actualizando sus pruebas.
- [x] 4.3 Actualizar `ScheduleFormModalComponent`, `ScheduleBatchModalComponent`, `ScheduleDetailModalComponent` y `AdminPortalComponent` para que las vigencias de horarios y promociones se ingresen con `appDateInput`, se visualicen con `appDate` en `DD/MM/AAAA`, y en reportes gerenciales o auditoría las horas se muestren en `HH:mm` sin segundos (ej. `log.timestampUtc` con `appDate:'time'`), actualizando sus pruebas.
- [x] 4.4 Actualizar `StaffPortalComponent`, `NotificationsCenterComponent` y `NavbarComponent` para que los filtros de fecha utilicen `appDateInput` y las tablas de clases, reservas, auditoría y notificaciones exhiban fechas en `DD/MM/AAAA` y horarios estrictamente en `HH:mm` sin segundos.

## 5. Verificación Integral

- [x] 5.1 Ejecutar la suite completa de pruebas unitarias de frontend (`npm test -- --watch=false` en `frontend/`) y backend (`dotnet test` en `backend/`) para verificar que todas las pruebas pasen sin regresiones.
- [x] 5.2 Ejecutar la compilación del frontend (`npm run build` en `frontend/`) comprobando que no existan errores de tipos ni de empaquetado.
