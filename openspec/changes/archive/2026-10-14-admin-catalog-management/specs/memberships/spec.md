## ADDED Requirements

### Requirement: Control de acceso a la configuración de planes de membresía
La interfaz web SHALL exponer las acciones de creación, edición, desactivación y reactivación de Planes de Membresía exclusivamente a usuarios con rol `Administrator` o `Secretary`. Los alumnos y visitantes solo SHALL visualizar los planes activos.

#### Scenario: Restricción de herramientas de planes para usuarios comunes
- **WHEN** un alumno o usuario no autenticado navega a `/planes`
- **THEN** la interfaz muestra solo los planes activos, sin botones para crear, editar ni cambiar el estado de planes

#### Scenario: Visualización administrativa en el catálogo de planes
- **WHEN** un usuario con rol `Administrator` o `Secretary` visualiza `/planes`
- **THEN** la interfaz lista planes activos e inactivos (los inactivos marcados visualmente), expone la acción `+ Nuevo Plan` y opciones para editar y desactivar o reactivar cada plan

### Requirement: Configuración interactiva de Planes de Membresía (MembershipPlans)
La aplicación SHALL proveer una interfaz de administración para listar, dar de alta y actualizar planes consumiendo `/api/membershipplans`. Dado que el servidor solo valida el nombre, el formulario SHALL exigir: nombre no vacío, precio mayor a 0, duración de al menos 1 día, al menos 1 crédito cuando el plan no es ilimitado, y al menos una actividad seleccionada cuando el plan no aplica a todas las actividades. Un plan ilimitado SHALL enviarse con `credits = 0` y un plan que aplica a todas las actividades SHALL enviarse con `allowedActivityIds` vacío.

#### Scenario: Alta de plan ilimitado por período
- **WHEN** el personal autorizado define el plan "Pase Libre Full", precio $35.000, duración de 30 días, marca ilimitado y "aplica a todas las actividades"
- **THEN** el sistema envía `POST /api/membershipplans` con `isUnlimited = true`, `credits = 0`, `appliesToAllActivities = true` y el plan queda disponible para contratación

#### Scenario: Alta de plan por paquete de créditos con actividades restringidas
- **WHEN** el personal autorizado crea "Bono 10 Clases Musculación", precio $20.000, duración de 60 días, 10 créditos, y selecciona únicamente la actividad "Musculación"
- **THEN** el sistema envía `POST /api/membershipplans` con `appliesToAllActivities = false` y `allowedActivityIds` conteniendo solo el identificador de "Musculación"

#### Scenario: Validaciones de cliente del plan
- **WHEN** el usuario intenta guardar un plan no ilimitado con 0 créditos, o un plan restringido sin actividades seleccionadas, o con precio 0
- **THEN** el formulario muestra el error correspondiente e impide el envío

#### Scenario: Desactivación de un plan obsoleto
- **WHEN** el personal autorizado desactiva un plan
- **THEN** el sistema envía `PUT /api/membershipplans/{id}` con `isActive = false`; el plan deja de mostrarse a alumnos y visitantes en `/planes`, y las membresías ya contratadas con ese plan continúan vigentes hasta su expiración

#### Scenario: Reactivación de un plan
- **WHEN** el personal autorizado reactiva un plan inactivo
- **THEN** el sistema envía `PUT /api/membershipplans/{id}` con `isActive = true` y el plan vuelve a mostrarse en el catálogo público
