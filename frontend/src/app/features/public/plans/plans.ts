import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { MembershipPlan } from '../../../core/models/gym.models';

@Component({
  selector: 'app-plans',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-12 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto">
        <div class="text-center max-w-3xl mx-auto mb-12">
          <h1 class="text-4xl font-black text-white">Planes y Membresías</h1>
          <p class="text-slate-400 mt-3 text-lg">Elige la modalidad que prefieras con créditos acumulables, descuentos por grupo familiar y promociones vigentes.</p>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-3 gap-8">
          @for (plan of plans(); track plan.id) {
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-8 flex flex-col justify-between hover:border-cyan-500/40 transition">
              <div>
                <span class="text-xs uppercase tracking-wider font-bold text-cyan-400 bg-cyan-500/10 px-3 py-1 rounded-full border border-cyan-500/20">
                  {{ plan.isUnlimited ? 'Ilimitado' : plan.credits + ' Clases' }}
                </span>
                <h2 class="text-2xl font-bold text-white mt-4">{{ plan.name }}</h2>
                <p class="text-slate-400 text-sm mt-2">{{ plan.description || 'Pase con acceso garantizado y reserva web.' }}</p>

                <div class="mt-6 flex items-baseline">
                  <span class="text-4xl font-extrabold text-white">\${{ plan.price }}</span>
                  <span class="text-slate-400 text-sm ml-2">/ {{ plan.durationDays }} días</span>
                </div>

                <ul class="mt-6 space-y-3 text-sm text-slate-300">
                  <li class="flex items-center gap-2">
                    <span class="text-cyan-400 font-bold">✓</span>
                    <span>{{ plan.appliesToAllActivities ? 'Todas las actividades' : plan.allowedActivityNames.join(', ') }}</span>
                  </li>
                  <li class="flex items-center gap-2">
                    <span class="text-cyan-400 font-bold">✓</span>
                    <span>Reintegro automático si cancelas con anticipación</span>
                  </li>
                  <li class="flex items-center gap-2">
                    <span class="text-cyan-400 font-bold">✓</span>
                    <span>Bonificación por grupo familiar</span>
                  </li>
                </ul>
              </div>

              <div class="mt-8">
                <a routerLink="/login" class="block w-full text-center bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold py-3 rounded-xl transition">
                  Adquirir Plan
                </a>
              </div>
            </div>
          }
        </div>
      </div>
    </div>
  `
})
export class PlansComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);
  readonly plans = signal<MembershipPlan[]>([]);

  ngOnInit(): void {
    this.gymApi.getMembershipPlans(true).subscribe({
      next: (items) => this.plans.set(items),
      error: () => this.plans.set([])
    });
  }
}
