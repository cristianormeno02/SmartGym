## Why

Los gimnasios orientados a clases y actividades dirigidas requieren una gestión integral, precisa y en tiempo real de su operación diaria y comercial. Actualmente, la falta de un sistema unificado genera sobreventa de cupos, inconsistencias en el control de asistencias, dificultades para gestionar roles cruzados (donde una persona es simultáneamente alumno e instructor), descontrol en saldos de membresías y rigidez ante situaciones operativas comunes (suspensiones por clima o mantenimiento, reemplazos de instructores y secretarios).

**Smart-Gym** surge para proporcionar una plataforma centralizada, construida como un monolito modular moderno (Angular 22 + ASP.NET Core Web API .NET 10 + PostgreSQL + S3), que garantice integridad transaccional, trazabilidad completa y un modelo de negocio altamente extensible.

## What Changes

* **Arquitectura base y backend modular**: Creación del monolito modular en ASP.NET Core (.NET 10) con Entity Framework Core, PostgreSQL y almacenamiento de medios en S3.
* **Modelo desacoplado Persona-Usuario-Rol**: Una persona física única con múltiples roles dinámicos (Administrativo, Secretario, Instructor, Alumno) y autenticación híbrida (Local + Google OAuth).
* **Gestión de actividades, salas, horarios y clases concretas**: Diferenciación estricta entre definición de actividad, plantilla de horario recurrente y la instancia de clase concreta (con estados, auditoría, secretarios a cargo y gestión de suspensiones/reemplazos).
* **Sistema de membresías, créditos y ledger de movimientos**: Modelo basado en créditos y reglas de acceso a actividades, con registro inmutable de movimientos (cargas, consumos, devoluciones, compensaciones).
* **Motor de reservas, cupos concurrentes y asistencias**: Reserva transaccional con control estricto de cupo bajo concurrencia, asistencia independiente de la reserva y políticas configurables de cancelación.
* **Motor de condiciones y beneficios para promociones y grupos familiares**: Soporte desacoplado para descuentos fijos/porcentuales, 2x1, primera clase gratis, alumno referido y familias.
* **Operaciones, pagos, notificaciones y analítica**: Registro de pagos multimétodo, alertas y notificaciones multicanal, calendarios segmentados por rol, fichas de menores y certificados médicos, auditoría y dashboard administrativo.
* **Frontend Angular 22**: SPA reactiva con Angular Material, Tailwind CSS, Signals y división estricta de áreas pública, autenticada y portales por rol.

## Capabilities

### New Capabilities
- `identity-access`: Gestión unificada de personas, cuentas de usuario, asignación múltiple de roles, autenticación local y Google OAuth/OIDC, y autorización por roles/permisos.
- `activities-schedule`: Administración de actividades, salas, horarios recurrentes con vigencia, generación de clases concretas, control de turnos de secretarios y registro de suspensiones y reemplazos.
- `memberships-credits`: Catálogo de planes, membresías adquiridas por alumnos, reglas de acceso a actividades, libro mayor (ledger) de movimientos de créditos, clases individuales y créditos compensatorios.
- `reservations-attendance`: Reservas de clases con garantía de cupo bajo concurrencia, estados de reserva, registro de asistencia en sala y políticas de cancelación/penalización.
- `promotions-family`: Motor dinámico de promociones basado en condiciones y beneficios evaluados en backend, gestión de grupos familiares y trazabilidad de aplicación.
- `operations-reporting`: Gestión de pagos, eventos y notificaciones multicanal, calendarios por rol, control de aptos médicos/menores, registro global de auditoría y dashboard de estadísticas gerenciales y operativas.

### Modified Capabilities
*(Ninguna - proyecto inicial nuevo)*

## Impact

* **Tecnología**: Nuevo repositorio y solución backend en .NET 10 y proyecto frontend en Angular 22.
* **Base de datos**: Esquema relacional en PostgreSQL con transacciones ACID, integridad referencial y uso puntual de JSONB para reglas dinámicas.
* **Almacenamiento**: Integración con Amazon S3 para fotos de perfil, imágenes de actividades y documentación/certificados médicos.
* **APIs**: Exposición de API REST documentada con OpenAPI/Swagger y protegida con JWT.
