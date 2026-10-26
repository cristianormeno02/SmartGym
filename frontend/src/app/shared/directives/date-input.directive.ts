import {
  Directive,
  ElementRef,
  HostListener,
  OnInit,
  forwardRef,
  inject
} from '@angular/core';
import {
  AbstractControl,
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator
} from '@angular/forms';
import {
  formatDateDisplay,
  parseDateInput
} from '../../core/utils/date-format';

@Directive({
  selector: 'input[appDateInput]',
  standalone: true,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => DateInputDirective),
      multi: true
    },
    {
      provide: NG_VALIDATORS,
      useExisting: forwardRef(() => DateInputDirective),
      multi: true
    }
  ]
})
export class DateInputDirective implements ControlValueAccessor, Validator, OnInit {
  private readonly el = inject(ElementRef<HTMLInputElement>);

  private onChange: (value: string | null) => void = () => {};
  private onTouched: () => void = () => {};
  private isInvalid = false;

  ngOnInit(): void {
    const input = this.el.nativeElement;
    if (!input.placeholder) {
      input.placeholder = 'DD/MM/AAAA';
    }
    input.setAttribute('inputmode', 'numeric');
    input.maxLength = 10;
  }

  writeValue(value: string | null | undefined): void {
    const input = this.el.nativeElement;
    if (!value) {
      input.value = '';
      this.isInvalid = false;
      return;
    }

    const formatted = formatDateDisplay(value);
    input.value = formatted;
    this.isInvalid = false;
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.el.nativeElement.disabled = isDisabled;
  }

  validate(control: AbstractControl): ValidationErrors | null {
    if (this.isInvalid) {
      return { invalidDate: true };
    }
    return null;
  }

  @HostListener('input')
  onInput(): void {
    const input = this.el.nativeElement;
    const raw = input.value;

    // Solo conservar dígitos
    const digits = raw.replace(/\D/g, '').slice(0, 8);

    let formatted = '';
    if (digits.length === 0) {
      formatted = '';
    } else if (digits.length <= 2) {
      formatted = digits;
      if (digits.length === 2 && raw.length >= 2) {
        formatted += '/';
      }
    } else if (digits.length <= 4) {
      formatted = `${digits.slice(0, 2)}/${digits.slice(2)}`;
      if (digits.length === 4) {
        formatted += '/';
      }
    } else {
      formatted = `${digits.slice(0, 2)}/${digits.slice(2, 4)}/${digits.slice(4)}`;
    }

    if (input.value !== formatted) {
      input.value = formatted;
    }

    if (digits.length === 0) {
      this.isInvalid = false;
      this.onChange(null);
      return;
    }

    if (digits.length === 8) {
      const parsedIso = parseDateInput(formatted);
      if (parsedIso) {
        this.isInvalid = false;
        this.onChange(parsedIso);
      } else {
        this.isInvalid = true;
        this.onChange(null);
      }
    } else {
      // Entrada incompleta mientras tipea
      this.isInvalid = false;
      this.onChange(null);
    }
  }

  @HostListener('blur')
  onBlur(): void {
    this.onTouched();
    const input = this.el.nativeElement;
    const val = input.value.trim();

    if (!val) {
      this.isInvalid = false;
      this.onChange(null);
      return;
    }

    const parsedIso = parseDateInput(val);
    if (!parsedIso) {
      this.isInvalid = true;
      this.onChange(null);
    } else {
      this.isInvalid = false;
      input.value = formatDateDisplay(parsedIso);
      this.onChange(parsedIso);
    }
  }
}
