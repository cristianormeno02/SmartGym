## Purpose

Centraliza la administración de transacciones de pago multimétodo, el envío de notificaciones multicanal, agendas y calendarios adaptados por rol, fichas médicas/menores, auditoría global y métricas gerenciales.

## Requirements

### Requirement: Gestión de Pagos Multimétodo
El sistema SHALL registrar transacciones de pago vinculadas a membresías, clases individuales u otros conceptos, admitiendo múltiples medios (efectivo, transferencia, tarjeta, Mercado Pago) y estados de transacción (PENDIENTE, PAGADO, PARCIAL, ANULADO, RECHAZADO).

#### Scenario: Registro exitoso de pago de membresía
- **WHEN** un secretario registra un pago de membresía en efectivo con importe total
- **THEN** el sistema registra el comprobante de pago en estado PAGADO, vincula la transacción a la membresía y activa sus créditos

### Requirement: Notificaciones Multicanal Basadas en Eventos
El sistema SHALL emitir notificaciones internas y preparar el despacho por canales externos (correo electrónico, push o WhatsApp) ante eventos clave del sistema (suspensión de clases, cambio de instructor/sala, recordatorios de inicio de clase y avisos de vencimiento de membresía).

#### Scenario: Notificación por suspensión de clase
- **WHEN** se cancela o suspende una clase concreta
- **THEN** el sistema genera automáticamente notificaciones dirigidas a todos los alumnos inscriptos detallando el motivo y la restitución de créditos

### Requirement: Calendarios segmentados por Rol
El sistema SHALL presentar vistas de calendario adaptadas al rol del usuario: para Alumnos (sus reservas y clases disponibles), para Instructores (sus clases asignadas, reemplazos y lista de inscriptos), para Secretarios (sus turnos de control asignados) y para Administradores (agenda global del gimnasio).

#### Scenario: Consulta de calendario de instructor
- **WHEN** un usuario con rol Instructor accede a su vista de calendario
- **THEN** visualiza únicamente las clases concretas donde está asignado como instructor programado o efectivo, junto a la cantidad de alumnos inscriptos

### Requirement: Control de Certificados Médicos y Menores de Edad
El sistema SHALL registrar la vigencia de certificados médicos (con archivo adjunto en S3) pudiendo bloquear reservas ante vencimiento según configuración del gimnasio, así como exigir la vinculación de un adulto responsable para alumnos menores de edad.

#### Scenario: Bloqueo de reserva por certificado médico vencido
- **WHEN** un alumno con certificado médico expirado intenta reservar una clase y el gimnasio tiene activa la regla de bloqueo
- **THEN** el sistema rechaza la solicitud solicitando la actualización del apto físico

### Requirement: Auditoría Global y Dashboard de Métricas
El sistema SHALL registrar en una bitácora inmutable las operaciones críticas del sistema (identificador de usuario, entidad afectada, fecha/hora, operación y valores anteriores/nuevos) y calcular métricas operativas y comerciales para el dashboard administrativo.

#### Scenario: Visualización de métricas en dashboard administrativo
- **WHEN** un administrativo ingresa al panel principal
- **THEN** el sistema presenta en tiempo real la cantidad de alumnos activos, membresías vigentes, asistencia del día, porcentaje de ocupación e ingresos del período seleccionado
