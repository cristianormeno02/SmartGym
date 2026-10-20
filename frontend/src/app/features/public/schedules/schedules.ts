import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { ScheduleFormModalComponent } from '../../catalog/schedule-form-modal/schedule-form-modal';
import { ScheduleBatchModalComponent } from '../../catalog/schedule-batch-modal/schedule-batch-modal';
import { ScheduleCalendarComponent } from '../../catalog/schedule-calendar/schedule-calendar';
import { ScheduleDetailModalComponent } from '../../catalog/schedule-detail-modal/schedule-detail-modal';

@Component({
  selector: 'app-schedules',
  standalone: true,
  imports: [
    CommonModule,
    ScheduleFormModalComponent,
    ScheduleBatchModalComponent,
    ScheduleCalendarComponent,
    ScheduleDetailModalComponent
  ],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-12 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-8">
        <!-- Header -->
        <div class="flex flex-col md:flex-row md:items-center md:justify-between gap-6 pb-6 border-b border-slate-800">
          <div>
            <h1 class="text-4xl font-black text-white">Grilla Semanal de Clases</h1>
            <p class="text-slate-400 mt-2 text-base">Consulta los días, turnos y docentes de cada actividad deportiva.</p>
          </div>

          <div class="flex flex-wrap items-center gap-3 self-start md:self-auto">
            <!-- View switcher -->
            <div class="flex items-center bg-slate-900 p-1 rounded-xl border border-slate-800">
              <button
                type="button"
                (click)="viewMode.set('calendar')"
                [class.bg-slate-800]="viewMode() === 'calendar'"
                [class.text-white]="viewMode() === 'calendar'"
                [class.text-slate-400]="viewMode() !== 'calendar'"
                class="btn-view-calendar px-3 py-1.5 rounded-lg text-xs font-semibold transition flex items-center gap-1.5"
              >
                <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                </svg>
                Calendario
              </button>
              <button
                type="button"
                (click)="viewMode.set('cards')"
                [class.bg-slate-800]="viewMode() === 'cards'"
                [class.text-white]="viewMode() === 'cards'"
                [class.text-slate-400]="viewMode() !== 'cards'"
                class="btn-view-cards px-3 py-1.5 rounded-lg text-xs font-semibold transition flex items-center gap-1.5"
              >
                <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2V6zM14 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2v-2zM14 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2v-2z" />
                </svg>
                Tarjetas
              </button>
            </div>

            @if (authService.isStaff()) {
              <div class="flex items-center gap-3">
                <button
                  (click)="openBatchModal()"
                  class="btn-batch-schedule px-4 py-2.5 rounded-xl text-xs font-bold text-cyan-400 bg-cyan-950/60 border border-cyan-800/80 hover:bg-cyan-900/60 transition flex items-center gap-2"
                >
                  <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                  </svg>
                  Carga Masiva
                </button>
                <button
                  (click)="openCreateModal()"
                  class="btn-new-schedule px-5 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-cyan-400 hover:bg-cyan-300 shadow-lg shadow-cyan-500/20 transition flex items-center gap-2"
                >
                  <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
                  </svg>
                  + Nuevo Horario
                </button>
              </div>
            }
          </div>
        </div>

        <!-- Multi-Activity & Multi-Room Filter Toolbar -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-4 space-y-3">
          <div class="flex flex-col md:flex-row md:items-center justify-between gap-3">
            <div class="flex flex-wrap items-center gap-2">
              <span class="text-xs font-semibold uppercase tracking-wider text-slate-400 mr-1">Actividades:</span>
              <button
                type="button"
                (click)="selectAllActivities()"
                [class.bg-cyan-500]="isAllActivitiesSelected()"
                [class.text-slate-950]="isAllActivitiesSelected()"
                [class.font-bold]="isAllActivitiesSelected()"
                [class.bg-slate-950]="!isAllActivitiesSelected()"
                [class.text-slate-300]="!isAllActivitiesSelected()"
                class="chip-all-activities px-3 py-1.5 rounded-xl text-xs border border-slate-800 transition hover:border-slate-700"
              >
                Todas las actividades
              </button>
              @for (act of uniqueActivities(); track act.id) {
                <button
                  type="button"
                  (click)="toggleActivityFilter(act.id)"
                  [class.bg-cyan-500]="isActivitySelected(act.id)"
                  [class.text-slate-950]="isActivitySelected(act.id)"
                  [class.font-bold]="isActivitySelected(act.id)"
                  [class.bg-slate-950]="!isActivitySelected(act.id)"
                  [class.text-slate-300]="!isActivitySelected(act.id)"
                  class="chip-activity px-3 py-1.5 rounded-xl text-xs border border-slate-800 transition hover:border-slate-700 inline-flex items-center gap-1.5"
                >
                  <span class="w-2.5 h-2.5 rounded-full shrink-0 border border-white/20" [style.backgroundColor]="act.colorHex"></span>
                  {{ act.name }}
                </button>
              }
            </div>

            <div class="text-xs text-slate-400 font-medium shrink-0">
              {{ filteredSchedules().length }} horarios visibles
            </div>
          </div>

          <div class="flex flex-wrap items-center gap-2 pt-2 border-t border-slate-800/80">
            <span class="text-xs font-semibold uppercase tracking-wider text-slate-400 mr-1">Salas:</span>
            <button
              type="button"
              (click)="selectAllRooms()"
              [class.bg-cyan-500]="isAllRoomsSelected()"
              [class.text-slate-950]="isAllRoomsSelected()"
              [class.font-bold]="isAllRoomsSelected()"
              [class.bg-slate-950]="!isAllRoomsSelected()"
              [class.text-slate-300]="!isAllRoomsSelected()"
              class="chip-all-rooms px-3 py-1.5 rounded-xl text-xs border border-slate-800 transition hover:border-slate-700"
            >
              Todas las salas
            </button>
            @for (room of uniqueRooms(); track room.id) {
              <button
                type="button"
                (click)="toggleRoomFilter(room.id)"
                [class.bg-cyan-500]="isRoomSelected(room.id)"
                [class.text-slate-950]="isRoomSelected(room.id)"
                [class.font-bold]="isRoomSelected(room.id)"
                [class.bg-slate-950]="!isRoomSelected(room.id)"
                [class.text-slate-300]="!isRoomSelected(room.id)"
                class="chip-room px-3 py-1.5 rounded-xl text-xs border border-slate-800 transition hover:border-slate-700"
              >
                {{ room.name }}
              </button>
            }
          </div>
        </div>

        <!-- Main View: Calendar vs Cards -->
        @if (viewMode() === 'calendar') {
          <app-schedule-calendar
            [schedules]="filteredSchedules()"
            [canManage]="authService.isStaff()"
            (scheduleSelected)="onScheduleSelectedFromCalendar($event)"
          ></app-schedule-calendar>
        } @else {
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

          <!-- Schedules Grid (Cards) -->
          <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            @for (item of displayedSchedules(); track item.id) {
              <div
                class="bg-slate-900 border border-slate-800 rounded-2xl p-6 hover:border-cyan-500/40 transition flex flex-col justify-between"
                [style.borderLeftWidth]="'4px'"
                [style.borderLeftColor]="item.activityColorHex || '#06B6D4'"
              >
                <div>
                  <div class="flex justify-between items-start mb-4">
                    <div>
                      <div class="flex items-center gap-2">
                        <span class="w-2.5 h-2.5 rounded-full shrink-0" [style.backgroundColor]="item.activityColorHex || '#06B6D4'"></span>
                        <h3 class="text-xl font-bold text-white">{{ item.activityName }}</h3>
                      </div>
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
                No hay clases programadas para este día con el filtro seleccionado.
              </div>
            }
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

      <!-- Form Modal (Create/Edit) -->
      @if (isModalOpen()) {
        @defer (on immediate) {
          <app-schedule-form-modal
            [schedule]="selectedSchedule()"
            (saved)="onScheduleSaved()"
            (cancelled)="isModalOpen.set(false)"
          ></app-schedule-form-modal>
        }
      }

      <!-- Batch Schedule Modal -->
      @if (isBatchModalOpen()) {
        @defer (on immediate) {
          <app-schedule-batch-modal
            (saved)="onBatchSchedulesSaved($event)"
            (cancelled)="isBatchModalOpen.set(false)"
          ></app-schedule-batch-modal>
        }
      }

      <!-- Schedule Detail Modal (from calendar click) -->
      @if (selectedDetailSchedule()) {
        <app-schedule-detail-modal
          [schedule]="selectedDetailSchedule()"
          [canManage]="authService.isStaff()"
          (edit)="onEditFromDetail($event)"
          (delete)="onDeleteFromDetail($event)"
          (closed)="selectedDetailSchedule.set(null)"
        ></app-schedule-detail-modal>
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
  readonly viewMode = signal<'calendar' | 'cards'>('calendar');
  readonly selectedActivityIds = signal<Set<string>>(new Set());
  readonly selectedRoomIds = signal<Set<string>>(new Set());

  readonly isModalOpen = signal(false);
  readonly isBatchModalOpen = signal(false);
  readonly selectedSchedule = signal<RecurringSchedule | null>(null);
  readonly selectedDetailSchedule = signal<RecurringSchedule | null>(null);
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

  readonly uniqueActivities = computed(() => {
    const map = new Map<string, { id: string; name: string; colorHex: string }>();
    for (const s of this.schedules()) {
      if (!map.has(s.activityId)) {
        map.set(s.activityId, {
          id: s.activityId,
          name: s.activityName,
          colorHex: s.activityColorHex || '#06B6D4'
        });
      }
    }
    return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly uniqueRooms = computed(() => {
    const map = new Map<string, { id: string; name: string }>();
    for (const s of this.schedules()) {
      if (!map.has(s.roomId)) {
        map.set(s.roomId, { id: s.roomId, name: s.roomName });
      }
    }
    return Array.from(map.values()).sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly isAllActivitiesSelected = computed(() => this.selectedActivityIds().size === 0);
  readonly isAllRoomsSelected = computed(() => this.selectedRoomIds().size === 0);

  readonly filteredSchedules = computed(() => {
    const selectedActivities = this.selectedActivityIds();
    const selectedRooms = this.selectedRoomIds();
    return this.schedules().filter(s =>
      (selectedActivities.size === 0 || selectedActivities.has(s.activityId)) &&
      (selectedRooms.size === 0 || selectedRooms.has(s.roomId))
    );
  });

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

  isActivitySelected(id: string): boolean {
    return this.selectedActivityIds().has(id);
  }

  selectAllActivities(): void {
    this.selectedActivityIds.set(new Set());
  }

  toggleActivityFilter(id: string): void {
    const current = new Set(this.selectedActivityIds());
    if (current.has(id)) {
      current.delete(id);
    } else {
      current.add(id);
    }
    this.selectedActivityIds.set(current);
  }

  isRoomSelected(id: string): boolean {
    return this.selectedRoomIds().has(id);
  }

  selectAllRooms(): void {
    this.selectedRoomIds.set(new Set());
  }

  toggleRoomFilter(id: string): void {
    const current = new Set(this.selectedRoomIds());
    if (current.has(id)) {
      current.delete(id);
    } else {
      current.add(id);
    }
    this.selectedRoomIds.set(current);
  }

  displayedSchedules(): RecurringSchedule[] {
    return this.filteredSchedules().filter(s => s.dayOfWeek === this.selectedDay() && s.isActive);
  }

  openCreateModal(): void {
    this.selectedSchedule.set(null);
    this.isModalOpen.set(true);
  }

  openBatchModal(): void {
    this.isBatchModalOpen.set(true);
  }

  openEditModal(schedule: RecurringSchedule): void {
    this.selectedSchedule.set(schedule);
    this.isModalOpen.set(true);
  }

  onScheduleSelectedFromCalendar(schedule: RecurringSchedule): void {
    this.selectedDetailSchedule.set(schedule);
  }

  onEditFromDetail(schedule: RecurringSchedule): void {
    this.selectedDetailSchedule.set(null);
    this.openEditModal(schedule);
  }

  onDeleteFromDetail(schedule: RecurringSchedule): void {
    this.selectedDetailSchedule.set(null);
    this.requestDelete(schedule);
  }

  onScheduleSaved(): void {
    this.isModalOpen.set(false);
    this.selectedSchedule.set(null);
    this.loadSchedules();
  }

  onBatchSchedulesSaved(created: RecurringSchedule[]): void {
    this.isBatchModalOpen.set(false);
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
