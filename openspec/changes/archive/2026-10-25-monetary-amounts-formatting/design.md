## Context

Ver `proposal.md` para la motivación y las especificaciones delta en `specs/` para los requerimientos normativos.

Estado actual relevante en el frontend (`frontend/src/app`):
- `app.config.ts` no registra un locale personalizado ni existe una utilidad centralizada para importes monetarios.
- En vistas de planes (`HomeComponent`, `PlansComponent`, `PlansManagementComponent`) y en la etiqueta de promociones (`AdminPortalComponent.promotionBenefitLabel`), los importes se interpolan directamente como `\${{ plan.price }}` o `$${p.benefitValue}`, mostrando enteros sin separador de miles ni decimales (ej. `$35000`).
- En `AdminPortalComponent` (`monthlyRevenue`) y `StudentPortalComponent` (`pendingBalance`), se usa `| number:'1.2-2'` con el locale por defecto de Angular (`en-US`), lo que invierte los separadores (`.` para decimales y `,` para miles: `$35,000.00`).
- En `MembershipPlanFormModalComponent`, el campo `price` es `<input type="number" step="0.01" min="0.01" formControlName="price">`, lo cual no permite mostrar puntos de miles mientras se escribe o edita un plan, ni garantiza 2 decimales con coma decimal.

## Goals / Non-Goals

**Goals:**
- Garantizar que **todos** los importes monetarios mostrados en la aplicación se representen con exactamente **2 decimales**, **coma (`,`) como separador de decimales** y **punto (`.`) como separador de miles** (ej. `35.000,00`, `0,00`, `1.250,50`).
- Proveer una experiencia de ingreso de importes monetarios (`appMonetaryInput`) que muestre el formato con punto de miles y coma decimal tanto al cargar valores existentes como al tipear y al salir del foco (`blur`), manteniendo el valor del `FormControl` como `number | null` para no alterar validadores ni contratos con el backend.
- Mantener una implementación determinista, sin depender de configuraciones regionales del navegador o del sistema operativo del cliente.

**Non-Goals:**
- Modificar tipos de datos o serialización JSON en el backend .NET (que ya opera con `decimal` y recibe/envía números JSON estándar).
- Afectar campos numéricos que **no** son importes monetarios (tales como `durationDays`, `credits`, `capacity`, `minAge`/`maxAge`, `compensatoryAmount`, porcentajes de asistencia o descuentos porcentuales).

## Decisions

### D1: Utilidad determinista de formateo y parseo (`src/app/core/utils/monetary-format.ts`)
En lugar de depender de `Intl.NumberFormat` del navegador (cuyo formato exacto puede variar entre motores o configuraciones de locale) o de `DecimalPipe` acoplado a `LOCALE_ID`, se implementará un módulo puro y testeable `monetary-format.ts`:

- `formatMonetaryAmount(value: number | string | null | undefined): string`:
  - Si el valor es `null`, `undefined`, `''` o `NaN`, devuelve `''` (o `'0,00'` cuando se invoque con valor por defecto en visualización).
  - Redondea a 2 decimales (`Math.round((num + Number.EPSILON) * 100) / 100`).
  - Separa la parte entera y la parte decimal (fija de 2 dígitos `toFixed(2)`).
  - Inserta `.` cada 3 dígitos en la parte entera (`/\B(?=(\d{3})+(?!\d))/g`) y une con `,` la fracción de 2 dígitos.
  - Soporta números negativos anteponiendo `-` si correspondiera (ej. `-1.234,50`).
- `parseMonetaryAmount(raw: string | number | null | undefined): number | null`:
  - Si recibe un `number` finito, lo devuelve redondeado a 2 decimales.
  - Si recibe un `string`:
    - Si contiene `,`, trata los `.` como separadores de miles (los elimina) y reemplaza la `,` por `.` decimal.
    - Si no contiene `,` pero proviene de una entrada del usuario o número serializado, normaliza adecuadamente (en el contexto del input formateado, los puntos son separadores de miles y la coma es el único separador decimal).
    - Devuelve `number` finito o `null` si la cadena está vacía o no es numérica.

### D2: Pipe standalone `MoneyAmountPipe` (`src/app/shared/pipes/money-amount.pipe.ts`)
- Selector: `moneyAmount` (`standalone: true`, `pure: true`).
- Transformación: `transform(value: number | string | null | undefined, defaultEmpty = '0,00'): string`.
- Si `value` es `null` o `undefined`, retorna `defaultEmpty` (`'0,00'`), de modo que expresiones como `\${{ metrics()?.monthlyRevenue | moneyAmount }}` o `\${{ plan.price | moneyAmount }}` siempre rendericen `$0,00` o `$35.000,00` de forma segura.

