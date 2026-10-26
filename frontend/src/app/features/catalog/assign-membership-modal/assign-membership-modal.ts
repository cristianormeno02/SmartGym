import { Component, OnInit, Input, Output, EventEmitter, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { MembershipDetail, MembershipPlan } from '../../../core/models/gym.models';
import { MoneyAmountPipe } from '../../../shared/pipes/money-amount.pipe';
import { apiErrorMessage } from '../../../core/utils/api-error';

@Component({
  selector: 'app-assign-membership-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, MoneyAmountPipe],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm overflow-y-auto" data-testid="modal-assign-membership">
      <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-xl p-6 sm:p-8 shadow-2xl space-y-6 my-8">

        <!-- Header -->
        <div class="flex items-center justify-between border-b border-slate-800 pb-4">
          <div>
            <span class="text-xs font-semibold text-emerald-400 uppercase tracking-widest">Asignación Directa</span>
            <h2 class="text-2xl font-bold text-white mt-1">
              Asignar Membresía
            </h2>
            @if (studentName) {
              <p class="text-xs text-slate-400 mt-1">
                Alumno: <span class="font-bold text-slate-200">{{ studentName }}</span>
              </p>
            }
          </div>
          <button (click)="cancelled.emit()"
                  data-testid="btn-close-assign"
                  class="text-slate-400 hover:text-white transition p-2 rounded-lg hover:bg-slate-800">
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        @if (errorMessage()) {
          <div class="p-4 rounded-xl text-xs font-semibold bg-rose-950/60 border border-rose-800 text-rose-300" data-testid="assign-error-msg">
            {{ errorMessage() }}
          </div>
        }

        @if (isLoadingPlans()) {
          <div class="py-8 text-center text-slate-400 text-xs">
            Cargando planes de membresía activos...
          </div>
        } @else {
          <div class="space-y-4">
            <!-- Selección de Plan -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Plan de Membresía <span class="text-rose-400">*</span>
              </label>
              <select
                [ngModel]="selectedPlanId()"
                (ngModelChange)="selectedPlanId.set($event)"
                data-testid="select-membership-plan"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-sm text-white focus:outline-none focus:border-emerald-500 transition"
              >
                <option value="">Seleccione un plan activo...</option>
                @for (plan of plans(); track plan.id) {
                  <option [value]="plan.id">
                    {{ plan.name }} — \${{ plan.price | moneyAmount }} ({{ plan.durationDays }} días)
                  </option>
                }
              </select>
            </div>

            <!-- Fecha de Inicio -->
            <div>
              <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
                Fecha de Inicio <span class="text-rose-400">*</span>
              </label>
              <input
                type="date"
                [min]="todayIso"
                [ngModel]="startDate()"
                (ngModelChange)="startDate.set($event)"
                data-testid="input-start-date"
                class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-sm text-white focus:outline-none focus:border-emerald-500 transition"
              />
              @if (startDate() && startDate() < todayIso) {
                <span class="text-xs text-rose-400 mt-1 block">La fecha de inicio no puede ser anterior a hoy.</span>
              }
            </div>

            <!-- Previsualización Comercial del Plan Seleccionado -->
            @if (selectedPlan(); as plan) {
              <div class="bg-slate-950/80 border border-slate-800 rounded-xl p-4 space-y-3" data-testid="plan-commercial-preview">
                <div class="flex items-center justify-between border-b border-slate-800 pb-2">
                  <div>
                    <h4 class="text-sm font-bold text-white">{{ plan.name }}</h4>
                    @if (plan.description) {
                      <p class="text-xs text-slate-400">{{ plan.description }}</p>
                    }
                  </div>
                  <div class="text-right">
                    <span class="text-lg font-black text-emerald-400">\${{ plan.price | moneyAmount }}</span>
                  </div>
                </div>

                <div class="grid grid-cols-2 gap-3 text-xs">
                  <div>
                    <span class="text-slate-500 uppercase font-semibold block">Duración</span>
                    <span class="text-slate-200 font-bold">{{ plan.durationDays }} días</span>
                  </div>
                  <div>
                    <span class="text-slate-500 uppercase font-semibold block">Vencimiento Estimado</span>
                    <span class="text-slate-200 font-mono font-bold">{{ projectedEndDate() || '—' }}</span>
                  </div>
                  <div>
                    <span class="text-slate-500 uppercase font-semibold block">Modalidad / Cupo</span>
                    <span class="text-cyan-400 font-bold">
                      {{ plan.isUnlimited ? 'Pase Libre Ilimitado (∞)' : (plan.credits + ' créditos') }}
                    </span>
                  </div>
                  <div>
                    <span class="text-slate-500 uppercase font-semibold block">Actividades Habilitadas</span>
                    <span class="text-slate-200 font-semibold">
                      {{ plan.appliesToAllActivities ? 'Todas las actividades' : (plan.allowedActivityNames.join(', ') || 'Actividades seleccionadas') }}
                    </span>
                  </div>
                </div>
              </div>
            }

            <!-- Acciones -->
            <div class="flex items-center justify-end gap-3 pt-4 border-t border-slate-800">
              <button
                type="button"
                (click)="cancelled.emit()"
                data-testid="btn-cancel-assign"
                class="px-4 py-2.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-xs font-semibold transition"
              >
                Cancelar
              </button>
              <button
                type="button"
                (click)="onSubmit()"
                [disabled]="isSubmitting() || !isFormValid()"
                data-testid="btn-confirm-assign"
                class="px-5 py-2.5 bg-emerald-500 hover:bg-emerald-400 disabled:opacity-40 disabled:cursor-not-allowed text-slate-950 rounded-xl text-xs font-bold transition shadow-lg shadow-emerald-500/20"
              >
                {{ isSubmitting() ? 'Asignando...' : 'Confirmar Asignación' }}
              </button>
            </div>
          </div>
        }
      </div>
    </div>
  `
})
export class AssignMembershipModalComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);

  @Input({ required: true }) studentId!: string;
  @Input() studentName = '';

  @Output() assigned = new EventEmitter<MembershipDetail>();
  @Output() cancelled = new EventEmitter<void>();

  readonly todayIso = this.buildLocalTodayIso();

  readonly plans = signal<MembershipPlan[]>([]);
  readonly isLoadingPlans = signal<boolean>(false);
  readonly selectedPlanId = signal<string>('');
  readonly startDate = signal<string>(this.todayIso);
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  readonly selectedPlan = computed(() => {
    const id = this.selectedPlanId();
    return this.plans().find((p) => p.id === id) ?? null;
  });

  readonly projectedEndDate = computed(() => {
    const plan = this.selectedPlan();
    const start = this.startDate();
    if (!plan || !start) return '';

    const parts = start.split('-').map(Number);
    if (parts.length !== 3 || parts.some(Number.isNaN)) return '';

    const [year, month, day] = parts;
    const endUtc = new Date(Date.UTC(year, month - 1, day + Math.max(1, plan.durationDays) - 1));
    const y = endUtc.getUTCFullYear();
    const m = String(endUtc.getUTCMonth() + 1).padStart(2, '0');
    const d = String(endUtc.getUTCDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  });

  readonly isFormValid = computed(() => {
    const planId = this.selectedPlanId();
    const start = this.startDate();
    return !!planId && !!start && start >= this.todayIso;
  });

  ngOnInit(): void {
    this.loadActivePlans();
  }

  private loadActivePlans(): void {
    this.isLoadingPlans.set(true);
    this.gymApi.getMembershipPlans(true).subscribe({
      next: (list) => {
        this.plans.set(list ?? []);
        this.isLoadingPlans.set(false);
      },
      error: (err) => {
        this.isLoadingPlans.set(false);
        this.errorMessage.set(apiErrorMessage(err, 'No se pudieron cargar los planes de membresía activos.'));
      }
    });
  }

  onSubmit(): void {
    if (!this.isFormValid() || this.isSubmitting()) return;

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    this.gymApi
      .assignMembership({
        studentId: this.studentId,
        membershipPlanId: this.selectedPlanId(),
        startDate: this.startDate()
      })
      .subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.assigned.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(apiErrorMessage(err, 'No se pudo asignar la membresía al alumno.'));
        }
      });
  }

  private buildLocalTodayIso(): string {
    const now = new Date();
    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const day = String(now.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
}
