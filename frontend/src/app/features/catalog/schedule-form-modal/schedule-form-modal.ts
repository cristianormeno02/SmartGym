import { Component, OnInit, OnChanges, Input, Output, EventEmitter, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { PeopleService } from '../../../core/services/people.service';
import { Activity, ActivityStatus, CreateRecurringScheduleRequest, RecurringSchedule, Room, UpdateRecurringScheduleRequest } from '../../../core/models/gym.models';
import { PersonStatus, PersonSummary } from '../../../core/models/person.model';
import { WEEK_DAYS, formatTimeForApi, formatTimeForInput, todayIsoDate } from '../../../core/utils/catalog-mappings';
import { PersonOption, loadSecretaryCandidates, toPersonOptions } from '../schedule-secretary-options';

function timeRangeValidator(group: AbstractControl): ValidationErrors | null {
  const start = group.get('startTime')?.value;
  const end = group.get('endTime')?.value;
  if (start && end && start >= end) {
    return { timeRangeInvalid: true };
  }
  return null;
}

function dateRangeValidator(group: AbstractControl): ValidationErrors | null {
  const from = group.get('validFrom')?.value;
  const to = group.get('validTo')?.value;
  if (from && to && to < from) {
    return { dateRangeInvalid: true };
  }
  return null;
}

@Component({
  selector: 'app-schedule-form-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm overflow-y-auto">
      <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl p-6 sm:p-8 shadow-2xl space-y-6 my-8">

        <!-- Modal Header -->
        <div class="flex items-center justify-between border-b border-slate-800 pb-4">
          <div>
            <span class="text-xs font-semibold text-cyan-400 uppercase tracking-widest">Gestión de Catálogo</span>
            <h2 class="text-2xl font-bold text-white mt-1">
              {{ isEditMode ? 'Editar Turno Recurrente' : 'Nuevo Turno Recurrente' }}
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
          <!-- Actividad -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Actividad Deportiva <span class="text-rose-400">*</span>
            </label>
            <select
              formControlName="activityId"
              (change)="onActivityChange()"
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition disabled:opacity-50 disabled:bg-slate-900"
            >
              <option value="" disabled>Seleccione una actividad...</option>
              @for (act of activities(); track act.id) {
                <option [value]="act.id">{{ act.name }} ({{ act.code }})</option>
              }
            </select>
            @if (form.get('activityId')?.touched && form.get('activityId')?.hasError('required')) {
              <span class="text-xs text-rose-400 mt-1 block">La actividad es obligatoria.</span>
            }
            @if (isEditMode) {
              <span class="text-xs text-slate-500 mt-1 block">La actividad de un turno recurrente no puede modificarse una vez programado.</span>
            }
          </div>

          <!-- Sala y Instructor Grid -->
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <!-- Sala -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Sala o Espacio Físico <span class="text-rose-400">*</span>
              </label>
              <select
                formControlName="roomId"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition"
              >
                <option value="" disabled>Seleccione una sala...</option>
                @for (room of roomOptions(); track room.id) {
                  <option [value]="room.id">{{ room.name }} (Capacidad: {{ room.capacity }}){{ room.isActive ? '' : ' — inactiva' }}</option>
                }
              </select>
              @if (form.get('roomId')?.touched && form.get('roomId')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">La sala es obligatoria.</span>
              }
            </div>

            <!-- Instructor -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Profesor / Instructor <span class="text-rose-400">*</span>
              </label>
              <select
                formControlName="instructorId"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition"
              >
                <option value="" disabled>Seleccione un instructor...</option>
                @for (inst of instructorOptions(); track inst.id) {
                  <option [value]="inst.id">{{ inst.label }}</option>
                }
              </select>
              @if (form.get('instructorId')?.touched && form.get('instructorId')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">El instructor es obligatorio.</span>
              }
            </div>

            <!-- Secretario -->
            <div class="md:col-span-2">
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Secretario (Opcional)
              </label>
              <select
                formControlName="secretaryId"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition"
              >
                <option value="">Sin secretario asignado</option>
                @for (sec of secretaryOptions(); track sec.id) {
                  <option [value]="sec.id">{{ sec.label }}</option>
                }
              </select>
            </div>
          </div>

          <!-- Día y Horarios Grid -->
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <!-- Día de la semana -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Día Semanal <span class="text-rose-400">*</span>
              </label>
              <select
                formControlName="dayOfWeek"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition"
              >
                @for (day of weekDays; track day.day) {
                  <option [value]="day.day">{{ day.name }}</option>
                }
              </select>
            </div>

            <!-- Hora inicio -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Hora Inicio <span class="text-rose-400">*</span>
              </label>
              <input
                type="time"
                formControlName="startTime"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white focus:outline-none focus:border-cyan-500 transition"
              />
              @if (form.get('startTime')?.touched && form.get('startTime')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">Hora inicio obligatoria.</span>
              }
            </div>

            <!-- Hora fin -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Hora Fin <span class="text-rose-400">*</span>
              </label>
              <input
                type="time"
                formControlName="endTime"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white focus:outline-none focus:border-cyan-500 transition"
              />
              @if (form.get('endTime')?.touched && form.get('endTime')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">Hora fin obligatoria.</span>
              }
            </div>
          </div>

          @if (form.hasError('timeRangeInvalid')) {
            <div class="p-3 bg-rose-950/40 border border-rose-800/80 rounded-xl text-xs text-rose-300 font-semibold">
              La hora de fin debe ser posterior a la hora de inicio.
            </div>
          }

          <!-- Fechas de Vigencia y Cupo Grid -->
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <!-- Vigencia Desde -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Vigencia Desde <span class="text-rose-400">*</span>
              </label>
              <input
                type="date"
                formControlName="validFrom"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white focus:outline-none focus:border-cyan-500 transition"
              />
              @if (form.get('validFrom')?.touched && form.get('validFrom')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">Fecha de inicio obligatoria.</span>
              }
            </div>

            <!-- Vigencia Hasta -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Vigencia Hasta (Opcional)
              </label>
              <input
                type="date"
                formControlName="validTo"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white focus:outline-none focus:border-cyan-500 transition"
              />
            </div>

            <!-- Cupo Máximo -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Cupo Máximo {{ isCapacityRequired() ? '*' : '(Opcional)' }}
              </label>
              <input
                type="number"
                min="1"
                formControlName="maxCapacity"
                placeholder="Heredar de actividad o fijar"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
              />
              @if (form.get('maxCapacity')?.touched && form.get('maxCapacity')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">Cupo obligatorio para esta actividad.</span>
              }
              @if (form.get('maxCapacity')?.touched && form.get('maxCapacity')?.hasError('min')) {
                <span class="text-xs text-rose-400 mt-1 block">El cupo debe ser al menos 1.</span>
              }
            </div>
          </div>

          @if (form.hasError('dateRangeInvalid')) {
            <div class="p-3 bg-rose-950/40 border border-rose-800/80 rounded-xl text-xs text-rose-300 font-semibold">
              La fecha "Vigencia Hasta" no puede ser anterior a "Vigencia Desde".
            </div>
          }

          <!-- Modal Actions -->
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
              class="px-6 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-cyan-400 hover:bg-cyan-300 disabled:opacity-50 disabled:cursor-not-allowed shadow-lg shadow-cyan-500/20 transition flex items-center gap-2"
            >
              @if (isSubmitting()) {
                <span class="inline-block animate-spin rounded-full h-3.5 w-3.5 border-b-2 border-slate-950"></span>
              }
              {{ isEditMode ? 'Actualizar Horario' : 'Crear Horario' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ScheduleFormModalComponent implements OnInit, OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly gymApi = inject(GymApiService);
  private readonly peopleService = inject(PeopleService);

  @Input() schedule: RecurringSchedule | null = null;
  @Input() preselectedActivityId: string | null = null;
  @Output() saved = new EventEmitter<RecurringSchedule>();
  @Output() cancelled = new EventEmitter<void>();

  readonly weekDays = WEEK_DAYS;
  readonly isSubmitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly isCapacityRequired = signal(false);

  readonly activities = signal<Activity[]>([]);
  readonly rooms = signal<Room[]>([]);
  readonly instructors = signal<PersonSummary[]>([]);
  readonly secretaries = signal<PersonSummary[]>([]);
  private readonly editingSchedule = signal<RecurringSchedule | null>(null);

  // Solo recursos activos, salvo los ya asignados al horario en edición (para no perder la selección actual)
  readonly roomOptions = computed(() => {
    const current = this.editingSchedule();
    return this.rooms().filter(r => r.isActive || r.id === current?.roomId);
  });

  readonly instructorOptions = computed(() => {
    const current = this.editingSchedule();
    const options = this.instructors().map(i => ({ id: i.id, label: `${i.lastName}, ${i.firstName}` }));
    if (current && !options.some(o => o.id === current.instructorId)) {
      options.push({ id: current.instructorId, label: current.instructorName });
    }
    return options;
  });

  readonly secretaryOptions = computed<PersonOption[]>(() => {
    const current = this.editingSchedule();
    const options = toPersonOptions(this.secretaries());
    if (current?.secretaryId && !options.some(o => o.id === current.secretaryId)) {
      options.push({ id: current.secretaryId, label: current.secretaryName || current.secretaryId });
    }
    return options;
  });

  form: FormGroup = this.fb.group({
    activityId: ['', [Validators.required]],
    roomId: ['', [Validators.required]],
    instructorId: ['', [Validators.required]],
    secretaryId: [''],
    dayOfWeek: [1, [Validators.required]],
    startTime: ['', [Validators.required]],
    endTime: ['', [Validators.required]],
    validFrom: ['', [Validators.required]],
    validTo: [''],
    maxCapacity: [null, [Validators.min(1)]]
  }, { validators: [timeRangeValidator, dateRangeValidator] });

  get isEditMode(): boolean {
    return !!this.schedule?.id;
  }

  ngOnInit(): void {
    this.loadSelectOptions();
    this.initForm();
  }

  ngOnChanges(): void {
    this.initForm();
  }

  private loadSelectOptions(): void {
    this.gymApi.getActivities(undefined, ActivityStatus.Active, undefined, 1, 100).subscribe({
      next: (res) => {
        this.activities.set(res.items || []);
        this.onActivityChange();
      }
    });

    this.gymApi.getRooms().subscribe({
      next: (res) => {
        this.rooms.set(res || []);
      }
    });

    this.peopleService.search({ role: 'Instructor', status: PersonStatus.Active, pageSize: 100 }).subscribe({
      next: (res) => {
        this.instructors.set(res.items || []);
      }
    });

    loadSecretaryCandidates(this.peopleService).subscribe({
      next: (people) => this.secretaries.set(people)
    });
  }

  onActivityChange(): void {
    const actId = this.form.get('activityId')?.value;
    const act = this.activities().find(a => a.id === actId);
    const capacityCtrl = this.form.get('maxCapacity');

    if (act && !act.defaultCapacity) {
      this.isCapacityRequired.set(true);
      capacityCtrl?.setValidators([Validators.required, Validators.min(1)]);
    } else {
      this.isCapacityRequired.set(false);
      capacityCtrl?.setValidators([Validators.min(1)]);
    }
    capacityCtrl?.updateValueAndValidity();
  }

  private initForm(): void {
    this.editingSchedule.set(this.schedule);
    if (this.schedule) {
      this.form.patchValue({
        activityId: this.schedule.activityId,
        roomId: this.schedule.roomId,
        instructorId: this.schedule.instructorId,
        secretaryId: this.schedule.secretaryId || '',
        dayOfWeek: this.schedule.dayOfWeek,
        startTime: formatTimeForInput(this.schedule.startTime),
        endTime: formatTimeForInput(this.schedule.endTime),
        validFrom: this.schedule.validFrom?.substring(0, 10) || '',
        validTo: this.schedule.validTo ? this.schedule.validTo.substring(0, 10) : '',
        maxCapacity: this.schedule.maxCapacity ?? null
      });
      this.form.get('activityId')?.disable();
    } else {
      this.form.reset({
        activityId: this.preselectedActivityId || '',
        secretaryId: '',
        dayOfWeek: 1,
        validFrom: todayIsoDate()
      });
      this.form.get('activityId')?.enable();
    }
    this.errorMessage.set(null);
    this.onActivityChange();
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.form.getRawValue();

    if (this.isEditMode && this.schedule?.id) {
      const updateReq: UpdateRecurringScheduleRequest = {
        roomId: val.roomId,
        instructorId: val.instructorId,
        secretaryId: val.secretaryId || null,
        dayOfWeek: Number(val.dayOfWeek),
        startTime: formatTimeForApi(val.startTime),
        endTime: formatTimeForApi(val.endTime),
        validFrom: val.validFrom,
        validTo: val.validTo || null,
        maxCapacity: val.maxCapacity ? Number(val.maxCapacity) : null,
        // La baja se hace con DELETE; GET /api/schedules solo devuelve horarios activos
        isActive: true
      };

      this.gymApi.updateSchedule(this.schedule.id, updateReq).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Error al actualizar el horario recurrente.');
        }
      });
    } else {
      const createReq: CreateRecurringScheduleRequest = {
        activityId: val.activityId,
        roomId: val.roomId,
        instructorId: val.instructorId,
        secretaryId: val.secretaryId || null,
        dayOfWeek: Number(val.dayOfWeek),
        startTime: formatTimeForApi(val.startTime),
        endTime: formatTimeForApi(val.endTime),
        validFrom: val.validFrom,
        validTo: val.validTo || null,
        maxCapacity: val.maxCapacity ? Number(val.maxCapacity) : null
      };

      this.gymApi.createSchedule(createReq).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Error al crear el horario recurrente.');
        }
      });
    }
  }
}
