import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { PublicActivity } from '../../../core/models/gym.models';

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
          <p class="text-slate-400 mt-3 text-lg">Descubre nuestras disciplinas dirigidas por profesores certificados.</p>
        </div>

        <!-- Search & Filter Bar -->
        <div class="max-w-xl mx-auto mb-10 flex flex-col sm:flex-row gap-4">
          <input
            type="text"
            [(ngModel)]="searchQuery"
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
                <a [routerLink]="['/horarios']" class="text-cyan-400 font-semibold hover:underline">
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

  readonly activities = signal<PublicActivity[]>([]);
  searchQuery = '';
  ageFilter?: number;

  ngOnInit(): void {
    this.loadActivities();
  }

  loadActivities(): void {
    const age = this.ageFilter && this.ageFilter > 0 ? this.ageFilter : undefined;
    this.gymApi.getPublicActivities(age).subscribe({
      next: (acts) => this.activities.set(acts),
      error: () => this.activities.set([])
    });
  }

  filteredActivities(): PublicActivity[] {
    const q = this.searchQuery.trim().toLowerCase();
    if (!q) return this.activities();
    return this.activities().filter(a =>
      a.name.toLowerCase().includes(q) ||
      (a.shortDescription && a.shortDescription.toLowerCase().includes(q)) ||
      (a.description && a.description.toLowerCase().includes(q)) ||
      a.code.toLowerCase().includes(q)
    );
  }
}
