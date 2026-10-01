## Purpose

El módulo People administra el registro maestro de personas físicas en Smart-Gym. Centraliza sus datos biográficos, de contacto y domicilio; garantiza la unicidad de sus documentos de identidad y emails mediante normalización canónica; administra su ciclo de vida de forma auditable; gestiona sus fotos de perfil en almacenamiento de objetos privado; y restringe el acceso a los datos personales según el rol. Todo esto con independencia de las credenciales de acceso, los roles funcionales y las membresías.

## ADDED Requirements

### Requirement: Registro único de Persona independiente de cuentas y roles
El sistema SHALL mantener un único registro de Persona por individuo, independiente de la existencia de una cuenta de usuario y de los roles asignados (`Persona ≠ Usuario ≠ Rol`). No SHALL existir entidades separadas para alumnos, instructores, secretarios o administradores que almacenen datos personales.

#### Scenario: Registro de una persona sin cuenta de usuario ni email
- **WHEN** un miembro del personal registra una persona proporcionando únicamente nombre y apellido
- **THEN** el sistema crea la Persona con un identificador único y estado `ACTIVA`, sin exigir usuario ni correo electrónico

#### Scenario: Persona con múltiples funciones en el gimnasio
- **WHEN** una persona existente actúa como instructor de una disciplina y alumno en otra
- **THEN** el sistema mantiene un único registro de Persona y los roles se asocian a su identificador desde el módulo Auth

#### Scenario: Vinculación posterior de cuenta de usuario
- **WHEN** se crea una cuenta de usuario para una persona que ya existía en People
- **THEN** la cuenta referencia el identificador de la Persona existente sin duplicar ni recrear su información personal

### Requirement: Atributos obligatorios y opcionales de Persona
El sistema SHALL requerir para cada Persona: identificador único, nombre y apellido (sin espacios sobrantes, no vacíos, máximo 100 caracteres), estado y fecha de alta. Todos los demás atributos SHALL ser opcionales: documento, fecha de nacimiento, género (`MASCULINO`, `FEMENINO`, `X`, `NO_INFORMA`), email, teléfono principal, teléfono secundario, domicilio, contacto de emergencia y foto.

#### Scenario: Registro con campos mínimos
- **WHEN** se envía una solicitud de alta sólo con nombre y apellido válidos
- **THEN** el sistema crea la persona asignando identificador, estado `ACTIVA` y fecha de alta, y deja vacíos los demás campos

#### Scenario: Registro completo
- **WHEN** se envía una solicitud con nombre, apellido, documento, fecha de nacimiento, género, email, teléfonos, domicilio y contacto de emergencia válidos
- **THEN** el sistema almacena todos los datos y retorna la ficha completa de la persona

#### Scenario: Rechazo por nombre o apellido ausente
- **WHEN** se intenta crear o actualizar una persona con nombre o apellido omitido, vacío o compuesto sólo por espacios
- **THEN** el sistema rechaza la solicitud con un error de validación (400) que identifica el campo

#### Scenario: Rechazo de email con formato inválido
- **WHEN** se envía un email que no tiene un formato válido
- **THEN** el sistema rechaza la solicitud con un error de validación (400)

#### Scenario: Menor de edad sin contacto de emergencia
- **WHEN** se crea o actualiza una persona cuya fecha de nacimiento indica menos de 18 años, sin un contacto de emergencia completo (nombre, teléfono y vínculo)
- **THEN** el sistema rechaza la solicitud con un error de validación (400) indicando que el contacto de emergencia es obligatorio para menores

### Requirement: Normalización canónica de documentos de identidad
El sistema SHALL admitir los tipos de documento `DNI`, `PASAPORTE`, `CI` y `OTRO`. El tipo y el número SHALL informarse juntos o ninguno. Se SHALL informar el país emisor (ISO 3166-1 alfa-2) para `PASAPORTE`, `CI` y `OTRO`; para `DNI` el sistema SHALL asignar `AR`. Antes de persistir o comparar, el sistema SHALL normalizar el número:
- `DNI`: se aceptan sólo dígitos, puntos, guiones y espacios; se eliminan los no dígitos y los ceros a la izquierda; el resultado debe tener entre 1 y 9 dígitos y ser distinto de cero.
- `PASAPORTE`, `CI` y `OTRO`: se eliminan los caracteres no alfanuméricos y se convierte a mayúsculas; el resultado debe tener entre 3 y 30 caracteres.

El sistema SHALL conservar también el número tal como fue ingresado.

