import { Component, effect, inject, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationsService } from '../../../core/services/notifications.service';
import { NotificationItem } from '../../../core/models/gym.models';
import { AppDatePipe } from '../../pipes/app-date.pipe';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, AppDatePipe],
  template: `
    <nav class="bg-slate-900 border-b border-slate-800 text-white sticky top-0 z-50 shadow-md">
      <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div class="flex justify-between h-16 items-center">
          <!-- Logo & Brand -->
          <div class="flex items-center space-x-3">
            <a routerLink="/" class="flex items-center space-x-2">
              <span class="text-2xl font-black tracking-wider text-cyan-400">SMART<span class="text-white">GYM</span></span>
            </a>
          </div>

          <!-- Navigation Links -->
          <div class="hidden md:flex space-x-6 items-center font-medium">
            <a routerLink="/" routerLinkActive="text-cyan-400 font-semibold" [routerLinkActiveOptions]="{exact: true}" class="text-slate-300 hover:text-white transition">Inicio</a>
            <a routerLink="/actividades" routerLinkActive="text-cyan-400 font-semibold" class="text-slate-300 hover:text-white transition">Actividades</a>
            <a routerLink="/horarios" routerLinkActive="text-cyan-400 font-semibold" class="text-slate-300 hover:text-white transition">Horarios</a>
            <a routerLink="/planes" routerLinkActive="text-cyan-400 font-semibold" class="text-slate-300 hover:text-white transition">Planes</a>

            @if (authService.isAuthenticated()) {
              <a routerLink="/portal/mis-datos" routerLinkActive="text-cyan-400 font-semibold" class="text-slate-300 hover:text-white transition">Mis Datos</a>
              @if (authService.isStaff()) {
                <a routerLink="/portal/personas" routerLinkActive="text-cyan-400 font-semibold" class="text-slate-300 hover:text-white transition">Personas</a>
              }
              @if (authService.isStudent()) {
                <a routerLink="/portal/alumno" routerLinkActive="text-cyan-400 font-semibold" class="bg-cyan-500/20 text-cyan-300 px-3 py-1.5 rounded-lg border border-cyan-500/30 hover:bg-cyan-500/30 transition">Mi Portal</a>
              }
              @if (authService.isInstructor() || authService.isSecretary()) {
                <a routerLink="/portal/docente" routerLinkActive="text-cyan-400 font-semibold" class="bg-emerald-500/20 text-emerald-300 px-3 py-1.5 rounded-lg border border-emerald-500/30 hover:bg-emerald-500/30 transition">Recepción & Sala</a>
              }
              @if (authService.isAdmin()) {
                <a routerLink="/portal/admin" routerLinkActive="text-cyan-400 font-semibold" class="bg-amber-500/20 text-amber-300 px-3 py-1.5 rounded-lg border border-amber-500/30 hover:bg-amber-500/30 transition">Admin Dashboard</a>
              }
            }
          </div>

          <!-- User / Auth Buttons -->
          <div class="flex items-center space-x-3 relative">
            @if (authService.isAuthenticated()) {
              <!-- Notifications Bell -->
              <div class="relative">
                <button
                  type="button"
                  data-testid="notifications-bell-btn"
                  (click)="toggleDropdown()"
                  class="relative p-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white border border-slate-700 transition"
                  aria-label="Notificaciones">
                  <span>🔔</span>
                  @if (notificationsService.unreadCount() > 0) {
                    <span
                      data-testid="notifications-unread-badge"
                      class="absolute -top-1.5 -right-1.5 min-w-[20px] h-5 px-1 rounded-full bg-cyan-500 text-slate-950 text-[11px] font-black flex items-center justify-center">
                      {{ notificationsService.unreadCount() }}
                    </span>
                  }
                </button>

                @if (isDropdownOpen()) {
                  <div
                    data-testid="notifications-dropdown"
                    class="absolute right-0 mt-2 w-80 sm:w-96 bg-slate-900 border border-slate-700 rounded-2xl shadow-2xl overflow-hidden z-50">
                    <div class="px-4 py-3 border-b border-slate-800 flex items-center justify-between bg-slate-950/60">
                      <span class="font-bold text-sm text-white">Notificaciones</span>
                      @if (notificationsService.unreadCount() > 0) {
                        <button
                          type="button"
                          data-testid="notifications-mark-all-btn"
                          (click)="markAllAsRead()"
                          class="text-xs text-cyan-400 hover:text-cyan-300 font-semibold">
                          Marcar todas como leídas
                        </button>
                      }
                    </div>

                    <div class="max-h-80 overflow-y-auto divide-y divide-slate-800/80">
                      @if (notificationsService.recentNotifications().length === 0) {
                        <div class="px-4 py-6 text-center text-xs text-slate-400">
                          No tenés notificaciones recientes.
                        </div>
                      } @else {
                        @for (notif of notificationsService.recentNotifications(); track notif.id) {
                          <div
                            (click)="openNotification(notif)"
                            class="px-4 py-3 cursor-pointer hover:bg-slate-800/60 transition"
                            [ngClass]="notif.isRead ? 'text-slate-400' : 'bg-cyan-950/20 text-slate-100'">
                            <div class="flex items-start justify-between gap-2">
                              <span class="font-bold text-xs">{{ notif.title }}</span>
                              <span class="text-[10px] text-slate-500 whitespace-nowrap">
                                {{ (notif.sentAtUtc || notif.createdAtUtc) | appDate:'time' }}
                              </span>
                            </div>
                            <p class="text-xs text-slate-300 mt-1 line-clamp-2">{{ notif.body || notif.message }}</p>
                          </div>
                        }
                      }
                    </div>

                    <div class="px-4 py-2.5 border-t border-slate-800 bg-slate-950/60 flex items-center justify-between text-xs">
                      <a routerLink="/portal/notificaciones" (click)="closeDropdown()" class="text-cyan-400 hover:text-cyan-300 font-semibold">
                        Ver todas
                      </a>
                      <a routerLink="/portal/notificaciones/preferencias" (click)="closeDropdown()" class="text-slate-400 hover:text-slate-200">
                        Preferencias ⚙️
                      </a>
                    </div>
                  </div>
                }
              </div>

              @if (authService.currentUser()?.photoUrl) {
                <img [src]="authService.currentUser()?.photoUrl" alt="Avatar" class="w-8 h-8 rounded-full object-cover border border-slate-700" />
              }
              <div class="text-right hidden sm:block">
                <div class="text-sm font-semibold text-slate-200">{{ authService.currentUser()?.fullName }}</div>
                <div class="text-xs text-slate-400">{{ authService.userRoles().join(', ') }}</div>
              </div>
              <button (click)="logout()" class="text-sm bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white px-3 py-1.5 rounded-lg transition border border-slate-700">
                Salir
              </button>
            } @else {
              <a routerLink="/login" class="bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold px-4 py-2 rounded-lg transition shadow-sm hover:shadow-cyan-500/20">
                Acceder
              </a>
            }
          </div>
        </div>
      </div>
    </nav>
  `
})
export class NavbarComponent {
  readonly authService = inject(AuthService);
  readonly notificationsService = inject(NotificationsService);
  private readonly router = inject(Router);

