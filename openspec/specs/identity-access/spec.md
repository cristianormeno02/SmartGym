## Purpose

Proporciona la gestión integral de identidades físicas (Personas), credenciales de acceso (Usuarios), asignación múltiple de roles y autenticación híbrida (local y OAuth con Google).

## Requirements

### Requirement: Separación estricta de Persona, Usuario y Rol
El sistema SHALL registrar los datos personales una única vez en la entidad Persona, permitiendo que dicha persona esté vinculada a una cuenta de Usuario y desempeñe simultáneamente múltiples roles dentro del gimnasio sin duplicar información.

#### Scenario: Persona con múltiples roles simultáneos
- **WHEN** un usuario con rol Administrativo asigna los roles Alumno e Instructor a una misma Persona existente
- **THEN** el sistema registra ambos roles vinculados al mismo registro de Persona sin duplicar sus datos personales

### Requirement: Autenticación híbrida y vinculación de identidades
El sistema SHALL permitir el inicio de sesión mediante credenciales locales (usuario/contraseña con hashing seguro) y mediante proveedores externos OAuth/OIDC (Google), vinculando la identidad externa a la Persona correspondiente. La vinculación por correo SHALL basarse en el email normalizado de la Persona, que es único cuando existe. Las personas creadas o vinculadas durante la autenticación SHALL cumplir las reglas del módulo People: nombre y apellido no vacíos y documento normalizado. El sistema SHALL denegar el inicio de sesión de personas en estado `FALLECIDA`.

#### Scenario: Inicio de sesión con cuenta local
- **WHEN** un usuario ingresa su correo electrónico y contraseña válidos
- **THEN** el sistema emite un token JWT que incluye el identificador de usuario y los roles activos asignados

#### Scenario: Inicio de sesión con Google OAuth
- **WHEN** un usuario se autentica satisfactoriamente con su cuenta de Google y el correo coincide con el email normalizado de una Persona registrada
- **THEN** el sistema vincula la identidad externa sin crear una Persona duplicada y emite el token JWT correspondiente

#### Scenario: Alta con Google sin apellido
- **WHEN** un usuario se autentica con Google por primera vez y el proveedor no informa apellido
- **THEN** el sistema crea la Persona con el apellido derivado del nombre completo o, si no es posible, con el valor "Sin apellido", y guarda la foto de Google como avatar externo

#### Scenario: Registro local con DNI con formato visual
- **WHEN** un usuario se registra con el DNI `12.345.678` y ya existe una Persona con DNI normalizado `12345678`
- **THEN** el sistema rechaza el registro indicando que el documento ya está registrado

#### Scenario: Inicio de sesión de una persona fallecida
- **WHEN** un usuario vinculado a una Persona en estado `FALLECIDA` intenta iniciar sesión, con credenciales locales o con Google
- **THEN** el sistema deniega el acceso sin emitir token

#### Scenario: Inicio de sesión de una persona bloqueada
- **WHEN** un usuario vinculado a una Persona en estado `BLOQUEADA` o `INACTIVA` inicia sesión con credenciales válidas
- **THEN** el sistema emite el token, y las operaciones que requieren una persona `ACTIVA` (reservas, membresías) se rechazan en sus módulos

### Requirement: Autorización basada en roles y permisos
El sistema SHALL restringir el acceso a los endpoints y recursos del sistema según los roles y permisos activos de la sesión autenticada (Administrativo, Secretario, Instructor, Alumno y acceso público).

#### Scenario: Intento de acceso administrativo por parte de un alumno
- **WHEN** un usuario autenticado únicamente con el rol Alumno intenta invocar un endpoint de administración de actividades o pagos
- **THEN** el sistema deniega el acceso retornando un código de estado HTTP 403 Forbidden

### Requirement: Creación del administrador inicial por configuración
El sistema SHALL crear al arrancar un usuario con rol `Administrator` a partir de la configuración `AdminSeed` (`Email`, `Password`, y opcionalmente `FirstName` y `LastName`) cuando no exista ningún administrador activo, de modo que una base de datos nueva pueda administrarse sin intervención manual sobre los datos.
- Las credenciales SHALL provenir únicamente de la configuración del entorno (user-secrets o variables de entorno); no SHALL existir credenciales por defecto en el código ni en archivos versionados.
- La contraseña SHALL almacenarse hasheada y no SHALL escribirse en logs.
- La operación SHALL ser idempotente: si ya existe un administrador activo no SHALL crear, modificar ni promover ninguna cuenta.
- Si el email configurado ya pertenece a un usuario o persona existente, el sistema no SHALL otorgarle el rol `Administrator` ni cambiar su contraseña.
- Una configuración ausente, inválida (email inválido o contraseña de menos de 12 caracteres) o un fallo al crear el administrador no SHALL impedir el arranque de la API.

