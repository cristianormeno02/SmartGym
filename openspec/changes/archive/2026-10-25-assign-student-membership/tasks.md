## 1. Servicio y Modelos de Asignación en Frontend

- [x] 1.1 (TDD Red) Actualizar `frontend/src/app/core/services/gym-api.service.spec.ts` agregando pruebas para el método `assignMembership`:
  - Verificar que efectúa una solicitud `POST` a `${apiUrl}/memberships/assign` con el payload de `AssignMembershipRequest`.
  - Verificar que retorna el objeto de tipo `MembershipDetail` recibido del backend.
- [x] 1.2 (TDD Green) Agregar `AssignMembershipRequest` a `frontend/src/app/core/models/gym.models.ts` e implementar `assignMembership(request: AssignMembershipRequest): Observable<MembershipDetail>` en `frontend/src/app/core/services/gym-api.service.ts` hasta que las pruebas pasen en verde.

## 2. Componente Modal de Asignación de Membresía (`AssignMembershipModalComponent`)

- [x] 2.1 (TDD Red) Crear `frontend/src/app/features/catalog/assign-membership-modal/assign-membership-modal.spec.ts` verificando:
  - Carga los planes de membresía activos al inicializarse mediante `gymApi.getMembershipPlans(true)`.
  - Inicializa la fecha de inicio (`startDate`) con la fecha actual y no permite fechas anteriores a hoy (`min` date).
  - Al seleccionar un plan, calcula y muestra la previsualización: duración en días, fecha de fin proyectada, créditos o modalidad ilimitada y precio formateado con `MoneyAmountPipe`.
  - Al enviar el formulario, deshabilita el botón de confirmación, invoca `gymApi.assignMembership` con los parámetros correspondientes y emite el evento `assigned`.
  - Muestra el mensaje de error correspondiente si la solicitud falla y reactiva el botón.
  - Al presionar cancelar o el botón de cierre, emite el evento `cancelled`.
- [x] 2.2 (TDD Green) Implementar `AssignMembershipModalComponent` en `frontend/src/app/features/catalog/assign-membership-modal/assign-membership-modal.ts` y confirmar que todas las pruebas pasen en verde.

## 3. Integración de Membresías en la Ficha de Persona (`PersonDetailComponent`)

- [x] 3.1 (TDD Red) Actualizar `frontend/src/app/features/people/person-detail.spec.ts` cubriendo:
  - Consulta y muestra las membresías del alumno utilizando `gymApi.getStudentMemberships(personId)`.
  - Muestra la tarjeta "Membresías del Alumno" con el botón `+ Asignar Membresía`.
  - Deshabilita o advierte si la persona no está en estado `ACTIVA`.
  - Abre el modal `AssignMembershipModalComponent` al pulsar el botón `+ Asignar Membresía`.
  - Al completar la asignación en el modal, cierra el modal, muestra el mensaje de éxito y recarga el listado de membresías.
- [x] 3.2 (TDD Green) Modificar `frontend/src/app/features/people/person-detail.ts` para integrar la sección de membresías, su estado de carga, el botón de asignación y el manejo del modal con refresco automático.

## 4. Verificación Integral

- [x] 4.1 Ejecutar la suite completa de pruebas unitarias de frontend (`npm test -- --watch=false`) en `frontend/` y comprobar que todos los tests pasen sin errores.
- [x] 4.2 Ejecutar `npm run build` en `frontend/` para validar la compilación limpia de la aplicación de producción sin advertencias de tipos.