### D3: Directiva `MonetaryInputDirective` (`src/app/shared/directives/monetary-input.directive.ts`)
- Selector: `input[appMonetaryInput]` (`standalone: true`), implementando `ControlValueAccessor` (`NG_VALUE_ACCESSOR`).
- Atributos de host recomendados: `type="text"`, `inputmode="decimal"`, `autocomplete="off"`.
- **Comportamiento en `writeValue(value: number | null)`** (ej. al hacer `form.patchValue({ price: 35000 })` o `form.reset()`):
  - Si `value !== null && value !== undefined && !Number.isNaN(Number(value))`, escribe en el `input.value` el importe formateado completo con 2 decimales (`formatMonetaryAmount(Number(value))` → `'35.000,00'`).
  - Si `value === null || value === undefined`, limpia `input.value = ''`.
- **Comportamiento durante la escritura (`input` event)**:
  - Permite dígitos `0-9` y coma `,` como separador decimal.
  - Si el usuario presiona `.` en el teclado numérico como intención de separador decimal cuando aún no hay coma y el cursor está al final (o mediante normalización), o bien si escribe dígitos:
    - Para evitar ambigüedad con el punto de miles, cualquier `,` inicia la parte decimal (máximo 2 dígitos decimales). Si el usuario ingresa dígitos en la parte entera, se formatean automáticamente en vivo con `.` como separador de miles (ej. escribir `35000` muestra `35.000`; escribir `35000,5` muestra `35.000,5`).
  - Emite mediante `onChange(numericValue)` el valor `number | null` correspondiente (ej. `35000` o `35000.5`) hacia el `FormControl`.
- **Comportamiento al perder foco (`blur` event)**:
  - Invoca `onTouched()`.
  - Si hay un número válido en el control, completa el formato a exactamente 2 decimales en el input visual (ej. `35.000` → `35.000,00`, `35.000,5` → `35.000,50`).

### D4: Puntos de aplicación en vistas y componentes
1. **`MembershipPlanFormModalComponent`**:
   - Reemplazar `<input type="number" step="0.01" min="0.01" formControlName="price" placeholder="35000" ...>` por `<input type="text" inputmode="decimal" appMonetaryInput formControlName="price" placeholder="35.000,00" ...>`.
   - Importar `MonetaryInputDirective` en `imports` del componente.
2. **`PlansManagementComponent`**:
   - Importar `MoneyAmountPipe` y actualizar `\${{ plan.price }}` → `\${{ plan.price | moneyAmount }}`.
3. **`PlansComponent` (`/planes`)**:
   - Importar `MoneyAmountPipe` y actualizar `\${{ plan.price }}` → `\${{ plan.price | moneyAmount }}`.
4. **`HomeComponent` (`/`)**:
   - Importar `MoneyAmountPipe` y actualizar `\${{ plan.price }}` → `\${{ plan.price | moneyAmount }}`.
5. **`AdminPortalComponent` (`/portal/admin`)**:
   - Importar `MoneyAmountPipe`.
   - En tarjeta "Ingresos del Mes": reemplazar `\${{ metrics()?.monthlyRevenue ?? 0 | number:'1.2-2' }}` por `\${{ metrics()?.monthlyRevenue ?? 0 | moneyAmount }}`.
   - En `promotionBenefitLabel(p: Promotion)`: para `PromotionBenefitType.FixedAmountDiscount`, usar `$${formatMonetaryAmount(p.benefitValue)} de descuento`.
6. **`StudentPortalComponent` (`/portal/alumno`)**:
   - Importar `MoneyAmountPipe`.
   - En "Saldo pendiente": reemplazar `\${{ mem.pendingBalance | number:'1.2-2' }}` por `\${{ mem.pendingBalance | moneyAmount }}`.

## Risks / Trade-offs

- **[Riesgo] Pruebas unitarias existentes que interactúan con `form.patchValue({ price: 35000 })`** → *Mitigación*: como `MonetaryInputDirective` implementa `ControlValueAccessor` y mantiene el valor del `FormControl` como `number | null`, todas las pruebas que usan `patchValue` o `setValue` con números siguen funcionando sin cambios, y además se agregan pruebas específicas para la directiva y el formateo visual del input.
- **[Riesgo] Posición del cursor al formatear puntos de miles mientras se escribe** → *Mitigación*: en `MonetaryInputDirective`, calcular la cantidad de dígitos previos al cursor antes de formatear y restaurar `selectionStart`/`selectionEnd` después de aplicar el formato en el evento `input`.
