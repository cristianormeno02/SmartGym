import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GymApiService } from '../../../core/services/gym-api.service';
import { Activity, ActivityStatus } from '../../../core/models/gym.models';

interface StatusDialog {
  message: string;
  details?: { activeSchedules?: number; futureSessions?: number };
}

/**
 * Acciones de cambio de estado de una actividad. Solo ofrece las transiciones válidas del dominio
 * (Active ↔ Inactive ↔ Archived) y envía siempre la versión vigente obtenida del detalle.
 */
@Component({
  selector: 'app-activity-status-actions',
  standalone: true,
  imports: [CommonModule],
  template: `
    <span class="inline-flex items-center gap-2">
      @if (activity.status === ActivityStatus.Active) {
        <button
          (click)="changeStatus(ActivityStatus.Inactive)"
          [disabled]="isBusy()"
          class="status-action px-3 py-1.5 rounded-lg border border-amber-500/40 text-amber-400 hover:bg-amber-500/10 text-xs font-semibold transition disabled:opacity-50"
        >Inactivar</button>
      }
      @if (activity.status === ActivityStatus.Inactive) {
        <button
          (click)="changeStatus(ActivityStatus.Active)"
          [disabled]="isBusy()"
          class="status-action px-3 py-1.5 rounded-lg border border-emerald-500/40 text-emerald-400 hover:bg-emerald-500/10 text-xs font-semibold transition disabled:opacity-50"
        >Activar</button>
        <button
          (click)="changeStatus(ActivityStatus.Archived)"
          [disabled]="isBusy()"
          class="status-action px-3 py-1.5 rounded-lg border border-rose-500/40 text-rose-400 hover:bg-rose-500/10 text-xs font-semibold transition disabled:opacity-50"
        >Archivar</button>
      }
      @if (activity.status === ActivityStatus.Archived) {
        <button
          (click)="changeStatus(ActivityStatus.Inactive)"
          [disabled]="isBusy()"
          class="status-action px-3 py-1.5 rounded-lg border border-cyan-500/40 text-cyan-400 hover:bg-cyan-500/10 text-xs font-semibold transition disabled:opacity-50"
        >Restaurar</button>
      }
    </span>

    @if (dialog()) {
      <div class="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/70 px-4 text-left whitespace-normal">
        <div class="w-full max-w-md bg-slate-900 border border-amber-500/30 text-amber-300 p-5 rounded-2xl text-sm flex flex-col gap-3 shadow-2xl">
          <div class="font-bold flex items-center gap-2">
            <span>⚠️</span> No se pudo cambiar el estado de "{{ activity.name }}"
          </div>
          <p>{{ dialog()?.message }}</p>
          @if (dialog()?.details) {
            <div class="text-xs bg-slate-950/60 p-2.5 rounded-lg border border-amber-500/20 text-slate-300">
              Turnos programados activos: {{ dialog()?.details?.activeSchedules ?? 0 }} | Clases futuras: {{ dialog()?.details?.futureSessions ?? 0 }}
            </div>
          }
          <button (click)="dialog.set(null)" class="self-end px-3 py-1 rounded bg-amber-500/20 hover:bg-amber-500/30 text-amber-200 text-xs font-semibold">
            Entendido
          </button>
        </div>
      </div>
    }
  `
})
export class ActivityStatusActionsComponent {
  private readonly gymApi = inject(GymApiService);

  @Input({ required: true }) activity!: Activity;
  @Output() changed = new EventEmitter<Activity>();

  readonly ActivityStatus = ActivityStatus;
  readonly isBusy = signal(false);
  readonly dialog = signal<StatusDialog | null>(null);

  changeStatus(newStatus: ActivityStatus): void {
    this.dialog.set(null);
    this.isBusy.set(true);

    this.gymApi.getActivity(this.activity.id).subscribe({
      next: (full) => {
        if (full.version === undefined || full.version === null) {
          this.fail({ message: 'No se pudo obtener la versión vigente de la actividad. Recargue e intente nuevamente.' });
          return;
        }

        this.gymApi.changeActivityStatus(this.activity.id, { status: newStatus, version: full.version }).subscribe({
          next: () => {
            this.isBusy.set(false);
            this.changed.emit(this.activity);
          },
          error: (err) => this.fail({
            message: err.error?.message || 'Error al cambiar el estado de la actividad.',
            details: err.status === 409 ? err.error?.details : undefined
          })
        });
      },
      error: (err) => this.fail({ message: err.error?.message || 'Error al obtener el detalle de la actividad.' })
    });
  }

  private fail(dialog: StatusDialog): void {
    this.isBusy.set(false);
    this.dialog.set(dialog);
  }
}
