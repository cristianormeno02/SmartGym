import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { MembershipPlan, UpdateMembershipPlanRequest } from '../../../core/models/gym.models';
import { MembershipPlanFormModalComponent } from '../../catalog/membership-plan-form-modal/membership-plan-form-modal';
import { MoneyAmountPipe } from '../../../shared/pipes/money-amount.pipe';

@Component({
  selector: 'app-plans',
  standalone: true,
  imports: [CommonModule, RouterLink, MembershipPlanFormModalComponent, MoneyAmountPipe],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-12 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-8">
        <!-- Header -->
        <div class="flex flex-col md:flex-row md:items-center md:justify-between gap-6 pb-6 border-b border-slate-800">
          <div>
            <h1 class="text-4xl font-black text-white">Planes y Membresías</h1>
            <p class="text-slate-400 mt-2 text-base">Elige la modalidad que prefieras con créditos acumulables, descuentos por grupo familiar y promociones vigentes.</p>
          </div>

          @if (authService.isStaff()) {
            <button
              (click)="openCreateModal()"
              class="btn-new-plan px-5 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-emerald-400 hover:bg-emerald-300 shadow-lg shadow-emerald-500/20 transition flex items-center gap-2 self-start md:self-auto"
            >
              <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
              </svg>
              + Nuevo Plan
            </button>
          }
        </div>

        <!-- Plans Grid -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-8">
          @for (plan of plans(); track plan.id) {
            <div
              class="bg-slate-900 border rounded-2xl p-8 flex flex-col justify-between transition"
              [ngClass]="plan.isActive ? 'border-slate-800 hover:border-cyan-500/40' : 'border-slate-800/50 opacity-60 bg-slate-900/40'"
            >
              <div>
                <div class="flex justify-between items-start">
                  <span class="text-xs uppercase tracking-wider font-bold text-cyan-400 bg-cyan-500/10 px-3 py-1 rounded-full border border-cyan-500/20">
                    {{ plan.isUnlimited ? 'Ilimitado' : plan.credits + ' Clases' }}
                  </span>
                  @if (!plan.isActive) {
                    <span class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded border bg-slate-800 text-slate-400 border-slate-700">
                      Inactivo
                    </span>
                  }
                </div>

                <h2 class="text-2xl font-bold text-white mt-4">{{ plan.name }}</h2>
                <p class="text-slate-400 text-sm mt-2">{{ plan.description || 'Pase con acceso garantizado y reserva web.' }}</p>

                <div class="mt-6 flex items-baseline">
                  <span class="text-4xl font-extrabold text-white">\${{ plan.price | moneyAmount }}</span>
                  <span class="text-slate-400 text-sm ml-2">/ {{ plan.durationDays }} días</span>
                </div>

                <ul class="mt-6 space-y-3 text-sm text-slate-300">
                  <li class="flex items-center gap-2">
                    <span class="text-cyan-400 font-bold">✓</span>
                    <span>{{ plan.appliesToAllActivities ? 'Todas las actividades' : (plan.allowedActivityNames?.join(', ') || 'Actividades seleccionadas') }}</span>
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

              <!-- Public CTA or Staff Actions -->
              <div class="mt-8 pt-4 border-t border-slate-800/80">
                @if (authService.isStaff()) {
                  <div class="flex items-center justify-end gap-2">
                    <button
                      (click)="openEditModal(plan)"
                      class="btn-edit-plan px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 transition"
                    >
                      Editar
                    </button>
                    <button
                      (click)="togglePlanStatus(plan)"
                      class="btn-toggle-plan px-3 py-1.5 rounded-lg text-xs font-semibold transition border"
                      [ngClass]="plan.isActive ? 'bg-slate-800 hover:bg-slate-700 text-slate-300 border-slate-700' : 'bg-emerald-500/10 hover:bg-emerald-500/20 text-emerald-300 border-emerald-500/30'"
                    >
                      {{ plan.isActive ? 'Desactivar' : 'Activar' }}
                    </button>
                  </div>
                } @else {
                  <a routerLink="/login" class="block w-full text-center bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold py-3 rounded-xl transition">
                    Adquirir Plan
                  </a>
                }
              </div>
            </div>
          }
        </div>
      </div>

      <!-- Modal -->
      @if (isModalOpen()) {
        <!-- Carga diferida: el formulario solo lo usa el staff y no debe pesar en el bundle inicial -->
        @defer (on immediate) {
          <app-membership-plan-form-modal
            [plan]="selectedPlan()"
            (saved)="onPlanSaved()"
            (cancelled)="isModalOpen.set(false)"
          ></app-membership-plan-form-modal>
        }
      }
    </div>
  `
})
export class PlansComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);
  readonly authService = inject(AuthService);

  readonly plans = signal<MembershipPlan[]>([]);
  readonly isModalOpen = signal(false);
  readonly selectedPlan = signal<MembershipPlan | null>(null);

  ngOnInit(): void {
    this.loadPlans();
  }

  loadPlans(): void {
    const onlyActive = !this.authService.isStaff();
    this.gymApi.getMembershipPlans(onlyActive).subscribe({
      next: (items) => this.plans.set(items || []),
      error: () => this.plans.set([])
    });
  }

  openCreateModal(): void {
    this.selectedPlan.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(plan: MembershipPlan): void {
    this.selectedPlan.set(plan);
    this.isModalOpen.set(true);
  }

  onPlanSaved(): void {
    this.isModalOpen.set(false);
    this.selectedPlan.set(null);
    this.loadPlans();
  }

  togglePlanStatus(plan: MembershipPlan): void {
    const updateReq: UpdateMembershipPlanRequest = {
      name: plan.name,
      description: plan.description,
      durationDays: plan.durationDays,
      price: plan.price,
      credits: plan.credits,
      isUnlimited: plan.isUnlimited,
      appliesToAllActivities: plan.appliesToAllActivities,
      allowedActivityIds: plan.allowedActivityIds,
      isActive: !plan.isActive
    };

    this.gymApi.updateMembershipPlan(plan.id, updateReq).subscribe({
      next: () => this.loadPlans()
    });
  }
}
