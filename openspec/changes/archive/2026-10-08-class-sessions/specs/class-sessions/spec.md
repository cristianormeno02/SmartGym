## Purpose

Gestiona el ciclo de vida de las ocurrencias concretas de clases en el calendario del gimnasio: generación idempotente desde horarios recurrentes, clases extraordinarias, personal programado vs. efectivo, sala, cupo, estados persistidos y derivados, suspensión, cancelación, reprogramación, inmutabilidad histórica y concurrencia.

## ADDED Requirements

### Requirement: Modelo temporal UTC con zona horaria del gimnasio
El sistema SHALL registrar el inicio y el fin de cada clase concreta como instantes UTC (`StartsAtUtc`, `EndsAtUtc`). SHALL interpretar y presentar las fechas y horas locales según la zona horaria configurada del gimnasio (por defecto `America/Argentina/San_Juan`), exigir `EndsAtUtc > StartsAtUtc` con una duración máxima de 24 horas y admitir clases que cruzan la medianoche.

#### Scenario: Clase que cruza la medianoche
- **WHEN** se crea una clase extraordinaria de 23:30 a 00:30 del día siguiente (hora local)
- **THEN** el sistema persiste `StartsAtUtc` y `EndsAtUtc` con una duración de 1 hora y la acepta

#### Scenario: Conversión de hora local
- **WHEN** un horario recurrente de las 08:30 genera una clase para el 05/10/2026 en un gimnasio configurado en `America/Argentina/San_Juan` (UTC-3)
- **THEN** la clase se persiste con `StartsAtUtc = 2026-10-05T11:30:00Z` y se presenta como 08:30 local

#### Scenario: Duración inválida
- **WHEN** se intenta crear una clase cuyo fin es anterior o igual a su inicio, o que dura más de 24 horas
- **THEN** la operación es rechazada con un error de validación

### Requirement: Generación automática e idempotente desde horarios recurrentes
El sistema SHALL generar periódicamente clases concretas en estado PROGRAMADA a partir de los horarios recurrentes activos de actividades `ACTIVE`, para una ventana móvil configurable (por defecto 60 días). SHALL garantizar la unicidad de `(horario, inicio)` de modo que las ejecuciones repetidas o concurrentes no produzcan duplicados ni recreen ocurrencias que ya existen en cualquier estado. Las ocurrencias que solapan sala o instructor con otra clase activa, o que no tienen un cupo resoluble, SHALL omitirse e informarse como conflictos.

#### Scenario: Generación de la ventana futura
- **WHEN** el proceso de generación se ejecuta para un horario activo de los lunes de 08:30 a 09:30
- **THEN** se crea una clase PROGRAMADA por cada lunes de los próximos 60 días, con la sala, el instructor y el secretario del horario como personal programado y efectivo, y el cupo resuelto según la regla de cupo efectivo

#### Scenario: Ejecución repetida sin duplicados
- **WHEN** el generador se ejecuta nuevamente sobre un rango con clases ya instanciadas
- **THEN** no se crean duplicados ni se modifican las clases existentes

#### Scenario: Clase cancelada manualmente no se regenera
- **WHEN** un administrativo cancela una clase generada y luego el generador vuelve a ejecutarse
- **THEN** la clase permanece CANCELADA y no se crea otra ocurrencia para ese horario e inicio

#### Scenario: Conflicto de sala durante la generación
- **WHEN** una ocurrencia del horario solapa con una clase extraordinaria activa en la misma sala
- **THEN** esa ocurrencia se omite y el resultado de la generación la informa como conflicto `ROOM_OVERLAP`, sin abortar el resto de la generación

### Requirement: Propagación de cambios del horario recurrente
Al modificar, cerrar la vigencia o desactivar un horario recurrente, el sistema SHALL actualizar o cancelar (con motivo de sistema `ScheduleEnded`) las clases futuras de ese horario que sigan PROGRAMADAS, sin reservas y sin modificaciones manuales, y SHALL generar las nuevas ocurrencias que correspondan. Las clases abiertas, con reservas o modificadas manualmente SHALL quedar intactas y devolverse como divergentes.

