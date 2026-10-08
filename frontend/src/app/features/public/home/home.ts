import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { PublicActivity, MembershipPlan } from '../../../core/models/gym.models';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
      <!-- Hero Section -->
      <section class="relative py-24 px-4 sm:px-6 lg:px-8 max-w-7xl mx-auto flex flex-col items-center text-center">
        <div class="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-cyan-500/10 border border-cyan-500/20 text-cyan-400 text-sm font-medium mb-6">
          <span class="inline-block w-2 h-2 rounded-full bg-cyan-400 animate-pulse"></span>
          Gestión inteligente de actividades y clases dirigidas
        </div>

        <h1 class="text-4xl sm:text-6xl font-black tracking-tight text-white max-w-4xl leading-tight">
          Supera tus límites en <span class="text-cyan-400">SmartGym</span> con cupos y horarios garantizados
        </h1>

        <p class="mt-6 text-lg sm:text-xl text-slate-400 max-w-2xl">
          Reserva clases grupales, accede a salas indoor y outdoor con instructores certificados, consulta tu saldo de créditos en tiempo real y entrena sin sobreventa.
        </p>

        <div class="mt-10 flex flex-wrap justify-center gap-4">
          <a routerLink="/actividades" class="bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold px-6 py-3 rounded-xl transition shadow-lg shadow-cyan-500/20">
            Explorar Actividades
          </a>
          <a routerLink="/horarios" class="bg-slate-900 hover:bg-slate-800 text-white font-semibold px-6 py-3 rounded-xl border border-slate-800 transition">
            Ver Grilla de Horarios
          </a>
        </div>
      </section>

      <!-- Actividades Destacadas -->
      <section class="py-16 bg-slate-900/50 border-y border-slate-800/80">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div class="flex justify-between items-end mb-10">
            <div>
              <h2 class="text-3xl font-black text-white">Nuestras Actividades</h2>
              <p class="text-slate-400 mt-2">Clases diseñadas para cada objetivo y nivel de condición física</p>
            </div>
            <a routerLink="/actividades" class="text-cyan-400 hover:underline font-semibold text-sm">Ver todas &rarr;</a>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-3 gap-8">
            @for (act of activities(); track act.code) {
              <div class="bg-slate-900 border border-slate-800 rounded-2xl overflow-hidden hover:border-cyan-500/40 transition shadow-sm flex flex-col justify-between">
                @if (act.primaryImageUrl) {
                  <div class="h-40 w-full overflow-hidden bg-slate-950">
                    <img [src]="act.primaryImageUrl" [alt]="act.name" class="w-full h-full object-cover" />
                  </div>
                }
                <div class="p-6 flex-1 flex flex-col justify-between">
                  <div>
                    <div class="flex items-center justify-between mb-4">
                      <div class="flex items-center gap-2">
                        @if (act.colorHex) {
                          <span class="w-2.5 h-2.5 rounded-full inline-block" [style.background-color]="act.colorHex"></span>
                        }
                        <span class="text-xs font-bold uppercase tracking-wider px-2.5 py-1 rounded-md bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
                          {{ act.code }}
                        </span>
                      </div>
                      @if (act.logoUrl) {
                        <img [src]="act.logoUrl" [alt]="act.name" class="w-6 h-6 object-contain rounded" />
                      }
                    </div>
                    <h3 class="text-xl font-bold text-white mb-2">{{ act.name }}</h3>
                    <p class="text-sm text-slate-400 line-clamp-3 mb-4">{{ act.shortDescription || act.description }}</p>
                  </div>
                  <div class="pt-4 border-t border-slate-800/80 flex justify-between items-center text-xs text-slate-400">
                    <span>Edades: {{ act.minAge || 14 }} a {{ act.maxAge || 70 }} años</span>
                    <a routerLink="/horarios" class="text-cyan-400 font-semibold hover:underline">Horarios</a>
                  </div>
                </div>
              </div>
            }
          </div>
        </div>
      </section>

      <!-- Planes de Membresía -->
      <section class="py-20 max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 w-full">
        <div class="text-center max-w-3xl mx-auto mb-16">
          <h2 class="text-3xl sm:text-4xl font-black text-white">Planes Flexibles y Transparentes</h2>
          <p class="text-slate-400 mt-3 text-lg">Elige el pase que mejor se adapte a tu ritmo. Créditos transferibles y reintegro ante suspensiones.</p>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-3 gap-8">
          @for (plan of plans(); track plan.id) {
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-8 flex flex-col justify-between hover:border-cyan-500/40 transition">
              <div>
                <h3 class="text-xl font-bold text-white">{{ plan.name }}</h3>
                <p class="text-slate-400 text-sm mt-2">{{ plan.description || 'Acceso garantizado a actividades dirigidas.' }}</p>
                <div class="mt-6 flex items-baseline">
                  <span class="text-4xl font-extrabold text-white">\${{ plan.price }}</span>
                  <span class="text-slate-400 text-sm ml-2">/ {{ plan.durationDays }} días</span>
                </div>
                <ul class="mt-6 space-y-3 text-sm text-slate-300">
                  <li class="flex items-center gap-2">
                    <span class="text-cyan-400 font-bold">✓</span>
                    <span>{{ plan.isUnlimited ? 'Clases ilimitadas' : plan.credits + ' créditos mensuales' }}</span>
                  </li>
                  <li class="flex items-center gap-2">
                    <span class="text-cyan-400 font-bold">✓</span>
                    <span>{{ plan.appliesToAllActivities ? 'Acceso a todas las actividades' : plan.allowedActivityNames.join(', ') }}</span>
                  </li>
                  <li class="flex items-center gap-2">
                    <span class="text-cyan-400 font-bold">✓</span>
                    <span>Reserva anticipada desde el celular</span>
                  </li>
                </ul>
              </div>
              <div class="mt-8">
                <a routerLink="/login" class="block w-full text-center bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold py-3 rounded-xl transition">
                  Comenzar Ahora
                </a>
              </div>
            </div>
          }
        </div>
      </section>
    </div>
  `
})
export class HomeComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);

  readonly activities = signal<PublicActivity[]>([]);
  readonly plans = signal<MembershipPlan[]>([]);

  ngOnInit(): void {
    this.gymApi.getPublicActivities().subscribe({
      next: (acts) => this.activities.set(acts.slice(0, 3)),
      error: () => this.activities.set([])
    });

    this.gymApi.getMembershipPlans(true).subscribe({
      next: (plans) => this.plans.set(plans),
      error: () => this.plans.set([])
    });
  }
}
