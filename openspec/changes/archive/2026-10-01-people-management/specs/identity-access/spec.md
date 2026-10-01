## MODIFIED Requirements

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