#### Scenario: Cambio de hora del horario
- **WHEN** un horario de las 08:30 pasa a las 09:00 y tiene clases futuras PROGRAMADAS sin reservas
- **THEN** esas clases se actualizan a las 09:00 en el mismo lugar

#### Scenario: Cierre de vigencia
- **WHEN** se adelanta el fin de vigencia de un horario al 31/10 y existen clases PROGRAMADAS sin reservas en noviembre
- **THEN** esas clases pasan a CANCELADA con motivo `ScheduleEnded`

#### Scenario: Clase con reservas no se modifica
- **WHEN** se cambia la sala de un horario y una de sus clases futuras está ABIERTA con reservas
- **THEN** esa clase conserva su sala y se informa como divergente en la respuesta de la edición del horario

### Requirement: Clases extraordinarias
El sistema SHALL permitir crear clases concretas sin horario recurrente asociado. SHALL exigir una actividad `ACTIVE`, sala, instructor, inicio y fin futuros, y un cupo explícito o resoluble desde la capacidad por defecto de la actividad. La secretaría y las notas son opcionales.

#### Scenario: Alta de clase extraordinaria
- **WHEN** un administrativo registra una "Masterclass Zumba" con sala, instructor, horario futuro y cupo 40
- **THEN** la clase se crea PROGRAMADA, sin horario recurrente, con cupo 40

#### Scenario: Alta en el pasado
- **WHEN** se intenta crear una clase extraordinaria cuyo inicio ya pasó
- **THEN** la operación es rechazada con un error de validación

### Requirement: Ausencia de solapamientos de sala e instructor
El sistema SHALL impedir que dos clases activas (PROGRAMADA o ABIERTA) se solapen en el tiempo en la misma sala o con el mismo instructor efectivo, garantizándolo a nivel de base de datos aun bajo operaciones concurrentes. Las clases consecutivas (una termina cuando empieza la otra) SHALL estar permitidas.

#### Scenario: Solapamiento de sala
- **WHEN** se intenta crear, reprogramar o mover una clase a una sala ocupada por otra clase activa en un horario superpuesto
- **THEN** la operación es rechazada con un conflicto que identifica la clase en conflicto

#### Scenario: Solapamiento de instructor
- **WHEN** se intenta asignar como instructor efectivo a una persona que dicta otra clase activa en un horario superpuesto
- **THEN** la operación es rechazada con un conflicto

#### Scenario: Clases consecutivas
- **WHEN** se crea una clase de 10:00 a 11:00 en una sala que tiene otra clase de 09:00 a 10:00
- **THEN** la clase se crea correctamente

### Requirement: Personal programado vs. efectivo con historial
El sistema SHALL almacenar por separado el instructor programado y el efectivo, y el secretario programado y el efectivo. El efectivo SHALL inicializarse igual al programado. Cada reemplazo SHALL registrar el rol, la persona anterior, la persona nueva, el motivo, el usuario responsable y la fecha, sin alterar al personal programado. El instructor reemplazante SHALL ser una persona activa con rol Instructor, y el secretario, una persona activa con rol Secretario o Administrador. Solo se pueden hacer reemplazos sobre clases no cerradas.

#### Scenario: Reemplazo de instructor
- **WHEN** un administrativo asigna un instructor efectivo distinto por enfermedad del programado, indicando el motivo
- **THEN** cambia el instructor efectivo, el programado permanece inalterado y se registra el cambio en el historial con el motivo y el responsable

#### Scenario: Reemplazo de secretario
- **WHEN** se registra una sustitución de secretario para una clase concreta
- **THEN** cambia el secretario efectivo, se conserva el programado y se registra el cambio en el historial

#### Scenario: Reemplazante sin rol habilitado
- **WHEN** se intenta asignar como instructor efectivo a una persona sin rol Instructor activo
- **THEN** la operación es rechazada con un error de validación

#### Scenario: Reemplazo durante la clase
- **WHEN** se reemplaza el instructor de una clase EN_CURSO
- **THEN** el reemplazo se registra correctamente

