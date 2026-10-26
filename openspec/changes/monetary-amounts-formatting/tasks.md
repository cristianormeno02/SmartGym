## 1. Utilidad y Pipe de Formato Monetario

- [x] 1.1 (TDD Red) Crear `frontend/src/app/core/utils/monetary-format.spec.ts` y `frontend/src/app/shared/pipes/money-amount.pipe.spec.ts` cubriendo:
  - `formatMonetaryAmount`: enteros (`35000` → `'35.000,00'`), decimales de 1 y 2 dígitos (`1234.5` → `'1.234,50'`, `1234.56` → `'1.234,56'`), redondeo a 2 decimales (`10.556` → `'10,56'`), cero (`0` → `'0,00'`), millones (`1250000` → `'1.250.000,00'`), valores `null`/`undefined`/`NaN` (`''`).
  - `parseMonetaryAmount`: cadenas formateadas (`'35.000,00'` → `35000`, `'1.234,50'` → `1234.5`, `'0,00'` → `0`), números directos y valores vacíos/inválidos (`null`).
  - `MoneyAmountPipe`: transformación de números a formato con punto de miles y coma decimal (`35000` → `'35.000,00'`) y fallback `'0,00'` para `null`/`undefined`.
- [x] 1.2 (TDD Green) Implementar `frontend/src/app/core/utils/monetary-format.ts` y `frontend/src/app/shared/pipes/money-amount.pipe.ts`, ejecutando las pruebas unitarias hasta que pasen en verde.

## 2. Directiva de Ingreso de Importes con Formato (`MonetaryInputDirective`)

- [x] 2.1 (TDD Red) Crear `frontend/src/app/shared/directives/monetary-input.directive.spec.ts` verificando:
  - Al asignar un valor numérico al `FormControl` (`setValue(35000)` / `patchValue`), el `input.value` muestra `'35.000,00'`.
  - Al escribir dígitos en el input (`'35000'`), el valor visual formatea los miles con punto (`'35.000'`) y el `FormControl` recibe el número `35000`.
  - Al escribir parte decimal con coma (`'35000,5'` o `'1250,75'`), el `input.value` muestra `'35.000,5'` / `'1.250,75'` (limitando a 2 decimales) y el `FormControl` recibe `35000.5` / `1250.75`.
  - Al perder el foco (`blur`), el `input.value` completa siempre los 2 decimales (`'35.000'` → `'35.000,00'`, `'35.000,5'` → `'35.000,50'`) y marca el control como `touched`.
  - Al limpiar el campo (`''`), el `FormControl` queda en `null`.
- [x] 2.2 (TDD Green) Implementar `MonetaryInputDirective` en `frontend/src/app/shared/directives/monetary-input.directive.ts` implementando `ControlValueAccessor`, y verificar que todas las pruebas pasen.

## 3. Integración en Formulario de Planes y Vistas de Catálogo, Inicio y Portales

- [x] 3.1 (TDD Red) Actualizar las pruebas en:
  - `frontend/src/app/features/catalog/membership-plan-form-modal/membership-plan-form-modal.spec.ts`: verificar que el input de precio muestra el importe formateado (`'35.000,00'`) en modo edición y al ingresar datos, enviando el número (`35000`) en el payload de creación/actualización.
  - `frontend/src/app/features/catalog/plans-management/plans-management.spec.ts` y `frontend/src/app/features/public/plans/plans.spec.ts`: verificar que los precios de los planes se renderizan con el formato `$35.000,00` y `$20.000,00`.
  - `frontend/src/app/features/portal/admin/admin-portal.spec.ts`: verificar que `monthlyRevenue` en KPIs y el beneficio de descuento de monto fijo (`FixedAmountDiscount`) en Promociones se muestran con formato de 2 decimales, coma decimal y punto de miles (ej. `$150.000,00` y `$5.000,00 de descuento`).
- [x] 3.2 (TDD Green) Actualizar los componentes para usar `MonetaryInputDirective`, `MoneyAmountPipe` y `formatMonetaryAmount`:
  - `frontend/src/app/features/catalog/membership-plan-form-modal/membership-plan-form-modal.ts`
  - `frontend/src/app/features/catalog/plans-management/plans-management.ts`
  - `frontend/src/app/features/public/plans/plans.ts`
  - `frontend/src/app/features/public/home/home.ts`
  - `frontend/src/app/features/portal/admin/admin-portal.ts`
  - `frontend/src/app/features/portal/student/student-portal.ts`

## 4. Verificación Integral

- [x] 4.1 Ejecutar la suite completa de pruebas unitarias de frontend (`npm test -- --watch=false`) y el build de producción (`npm run build`) en `frontend/` para confirmar que todas las pruebas pasen sin regresiones ni errores de compilación.
