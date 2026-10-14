## ADDED Requirements

### Requirement: Protección del último administrador al cambiar el estado de una Persona
El sistema SHALL rechazar el cambio de estado de una Persona hacia cualquier estado distinto de `ACTIVA` cuando esa Persona es el único administrador operativo del sistema (rol `Administrator` activo, persona `ACTIVA` y cuenta de usuario activa).

#### Scenario: Inactivación del único administrador
- **WHEN** un usuario de staff cambia a `INACTIVA`, `BLOQUEADA` o `FALLECIDA` el estado de la persona que es el único administrador operativo
- **THEN** el sistema rechaza la operación con 409 indicando que el gimnasio quedaría sin administración

#### Scenario: Inactivación de un administrador con otro administrador operativo
- **WHEN** se cambia a `INACTIVA` el estado de una persona administradora y existe otro administrador operativo
- **THEN** el sistema aplica el cambio de estado normalmente