#### Scenario: Base sin administradores y configuración presente
- **WHEN** la API arranca con `AdminSeed:Email` y `AdminSeed:Password` válidos y no existe ninguna persona con el rol `Administrator` activo
- **THEN** el sistema crea una Persona y un Usuario con ese email, con la contraseña hasheada y el rol `Administrator`, y ese usuario puede iniciar sesión con las credenciales configuradas

#### Scenario: Ya existe un administrador
- **WHEN** la API arranca con `AdminSeed` configurado y ya existe una persona con el rol `Administrator` activo
- **THEN** el sistema no crea ningún usuario ni modifica la contraseña o los roles de las cuentas existentes

#### Scenario: Configuración ausente
- **WHEN** la API arranca sin `AdminSeed:Email` o sin `AdminSeed:Password`
- **THEN** el sistema no crea ningún usuario y la API arranca normalmente

#### Scenario: Email configurado ya utilizado por otra cuenta
- **WHEN** la API arranca sin administradores y el `AdminSeed:Email` coincide con un usuario o persona existente que no es administrador
- **THEN** el sistema no le otorga el rol `Administrator`, no cambia su contraseña y registra el motivo

### Requirement: Acceso al portal administrativo por rol
La ruta `/portal/admin` SHALL ser accesible para usuarios autenticados con rol `Administrator` o `Secretary`. Cada pestaña del portal SHALL mostrarse solo a los roles autorizados por la política de los endpoints que consume: las pestañas de Usuarios y Auditoría SHALL ser exclusivas de `Administrator`, y las pestañas "Gestión de Catálogo" y "Salas y Espacios" SHALL mostrarse a ambos roles. La pestaña seleccionada inicialmente SHALL ser la primera visible para el rol del usuario.

#### Scenario: Secretario accede al portal administrativo
- **WHEN** un usuario con rol `Secretary` navega a `/portal/admin`
- **THEN** el portal se muestra con las pestañas "Gestión de Catálogo" y "Salas y Espacios" visibles y sin las pestañas de Usuarios ni Auditoría

#### Scenario: Administrador ve todas las pestañas
- **WHEN** un usuario con rol `Administrator` navega a `/portal/admin`
- **THEN** el portal muestra todas las pestañas, incluidas Usuarios, Auditoría, "Gestión de Catálogo" y "Salas y Espacios"

#### Scenario: Roles no autorizados
- **WHEN** un usuario con rol `Student` o `Instructor`, sin rol de staff, navega a `/portal/admin`
- **THEN** el guard de rutas deniega el acceso

### Requirement: Consulta de la cuenta de acceso de una Persona
El sistema SHALL permitir al Administrador consultar, por identificador de Persona, si ésta posee cuenta de usuario, el estado de la cuenta (activa o suspendida), su nombre de usuario, su último inicio de sesión y los roles activos de la Persona. Los roles SHALL informarse aunque la Persona no tenga cuenta.

#### Scenario: Persona sin cuenta con rol asignado
- **WHEN** el administrador consulta la cuenta de una persona registrada en el padrón con el rol `Student` y sin usuario
- **THEN** el sistema responde que no posee cuenta e informa el rol `Student`

#### Scenario: Acceso denegado a no administradores
- **WHEN** un usuario con rol `Secretary` invoca cualquiera de los endpoints de gestión de cuentas y roles
- **THEN** el sistema responde 403 Forbidden

### Requirement: Aprovisionamiento de cuenta de usuario para una Persona existente
El sistema SHALL permitir al Administrador crear una cuenta de acceso (`User`) vinculada a una `Person` existente, no fallecida y sin credenciales previas, requiriendo que la persona posea un correo electrónico cargado y que dicho correo normalizado no sea el nombre de usuario de otra cuenta. La contraseña inicial SHALL tener al menos 8 caracteres, una mayúscula, un dígito y un carácter no alfanumérico, y SHALL almacenarse con hash seguro. Los roles iniciales SHALL limitarse a `Administrator`, `Secretary`, `Instructor` y `Student`.