### Requirement: Cambio de sala de una clase concreta
El sistema SHALL permitir cambiar la sala de una clase no cerrada sin alterar el horario recurrente, registrando auditoría, notificando a los alumnos con reserva activa y validando que la nueva sala no esté ocupada.

#### Scenario: Cambio excepcional de sala
- **WHEN** una clase concreta se traslada de "Sala Indoor" a "Sala Outdoor" por razones operativas
- **THEN** la clase pasa a "Sala Outdoor", el horario recurrente no cambia y los alumnos con reserva reciben una notificación

### Requirement: Cupo propio y disponibilidad
Cada clase concreta SHALL persistir su propio cupo, fijado al crearla según la regla de cupo efectivo y modificable solo de forma explícita sobre la clase, antes de su inicio y sin quedar por debajo de las reservas existentes. El sistema SHALL informar los lugares disponibles como `cupo − reservas` y el estado derivado COMPLETA cuando no queden lugares.

#### Scenario: Aislamiento frente a cambios de cupo del horario o de la actividad
- **WHEN** cambia la capacidad por defecto de la actividad de 20 a 25
- **THEN** las clases ya creadas conservan su cupo de 20

#### Scenario: Reducción de cupo por debajo de las reservas
- **WHEN** se intenta reducir a 10 el cupo de una clase con 12 reservas
- **THEN** la operación es rechazada con un conflicto

### Requirement: Máquina de estados de la clase concreta
El sistema SHALL persistir únicamente los estados PROGRAMADA, ABIERTA, SUSPENDIDA, CANCELADA y REPROGRAMADA, con estas transiciones:
- PROGRAMADA → ABIERTA, antes del inicio.
- PROGRAMADA o ABIERTA → SUSPENDIDA, antes del fin.
- PROGRAMADA o ABIERTA → CANCELADA, antes del inicio.
- PROGRAMADA o ABIERTA → REPROGRAMADA, antes del inicio.

SUSPENDIDA, CANCELADA y REPROGRAMADA SHALL ser terminales. El sistema SHALL derivar, sin persistirlos y en este orden de precedencia, los estados FINALIZADA (fin alcanzado), EN_CURSO (inicio alcanzado) y COMPLETA (ABIERTA sin lugares disponibles) para clases PROGRAMADAS o ABIERTAS. Solo las clases ABIERTAS admiten reservas.

#### Scenario: Apertura manual
- **WHEN** un administrativo abre una clase PROGRAMADA futura
- **THEN** su estado pasa a ABIERTA y admite reservas

#### Scenario: Apertura automática
- **WHEN** una clase PROGRAMADA entra en la ventana de apertura configurada (por defecto 7 días antes del inicio)
- **THEN** el proceso periódico la pasa a ABIERTA

#### Scenario: Rechazo de transición desde un estado terminal
- **WHEN** se intenta abrir una clase CANCELADA
- **THEN** el sistema rechaza la solicitud indicando que la transición no está permitida

#### Scenario: Estado derivado por tiempo
- **WHEN** se consulta una clase ABIERTA cuyo horario de fin ya pasó
- **THEN** el sistema la presenta como FINALIZADA, aunque su estado persistido siga siendo ABIERTA

### Requirement: Suspensión de clase concreta
El sistema SHALL permitir suspender una clase PROGRAMADA o ABIERTA antes de su fin, registrando la fecha, el usuario responsable, un motivo tipificado (CLIMA, FERIADO, DIA_NO_LABORABLE, AUSENCIA_INSTRUCTOR, ENFERMEDAD_INSTRUCTOR, PROBLEMA_EDILICIO, MANTENIMIENTO, OTRO) y observaciones (obligatorias si el motivo es OTRO). En la misma transacción, SHALL cancelar las reservas activas con devolución íntegra del crédito y notificar a los alumnos afectados, sin afectar a las demás clases del mismo horario. Un instructor solo SHALL poder suspender clases en las que es instructor programado o efectivo.