#### Scenario: DNI con formato visual
- **WHEN** se ingresa el tipo `DNI` con el número `12.345.678` o `12-345-678`
- **THEN** el sistema almacena el número normalizado `12345678` y el país emisor `AR`

#### Scenario: DNI con ceros a la izquierda
- **WHEN** se ingresa el tipo `DNI` con el número `01.234.567`
- **THEN** el sistema almacena el número normalizado `1234567`

#### Scenario: DNI con letras
- **WHEN** se ingresa el tipo `DNI` con el número `12345678A`
- **THEN** el sistema rechaza la solicitud con un error de validación (400)

#### Scenario: Pasaporte con letras y separadores
- **WHEN** se ingresa el tipo `PASAPORTE`, país `BR` y número ` a-123.456-x `
- **THEN** el sistema almacena el número normalizado `A123456X`

#### Scenario: Número que queda vacío tras normalizar
- **WHEN** se ingresa un número de documento compuesto sólo por separadores (por ejemplo `---`)
- **THEN** el sistema rechaza la solicitud con un error de validación (400)

#### Scenario: Documento incompleto
- **WHEN** se informa un tipo de documento sin número, un número sin tipo, o un `PASAPORTE`, `CI` u `OTRO` sin país emisor
- **THEN** el sistema rechaza la solicitud con un error de validación (400)

### Requirement: Unicidad de documento y email
El sistema SHALL garantizar que no existan dos personas con la misma combinación de tipo de documento, país emisor y número normalizado, ni con el mismo email normalizado (sin espacios sobrantes y en minúsculas). El sistema SHALL permitir múltiples personas sin documento y múltiples personas sin email. La unicidad SHALL garantizarse también ante solicitudes concurrentes.

#### Scenario: Documento duplicado
- **WHEN** se intenta crear o actualizar una persona con un documento cuyo tipo, país y número normalizado coinciden con los de otra persona
- **THEN** el sistema rechaza la operación con 409 indicando que ya existe una persona con ese documento

#### Scenario: Altas concurrentes con el mismo documento
- **WHEN** dos solicitudes simultáneas intentan registrar personas con el mismo documento
- **THEN** sólo una se completa y la otra recibe 409 con el mismo mensaje de documento duplicado

#### Scenario: Mismo número en distinto país emisor
- **WHEN** se registran dos personas con `PASAPORTE` y el mismo número normalizado pero distinto país emisor
- **THEN** el sistema registra ambas personas sin conflicto

#### Scenario: Actualización conservando el propio documento
- **WHEN** se actualizan los datos de una persona sin modificar su documento
- **THEN** el sistema no considera que el documento esté duplicado con la misma persona

#### Scenario: Múltiples personas sin documento ni email
- **WHEN** se crean dos o más personas sin documento y sin email
- **THEN** el sistema registra a cada una sin conflicto de unicidad

#### Scenario: Email duplicado con distinta capitalización
- **WHEN** se intenta registrar una persona con el email `Ana@Mail.com` y ya existe otra con `ana@mail.com`
- **THEN** el sistema rechaza la operación con 409 indicando que el email ya está registrado

### Requirement: Email de personas con cuenta de usuario
El sistema SHALL impedir que se modifique desde People el email de una persona vinculada a una cuenta de usuario, porque ese email identifica la cuenta.

#### Scenario: Cambio de email con cuenta vinculada
- **WHEN** se actualiza una persona que tiene cuenta de usuario y se envía un email distinto del registrado
- **THEN** el sistema rechaza la operación con 409 indicando que el email es administrado por la cuenta de usuario

### Requirement: Ciclo de vida y transiciones de estado de Persona
El sistema SHALL gestionar el estado de cada Persona como uno solo de: `ACTIVA`, `INACTIVA`, `BLOQUEADA` o `FALLECIDA`. Las transiciones SHALL estar restringidas así:
- Entre `ACTIVA`, `INACTIVA` y `BLOQUEADA`: las puede realizar el personal (Administrador o Secretario).
- Hacia `FALLECIDA` desde cualquier otro estado: sólo un Administrador.
- Desde `FALLECIDA` hacia `ACTIVA`: sólo un Administrador, como corrección de un error.

El motivo SHALL ser obligatorio al pasar a `BLOQUEADA`, al pasar a `FALLECIDA` y al revertir `FALLECIDA`. Cada transición SHALL registrar la fecha, el usuario que la realizó y el motivo. Una transición no permitida o hacia el mismo estado SHALL rechazarse con 409.

#### Scenario: Estado inicial
- **WHEN** se crea una nueva persona
- **THEN** el sistema le asigna el estado `ACTIVA`

