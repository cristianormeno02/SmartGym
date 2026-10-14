import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GymApiService } from '../../../core/services/gym-api.service';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { ScheduleFormModalComponent } from '../schedule-form-modal/schedule-form-modal';
import { WEEK_DAYS } from '../../../core/utils/catalog-mappings';

@Component({
  selector: 'app-schedules-management',
  standalone: true,
  imports: [CommonModule, ScheduleFormModalComponent],
  template: `
    <div class="space-y-6">
      <!-- Header bar -->
      <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <div class="flex items-center gap-3">
            <span class="h-3 w-3 rounded-full bg-cyan-400 animate-pulse"></span>
            <span class="text-xs uppercase tracking-widest font-semibold text-cyan-400">Administración de Catálogo</span>
          </div>
          <h2 class="text-2xl font-black text-white mt-1">Grilla Semanal de Horarios</h2>
          <p class="text-xs text-slate-400">Configure los turnos recurrentes semanales vinculando actividades, salas e instructores</p>
        </div>

        <div class="flex items-center gap-4">
          <button
            (click)="openCreateModal()"
            class="px-5 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-cyan-400 hover:bg-cyan-300 shadow-lg shadow-cyan-500/20 transition flex items-center gap-2"
          >
            <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
            </svg>
            Nuevo Horario
          </button>
        </div>
      </div>

      <!-- Feedback message -->
      @if (feedbackMessage()) {
        <div class="p-4 rounded-xl text-xs font-semibold bg-emerald-950/60 border border-emerald-800 text-emerald-300">
          {{ feedbackMessage() }}
        </div>
      }
      @if (errorMessage()) {
        <div class="p-4 rounded-xl text-xs font-semibold bg-rose-950/60 border border-rose-800 text-rose-300">
          {{ errorMessage() }}
        </div>
      }

      <!-- Days of Week Tabs -->
      <div class="flex flex-wrap gap-2">
        @for (day of weekDays; track day.day) {
          <button
            (click)="selectedDay.set(day.day)"
            class="px-4 py-2 rounded-xl text-xs font-bold transition flex items-center gap-2"
            [ngClass]="selectedDay() === day.day ? 'bg-cyan-500 text-slate-950' : 'bg-slate-900 border border-slate-800 text-slate-400 hover:text-white'"
          >
            {{ day.name }}
            <span
              class="px-1.5 py-0.5 rounded-full text-[10px]"
              [ngClass]="selectedDay() === day.day ? 'bg-slate-900/30 text-slate-950 font-black' : 'bg-slate-800 text-slate-400'"
            >
              {{ countByDay(day.day) }}
            </span>
          </button>
        }
      </div>

      <!-- Schedules Grid -->
      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        @for (item of displayedSchedules(); track item.id) {
          <div
            class="bg-slate-900 border rounded-2xl p-6 transition flex flex-col justify-between"
            [ngClass]="item.isActive ? 'border-slate-800 hover:border-cyan-500/40' : 'border-slate-800/50 opacity-60 bg-slate-900/40'"
          >
            <div>
              <div class="flex justify-between items-start mb-3">
                <div>
                  <h3 class="text-lg font-bold text-white">{{ item.activityName }}</h3>
                  <span class="text-xs text-cyan-400 font-semibold">{{ item.roomName }}</span>
                </div>
                <div class="text-right">
                  <span class="text-lg font-black text-white">{{ item.startTime.substring(0, 5) }}</span>
                  <span class="text-xs text-slate-400 block">a {{ item.endTime.substring(0, 5) }}</span>
                </div>
              </div>

              <div class="space-y-1 text-xs text-slate-400 py-2">
                <p class="flex items-center gap-1.5">
                  <span class="w-2 h-2 rounded-full" [ngClass]="item.isActive ? 'bg-emerald-400' : 'bg-slate-500'"></span>
                  <span>Prof. <strong class="text-slate-300">{{ item.instructorName }}</strong></span>
                </p>
                <p>Cupo: <strong class="text-slate-300">{{ item.maxCapacity ? item.maxCapacity + ' alumnos' : 'Por actividad' }}</strong></p>
                <p>Vigencia: {{ item.validFrom.substring(0, 10) }}{{ item.validTo ? ' al ' + item.validTo.substring(0, 10) : ' (indefinida)' }}</p>
                <div>
                  <span
                    class="inline-block px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider"
                    [ngClass]="item.isActive ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : 'bg-slate-800 text-slate-400 border border-slate-700'"
                  >
                    {{ item.isActive ? 'Activo' : 'Inactivo' }}
                  </span>
                </div>
              </div>
            </div>

            <!-- Actions -->
            <div class="pt-4 mt-4 border-t border-slate-800/80 flex items-center justify-end gap-2">
              <button
                (click)="openEditModal(item)"
                class="px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 transition"
              >
                Editar
              </button>
              <button
                (click)="requestDelete(item)"
                class="px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-950/40 hover:bg-rose-900/60 text-rose-300 border border-rose-800/50 transition"
              >
                Dar de Baja
              </button>
            </div>
          </div>
        } @empty {
          <div class="col-span-full text-center py-12 bg-slate-900/30 rounded-2xl border border-slate-800/60 text-slate-500">
            No hay horarios registrados para este día.
          </div>
        }
      </div>

      <!-- Delete Confirmation Dialog -->
      @if (scheduleToDelete()) {
        <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div class="bg-slate-900 border border-rose-900/50 rounded-2xl max-w-md w-full p-6 space-y-4 shadow-2xl">
            <div class="flex items-center gap-3 text-rose-400">
              <svg class="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
              <h3 class="text-lg font-bold text-white">¿Dar de baja horario recurrente?</h3>
            </div>
            <p class="text-xs text-slate-300 leading-relaxed">
              Está a punto de eliminar el turno de
              <strong class="text-white">{{ scheduleToDelete()?.activityName }}</strong> los días
              <strong class="text-white">{{ scheduleToDelete() ? getDayName(scheduleToDelete()!.dayOfWeek) : '' }}</strong> de
              <strong class="text-white">{{ scheduleToDelete()?.startTime?.substring(0, 5) }}</strong> a
              <strong class="text-white">{{ scheduleToDelete()?.endTime?.substring(0, 5) }}</strong>.
            </p>
            <div class="p-3 bg-rose-950/50 border border-rose-800 rounded-xl text-xs text-rose-300">
              ⚠️ <strong>Advertencia:</strong> Esta acción cancelará las clases programadas futuras asociadas a este turno recurrente.
            </div>
            <div class="flex items-center justify-end gap-3 pt-2">
              <button
                (click)="cancelDelete()"
                class="px-4 py-2 rounded-xl text-xs font-bold text-slate-400 hover:text-white bg-slate-800 hover:bg-slate-700 transition"
              >
                Cancelar
              </button>
              <button
                (click)="confirmDelete()"
                class="px-4 py-2 rounded-xl text-xs font-bold text-white bg-rose-600 hover:bg-rose-500 transition shadow-lg shadow-rose-600/30"
              >
                Confirmar Baja
              </button>
            </div>
          </div>
        </div>
      }

      <!-- Schedule Modal -->
      @if (isModalOpen()) {
        <app-schedule-form-modal
          [schedule]="selectedSchedule()"
          (saved)="onScheduleSaved()"
          (cancelled)="isModalOpen.set(false)"
        ></app-schedule-form-modal>
      }
    </div>
  `
})
export class SchedulesManagementComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);

  readonly weekDays = WEEK_DAYS;
  readonly schedules = signal<RecurringSchedule[]>([]);
  readonly selectedDay = signal<number>(1); // Lunes por defecto

  readonly isModalOpen = signal(false);
  readonly selectedSchedule = signal<RecurringSchedule | null>(null);
  readonly scheduleToDelete = signal<RecurringSchedule | null>(null);

  readonly feedbackMessage = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.loadSchedules();
  }

  loadSchedules(): void {
    this.gymApi.getSchedules().subscribe({
      next: (data) => this.schedules.set(data || []),
      error: () => this.schedules.set([])
    });
  }

  countByDay(day: number): number {
    return this.schedules().filter(s => s.dayOfWeek === day).length;
  }

  displayedSchedules(): RecurringSchedule[] {
    // GET /api/schedules solo devuelve horarios activos
    return this.schedules().filter(s => s.dayOfWeek === this.selectedDay());
  }

  getDayName(day: number): string {
    const found = this.weekDays.find(d => d.day === day);
    return found ? found.name : '';
  }

  openCreateModal(): void {
    this.selectedSchedule.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(schedule: RecurringSchedule): void {
    this.selectedSchedule.set(schedule);
    this.isModalOpen.set(true);
  }

  onScheduleSaved(): void {
    this.isModalOpen.set(false);
    this.selectedSchedule.set(null);
    this.feedbackMessage.set('Horario guardado correctamente.');
    this.loadSchedules();
    setTimeout(() => this.feedbackMessage.set(null), 4000);
  }

  requestDelete(schedule: RecurringSchedule): void {
    this.scheduleToDelete.set(schedule);
  }

  cancelDelete(): void {
    this.scheduleToDelete.set(null);
  }

  confirmDelete(): void {
    const toDelete = this.scheduleToDelete();
    if (!toDelete) return;

    this.gymApi.deleteSchedule(toDelete.id).subscribe({
      next: () => {
        this.scheduleToDelete.set(null);
        this.feedbackMessage.set('El horario fue eliminado exitosamente.');
        this.loadSchedules();
        setTimeout(() => this.feedbackMessage.set(null), 4000);
      },
      error: (err) => {
        this.scheduleToDelete.set(null);
        this.errorMessage.set(err.error?.message || 'Error al eliminar el horario.');
        setTimeout(() => this.errorMessage.set(null), 5000);
      }
    });
  }
}
