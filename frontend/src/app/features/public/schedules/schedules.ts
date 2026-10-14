import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { ScheduleFormModalComponent } from '../../catalog/schedule-form-modal/schedule-form-modal';
import { WEEK_DAYS } from '../../../core/utils/catalog-mappings';

@Component({
  selector: 'app-schedules',
  standalone: true,
  imports: [CommonModule, ScheduleFormModalComponent],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-12 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-8">
        <!-- Header -->
        <div class="flex flex-col md:flex-row md:items-center md:justify-between gap-6 pb-6 border-b border-slate-800">
          <div>
            <h1 class="text-4xl font-black text-white">Grilla Semanal de Clases</h1>
            <p class="text-slate-400 mt-2 text-base">Consulta los días, turnos y docentes de cada actividad deportiva.</p>
          </div>

          @if (authService.isStaff()) {
            <button
              (click)="openCreateModal()"
              class="btn-new-schedule px-5 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-cyan-400 hover:bg-cyan-300 shadow-lg shadow-cyan-500/20 transition flex items-center gap-2 self-start md:self-auto"
            >
              <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
              </svg>
              + Nuevo Horario
            </button>
          }
        </div>

        <!-- Day Selector Tabs -->
        <div class="flex flex-wrap justify-center gap-2">
          @for (day of daysOfWeek; track day.value) {
            <button
              (click)="selectedDay.set(day.value)"
              [class.bg-cyan-500]="selectedDay() === day.value"
              [class.text-slate-950]="selectedDay() === day.value"
              [class.font-bold]="selectedDay() === day.value"
              [class.bg-slate-900]="selectedDay() !== day.value"
              [class.text-slate-400]="selectedDay() !== day.value"
              class="px-4 py-2 rounded-xl text-sm border border-slate-800 transition hover:text-white"
            >
              {{ day.label }}
            </button>
          }
        </div>

        <!-- Schedules Grid -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          @for (item of filteredSchedules(); track item.id) {
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 hover:border-cyan-500/40 transition flex flex-col justify-between">
              <div>
                <div class="flex justify-between items-start mb-4">
                  <div>
                    <h3 class="text-xl font-bold text-white">{{ item.activityName }}</h3>
                    <span class="text-xs text-cyan-400 font-medium">{{ item.roomName }}</span>
                  </div>
                  <div class="text-right">
                    <span class="text-lg font-black text-white">{{ item.startTime.substring(0, 5) }}</span>
                    <span class="text-xs text-slate-500 block">a {{ item.endTime.substring(0, 5) }}</span>
                  </div>
                </div>

                <div class="pt-4 border-t border-slate-800/80 flex justify-between items-center text-xs text-slate-400">
                  <div class="flex items-center gap-1.5">
                    <span class="w-2 h-2 rounded-full bg-emerald-400"></span>
                    <span>Prof. {{ item.instructorName }}</span>
                  </div>
                  <span class="font-medium">Cupo: {{ item.maxCapacity || 30 }}</span>
                </div>
              </div>

              <!-- Staff Actions -->
              @if (authService.isStaff()) {
                <div class="pt-4 mt-4 border-t border-slate-800/80 flex items-center justify-end gap-2">
                  <button
                    (click)="openEditModal(item)"
                    class="btn-edit-schedule px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 transition"
                  >
                    Editar
                  </button>
                  <button
                    (click)="requestDelete(item)"
                    class="btn-delete-schedule px-3 py-1.5 rounded-lg text-xs font-semibold bg-rose-950/40 hover:bg-rose-900/60 text-rose-300 border border-rose-800/50 transition"
                  >
                    Dar de Baja
                  </button>
                </div>
              }
            </div>
          } @empty {
            <div class="col-span-full text-center py-12 bg-slate-900/30 rounded-2xl border border-slate-800/60 text-slate-500">
              No hay clases programadas para este día de la semana.
            </div>
          }
        </div>
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
              <strong class="text-white">{{ scheduleToDelete()?.activityName }}</strong>.
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

      <!-- Modal -->
      @if (isModalOpen()) {
        <!-- Carga diferida: el formulario solo lo usa el staff y no debe pesar en el bundle inicial -->
        @defer (on immediate) {
          <app-schedule-form-modal
            [schedule]="selectedSchedule()"
            (saved)="onScheduleSaved()"
            (cancelled)="isModalOpen.set(false)"
          ></app-schedule-form-modal>
        }
      }
    </div>
  `
})
export class SchedulesComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);
  private readonly route = inject(ActivatedRoute);
  readonly authService = inject(AuthService);

  readonly schedules = signal<RecurringSchedule[]>([]);
  readonly selectedDay = signal<number>(1); // Lunes por defecto
  readonly isModalOpen = signal(false);
  readonly selectedSchedule = signal<RecurringSchedule | null>(null);
  readonly scheduleToDelete = signal<RecurringSchedule | null>(null);

  readonly daysOfWeek = [
    { value: 1, label: 'Lunes' },
    { value: 2, label: 'Martes' },
    { value: 3, label: 'Miércoles' },
    { value: 4, label: 'Jueves' },
    { value: 5, label: 'Viernes' },
    { value: 6, label: 'Sábado' },
    { value: 0, label: 'Domingo' }
  ];

  ngOnInit(): void {
    this.loadSchedules();
  }

  loadSchedules(): void {
    this.route.queryParams.subscribe(params => {
      const activityId = params['activityId'];
      this.gymApi.getSchedules(activityId).subscribe({
        next: (items) => this.schedules.set(items || []),
        error: () => this.schedules.set([])
      });
    });
  }

  filteredSchedules(): RecurringSchedule[] {
    return this.schedules().filter(s => s.dayOfWeek === this.selectedDay() && s.isActive);
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
    this.loadSchedules();
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
        this.loadSchedules();
      },
      error: () => {
        this.scheduleToDelete.set(null);
      }
    });
  }
}
