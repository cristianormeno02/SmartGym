## ADDED Requirements

### Requirement: Planes habilitados como pase de clase individual
El sistema SHALL permitir marcar un plan de membresía como pase de clase individual (`IsSingleClassPass`, booleano, por defecto `false`), expuesto y editable en `/api/membershipplans`. Un plan SHALL considerarse **opción de pase individual** para una clase cuando cumple todas estas condiciones:
- está activo;
- su precio es mayor a 0;
- permite la actividad de la clase (`AppliesToAllActivities` o actividad incluida en `AllowedActivities`);
- tiene `IsSingleClassPass == true`, o no es ilimitado y tiene exactamente 1 crédito.

Las opciones de pase individual SHALL usarse como catálogo de precios para el cobro in situ del Check-in Directo en mostrador (`reservations-attendance`). El importe cobrado SHALL ser el precio vigente del plan elegido. El cobro in situ emite un pase `SingleClassTicket` para la clase y no contrata una membresía, por lo que no SHALL alterar las membresías del alumno ni aplicar las reglas de solapamiento de contrataciones. Si existen varias opciones, SHALL presentarse todas para que el personal elija; si existe una sola, SHALL preseleccionarse; si no existe ninguna, el cobro in situ no SHALL estar disponible.

El formulario de administración de planes SHALL incluir el control "Disponible como pase de clase individual en recepción" (inicialmente desmarcado), con una ayuda que indique que los planes no ilimitados de 1 crédito se ofrecen como pase aunque no estén marcados. Los planes existentes SHALL quedar con `IsSingleClassPass = false` tras la migración.

#### Scenario: Plan marcado explícitamente como pase
- **WHEN** el administrador crea el plan "Clase Suelta Estudiante" con precio `3.500,00`, 30 días, 4 créditos, aplicable a todas las actividades y marca "Disponible como pase de clase individual"
- **THEN** el plan se guarda con `IsSingleClassPass = true` y se ofrece como opción de pase para cualquier clase al precio de 3500

#### Scenario: Plan de un crédito ofrecido automáticamente
- **WHEN** existe el plan activo "Clase Suelta" no ilimitado, con 1 crédito, precio 5000 y sin `IsSingleClassPass`
- **THEN** el plan se ofrece como opción de pase individual para las clases de las actividades que permite

#### Scenario: Planes excluidos como opción de pase
- **WHEN** un plan está inactivo, o no permite la actividad de la clase, o es ilimitado sin `IsSingleClassPass`, o tiene 8 créditos sin `IsSingleClassPass`
- **THEN** el plan no se ofrece como opción de pase individual para esa clase

#### Scenario: Varias opciones de pase disponibles
- **WHEN** para una clase existen dos opciones de pase elegibles
- **THEN** recepción ve ambas con su nombre y precio y debe elegir una antes de cobrar