#### Scenario: Suspensión de clase individual
- **WHEN** un administrativo suspende una clase concreta por PROBLEMA_EDILICIO
- **THEN** la clase pasa a SUSPENDIDA, sus reservas activas se cancelan con devolución del crédito, los alumnos reciben una notificación, se registra la auditoría y las demás clases del horario no cambian

#### Scenario: Instructor suspende una clase ajena
- **WHEN** un instructor intenta suspender una clase en la que no es instructor programado ni efectivo
- **THEN** la operación es rechazada como recurso no encontrado

### Requirement: Cancelación definitiva de clase concreta
El sistema SHALL permitir cancelar una clase PROGRAMADA o ABIERTA antes de su inicio, registrando el motivo tipificado, las observaciones, el responsable y la fecha. Con los mismos efectos sobre reservas y notificaciones que la suspensión, SHALL preservar el registro histórico.

#### Scenario: Cancelación definitiva
- **WHEN** una clase ABIERTA con reservas se cancela antes de su inicio
- **THEN** pasa a CANCELADA, sus reservas se cancelan con devolución del crédito y la clase no admite más operaciones salvo la edición de notas

### Requirement: Reprogramación trazable
El sistema SHALL reprogramar una clase PROGRAMADA o ABIERTA, antes de su inicio, creando una nueva clase extraordinaria vinculada a la original (con la misma actividad y cupo, la sala indicada o la original, y como personal programado el personal efectivo de la original) y marcando la original como REPROGRAMADA con referencia a la nueva. Las reservas de la original SHALL cancelarse con devolución íntegra y los alumnos SHALL ser notificados.

#### Scenario: Reprogramación exitosa
- **WHEN** se reprograma una clase a una nueva fecha y hora futura sin solapamientos
- **THEN** la original queda REPROGRAMADA apuntando a la nueva, la nueva queda PROGRAMADA apuntando a la original, las reservas de la original se cancelan con devolución y el generador no recrea la ocurrencia original

### Requirement: Inmutabilidad histórica
Una clase SHALL considerarse cerrada cuando su fin ya pasó o su estado es terminal. Sobre una clase cerrada, el sistema SHALL rechazar cualquier modificación de cupo, sala, personal, horario o estado, y SHALL permitir únicamente la edición auditada de sus notas.

#### Scenario: Modificación sobre una clase finalizada
- **WHEN** un usuario intenta cambiar la sala o el instructor de una clase cuyo fin ya pasó
- **THEN** la operación es rechazada con un conflicto de negocio

#### Scenario: Notas sobre una clase finalizada
- **WHEN** un administrativo agrega una observación a una clase finalizada
- **THEN** la nota se guarda y se audita

### Requirement: Control de concurrencia optimista
El sistema SHALL proteger las ediciones administrativas de una clase (cupo, notas, sala y personal) con un token de versión que el cliente obtiene al consultar la clase y envía al modificarla. SHALL rechazar con un conflicto las modificaciones hechas sobre una versión desactualizada.

#### Scenario: Conflicto de modificación simultánea
- **WHEN** dos operadores modifican la misma clase partiendo de la misma versión
- **THEN** la primera modificación se confirma y la segunda es rechazada con un conflicto de concurrencia

### Requirement: Consulta de agenda y disponibilidad
El sistema SHALL permitir al personal consultar las clases por rango de fechas locales y filtrar por actividad, sala, instructor, secretario y estado (persistido o derivado), con paginación y orden cronológico. Un instructor sin rol administrativo SHALL ver solo las clases en las que es instructor programado o efectivo. Los usuarios autenticados SHALL poder consultar las clases futuras ABIERTAS de actividades activas, con sus lugares disponibles.

#### Scenario: Agenda del instructor
- **WHEN** un instructor consulta la agenda de la semana
- **THEN** recibe solo las clases en las que figura como instructor programado o efectivo

#### Scenario: Disponibilidad para alumnos
- **WHEN** un alumno consulta las clases disponibles de la semana
- **THEN** recibe solo las clases ABIERTAS que aún no comenzaron, con sus lugares disponibles e indicación de COMPLETA
