import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="min-h-screen bg-slate-950 flex flex-col justify-center py-12 sm:px-6 lg:px-8">
      <div class="sm:mx-auto sm:w-full sm:max-w-md text-center">
        <h2 class="text-3xl font-black text-white tracking-wider">SMART<span class="text-cyan-400">GYM</span></h2>
        <p class="mt-2 text-sm text-slate-400">Acceso a tu portal de entrenamiento y gestión</p>
      </div>

      <div class="mt-8 sm:mx-auto sm:w-full sm:max-w-md px-4">
        <div class="bg-slate-900 border border-slate-800 py-8 px-6 shadow-xl rounded-2xl sm:px-10">
          <!-- Toggle Login / Register -->
          <div class="flex rounded-xl bg-slate-950 p-1 mb-6 border border-slate-800">
            <button
              (click)="isRegister.set(false)"
              [class.bg-cyan-500]="!isRegister()"
              [class.text-slate-950]="!isRegister()"
              [class.font-bold]="!isRegister()"
              [class.text-slate-400]="isRegister()"
              class="flex-1 py-2 text-sm rounded-lg transition"
            >
              Iniciar Sesión
            </button>
            <button
              (click)="isRegister.set(true)"
              [class.bg-cyan-500]="isRegister()"
              [class.text-slate-950]="isRegister()"
              [class.font-bold]="isRegister()"
              [class.text-slate-400]="!isRegister()"
              class="flex-1 py-2 text-sm rounded-lg transition"
            >
              Registrarse
            </button>
          </div>

          @if (errorMessage()) {
            <div class="mb-4 p-3 rounded-lg bg-rose-500/10 border border-rose-500/20 text-rose-400 text-sm">
              {{ errorMessage() }}
            </div>
          }

          @if (!isRegister()) {
            <!-- Login Form -->
            <form (ngSubmit)="onLogin()" class="space-y-4">
              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Email</label>
                <input
                  type="email"
                  [(ngModel)]="loginEmail"
                  name="email"
                  required
                  placeholder="ejemplo@smartgym.com"
                  class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 text-sm"
                />
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Contraseña</label>
                <input
                  type="password"
                  [(ngModel)]="loginPassword"
                  name="password"
                  required
                  placeholder="••••••••"
                  class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white focus:outline-none focus:border-cyan-500 text-sm"
                />
              </div>

              <button
                type="submit"
                [disabled]="isLoading()"
                class="w-full bg-cyan-500 hover:bg-cyan-400 disabled:opacity-50 text-slate-950 font-bold py-3 rounded-xl transition shadow-md shadow-cyan-500/10 mt-2"
              >
                {{ isLoading() ? 'Iniciando sesión...' : 'Entrar' }}
              </button>
            </form>

            <!-- Quick Demo Accounts -->
            <div class="mt-8 pt-6 border-t border-slate-800">
              <span class="text-xs uppercase text-slate-500 font-semibold block mb-3 text-center">Acceso Rápido para Pruebas</span>
              <div class="grid grid-cols-2 gap-2 text-xs">
                <button (click)="fillCredentials('admin@smartgym.com', 'Admin123!')" class="p-2 bg-slate-950 border border-slate-800 hover:border-amber-500/50 rounded-lg text-slate-300 transition text-left">
                  <span class="font-bold text-amber-400 block">Administrador</span>
                  admin&#64;smartgym.com
                </button>
                <button (click)="fillCredentials('secretaria@smartgym.com', 'Secretaria123!')" class="p-2 bg-slate-950 border border-slate-800 hover:border-emerald-500/50 rounded-lg text-slate-300 transition text-left">
                  <span class="font-bold text-emerald-400 block">Secretario/a</span>
                  secretaria&#64;smartgym.com
                </button>
                <button (click)="fillCredentials('instructor@smartgym.com', 'Instructor123!')" class="p-2 bg-slate-950 border border-slate-800 hover:border-cyan-500/50 rounded-lg text-slate-300 transition text-left">
                  <span class="font-bold text-cyan-400 block">Profesor/a</span>
                  instructor&#64;smartgym.com
                </button>
                <button (click)="fillCredentials('alumno@smartgym.com', 'Alumno123!')" class="p-2 bg-slate-950 border border-slate-800 hover:border-blue-500/50 rounded-lg text-slate-300 transition text-left">
                  <span class="font-bold text-blue-400 block">Alumno/a</span>
                  alumno&#64;smartgym.com
                </button>
              </div>
            </div>
          } @else {
            <!-- Register Form -->
            <form (ngSubmit)="onRegister()" class="space-y-3">
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Nombre</label>
                  <input type="text" [(ngModel)]="regFirstName" name="firstName" required class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white text-sm" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Apellido</label>
                  <input type="text" [(ngModel)]="regLastName" name="lastName" required class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white text-sm" />
                </div>
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">DNI</label>
                <input type="text" [(ngModel)]="regDni" name="dni" required class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white text-sm" />
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Email</label>
                <input type="email" [(ngModel)]="regEmail" name="email" required class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white text-sm" />
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Contraseña</label>
                <input type="password" [(ngModel)]="regPassword" name="password" required class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-white text-sm" />
              </div>

              <button
                type="submit"
                [disabled]="isLoading()"
                class="w-full bg-cyan-500 hover:bg-cyan-400 disabled:opacity-50 text-slate-950 font-bold py-3 rounded-xl transition mt-3"
              >
                {{ isLoading() ? 'Registrando...' : 'Crear Cuenta' }}
              </button>
            </form>
          }
        </div>
      </div>
    </div>
  `
})
export class LoginComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly isRegister = signal(false);
  readonly isLoading = signal(false);
  readonly errorMessage = signal('');

  loginEmail = '';
  loginPassword = '';

  regFirstName = '';
  regLastName = '';
  regDni = '';
  regEmail = '';
  regPassword = '';

  fillCredentials(email: string, pass: string): void {
    this.loginEmail = email;
    this.loginPassword = pass;
    this.errorMessage.set('');
  }

  onLogin(): void {
    if (!this.loginEmail || !this.loginPassword) {
      this.errorMessage.set('Por favor completa todos los campos.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    this.authService.login({ email: this.loginEmail, password: this.loginPassword }).subscribe({
      next: (res) => {
        this.isLoading.set(false);
        this.redirectByRole(res.roles);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.errorMessage.set(err?.error?.message || 'Error al iniciar sesión. Verifica tus credenciales.');
      }
    });
  }

  onRegister(): void {
    if (!this.regFirstName || !this.regLastName || !this.regDni || !this.regEmail || !this.regPassword) {
      this.errorMessage.set('Por favor completa todos los campos requeridos.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    this.authService.register({
      firstName: this.regFirstName,
      lastName: this.regLastName,
      dni: this.regDni,
      email: this.regEmail,
      password: this.regPassword
    }).subscribe({
      next: (res) => {
        this.isLoading.set(false);
        this.redirectByRole(res.roles);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.errorMessage.set(err?.error?.message || 'Error al registrarse. Revisa los datos ingresados.');
      }
    });
  }

  private redirectByRole(roles: string[]): void {
    if (roles.includes('Administrator')) {
      this.router.navigate(['/portal/admin']);
    } else if (roles.includes('Secretary') || roles.includes('Instructor')) {
      this.router.navigate(['/portal/docente']);
    } else {
      this.router.navigate(['/portal/alumno']);
    }
  }
}
