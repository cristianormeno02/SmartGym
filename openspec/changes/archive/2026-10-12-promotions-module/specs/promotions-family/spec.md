## MODIFIED Requirements

### Requirement: Registro de uso de promociones y trazabilidad
El sistema SHALL registrar cada aplicación efectiva de una promoción (alumno beneficiado, promoción, beneficio concedido, fecha y entidad relacionada: membresía o reserva de clase), impidiendo que se superen los límites máximos de utilización por persona o vigencia general. Estos límites SHALL respetarse también ante canjes simultáneos: la verificación del tope y el registro del canje SHALL ejecutarse de forma atómica por promoción. Un canje SHALL originarse únicamente en la contratación de una membresía o en la confirmación de una reserva de clase; el sistema SHALL NOT ofrecer una operación de canje independiente de ellas. La vigencia de la promoción SHALL evaluarse con la fecha local del gimnasio. Cuando el canje corresponde a una membresía, SHALL quedar vinculado a ella, una membresía SHALL tener como máximo un canje vigente, y si esa membresía se cancela sin haber llegado a activarse el canje SHALL anularse: se conserva para auditoría pero deja de contar contra los topes de uso por persona y general. Cuando el canje habilita el acceso a una clase, SHALL registrarse al confirmarse la reserva y quedar vinculado a ella (`ReservationId`), respaldando una única reserva; SHALL anularse si la reserva se cancela con derecho a restitución (cancelación anticipada, excepción administrativa o suspensión, cancelación o reprogramación de la clase) y SHALL conservarse vigente si la cancelación es tardía o el alumno asistió o se ausentó. La anulación de un canje SHALL ser idempotente.

#### Scenario: Control de tope de usos por persona
- **WHEN** un alumno intenta aplicar nuevamente una promoción limitada a un único uso por persona
- **THEN** el sistema rechaza la aplicación de la promoción informando que el beneficio ya fue consumido con anterioridad

#### Scenario: Anulación del canje por membresía nunca activada
- **WHEN** una membresía `PENDING` contratada con una promoción de un único uso por persona se cancela por falta de pago
- **THEN** el canje queda anulado y el alumno puede volver a aplicar esa promoción en una nueva contratación

#### Scenario: Canje conservado al cancelar una membresía activada
- **WHEN** se cancela una membresía que ya estuvo `ACTIVE` y fue contratada con una promoción
- **THEN** el canje permanece vigente y sigue contando contra los topes de uso

#### Scenario: Canje promocional en reserva de clase anulado por suspensión de la clase
- **WHEN** un alumno utiliza una promoción de "Primera Clase Gratis" para reservar una clase y luego el gimnasio suspende esa clase
- **THEN** el canje vinculado a la reserva se anula y el alumno puede utilizar su beneficio en otra clase

#### Scenario: Canje conservado tras cancelación tardía de la reserva
- **WHEN** un alumno que reservó con una promoción de un único uso cancela faltando 30 minutos para el inicio, sin excepción administrativa
- **THEN** el canje permanece vigente y el alumno no puede volver a aplicar la promoción

#### Scenario: Último cupo general disputado por dos canjes simultáneos
- **WHEN** a una promoción con tope general de 100 usos le queda un único uso disponible y dos alumnos distintos contratan con ella al mismo tiempo
- **THEN** exactamente una contratación obtiene el beneficio, la otra es rechazada con el código `PROMOTION_LIMIT_REACHED` y el contador de usos queda en 100

#### Scenario: Tope por persona disputado por dos canjes simultáneos del mismo alumno
- **WHEN** un mismo alumno envía al mismo tiempo dos operaciones distintas que aplican una promoción limitada a un único uso por persona
- **THEN** sólo una de ellas registra el canje y la otra es rechazada con el código `PROMOTION_LIMIT_REACHED`

#### Scenario: Anulación repetida del mismo canje
- **WHEN** se solicita anular un canje que ya fue anulado
- **THEN** la operación termina sin error y el contador de usos de la promoción no vuelve a decrementarse

#### Scenario: Canje sin membresía ni reserva
- **WHEN** un usuario autenticado invoca `POST /api/promotions/redeem`
- **THEN** el sistema no registra ningún canje, porque esa operación no existe

## ADDED Requirements

