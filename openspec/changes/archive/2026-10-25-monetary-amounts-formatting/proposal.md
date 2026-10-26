## Why

Actualmente, los importes monetarios en la interfaz web de SmartGym se muestran e ingresan sin un formato uniforme ni acorde a la convención numérica local: en varias pantallas se interpolan los números crudos sin decimales ni separadores (`$35000`), en otras se usa `DecimalPipe` con el locale por defecto en inglés (`en-US`, produciendo `$35,000.00` con coma de miles y punto decimal), y en los formularios de carga/edición (como el precio de los planes de membresía) se utiliza `<input type="number">` nativo que impide visualizar separadores de miles y fuerza el comportamiento dispar del navegador con separadores decimales.

Unificar el formato monetario con **2 decimales fijos, coma (`,`) como separador de decimales y punto (`.`) como separador de miles** (ej. `35.000,00`) tanto al mostrar importes como durante el ingreso de datos mejora la legibilidad, evita errores de carga por ceros de más o de menos y brinda una experiencia consistente en toda la plataforma.

## What Changes

- **Utilidades y Pipe compartido de formato monetario (`MonetaryFormat`)**:
  - Se incorporarán funciones puras reutilizables (`formatMonetaryAmount`, `parseMonetaryAmount`) y un pipe standalone (`MoneyAmountPipe` / `moneyAmount`) que formatean cualquier valor numérico monetario con exactamente 2 decimales, coma (`,`) como separador decimal y punto (`.`) como separador de miles (ej. `35000` → `35.000,00`, `1234.5` → `1.234,50`, `0` → `0,00`).
- **Directiva / Control de ingreso de importes con formato (`MonetaryInputDirective`)**:
  - Se proveerá una directiva `ControlValueAccessor` aplicable a campos de texto (`<input type="text" inputmode="decimal" appMonetaryInput>`) integrada con `ReactiveFormsModule` y `FormsModule`:
    - Muestra el valor con separador de miles (`.`) y coma decimal (`,`), completando los 2 decimales al perder el foco (`blur`) o al inicializar el formulario en modo edición (ej. `35.000,00`).
    - Durante la escritura formatea dinámicamente la parte entera con puntos de miles y admite coma (o punto convertido a coma) para ingresar hasta 2 dígitos decimales.
    - Expone al `FormControl` subyacente el valor numérico (`number | null`) para que las validaciones (`Validators.required`, `Validators.min(0.01)`) y los payloads enviados a la API REST continúen recibiendo `number` estándar sin cambios de contrato en el backend.
- **Actualización de todas las vistas que muestran o ingresan importes monetarios**:
  - **Formulario de Planes de Membresía (`MembershipPlanFormModalComponent`)**: El campo `price` utilizará el ingreso formateado (`placeholder="35.000,00"`), mostrando el importe con formato tanto al tipear como al editar un plan existente.
  - **Catálogo público e inicio (`HomeComponent`, `PlansComponent`)**: Los precios de planes se mostrarán con el formato `$35.000,00`.
  - **Administración de catálogo (`PlansManagementComponent`)**: Los precios de planes se mostrarán con el formato `$35.000,00`.
  - **Portal de Administración (`AdminPortalComponent`)**: La métrica de `Ingresos del Mes` (`monthlyRevenue`) y las etiquetas de promociones de descuento fijo (`FixedAmountDiscount`) se mostrarán con formato `$35.000,00`.
  - **Portal del Alumno (`StudentPortalComponent`)**: El `Saldo pendiente` (`pendingBalance`) de la membresía se mostrará con formato `$15.000,00`.

## Capabilities

### New Capabilities
<!-- Ninguna capacidad de dominio nueva; se refinan las capacidades existentes en su representación e ingreso de importes. -->

### Modified Capabilities
- `memberships`: Los precios de planes de membresía (en catálogo público, inicio, panel de administración y formulario de alta/edición) y los saldos/importes de membresías en el portal del alumno se muestran e ingresan con 2 decimales, coma decimal y punto de miles.
- `operations-reporting`: Los importes monetarios de métricas gerenciales (ingresos mensuales en el dashboard administrativo) se muestran con 2 decimales, coma decimal y punto de miles.
- `promotions-family`: Los importes monetarios de beneficios de descuento de monto fijo en el listado de promociones se muestran con 2 decimales, coma decimal y punto de miles.

## Impact

- **Frontend (`frontend/src/app`)**:
  - Nuevos artefactos en `src/app/shared/`:
    - `src/app/core/utils/monetary-format.ts` (+ `.spec.ts`): funciones de formateo y parseo de importes.
    - `src/app/shared/pipes/money-amount.pipe.ts` (+ `.spec.ts`): pipe standalone para plantillas Angular.
    - `src/app/shared/directives/monetary-input.directive.ts` (+ `.spec.ts`): directiva `ControlValueAccessor` para inputs de importes monetarios.
  - Componentes actualizados:
    - `src/app/features/catalog/membership-plan-form-modal/membership-plan-form-modal.ts` (+ `.spec.ts`)
    - `src/app/features/catalog/plans-management/plans-management.ts` (+ `.spec.ts`)
    - `src/app/features/public/plans/plans.ts` (+ `.spec.ts`)
    - `src/app/features/public/home/home.ts`
    - `src/app/features/portal/admin/admin-portal.ts` (+ `.spec.ts`)
    - `src/app/features/portal/student/student-portal.ts`
- **Backend**:
  - Sin cambios. Los contratos JSON siguen transportando números (`decimal` en .NET / `number` en JSON).
- **Dependencias**:
  - Sin dependencias externas nuevas.
