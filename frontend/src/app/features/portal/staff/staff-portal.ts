import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { GymApiService } from '../../../core/services/gym-api.service';
import {
  ClassSession,
  Reservation
} from '../../../core/models/gym.models';

@Component({
  selector: 'app-staff-portal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-8">

        <!-- Top Header Staff -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div>
            <div class="flex items-center gap-3">
              <span class="h-3 w-3 rounded-full bg-emerald-500 animate-pulse"></span>
              <span class="text-xs uppercase tracking-widest font-semibold text-emerald-400">Portal de Recepción & Sala</span>
            </div>
            <h1 class="text-3xl font-extrabold text-white mt-1">{{ authService.currentUser()?.fullName }}</h1>
            <p class="text-sm text-slate-400">Roles: {{ authService.userRoles().join(', ') }} | Control de asistencias y operaciones de clase</p>
          </div>

          <div class="flex items-center gap-3">
            <button (click)="openCompensatoryModal()" class="text-xs font-bold bg-cyan-600 hover:bg-cyan-500 text-slate-950 px-3 py-2 rounded-lg transition">
              + Emitir Crédito Compensatorio
            </button>
            <button (click)="loadSessions()" class="text-xs font-semibold bg-slate-800 hover:bg-slate-700 text-slate-200 px-3 py-2 rounded-lg transition border border-slate-700">
              🔄 Actualizar Clases
            </button>
          </div>
        </div>

        <!-- Feedback Message -->
        @if (feedbackMessage()) {
          <div class="p-4 rounded-xl text-sm font-semibold transition"
               [ngClass]="feedbackSuccess() ? 'bg-emerald-950/60 border border-emerald-800 text-emerald-300' : 'bg-rose-950/60 border border-rose-800 text-rose-300'">
            {{ feedbackMessage() }}
          </div>
        }

        <!-- Main Layout: Session Selector on Left, Roster & Actions on Right -->
        <div class="grid grid-cols-1 lg:grid-cols-3 gap-8">

          <!-- Left Column (1 col): Class Sessions List -->
          <div class="space-y-4">
            <h2 class="text-xl font-bold text-white flex items-center justify-between">
              <span>📋 Clases del Día</span>
              <span class="text-xs text-slate-400 font-normal">{{ sessions().length }} programadas</span>
            </h2>

            @if (sessions().length === 0) {
              <div class="bg-slate-900 border border-slate-800 rounded-xl p-6 text-center text-slate-500 text-sm">
                No hay clases programadas para hoy.
              </div>
            } @else {
              <div class="space-y-3">
                @for (s of sessions(); track s.id) {
                  <div (click)="selectSession(s)"
                       class="p-4 rounded-xl border cursor-pointer transition select-none"
                       [ngClass]="selectedSession()?.id === s.id ? 'bg-cyan-950/40 border-cyan-500 shadow-md' : 'bg-slate-900 border-slate-800 hover:border-slate-700'">
                    <div class="flex items-center justify-between">
                      <span class="font-bold text-base text-white">{{ s.activityName }}</span>
                      <span class="text-xs px-2 py-0.5 rounded font-semibold"
                            [ngClass]="getStatusBadgeClass(s.status)">
                        {{ getStatusText(s.status) }}
                      </span>
                    </div>

                    <div class="text-xs text-slate-400 mt-2 space-y-0.5">
                      <p>⏰ {{ s.startTime }} - {{ s.endTime }} | 📍 {{ s.roomName }}</p>
                      <p>👤 Profesor: <span class="text-slate-200">{{ s.instructorName }}</span></p>
                    </div>

                    <div class="mt-3 pt-2 border-t border-slate-800/80 flex justify-between items-center text-xs">
                      <span class="text-slate-400">Inscriptos: <strong class="text-white">{{ s.reservedCount }}</strong>/{{ s.maxCapacity }}</span>
                      <span class="text-emerald-400 font-semibold">Presentes: {{ s.attendedCount }}</span>
                    </div>
                  </div>
                }
              </div>
            }
          </div>

          <!-- Right Column (2 cols): Selected Class Attendance Roster & Control Actions -->
          <div class="lg:col-span-2 space-y-6">

            @if (selectedSession(); as session) {
              <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl space-y-6">

                <!-- Session Header Info & Action Controls -->
                <div class="flex flex-col sm:flex-row sm:items-center justify-between pb-4 border-b border-slate-800 gap-4">
                  <div>
                    <div class="flex items-center gap-2">
                      <h2 class="text-2xl font-extrabold text-white">{{ session.activityName }}</h2>
                      <span class="text-xs px-2.5 py-0.5 rounded-full font-bold" [ngClass]="getStatusBadgeClass(session.status)">
                        {{ getStatusText(session.status) }}
                      </span>
                    </div>
                    <p class="text-xs text-slate-400 mt-1">
                      Sala: <strong class="text-slate-200">{{ session.roomName }}</strong> | Horario: <strong class="text-slate-200">{{ session.startTime }} - {{ session.endTime }}</strong> | Instructor: <strong class="text-slate-200">{{ session.instructorName }}</strong>
                    </p>
                  </div>

                  <!-- Actions: Suspend or Finish -->
                  <div class="flex items-center gap-2">
                    @if (session.status === 0 || session.status === 1) {
                      <button (click)="openSuspendModal()" class="text-xs bg-rose-950 hover:bg-rose-900 text-rose-300 border border-rose-800 px-3 py-1.5 rounded-lg transition font-semibold">
                        Suspender Clase
                      </button>
                      <button (click)="finishSession(session.id)" class="text-xs bg-emerald-600 hover:bg-emerald-500 text-slate-950 px-3 py-1.5 rounded-lg transition font-bold">
                        Finalizar Clase
                      </button>
                    }
                  </div>
                </div>

                <!-- Session Suspension Notice if suspended -->
                @if (session.status === 3) {
                  <div class="p-3 bg-amber-950/40 border border-amber-800 rounded-xl text-amber-200 text-xs">
                    ⚠️ Esta clase ha sido suspendida. Motivo: {{ session.cancellationReason || 'Sin motivo especificado' }}
                  </div>
                }

                <!-- Attendees Table / Roster -->
                <div>
                  <div class="flex items-center justify-between mb-3">
                    <h3 class="text-lg font-bold text-white">Nómina de Alumnos Inscriptos</h3>
                    <span class="text-xs text-slate-400">Registra el ingreso o ausencia con un clic</span>
                  </div>

                  @if (roster().length === 0) {
                    <div class="text-center py-10 text-slate-500 text-sm">
                      No hay alumnos inscriptos en esta clase.
                    </div>
                  } @else {
                    <div class="overflow-x-auto">
                      <table class="w-full text-left text-sm">
                        <thead class="text-xs text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase">
                          <tr>
                            <th class="py-3 px-4">Alumno</th>
                            <th class="py-3 px-4">Documento</th>
                            <th class="py-3 px-4">Estado</th>
                            <th class="py-3 px-4 text-right">Control de Asistencia</th>
                          </tr>
                        </thead>
                        <tbody class="divide-y divide-slate-800/80">
                          @for (item of roster(); track item.id) {
                            <tr class="hover:bg-slate-800/30 transition">
                              <td class="py-3 px-4 font-semibold text-slate-100">{{ item.studentName }}</td>
                              <td class="py-3 px-4 text-slate-400 text-xs">{{ item.documentNumber ?? item.studentDni }}</td>
                              <td class="py-3 px-4">
                                <span class="text-xs px-2 py-0.5 rounded font-bold"
                                      [ngClass]="getReservationBadgeClass(item.status)">
                                  {{ getReservationStatusText(item.status) }}
                                </span>
                              </td>
                              <td class="py-3 px-4 text-right space-x-2">
                                @if (item.status === 0 || item.status === 1) {
                                  <button (click)="checkIn(item.id)" class="text-xs bg-emerald-600 hover:bg-emerald-500 text-slate-950 font-bold px-2.5 py-1 rounded transition">
                                    ✓ Presente
                                  </button>
                                  <button (click)="markNoShow(item.id)" class="text-xs bg-slate-800 hover:bg-rose-950 text-slate-300 hover:text-rose-300 border border-slate-700 px-2.5 py-1 rounded transition">
                                    ✗ Ausente
                                  </button>
                                } @else if (item.status === 3) {
                                  <span class="text-xs text-emerald-400 font-semibold">Asistencia Registrada</span>
                                } @else if (item.status === 4) {
                                  <span class="text-xs text-rose-400 font-semibold">No Show Registrado</span>
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
            } @else {
              <div class="bg-slate-900 border border-slate-800 rounded-2xl p-12 text-center text-slate-500">
                Selecciona una clase del listado lateral para ver la nómina y tomar asistencia.
              </div>
            }

          </div>

        </div>

        <!-- Modal: Suspend Class -->
        @if (showSuspendModal()) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 max-w-md w-full shadow-2xl space-y-4">
              <h3 class="text-xl font-bold text-white">Suspender Clase</h3>
              <p class="text-xs text-slate-400">Se notificará a todos los alumnos inscriptos y se les reintegrará el crédito automáticamente.</p>
              <div>
                <label class="block text-xs font-semibold text-slate-300 mb-1">Motivo de suspensión</label>
                <textarea [(ngModel)]="suspendReason" rows="3" class="w-full bg-slate-950 border border-slate-700 rounded-lg p-2.5 text-sm text-slate-200 focus:outline-none focus:border-cyan-500" placeholder="Ej: Falta de suministro eléctrico, indisposición docente..."></textarea>
              </div>
              <div class="flex justify-end gap-3 pt-2">
                <button (click)="showSuspendModal.set(false)" class="text-xs bg-slate-800 text-slate-300 px-4 py-2 rounded-lg">Cancelar</button>
                <button (click)="confirmSuspend()" class="text-xs bg-rose-600 hover:bg-rose-500 text-white font-bold px-4 py-2 rounded-lg transition">Confirmar Suspensión</button>
              </div>
            </div>
          </div>
        }

        <!-- Modal: Issue Compensatory Credit -->
        @if (showCompensatoryModal()) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 max-w-md w-full shadow-2xl space-y-4">
              <h3 class="text-xl font-bold text-white">Emitir Crédito Compensatorio</h3>
              <p class="text-xs text-slate-400">Agrega créditos de forma auditable al saldo de una membresía de alumno.</p>
              <div class="space-y-3">
                <div>
                  <label class="block text-xs font-semibold text-slate-300 mb-1">ID de Membresía del Alumno</label>
                  <input [(ngModel)]="compensatoryMembershipId" type="text" class="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-slate-200 focus:border-cyan-500" placeholder="Guid de la membresía" />
                </div>
                <div>
                  <label class="block text-xs font-semibold text-slate-300 mb-1">Cantidad de Créditos</label>
                  <input [(ngModel)]="compensatoryAmount" type="number" min="1" class="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-slate-200 focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold text-slate-300 mb-1">Motivo / Justificación</label>
                  <input [(ngModel)]="compensatoryReason" type="text" class="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-slate-200 focus:border-cyan-500" placeholder="Ej: Compensación por clase suspendida por lluvia" />
                </div>
              </div>
              <div class="flex justify-end gap-3 pt-2">
                <button (click)="showCompensatoryModal.set(false)" class="text-xs bg-slate-800 text-slate-300 px-4 py-2 rounded-lg">Cerrar</button>
                <button (click)="confirmCompensatoryCredit()" class="text-xs bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold px-4 py-2 rounded-lg transition">Emitir Crédito</button>
              </div>
            </div>
          </div>
        }

      </div>
    </div>
  `
})
export class StaffPortalComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly gymApi = inject(GymApiService);

  sessions = signal<ClassSession[]>([]);
  selectedSession = signal<ClassSession | null>(null);
  roster = signal<Reservation[]>([]);

  showSuspendModal = signal(false);
  suspendReason = '';

  showCompensatoryModal = signal(false);
  compensatoryMembershipId = '';
  compensatoryAmount = 1;
  compensatoryReason = '';

  feedbackMessage = signal<string | null>(null);
  feedbackSuccess = signal(true);

  ngOnInit(): void {
    this.loadSessions();
  }

  loadSessions(): void {
    const today = new Date().toISOString().split('T')[0];
    this.gymApi.getClassSessions({ fromDate: today, toDate: today }).subscribe({
      next: (data) => {
        this.sessions.set(data);
        if (data.length > 0 && !this.selectedSession()) {
          this.selectSession(data[0]);
        }
      },
      error: () => this.sessions.set([])
    });
  }

  selectSession(session: ClassSession): void {
    this.selectedSession.set(session);
    this.loadRoster(session.id);
  }

  loadRoster(sessionId: string): void {
    this.gymApi.getSessionReservations(sessionId).subscribe({
      next: (list) => this.roster.set(list),
      error: () => this.roster.set([])
    });
  }

  checkIn(reservationId: string): void {
    this.gymApi.recordAttendance(reservationId, 0).subscribe({
      next: () => {
        this.showFeedback('Asistencia registrada correctamente.', true);
        if (this.selectedSession()) {
          this.loadRoster(this.selectedSession()!.id);
          this.loadSessions();
        }
      },
      error: (err) => {
        const msg = err.error?.message || 'Error al registrar asistencia.';
        this.showFeedback(msg, false);
      }
    });
  }

  markNoShow(reservationId: string): void {
    this.gymApi.recordNoShow(reservationId).subscribe({
      next: () => {
        this.showFeedback('Ausente / No-Show registrado.', true);
        if (this.selectedSession()) {
          this.loadRoster(this.selectedSession()!.id);
          this.loadSessions();
        }
      },
      error: (err) => {
        const msg = err.error?.message || 'Error al registrar No-Show.';
        this.showFeedback(msg, false);
      }
    });
  }

  openSuspendModal(): void {
    this.suspendReason = '';
    this.showSuspendModal.set(true);
  }

  confirmSuspend(): void {
    const s = this.selectedSession();
    if (!s || !this.suspendReason.trim()) return;

    this.gymApi.suspendClassSession(s.id, this.suspendReason).subscribe({
      next: (updated) => {
        this.showSuspendModal.set(false);
        this.selectedSession.set(updated);
        this.showFeedback('Clase suspendida. Los alumnos han sido notificados.', true);
        this.loadSessions();
      },
      error: (err) => {
        const msg = err.error?.message || 'Error al suspender clase.';
        this.showFeedback(msg, false);
      }
    });
  }

  finishSession(sessionId: string): void {
    this.gymApi.finishClassSession(sessionId).subscribe({
      next: (updated) => {
        this.selectedSession.set(updated);
        this.showFeedback('Clase dada por finalizada.', true);
        this.loadSessions();
      },
      error: (err) => {
        const msg = err.error?.message || 'Error al finalizar clase.';
        this.showFeedback(msg, false);
      }
    });
  }

  openCompensatoryModal(): void {
    this.compensatoryMembershipId = '';
    this.compensatoryAmount = 1;
    this.compensatoryReason = '';
    this.showCompensatoryModal.set(true);
  }

  confirmCompensatoryCredit(): void {
    if (!this.compensatoryMembershipId.trim() || !this.compensatoryReason.trim()) return;

    this.gymApi.issueCompensatoryCredit({
      membershipId: this.compensatoryMembershipId,
      amount: this.compensatoryAmount,
      reason: this.compensatoryReason
    }).subscribe({
      next: () => {
        this.showCompensatoryModal.set(false);
        this.showFeedback('Crédito compensatorio emitido exitosamente.', true);
      },
      error: (err) => {
        const msg = err.error?.message || 'Error al emitir crédito compensatorio.';
        this.showFeedback(msg, false);
      }
    });
  }

  getStatusBadgeClass(status: number): string {
    switch (status) {
      case 0: return 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/30';
      case 1: return 'bg-amber-500/20 text-amber-300 border border-amber-500/30';
      case 2: return 'bg-slate-700 text-slate-300';
      case 3: return 'bg-rose-500/20 text-rose-300 border border-rose-500/30';
      default: return 'bg-slate-800 text-slate-400';
    }
  }

  getStatusText(status: number): string {
    switch (status) {
      case 0: return 'Programada';
      case 1: return 'En Curso';
      case 2: return 'Finalizada';
      case 3: return 'Suspendida';
      case 4: return 'Cancelada';
      default: return 'Desconocido';
    }
  }

  getReservationBadgeClass(status: number): string {
    switch (status) {
      case 0: return 'bg-cyan-500/20 text-cyan-300';
      case 1: return 'bg-blue-500/20 text-blue-300';
      case 2: return 'bg-slate-800 text-slate-400';
      case 3: return 'bg-emerald-500/20 text-emerald-300';
      case 4: return 'bg-rose-500/20 text-rose-300';
      case 5: return 'bg-amber-500/20 text-amber-300';
      default: return 'bg-slate-800 text-slate-300';
    }
  }

  getReservationStatusText(status: number): string {
    switch (status) {
      case 0: return 'Reservada';
      case 1: return 'Confirmada';
      case 2: return 'Cancelada';
      case 3: return 'Presente';
      case 4: return 'Ausente / No-Show';
      case 5: return 'En Espera';
      default: return 'Desconocido';
    }
  }

  private showFeedback(msg: string, success: boolean): void {
    this.feedbackMessage.set(msg);
    this.feedbackSuccess.set(success);
    setTimeout(() => this.feedbackMessage.set(null), 5000);
  }
}
