import { Component, Input, Output, EventEmitter, OnInit, OnChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { Activity, CreateActivityRequest, UpdateActivityRequest } from '../../../core/models/gym.models';

export const ageRangeValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const minAge = control.get('minAge')?.value;
  const maxAge = control.get('maxAge')?.value;
  if (minAge !== null && minAge !== undefined && maxAge !== null && maxAge !== undefined && minAge !== '' && maxAge !== '') {
    if (Number(minAge) > Number(maxAge)) {
      return { ageRangeInvalid: true };
    }
  }
  return null;
};

@Component({
  selector: 'app-activity-form-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm animate-fadeIn">
      <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl shadow-2xl overflow-hidden flex flex-col max-h-[90vh]">
        <!-- Modal Header -->
        <div class="p-6 border-b border-slate-800 flex justify-between items-center">
          <div>
            <h3 class="text-xl font-bold text-white">
              {{ isEditMode ? 'Editar Actividad' : 'Nueva Actividad' }}
            </h3>
            <p class="text-xs text-slate-400 mt-1">Configuración del catálogo maestro y metadatos de la disciplina</p>
          </div>
          <button (click)="cancelled.emit()" class="text-slate-400 hover:text-white text-2xl leading-none">&times;</button>
        </div>

        <!-- Form Body -->
        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="p-6 space-y-4 overflow-y-auto flex-1">
          @if (errorMessage()) {
            <div class="bg-rose-500/10 border border-rose-500/30 text-rose-400 px-4 py-3 rounded-xl text-sm">
              {{ errorMessage() }}
            </div>
          }

          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <!-- Code -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Código Canónico * {{ isEditMode ? '(Inmutable)' : '' }}
              </label>
              <input
                type="text"
                formControlName="code"
                (input)="onCodeInput()"
                placeholder="ej. CROSSFIT, SPINNING"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 disabled:opacity-50 transition"
              />
              @if (form.get('code')?.touched && form.get('code')?.errors) {
                <span class="text-xs text-rose-400 mt-1 block">
                  El código debe tener entre 3 y 50 letras mayúsculas, dígitos o guión bajo.
                </span>
              }
            </div>

            <!-- Name -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Nombre Descriptivo *
              </label>
              <input
                type="text"
                formControlName="name"
                placeholder="ej. CrossFit WOD"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
              />
              @if (form.get('name')?.touched && form.get('name')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">El nombre es obligatorio.</span>
              }
            </div>
          </div>

          <!-- Short Description -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Resumen Breve (máx. 250 caracteres)
            </label>
            <input
              type="text"
              formControlName="shortDescription"
              placeholder="Breve reseña para listados rápidos"
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
            />
          </div>

          <!-- Description -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Descripción Detallada
            </label>
            <textarea
              formControlName="description"
              rows="3"
              placeholder="Detalle completo de los objetivos y dinámica de la clase..."
              class="w-full bg-slate-950 border border-slate-800 rounded-xl p-4 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition resize-none"
            ></textarea>
          </div>

          <!-- Equipment Notes -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Notas de Equipamiento Recomendado
            </label>
            <input
              type="text"
              formControlName="equipmentNotes"
              placeholder="ej. Zapatillas deportivas, toalla obligatoria, hidratación"
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
            />
          </div>

          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <!-- Color Hex -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Color Identificatorio
              </label>
              <div class="flex items-center gap-2">
                <input
                  type="color"
                  [value]="form.get('colorHex')?.value || '#06B6D4'"
                  (input)="onColorPickerChange($event)"
                  class="w-10 h-10 rounded-lg cursor-pointer bg-transparent border-0"
                />
                <input
                  type="text"
                  formControlName="colorHex"
                  placeholder="#06B6D4"
                  class="flex-1 bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 uppercase transition"
                />
              </div>
              @if (form.get('colorHex')?.touched && form.get('colorHex')?.hasError('pattern')) {
                <span class="text-xs text-rose-400 mt-1 block">Formato inválido (debe ser #RRGGBB).</span>
              }
            </div>

            <!-- Default Capacity -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Cupo Sugerido
              </label>
              <input
                type="number"
                formControlName="defaultCapacity"
                min="1"
                placeholder="20"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
              />
              @if (form.get('defaultCapacity')?.touched && form.get('defaultCapacity')?.hasError('min')) {
                <span class="text-xs text-rose-400 mt-1 block">Debe ser al menos 1.</span>
              }
            </div>

            <!-- Age Range -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Rango Etario (Min - Max)
              </label>
              <div class="flex items-center gap-2">
                <input
                  type="number"
                  formControlName="minAge"
                  min="0"
                  placeholder="Min"
                  class="w-1/2 bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
                />
                <span class="text-slate-500">-</span>
                <input
                  type="number"
                  formControlName="maxAge"
                  min="0"
                  placeholder="Max"
                  class="w-1/2 bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
                />
              </div>
              @if (form.hasError('ageRangeInvalid')) {
                <span class="text-xs text-rose-400 mt-1 block">Edad mínima no puede superar a la máxima.</span>
              }
            </div>
          </div>

          <!-- Requires Booking Checkbox -->
          <div class="p-4 bg-slate-950/60 border border-slate-800 rounded-xl flex items-start gap-3">
            <input
              id="requiresBooking"
              type="checkbox"
              formControlName="requiresBooking"
              class="mt-1 h-4 w-4 rounded border-slate-700 bg-slate-900 text-cyan-600 focus:ring-cyan-500 focus:ring-offset-slate-900"
            />
            <div>
              <label for="requiresBooking" class="text-sm font-medium text-white cursor-pointer">
                ¿Requiere reserva previa online?
              </label>
              <p class="text-xs text-slate-400 mt-0.5">
                Si está desmarcado, la actividad funciona como acceso libre/mostrador (Drop-in). Los alumnos no reservarán por el portal; su asistencia se registrará directamente en recepción debitando crédito o cobrando el pase.
              </p>
            </div>
          </div>

          <!-- Actions -->
          <div class="flex justify-end gap-3 pt-4 border-t border-slate-800">
            <button
              type="button"
              (click)="cancelled.emit()"
              class="px-5 py-2.5 rounded-xl border border-slate-700 text-slate-300 hover:text-white hover:bg-slate-800 text-sm font-semibold transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              [disabled]="form.invalid || isSubmitting()"
              class="px-5 py-2.5 rounded-xl bg-cyan-500 hover:bg-cyan-400 disabled:opacity-50 text-slate-950 font-bold text-sm transition flex items-center gap-2"
            >
              @if (isSubmitting()) {
                <span class="w-4 h-4 border-2 border-slate-950 border-t-transparent rounded-full animate-spin"></span>
              }
              {{ isEditMode ? 'Guardar Cambios' : 'Crear Actividad' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ActivityFormModalComponent implements OnInit, OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly gymApi = inject(GymApiService);

  @Input() activity: Activity | null = null;
  @Output() saved = new EventEmitter<Activity>();
  @Output() cancelled = new EventEmitter<void>();

  readonly isSubmitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  form: FormGroup = this.fb.group({
    code: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(50), Validators.pattern('^[A-Z0-9_]{3,50}$')]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
    shortDescription: ['', [Validators.maxLength(250)]],
    description: ['', [Validators.maxLength(2000)]],
    equipmentNotes: ['', [Validators.maxLength(500)]],
    colorHex: ['', [Validators.pattern('^#[0-9A-Fa-f]{6}$')]],
    defaultCapacity: [null, [Validators.min(1)]],
    minAge: [null, [Validators.min(0)]],
    maxAge: [null, [Validators.min(0)]],
    requiresBooking: [true]
  }, { validators: ageRangeValidator });

  get isEditMode(): boolean {
    return !!this.activity?.id;
  }

  ngOnInit(): void {
    this.initForm();
  }

  ngOnChanges(): void {
    this.initForm();
  }

  onCodeInput(): void {
    const codeCtrl = this.form.get('code');
    if (codeCtrl && codeCtrl.value) {
      codeCtrl.setValue(codeCtrl.value.trim().toUpperCase(), { emitEvent: false });
    }
  }

  onColorPickerChange(event: Event): void {
    const target = event.target as HTMLInputElement;
    if (target) {
      this.form.get('colorHex')?.setValue(target.value.toUpperCase());
    }
  }

  private initForm(clearError = true): void {
    if (this.activity) {
      this.form.patchValue({
        code: this.activity.code,
        name: this.activity.name,
        shortDescription: this.activity.shortDescription || '',
        description: this.activity.description || '',
        equipmentNotes: this.activity.equipmentNotes || '',
        colorHex: this.activity.colorHex || '',
        defaultCapacity: this.activity.defaultCapacity ?? null,
        minAge: this.activity.minAge ?? null,
        maxAge: this.activity.maxAge ?? null,
        requiresBooking: this.activity.requiresBooking ?? true
      });
      this.form.get('code')?.disable();
    } else {
      this.form.reset({ requiresBooking: true });
      this.form.get('code')?.enable();
    }
    if (clearError) {
      this.errorMessage.set(null);
    }
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    if (this.isEditMode && (this.activity?.version === undefined || this.activity?.version === null)) {
      this.errorMessage.set('No se pudo obtener la versión vigente de la actividad. Cierre y vuelva a abrir el formulario.');
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.form.getRawValue();

    if (this.isEditMode && this.activity?.id) {
      const updateReq: UpdateActivityRequest = {
        name: val.name.trim(),
        shortDescription: val.shortDescription?.trim() || undefined,
        description: val.description?.trim() || undefined,
        equipmentNotes: val.equipmentNotes?.trim() || undefined,
        colorHex: val.colorHex?.trim()?.toUpperCase() || undefined,
        defaultCapacity: val.defaultCapacity ? Number(val.defaultCapacity) : undefined,
        minAge: val.minAge !== null && val.minAge !== '' ? Number(val.minAge) : undefined,
        maxAge: val.maxAge !== null && val.maxAge !== '' ? Number(val.maxAge) : undefined,
        requiresBooking: val.requiresBooking !== undefined ? !!val.requiresBooking : true,
        version: this.activity.version!
      };

      this.gymApi.updateActivity(this.activity.id, updateReq).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          if (err.status === 409) {
            this.errorMessage.set('La actividad fue modificada por otro usuario. Se actualizaron los datos más recientes; por favor revise antes de reintentar.');
            // Reload fresh detail
            this.gymApi.getActivity(this.activity!.id).subscribe({
              next: (fresh) => {
                this.activity = fresh;
                this.initForm(false);
              }
            });
          } else {
            this.errorMessage.set(err.error?.message || 'Error al actualizar la actividad.');
          }
        }
      });
    } else {
      const createReq: CreateActivityRequest = {
        code: val.code.trim().toUpperCase(),
        name: val.name.trim(),
        shortDescription: val.shortDescription?.trim() || undefined,
        description: val.description?.trim() || undefined,
        equipmentNotes: val.equipmentNotes?.trim() || undefined,
        colorHex: val.colorHex?.trim()?.toUpperCase() || undefined,
        defaultCapacity: val.defaultCapacity ? Number(val.defaultCapacity) : undefined,
        minAge: val.minAge !== null && val.minAge !== '' ? Number(val.minAge) : undefined,
        maxAge: val.maxAge !== null && val.maxAge !== '' ? Number(val.maxAge) : undefined,
        requiresBooking: val.requiresBooking !== undefined ? !!val.requiresBooking : true
      };

      this.gymApi.createActivity(createReq).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Error al crear la actividad.');
        }
      });
    }
  }
}