### Requirement: Ciclo de vida de la promoción
El sistema SHALL mantener cada promoción en uno de los estados `Active`, `Paused` o `Archived`. Una promoción SHALL crearse en estado `Active`. Las transiciones válidas SHALL ser `Active → Paused`, `Paused → Active`, `Active → Archived` y `Paused → Archived`; `Archived` SHALL ser terminal y cualquier otra transición SHALL rechazarse con el código `PROMOTION_INVALID_TRANSITION`. Sólo una promoción `Active` y dentro de su vigencia SHALL poder evaluarse o canjearse; para las demás la evaluación SHALL rechazarse con `PROMOTION_NOT_APPLICABLE`. Pausar o archivar una promoción SHALL NOT anular ni modificar los canjes ya registrados, y una promoción archivada SHALL seguir siendo consultable junto con sus canjes.

#### Scenario: Promoción pausada no aplicable
- **WHEN** un alumno contrata un plan con el código de una promoción en estado `Paused` y dentro de su vigencia
- **THEN** el sistema rechaza la aplicación con el código `PROMOTION_NOT_APPLICABLE` y no registra ningún canje

#### Scenario: Reactivación de una promoción pausada
- **WHEN** el personal activa una promoción `Paused` que sigue dentro de su vigencia
- **THEN** la promoción pasa a `Active` y vuelve a ser aplicable conservando su contador de usos

#### Scenario: Archivo conserva los canjes
- **WHEN** el personal archiva una promoción con 12 canjes vigentes
- **THEN** la promoción pasa a `Archived`, deja de ser aplicable y sus 12 canjes y las membresías o reservas asociadas permanecen sin cambios

#### Scenario: Transición inválida desde archivada
- **WHEN** el personal intenta activar una promoción `Archived`
- **THEN** el sistema rechaza la operación con el código `PROMOTION_INVALID_TRANSITION`

### Requirement: Administración y validación de promociones
El sistema SHALL permitir al personal con permiso `PROMOTIONS_MANAGE` crear y editar promociones, validando: código obligatorio y único sin distinguir mayúsculas (`PROMOTION_CODE_TAKEN`); descuento porcentual mayor que 0 y menor o igual que 100; descuento fijo mayor que 0; créditos adicionales entero mayor o igual que 1; fecha de fin, si existe, mayor o igual que la de inicio; tope general, si existe, mayor o igual que 1; tope por persona mayor o igual que 1; y plan aplicable, si se indica, existente y activo. Los valores inválidos SHALL rechazarse y SHALL NOT corregirse en silencio. Una promoción con al menos un canje registrado, vigente o anulado, SHALL admitir cambios únicamente en nombre, descripción, fecha de fin y topes de uso; el intento de modificar su código, condición, beneficio, valor, fecha de inicio o plan aplicable SHALL rechazarse con `PROMOTION_LOCKED_FIELDS`. Un tope SHALL NOT reducirse por debajo de los usos ya consumidos. La edición SHALL usar concurrencia optimista y rechazar con `CONCURRENCY_CONFLICT` una versión desactualizada. Una promoción `Archived` SHALL NOT poder editarse.

#### Scenario: Alta con porcentaje inválido
- **WHEN** el personal crea una promoción de descuento porcentual con valor 150
- **THEN** el sistema rechaza el alta con un error de validación sobre el valor del beneficio y no crea la promoción

#### Scenario: Alta con tope por persona en cero
- **WHEN** el personal crea una promoción con tope por persona igual a 0
- **THEN** el sistema rechaza el alta con un error de validación, en lugar de registrar la promoción con tope 1

#### Scenario: Código duplicado con otra capitalización
- **WHEN** existe la promoción `BIENVENIDA` y el personal crea otra con el código `bienvenida`
- **THEN** el sistema rechaza el alta con el código `PROMOTION_CODE_TAKEN`

#### Scenario: Edición del beneficio de una promoción ya canjeada
- **WHEN** el personal intenta cambiar de 20 % a 50 % el valor de una promoción que ya tiene canjes registrados
- **THEN** el sistema rechaza la edición con el código `PROMOTION_LOCKED_FIELDS` y la promoción conserva su valor

#### Scenario: Extensión de la vigencia de una promoción ya canjeada
- **WHEN** el personal extiende un mes la fecha de fin de una promoción que ya tiene canjes registrados
- **THEN** el sistema guarda la nueva fecha de fin

#### Scenario: Reducción del tope general por debajo de lo consumido
- **WHEN** una promoción acumula 40 usos y el personal intenta fijar su tope general en 30
- **THEN** el sistema rechaza la edición con un error de validación sobre el tope general

