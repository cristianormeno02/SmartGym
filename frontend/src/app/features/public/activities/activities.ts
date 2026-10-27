import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { Activity, ActivityStatus, PublicActivity } from '../../../core/models/gym.models';
import { ActivityFormModalComponent } from '../../catalog/activity-form-modal/activity-form-modal';
import { ActivityStatusActionsComponent } from '../../catalog/activity-status-actions/activity-status-actions';

@Component({
  selector: 'app-activities',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ActivityFormModalComponent, ActivityStatusActionsComponent],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-12 px-4 sm:px-6 lg:px-8 animate-fadeIn">
      <div class="max-w-7xl mx-auto space-y-8">
        <!-- Header -->
        <div class="flex flex-col md:flex-row md:items-center md:justify-between gap-6 border-b border-slate-800 pb-8">
          <div>
            <h1 class="text-4xl font-black text-white">Catálogo de Actividades</h1>
            <p class="text-slate-400 mt-2 text-lg">Descubre nuestras disciplinas dirigidas por profesores certificados.</p>
          </div>

          @if (authService.isStaff()) {
            <button
              (click)="openCreateModal()"
              class="btn-new-activity px-5 py-2.5 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold text-sm transition flex items-center gap-2 self-start md:self-auto shadow-lg shadow-cyan-500/20"
            >
              <span>+</span> Nueva Actividad
            </button>
          }
        </div>

        <!-- Feedback message -->
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

        <!-- Search & Filter Bar -->
        <div class="max-w-xl mx-auto mb-10 flex flex-col sm:flex-row gap-4">
          <input
            type="text"
            [(ngModel)]="searchQuery"
            (ngModelChange)="onSearchChange()"
            placeholder="Buscar por nombre o descripción..."
            class="flex-1 bg-slate-900 border border-slate-800 rounded-xl px-4 py-3 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
          />
          <input
            type="number"
            [(ngModel)]="ageFilter"
            (ngModelChange)="loadActivities()"
            placeholder="Tu edad (opcional)"
            min="0"
            max="120"
            class="w-full sm:w-44 bg-slate-900 border border-slate-800 rounded-xl px-4 py-3 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
          />

          @if (authService.isStaff()) {
            <select
              [(ngModel)]="statusFilter"
              (ngModelChange)="loadActivities()"
              class="bg-slate-900 border border-slate-800 rounded-xl px-4 py-3 text-white text-sm focus:outline-none focus:border-cyan-500 transition"
            >
              <option [ngValue]="undefined">Todas</option>
              <option [ngValue]="ActivityStatus.Active">Activas</option>
              <option [ngValue]="ActivityStatus.Inactive">Inactivas</option>
              <option [ngValue]="ActivityStatus.Archived">Archivadas</option>
            </select>
          }
        </div>

        <!-- Grid of Activities -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-8">
          @for (act of filteredActivities(); track act.code) {
            <div class="bg-slate-900 border border-slate-800 rounded-2xl overflow-hidden hover:border-cyan-500/40 transition flex flex-col justify-between">
              @if (act.primaryImageUrl) {
                <div class="h-48 w-full overflow-hidden bg-slate-950">
                  <img [src]="act.primaryImageUrl" [alt]="act.name" class="w-full h-full object-cover" />
                </div>
              }
              <div class="p-6 flex-1 flex flex-col">
                <div class="flex items-center justify-between mb-3">
                  <div class="flex items-center gap-2">
                    @if (act.colorHex) {
                      <span class="w-3 h-3 rounded-full inline-block" [style.background-color]="act.colorHex"></span>
                    }
                    <span class="text-xs font-bold uppercase tracking-wider px-2.5 py-1 rounded-md bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
                      {{ act.code }}
                    </span>
                    @if (act.requiresBooking === false) {
                      <span class="text-[10px] uppercase font-bold px-2 py-0.5 rounded-full bg-cyan-500/10 text-cyan-400 border border-cyan-500/30">
                        Acceso libre sin reserva
                      </span>
                    } @else {
                      <span class="text-[10px] uppercase font-bold px-2 py-0.5 rounded-full bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
                        Con reserva previa
                      </span>
                    }
                    @if (authService.isStaff() && isActivityItem(act)) {
                      <span
                        class="text-[10px] uppercase font-bold px-2 py-0.5 rounded-full"
                        [ngClass]="act.status === ActivityStatus.Active ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20' : act.status === ActivityStatus.Archived ? 'bg-slate-700/50 text-slate-400 border border-slate-600' : 'bg-amber-500/10 text-amber-400 border border-amber-500/20'"
                      >
                        {{ act.status === ActivityStatus.Active ? 'Activa' : act.status === ActivityStatus.Archived ? 'Archivada' : 'Inactiva' }}
                      </span>
                    }
                  </div>
                  @if (act.logoUrl) {
                    <img [src]="act.logoUrl" [alt]="act.name + ' logo'" class="w-7 h-7 object-contain rounded" />
                  }
                </div>
                <h3 class="text-2xl font-bold text-white mb-2">{{ act.name }}</h3>
                @if (act.shortDescription) {
                  <p class="text-sm text-slate-300 font-medium mb-3">{{ act.shortDescription }}</p>
                }
                @if (act.description) {
                  <p class="text-xs text-slate-400 leading-relaxed mb-3 line-clamp-3">{{ act.description }}</p>
                }
                @if (act.equipmentNotes) {
                  <p class="text-xs text-amber-400/90 mt-auto pt-2">
                    <span class="font-semibold">Equipamiento:</span> {{ act.equipmentNotes }}
                  </p>
                }
              </div>

              <div class="p-6 pt-0 border-t border-slate-800/80 mt-4 flex items-center justify-between text-xs text-slate-400">
                <span>Edades: {{ act.minAge || 14 }} a {{ act.maxAge || 70 }} años</span>
                <div class="flex items-center gap-3">
                  @if (authService.isStaff() && isActivityItem(act)) {
                    <button
                      (click)="openEditModal(act)"
                      class="px-2.5 py-1 rounded-lg bg-slate-800 hover:bg-slate-700 text-white font-semibold border border-slate-700 transition"
                    >
                      Editar
                    </button>
                    @defer (on immediate) {
                      <app-activity-status-actions [activity]="act" (changed)="onStatusChanged($event)"></app-activity-status-actions>
                    }
                  }
                  <a [routerLink]="['/horarios']" class="text-cyan-400 font-semibold hover:underline">
                    Ver Horarios &rarr;
                  </a>
                </div>
              </div>
            </div>
          } @empty {
            <div class="col-span-full text-center py-12 text-slate-500">
              No se encontraron actividades que coincidan con la búsqueda.
            </div>
          }
        </div>
      </div>

      <!-- Activity Form Modal -->
      @if (isModalOpen()) {
        <!-- Carga diferida: el formulario solo lo usa el staff y no debe pesar en el bundle inicial -->
        @defer (on immediate) {
          <app-activity-form-modal
            [activity]="selectedActivity()"
            (saved)="onActivitySaved()"
            (cancelled)="isModalOpen.set(false)"
          ></app-activity-form-modal>
        }
      }
    </div>
  `
})
export class ActivitiesComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly gymApi = inject(GymApiService);

  readonly ActivityStatus = ActivityStatus;

  readonly publicActivities = signal<PublicActivity[]>([]);
  readonly adminActivities = signal<Activity[]>([]);

  readonly isModalOpen = signal(false);
  readonly selectedActivity = signal<Activity | null>(null);
  readonly feedbackMessage = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  searchQuery = '';
  ageFilter?: number;
  statusFilter?: ActivityStatus;

  ngOnInit(): void {
    this.loadActivities();
  }

  loadActivities(): void {
    const age = this.ageFilter && this.ageFilter > 0 ? this.ageFilter : undefined;

    if (this.authService.isStaff()) {
      this.gymApi.getActivities(this.searchQuery.trim() || undefined, this.statusFilter, age, 1, 50).subscribe({
        next: (paged) => this.adminActivities.set(paged.items || []),
        error: () => this.adminActivities.set([])
      });
    } else {
      this.gymApi.getPublicActivities(age).subscribe({
        next: (acts) => this.publicActivities.set(acts),
        error: () => this.publicActivities.set([])
      });
    }
  }

  onSearchChange(): void {
    if (this.authService.isStaff()) {
      this.loadActivities();
    }
  }

  isActivityItem(act: any): act is Activity {
    return 'id' in act;
  }

  filteredActivities(): (PublicActivity | Activity)[] {
    if (this.authService.isStaff()) {
      return this.adminActivities();
    }

    const q = this.searchQuery.trim().toLowerCase();
    if (!q) return this.publicActivities();
    return this.publicActivities().filter(a =>
      a.name.toLowerCase().includes(q) ||
      (a.shortDescription && a.shortDescription.toLowerCase().includes(q)) ||
      (a.description && a.description.toLowerCase().includes(q)) ||
      a.code.toLowerCase().includes(q)
    );
  }

  openCreateModal(): void {
    this.selectedActivity.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(act: Activity): void {
    this.errorMessage.set(null);
    // El detalle trae la versión vigente, necesaria para el PUT
    this.gymApi.getActivity(act.id).subscribe({
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