#### Scenario: Baja lógica
- **WHEN** un miembro del personal cambia a `INACTIVA` el estado de una persona `ACTIVA`
- **THEN** el sistema actualiza el estado, registra fecha y autor, y preserva todos sus datos e historial

#### Scenario: Bloqueo por sanción
- **WHEN** un miembro del personal cambia a `BLOQUEADA` el estado de una persona e informa un motivo
- **THEN** el sistema actualiza el estado y registra motivo, fecha y autor, sin alterar su cuenta de usuario ni sus membresías

#### Scenario: Bloqueo sin motivo
- **WHEN** se intenta pasar una persona a `BLOQUEADA` o `FALLECIDA` sin informar motivo
- **THEN** el sistema rechaza la solicitud con un error de validación (400)

#### Scenario: Registro de fallecimiento por personal no administrador
- **WHEN** un Secretario intenta cambiar a `FALLECIDA` el estado de una persona
- **THEN** el sistema rechaza la solicitud con 403

#### Scenario: Registro de fallecimiento
- **WHEN** un Administrador cambia a `FALLECIDA` el estado de una persona e informa un motivo
- **THEN** el sistema actualiza el estado y la persona pasa a ser de sólo lectura

#### Scenario: Edición de una persona fallecida
- **WHEN** se intenta actualizar los datos, la foto o el estado de una persona `FALLECIDA` sin que sea una reversión realizada por un Administrador
- **THEN** el sistema rechaza la operación con 409

#### Scenario: Reversión de un fallecimiento cargado por error
- **WHEN** un Administrador cambia a `ACTIVA` el estado de una persona `FALLECIDA` e informa un motivo
- **THEN** el sistema restablece el estado `ACTIVA` y registra motivo, fecha y autor

#### Scenario: Transición no permitida
- **WHEN** se solicita pasar a `INACTIVA` o `BLOQUEADA` una persona `FALLECIDA`, o pasar una persona al estado que ya tiene
- **THEN** el sistema rechaza la solicitud con 409

### Requirement: Efecto del estado de Persona sobre otros módulos
El sistema SHALL permitir nuevas membresías, movimientos de créditos, reservas y asignaciones como instructor únicamente a personas en estado `ACTIVA`. El estado de la Persona SHALL ser independiente del estado de la cuenta de usuario, de las membresías, de las reservas y de los grupos familiares: ningún cambio en esas dimensiones SHALL modificar el estado de la Persona, y un cambio de estado de la Persona SHALL preservar sin alteraciones sus membresías, reservas e historial.

#### Scenario: Reserva de una persona bloqueada
- **WHEN** una persona en estado `BLOQUEADA` o `INACTIVA` intenta reservar una clase o contratar una membresía
- **THEN** el sistema rechaza la operación indicando que la persona no está habilitada

#### Scenario: Membresía vencida
- **WHEN** la membresía de una persona `ACTIVA` vence o queda impaga
- **THEN** el estado de la persona sigue siendo `ACTIVA`

#### Scenario: Bloqueo preserva el historial
- **WHEN** una persona con membresías y reservas pasadas pasa a `BLOQUEADA`
- **THEN** sus membresías, créditos y reservas existentes permanecen sin cambios

### Requirement: Autorización de acceso a datos personales
El sistema SHALL restringir la búsqueda, consulta, alta, edición, cambio de estado y gestión de fotos de cualquier persona al personal (Administrador y Secretario). Cualquier usuario autenticado vinculado a una Persona SHALL poder consultar su propia ficha y modificar sólo sus teléfonos, domicilio, contacto de emergencia y foto. Ningún usuario podrá modificar desde autoservicio su nombre, apellido, documento, fecha de nacimiento, email o estado. El sistema no SHALL ofrecer eliminación física de personas.

#### Scenario: Alumno intenta consultar a otra persona
- **WHEN** un usuario con rol Alumno o Instructor solicita el listado de personas o la ficha de otra persona
- **THEN** el sistema responde 403

#### Scenario: Consulta de la ficha propia
- **WHEN** un usuario autenticado solicita su propia ficha
- **THEN** el sistema retorna los datos de la Persona vinculada a su cuenta

#### Scenario: Actualización de contacto propio
- **WHEN** un usuario autenticado actualiza sus teléfonos, domicilio o contacto de emergencia
- **THEN** el sistema guarda los cambios aplicando las mismas validaciones que el personal

#### Scenario: Intento de modificar datos protegidos por autoservicio
- **WHEN** un usuario autenticado intenta modificar por autoservicio su nombre, documento, email o estado
- **THEN** el sistema ignora o rechaza esos campos y no los modifica

