import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { GymApiService } from '../../../core/services/gym-api.service';
import {
  Membership,
  Reservation,
  ClassSession,
  MedicalCertificateStatus,
  NotificationItem,
  CreditMovement
} from '../../../core/models/gym.models';

@Component({
  selector: 'app-student-portal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-8">

        <!-- Top Header Profile -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <div class="flex items-center gap-3">
              <span class="h-3 w-3 rounded-full bg-emerald-500 animate-pulse"></span>
              <span class="text-xs uppercase tracking-widest font-semibold text-emerald-400">Portal del Alumno</span>
            </div>
            <h1 class="text-3xl font-extrabold text-white mt-1">{{ authService.currentUser()?.fullName }}</h1>
            <p class="text-sm text-slate-400">DNI: {{ authService.currentUser()?.dni }} | Email: {{ authService.currentUser()?.email }}</p>
          </div>

          <!-- Quick Metrics Bar -->
          <div class="flex items-center gap-4 bg-slate-950/60 border border-slate-800/80 px-5 py-3 rounded-xl">
            <div class="text-center">
              <span class="text-xs text-slate-400 uppercase font-medium">Créditos Disp.</span>
              <div class="text-2xl font-black text-cyan-400">{{ activeMembership()?.availableCredits ?? 0 }}</div>
            </div>
            <div class="h-8 w-px bg-slate-800"></div>
            <div class="text-center">
              <span class="text-xs text-slate-400 uppercase font-medium">Plan</span>
              <div class="text-sm font-bold text-slate-200 truncate max-w-[130px]">{{ activeMembership()?.membershipPlanName ?? 'Sin Plan Activo' }}</div>
            </div>
            <div class="h-8 w-px bg-slate-800"></div>
            <div class="text-center">
              <span class="text-xs text-slate-400 uppercase font-medium">Apto Médico</span>
              <div>
                @if (medicalStatus()?.isValid) {
                  <span class="text-xs bg-emerald-500/20 text-emerald-400 px-2 py-0.5 rounded font-bold">VIGENTE</span>
                } @else {
                  <span class="text-xs bg-rose-500/20 text-rose-400 px-2 py-0.5 rounded font-bold">REQUERIDO</span>
                }
              </div>
            </div>
          </div>
        </div>

        <!-- Alert Banner: Missing Medical Certificate -->
        @if (!medicalStatus()?.isValid) {
          <div class="bg-rose-950/40 border border-rose-800/60 rounded-xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div class="flex items-center gap-3">
              <div class="p-2 rounded-lg bg-rose-500/20 text-rose-400">
                <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/>
                </svg>
              </div>
              <div>
                <h4 class="font-bold text-rose-200">Certificado Médico No Válido o Pendiente</h4>
                <p class="text-xs text-rose-300">Debes adjuntar tu apto físico para poder participar en clases presenciales.</p>
              </div>
            </div>
            <div class="flex items-center gap-2">
              <input type="date" [(ngModel)]="uploadExpiryDate" class="bg-slate-900 border border-slate-700 text-xs px-2 py-1.5 rounded text-slate-200 focus:outline-none focus:border-rose-500" />
              <input #fileInput type="file" (change)="onFileSelected($event)" class="hidden" accept=".pdf,.png,.jpg,.jpeg" />
              <button (click)="fileInput.click()" [disabled]="isUploadingCert()" class="bg-rose-600 hover:bg-rose-500 text-white text-xs font-bold px-3 py-1.5 rounded transition">
                {{ isUploadingCert() ? 'Subiendo...' : 'Adjuntar Apto' }}
              </button>
            </div>
          </div>
        }

        <!-- Flash Message -->
        @if (feedbackMessage()) {
          <div class="p-4 rounded-xl text-sm font-semibold transition"
               [ngClass]="feedbackSuccess() ? 'bg-emerald-950/60 border border-emerald-800 text-emerald-300' : 'bg-rose-950/60 border border-rose-800 text-rose-300'">
            {{ feedbackMessage() }}
          </div>
        }

        <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">

          <!-- Left Column (2 cols): My Reservations & Available Classes -->
          <div class="lg:col-span-2 space-y-8">

            <!-- Active Reservations -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
              <div class="flex items-center justify-between mb-4">
                <h2 class="text-xl font-bold text-white flex items-center gap-2">
                  <span class="text-cyan-400">📅</span> Mis Reservas Próximas
                </h2>
                <span class="text-xs text-slate-400">Cancelación anticipada: hasta 2hs antes con devolución</span>
              </div>

              @if (myReservations().length === 0) {
                <div class="text-center py-8 text-slate-500 text-sm">
                  No tienes reservas activas en este momento. ¡Elige una clase disponible abajo!
                </div>
              } @else {
                <div class="space-y-3">
                  @for (r of myReservations(); track r.id) {
                    <div class="bg-slate-950/70 border border-slate-800/80 rounded-xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                      <div>
                        <div class="flex items-center gap-2">
                          <span class="font-bold text-base text-slate-100">{{ r.activityName }}</span>
                          <span class="text-xs px-2 py-0.5 rounded font-semibold"
                                [ngClass]="r.status === 0 ? 'bg-cyan-500/20 text-cyan-300' : (r.status === 5 ? 'bg-amber-500/20 text-amber-300' : 'bg-slate-700 text-slate-300')">
                            {{ r.status === 0 ? 'Reservada' : (r.status === 5 ? 'Lista de Espera #' + r.waitListPosition : 'Confirmada') }}
                          </span>
                        </div>
                        <div class="text-xs text-slate-400 mt-1">
                          📆 {{ r.classDate | date:'mediumDate' }} | ⏰ {{ r.startTime }} - {{ r.endTime }} | 📍 {{ r.roomName }}
                        </div>
                      </div>

                      @if (r.status === 0 || r.status === 1 || r.status === 5) {
                        <button (click)="cancelReservation(r.id)" class="text-xs font-bold bg-rose-950 hover:bg-rose-900 text-rose-300 border border-rose-800/60 px-3 py-1.5 rounded-lg transition self-start sm:self-center">
                          Cancelar Turno
                        </button>
                      }
                    </div>
                  }
                </div>
              }
            </div>

            <!-- Available Sessions Schedule -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
              <div class="flex items-center justify-between mb-4">
                <h2 class="text-xl font-bold text-white flex items-center gap-2">
                  <span class="text-cyan-400">⚡</span> Clases Disponibles para Reservar
                </h2>
                <button (click)="loadAvailableSessions()" class="text-xs text-cyan-400 hover:text-cyan-300">Actualizar</button>
              </div>

              @if (availableSessions().length === 0) {
                <div class="text-center py-8 text-slate-500 text-sm">
                  No hay clases programadas disponibles para reservar hoy.
                </div>
              } @else {
                <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                  @for (s of availableSessions(); track s.id) {
                    <div class="bg-slate-950/70 border border-slate-800/80 rounded-xl p-4 flex flex-col justify-between hover:border-slate-700 transition">
                      <div>
                        <div class="flex items-center justify-between">
                          <span class="font-bold text-white">{{ s.activityName }}</span>
                          <span class="text-xs px-2 py-0.5 rounded font-bold"
                                [ngClass]="s.availableSpots > 0 ? 'bg-emerald-500/20 text-emerald-400' : 'bg-amber-500/20 text-amber-400'">
                            {{ s.availableSpots > 0 ? s.availableSpots + ' lugares' : 'Agotada (Espera)' }}
                          </span>
                        </div>
                        <div class="text-xs text-slate-400 mt-2 space-y-0.5">
                          <p>⏰ {{ s.startTime }} - {{ s.endTime }} | 📆 {{ s.date | date:'shortDate' }}</p>
                          <p>📍 {{ s.roomName }}</p>
                          <p>👤 {{ s.instructorName }}</p>
                        </div>
                      </div>

                      <div class="mt-4 pt-3 border-t border-slate-800/80 flex items-center justify-between">
                        <span class="text-xs text-slate-500">Cupo: {{ s.reservedCount }}/{{ s.maxCapacity }}</span>
                        <button (click)="reserveClass(s.id)"
                                [disabled]="isSubmittingReservation()"
                                class="text-xs font-bold px-3 py-1.5 rounded-lg transition"
                                [ngClass]="s.availableSpots > 0 ? 'bg-cyan-500 hover:bg-cyan-400 text-slate-950' : 'bg-amber-600 hover:bg-amber-500 text-slate-950'">
                          {{ s.availableSpots > 0 ? 'Reservar' : 'Anotarme en Espera' }}
                        </button>
                      </div>
                    </div>
                  }
                </div>
              }
            </div>

          </div>

          <!-- Right Column (1 col): Membership details, Credit Ledger & Notifications -->
          <div class="space-y-8">

            <!-- Membership Details Card -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
              <h3 class="text-lg font-bold text-white mb-3">Detalle de Membresía</h3>
              @if (activeMembership(); as mem) {
                <div class="space-y-3 text-sm">
                  <div class="flex justify-between py-1 border-b border-slate-800">
                    <span class="text-slate-400">Plan:</span>
                    <span class="font-semibold text-slate-200">{{ mem.membershipPlanName }}</span>
                  </div>
                  <div class="flex justify-between py-1 border-b border-slate-800">
                    <span class="text-slate-400">Vigencia hasta:</span>
                    <span class="font-semibold text-slate-200">{{ mem.endDate | date:'mediumDate' }}</span>
                  </div>
                  <div class="flex justify-between py-1 border-b border-slate-800">
                    <span class="text-slate-400">Créditos totales:</span>
                    <span class="font-semibold text-slate-200">{{ mem.totalCredits }}</span>
                  </div>
                  <div class="flex justify-between py-1">
                    <span class="text-slate-400">Créditos disponibles:</span>
                    <span class="font-bold text-cyan-400 text-base">{{ mem.availableCredits }}</span>
                  </div>
                </div>
              } @else {
                <p class="text-xs text-slate-500">No cuentas con una membresía activa actualmente.</p>
              }
            </div>

            <!-- Notifications Feed -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
              <div class="flex items-center justify-between mb-3">
                <h3 class="text-lg font-bold text-white flex items-center gap-2">
                  <span>🔔</span> Notificaciones
                </h3>
                <span class="text-xs bg-slate-800 px-2 py-0.5 rounded text-slate-400">{{ notifications().length }}</span>
              </div>

              @if (notifications().length === 0) {
                <p class="text-xs text-slate-500 py-3">No tienes notificaciones pendientes.</p>
              } @else {
                <div class="space-y-2 max-h-60 overflow-y-auto pr-1">
                  @for (n of notifications(); track n.id) {
                    <div class="p-3 rounded-xl border text-xs"
                         [ngClass]="n.isRead ? 'bg-slate-950/40 border-slate-800/60 text-slate-400' : 'bg-cyan-950/30 border-cyan-800/50 text-slate-200'">
                      <div class="flex items-center justify-between font-bold">
                        <span>{{ n.title }}</span>
                        <span class="text-[10px] text-slate-500">{{ n.createdAtUtc | date:'short' }}</span>
                      </div>
                      <p class="mt-1 text-slate-300">{{ n.message }}</p>
                      @if (!n.isRead) {
                        <button (click)="markNotificationRead(n.id)" class="mt-2 text-[11px] text-cyan-400 hover:underline">
                          Marcar como leída
                        </button>
                      }
                    </div>
                  }
                </div>
              }
            </div>

            <!-- Immutable Credit Movements Ledger -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg">
              <h3 class="text-lg font-bold text-white mb-3">Movimientos de Créditos</h3>
              @if (creditMovements().length === 0) {
                <p class="text-xs text-slate-500 py-3">No hay movimientos registrados.</p>
              } @else {
                <div class="space-y-2 max-h-60 overflow-y-auto pr-1">
                  @for (m of creditMovements(); track m.id) {
                    <div class="p-2.5 bg-slate-950/60 rounded-lg border border-slate-800/80 text-xs flex items-center justify-between">
                      <div>
                        <div class="font-semibold text-slate-200">{{ m.concept }}</div>
                        <div class="text-[10px] text-slate-500">{{ m.createdAtUtc | date:'short' }}</div>
                      </div>
                      <div class="text-right">
                        <span class="font-bold text-sm" [ngClass]="m.amount > 0 ? 'text-emerald-400' : 'text-rose-400'">
                          {{ m.amount > 0 ? '+' + m.amount : m.amount }}
                        </span>
                        <div class="text-[10px] text-slate-400">Saldo: {{ m.balanceAfter }}</div>
                      </div>
                    </div>
                  }
                </div>
              }
            </div>

          </div>

        </div>

      </div>
    </div>
  `
})
export class StudentPortalComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly gymApi = inject(GymApiService);

  activeMembership = signal<Membership | null>(null);
  myReservations = signal<Reservation[]>([]);
  availableSessions = signal<ClassSession[]>([]);
  medicalStatus = signal<MedicalCertificateStatus | null>(null);
  notifications = signal<NotificationItem[]>([]);
  creditMovements = signal<CreditMovement[]>([]);

  uploadExpiryDate = '';
  isUploadingCert = signal(false);
  isSubmittingReservation = signal(false);
  feedbackMessage = signal<string | null>(null);
  feedbackSuccess = signal(true);

  ngOnInit(): void {
    const studentId = this.authService.currentUser()?.personId;
    if (studentId) {
      this.loadStudentData(studentId);
    }
  }

  loadStudentData(studentId: string): void {
    this.gymApi.getActiveMembership(studentId).subscribe({
      next: (mem) => this.activeMembership.set(mem),
      error: () => this.activeMembership.set(null)
    });

    this.gymApi.getStudentReservations(studentId).subscribe({
      next: (res) => this.myReservations.set(res.filter(r => r.status === 0 || r.status === 1 || r.status === 5)),
      error: () => this.myReservations.set([])
    });

    this.gymApi.getMedicalCertificateStatus(studentId).subscribe({
      next: (status) => this.medicalStatus.set(status),
      error: () => this.medicalStatus.set(null)
    });

    this.gymApi.getMyNotifications().subscribe({
      next: (notifs) => this.notifications.set(notifs),
      error: () => this.notifications.set([])
    });

    this.gymApi.getStudentCreditMovements(studentId).subscribe({
      next: (movs) => this.creditMovements.set(movs),
      error: () => this.creditMovements.set([])
    });

    this.loadAvailableSessions();
  }

  loadAvailableSessions(): void {
    const today = new Date().toISOString().split('T')[0];
    const nextWeek = new Date(Date.now() + 7 * 86400000).toISOString().split('T')[0];
    this.gymApi.getClassSessions({ fromDate: today, toDate: nextWeek, status: 0 }).subscribe({
      next: (sessions) => this.availableSessions.set(sessions),
      error: () => this.availableSessions.set([])
    });
  }

  reserveClass(sessionId: string): void {
    const studentId = this.authService.currentUser()?.personId;
    if (!studentId) return;

    this.isSubmittingReservation.set(true);
    this.gymApi.createReservation(sessionId, studentId).subscribe({
      next: (res) => {
        this.isSubmittingReservation.set(false);
        this.showFeedback('¡Reserva confirmada con éxito!', true);
        this.loadStudentData(studentId);
      },
      error: (err) => {
        this.isSubmittingReservation.set(false);
        const msg = err.error?.message || 'Error al procesar la reserva.';
        this.showFeedback(msg, false);
      }
    });
  }

  cancelReservation(reservationId: string): void {
    const studentId = this.authService.currentUser()?.personId;
    if (!studentId) return;

    this.gymApi.cancelReservation(reservationId, 'Cancelada por el alumno desde el portal').subscribe({
      next: () => {
        this.showFeedback('Reserva cancelada correctamente.', true);
        this.loadStudentData(studentId);
      },
      error: (err) => {
        const msg = err.error?.message || 'Error al cancelar la reserva.';
        this.showFeedback(msg, false);
      }
    });
  }

  onFileSelected(event: Event): void {
    const studentId = this.authService.currentUser()?.personId;
    if (!studentId) return;

    const input = event.target as HTMLInputElement;
    if (input.files && input.files[0]) {
      const file = input.files[0];
      const expiry = this.uploadExpiryDate || new Date(Date.now() + 365 * 86400000).toISOString().split('T')[0];

      this.isUploadingCert.set(true);
      this.gymApi.uploadMedicalCertificate(studentId, file, expiry).subscribe({
        next: (status) => {
          this.isUploadingCert.set(false);
          this.medicalStatus.set(status);
          this.showFeedback('Apto médico subido correctamente.', true);
        },
        error: (err) => {
          this.isUploadingCert.set(false);
          const msg = err.error?.message || 'Error al subir el certificado médico.';
          this.showFeedback(msg, false);
        }
      });
    }
  }

  markNotificationRead(notificationId: string): void {
    this.gymApi.markNotificationAsRead(notificationId).subscribe({
      next: () => {
        this.notifications.update(list => list.map(n => n.id === notificationId ? { ...n, isRead: true } : n));
      }
    });
  }

  private showFeedback(msg: string, success: boolean): void {
    this.feedbackMessage.set(msg);
    this.feedbackSuccess.set(success);
    setTimeout(() => this.feedbackMessage.set(null), 5000);
  }
}
