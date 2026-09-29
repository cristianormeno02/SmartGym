## Purpose

Proporciona la gestión integral de identidades físicas (Personas), credenciales de acceso (Usuarios), asignación múltiple de roles y autenticación híbrida (local y OAuth con Google).

## Requirements

### Requirement: Separación estricta de Persona, Usuario y Rol
El sistema SHALL registrar los datos personales una única vez en la entidad Persona, permitiendo que dicha persona esté vinculada a una cuenta de Usuario y desempeñe simultáneamente múltiples roles dentro del gimnasio sin duplicar información.

#### Scenario: Persona con múltiples roles simultáneos
- **WHEN** un usuario con rol Administrativo asigna los roles Alumno e Instructor a una misma Persona existente
- **THEN** el sistema registra ambos roles vinculados al mismo registro de Persona sin duplicar sus datos personales

### Requirement: Autenticación híbrida y vinculación de identidades
El sistema SHALL permitir el inicio de sesión mediante credenciales locales (usuario/contraseña con hashing seguro) y mediante proveedores externos OAuth/OIDC (Google), vinculando la identidad externa a la Persona correspondiente.

#### Scenario: Inicio de sesión con cuenta local
- **WHEN** un usuario ingresa su correo electrónico y contraseña válidos
- **THEN** el sistema emite un token JWT que incluye el identificador de usuario y los roles activos asignados

#### Scenario: Inicio de sesión con Google OAuth
- **WHEN** un usuario se autentica satisfactoriamente con su cuenta de Google y el correo coincide con una Persona registrada
- **THEN** el sistema vincula la identidad externa sin crear una Persona duplicada y emite el token JWT correspondiente

### Requirement: Autorización basada en roles y permisos
El sistema SHALL restringir el acceso a los endpoints y recursos del sistema según los roles y permisos activos de la sesión autenticada (Administrativo, Secretario, Instructor, Alumno y acceso público).

#### Scenario: Intento de acceso administrativo por parte de un alumno
- **WHEN** un usuario autenticado únicamente con el rol Alumno intenta invocar un endpoint de administración de actividades o pagos
- **THEN** el sistema deniega el acceso retornando un código de estado HTTP 403 Forbidden
