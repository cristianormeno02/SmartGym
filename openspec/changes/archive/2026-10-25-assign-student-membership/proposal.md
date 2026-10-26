## Why

Para que los alumnos puedan reservar clases en el gimnasio, el sistema exige que cuenten con una membresía activa y vigente (además de su apto médico para las actividades presenciales que lo requieran). 

Actualmente, el backend de SmartGym ya cuenta con la lógica y el endpoint para la asignación y contratación directa de membresías por parte del personal administrativo (`POST /api/memberships/assign` en `MembershipsController`), así como la consulta de membresías por alumno (`GET /api/memberships/student/{studentId}`). Sin embargo, en el frontend no existe ninguna interfaz ni botón para que secretarios o administradores puedan asignar una membresía a un alumno desde su ficha ni visualizar el historial o estado de sus membresías.

Implementar la pantalla y flujo de asignación de membresía en el frontend (`/portal/personas/:id`) es el paso clave pendiente para habilitar el circuito de reservas y pruebas integrales de alumnos.

## What Changes

- **Modelos y Servicio API de Frontend (`gym.models.ts`, `gym-api.service.ts`)**:
  - Definir la interfaz `AssignMembershipRequest` (`studentId`, `membershipPlanId`, `startDate?`).
  - Agregar el método `assignMembership(request: AssignMembershipRequest): Observable<MembershipDetail>` en `GymApiService` para consumir `POST /api/memberships/assign`.
- **Componente Modal de Asignación de Membresía (`assign-membership-modal`)**:
  - Componente modal interactivo para que el personal elija un plan activo (`GET /api/membershipplans?onlyActive=true`).
  - Selector de fecha de inicio (`startDate`), por defecto hoy en hora local (`YYYY-MM-DD`) con restricción de no permitir fechas pasadas (`min = today`).
  - Previsualización comercial del plan seleccionado:
    - Nombre del plan y descripción.
    - Precio formateado con dos decimales y separador de miles (`$35.000,00` mediante `MoneyAmountPipe`).
    - Duración en días y cálculo de fecha de vencimiento proyectada.
    - Modalidad de acceso: Pase ilimitado (`∞`) o cantidad de créditos iniciales.
    - Alcance de actividades: "Todas las actividades" o detalle de actividades permitidas.
  - Validación de campos, indicador de progreso (`isSubmitting`), bloqueo de doble clic y manejo de errores devueltos por el backend (por ejemplo si la persona no tiene el rol de Alumno o se encuentra inactiva).
- **Integración en la Ficha de Persona (`PersonDetailComponent` en `/portal/personas/:id`)**:
  - Incorporar una sección dedicada a **Membresías del Alumno** visible para el personal (`Administrator` y `Secretary`):
    - Consulta reactiva de las membresías del alumno (`gymApi.getStudentMemberships(personId)`).
    - Botón de acción **`+ Asignar Membresía`**, habilitado cuando la persona está en estado `ACTIVA` y posee el rol `Student`. En caso contrario, se deshabilita o muestra advertencia guiada (indicando por ejemplo que debe asignarse el rol `Student` en la tarjeta de gestión de roles).
    - Listado de membresías asignadas con insignias de estado (`Activa`, `Pendiente`, `Vencida`, `Suspendida`, `Cancelada`), período de vigencia (`StartDate` a `EndDate`), créditos disponibles / totales (o indicación de Pase Libre Ilimitado), y fecha de contratación.
    - Al confirmarse la asignación en el modal, refresco automático del listado y mensaje de retroalimentación exitosa.

## Capabilities

### New Capabilities
<!-- Ninguna capacidad de dominio nueva a nivel backend -->

### Modified Capabilities
- `memberships`: Se amplía la especificación para incorporar la capacidad interactiva de asignación directa de membresías a alumnos desde la interfaz de personal administrativo en el frontend, consumiendo el endpoint existente de asignación y reflejando inmediatamente el alta en el listado del alumno.

## Impact

- **Frontend (`frontend/src/app`)**:
  - Modelos: Actualización de `src/app/core/models/gym.models.ts` con `AssignMembershipRequest`.
  - Servicios: Actualización de `src/app/core/services/gym-api.service.ts` con `assignMembership` y sus correspondientes pruebas en `gym-api.service.spec.ts`.
  - Componentes:
    - Nuevo componente modal `src/app/features/catalog/assign-membership-modal/assign-membership-modal.ts` (+ `.spec.ts`) o módulo modal en personas.
    - Actualización de `src/app/features/people/person-detail.ts` (+ `.spec.ts`) para incluir la sección de membresías, el botón de asignación y el manejo del modal.
- **Backend**:
  - Sin cambios de código necesarios (`MembershipsController.AssignMembership` ya se encuentra implementado y testeado).
- **Seguridad**:
  - La asignación respeta la política `Policies.MembershipsCreate` en el backend (roles `Administrator` y `Secretary`), coherente con la guardia de personal (`authService.isStaff()`) en el frontend.
