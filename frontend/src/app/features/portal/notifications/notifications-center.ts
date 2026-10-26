import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { NotificationsService } from '../../../core/services/notifications.service';
import { NotificationCategory, NotificationItem } from '../../../core/models/gym.models';
import { AppDatePipe } from '../../../shared/pipes/app-date.pipe';

@Component({
  selector: 'app-notifications-center',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AppDatePipe],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-5xl mx-auto space-y-6">
        <!-- Header -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <div class="flex items-center gap-2 text-xs uppercase tracking-widest font-semibold text-cyan-400">
              <span>🔔</span> Centro de Comunicaciones
            </div>
            <h1 class="text-2xl sm:text-3xl font-extrabold text-white mt-1">Mis Notificaciones</h1>
            <p class="text-sm text-slate-400">Consultá tus avisos de reservas, clases, membresías, pagos y promociones.</p>
          </div>
          <div class="flex items-center gap-3">
            <a routerLink="/portal/notificaciones/preferencias"
               class="text-xs font-bold bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 px-3 py-2 rounded-lg transition">
              ⚙️ Preferencias
            </a>
            @if (notificationsService.unreadCount() > 0) {
              <button (click)="markAllRead()"
                      class="text-xs font-bold bg-cyan-500 hover:bg-cyan-400 text-slate-950 px-3 py-2 rounded-lg transition">
                Marcar todas como leídas ({{ notificationsService.unreadCount() }})
              </button>
            }
          </div>
        </div>

        <!-- Filters -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-4 flex flex-wrap items-center gap-3">
          <div class="flex items-center gap-2">
            <label class="text-xs text-slate-400 font-semibold">Estado:</label>
            <select [(ngModel)]="readFilter" (change)="applyFilters()"
                    class="bg-slate-950 border border-slate-700 text-xs text-slate-200 rounded-lg px-3 py-1.5">
              <option value="all">Todas</option>
              <option value="unread">No leídas</option>
              <option value="read">Leídas</option>
            </select>
          </div>

          <div class="flex items-center gap-2">
            <label class="text-xs text-slate-400 font-semibold">Categoría:</label>
            <select [(ngModel)]="categoryFilter" (change)="applyFilters()"
                    class="bg-slate-950 border border-slate-700 text-xs text-slate-200 rounded-lg px-3 py-1.5">
              <option value="">Todas las categorías</option>
              <option [value]="NotificationCategory.Reservations">Reservas</option>
              <option [value]="NotificationCategory.Classes">Clases</option>
              <option [value]="NotificationCategory.Memberships">Membresías y Créditos</option>
              <option [value]="NotificationCategory.Payments">Pagos</option>
              <option [value]="NotificationCategory.Promotions">Promociones</option>
              <option [value]="NotificationCategory.Operational">Operativas</option>
              <option [value]="NotificationCategory.Security">Seguridad</option>
            </select>
          </div>
        </div>

        <!-- Notifications List -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl shadow-lg divide-y divide-slate-800">
          @if (items().length === 0) {
            <div class="p-10 text-center text-sm text-slate-500">
              No se encontraron notificaciones con los filtros seleccionados.
            </div>
          } @else {
            @for (item of items(); track item.id) {
              <div class="p-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4 transition"
                   [ngClass]="item.isRead ? 'bg-slate-900/40 text-slate-400' : 'bg-cyan-950/20 text-slate-100'">
                <div class="space-y-1">
                  <div class="flex items-center gap-2">
                    @if (!item.isRead) {
                      <span class="h-2 w-2 rounded-full bg-cyan-400"></span>
                    }
                    <span class="font-bold text-sm text-white">{{ item.title }}</span>
                    <span class="text-[11px] bg-slate-800 text-slate-300 px-2 py-0.5 rounded border border-slate-700">
                      {{ getCategoryLabel(item.category) }}
                    </span>
                  </div>
                  <p class="text-xs text-slate-300">{{ item.body || item.message }}</p>
                  <div class="text-[11px] text-slate-500">
                    Enviada: {{ (item.sentAtUtc || item.createdAtUtc) | appDate:'time' }}
                    @if (item.readAtUtc) {
                      <span> · Leída: {{ item.readAtUtc | appDate:'time' }}</span>
                    }
                  </div>
                </div>

                <div class="flex items-center gap-2 self-start sm:self-center">
                  @if (item.actionUrl && notificationsService.isSafeInternalUrl(item.actionUrl)) {
                    <button (click)="navigateToAction(item)"
                            class="text-xs font-bold bg-cyan-500/20 hover:bg-cyan-500/30 text-cyan-300 border border-cyan-500/30 px-3 py-1.5 rounded-lg transition">
                      Ir al detalle
                    </button>
                  }
                  @if (!item.isRead) {
                    <button (click)="toggleRead(item, true)"
                            class="text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-200 px-3 py-1.5 rounded-lg border border-slate-700 transition">
                      Marcar leída
                    </button>
                  } @else {
                    <button (click)="toggleRead(item, false)"
                            class="text-xs font-semibold bg-slate-950 hover:bg-slate-800 text-slate-400 px-3 py-1.5 rounded-lg border border-slate-800 transition">
                      Marcar no leída
                    </button>
                  }
                </div>
              </div>
            }
          }
        </div>

        <!-- Pagination -->
        @if (totalPages() > 1) {
          <div class="flex items-center justify-between text-xs text-slate-400">
            <span>Página {{ pageNumber() }} de {{ totalPages() }} ({{ totalCount() }} notificaciones)</span>
            <div class="flex items-center gap-2">
              <button (click)="changePage(pageNumber() - 1)" [disabled]="pageNumber() <= 1"
                      class="px-3 py-1.5 rounded bg-slate-900 border border-slate-800 disabled:opacity-40">
                Anterior
              </button>
              <button (click)="changePage(pageNumber() + 1)" [disabled]="pageNumber() >= totalPages()"
                      class="px-3 py-1.5 rounded bg-slate-900 border border-slate-800 disabled:opacity-40">
                Siguiente
              </button>
            </div>
          </div>
        }
      </div>
    </div>
  `
})
export class NotificationsCenterComponent implements OnInit {
  readonly notificationsService = inject(NotificationsService);
  private readonly router = inject(Router);

  readonly NotificationCategory = NotificationCategory;

  readonly items = signal<NotificationItem[]>([]);
  readonly pageNumber = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);

  readFilter: 'all' | 'unread' | 'read' = 'all';
  categoryFilter = '';

  ngOnInit(): void {
    this.notificationsService.refreshUnreadCount().subscribe({ error: () => {} });
    this.loadPage(1);
  }

  applyFilters(): void {
    this.loadPage(1);
  }

  changePage(nextPage: number): void {
    if (nextPage < 1 || nextPage > this.totalPages()) return;
    this.loadPage(nextPage);
  }

  loadPage(page: number): void {
    const isRead =
      this.readFilter === 'unread' ? false : this.readFilter === 'read' ? true : undefined;
    const category = this.categoryFilter ? (Number(this.categoryFilter) as NotificationCategory) : undefined;

    this.notificationsService
      .getNotifications({
        isRead,
        category,
        pageNumber: page,
        pageSize: 15
      })
      .subscribe({
        next: (res) => {
          this.items.set(res.items);
          this.pageNumber.set(res.pageNumber);
          this.totalPages.set(res.totalPages);
          this.totalCount.set(res.totalCount);
        },
        error: () => this.items.set([])
      });
  }

  toggleRead(item: NotificationItem, markRead: boolean): void {
    const obs = markRead
      ? this.notificationsService.markAsRead(item.id)
      : this.notificationsService.markAsUnread(item.id);

    obs.subscribe({
      next: (updated) => {
        this.items.update((list) => list.map((n) => (n.id === item.id ? updated : n)));
      }
    });
  }

  markAllRead(): void {
    this.notificationsService.markAllAsRead().subscribe({
      next: () => {
        this.items.update((list) => list.map((n) => ({ ...n, isRead: true })));
      }
    });
  }

  navigateToAction(item: NotificationItem): void {
    if (!item.isRead) {
      this.notificationsService.markAsRead(item.id).subscribe({ error: () => {} });
    }
    if (this.notificationsService.isSafeInternalUrl(item.actionUrl)) {
      this.router.navigateByUrl(item.actionUrl!);
    }
  }

  getCategoryLabel(category?: NotificationCategory): string {
    switch (category) {
      case NotificationCategory.Reservations: return 'Reservas';
      case NotificationCategory.Classes: return 'Clases';
      case NotificationCategory.Memberships: return 'Membresías';
      case NotificationCategory.Payments: return 'Pagos';
      case NotificationCategory.Promotions: return 'Promociones';
      case NotificationCategory.Operational: return 'Operativa';
      case NotificationCategory.Security: return 'Seguridad';
      default: return 'General';
    }
  }
}
