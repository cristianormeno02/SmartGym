## MODIFIED Requirements

### Requirement: Configuración interactiva de Planes de Membresía (MembershipPlans)
La aplicación SHALL proveer una interfaz de administración para listar, dar de alta y actualizar planes consumiendo `/api/membershipplans`. Dado que el servidor solo valida el nombre, el formulario SHALL exigir: nombre no vacío, precio mayor a 0, duración de al menos 1 día, al menos 1 crédito cuando el plan no es ilimitado, y al menos una actividad seleccionada cuando el plan no aplica a todas las actividades. Un plan ilimitado SHALL enviarse con `credits = 0` y un plan que aplica a todas las actividades SHALL enviarse con `allowedActivityIds` vacío. El campo de ingreso del precio y todas las vistas donde se exhiban importes de planes de membresía o saldos de membresías (incluyendo inicio `/`, catálogo público `/planes`, panel administrativo de planes y detalle de membresía en el portal del alumno) SHALL mostrar y formatear los importes monetarios con exactamente 2 decimales, coma (`,`) como separador de decimales y punto (`.`) como separador de miles (ej. `35.000,00`), enviando al backend el valor numérico decimal equivalente.

#### Scenario: Alta de plan ilimitado por período
- **WHEN** el personal autorizado define el plan "Pase Libre Full", ingresa el precio `35.000,00`, duración de 30 días, marca ilimitado y "aplica a todas las actividades"
- **THEN** el sistema envía `POST /api/membershipplans` con `price = 35000`, `isUnlimited = true`, `credits = 0`, `appliesToAllActivities = true` y el plan queda disponible para contratación

#### Scenario: Alta de plan por paquete de créditos con actividades restringidas
- **WHEN** el personal autorizado crea "Bono 10 Clases Musculación", ingresa el precio `20.000,00`, duración de 60 días, 10 créditos, y selecciona únicamente la actividad "Musculación"
- **THEN** el sistema envía `POST /api/membershipplans` con `price = 20000`, `appliesToAllActivities = false` y `allowedActivityIds` conteniendo solo el identificador de "Musculación"

#### Scenario: Formato de importe en el ingreso y edición del precio del plan
- **WHEN** el personal autorizado ingresa un importe en el campo de precio del formulario de plan (por ejemplo `35000` o `35000,5`) o abre el formulario para editar un plan con `price = 35000`
- **THEN** el campo formatea los miles con punto (`.`) y los decimales con coma (`,`), mostrando `35.000,00` (o `35.000,50` al completar decimales) mientras mantiene el valor numérico `35000` (o `35000.5`) en el control del formulario

#### Scenario: Visualización de precios de planes y saldos con formato monetario
- **WHEN** se visualizan los precios de planes en la portada (`/`), en `/planes` o en la administración de catálogo, o el saldo pendiente de una membresía en el portal del alumno
- **THEN** cada importe monetario se muestra con 2 decimales, coma como separador decimal y punto como separador de miles (por ejemplo `$35.000,00` y `$15.000,00`)

#### Scenario: Validaciones de cliente del plan
- **WHEN** el usuario intenta guardar un plan no ilimitado con 0 créditos, o un plan restringido sin actividades seleccionadas, o con precio 0 (`0,00`)
- **THEN** el formulario muestra el error correspondiente e impide el envío

#### Scenario: Desactivación de un plan obsoleto
- **WHEN** el personal autorizado desactiva un plan
- **THEN** el sistema envía `PUT /api/membershipplans/{id}` con `isActive = false`; el plan deja de mostrarse a alumnos y visitantes en `/planes`, y las membresías ya contratadas con ese plan continúan vigentes hasta su expiración

#### Scenario: Reactivación de un plan
- **WHEN** el personal autorizado reactiva un plan inactivo
- **THEN** el sistema envía `PUT /api/membershipplans/{id}` con `isActive = true` y el plan vuelve a mostrarse en el catálogo público
