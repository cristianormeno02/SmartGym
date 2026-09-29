import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { Activity } from '../../../core/models/gym.models';

@Component({
  selector: 'app-activities',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-12 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto">
        <!-- Header -->
        <div class="text-center max-w-3xl mx-auto mb-12">
          <h1 class="text-4xl font-black text-white">Catálogo de Actividades</h1>
          <p class="text-slate-400 mt-3 text-lg">Descubre nuestras disciplinas dirigidas por profesores certificados en salas equipadas.</p>
        </div>

        <!-- Search Bar -->
        <div class="max-w-md mx-auto mb-10">
          <input
            type="text"
            [(ngModel)]="searchQuery"
            placeholder="Buscar por nombre de actividad..."
            class="w-full bg-slate-900 border border-slate-800 rounded-xl px-4 py-3 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
          />
        </div>

        <!-- Grid of Activities -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-8">
          @for (act of filteredActivities(); track act.id) {
            <div class="bg-slate-900 border border-slate-800 rounded-2xl overflow-hidden hover:border-cyan-500/40 transition flex flex-col justify-between">
              <div class="p-6">
                <div class="flex items-center justify-between mb-3">
                  <span class="text-xs font-bold uppercase tracking-wider px-2.5 py-1 rounded-md bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
                    {{ act.defaultRoomName || 'Sala General' }}
                  </span>
                  <span class="text-xs text-slate-400">Cupo: {{ act.minCapacity }} a {{ act.maxCapacity }} pers.</span>
                </div>
                <h3 class="text-2xl font-bold text-white mb-2">{{ act.name }}</h3>
                <p class="text-sm text-slate-300 font-medium mb-3">{{ act.summary }}</p>
                <p class="text-xs text-slate-400 leading-relaxed">{{ act.description }}</p>
              </div>

              <div class="p-6 pt-0 border-t border-slate-800/80 mt-4 flex items-center justify-between text-xs text-slate-400">
                <span>Rango de edad: {{ act.minAge || 14 }} a {{ act.maxAge || 70 }} años</span>
                <a [routerLink]="['/horarios']" [queryParams]="{ activityId: act.id }" class="text-cyan-400 font-semibold hover:underline">
                  Ver Horarios &rarr;
                </a>
              </div>
            </div>
          } @empty {
            <div class="col-span-full text-center py-12 text-slate-500">
              No se encontraron actividades que coincidan con la búsqueda.
            </div>
          }
        </div>
      </div>
    </div>
  `
})
export class ActivitiesComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);

  readonly activities = signal<Activity[]>([]);
  searchQuery = '';

  ngOnInit(): void {
    this.gymApi.getActivities(true).subscribe({
      next: (acts) => this.activities.set(acts),
      error: () => this.activities.set([])
    });
  }

  filteredActivities(): Activity[] {
    const q = this.searchQuery.trim().toLowerCase();
    if (!q) return this.activities();
    return this.activities().filter(a =>
      a.name.toLowerCase().includes(q) ||
      (a.summary && a.summary.toLowerCase().includes(q)) ||
      (a.description && a.description.toLowerCase().includes(q))
    );
  }
}
