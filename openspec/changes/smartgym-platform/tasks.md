## 1. Configuración Base e Infraestructura

- [x] 1.1 Inicializar la solución de ASP.NET Core Web API (.NET 10) con arquitectura de monolito modular y verificar que compile exitosamente con `dotnet build`
- [x] 1.2 Configurar Entity Framework Core con PostgreSQL, cadenas de conexión y migraciones iniciales, verificando conexión a la base de datos
- [x] 1.3 Inicializar el proyecto frontend en Angular 22 con soporte para Standalone Components, Angular Material y Tailwind CSS, verificando que ejecute con `ng build`
- [x] 1.4 Configurar la integración y abstracción de almacenamiento para Amazon S3 (o emulador local) para carga de archivos y avatares

## 2. Módulo Identity & Access

- [x] 2.1 Modelar entidades `Person`, `User`, `Role` y `PersonRole` asegurando soporte para múltiples roles simultáneos sin duplicar datos personales
- [x] 2.2 Implementar autenticación local por usuario/contraseña con hashing seguro y generación de tokens JWT con claims de roles
- [x] 2.3 Implementar autenticación con Google OAuth/OIDC vinculando identidades externas a la entidad `Person` existente
- [x] 2.4 Configurar políticas de autorización y middlewares de permisos por rol (Administrativo, Secretario, Instructor, Alumno, Público)

## 3. Módulo Activities & Schedules

- [x] 3.1 Implementar CRUD de `Activity` (con cupos mínimos/máximos, rangos de edad e imágenes) y `Room` (Indoor, Outdoor)
- [x] 3.2 Implementar `RecurringSchedule` con soporte para días de la semana, horarios, instructor asignado y fechas de vigencia (`ValidFrom`, `ValidTo`)
- [x] 3.3 Desarrollar el generador de `ClassSession` (clases concretas) con estados (`Scheduled`, `InProgress`, `Finished`, `Suspended`, `Cancelled`)
- [x] 3.4 Implementar lógica de suspensión de clases con registro de motivo y auditoría, y mecanismo de reemplazo para instructores y secretarios

## 4. Módulo Memberships & Credits Ledger

- [x] 4.1 Implementar catálogo de `MembershipPlan` con duración, precio, créditos y reglas de actividades permitidas
- [x] 4.2 Desarrollar gestión de `Membership` por alumno con fechas de vigencia, créditos disponibles y estados
- [x] 4.3 Construir el ledger inmutable `MembershipCreditMovement` para registrar débitos, recargas, devoluciones y compensaciones
- [x] 4.4 Implementar emisión de créditos compensatorios y tickets para clases individuales para alumnos sin membresía

## 5. Módulo Reservations & Attendance

- [x] 5.1 Implementar reserva de clases con validación transaccional y control de concurrencia sobre el cupo disponible (`SELECT ... FOR UPDATE`)
- [x] 5.2 Implementar ciclo de vida de la reserva (`Reserved`, `Confirmed`, `Cancelled`, `Attended`, `NoShow`, `WaitList`)
- [x] 5.3 Desarrollar registro independiente de asistencia para secretarios e instructores en sala
- [x] 5.4 Implementar políticas de cancelación configurables (cancelación anticipada con devolución vs cancelación tardía sin devolución)

## 6. Módulo Promotions & Family Groups

- [x] 6.1 Implementar gestión de `FamilyGroup` permitiendo asociar múltiples alumnos a un núcleo familiar
- [x] 6.2 Construir el motor de promociones basado en condiciones (alumno nuevo, grupo familiar, 2x1, referido) y beneficios (descuentos, créditos extra)
- [x] 6.3 Implementar registro auditable de uso de promociones con validación de topes de uso por persona y vigencia

## 7. Módulo Operations, Payments & Notifications

- [x] 7.1 Implementar registro de `Payment` multimétodo (efectivo, transferencia, tarjeta, Mercado Pago) asociado a membresías y clases
- [x] 7.2 Implementar gestión de certificados médicos (con adjuntos en S3) y validación de reglas de bloqueo por apto vencido y menores de edad
- [x] 7.3 Implementar servicio de notificaciones basado en eventos (suspensión de clase, recordatorios, avisos de membresía)
- [x] 7.4 Desarrollar sistema global de auditoría (`AuditLog`) y consultas de estadísticas y dashboard administrativo

## 8. Frontend Angular 22 & Vistas de Usuario

- [ ] 8.1 Implementar servicios de autenticación, interceptores JWT y guardas de rutas por rol
- [ ] 8.2 Construir vistas públicas: landing page, catálogo de actividades públicas y horarios disponibles
- [ ] 8.3 Desarrollar portal del alumno: consulta de membresía, saldo de clases, calendario y reserva/cancelación de clases
- [ ] 8.4 Desarrollar portal del instructor y secretario: calendario de clases asignadas, control de asistencias y gestión de turnos
- [ ] 8.5 Desarrollar portal administrativo: dashboard con métricas clave, gestión de personas, planes, promociones y reportes

## 9. Verificación y Pruebas End-to-End

- [ ] 9.1 Ejecutar pruebas unitarias y de integración para validar consistencia de cupos y movimientos de créditos bajo concurrencia
- [ ] 9.2 Realizar verificación de flujos completos (adquisición de plan → reserva → asistencia → consumo de crédito)
