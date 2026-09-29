## Context

El sistema SmartGym se inicia como un proyecto greenfield que requiere alta consistencia transaccional y flexibilidad de negocio. Ver motivación en [proposal.md](proposal.md). Los requerimientos técnicos y funcionales exigen un backend en ASP.NET Core Web API (.NET 10), persistencia relacional en PostgreSQL, almacenamiento de adjuntos en Amazon S3 y un frontend reactivo en Angular 22 (Standalone Components + Signals + Material/Tailwind).

## Goals / Non-Goals

**Goals:**
* Establecer una arquitectura de **Monolito Modular** en backend con límites bien definidos por módulo de dominio (Auth, People, Activities, Schedules, ClassSessions, Memberships, Credits, Reservations, Attendance, Promotions, FamilyGroups, Payments, Notifications, Reports, Audit).
* Garantizar consistencia transaccional estricta (ACID) en operaciones de cupos y movimientos de créditos bajo concurrencia.
* Implementar el desacoplamiento Persona / Usuario / Roles para soportar identidades con múltiples responsabilidades concurrentes (e.g. Alumno e Instructor).
* Estructurar el libro mayor (ledger) inmutable para débitos, créditos y compensaciones de membresías.
* Proveer un motor de promociones extensible basado en el patrón Strategy (condiciones y beneficios) evaluado en backend.
* Diseñar el frontend en Angular 22 organizado por módulos funcionales y áreas según rol de acceso (Público, Alumno, Instructor, Secretario, Administrativo).

**Non-Goals:**
* Implementar arquitectura de microservicios o mensajería distribuida pesada (Kafka/RabbitMQ) en la fase inicial.
* Integración directa y en vivo con SDK de pagos externos (se implementa la capa de abstracción para registro de pagos y referencias externas, posponiendo webhooks complejos de terceros a fases posteriores).
* Streaming o WebSockets en vivo para chat o seguimiento por GPS.

## Decisions

### 1. Monolito Modular en ASP.NET Core (.NET 10)
* **Decisión:** Agrupar los módulos de negocio dentro de una misma solución backend organizada en proyectos o carpetas de módulo con límites de contexto claros (Domain, Infrastructure, Application/Endpoints).
* **Razón:** Simplifica el despliegue, facilita las transacciones cross-módulo mediante Entity Framework Core y PostgreSQL, y elimina la sobrecarga operativa y de red de los microservicios sin renunciar a la modularidad.
* **Alternativas consideradas:** Microservicios independientes (descartado por latencia, complejidad de orquestación transaccional y sobrediseño prematuro) o arquitectura monolítica desestructurada (descartado por riesgo de acoplamiento espagueti).

### 2. Desacoplamiento de Identidad: Persona vs. Usuario vs. Rol
* **Decisión:** La entidad `Person` almacena datos biográficos y de contacto una sola vez. `User` encapsula credenciales locales o vinculación Google OAuth. Una tabla de unión `PersonRole` asocia una persona a múltiples roles simultáneos.
* **Razón:** Permite que Juan Pérez sea alumno por la mañana e instructor por la tarde sin duplicar legajos, datos de contacto ni certificados médicos.
* **Alternativas consideradas:** Tablas separadas por tipo (e.g. `Instructors`, `Students`), descartado porque genera duplicación y desincronización de datos personales.

### 3. Modelo Temporal de Horarios y Clases Concretas
* **Decisión:** La tabla `RecurringSchedule` define el patrón semanal con fechas de vigencia (`ValidFrom`, `ValidTo`). La tabla `ClassSession` representa cada clase real en un día/hora específico con estados (`Scheduled`, `InProgress`, `Finished`, `Suspended`, etc.) y referencias al instructor/secretario programado y efectivo.
* **Razón:** Permite suspender una clase concreta, cambiar de profesor o cambiar de sala un día de lluvia sin alterar el horario base recurrente ni alterar el historial de clases pasadas.

### 4. Ledger Inmutable de Créditos de Membresía
* **Decisión:** Cada membresía posee un balance derivado o sincronizado mediante la tabla inmutable `MembershipCreditMovement`. Toda acción (compra, consumo, devolución, compensación) genera un registro inmutable con usuario, fecha, motivo y delta.
* **Razón:** Proporciona trazabilidad financiera total, auditoría innegable y capacidad de reconstruir el saldo en cualquier momento histórico.

### 5. Control de Concurrencia en Reservas y Cupos
* **Decisión:** Uso de transacciones con bloqueo de fila (`SELECT ... FOR UPDATE` en PostgreSQL) o control de concurrencia optimista/pesimista sobre el contador de cupo de `ClassSession`.
* **Razón:** Previene la sobreventa de cupos cuando múltiples alumnos reservan simultáneamente la última vacante disponible.

### 6. Motor de Promociones Basado en Strategy Pattern
* **Decisión:** Las promociones se estructuran como un conjunto de evaluadores de condiciones (tipo de alumno, grupo familiar, horario, actividad) que calculan un beneficio (porcentaje, importe fijo, 2x1, etc.) ejecutados exclusivamente en el backend.
* **Razón:** Evita acoplar reglas comerciales cambiantes a las vistas del frontend o requerir cambios de esquema en base de datos para nuevas campañas.

## Risks / Trade-offs

* **[Concurrencia alta en apertura de inscripciones]** → Mitigación: Transacciones cortas a nivel de base de datos con bloqueo optimista/pesimista sobre la clase concreta y uso de índices en reservas activas.
* **[Reglas de negocio cambiantes para membresías y actividades]** → Mitigación: Uso de tablas de enlace `PlanAllowedActivity` y campos `jsonb` para configuraciones específicas no estructurales.
* **[Seguridad y elevación de privilegios con roles múltiples]** → Mitigación: Claims basados en roles en el JWT validados a nivel de middleware/policy en ASP.NET Core, requiriendo autorización explícita para cada endpoint.
