import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NotificationsService } from '../../../core/services/notifications.service';
import {
  NotificationCategory,
  NotificationChannel,
  NotificationPreferenceItem
} from '../../../core/models/gym.models';

@Component({
  selector: 'app-notification-preferences',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-4xl mx-auto space-y-6">
        <!-- Header -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <div class="flex items-center gap-2 text-xs uppercase tracking-widest font-semibold text-cyan-400">
              <span>⚙️</span> Configuración de Cuenta
            </div>
            <h1 class="text-2xl sm:text-3xl font-extrabold text-white mt-1">Preferencias de Notificación</h1>
            <p class="text-sm text-slate-400">
              Elegí cómo querés recibir cada tipo de comunicación. Los avisos transaccionales en la aplicación y las alertas de seguridad son obligatorios.
            </p>
          </div>
          <a routerLink="/portal/notificaciones"
             class="text-xs font-bold bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 px-3 py-2 rounded-lg transition self-start sm:self-center">
            ← Volver a Notificaciones
          </a>
        </div>

        @if (feedbackMessage()) {
          <div class="p-4 rounded-xl text-sm font-semibold"
               [ngClass]="feedbackSuccess() ? 'bg-emerald-950/60 border border-emerald-800 text-emerald-300' : 'bg-rose-950/60 border border-rose-800 text-rose-300'">
            {{ feedbackMessage() }}
          </div>
        }

        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg space-y-4">
          <div class="overflow-x-auto">
            <table class="w-full text-left text-sm">
              <thead class="text-xs text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase">
                <tr>
                  <th class="py-3 px-4">Categoría</th>
                  <th class="py-3 px-4">Canal</th>
                  <th class="py-3 px-4">Tipo</th>
                  <th class="py-3 px-4 text-right">Estado</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-800/80">
                @for (pref of preferences(); track pref.category + '-' + pref.channel) {
                  <tr class="hover:bg-slate-800/30 transition">
                    <td class="py-3 px-4 font-semibold text-slate-100">{{ getCategoryLabel(pref.category) }}</td>
                    <td class="py-3 px-4 text-slate-300">{{ getChannelLabel(pref.channel) }}</td>
                    <td class="py-3 px-4">
                      <span class="text-xs px-2 py-0.5 rounded font-semibold"
                            [ngClass]="pref.isTransactional ? 'bg-cyan-500/20 text-cyan-300' : 'bg-amber-500/20 text-amber-300'">
                        {{ pref.isTransactional ? 'Transaccional' : 'Promocional' }}
                      </span>
                    </td>
                    <td class="py-3 px-4 text-right">
                      @if (pref.isLocked) {
                        <span class="text-xs text-slate-500 font-semibold">🔒 Obligatorio (Activo)</span>
                      } @else {
                        <button
                          type="button"
                          (click)="togglePreference(pref)"
                          [disabled]="saving()"
                          class="text-xs font-bold px-3 py-1 rounded-lg border transition"
                          [ngClass]="pref.isEnabled
                            ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40 hover:bg-emerald-500/30'
                            : 'bg-slate-800 text-slate-400 border-slate-700 hover:bg-slate-700'">
                          {{ pref.isEnabled ? 'Habilitado' : 'Deshabilitado' }}
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  `
})
export class NotificationPreferencesComponent implements OnInit {
  private readonly notificationsService = inject(NotificationsService);

  readonly preferences = signal<NotificationPreferenceItem[]>([]);
  readonly saving = signal(false);
  readonly feedbackMessage = signal<string | null>(null);
  readonly feedbackSuccess = signal(true);

  ngOnInit(): void {
    this.notificationsService.getPreferences().subscribe({
      next: (prefs) => this.preferences.set(prefs),
      error: () => this.preferences.set([])
    });
  }

  togglePreference(pref: NotificationPreferenceItem): void {
    if (pref.isLocked) return;
    this.saving.set(true);
    this.notificationsService
      .updatePreferences({
        preferences: [
          {
            category: pref.category,
            channel: pref.channel,
            isEnabled: !pref.isEnabled
          }
        ]
      })
      .subscribe({
        next: (updated) => {
          this.saving.set(false);
          this.preferences.set(updated);
          this.showFeedback('Preferencias actualizadas correctamente.', true);
        },
        error: (err) => {
          this.saving.set(false);
          this.showFeedback(err.error?.message || 'No se pudo actualizar la preferencia.', false);
        }
      });
  }

  getCategoryLabel(category: NotificationCategory): string {
    switch (category) {
      case NotificationCategory.Reservations: return 'Reservas y Lista de Espera';
      case NotificationCategory.Classes: return 'Clases y Cambios de Horario';
      case NotificationCategory.Memberships: return 'Membresías y Créditos';
      case NotificationCategory.Payments: return 'Pagos y Reembolsos';
      case NotificationCategory.Promotions: return 'Promociones y Novedades';
      case NotificationCategory.Operational: return 'Avisos Operativos';
      case NotificationCategory.Security: return 'Seguridad de la Cuenta';
      default: return 'General';
    }
  }

  getChannelLabel(channel: NotificationChannel): string {
    switch (channel) {
      case NotificationChannel.InApp: return 'En la aplicación (In-App)';
      case NotificationChannel.Email: return 'Correo electrónico (Email)';
      case NotificationChannel.WhatsApp: return 'WhatsApp';
      case NotificationChannel.Push: return 'Push';
      default: return 'Canal';
    }
  }

  private showFeedback(msg: string, success: boolean): void {
    this.feedbackMessage.set(msg);
    this.feedbackSuccess.set(success);
    setTimeout(() => this.feedbackMessage.set(null), 4000);
  }
}