#### Scenario: Creación exitosa de cuenta de usuario con roles iniciales
- **WHEN** el administrador aprovisiona la cuenta de una persona sin usuario con una contraseña válida y los roles `Secretary` e `Instructor`
- **THEN** el sistema persiste el `User` activo vinculado a la `Person` con el email normalizado como nombre de usuario, deja activos los `PersonRole` correspondientes y registra el evento en auditoría con el administrador que lo ejecutó

#### Scenario: Rechazo por persona sin correo electrónico
- **WHEN** se intenta crear una cuenta de acceso para una persona cuyo registro no tiene email cargado
- **THEN** la operación es rechazada con un error de validación (400) exigiendo que se complete el email en la ficha antes de aprovisionar el usuario

#### Scenario: Rechazo por correo electrónico ya vinculado a otro usuario
- **WHEN** el correo de la persona ya es el nombre de usuario de otra cuenta existente
- **THEN** el sistema rechaza la creación con un error de conflicto (409) informando la colisión de credenciales

#### Scenario: Rechazo por persona que ya posee cuenta
- **WHEN** se intenta aprovisionar una cuenta para una persona que ya tiene usuario
- **THEN** el sistema rechaza la operación con un error de conflicto (409)

#### Scenario: Rechazo por persona fallecida
- **WHEN** se intenta aprovisionar una cuenta para una persona en estado `FALLECIDA`
- **THEN** el sistema rechaza la operación con un error de conflicto (409)

#### Scenario: Rechazo por contraseña débil o rol no asignable
- **WHEN** la contraseña no cumple la política de complejidad o la lista de roles incluye `Public` o un identificador inexistente
- **THEN** el sistema rechaza la operación con un error de validación (400) sin crear el usuario

### Requirement: Asignación y revocación dinámica de roles
El sistema SHALL permitir al Administrador reemplazar el conjunto de roles activos de una Persona por un conjunto deseado de roles asignables (`Administrator`, `Secretary`, `Instructor`, `Student`). La operación SHALL ser idempotente, SHALL reactivar la asignación existente cuando un rol revocado se vuelve a asignar, y SHALL registrar en auditoría el administrador que realizó el cambio junto con los roles anteriores y nuevos.

#### Scenario: Asignación de rol de Profesor a un socio existente
- **WHEN** el administrador establece los roles `Student` e `Instructor` para una persona que sólo tenía el rol `Student`
- **THEN** el sistema activa el rol `Instructor`, conserva el rol `Student` y la persona accede tanto a su portal de alumno como a la grilla docente al volver a iniciar sesión

#### Scenario: Revocación de rol de Secretaría
- **WHEN** el administrador quita el rol `Secretary` del conjunto de roles de una persona con cuenta
- **THEN** el sistema desactiva la asignación (`IsActive = false`) e invalida las sesiones vigentes del usuario, que pierde de inmediato los permisos de secretaría

#### Scenario: Reasignación de un rol previamente revocado
- **WHEN** el administrador vuelve a asignar el rol `Secretary` a una persona a la que se le había revocado
- **THEN** el sistema reactiva la asignación existente sin crear un registro duplicado

#### Scenario: Envío repetido del mismo conjunto de roles
- **WHEN** el administrador envía un conjunto de roles idéntico a los roles activos actuales
- **THEN** el sistema responde con éxito sin modificar asignaciones, sin invalidar sesiones y sin registrar auditoría

#### Scenario: Protección contra la eliminación del último Administrador
- **WHEN** un administrador intenta quitar el rol `Administrator` a sí mismo o a otra persona y no existe otro administrador operativo (rol activo, persona en estado `ACTIVA` y cuenta de usuario activa)
- **THEN** el sistema rechaza la operación con un error de conflicto (409) impidiendo que el gimnasio quede sin administración

#### Scenario: Revocaciones simultáneas de los dos últimos administradores
- **WHEN** dos operaciones concurrentes intentan quitar el rol `Administrator` a cada uno de los dos únicos administradores operativos
- **THEN** a lo sumo una de ellas se completa y la otra es rechazada con 409