#### Scenario: Edición con versión desactualizada
- **WHEN** dos operadores editan la misma promoción y el segundo envía la versión que leyó antes de que el primero guardara
- **THEN** el sistema rechaza la segunda edición con el código `CONCURRENCY_CONFLICT`

### Requirement: Condiciones no verificables
El sistema SHALL NOT conceder el beneficio de una promoción cuya condición no pueda verificar. La creación de una promoción con un tipo de condición sin evaluación implementada SHALL rechazarse con un error de validación, y la evaluación o el canje de una promoción existente con ese tipo de condición SHALL rechazarse con `PROMOTION_NOT_APPLICABLE`.

#### Scenario: Alta de una promoción de referido
- **WHEN** el personal crea una promoción cuya condición es "alumno referido" y el sistema no tiene implementada la verificación de referidos
- **THEN** el sistema rechaza el alta con un error de validación que indica que ese tipo de condición no está disponible

#### Scenario: Evaluación de una promoción heredada con condición no verificable
- **WHEN** un alumno contrata un plan con el código de una promoción 2x1 creada con anterioridad
- **THEN** el sistema rechaza la aplicación con el código `PROMOTION_NOT_APPLICABLE` y no concede ningún descuento

### Requirement: Evaluación previa y autorización de consultas
El sistema SHALL ofrecer una evaluación previa de un código promocional para la contratación de un plan, que informe el descuento y los créditos adicionales sin registrar ningún canje ni modificar contadores. El precio base SHALL obtenerse del plan en el servidor y SHALL NOT aceptarse del cliente. Un alumno SHALL poder evaluar únicamente para sí mismo; el personal SHALL poder evaluar para cualquier persona. La consulta del catálogo de promociones, de una promoción por identificador o por código y de sus contadores de uso SHALL requerir el permiso `PROMOTIONS_READ`. La consulta de los canjes de una promoción SHALL requerir `PROMOTIONS_REDEMPTIONS_READ`, estar paginada e incluir los canjes anulados identificados como tales. La consulta de los canjes de una persona SHALL estar permitida al personal con `PROMOTIONS_REDEMPTIONS_READ` y al propio titular; cualquier otro usuario SHALL recibir una respuesta de acceso denegado.

#### Scenario: Evaluación previa sin efectos
- **WHEN** un alumno evalúa un código de 20 % de descuento para un plan cuyo precio es 10.000
- **THEN** el sistema informa precio original 10.000, descuento 2.000 y precio final 8.000, y el contador de usos de la promoción no cambia

#### Scenario: Precio base tomado del servidor
- **WHEN** un cliente envía en la evaluación un precio base distinto del precio del plan
- **THEN** el sistema ignora ese valor y calcula el descuento sobre el precio vigente del plan

#### Scenario: Alumno evalúa para otra persona
- **WHEN** un alumno solicita la evaluación de un código indicando el identificador de otro alumno
- **THEN** el sistema rechaza la solicitud con acceso denegado

#### Scenario: Alumno consulta los canjes de otro alumno
- **WHEN** un alumno solicita los canjes promocionales de otra persona
- **THEN** el sistema rechaza la solicitud con acceso denegado y no devuelve datos

#### Scenario: Alumno consulta sus propios canjes
- **WHEN** un alumno solicita sus propios canjes promocionales
- **THEN** el sistema devuelve sus canjes vigentes

#### Scenario: Alumno consulta una promoción por código
- **WHEN** un alumno invoca la consulta de una promoción por código
- **THEN** el sistema rechaza la solicitud con acceso denegado y no expone los contadores de uso

### Requirement: Auditoría de promociones
El sistema SHALL registrar en la auditoría el alta, la edición y cada cambio de estado de una promoción, así como cada canje y cada anulación de canje, indicando el usuario que operó, la promoción y, cuando corresponda, el alumno beneficiado y la membresía o reserva relacionada. En las ediciones SHALL registrar los valores anteriores y los nuevos.

#### Scenario: Auditoría de una pausa
- **WHEN** una secretaria pausa una promoción
- **THEN** queda un registro de auditoría con la acción, la promoción, el estado anterior `Active`, el nuevo estado `Paused` y la usuaria que operó

#### Scenario: Auditoría de un canje anulado
- **WHEN** se anula el canje de una membresía `PENDING` cancelada por falta de pago
- **THEN** queda un registro de auditoría de la anulación con la promoción, el alumno y la membresía relacionada
