## Context

En el backend de SmartGym, la asignación directa de membresías está soportada por el endpoint `POST /api/memberships/assign`, el cual recibe `{ studentId: Guid, membershipPlanId: Guid, startDate: DateOnly? }` y ejecuta la contratación con `ActivateImmediately = true` y motivo `"Asignación directa por personal"`. Además, la consulta de membresías de un alumno se realiza mediante `GET /api/memberships/student/{studentId}`.

En el frontend, la ficha de persona (`frontend/src/app/features/people/person-detail.ts`) concentra la información del cliente/personal (datos personales, domicilio, contacto de emergencia, auditoría y aprovisionamiento/roles de usuario), pero carece de un bloque para visualizar o asignar membresías. Para permitir a los alumnos reservar actividades (que requieren membresía activa), el personal administrativo necesita poder realizar esta asignación de forma ágil desde dicha pantalla.

## Goals / Non-Goals

**Goals:**
- Extender el servicio `GymApiService` para soportar la asignación de membresías (`assignMembership`) hacia `POST /api/memberships/assign`.
- Incorporar en la ficha de persona (`PersonDetailComponent`) una tarjeta de **Membresías del Alumno** que muestre las membresías registradas (activas, vencidas, canceladas) con sus créditos y vigencia.
- Proveer un modal dedicado (`AssignMembershipModalComponent`) para que el personal seleccione un plan de membresía activo, defina la fecha de inicio (por defecto hoy, sin permitir fechas pasadas), revise las condiciones del plan (precio con `MoneyAmountPipe`, duración, créditos) y confirme la asignación.
- Actualizar de forma reactiva el listado tras la asignación exitosa y manejar adecuadamente los errores y restricciones (persona no activa o sin rol de Alumno).
- Garantizar que todo el flujo esté cubierto con pruebas unitarias exhaustivas en Vitest.

**Non-Goals:**
- Modificar el backend .NET (el controlador `MembershipsController` y los servicios de aplicación ya cuentan con la lógica y pruebas necesarias).
- Implementar pasarelas de pago o cobros con tarjeta en este flujo (la asignación administrativa activa la membresía directamente sin pasar por pasarela externa).
- Modificar la vista del portal del alumno (`StudentPortalComponent`), la cual ya lee y muestra la membresía activa cuando existe.

## Decisions

### D1: Integración en `GymApiService` y modelos (`gym.models.ts`, `gym-api.service.ts`)
- Se agregará la interfaz a `gym.models.ts`:
  ```typescript
  export interface AssignMembershipRequest {
    studentId: string;
    membershipPlanId: string;
    startDate?: string; // Formato YYYY-MM-DD
  }
  ```
- En `GymApiService`:
  ```typescript
  assignMembership(request: AssignMembershipRequest): Observable<MembershipDetail> {
    return this.http.post<MembershipDetail>(`${this.apiUrl}/memberships/assign`, request);
  }
  ```

### D2: Componente Modal Standalone `AssignMembershipModalComponent`
Para mantener la modularidad y no sobrecargar excesivamente `PersonDetailComponent`, se creará un componente modal específico:
- **Ruta**: `frontend/src/app/features/catalog/assign-membership-modal/assign-membership-modal.ts` (o dentro de `features/people/assign-membership-modal/`).
- **Inputs**:
  - `studentId`: string (identificador de la persona).
  - `studentName`: string (nombre completo para mostrar en el encabezado).
- **Outputs**:
  - `assigned`: EventEmitter<MembershipDetail> (emite al completarse exitosamente la asignación).
  - `cancelled`: EventEmitter<void> (emite al cerrar sin guardar).
- **Comportamiento interno**:
  - Carga los planes de membresía activos al abrirse (`gymApi.getMembershipPlans(true)`).
  - Formulario reactivo o con signals:
    - `selectedPlanId`: plan seleccionado por el usuario.
    - `startDate`: string en formato `YYYY-MM-DD`, inicializado con la fecha de hoy local. Atributo `min` fijado en la fecha actual para prevenir fechas retroactivas.
  - Previsualización dinámica de condiciones:
    - Al seleccionar un plan, calcula la fecha de finalización aproximada (`startDate + durationDays - 1`).
    - Exhibe precio formateado (`$XX.XXX,XX` con `MoneyAmountPipe`), cupo de créditos (`X créditos` o `Pase Libre Ilimitado`), y detalle de actividades habilitadas.
  - Estado de carga (`isSubmitting`) y mensaje de error en caso de fallo en la API.

### D3: Incorporación en `PersonDetailComponent`
- **Condición de visualización**:
  - La sección se visualiza cuando el usuario actual es personal del gimnasio (`authService.isStaff()`).
  - Para personas que tienen el rol de `Student`, el botón `+ Asignar Membresía` se encuentra disponible.
  - Si la persona no es activa (`status !== PersonStatus.Active`), el botón se deshabilita con una explicación clara.
  - Si la persona aún no tiene el rol de `Student` asignado, se muestra una indicación amigable para que el administrador le asigne el rol desde la tarjeta de roles en la misma pantalla.
- **Carga de datos**:
  - Al iniciar el componente o cargar la persona, se invoca `gymApi.getStudentMemberships(personId)`.
  - Muestra un estado de carga (`isLoadingMemberships`), un estado vacío si no tiene contrataciones previas ("No posee membresías registradas"), y las tarjetas o tabla de membresías.
- **Respuesta a la asignación**:
  - Cuando el modal emite `assigned`, se cierra el modal, se dispara una notificación de éxito (`feedback.set('Membresía asignada exitosamente.')`) y se refresca el listado de membresías llamando a `loadMemberships(personId)`.

### D4: Formato Visual y Diseño
- Sigue el patrón visual estético oscuro (Dark Slate UI) con acentos de color coherentes:
  - Insignias de estado:
    - `Active`: Verde esmeralda (`bg-emerald-950/50 text-emerald-400 border-emerald-800`).
    - `Pending`: Ámbar (`bg-amber-950/50 text-amber-400 border-amber-800`).
    - `Expired`: Pizarra/Gris (`bg-slate-800 text-slate-400 border-slate-700`).
    - `Suspended`: Azul/Índigo (`bg-indigo-950/50 text-indigo-400 border-indigo-800`).
    - `Cancelled`: Rosa/Rojo (`bg-rose-950/50 text-rose-400 border-rose-800`).
  - Botón principal de asignación: Estilo destacado con icono `+` y tono esmeralda o cian (`bg-emerald-600 hover:bg-emerald-500 text-white font-bold text-xs px-4 py-2 rounded-xl transition`).

## Risks / Mitigations

- **Riesgo**: La persona existe en `People`, pero no tiene cuenta de usuario o no tiene el rol `Student`.
  - *Mitigación*: El backend valida estrictamente `hasStudentRole`. La interfaz del frontend detecta si el usuario tiene rol `Student` (a través de `userAccount().roles`), previene el envío anticipadamente o explica el error claramente si el backend retorna `400 ValidationException` ("La persona especificada no posee el rol de Alumno activo").
- **Riesgo**: Selección de fecha en el pasado o inválida.
  - *Mitigación*: Se configura `min` en el input `type="date"` y se valida en el formulario que la fecha sea mayor o igual a hoy.
- **Riesgo**: Doble clic provocando asignaciones duplicadas.
  - *Mitigación*: Deshabilitar el botón de confirmación mientras `isSubmitting` esté activo.
