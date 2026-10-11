import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { GymApiService } from '../../../core/services/gym-api.service';
import {
  ACTIVE_RESERVATION_STATUSES,
  AttendanceSource,
  ClassSession,
  Membership,
  PagedResult,
  Reservation,
  ReservationAccessType,
  ReservationApiError,
  ReservationStatus,
  WaitlistEntry
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
                                @if (item.status === ReservationStatus.Confirmed) {
                                  <button (click)="checkIn(item.id)" class="text-xs bg-emerald-600 hover:bg-emerald-500 text-slate-950 font-bold px-2.5 py-1 rounded transition">
                                    ✓ Presente
                                  </button>
                                  <button (click)="markNoShow(item.id)" class="text-xs bg-slate-800 hover:bg-rose-950 text-slate-300 hover:text-rose-300 border border-slate-700 px-2.5 py-1 rounded transition">
                                    ✗ Ausente
                                  </button>
                                } @else if (item.status === ReservationStatus.Attended) {
                                  <span class="text-xs text-emerald-400 font-semibold">Asistencia Registrada</span>
                                } @else if (item.status === ReservationStatus.NoShow) {
                                  <span class="text-xs text-rose-400 font-semibold">No Show Registrado</span>
                                }
                                @if (authService.isStaff() && isActiveReservation(item)) {
                                  <button (click)="openCancelModal(item)" class="text-xs bg-rose-950 hover:bg-rose-900 text-rose-300 border border-rose-800/60 px-2.5 py-1 rounded transition">
                                    Cancelar
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

                <!-- Lista de espera de la clase -->
                <div>
                  <h3 class="text-lg font-bold text-white mb-3">Lista de Espera ({{ waitlist().length }})</h3>
                  @if (waitlist().length === 0) {
                    <p class="text-xs text-slate-500">No hay alumnos en lista de espera.</p>
                  } @else {
                    <ol class="space-y-1.5 text-sm">
                      @for (w of waitlist(); track w.reservationId) {
                        <li class="flex items-center justify-between bg-slate-950/60 border border-slate-800/80 rounded-lg px-3 py-2">
                          <span class="text-slate-100">
                            @if (w.status === ReservationStatus.Pending) {
                              <span class="text-xs bg-emerald-500/20 text-emerald-300 px-2 py-0.5 rounded font-bold mr-2">Cupo ofrecido</span>
                            } @else {
                              <span class="text-xs bg-amber-500/20 text-amber-300 px-2 py-0.5 rounded font-bold mr-2">#{{ w.waitListPosition }}</span>
                            }
                            {{ w.studentName }}
                          </span>
                          <span class="text-xs text-slate-500">
                            {{ w.status === ReservationStatus.Pending
                                ? 'vence ' + (w.waitlistOfferExpiresAtUtc | date:'short')
                                : 'desde ' + (w.waitlistEnteredAtUtc | date:'short') }}
                          </span>
                        </li>
                      }
                    </ol>
                  }
                </div>

                <!-- Reserva en nombre de un alumno -->
                @if (authService.isStaff()) {
                <div class="pt-4 border-t border-slate-800">
                  <h3 class="text-lg font-bold text-white mb-1">Reservar para un Alumno</h3>
                  <p class="text-xs text-slate-400 mb-3">Se aplican las mismas reglas de cupo y de derecho de acceso que al alumno.</p>
                  <div class="flex flex-col sm:flex-row gap-2">
                    <input [(ngModel)]="deskStudentId" type="text" placeholder="ID del alumno (Guid)"
                           class="flex-1 bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-slate-200 focus:outline-none focus:border-cyan-500" />
                    <label class="flex items-center gap-2 text-xs text-slate-300">
                      <input type="checkbox" [(ngModel)]="deskAllowWaitlist" /> Lista de espera si está completa
                    </label>
                    <button (click)="reserveForStudent()" [disabled]="!deskStudentId.trim() || isSubmitting()"
                            class="text-xs bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold px-4 py-2 rounded-lg transition disabled:opacity-40">
                      Reservar
                    </button>
                  </div>
                </div>
                }

              </div>
            } @else {
              <div class="bg-slate-900 border border-slate-800 rounded-2xl p-12 text-center text-slate-500">
                Selecciona una clase del listado lateral para ver la nómina y tomar asistencia.
              </div>
            }

          </div>

        </div>

        <!-- Gestión general de reservas (sólo secretaría y administración) -->
        @if (authService.isStaff()) {
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl space-y-4">
          <div class="flex flex-col lg:flex-row lg:items-end justify-between gap-4">
            <div>
              <h2 class="text-xl font-bold text-white">🗂️ Gestión de Reservas</h2>
              <p class="text-xs text-slate-400">{{ reservationsPage()?.totalCount ?? 0 }} reservas encontradas</p>
            </div>
            <div class="flex flex-wrap items-end gap-3 text-xs">
              <label class="flex flex-col gap-1 text-slate-400">Estado
                <select [(ngModel)]="filterStatus" class="bg-slate-950 border border-slate-700 rounded-lg p-2 text-slate-200">
                  <option [ngValue]="undefined">Todos</option>
                  @for (option of statusOptions; track option.value) {
                    <option [ngValue]="option.value">{{ option.label }}</option>
                  }
                </select>
              </label>
              <label class="flex flex-col gap-1 text-slate-400">Acceso
                <select [(ngModel)]="filterAccessType" class="bg-slate-950 border border-slate-700 rounded-lg p-2 text-slate-200">
                  <option [ngValue]="undefined">Todos</option>
                  @for (option of accessTypeOptions; track option.value) {
                    <option [ngValue]="option.value">{{ option.label }}</option>
                  }
                </select>
              </label>
              <label class="flex flex-col gap-1 text-slate-400">Desde
                <input type="date" [(ngModel)]="filterFrom" class="bg-slate-950 border border-slate-700 rounded-lg p-2 text-slate-200" />
              </label>
              <label class="flex flex-col gap-1 text-slate-400">Hasta
                <input type="date" [(ngModel)]="filterTo" class="bg-slate-950 border border-slate-700 rounded-lg p-2 text-slate-200" />
              </label>
              <label class="flex items-center gap-2 text-slate-300 pb-2">
                <input type="checkbox" [(ngModel)]="filterAffectedBySessionChange" /> Afectadas por cambio de clase
              </label>
              <label class="flex items-center gap-2 text-slate-300 pb-2">
                <input type="checkbox" [(ngModel)]="filterLateCancellation" /> Cancelaciones tardías
              </label>
              <button (click)="loadReservations(1)" class="bg-cyan-600 hover:bg-cyan-500 text-slate-950 font-bold px-4 py-2 rounded-lg transition">
                Buscar
              </button>
            </div>
          </div>

          @if (filterAffectedBySessionChange && filterAccessType === ReservationAccessType.SingleClassTicket) {
            <p class="text-xs text-amber-300">
              Estas reservas usaban un pase individual de una clase anulada: requieren compensación manual (crédito compensatorio o reintegro).
            </p>
          }

          @if ((reservationsPage()?.items ?? []).length === 0) {
            <div class="text-center py-8 text-slate-500 text-sm">No hay reservas para los filtros indicados.</div>
          } @else {
            <div class="overflow-x-auto">
              <table class="w-full text-left text-sm">
                <thead class="text-xs text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase">
                  <tr>
                    <th class="py-3 px-4">Alumno</th>
                    <th class="py-3 px-4">Clase</th>
                    <th class="py-3 px-4">Acceso</th>
                    <th class="py-3 px-4">Estado</th>
                    <th class="py-3 px-4 text-right">Acciones</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-slate-800/80">
                  @for (r of reservationsPage()!.items; track r.id) {
                    <tr class="hover:bg-slate-800/30 transition">
                      <td class="py-3 px-4">
                        <div class="font-semibold text-slate-100">{{ r.studentName }}</div>
                        <div class="text-xs text-slate-500">{{ r.documentNumber ?? r.studentDni }}</div>
                      </td>
                      <td class="py-3 px-4 text-xs text-slate-300">
                        {{ r.activityName }}<br />
                        <span class="text-slate-500">{{ r.classDate | date:'shortDate' }} {{ r.startTime }} · {{ r.roomName }}</span>
                      </td>
                      <td class="py-3 px-4 text-xs text-slate-300">{{ getAccessTypeText(r.accessType) }}</td>
                      <td class="py-3 px-4">
                        <span class="text-xs px-2 py-0.5 rounded font-bold" [ngClass]="getReservationBadgeClass(r.status)">
                          {{ getReservationStatusText(r.status) }}
                        </span>
                        @if (r.isLateCancellation) {
                          <span class="ml-1 text-[10px] text-amber-400 font-semibold">TARDÍA</span>
                        }
                        @if (r.isPolicyOverride) {
                          <span class="ml-1 text-[10px] text-fuchsia-400 font-semibold" [title]="r.policyOverrideReason ?? ''">EXCEPCIÓN</span>
                        }
                        @if (r.affectedBySessionChange) {
                          <span class="ml-1 text-[10px] text-rose-400 font-semibold">CLASE ANULADA</span>
                        }
                      </td>
                      <td class="py-3 px-4 text-right">
                        @if (isActiveReservation(r)) {
                          <button (click)="openCancelModal(r)" class="text-xs bg-rose-950 hover:bg-rose-900 text-rose-300 border border-rose-800/60 px-2.5 py-1 rounded transition">
                            Cancelar
                          </button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <div class="flex items-center justify-end gap-3 text-xs text-slate-400">
              <button (click)="loadReservations(reservationsPage()!.pageNumber - 1)" [disabled]="!reservationsPage()!.hasPreviousPage"
                      class="px-3 py-1.5 rounded-lg bg-slate-800 border border-slate-700 disabled:opacity-40">Anterior</button>
              <span>Página {{ reservationsPage()!.pageNumber }} de {{ reservationsPage()!.totalPages }}</span>
              <button (click)="loadReservations(reservationsPage()!.pageNumber + 1)" [disabled]="!reservationsPage()!.hasNextPage"
                      class="px-3 py-1.5 rounded-lg bg-slate-800 border border-slate-700 disabled:opacity-40">Siguiente</button>
            </div>
          }
        </div>
        }

        <!-- Modal: Cancel Reservation -->
        @if (reservationToCancel(); as target) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 max-w-md w-full shadow-2xl space-y-4">
              <h3 class="text-xl font-bold text-white">Cancelar Reserva</h3>
              <p class="text-xs text-slate-400">
                {{ target.studentName }} · {{ target.activityName }} · {{ target.classDate | date:'shortDate' }} {{ target.startTime }}
              </p>
              <div>
                <label class="block text-xs font-semibold text-slate-300 mb-1">Motivo</label>
                <input [(ngModel)]="cancelReason" type="text" class="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-slate-200 focus:border-cyan-500" placeholder="Ej: Pedido del alumno por teléfono" />
              </div>
              <label class="flex items-start gap-2 text-xs text-slate-300">
                <input type="checkbox" [(ngModel)]="cancelOverridePolicy" class="mt-0.5" />
                <span>Excepción administrativa: devolver el crédito aunque la cancelación sea fuera de término. Queda auditada.</span>
              </label>
              @if (cancelOverridePolicy) {
                <div>
                  <label class="block text-xs font-semibold text-slate-300 mb-1">Justificación de la excepción (obligatoria)</label>
                  <input [(ngModel)]="cancelOverrideReason" type="text" class="w-full bg-slate-950 border border-slate-700 rounded-lg p-2 text-sm text-slate-200 focus:border-cyan-500" placeholder="Ej: Emergencia médica acreditada" />
                </div>
              }
              <div class="flex justify-end gap-3 pt-2">
                <button (click)="reservationToCancel.set(null)" class="text-xs bg-slate-800 text-slate-300 px-4 py-2 rounded-lg">Volver</button>
                <button (click)="confirmCancelReservation()"
                        [disabled]="isSubmitting() || (cancelOverridePolicy && !cancelOverrideReason.trim())"
                        class="text-xs bg-rose-600 hover:bg-rose-500 text-white font-bold px-4 py-2 rounded-lg transition disabled:opacity-40">
                  Confirmar Cancelación
                </button>
              </div>
            </div>
          </div>
        }

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

  readonly ReservationStatus = ReservationStatus;
  readonly ReservationAccessType = ReservationAccessType;

  readonly statusOptions = [
    { value: ReservationStatus.Confirmed, label: 'Confirmada' },
    { value: ReservationStatus.Waitlisted, label: 'En espera' },
    { value: ReservationStatus.Pending, label: 'Cupo ofrecido' },
    { value: ReservationStatus.Cancelled, label: 'Cancelada' },
    { value: ReservationStatus.Attended, label: 'Presente' },
    { value: ReservationStatus.NoShow, label: 'Ausente' }
  ];

  readonly accessTypeOptions = [
    { value: ReservationAccessType.MembershipCredit, label: 'Membresía (crédito)' },
    { value: ReservationAccessType.UnlimitedMembership, label: 'Membresía ilimitada' },
    { value: ReservationAccessType.CompensatoryCredit, label: 'Crédito compensatorio' },
    { value: ReservationAccessType.SingleClassTicket, label: 'Pase individual' },
    { value: ReservationAccessType.PromotionalBenefit, label: 'Promoción' }
  ];

  waitlist = signal<WaitlistEntry[]>([]);
  isSubmitting = signal(false);

  // Reserva en mostrador
  deskStudentId = '';
  deskAllowWaitlist = true;

  // Listado general
  reservationsPage = signal<PagedResult<Reservation> | null>(null);
  filterStatus?: ReservationStatus;
  filterAccessType?: ReservationAccessType;
  filterFrom = '';
  filterTo = '';
  filterAffectedBySessionChange = false;
  filterLateCancellation = false;

  // Cancelación
  reservationToCancel = signal<Reservation | null>(null);
  cancelReason = '';
  cancelOverridePolicy = false;
  cancelOverrideReason = '';

  ngOnInit(): void {
    this.loadSessions();
    if (this.authService.isStaff()) {
      this.loadReservations(1);
    }
  }

  loadReservations(page: number): void {
    this.gymApi.getReservations({
      status: this.filterStatus,
      accessType: this.filterAccessType,
      from: this.filterFrom || undefined,
      to: this.filterTo || undefined,
      // Sin tildar no se filtra; tildado trae sólo las que cumplen.
      affectedBySessionChange: this.filterAffectedBySessionChange || undefined,
      isLateCancellation: this.filterLateCancellation || undefined,
      page,
      pageSize: 15
    }).subscribe({
      next: (result) => this.reservationsPage.set(result),
      error: (err) => {
        this.reservationsPage.set(null);
        this.showFeedback(err.error?.message || 'Error al consultar las reservas.', false);
      }
    });
  }

  reserveForStudent(): void {
    const session = this.selectedSession();
    const studentId = this.deskStudentId.trim();
    if (!session || !studentId) return;

    this.isSubmitting.set(true);
    this.gymApi.createReservation({
      classSessionId: session.id,
      studentId,
      allowWaitlist: this.deskAllowWaitlist
    }).subscribe({
      next: (res) => {
        this.isSubmitting.set(false);
        this.deskStudentId = '';
        this.showFeedback(
          res.status === ReservationStatus.Waitlisted
            ? `Clase completa: ${res.studentName} quedó en lista de espera, posición ${res.waitListPosition}.`
            : `Reserva confirmada para ${res.studentName}.`,
          true);
        this.refreshAfterReservationChange();
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.showFeedback(this.describeError(err, 'Error al crear la reserva.'), false);
      }
    });
  }

  openCancelModal(reservation: Reservation): void {
    this.cancelReason = '';
    this.cancelOverridePolicy = false;
    this.cancelOverrideReason = '';
    this.reservationToCancel.set(reservation);
  }

  confirmCancelReservation(): void {
    const target = this.reservationToCancel();
    if (!target) return;

    this.isSubmitting.set(true);
    this.gymApi.cancelReservation(target.id, {
      reason: this.cancelReason.trim() || undefined,
      overridePolicy: this.cancelOverridePolicy,
      policyOverrideReason: this.cancelOverridePolicy ? this.cancelOverrideReason.trim() : undefined
    }).subscribe({
      next: (res) => {
        this.isSubmitting.set(false);
        this.reservationToCancel.set(null);
        this.showFeedback(
          res.isLateCancellation
            ? 'Reserva cancelada fuera de término: el crédito no se devolvió.'
            : 'Reserva cancelada.',
          true);
        this.refreshAfterReservationChange();
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.showFeedback(this.describeError(err, 'Error al cancelar la reserva.'), false);
      }
    });
  }

  isActiveReservation(reservation: Reservation): boolean {
    return ACTIVE_RESERVATION_STATUSES.includes(reservation.status);
  }

  getAccessTypeText(accessType: ReservationAccessType): string {
    return this.accessTypeOptions.find(o => o.value === accessType)?.label ?? 'Desconocido';
  }

  /** Mensaje del error de la API más los motivos de inelegibilidad, cuando los informa. */
  private describeError(err: any, fallback: string): string {
    const body = err.error as Partial<ReservationApiError> | undefined;
    const reasons = Array.isArray(body?.reasons) ? body!.reasons.map(r => r.message).join(' · ') : '';
    const message = body?.message || fallback;
    return reasons && !message.includes(reasons) ? `${message} (${reasons})` : message;
  }

  private refreshAfterReservationChange(): void {
    const session = this.selectedSession();
    if (session) {
      this.loadRoster(session.id);
    }
    this.loadSessions();
    if (this.authService.isStaff()) {
      this.loadReservations(this.reservationsPage()?.pageNumber ?? 1);
    }
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
      next: (list) => this.roster.set(list.filter(r =>
        r.status !== ReservationStatus.Waitlisted && r.status !== ReservationStatus.Pending)),
      error: () => this.roster.set([])
    });
    this.gymApi.getSessionWaitlist(sessionId).subscribe({
      next: (list) => this.waitlist.set(list),
      error: () => this.waitlist.set([])
    });
  }

  checkIn(reservationId: string): void {
    const source = this.authService.isStaff() ? AttendanceSource.SecretaryDesk : AttendanceSource.InstructorInRoom;
    this.gymApi.recordAttendance(reservationId, source).subscribe({
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

  getReservationBadgeClass(status: ReservationStatus): string {
    switch (status) {
      case ReservationStatus.Confirmed: return 'bg-cyan-500/20 text-cyan-300';
      case ReservationStatus.Cancelled: return 'bg-slate-800 text-slate-400';
      case ReservationStatus.Attended: return 'bg-emerald-500/20 text-emerald-300';
      case ReservationStatus.NoShow: return 'bg-rose-500/20 text-rose-300';
      case ReservationStatus.Waitlisted: return 'bg-amber-500/20 text-amber-300';
      case ReservationStatus.Pending: return 'bg-emerald-500/20 text-emerald-300';
      default: return 'bg-slate-800 text-slate-300';
    }
  }

  getReservationStatusText(status: ReservationStatus): string {
    switch (status) {
      case ReservationStatus.Confirmed: return 'Confirmada';
      case ReservationStatus.Cancelled: return 'Cancelada';
      case ReservationStatus.Attended: return 'Presente';
      case ReservationStatus.NoShow: return 'Ausente / No-Show';
      case ReservationStatus.Waitlisted: return 'En Espera';
      case ReservationStatus.Pending: return 'Cupo Ofrecido';
      default: return 'Desconocido';
    }
  }

  getMembershipStatusText(mem: Membership): string {
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

  private showFeedback(msg: string, success: boolean): void {
    this.feedbackMessage.set(msg);
    this.feedbackSuccess.set(success);
    setTimeout(() => this.feedbackMessage.set(null), 5000);
  }
}
