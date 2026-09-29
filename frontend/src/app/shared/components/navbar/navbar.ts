import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
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
          <div class="flex items-center space-x-3">
            @if (authService.isAuthenticated()) {
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

  logout(): void {
    this.authService.logout();
  }
}
