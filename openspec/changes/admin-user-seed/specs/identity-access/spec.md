## ADDED Requirements

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
