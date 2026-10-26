import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { MembershipPlan, UpdateMembershipPlanRequest } from '../../../core/models/gym.models';
import { MembershipPlanFormModalComponent } from '../membership-plan-form-modal/membership-plan-form-modal';
import { MoneyAmountPipe } from '../../../shared/pipes/money-amount.pipe';

@Component({
  selector: 'app-plans-management',
  standalone: true,
  imports: [CommonModule, FormsModule, MembershipPlanFormModalComponent, MoneyAmountPipe],
  template: `
    <div class="space-y-6">
      <!-- Header bar -->
      <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col md:flex-row md:items-center md:justify-between gap-4">
        <div>
          <div class="flex items-center gap-3">
            <span class="h-3 w-3 rounded-full bg-emerald-400 animate-pulse"></span>
            <span class="text-xs uppercase tracking-widest font-semibold text-emerald-400">Administración de Catálogo</span>
          </div>
          <h2 class="text-2xl font-black text-white mt-1">Planes de Membresía y Paquetes</h2>
          <p class="text-xs text-slate-400">Configure los planes de suscripción, pases ilimitados y paquetes de créditos con actividades habilitadas</p>
        </div>

        <button
          (click)="openCreateModal()"
          class="px-5 py-2.5 rounded-xl text-xs font-bold text-slate-950 bg-emerald-400 hover:bg-emerald-300 shadow-lg shadow-emerald-500/20 transition flex items-center gap-2 self-start md:self-auto"
        >
          <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
          </svg>
          Nuevo Plan
        </button>
      </div>

      <!-- Feedback -->
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

      <!-- Filters -->
      <div class="flex flex-col sm:flex-row items-center gap-3 bg-slate-900 border border-slate-800 rounded-2xl p-4 shadow-lg">
        <div class="relative flex-1 w-full">
          <input
            type="text"
            [(ngModel)]="searchTerm"
            placeholder="Buscar por nombre de plan..."
            class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-emerald-500 transition"
          />
        </div>
        <div class="flex items-center gap-2 w-full sm:w-auto">
          <select
            [(ngModel)]="statusFilter"
            class="bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-xs text-white focus:outline-none focus:border-emerald-500 transition w-full sm:w-auto"
          >
            <option value="all">Todos los estados</option>
            <option value="active">Solo Activos</option>
            <option value="inactive">Solo Inactivos</option>
          </select>
        </div>
      </div>

      <!-- Plans Grid -->
      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        @for (plan of filteredPlans(); track plan.id) {
          <div
            class="bg-slate-900 border rounded-2xl p-6 transition flex flex-col justify-between"
            [ngClass]="plan.isActive ? 'border-slate-800 hover:border-emerald-500/40' : 'border-slate-800/50 opacity-60 bg-slate-900/40'"
          >
            <div>
              <div class="flex justify-between items-start mb-3">
                <span
                  class="text-[10px] uppercase tracking-wider font-bold px-2.5 py-0.5 rounded-full border"
                  [ngClass]="plan.isUnlimited ? 'bg-cyan-500/10 text-cyan-400 border-cyan-500/20' : 'bg-purple-500/10 text-purple-400 border-purple-500/20'"
                >
                  {{ plan.isUnlimited ? 'Pase Ilimitado' : plan.credits + ' Créditos' }}
                </span>
                <span
                  class="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded border"
                  [ngClass]="plan.isActive ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' : 'bg-slate-800 text-slate-400 border-slate-700'"
                >
                  {{ plan.isActive ? 'Activo' : 'Inactivo' }}
                </span>
              </div>

              <h3 class="text-xl font-bold text-white mt-2">{{ plan.name }}</h3>
              <p class="text-xs text-slate-400 mt-1 line-clamp-2">{{ plan.description || 'Sin descripción comercial.' }}</p>

              <div class="mt-4 flex items-baseline">
                <span class="text-3xl font-black text-white">\${{ plan.price | moneyAmount }}</span>
                <span class="text-xs text-slate-400 ml-2">/ {{ plan.durationDays }} días</span>
              </div>

              <div class="mt-4 pt-3 border-t border-slate-800/80 text-xs text-slate-300 space-y-1.5">
                <p class="flex items-center gap-1.5">
                  <span class="text-emerald-400 font-bold">✓</span>
                  <span>{{ plan.appliesToAllActivities ? 'Todas las actividades del gimnasio' : (plan.allowedActivityNames?.join(', ') || 'Actividades seleccionadas') }}</span>
                </p>
              </div>
            </div>

            <!-- Actions -->
            <div class="pt-4 mt-6 border-t border-slate-800/80 flex items-center justify-end gap-2">
              <button
                (click)="openEditModal(plan)"
                class="px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 transition"
              >
                Editar
              </button>
              <button
                (click)="togglePlanStatus(plan)"
                class="px-3 py-1.5 rounded-lg text-xs font-semibold transition border"
                [ngClass]="plan.isActive ? 'bg-slate-800 hover:bg-slate-700 text-slate-300 border-slate-700' : 'bg-emerald-500/10 hover:bg-emerald-500/20 text-emerald-300 border-emerald-500/30'"
              >
                {{ plan.isActive ? 'Desactivar' : 'Reactivar' }}
              </button>
            </div>
          </div>
        } @empty {
          <div class="col-span-full text-center py-12 bg-slate-900/30 rounded-2xl border border-slate-800/60 text-slate-500">
            No se encontraron planes con los filtros aplicados.
          </div>
        }
      </div>

      <!-- Modal -->
      @if (isModalOpen()) {
        <app-membership-plan-form-modal
          [plan]="selectedPlan()"
          (saved)="onPlanSaved()"
          (cancelled)="isModalOpen.set(false)"
        ></app-membership-plan-form-modal>
      }
    </div>
  `
})
export class PlansManagementComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);

  readonly plans = signal<MembershipPlan[]>([]);
  readonly isModalOpen = signal(false);
  readonly selectedPlan = signal<MembershipPlan | null>(null);

  searchTerm = '';
  statusFilter = 'all';

  readonly feedbackMessage = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.loadPlans();
  }

  loadPlans(): void {
    this.gymApi.getMembershipPlans(false).subscribe({
      next: (data) => this.plans.set(data || []),
      error: () => this.plans.set([])
    });
  }

  filteredPlans(): MembershipPlan[] {
    return this.plans().filter(p => {
      const matchesSearch = !this.searchTerm.trim() || p.name.toLowerCase().includes(this.searchTerm.trim().toLowerCase());
      const matchesStatus = this.statusFilter === 'all' ||
        (this.statusFilter === 'active' && p.isActive) ||
        (this.statusFilter === 'inactive' && !p.isActive);
      return matchesSearch && matchesStatus;
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
    this.feedbackMessage.set('Plan guardado exitosamente.');
    this.loadPlans();
    setTimeout(() => this.feedbackMessage.set(null), 4000);
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
      next: () => {
        this.feedbackMessage.set(`El plan fue ${plan.isActive ? 'desactivado' : 'reactivado'} correctamente.`);
        this.loadPlans();
        setTimeout(() => this.feedbackMessage.set(null), 4000);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Error al actualizar el estado del plan.');
        setTimeout(() => this.errorMessage.set(null), 5000);
      }
    });
  }
}
