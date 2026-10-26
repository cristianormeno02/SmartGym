import { Directive, ElementRef, HostListener, forwardRef, inject } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { formatMonetaryAmount, parseMonetaryAmount } from '../../core/utils/monetary-format';

@Directive({
  selector: 'input[appMonetaryInput]',
  standalone: true,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => MonetaryInputDirective),
      multi: true
    }
  ]
})
export class MonetaryInputDirective implements ControlValueAccessor {
  private readonly el = inject(ElementRef<HTMLInputElement>);

  private onChange: (value: number | null) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: number | string | null | undefined): void {
    const input = this.el.nativeElement;
    if (value === null || value === undefined || value === '') {
      input.value = '';
      return;
    }

    const formatted = formatMonetaryAmount(value);
    input.value = formatted;
  }

  registerOnChange(fn: (value: number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.el.nativeElement.disabled = isDisabled;
  }

  @HostListener('input')
  onInput(): void {
    const input = this.el.nativeElement;
    const raw = input.value;

    // Conserva solo dígitos y la primera coma decimal
    let cleaned = raw.replace(/[^0-9,]/g, '');
    const commaIndex = cleaned.indexOf(',');

    let intPart = '';
    let decPart: string | null = null;

    if (commaIndex !== -1) {
      intPart = cleaned.slice(0, commaIndex);
      decPart = cleaned.slice(commaIndex + 1).replace(/,/g, '').slice(0, 2);
    } else {
      intPart = cleaned;
    }

    // Quita ceros a la izquierda innecesarios (dejando al menos un '0' si había dígitos)
    if (intPart.length > 1) {
      intPart = intPart.replace(/^0+(?=\d)/, '');
    }

    if (!intPart && decPart === null) {
      input.value = '';
      this.onChange(null);
      return;
    }

    if (!intPart && decPart !== null) {
      intPart = '0';
    }

    const formattedInt = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    const formattedDisplay = decPart !== null ? `${formattedInt},${decPart}` : formattedInt;

    if (input.value !== formattedDisplay) {
      input.value = formattedDisplay;
    }

    const numericValue = parseMonetaryAmount(formattedDisplay);
    this.onChange(numericValue);
  }

  @HostListener('blur')
  onBlur(): void {
    this.onTouched();
    const input = this.el.nativeElement;
    const numericValue = parseMonetaryAmount(input.value);
    if (numericValue !== null) {
      input.value = formatMonetaryAmount(numericValue);
      this.onChange(numericValue);
    } else {
      input.value = '';
    }
  }
}
