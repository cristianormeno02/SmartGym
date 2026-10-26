import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { getDayOfWeekName } from '../../../core/utils/catalog-mappings';
import { AppDatePipe } from '../../../shared/pipes/app-date.pipe';

@Component({
  selector: 'app-schedule-detail-modal',
  standalone: true,
  imports: [CommonModule, AppDatePipe],
  template: `
    @if (schedule) {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm overflow-y-auto">
        <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg p-6 sm:p-8 shadow-2xl space-y-6 my-8">

          <!-- Header -->
          <div class="flex items-center justify-between border-b border-slate-800 pb-4">
            <div>
              <span class="text-xs font-semibold text-cyan-400 uppercase tracking-widest">
                Detalle del Turno Recurrente
              </span>
              <h2 class="text-2xl font-bold text-white mt-1">
                {{ schedule.activityName }}
              </h2>
            </div>
            <button
              type="button"
              (click)="closed.emit()"
              class="btn-close-modal text-slate-400 hover:text-white transition p-2 rounded-lg hover:bg-slate-800"
            >
              <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>

          <!-- Body Info Grid -->
          <div class="space-y-4 text-sm">
            <!-- Horario y Día -->
            <div class="p-4 bg-slate-950/60 border border-slate-800/80 rounded-xl flex items-center justify-between">
              <div>
                <span class="text-xs text-slate-400 uppercase tracking-wider block font-medium">Día Semanal</span>
                <span class="text-base font-bold text-white">{{ dayName }}</span>
              </div>
              <div class="text-right">
                <span class="text-xs text-slate-400 uppercase tracking-wider block font-medium">Horario</span>
                <span class="text-base font-black font-mono text-cyan-400">
                  {{ schedule.startTime.substring(0, 5) }} - {{ schedule.endTime.substring(0, 5) }}
                </span>
              </div>
            </div>

            <!-- Sala y Docente -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div class="p-3.5 bg-slate-950/40 border border-slate-800/60 rounded-xl">
                <span class="text-xs text-slate-400 uppercase tracking-wider block font-medium">Sala / Espacio</span>
                <span class="text-sm font-semibold text-white mt-0.5 block">{{ schedule.roomName }}</span>
              </div>

              <div class="p-3.5 bg-slate-950/40 border border-slate-800/60 rounded-xl">
                <span class="text-xs text-slate-400 uppercase tracking-wider block font-medium">Docente / Instructor</span>
                <span class="text-sm font-semibold text-white mt-0.5 block">{{ schedule.instructorName }}</span>
              </div>

              @if (schedule.secretaryName) {
                <div class="detail-secretary p-3.5 bg-slate-950/40 border border-slate-800/60 rounded-xl sm:col-span-2">
                  <span class="text-xs text-slate-400 uppercase tracking-wider block font-medium">Secretario</span>
                  <span class="text-sm font-semibold text-white mt-0.5 block">{{ schedule.secretaryName }}</span>
                </div>
              }
            </div>

            <!-- Cupo y Vigencia -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div class="p-3.5 bg-slate-950/40 border border-slate-800/60 rounded-xl">
                <span class="text-xs text-slate-400 uppercase tracking-wider block font-medium">Cupo Máximo</span>
                <span class="text-sm font-semibold text-white mt-0.5 block">
                  {{ schedule.maxCapacity ? schedule.maxCapacity + ' participantes' : 'Heredado de la actividad' }}
                </span>
              </div>

              <div class="p-3.5 bg-slate-950/40 border border-slate-800/60 rounded-xl">
                <span class="text-xs text-slate-400 uppercase tracking-wider block font-medium">Estado</span>
                <span
                  class="inline-block mt-1 px-2.5 py-0.5 rounded text-[11px] font-bold uppercase tracking-wider"
                  [ngClass]="schedule.isActive ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : 'bg-slate-800 text-slate-400 border border-slate-700'"
                >
                  {{ schedule.isActive ? 'Activo' : 'Inactivo' }}
                </span>
              </div>
            </div>

            <!-- Periodo de Vigencia -->
            <div class="p-3.5 bg-slate-950/40 border border-slate-800/60 rounded-xl text-xs text-slate-400">
              <span class="uppercase tracking-wider font-semibold block text-slate-300 mb-1">Periodo de Vigencia</span>
              <span>
                Desde <strong>{{ schedule.validFrom | appDate }}</strong>
                {{ schedule.validTo ? ' hasta ' + (schedule.validTo | appDate) : ' (vigencia indefinida)' }}
              </span>
            </div>
          </div>

          <!-- Actions -->
          <div class="flex items-center justify-between pt-4 border-t border-slate-800">
            <div>
              @if (canManageSignal()) {
                <button
                  type="button"
                  (click)="delete.emit(schedule)"
                  class="btn-delete-detail px-4 py-2 rounded-xl text-xs font-bold text-rose-400 bg-rose-950/40 border border-rose-800/60 hover:bg-rose-900/60 transition"
                >
                  Dar de Baja
                </button>
              }
            </div>

            <div class="flex items-center gap-2">
              <button
                type="button"
                (click)="closed.emit()"
                class="btn-close-modal px-4 py-2 rounded-xl text-xs font-bold text-slate-400 hover:text-white bg-slate-800 hover:bg-slate-700 transition"
              >
                Cerrar
              </button>
              @if (canManageSignal()) {
                <button
                  type="button"
                  (click)="edit.emit(schedule)"
                  class="btn-edit-detail px-5 py-2 rounded-xl text-xs font-bold text-slate-950 bg-cyan-400 hover:bg-cyan-300 shadow-lg shadow-cyan-500/20 transition"
                >
                  Editar Horario
                </button>
              }
            </div>
          </div>

        </div>
      </div>
    }
  `
})
export class ScheduleDetailModalComponent {
  readonly scheduleSignal = signal<RecurringSchedule | null>(null);
  readonly canManageSignal = signal<boolean>(false);

  @Input()
  set schedule(val: RecurringSchedule | null) {
    this.scheduleSignal.set(val);
  }
  get schedule(): RecurringSchedule | null {
    return this.scheduleSignal();
  }

  @Input()
  set canManage(val: boolean) {
    this.canManageSignal.set(val);
  }
  get canManage(): boolean {
    return this.canManageSignal();
  }

  @Output() edit = new EventEmitter<RecurringSchedule>();
  @Output() delete = new EventEmitter<RecurringSchedule>();
  @Output() closed = new EventEmitter<void>();

  get dayName(): string {
    const s = this.scheduleSignal();
    return s ? getDayOfWeekName(s.dayOfWeek) : '';
  }
}
