import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { Activity, ActivityStatus, PagedResult } from '../../../core/models/gym.models';
import { ActivityFormModalComponent } from '../activity-form-modal/activity-form-modal';
import { ActivityStatusActionsComponent } from '../activity-status-actions/activity-status-actions';

@Component({
  selector: 'app-activities-management',
  standalone: true,
  imports: [CommonModule, FormsModule, ActivityFormModalComponent, ActivityStatusActionsComponent],
  template: `
    <div class="space-y-6 animate-fadeIn">
      <!-- Section Header -->
      <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <h2 class="text-2xl font-bold text-white flex items-center gap-2">
            <span>🏋️‍♂️</span> Catálogo de Actividades
          </h2>
          <p class="text-sm text-slate-400 mt-1">
            Administra disciplinas deportivas, cupos sugeridos, restricciones etarias y estados operativos
          </p>
        </div>
        <button
          (click)="openCreateModal()"
          class="px-5 py-2.5 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold text-sm transition flex items-center gap-2 self-start md:self-auto shadow-lg shadow-cyan-500/20"
        >
          <span>+</span> Nueva Actividad
        </button>
      </div>

      <!-- Filters Bar -->
      <div class="bg-slate-900 border border-slate-800 rounded-2xl p-4 shadow-xl flex flex-col sm:flex-row gap-4 items-center">
        <div class="flex-1 w-full relative">
          <input
            type="text"
            [(ngModel)]="searchTerm"
            (ngModelChange)="applyFilters()"
            placeholder="Buscar por código, nombre o descripción..."
            class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition text-sm"
          />
        </div>
        <div class="w-full sm:w-auto flex items-center gap-2">
          <label class="text-xs uppercase font-semibold text-slate-400 whitespace-nowrap">Estado:</label>
          <select
            [(ngModel)]="selectedStatus"
            (ngModelChange)="applyFilters()"
            class="bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white text-sm focus:outline-none focus:border-cyan-500 transition"
          >
            <option [ngValue]="undefined">Todos los estados</option>
            <option [ngValue]="ActivityStatus.Active">Activas</option>
            <option [ngValue]="ActivityStatus.Inactive">Inactivas</option>
            <option [ngValue]="ActivityStatus.Archived">Archivadas</option>
          </select>
        </div>
      </div>

      <!-- Feedback / Conflict banner -->
      @if (feedbackMessage()) {
        <div class="bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 px-4 py-3 rounded-xl text-sm flex justify-between items-center">
          <span>{{ feedbackMessage() }}</span>
          <button (click)="feedbackMessage.set(null)" class="text-emerald-400 hover:text-white">&times;</button>
        </div>
      }

      @if (errorMessage()) {
        <div class="bg-rose-500/10 border border-rose-500/30 text-rose-400 px-4 py-3 rounded-xl text-sm flex justify-between items-center">
          <span>{{ errorMessage() }}</span>
          <button (click)="errorMessage.set(null)" class="text-rose-400 hover:text-white">&times;</button>
        </div>
      }

      <!-- Activities Table -->
      <div class="bg-slate-900 border border-slate-800 rounded-2xl shadow-xl overflow-hidden">
        <div class="overflow-x-auto">
          <table class="w-full text-left text-sm text-slate-300">
            <thead class="text-xs uppercase bg-slate-950/70 text-slate-400 border-b border-slate-800">
              <tr>
                <th class="py-3.5 px-6">Código / Disciplina</th>
                <th class="py-3.5 px-6">Cupo Sugerido</th>
                <th class="py-3.5 px-6">Rango Etario</th>
                <th class="py-3.5 px-6 text-center">Estado</th>
                <th class="py-3.5 px-6 text-right">Acciones</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-800/80">
              @for (act of activities(); track act.id) {
                <tr class="hover:bg-slate-800/30 transition">
                  <td class="py-4 px-6">
                    <div class="flex items-center gap-3">
                      <span class="w-3.5 h-3.5 rounded-full shrink-0" [style.background-color]="act.colorHex || '#06B6D4'"></span>
                      <div>
                        <div class="font-bold text-white flex flex-wrap items-center gap-2">
                          {{ act.name }}
                          <span class="text-xs font-mono px-2 py-0.5 rounded bg-slate-800 text-slate-400 border border-slate-700">
                            {{ act.code }}
                          </span>
                          @if (act.requiresBooking === false) {
                            <span class="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-cyan-500/10 text-cyan-400 border border-cyan-500/30">
                              Acceso libre sin reserva
                            </span>
                          } @else {
                            <span class="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-slate-800 text-slate-400 border border-slate-700">
                              Con reserva previa
                            </span>
                          }
                        </div>
                        @if (act.shortDescription) {
                          <p class="text-xs text-slate-400 line-clamp-1 mt-0.5">{{ act.shortDescription }}</p>
                        }
                      </div>
                    </div>
                  </td>
                  <td class="py-4 px-6">
                    <span class="text-xs font-semibold text-slate-300">
                      {{ act.defaultCapacity ? act.defaultCapacity + ' personas' : 'Sin sugerir' }}
                    </span>
                  </td>
                  <td class="py-4 px-6 text-xs text-slate-400">
                    {{ act.minAge || act.maxAge ? (act.minAge || 0) + ' a ' + (act.maxAge || '∞') + ' años' : 'Sin restricción' }}
                  </td>
                  <td class="py-4 px-6 text-center">
                    @switch (act.status) {
                      @case (ActivityStatus.Active) {
                        <span class="px-2.5 py-1 rounded-full text-xs font-bold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                          Activa
                        </span>
                      }
                      @case (ActivityStatus.Inactive) {
                        <span class="px-2.5 py-1 rounded-full text-xs font-bold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                          Inactiva
                        </span>
                      }
                      @case (ActivityStatus.Archived) {
                        <span class="px-2.5 py-1 rounded-full text-xs font-bold bg-slate-700/50 text-slate-400 border border-slate-600">
                          Archivada
                        </span>
                      }
                    }
                  </td>
                  <td class="py-4 px-6 text-right space-x-2 whitespace-nowrap">
                    <button
                      (click)="openEditModal(act)"
                      class="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-white border border-slate-700 transition"
                    >
                      Editar
                    </button>

                    <app-activity-status-actions [activity]="act" (changed)="onStatusChanged($event)"></app-activity-status-actions>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5" class="py-8 text-center text-slate-500">
                    No se encontraron actividades registradas con los filtros seleccionados.
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Pagination -->
        @if (totalPages() > 1) {
          <div class="p-4 border-t border-slate-800 flex justify-between items-center text-xs text-slate-400">
            <span>Página {{ currentPage() }} de {{ totalPages() }} ({{ totalCount() }} actividades)</span>
            <div class="flex gap-2">
              <button
                [disabled]="currentPage() === 1"
                (click)="goToPage(currentPage() - 1)"
                class="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 disabled:opacity-40 text-white font-semibold transition"
              >
                Anterior
              </button>
              <button
                [disabled]="currentPage() === totalPages()"
                (click)="goToPage(currentPage() + 1)"
                class="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 disabled:opacity-40 text-white font-semibold transition"
              >
                Siguiente
              </button>
            </div>
          </div>
        }
      </div>

      <!-- Activity Form Modal -->
      @if (isModalOpen()) {
        <app-activity-form-modal
          [activity]="selectedActivity()"
          (saved)="onActivitySaved()"
          (cancelled)="isModalOpen.set(false)"
        ></app-activity-form-modal>
      }
    </div>
  `
})
export class ActivitiesManagementComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);

  readonly ActivityStatus = ActivityStatus;

  readonly activities = signal<Activity[]>([]);
  readonly isModalOpen = signal(false);
  readonly selectedActivity = signal<Activity | null>(null);
  readonly feedbackMessage = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  searchTerm = '';
  selectedStatus?: ActivityStatus;
  currentPage = signal(1);
  pageSize = signal(10);
  totalPages = signal(1);
  totalCount = signal(0);

  ngOnInit(): void {
    this.loadActivities();
  }

  loadActivities(): void {
    this.gymApi.getActivities(
      this.searchTerm.trim() || undefined,
      this.selectedStatus,
      undefined,
      this.currentPage(),
      this.pageSize()
    ).subscribe({
      next: (res: PagedResult<Activity>) => {
        this.activities.set(res.items || []);
        this.totalPages.set(res.totalPages || 1);
        this.totalCount.set(res.totalCount || 0);
      },
      error: () => this.activities.set([])
    });
  }

  applyFilters(): void {
    this.currentPage.set(1);
    this.loadActivities();
  }

  goToPage(page: number): void {
    this.currentPage.set(page);
    this.loadActivities();
  }

  openCreateModal(): void {
    this.selectedActivity.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(activity: Activity): void {
    this.errorMessage.set(null);
    // El detalle trae la versión vigente, necesaria para el PUT
    this.gymApi.getActivity(activity.id).subscribe({
      next: (full) => {
        this.selectedActivity.set(full);
        this.isModalOpen.set(true);
      },
      error: (err) => this.errorMessage.set(err.error?.message || 'No se pudo cargar el detalle de la actividad.')
    });
  }

  onActivitySaved(): void {
    this.isModalOpen.set(false);
    this.feedbackMessage.set('Actividad guardada exitosamente.');
    this.loadActivities();
  }

  onStatusChanged(activity: Activity): void {
    this.feedbackMessage.set(`Estado de "${activity.name}" actualizado.`);
    this.loadActivities();
  }
}
