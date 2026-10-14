## ADDED Requirements

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
