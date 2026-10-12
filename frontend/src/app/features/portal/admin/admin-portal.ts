import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { GymApiService } from '../../../core/services/gym-api.service';
import { NotificationsService } from '../../../core/services/notifications.service';
import {
  DashboardMetrics,
  AdminUserOverview,
  Promotion,
  PromotionBenefitType,
  PromotionStatus,
  AuditLog,
  NotificationDeliveryItem,
  NotificationTemplateItem,
  NotificationTemplateStatus,
  NotificationChannel,
  NotificationType
} from '../../../core/models/gym.models';

@Component({
  selector: 'app-admin-portal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-8">

        <!-- Header -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <div class="flex items-center gap-3">
              <span class="h-3 w-3 rounded-full bg-amber-500 animate-pulse"></span>
              <span class="text-xs uppercase tracking-widest font-semibold text-amber-400">Panel de Control General</span>
            </div>
            <h1 class="text-3xl font-extrabold text-white mt-1">Administración SmartGym</h1>
            <p class="text-sm text-slate-400">KPIs en tiempo real, gestión de usuarios, auditoría, promociones y notificaciones</p>
          </div>

          <!-- Navigation Tabs -->
          <div class="flex flex-wrap gap-2 bg-slate-950 p-1.5 rounded-xl border border-slate-800">
            <button (click)="activeTab.set('kpis')"
                    class="px-4 py-2 rounded-lg text-xs font-bold transition"
                    [ngClass]="activeTab() === 'kpis' ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white'">
              📊 Métricas
            </button>
            <button (click)="activeTab.set('users')"
                    class="px-4 py-2 rounded-lg text-xs font-bold transition"
                    [ngClass]="activeTab() === 'users' ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white'">
              👥 Usuarios ({{ users().length }})
            </button>
            <button (click)="activeTab.set('promotions')"
                    class="px-4 py-2 rounded-lg text-xs font-bold transition"
                    [ngClass]="activeTab() === 'promotions' ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white'">
              🏷️ Promociones ({{ promotions().length }})
            </button>
            <button (click)="activeTab.set('notifications')"
                    class="px-4 py-2 rounded-lg text-xs font-bold transition"
                    [ngClass]="activeTab() === 'notifications' ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white'">
              🔔 Notificaciones ({{ failedDeliveries().length }})
            </button>
            <button (click)="activeTab.set('audit')"
                    class="px-4 py-2 rounded-lg text-xs font-bold transition"
                    [ngClass]="activeTab() === 'audit' ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white'">
              🛡️ Auditoría
            </button>
          </div>
        </div>

        <!-- TAB 1: KPIS & METRICS -->
        @if (activeTab() === 'kpis') {
          <div class="space-y-6">
            <!-- Key Metric Cards Grid -->
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">

              <!-- Card 1 -->
              <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
                <span class="text-xs uppercase font-semibold text-slate-400">Alumnos Activos</span>
                <div class="text-3xl font-black text-white mt-2">{{ metrics()?.totalActiveStudents ?? 0 }}</div>
                <div class="text-xs text-emerald-400 mt-2 flex items-center gap-1">
                  <span>●</span> Membresías al día
                </div>
              </div>

              <!-- Card 2 -->
              <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
                <span class="text-xs uppercase font-semibold text-slate-400">Asistencia Hoy</span>
                <div class="text-3xl font-black text-cyan-400 mt-2">{{ metrics()?.attendanceRateTodayPercent ?? 0 }}%</div>
                <div class="text-xs text-slate-400 mt-2">
                  {{ metrics()?.totalAttendanceToday ?? 0 }} de {{ metrics()?.totalReservationsToday ?? 0 }} reservas
                </div>
              </div>

              <!-- Card 3 -->
              <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
                <span class="text-xs uppercase font-semibold text-slate-400">Ingresos del Mes</span>
                <div class="text-3xl font-black text-emerald-400 mt-2">\${{ metrics()?.monthlyRevenue ?? 0 | number:'1.2-2' }}</div>
                <div class="text-xs text-slate-400 mt-2">Facturación consolidada</div>
              </div>

              <!-- Card 4 -->
              <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
                <span class="text-xs uppercase font-semibold text-slate-400">Aptos Pendientes</span>
                <div class="text-3xl font-black text-rose-400 mt-2">{{ metrics()?.pendingMedicalCertificatesCount ?? 0 }}</div>
                <div class="text-xs text-rose-400/80 mt-2">Alumnos sin certificado vigente</div>
              </div>

            </div>

            <!-- Secondary Metrics Bar -->
            <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
              <div class="bg-slate-900/60 border border-slate-800 rounded-xl p-5 flex items-center justify-between">
                <div>
                  <div class="text-xs text-slate-400">Instructores en Nómina</div>
                  <div class="text-xl font-bold text-white mt-1">{{ metrics()?.totalActiveInstructors ?? 0 }}</div>
                </div>
                <span class="text-2xl">🏋️</span>
              </div>
              <div class="bg-slate-900/60 border border-slate-800 rounded-xl p-5 flex items-center justify-between">
                <div>
                  <div class="text-xs text-slate-400">Clases Programadas Hoy</div>
                  <div class="text-xl font-bold text-white mt-1">{{ metrics()?.totalClassesToday ?? 0 }}</div>
                </div>
                <span class="text-2xl">⏱️</span>
              </div>
              <div class="bg-slate-900/60 border border-slate-800 rounded-xl p-5 flex items-center justify-between">
                <div>
                  <div class="text-xs text-slate-400">Planes a Vencer (7 días)</div>
                  <div class="text-xl font-bold text-amber-400 mt-1">{{ metrics()?.expiringMembershipsNext7Days ?? 0 }}</div>
                </div>
                <span class="text-2xl">⏳</span>
              </div>
            </div>
          </div>
        }

        <!-- TAB 2: USERS & ROLES -->
        @if (activeTab() === 'users') {
          <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl space-y-4">
            <div class="flex items-center justify-between">
              <h2 class="text-xl font-bold text-white">Directorio de Personas y Usuarios</h2>
              <button (click)="loadUsers()" class="text-xs text-amber-400 hover:text-amber-300">Actualizar Listado</button>
            </div>

            <div class="overflow-x-auto">
              <table class="w-full text-left text-sm">
                <thead class="text-xs text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase">
                  <tr>
                    <th class="py-3 px-4">Nombre y Apellido</th>
                    <th class="py-3 px-4">Email</th>
                    <th class="py-3 px-4">Documento</th>
                    <th class="py-3 px-4">Roles Asignados</th>
                    <th class="py-3 px-4 text-center">Estado</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-slate-800/80">
                  @for (u of users(); track u.userId) {
                    <tr class="hover:bg-slate-800/30 transition">
                      <td class="py-3 px-4 font-semibold text-slate-100">{{ u.fullName }}</td>
                      <td class="py-3 px-4 text-slate-400 text-xs">{{ u.email }}</td>
                      <td class="py-3 px-4 text-slate-400 text-xs">{{ u.documentNumber ?? u.dni }}</td>
                      <td class="py-3 px-4">
                        <div class="flex flex-wrap gap-1">
                          @for (r of u.roles; track r) {
                            <span class="text-[11px] bg-slate-800 text-slate-300 px-2 py-0.5 rounded font-medium border border-slate-700">
                              {{ r }}
                            </span>
                          }
                        </div>
                      </td>
                      <td class="py-3 px-4 text-center">
                        <span class="text-xs px-2 py-0.5 rounded font-bold"
                              [ngClass]="u.isActive ? 'bg-emerald-500/20 text-emerald-400' : 'bg-rose-500/20 text-rose-400'">
                          {{ u.isActive ? 'Activo' : 'Inactivo' }}
                        </span>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        }

        <!-- TAB 3: PROMOTIONS -->
        @if (activeTab() === 'promotions') {
          <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl space-y-4">
            <div class="flex items-center justify-between">
              <h2 class="text-xl font-bold text-white">Catálogo de Promociones y Descuentos</h2>
              <span class="text-xs text-slate-400">Motor de beneficios automáticos y cupones</span>
            </div>

            @if (promotions().length === 0) {
              <div class="text-center py-8 text-slate-500 text-sm">
                No hay promociones registradas.
              </div>
            } @else {
              <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                @for (p of promotions(); track p.id) {
                  <div class="bg-slate-950/70 border border-slate-800 rounded-xl p-5 space-y-3">
                    <div class="flex items-center justify-between">
                      <span class="font-bold text-white text-base">{{ p.name }}</span>
                      <span class="text-xs px-2 py-0.5 rounded font-bold"
                            [ngClass]="p.status === PromotionStatus.Active ? 'bg-emerald-500/20 text-emerald-400' : 'bg-slate-800 text-slate-500'">
                        {{ promotionStatusLabel(p.status) }}
                      </span>
                    </div>

                    @if (p.code) {
                      <div class="text-xs bg-slate-900 border border-slate-800 rounded px-2.5 py-1 text-amber-400 font-mono inline-block">
                        Código: <strong>{{ p.code }}</strong>
                      </div>
                    }

                    <div class="text-xs text-slate-400 space-y-1">
                      <p>Beneficio: <strong class="text-slate-200">{{ promotionBenefitLabel(p) }}</strong></p>
                      <p>Canjes realizados: <strong class="text-slate-200">{{ p.currentUsesTotal }}</strong> {{ p.maxUsesTotal ? '/ ' + p.maxUsesTotal : '' }}</p>
                      <p>Vigencia: {{ p.validFrom | date:'shortDate' }}{{ p.validTo ? ' al ' + (p.validTo | date:'shortDate') : '' }}</p>
                    </div>
                  </div>
                }
              </div>
            }
          </div>
        }

        <!-- TAB 4: NOTIFICATIONS & TEMPLATES -->
        @if (activeTab() === 'notifications') {
          <div class="space-y-6">
            @if (notificationFeedback()) {
              <div class="p-4 rounded-xl text-xs font-semibold bg-amber-950/60 border border-amber-800 text-amber-300">
                {{ notificationFeedback() }}
              </div>
            }

            <!-- Failed Deliveries -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl space-y-4">
              <div class="flex items-center justify-between">
                <div>
                  <h2 class="text-xl font-bold text-white">Entregas Fallidas</h2>
                  <p class="text-xs text-slate-400">Comunicaciones externas con error que permiten reintento manual auditado</p>
                </div>
                <button (click)="loadFailedDeliveries()" class="text-xs text-amber-400 hover:text-amber-300">Actualizar</button>
              </div>

              @if (failedDeliveries().length === 0) {
                <div class="text-center py-6 text-slate-500 text-sm">
                  No hay entregas fallidas pendientes de revisión.
                </div>
              } @else {
                <div class="overflow-x-auto">
                  <table class="w-full text-left text-xs">
                    <thead class="text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase">
                      <tr>
                        <th class="py-3 px-4">Canal / Proveedor</th>
                        <th class="py-3 px-4">Destinatario</th>
                        <th class="py-3 px-4">Intentos</th>
                        <th class="py-3 px-4">Error</th>
                        <th class="py-3 px-4 text-right">Acción</th>
                      </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-800/80">
                      @for (d of failedDeliveries(); track d.id) {
                        <tr class="hover:bg-slate-800/30 transition">
                          <td class="py-2.5 px-4 text-slate-200 font-semibold">{{ d.providerName || '-' }} ({{ getChannelLabel(d.channel) }})</td>
                          <td class="py-2.5 px-4 text-slate-400">{{ d.destination || '-' }}</td>
                          <td class="py-2.5 px-4 text-slate-400">{{ d.attemptCount }} / {{ d.maxAttempts }}</td>
                          <td class="py-2.5 px-4 text-rose-300">{{ d.errorCode }}: {{ d.errorMessage }}</td>
                          <td class="py-2.5 px-4 text-right">
                            <button (click)="retryFailedDelivery(d)"
                                    class="text-xs font-bold bg-amber-500 hover:bg-amber-400 text-slate-950 px-3 py-1 rounded-lg transition">
                              Reintentar
                            </button>
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>

            <!-- Notification Templates -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl space-y-4">
              <div class="flex items-center justify-between">
                <div>
                  <h2 class="text-xl font-bold text-white">Plantillas de Notificación ({{ notificationTemplates().length }})</h2>
                  <p class="text-xs text-slate-400">Gestión versionada de plantillas In-App y Email</p>
                </div>
                <button (click)="loadNotificationTemplates()" class="text-xs text-amber-400 hover:text-amber-300">Recargar Plantillas</button>
              </div>

              @if (notificationTemplates().length === 0) {
                <div class="text-center py-6 text-slate-500 text-sm">
                  No hay plantillas cargadas.
                </div>
              } @else {
                <div class="overflow-x-auto max-h-96">
                  <table class="w-full text-left text-xs">
                    <thead class="text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase sticky top-0">
                      <tr>
                        <th class="py-3 px-4">Código</th>
                        <th class="py-3 px-4">Canal</th>
                        <th class="py-3 px-4">Versión</th>
                        <th class="py-3 px-4">Asunto / Vista Previa</th>
                        <th class="py-3 px-4">Estado</th>
                        <th class="py-3 px-4 text-right">Acción</th>
                      </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-800/80">
                      @for (tpl of notificationTemplates(); track tpl.id) {
                        <tr class="hover:bg-slate-800/30 transition">
                          <td class="py-2.5 px-4 font-mono text-amber-300 font-semibold">{{ tpl.code }}</td>
                          <td class="py-2.5 px-4 text-slate-300">{{ getChannelLabel(tpl.channel) }}</td>
                          <td class="py-2.5 px-4 text-slate-400">v{{ tpl.version }} ({{ tpl.locale }})</td>
                          <td class="py-2.5 px-4 text-slate-300 max-w-xs truncate">{{ tpl.subjectTemplate || tpl.bodyTemplate }}</td>
                          <td class="py-2.5 px-4">
                            <span class="px-2 py-0.5 rounded font-bold"
                                  [ngClass]="tpl.status === NotificationTemplateStatus.Active ? 'bg-emerald-500/20 text-emerald-400' : 'bg-slate-800 text-slate-400'">
                              {{ tpl.status === NotificationTemplateStatus.Active ? 'Activa' : (tpl.status === NotificationTemplateStatus.Draft ? 'Borrador' : 'Inactiva') }}
                            </span>
                          </td>
                          <td class="py-2.5 px-4 text-right">
                            @if (tpl.status === NotificationTemplateStatus.Active) {
                              <button (click)="toggleTemplateStatus(tpl, false)"
                                      class="text-xs bg-slate-800 hover:bg-slate-700 text-slate-300 px-2.5 py-1 rounded border border-slate-700">
                                Desactivar
                              </button>
                            } @else {
                              <button (click)="toggleTemplateStatus(tpl, true)"
                                      class="text-xs bg-emerald-500/20 hover:bg-emerald-500/30 text-emerald-300 px-2.5 py-1 rounded border border-emerald-500/30">
                                Activar
                              </button>
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </div>
          </div>
        }

        <!-- TAB 5: AUDIT LOGS -->
        @if (activeTab() === 'audit') {
          <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl space-y-4">
            <div class="flex items-center justify-between">
              <h2 class="text-xl font-bold text-white">Registro Global de Auditoría</h2>
              <button (click)="loadAuditLogs()" class="text-xs text-amber-400 hover:text-amber-300">Recargar Logs</button>
            </div>

            @if (auditLogs().length === 0) {
              <div class="text-center py-8 text-slate-500 text-sm">
                No hay eventos de auditoría recientes.
              </div>
            } @else {
              <div class="overflow-x-auto">
                <table class="w-full text-left text-xs">
                  <thead class="text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase">
                    <tr>
                      <th class="py-3 px-4">Fecha y Hora</th>
                      <th class="py-3 px-4">Acción</th>
                      <th class="py-3 px-4">Entidad</th>
                      <th class="py-3 px-4">Usuario</th>
                      <th class="py-3 px-4">Detalles</th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-slate-800/80">
                    @for (log of auditLogs(); track log.id) {
                      <tr class="hover:bg-slate-800/30 transition">
                        <td class="py-2.5 px-4 text-slate-400 whitespace-nowrap">{{ log.timestampUtc | date:'medium' }}</td>
                        <td class="py-2.5 px-4 font-bold text-amber-300">{{ log.action }}</td>
                        <td class="py-2.5 px-4 text-slate-300">{{ log.entityName }}</td>
                        <td class="py-2.5 px-4 text-slate-400">{{ log.performedByEmail || 'Sistema / Anónimo' }}</td>
                        <td class="py-2.5 px-4 text-slate-400 truncate max-w-xs">{{ log.details || '-' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        }

      </div>
    </div>
  `
})
export class AdminPortalComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly gymApi = inject(GymApiService);
  private readonly notificationsService = inject(NotificationsService);

  activeTab = signal<'kpis' | 'users' | 'promotions' | 'notifications' | 'audit'>('kpis');

  metrics = signal<DashboardMetrics | null>(null);
  users = signal<AdminUserOverview[]>([]);
  promotions = signal<Promotion[]>([]);
  auditLogs = signal<AuditLog[]>([]);
  failedDeliveries = signal<NotificationDeliveryItem[]>([]);
  notificationTemplates = signal<NotificationTemplateItem[]>([]);
  notificationFeedback = signal<string | null>(null);

  readonly PromotionStatus = PromotionStatus;
  readonly NotificationTemplateStatus = NotificationTemplateStatus;
  readonly NotificationChannel = NotificationChannel;
  readonly NotificationType = NotificationType;

  promotionStatusLabel(status: PromotionStatus): string {
    switch (status) {
      case PromotionStatus.Active: return 'Activa';
      case PromotionStatus.Paused: return 'Pausada';
      default: return 'Archivada';
    }
  }

  promotionBenefitLabel(p: Promotion): string {
    switch (p.benefitType) {
      case PromotionBenefitType.PercentageDiscount: return `${p.benefitValue}% de descuento`;
      case PromotionBenefitType.FixedAmountDiscount: return `$${p.benefitValue} de descuento`;
      default: return `${p.benefitValue} créditos extra`;
    }
  }

  ngOnInit(): void {
    this.loadMetrics();
    this.loadUsers();
    this.loadPromotions();
    this.loadFailedDeliveries();
    this.loadNotificationTemplates();
    this.loadAuditLogs();
  }

  loadMetrics(): void {
    this.gymApi.getDashboardMetrics().subscribe({
      next: (m) => this.metrics.set(m),
      error: () => this.metrics.set(null)
    });
  }

  loadUsers(): void {
    this.gymApi.getAdminUsers().subscribe({
      next: (data) => this.users.set(data),
      error: () => this.users.set([])
    });
  }

  loadPromotions(): void {
    this.gymApi.getPromotions(false).subscribe({
      next: (data) => this.promotions.set(data),
      error: () => this.promotions.set([])
    });
  }

  loadFailedDeliveries(): void {
    this.notificationsService.getFailedDeliveries().subscribe({
      next: (res) => this.failedDeliveries.set(res.items ?? []),
      error: () => this.failedDeliveries.set([])
    });
  }

  loadNotificationTemplates(): void {
    this.notificationsService.getTemplates().subscribe({
      next: (items) => this.notificationTemplates.set(items ?? []),
      error: () => this.notificationTemplates.set([])
    });
  }

  getChannelLabel(channel: NotificationChannel): string {
    switch (channel) {
      case NotificationChannel.Email: return 'Email';
      case NotificationChannel.InApp: return 'InApp';
      case NotificationChannel.WhatsApp: return 'WhatsApp';
      case NotificationChannel.Push: return 'Push';
      default: return 'Canal';
    }
  }

  retryFailedDelivery(delivery: NotificationDeliveryItem): void {
    this.notificationsService.retryDelivery(delivery.id, 'Reintento manual desde panel de administración').subscribe({
      next: () => {
        this.notificationFeedback.set('Entrega reprogramada en estado Pending.');
        this.loadFailedDeliveries();
        this.loadAuditLogs();
      },
      error: (err) => {
        this.notificationFeedback.set(err.error?.message || 'No se pudo reintentar la entrega.');
      }
    });
  }

  toggleTemplateStatus(template: NotificationTemplateItem, activate: boolean): void {
    const obs = activate
      ? this.notificationsService.activateTemplate(template.id)
      : this.notificationsService.deactivateTemplate(template.id);

    obs.subscribe({
      next: () => {
        this.notificationFeedback.set(activate ? 'Plantilla activada.' : 'Plantilla desactivada.');
        this.loadNotificationTemplates();
        this.loadAuditLogs();
      },
      error: (err) => {
        this.notificationFeedback.set(err.error?.message || 'Error al cambiar el estado de la plantilla.');
      }
    });
  }

  loadAuditLogs(): void {
    this.gymApi.getAuditLogs(50).subscribe({
      next: (data) => this.auditLogs.set(data),
      error: () => this.auditLogs.set([])
    });
  }

  getMembershipStatusText(mem: { status: number; effectiveStatus?: number }): string {
    const effective = mem.effectiveStatus ?? mem.status;
    switch (effective) {
      case 0: return 'Pendiente';
      case 1: return 'Activa';
      case 2: return 'Vencida';
      case 3: return 'Suspendida';
      case 4: return 'Cancelada';
      default: return 'Desconocido';
    }
  }
}
