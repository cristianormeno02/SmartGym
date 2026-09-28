# Project Guidelines: SmartGym

## Workflow: OpenSpec + Superpowers

Este proyecto combina la gobernanza de especificaciones de **OpenSpec** con la disciplina de ingeniería de **Superpowers**.

### 1. Planificación y Especificación (OpenSpec)
- Para cualquier funcionalidad, cambio arquitectónico o refactorización significativa, utiliza las directivas de **OpenSpec** (`/opsx-propose`, `/opsx-explore`).
- Mantén la fuente de la verdad en `openspec/specs/` y las propuestas activas en `openspec/changes/`.
- No comiences a escribir código de producción sin haber acordado la propuesta y las tareas.

### 2. Disciplina de Ejecución (Superpowers)
Durante la implementación y el desarrollo cotidiano, aplica los principios y habilidades de **Superpowers**:

- **Brainstorming (`brainstorming`)**: Antes de crear componentes o tomar decisiones de diseño, explora la intención del usuario y ofrece alternativas claras.
- **Desarrollo Guiado por Pruebas (`test-driven-development`)**:
  - Aplica estrictamente el ciclo **Red-Green-Refactor**.
  - Escribe primero la prueba que falla.
  - Escribe el código mínimo necesario para que pase.
  - Refactoriza manteniendo las pruebas en verde.
- **Depuración Sistemática (`systematic-debugging`)**:
  - Ante cualquier bug o falla, investiga la causa raíz sistemáticamente antes de intentar parches o adivinanzas.
- **Verificación Antes de Finalizar (`verification-before-completion`)**:
  - Ejecuta las pruebas automatizadas y comprueba que todos los criterios de aceptación se cumplan antes de dar una tarea por terminada.
- **Subagentes (`subagent-driven-development`)**:
  - Para tareas complejas o aisladas, delega en subagentes manteniendo el contexto limpio.
