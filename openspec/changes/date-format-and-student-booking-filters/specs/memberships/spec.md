## MODIFIED Requirements

### Requirement: Asignación interactiva de Membresías a Alumnos
La interfaz de usuario SHALL proveer una experiencia fluida para asignar membresías a alumnos (mediante el modal de asignación o panel administrativo), requiriendo la selección de un alumno, un plan activo y la fecha de inicio de vigencia (`startDate`).

El campo de ingreso de fecha de inicio de vigencia en el formulario de asignación, así como todas las fechas de vigencia de membresías listadas en la plataforma (fecha de inicio y fin en el detalle de membresía del portal del alumno, en el perfil del alumno y en la ficha de persona), SHALL exhibirse e ingresarse obligatoriamente con formato **`DD/MM/AAAA`**.

#### Scenario: Carga de fecha de inicio en asignación de membresía
- **WHEN** el operador asigna una membresía e ingresa la fecha de inicio en formato `DD/MM/AAAA` (ej. `01/11/2026`)
- **THEN** el campo interpreta y valida la fecha en formato `DD/MM/AAAA` y envía al backend `startDate = "2026-11-01"`

#### Scenario: Visualización del período de vigencia de membresía
- **WHEN** el alumno o el personal consulta una membresía vigente desde el 1 de octubre hasta el 31 de octubre de 2026
- **THEN** la interfaz muestra el período como `01/10/2026 - 31/10/2026`