### Requirement: Suspensión y reactivación de cuentas de acceso
El sistema SHALL permitir al Administrador suspender (`User.IsActive = false`) y reactivar (`User.IsActive = true`) una cuenta de usuario sin eliminar los datos personales, los roles ni el historial. Una cuenta suspendida SHALL ser rechazada tanto en el inicio de sesión local como en el inicio de sesión con Google. El sistema SHALL rechazar la suspensión del último administrador operativo.

#### Scenario: Intento de inicio de sesión de cuenta suspendida
- **WHEN** un usuario cuya cuenta fue suspendida intenta autenticarse con credenciales locales válidas o con su cuenta de Google vinculada
- **THEN** el sistema rechaza el inicio de sesión sin emitir token, informando que la cuenta se encuentra suspendida

#### Scenario: Sesión vigente de una cuenta suspendida
- **WHEN** el administrador suspende una cuenta que tiene un token vigente
- **THEN** los requests siguientes con ese token son rechazados con 401

#### Scenario: Reactivación de cuenta
- **WHEN** el administrador reactiva la cuenta de un usuario suspendido
- **THEN** la cuenta vuelve a estar activa y el usuario puede iniciar sesión inmediatamente con sus roles y credenciales vigentes

#### Scenario: Suspensión del último administrador
- **WHEN** el administrador intenta suspender la cuenta del único administrador operativo
- **THEN** el sistema rechaza la operación con un error de conflicto (409)

### Requirement: Restablecimiento administrativo de contraseña
El sistema SHALL permitir al Administrador asignar una nueva contraseña provisoria, que cumpla la política de complejidad, a un usuario existente.

#### Scenario: Restablecimiento exitoso de contraseña
- **WHEN** el administrador define una nueva contraseña que cumple los requisitos de complejidad para el usuario
- **THEN** el sistema actualiza el hash de la contraseña, invalida las sesiones previas del usuario y registra la acción en auditoría sin incluir la contraseña

#### Scenario: Restablecimiento para persona sin cuenta
- **WHEN** el administrador intenta restablecer la contraseña de una persona que no posee usuario
- **THEN** el sistema responde 404

### Requirement: Invalidación de sesiones por sello de seguridad
Cada token emitido SHALL incluir el sello de seguridad vigente del usuario. El sistema SHALL rechazar con 401 todo request cuyo token pertenezca a un usuario inexistente o suspendido, o cuyo sello no coincida con el vigente. El sello SHALL renovarse al suspender la cuenta, al restablecer la contraseña y al modificar los roles activos.

#### Scenario: Token emitido antes de un cambio de contraseña
- **WHEN** un usuario usa un token emitido antes de que el administrador restableciera su contraseña
- **THEN** el sistema rechaza el request con 401 y el usuario debe iniciar sesión nuevamente

### Requirement: Interfaz gráfica en el Portal Administrativo y Ficha de Persona
La interfaz de usuario SHALL reflejar el estado de la cuenta de acceso y sus roles en dos puntos clave de la aplicación, visibles sólo para usuarios con rol `Administrator`:
1. **Ficha Personal (`/portal/personas/{id}`)**: Tarjeta "Cuenta de Acceso y Roles" que indica si no posee cuenta (con botón "Crear Cuenta de Acceso", deshabilitado con explicación si la persona no tiene email o está fallecida) o si posee cuenta (con estado Activa/Suspendida, chips de roles y acciones para gestionar roles, restablecer contraseña y suspender/reactivar la cuenta).
2. **Directorio de Usuarios (`/portal/admin` pestaña Usuarios)**: Acceso directo en cada fila para ver la ficha de la persona y gestionar sus roles.

#### Scenario: Administrador visualiza estado de credenciales en la ficha
- **WHEN** el administrador navega a la ficha de una persona recién registrada
- **THEN** la interfaz muestra el indicador "Sin cuenta de usuario creada" junto con el botón para aprovisionarla

#### Scenario: Secretaria visualiza la ficha
- **WHEN** un usuario con rol `Secretary` navega a la ficha de una persona
- **THEN** la tarjeta "Cuenta de Acceso y Roles" no se muestra

#### Scenario: Rechazo por último administrador en la interfaz
- **WHEN** el administrador intenta quitarse el rol `Administrator` siendo el único administrador operativo
- **THEN** la interfaz muestra el mensaje de conflicto devuelto por el servidor y mantiene los roles sin cambios

#### Scenario: Sesión invalidada
- **WHEN** un request del frontend recibe 401 por sesión invalidada
- **THEN** la aplicación descarta el token y redirige al inicio de sesión
