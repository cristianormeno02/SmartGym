## MODIFIED Requirements

### Requirement: Atributos obligatorios y opcionales de Persona
El sistema SHALL requerir para cada Persona: identificador único, nombre y apellido (sin espacios sobrantes, no vacíos, máximo 100 caracteres), estado y fecha de alta. Todos los demás atributos SHALL ser opcionales: documento, fecha de nacimiento, género (`MASCULINO`, `FEMENINO`, `X`, `NO_INFORMA`), email, teléfono principal, teléfono secundario, domicilio, contacto de emergencia y foto.

En la interfaz web de usuario (formulario de alta/edición de personas, ficha de detalle de persona, listado general de personas y perfil de usuario), todos los campos y columnas que exhiban o soliciten fechas (fecha de nacimiento, fecha de alta `createdAtUtc`, fecha de cambio de estado `statusChangedAtUtc` y último inicio de sesión `lastLoginUtc`) SHALL presentarse e ingresarse obligatoriamente en formato **`DD/MM/AAAA`** (o `DD/MM/AAAA HH:mm` en caso de incluir marca temporal).

#### Scenario: Carga de fecha de nacimiento en formulario de persona
- **WHEN** un operador o usuario carga la fecha de nacimiento en el formulario de persona (ej. `15/04/1995`)
- **THEN** el campo admite y valida el formato `DD/MM/AAAA` y envía al backend la fecha correspondiente en formato ISO `1995-04-15`

#### Scenario: Visualización de fechas biográficas y de auditoría en la ficha de persona
- **WHEN** se consulta el perfil o la ficha detallada de una persona
- **THEN** su fecha de nacimiento se visualiza como `DD/MM/AAAA` y sus marcas de alta, cambio de estado y último acceso se visualizan como `DD/MM/AAAA HH:mm`
