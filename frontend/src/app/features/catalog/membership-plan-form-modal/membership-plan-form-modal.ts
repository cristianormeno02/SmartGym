import { Component, OnInit, OnChanges, Input, Output, EventEmitter, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { Activity, ActivityStatus, CreateMembershipPlanRequest, MembershipPlan, UpdateMembershipPlanRequest } from '../../../core/models/gym.models';
import { MonetaryInputDirective } from '../../../shared/directives/monetary-input.directive';

@Component({
  selector: 'app-membership-plan-form-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MonetaryInputDirective],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm overflow-y-auto">
      <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl p-6 sm:p-8 shadow-2xl space-y-6 my-8">

        <!-- Header -->
        <div class="flex items-center justify-between border-b border-slate-800 pb-4">
          <div>
            <span class="text-xs font-semibold text-emerald-400 uppercase tracking-widest">Gestión de Catálogo</span>
            <h2 class="text-2xl font-bold text-white mt-1">
              {{ isEditMode ? 'Editar Plan de Membresía' : 'Nuevo Plan de Membresía' }}
            </h2>
          </div>
          <button (click)="cancelled.emit()"
                  class="text-slate-400 hover:text-white transition p-2 rounded-lg hover:bg-slate-800">
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        @if (errorMessage()) {
          <div class="p-4 rounded-xl text-xs font-semibold bg-rose-950/60 border border-rose-800 text-rose-300">
            {{ errorMessage() }}
          </div>
        }

        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="space-y-4">
          <!-- Nombre -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Nombre del Plan <span class="text-rose-400">*</span>
            </label>
            <input
              type="text"
              formControlName="name"
              placeholder="ej. Pase Libre Full, Bono 10 Clases Musculación"
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition"
            />
            @if (form.get('name')?.touched && form.get('name')?.hasError('required')) {
              <span class="text-xs text-rose-400 mt-1 block">El nombre es obligatorio.</span>
            }
          </div>

          <!-- Descripción -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Descripción Comercial (Opcional)
            </label>
            <textarea
              formControlName="description"
              rows="2"
              placeholder="Detalle o condiciones visibles para los alumnos..."
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition resize-none"
            ></textarea>
          </div>

          <!-- Precio y Duración Grid -->
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <!-- Precio -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Precio ($) <span class="text-rose-400">*</span>
              </label>
              <input
                type="text"
                inputmode="decimal"
                appMonetaryInput
                formControlName="price"
                placeholder="35.000,00"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition"
              />
              @if (form.get('price')?.touched && form.get('price')?.hasError('min')) {
                <span class="text-xs text-rose-400 mt-1 block">El precio debe ser mayor a 0.</span>
              }
            </div>

            <!-- Duración en Días -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Duración (Días) <span class="text-rose-400">*</span>
              </label>
              <input
                type="number"
                min="1"
                formControlName="durationDays"
                placeholder="30"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition"
              />
              @if (form.get('durationDays')?.touched && form.get('durationDays')?.hasError('min')) {
                <span class="text-xs text-rose-400 mt-1 block">La duración debe ser de al menos 1 día.</span>
              }
            </div>
          </div>

          <!-- Tipo de Acceso / Créditos -->
          <div class="p-4 bg-slate-950/60 border border-slate-800/80 rounded-xl space-y-3">
            <div class="flex items-center justify-between">
              <div>
                <span class="text-xs font-bold text-white uppercase tracking-wider">Modalidad de Cupo</span>
                <p class="text-xs text-slate-400">Elija si es pase ilimitado o paquete con cantidad fija de créditos</p>
              </div>
              <label class="relative inline-flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  formControlName="isUnlimited"
                  (change)="onUnlimitedChange()"
                  class="sr-only peer"
                />
                <div class="w-11 h-6 bg-slate-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-emerald-500"></div>
                <span class="ml-3 text-xs font-bold" [ngClass]="form.get('isUnlimited')?.value ? 'text-emerald-400' : 'text-slate-400'">
                  {{ form.get('isUnlimited')?.value ? 'Pase Ilimitado' : 'Por Créditos' }}
                </span>
              </label>
            </div>

            @if (!form.get('isUnlimited')?.value) {
              <div>
                <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                  Cantidad de Créditos <span class="text-rose-400">*</span>
                </label>
                <input
                  type="number"
                  min="0"
                  formControlName="credits"
                  placeholder="ej. 8, 10, 12 clases"
                  class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition"
                />
                @if (form.hasError('creditsRequired')) {
                  <span class="text-xs text-rose-400 mt-1 block">Debe indicar al menos 1 crédito para planes no ilimitados.</span>
                }
              </div>
            }
          </div>

          <!-- Alcance de Actividades -->
          <div class="p-4 bg-slate-950/60 border border-slate-800/80 rounded-xl space-y-3">
            <div class="flex items-center justify-between">
              <div>
                <span class="text-xs font-bold text-white uppercase tracking-wider">Actividades Permitidas</span>
                <p class="text-xs text-slate-400">Define a qué actividades da acceso este plan</p>
              </div>
              <label class="relative inline-flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  formControlName="appliesToAllActivities"
                  (change)="onAppliesToAllChange()"
                  class="sr-only peer"
                />
                <div class="w-11 h-6 bg-slate-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-emerald-500"></div>
                <span class="ml-3 text-xs font-bold" [ngClass]="form.get('appliesToAllActivities')?.value ? 'text-emerald-400' : 'text-slate-400'">
                  {{ form.get('appliesToAllActivities')?.value ? 'Todas las Actividades' : 'Actividades Específicas' }}
                </span>
              </label>
            </div>

            @if (!form.get('appliesToAllActivities')?.value) {
              <div class="pt-2 border-t border-slate-800/60 space-y-2">
                <span class="text-xs text-slate-400 block mb-1">Seleccione las actividades habilitadas:</span>
                <div class="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-48 overflow-y-auto p-1">
                  @for (act of activities(); track act.id) {
                    <label class="flex items-center gap-2.5 p-2 rounded-lg bg-slate-900 border border-slate-800 hover:border-slate-700 cursor-pointer text-xs">
                      <input
                        type="checkbox"
                        [checked]="isActivitySelected(act.id)"
                        (change)="toggleActivity(act.id)"
                        class="rounded bg-slate-950 border-slate-700 text-emerald-500 focus:ring-0"
                      />
                      <span class="text-slate-200 font-semibold">{{ act.name }}</span>
                      <span class="text-[10px] text-slate-500 font-mono">({{ act.code }})</span>
                    </label>
                  }
                </div>
                @if (form.hasError('activitiesRequired')) {
                  <span class="text-xs text-rose-400 mt-1 block">Debe seleccionar al menos una actividad.</span>
                }
              </div>
            }
          </div>

          <!-- Pase de Clase Individual en Recepción -->
          <div class="p-4 bg-slate-950/60 border border-slate-800 rounded-xl flex items-start gap-3">
            <input
              id="isSingleClassPass"
              type="checkbox"
              formControlName="isSingleClassPass"
              class="mt-1 h-4 w-4 rounded border-slate-700 bg-slate-900 text-emerald-500 focus:ring-emerald-500 focus:ring-offset-slate-900"
            />
            <div>
              <label for="isSingleClassPass" class="text-sm font-medium text-white cursor-pointer">
                Disponible como pase de clase individual en recepción
              </label>
              <p class="text-xs text-slate-400 mt-0.5">
                Permite ofrecer este plan en el portal de mostrador para alumnos sin membresía previa o con cupo agotado. Nota: los planes con 1 crédito se ofrecen automáticamente en recepción.
              </p>
            </div>
          </div>

          <!-- Estado en edición -->
          @if (isEditMode) {
            <div class="flex items-center gap-3 pt-1">
              <label class="relative inline-flex items-center cursor-pointer">
                <input type="checkbox" formControlName="isActive" class="sr-only peer">
                <div class="w-11 h-6 bg-slate-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-emerald-500"></div>
                <span class="ml-3 text-xs font-semibold text-slate-300">
                  {{ form.get('isActive')?.value ? 'Plan Activo (visible al público)' : 'Plan Inactivo (oculto en compras)' }}
                </span>
              </label>
            </div>
          }

          <!-- Actions -->
          <div class="flex items-center justify-end gap-3 pt-4 border-t border-slate-800">
            <button
              type="button"
              (click)="cancelled.emit()"
              [disabled]="isSubmitting()"
              class="px-5 py-2.5 rounded-xl text-xs font-bold text-slate-400 hover:text-white bg-slate-800 hover:bg-slate-700 transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              [disabled]="form.invalid || isSubmitting()"
              class="px-6 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-emerald-400 hover:bg-emerald-300 disabled:opacity-50 disabled:cursor-not-allowed shadow-lg shadow-emerald-500/20 transition flex items-center gap-2"
            >
              @if (isSubmitting()) {
                <span class="inline-block animate-spin rounded-full h-3.5 w-3.5 border-b-2 border-slate-950"></span>
              }
              {{ isEditMode ? 'Actualizar Plan' : 'Crear Plan' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class MembershipPlanFormModalComponent implements OnInit, OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly gymApi = inject(GymApiService);

  @Input() plan: MembershipPlan | null = null;
  @Output() saved = new EventEmitter<MembershipPlan>();
  @Output() cancelled = new EventEmitter<void>();

  readonly isSubmitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly activities = signal<Activity[]>([]);
  readonly selectedActivityIds = signal<string[]>([]);

  form: FormGroup = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(500)]],
    durationDays: [30, [Validators.required, Validators.min(1)]],
    price: [null, [Validators.required, Validators.min(0.01)]],
    isUnlimited: [false],
    credits: [null],
    appliesToAllActivities: [true],
    allowedActivityIds: [[]],
    isActive: [true],
    isSingleClassPass: [false]
  }, {
    validators: (group: AbstractControl): ValidationErrors | null => {
      const isUnlimited = group.get('isUnlimited')?.value;
      const credits = group.get('credits')?.value;
      const appliesToAll = group.get('appliesToAllActivities')?.value;

      const errors: ValidationErrors = {};

      if (!isUnlimited && (credits === null || credits === undefined || credits < 1)) {
        errors['creditsRequired'] = true;
      }

      if (!appliesToAll && this.selectedActivityIds().length === 0) {
        errors['activitiesRequired'] = true;
      }

      return Object.keys(errors).length > 0 ? errors : null;
    }
  });

  get isEditMode(): boolean {
    return !!this.plan?.id;
  }

  ngOnInit(): void {
    this.loadActivities();
    this.initForm();
  }

  ngOnChanges(): void {
    this.initForm();
  }

  private loadActivities(): void {
    this.gymApi.getActivities(undefined, ActivityStatus.Active, undefined, 1, 100).subscribe({
      next: (res) => this.activities.set(res.items || [])
    });
  }

  onUnlimitedChange(): void {
    if (this.form.get('isUnlimited')?.value) {
      this.form.get('credits')?.setValue(0);
    }
    this.form.updateValueAndValidity();
  }

  onAppliesToAllChange(): void {
    if (this.form.get('appliesToAllActivities')?.value) {
      this.selectedActivityIds.set([]);
    }
    this.form.updateValueAndValidity();
  }

  isActivitySelected(id: string): boolean {
    return this.selectedActivityIds().includes(id);
  }

  toggleActivity(id: string): void {
    this.selectedActivityIds.update(current => {
      if (current.includes(id)) {
        return current.filter(x => x !== id);
      } else {
        return [...current, id];
      }
    });
    this.form.updateValueAndValidity();
  }

  private initForm(): void {
    if (this.plan) {
      this.selectedActivityIds.set(this.plan.allowedActivityIds || []);
      this.form.patchValue({
        name: this.plan.name,
        description: this.plan.description || '',
        durationDays: this.plan.durationDays,
        price: this.plan.price,
        isUnlimited: this.plan.isUnlimited,
        credits: this.plan.credits,
        appliesToAllActivities: this.plan.appliesToAllActivities,
        isActive: this.plan.isActive,
        isSingleClassPass: !!this.plan.isSingleClassPass
      });
    } else {
      this.selectedActivityIds.set([]);
      this.form.reset({
        name: '',
        description: '',
        durationDays: 30,
        price: null,
        isUnlimited: false,
        credits: null,
        appliesToAllActivities: true,
        isActive: true,
        isSingleClassPass: false
      });
    }
    this.errorMessage.set(null);
    this.form.updateValueAndValidity();
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.form.getRawValue();
    const allowedIds = val.appliesToAllActivities ? [] : this.selectedActivityIds();
    const creditsValue = val.isUnlimited ? 0 : Number(val.credits);

    if (this.isEditMode && this.plan?.id) {
      const updateReq: UpdateMembershipPlanRequest = {
        name: val.name.trim(),
        description: val.description?.trim() || undefined,
        durationDays: Number(val.durationDays),
        price: Number(val.price),
        credits: creditsValue,
        isUnlimited: !!val.isUnlimited,
        appliesToAllActivities: !!val.appliesToAllActivities,
        allowedActivityIds: allowedIds,
        isActive: !!val.isActive,
        isSingleClassPass: !!val.isSingleClassPass
      };

      this.gymApi.updateMembershipPlan(this.plan.id, updateReq).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Error al actualizar el plan.');
        }
      });
    } else {
      const createReq: CreateMembershipPlanRequest = {
        name: val.name.trim(),
        description: val.description?.trim() || undefined,
        durationDays: Number(val.durationDays),
        price: Number(val.price),
        credits: creditsValue,
        isUnlimited: !!val.isUnlimited,
        appliesToAllActivities: !!val.appliesToAllActivities,
        allowedActivityIds: allowedIds,
        isSingleClassPass: !!val.isSingleClassPass
      };

      this.gymApi.createMembershipPlan(createReq).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Error al crear el plan.');
        }
      });
    }
  }
}