### Requirement: Concurrencia en la edición de personas
El sistema SHALL detectar ediciones concurrentes de una misma persona y rechazar la que se base en una versión desactualizada.

#### Scenario: Edición con versión desactualizada
- **WHEN** dos operadores abren la misma ficha y ambos guardan cambios, enviando el segundo la versión que leyó antes del primer guardado
- **THEN** el sistema aplica el primer guardado y rechaza el segundo con 409 indicando que la ficha fue modificada

### Requirement: Gestión de foto de perfil en almacenamiento de objetos
El sistema SHALL almacenar las fotos de perfil en un almacenamiento de objetos privado (Cloudflare R2) a través de la abstracción `IFileStorageService`. La base de datos SHALL guardar sólo la metadata: clave del objeto, tipo de contenido, tamaño en bytes y fecha de carga. El sistema SHALL aceptar sólo archivos JPEG, PNG o WebP de hasta 5 MB cuya firma de archivo coincida con un formato admitido. Antes de almacenar la imagen, el sistema SHALL reencodearla a WebP, limitarla a 1024 × 1024 píxeles y eliminar su metadata. Las fotos SHALL entregarse mediante URLs de acceso temporal, nunca exponiendo la clave del objeto.

#### Scenario: Carga exitosa
- **WHEN** se sube una imagen JPEG, PNG o WebP válida de hasta 5 MB para una persona
- **THEN** el sistema almacena la imagen procesada con una clave única bajo el prefijo de la persona, actualiza la metadata y retorna una URL de acceso temporal

#### Scenario: Formato o tamaño no admitido
- **WHEN** se sube un archivo con tipo no admitido (por ejemplo PDF), cuya firma no coincide con el tipo declarado o que supera los 5 MB
- **THEN** el sistema rechaza la solicitud con 400 sin transferir el archivo al almacenamiento

#### Scenario: Eliminación de metadata sensible
- **WHEN** se sube una imagen JPEG que contiene metadata EXIF con geolocalización
- **THEN** la imagen almacenada no contiene esa metadata

#### Scenario: Reemplazo de foto
- **WHEN** se sube una nueva foto para una persona que ya tenía una
- **THEN** el sistema almacena la nueva foto, actualiza la metadata y elimina el objeto anterior del almacenamiento

#### Scenario: Fallo al guardar la metadata
- **WHEN** la imagen se almacena correctamente pero falla el guardado en la base de datos
- **THEN** el sistema elimina la imagen recién almacenada y reporta el error, conservando la foto anterior

#### Scenario: Eliminación de foto
- **WHEN** se solicita eliminar la foto de una persona
- **THEN** el sistema elimina el objeto del almacenamiento y restablece la metadata a nulo

#### Scenario: Foto provista por Google
- **WHEN** una persona no tiene foto propia pero tiene una foto provista por su cuenta de Google
- **THEN** el sistema muestra la foto de Google, y al cargar una foto propia muestra esta última

### Requirement: Búsqueda paginada de personas
El sistema SHALL permitir buscar personas con paginación (tamaño por defecto 20, máximo 100) y orden por apellido y nombre. Un único término de búsqueda SHALL coincidir:
- con nombre y apellido de forma parcial, sin distinguir mayúsculas ni acentos, exigiendo que cada palabra del término esté contenida;
- con el número de documento normalizado de forma exacta, cuando el término contenga dígitos y tenga al menos 5 caracteres alfanuméricos.

El sistema SHALL permitir además filtrar por estado y por tipo de documento.

#### Scenario: Búsqueda por documento con formato visual
- **WHEN** un operador busca con el término `12.345.678`
- **THEN** el sistema retorna la persona cuyo documento normalizado es `12345678`

#### Scenario: Búsqueda parcial sin acentos
- **WHEN** un operador busca con el término `gonza`
- **THEN** el sistema retorna una lista paginada que incluye personas con nombre o apellido González o Gonzalo

#### Scenario: Búsqueda por nombre completo
- **WHEN** un operador busca con el término `juan perez`
- **THEN** el sistema retorna las personas cuyo nombre y apellido contienen ambas palabras, por ejemplo "Juan Pérez"

#### Scenario: Filtro por estado
- **WHEN** un operador lista personas filtrando por el estado `BLOQUEADA`
- **THEN** el sistema retorna sólo personas en ese estado, con el total de resultados para paginar

#### Scenario: Persona inexistente
- **WHEN** se solicita la ficha de un identificador que no existe
- **THEN** el sistema responde 404
