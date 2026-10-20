import { Component, OnInit, Input, Output, EventEmitter, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, FormArray, ReactiveFormsModule, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { PeopleService } from '../../../core/services/people.service';
import {
  Activity,
  ActivityStatus,
  BatchScheduleSlot,
  CreateBatchRecurringScheduleRequest,
  RecurringSchedule,
  Room,
  ScheduleConflict,
  ScheduleConflictKind
} from '../../../core/models/gym.models';
import { PersonStatus, PersonSummary } from '../../../core/models/person.model';
import { WEEK_DAYS, formatTimeForApi, formatTimeForInput, todayIsoDate, getDayOfWeekName } from '../../../core/utils/catalog-mappings';
import { loadSecretaryCandidates, toPersonOptions } from '../schedule-secretary-options';

export interface PreviewSlotItem {
  dayOfWeek: number;
  dayName: string;
  startTime: string;
  endTime: string;
  conflict?: ScheduleConflict;
}

function timeToMinutes(time: string): number {
  if (!time) return 0;
  const [h, m] = time.split(':').map(Number);
  return (h || 0) * 60 + (m || 0);
}

function minutesToTime(mins: number): string {
  const norm = ((mins % 1440) + 1440) % 1440;
  const h = Math.floor(norm / 60);
  const m = norm % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

function slotGroupValidator(group: AbstractControl): ValidationErrors | null {
  const errors: ValidationErrors = {};
  const days = group.get('days')?.value;
  if (!Array.isArray(days) || days.length === 0) {
    errors['noDaysSelected'] = true;
  }
  const start = group.get('startTime')?.value;
  const end = group.get('endTime')?.value;
  if (start && end && start >= end) {
    errors['timeRangeInvalid'] = true;
  }
  return Object.keys(errors).length > 0 ? errors : null;
}

function dateRangeValidator(group: AbstractControl): ValidationErrors | null {
  const from = group.get('validFrom')?.value;
  const to = group.get('validTo')?.value;
  if (from && to && to < from) {
    return { dateRangeInvalid: true };
  }
  return null;
}

function batchFormValidator(control: AbstractControl): ValidationErrors | null {
  const slotsArray = control.get('slots') as FormArray;
  if (!slotsArray) return null;

  let totalSlots = 0;
  const dayIntervals = new Map<number, { start: number; end: number }[]>();

  for (let i = 0; i < slotsArray.length; i++) {
    const group = slotsArray.at(i);
    const days: number[] = group.get('days')?.value || [];
    const startTime: string = group.get('startTime')?.value;
    const endTime: string = group.get('endTime')?.value;

    totalSlots += days.length;

    if (startTime && endTime && startTime < endTime) {
      const startMin = timeToMinutes(startTime);
      const endMin = timeToMinutes(endTime);

      for (const day of days) {
        const intervals = dayIntervals.get(day) || [];
        for (const interval of intervals) {
          if (startMin < interval.end && endMin > interval.start) {
            return { slotOverlap: true, day };
          }
        }
        intervals.push({ start: startMin, end: endMin });
        dayIntervals.set(day, intervals);
      }
    }
  }

  if (totalSlots > 50) {
    return { maxSlotsExceeded: true, count: totalSlots };
  }

  return null;
}

@Component({
  selector: 'app-schedule-batch-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm overflow-y-auto">
      <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-4xl p-6 sm:p-8 shadow-2xl space-y-6 my-8">

        <!-- Modal Header -->
        <div class="flex items-center justify-between border-b border-slate-800 pb-4">
          <div>
            <div class="flex items-center gap-2">
              <span class="text-xs font-semibold text-cyan-400 uppercase tracking-widest">Gestión de Catálogo</span>
              <span class="px-2 py-0.5 rounded text-[10px] font-bold bg-cyan-950 text-cyan-400 border border-cyan-800">Carga Masiva</span>
            </div>
            <h2 class="text-2xl font-bold text-white mt-1">
              Programar Múltiples Horarios
            </h2>
            <p class="text-xs text-slate-400 mt-0.5">
              Configura una sala e instructor y define una o varias franjas horarias semanales.
            </p>
          </div>
          <button (click)="onCancel()"
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

        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="space-y-6">
          <!-- Actividad, Sala, Instructor y Secretario Grid -->
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <!-- Actividad -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Actividad <span class="text-rose-400">*</span>
              </label>
              <select
                formControlName="activityId"
                (change)="onActivityChange()"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition text-sm"
              >
                <option value="" disabled>Seleccione una actividad...</option>
                @for (act of activities(); track act.id) {
                  <option [value]="act.id">{{ act.name }} ({{ act.code }})</option>
                }
              </select>
              @if (form.get('activityId')?.touched && form.get('activityId')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">La actividad es obligatoria.</span>
              }
            </div>

            <!-- Sala -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Sala <span class="text-rose-400">*</span>
              </label>
              <select
                formControlName="roomId"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition text-sm"
              >
                <option value="" disabled>Seleccione una sala...</option>
                @for (room of roomOptions(); track room.id) {
                  <option [value]="room.id">{{ room.name }} (Capacidad: {{ room.capacity }})</option>
                }
              </select>
              @if (form.get('roomId')?.touched && form.get('roomId')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">La sala es obligatoria.</span>
              }
            </div>

            <!-- Instructor -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Instructor <span class="text-rose-400">*</span>
              </label>
              <select
                formControlName="instructorId"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition text-sm"
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
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Secretario (Opcional)
              </label>
              <select
                formControlName="secretaryId"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 transition text-sm"
              >
                <option value="">Sin secretario asignado</option>
                @for (sec of secretaryOptions(); track sec.id) {
                  <option [value]="sec.id">{{ sec.label }}</option>
                }
              </select>
            </div>
          </div>

          <!-- Vigencia y Cupo Grid -->
          <div class="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Vigencia Desde <span class="text-rose-400">*</span>
              </label>
              <input
                type="date"
                formControlName="validFrom"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white focus:outline-none focus:border-cyan-500 transition text-sm"
              />
              @if (form.get('validFrom')?.touched && form.get('validFrom')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">Fecha de inicio obligatoria.</span>
              }
            </div>

            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Vigencia Hasta (Opcional)
              </label>
              <input
                type="date"
                formControlName="validTo"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white focus:outline-none focus:border-cyan-500 transition text-sm"
              />
            </div>

            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Cupo Máximo {{ isCapacityRequired() ? '*' : '(Opcional)' }}
              </label>
              <input
                type="number"
                min="1"
                formControlName="maxCapacity"
                placeholder="Heredar de actividad o fijar"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition text-sm"
              />
              @if (form.get('maxCapacity')?.touched && form.get('maxCapacity')?.hasError('required')) {
                <span class="text-xs text-rose-400 mt-1 block">Cupo obligatorio para esta actividad.</span>
              }
            </div>
          </div>

          @if (form.hasError('dateRangeInvalid')) {
            <div class="p-3 bg-rose-950/40 border border-rose-800/80 rounded-xl text-xs text-rose-300 font-semibold">
              La fecha "Vigencia Hasta" no puede ser anterior a "Vigencia Desde".
            </div>
          }

          <!-- Franjas Horarias (FormArray) -->
          <div class="space-y-4 pt-2 border-t border-slate-800">
            <div class="flex items-center justify-between">
              <div>
                <h3 class="text-sm font-bold text-white uppercase tracking-wider">Franjas Horarias Semanales</h3>
                <p class="text-xs text-slate-400">Selecciona los días y el rango horario para cada grupo de clases.</p>
              </div>
              <button
                type="button"
                (click)="addSlotGroup()"
                class="px-3 py-1.5 rounded-xl text-xs font-semibold text-cyan-400 bg-cyan-950/50 border border-cyan-800/60 hover:bg-cyan-900/60 transition flex items-center gap-1.5"
              >
                <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
                </svg>
                Agregar Franja
              </button>
            </div>

            <div formArrayName="slots" class="space-y-3">
              @for (group of slotGroups.controls; track $index; let i = $index) {
                <div [formGroupName]="i" class="p-4 bg-slate-950/60 border border-slate-800 rounded-xl space-y-3 relative">
                  <div class="flex items-center justify-between">
                    <span class="text-xs font-bold text-slate-400 uppercase tracking-wider">
                      Franja #{{ i + 1 }}
                    </span>
                    @if (slotGroups.length > 1) {
                      <button
                        type="button"
                        (click)="removeSlotGroup(i)"
                        class="text-xs text-rose-400 hover:text-rose-300 flex items-center gap-1 transition"
                      >
                        <svg class="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                        Quitar
                      </button>
                    }
                  </div>

                  <!-- Días de la semana pills -->
                  <div>
                    <label class="block text-[11px] font-semibold uppercase tracking-wider text-slate-400 mb-1.5">
                      Días aplicables <span class="text-rose-400">*</span>
                    </label>
                    <div class="flex flex-wrap gap-1.5">
                      @for (day of weekDays; track day.day) {
                        <button
                          type="button"
                          (click)="toggleDay(i, day.day)"
                          [class.bg-cyan-500]="isDaySelected(i, day.day)"
                          [class.text-slate-950]="isDaySelected(i, day.day)"
                          [class.font-bold]="isDaySelected(i, day.day)"
                          [class.bg-slate-900]="!isDaySelected(i, day.day)"
                          [class.text-slate-300]="!isDaySelected(i, day.day)"
                          [class.border-slate-800]="!isDaySelected(i, day.day)"
                          class="px-2.5 py-1 rounded-lg text-xs border transition hover:border-cyan-500/50"
                        >
                          {{ day.name }}
                        </button>
                      }
                    </div>
                    @if (group.get('days')?.touched && group.hasError('noDaysSelected')) {
                      <span class="text-xs text-rose-400 mt-1 block">Debes seleccionar al menos un día.</span>
                    }
                  </div>

                  <!-- Horas inicio y fin -->
                  <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div>
                      <label class="block text-[11px] font-semibold uppercase tracking-wider text-slate-400 mb-1">
                        Hora Inicio <span class="text-rose-400">*</span>
                      </label>
                      <input
                        type="time"
                        formControlName="startTime"
                        (change)="onStartTimeChanged(i)"
                        class="w-full bg-slate-900 border border-slate-800 rounded-xl px-3 py-1.5 text-white text-sm focus:outline-none focus:border-cyan-500 transition"
                      />
                    </div>
                    <div>
                      <label class="block text-[11px] font-semibold uppercase tracking-wider text-slate-400 mb-1">
                        Hora Fin <span class="text-rose-400">*</span>
                      </label>
                      <input
                        type="time"
                        formControlName="endTime"
                        class="w-full bg-slate-900 border border-slate-800 rounded-xl px-3 py-1.5 text-white text-sm focus:outline-none focus:border-cyan-500 transition"
                      />
                    </div>
                  </div>

                  @if (group.hasError('timeRangeInvalid')) {
                    <div class="p-2.5 bg-rose-950/40 border border-rose-800/80 rounded-lg text-xs text-rose-300 font-semibold">
                      La hora de fin debe ser posterior a la de inicio.
                    </div>
                  }
                </div>
              }
            </div>
          </div>

          @if (form.hasError('slotOverlap')) {
            <div class="p-3 bg-rose-950/40 border border-rose-800/80 rounded-xl text-xs text-rose-300 font-semibold">
              Existen franjas horarias superpuestas para el mismo día de la semana dentro de este lote.
            </div>
          }

          @if (form.hasError('maxSlotsExceeded')) {
            <div class="p-3 bg-rose-950/40 border border-rose-800/80 rounded-xl text-xs text-rose-300 font-semibold">
              El total de horarios a generar supera el máximo permitido de 50 turnos.
            </div>
          }

          <!-- Previsualización -->
          <div class="pt-2 border-t border-slate-800 space-y-2">
            <div class="flex items-center justify-between">
              <span class="text-xs font-bold uppercase tracking-wider text-slate-300">
                Previsualización de Turnos
              </span>
              <span class="text-xs font-semibold px-2.5 py-0.5 rounded-full"
                    [class.bg-cyan-950]="totalSlotsCount() > 0 && totalSlotsCount() <= 50"
                    [class.text-cyan-400]="totalSlotsCount() > 0 && totalSlotsCount() <= 50"
                    [class.bg-rose-950]="totalSlotsCount() > 50"
                    [class.text-rose-400]="totalSlotsCount() > 50"
                    [class.bg-slate-800]="totalSlotsCount() === 0"
                    [class.text-slate-400]="totalSlotsCount() === 0">
                Se crearán {{ totalSlotsCount() }} horarios
              </span>
            </div>

            @if (previewSlots().length === 0) {
              <p class="text-xs text-slate-500 italic py-2">
                Selecciona al menos un día y horario en las franjas anteriores para previsualizar los turnos.
              </p>
            } @else {
              <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-2 max-h-48 overflow-y-auto pr-1">
                @for (item of previewSlots(); track item.dayOfWeek + '-' + item.startTime) {
                  <div class="p-2.5 rounded-lg border text-xs transition flex flex-col justify-between"
                       [class.bg-rose-950/40]="item.conflict"
                       [class.border-rose-700]="item.conflict"
                       [class.bg-slate-950]="!item.conflict"
                       [class.border-slate-800]="!item.conflict">
                    <div class="flex items-center justify-between">
                      <span class="font-bold text-white">{{ item.dayName }}</span>
                      <span class="text-cyan-400 font-mono">{{ item.startTime }} - {{ item.endTime }}</span>
                    </div>

                    @if (item.conflict) {
                      <div class="mt-2 text-[11px] text-rose-300 space-y-0.5 bg-rose-950/60 p-1.5 rounded border border-rose-800">
                        <div class="font-bold flex items-center gap-1 text-rose-200">
                          <svg class="w-3.5 h-3.5 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fill-rule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clip-rule="evenodd"/>
                          </svg>
                          Conflicto: {{ getConflictLabel(item.conflict.kind) }}
                        </div>
                        @if (item.conflict.conflictingActivityName) {
                          <div class="truncate">
                            {{ item.conflict.conflictingActivityName }} ({{ item.conflict.conflictingStartTime?.substring(0, 5) }} - {{ item.conflict.conflictingEndTime?.substring(0, 5) }})
                          </div>
                        }
                      </div>
                    }
                  </div>
                }
              </div>
            }
          </div>

          <!-- Modal Actions -->
          <div class="flex items-center justify-end gap-3 pt-4 border-t border-slate-800">
            <button
              type="button"
              (click)="onCancel()"
              [disabled]="isSubmitting()"
              class="px-5 py-2.5 rounded-xl text-xs font-bold text-slate-400 hover:text-white bg-slate-800 hover:bg-slate-700 transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              [disabled]="form.invalid || totalSlotsCount() === 0 || isSubmitting()"
              class="px-6 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-cyan-400 hover:bg-cyan-300 disabled:opacity-50 disabled:cursor-not-allowed shadow-lg shadow-cyan-500/20 transition flex items-center gap-2"
            >
              @if (isSubmitting()) {
                <span class="inline-block animate-spin rounded-full h-3.5 w-3.5 border-b-2 border-slate-950"></span>
              }
              Crear {{ totalSlotsCount() }} Horarios
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class ScheduleBatchModalComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly gymApi = inject(GymApiService);
  private readonly peopleService = inject(PeopleService);

  @Input() preselectedActivityId: string | null = null;
  @Output() saved = new EventEmitter<RecurringSchedule[]>();
  @Output() cancelled = new EventEmitter<void>();

  readonly weekDays = WEEK_DAYS;
  readonly isSubmitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly isCapacityRequired = signal(false);
  readonly conflicts = signal<ScheduleConflict[]>([]);

  readonly activities = signal<Activity[]>([]);
  readonly rooms = signal<Room[]>([]);
  readonly instructors = signal<PersonSummary[]>([]);
  readonly secretaries = signal<PersonSummary[]>([]);

  readonly roomOptions = computed(() => {
    return this.rooms().filter(r => r.isActive);
  });

  readonly instructorOptions = computed(() => {
    return this.instructors().map(i => ({ id: i.id, label: `${i.lastName}, ${i.firstName}` }));
  });

  readonly secretaryOptions = computed(() => toPersonOptions(this.secretaries()));

  readonly formRevision = signal(0);

  form: FormGroup = this.fb.group({
    activityId: ['', [Validators.required]],
    roomId: ['', [Validators.required]],
    instructorId: ['', [Validators.required]],
    secretaryId: [''],
    validFrom: [todayIsoDate(), [Validators.required]],
    validTo: [''],
    maxCapacity: [null, [Validators.min(1)]],
    slots: this.fb.array([])
  }, { validators: [dateRangeValidator, batchFormValidator] });

  get slotGroups(): FormArray {
    return this.form.get('slots') as FormArray;
  }

  // Previsualización reactiva
  readonly previewSlots = computed<PreviewSlotItem[]>(() => {
    this.formRevision();
    const activeConflicts = this.conflicts();
    const result: PreviewSlotItem[] = [];

    for (let i = 0; i < this.slotGroups.length; i++) {
      const group = this.slotGroups.at(i);
      const days = (group.get('days')?.value || []) as number[];
      const startTime = (group.get('startTime')?.value || '') as string;
      const endTime = (group.get('endTime')?.value || '') as string;

      if (!startTime || !endTime) continue;

      for (const day of days) {
        const conflict = activeConflicts.find(c => {
          const cStart = c.startTime.substring(0, 5);
          return c.dayOfWeek === day && cStart === startTime;
        });

        result.push({
          dayOfWeek: day,
          dayName: getDayOfWeekName(day),
          startTime,
          endTime,
          conflict
        });
      }
    }

    // Ordenar por día (Lunes 1 ... Sábado 6, Domingo 0) y luego por startTime
    const dayOrder = (d: number) => (d === 0 ? 7 : d);
    result.sort((a, b) => {
      const dayDiff = dayOrder(a.dayOfWeek) - dayOrder(b.dayOfWeek);
      if (dayDiff !== 0) return dayDiff;
      return a.startTime.localeCompare(b.startTime);
    });

    return result;
  });

  readonly totalSlotsCount = computed(() => this.previewSlots().length);

  ngOnInit(): void {
    this.loadSelectOptions();
    if (this.slotGroups.length === 0) {
      this.addSlotGroup();
    }
    if (this.preselectedActivityId) {
      this.form.patchValue({ activityId: this.preselectedActivityId });
    }
    this.form.valueChanges.subscribe(() => {
      this.formRevision.update(r => r + 1);
    });
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

  addSlotGroup(): void {
    const group = this.fb.group({
      days: [[]],
      startTime: ['', [Validators.required]],
      endTime: ['', [Validators.required]]
    }, { validators: [slotGroupValidator] });

    group.valueChanges.subscribe(() => {
      this.formRevision.update(r => r + 1);
      this.form.updateValueAndValidity({ emitEvent: false });
    });

    this.slotGroups.push(group);
    this.formRevision.update(r => r + 1);
    this.form.updateValueAndValidity({ emitEvent: false });
  }

  removeSlotGroup(index: number): void {
    if (this.slotGroups.length > 1) {
      this.slotGroups.removeAt(index);
      this.formRevision.update(r => r + 1);
      this.form.updateValueAndValidity({ emitEvent: false });
    }
  }

  toggleDay(groupIndex: number, day: number): void {
    const group = this.slotGroups.at(groupIndex);
    const currentDays: number[] = group.get('days')?.value || [];
    const idx = currentDays.indexOf(day);
    let newDays: number[];
    if (idx >= 0) {
      newDays = currentDays.filter(d => d !== day);
    } else {
      newDays = [...currentDays, day];
    }
    group.get('days')?.setValue(newDays);
    group.get('days')?.markAsTouched();
    this.formRevision.update(r => r + 1);
    this.form.updateValueAndValidity();
  }

  isDaySelected(groupIndex: number, day: number): boolean {
    const group = this.slotGroups.at(groupIndex);
    const currentDays: number[] = group?.get('days')?.value || [];
    return currentDays.includes(day);
  }

  onStartTimeChanged(groupIndex: number): void {
    const group = this.slotGroups.at(groupIndex);
    const start = group.get('startTime')?.value;
    if (!start) return;

    let durationMins = 60;
    if (groupIndex > 0) {
      const prevGroup = this.slotGroups.at(groupIndex - 1);
      const prevStart = prevGroup.get('startTime')?.value;
      const prevEnd = prevGroup.get('endTime')?.value;
      if (prevStart && prevEnd) {
        const diff = timeToMinutes(prevEnd) - timeToMinutes(prevStart);
        if (diff > 0) {
          durationMins = diff;
        }
      }
    }

    const startMins = timeToMinutes(start);
    const newEnd = minutesToTime(startMins + durationMins);
    group.get('endTime')?.setValue(newEnd);
    group.get('endTime')?.markAsTouched();
    group.updateValueAndValidity();
    this.form.updateValueAndValidity();
  }

  getConflictLabel(kind: ScheduleConflictKind): string {
    switch (kind) {
      case ScheduleConflictKind.Room:
        return 'Sala ocupada';
      case ScheduleConflictKind.Instructor:
        return 'Profesor superpuesto';
      case ScheduleConflictKind.WithinBatch:
        return 'Superposición en lote';
      default:
        return 'Conflicto';
    }
  }

  onCancel(): void {
    this.cancelled.emit();
  }

  onSubmit(): void {
    if (this.form.invalid || this.totalSlotsCount() === 0) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.conflicts.set([]);

    const val = this.form.getRawValue();
    const rawSlots = val.slots as { days: number[]; startTime: string; endTime: string }[];

    const batchSlots: BatchScheduleSlot[] = rawSlots.map(s => ({
      daysOfWeek: s.days,
      startTime: formatTimeForApi(s.startTime),
      endTime: formatTimeForApi(s.endTime)
    }));

    const request: CreateBatchRecurringScheduleRequest = {
      activityId: val.activityId,
      roomId: val.roomId,
      instructorId: val.instructorId,
      secretaryId: val.secretaryId || null,
      validFrom: val.validFrom,
      validTo: val.validTo || null,
      maxCapacity: val.maxCapacity ? Number(val.maxCapacity) : null,
      slots: batchSlots
    };

    this.gymApi.createBatchSchedules(request).subscribe({
      next: (created) => {
        this.isSubmitting.set(false);
        this.saved.emit(created);
      },
      error: (err) => {
        this.isSubmitting.set(false);
        const conflictsList = err.error?.conflicts || [];
        this.conflicts.set(conflictsList);
        this.errorMessage.set(
          err.error?.message || 'Se produjeron errores o conflictos al registrar el lote de horarios.'
        );
      }
    });
  }
}