  readonly isDropdownOpen = signal(false);

  constructor() {
    // El navbar vive durante toda la app: el estado de notificaciones sigue a la sesión,
    // no a la creación del componente.
    effect(() => {
      const authenticated = this.authService.isAuthenticated();
      untracked(() => {
        this.closeDropdown();
        this.notificationsService.reset();
        if (authenticated) {
          this.notificationsService.refreshUnreadCount().subscribe({ error: () => {} });
        }
      });
    });
  }

  toggleDropdown(): void {
    const next = !this.isDropdownOpen();
    this.isDropdownOpen.set(next);
    if (next) {
      this.notificationsService.loadRecentNotifications().subscribe({ error: () => {} });
    }
  }

  closeDropdown(): void {
    this.isDropdownOpen.set(false);
  }

  markAllAsRead(): void {
    this.notificationsService.markAllAsRead().subscribe({ error: () => {} });
  }

  openNotification(notification: NotificationItem): void {
    if (!notification.isRead) {
      this.notificationsService.markAsRead(notification.id).subscribe({ error: () => {} });
    }
    this.closeDropdown();
    if (this.notificationsService.isSafeInternalUrl(notification.actionUrl)) {
      this.router.navigateByUrl(notification.actionUrl!);
    }
  }

  logout(): void {
    this.closeDropdown();
    this.authService.logout();
  }
}
