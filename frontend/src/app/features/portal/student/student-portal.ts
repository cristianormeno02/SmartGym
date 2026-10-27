import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { GymApiService } from '../../../core/services/gym-api.service';
import { NotificationsService } from '../../../core/services/notifications.service';
import { Observable } from 'rxjs';
import {
  Membership,
  Reservation,
  ReservationStatus,
  ReservableClassSession,
  MedicalCertificateStatus,
  NotificationItem,
  CreditMovement
} from '../../../core/models/gym.models';
import { MoneyAmountPipe } from '../../../shared/pipes/money-amount.pipe';
import { AppDatePipe } from '../../../shared/pipes/app-date.pipe';
import { DateInputDirective } from '../../../shared/directives/date-input.directive';
import { formatDateDisplay } from '../../../core/utils/date-format';

@Component({
  selector: 'app-student-portal',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MoneyAmountPipe, AppDatePipe, DateInputDirective],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-8">

        <!-- Top Header Profile -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col md:flex-row md:items-center md:justify-between gap-4">
          <div class="flex items-center gap-4">
            @if (authService.currentUser()?.photoUrl) {
              <img [src]="authService.currentUser()?.photoUrl" alt="Avatar" class="w-14 h-14 rounded-full object-cover border-2 border-slate-700 shadow" />
            }
            <div>
              <div class="flex items-center gap-3">
                <span class="h-3 w-3 rounded-full bg-emerald-500 animate-pulse"></span>
                <span class="text-xs uppercase tracking-widest font-semibold text-emerald-400">Portal del Alumno</span>
              </div>
              <h1 class="text-3xl font-extrabold text-white mt-1">{{ authService.currentUser()?.fullName }}</h1>
              <p class="text-sm text-slate-400">Documento: {{ authService.currentUser()?.documentNumber ?? authService.currentUser()?.dni }} | Email: {{ authService.currentUser()?.email }}</p>
            </div>
          </div>

          <!-- Quick Metrics Bar -->
          <div class="flex items-center gap-4 bg-slate-950/60 border border-slate-800/80 px-5 py-3 rounded-xl">
            <div class="text-center">
              <span class="text-xs text-slate-400 uppercase font-medium">Créditos Disp.</span>
              <div class="text-2xl font-black text-cyan-400">
                {{ activeMembership()?.isUnlimited ? '∞' : (activeMembership()?.availableCredits ?? 0) }}
              </div>
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
              <input type="text" [(ngModel)]="uploadExpiryDate" appDateInput placeholder="DD/MM/AAAA" class="bg-slate-900 border border-slate-700 text-xs px-2.5 py-1.5 rounded text-slate-200 focus:outline-none focus:border-rose-500 w-32" />
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
                <span class="text-xs text-slate-400">Cancelación sin penalización: hasta {{ freeCancellationHours }}hs antes</span>
              </div>

              @if (myReservations().length === 0) {
                <div class="text-center py-8 text-slate-500 text-sm">
                  No tienes reservas activas en este momento. ¡Elige una clase disponible abajo!
                </div>
              } @else {
                <div class="space-y-3">
                  @for (r of myReservations(); track r.id) {
                    <div class="bg-slate-950/70 border rounded-xl p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-4"
                         [ngClass]="r.status === ReservationStatus.Pending ? 'border-emerald-600/70' : 'border-slate-800/80'">
                      <div>
                        <div class="flex items-center gap-2">
                          <span class="font-bold text-base text-slate-100">{{ r.activityName }}</span>
                          <span class="text-xs px-2 py-0.5 rounded font-semibold" [ngClass]="getReservationBadgeClass(r)">
                            {{ getReservationStatusText(r) }}
                          </span>
                        </div>
                        <div class="text-xs text-slate-400 mt-1">
                          📆 {{ r.classDate | appDate }} | ⏰ {{ r.startTime | appDate:'timeOnly' }} - {{ r.endTime | appDate:'timeOnly' }} | 📍 {{ r.roomName }}
                        </div>
                        @if (r.status === ReservationStatus.Pending) {
                          <div class="text-xs text-emerald-300 mt-1">
                            Se liberó un cupo para vos. Aceptalo antes del {{ r.waitlistOfferExpiresAtUtc | appDate:'time' }}.
                          </div>
                        } @else if (isLateCancellation(r)) {
                          <div class="text-xs text-amber-400 mt-1">
                            Faltan menos de {{ freeCancellationHours }}hs: si cancelás, el crédito no se devuelve.
                          </div>
                        }
                      </div>

                      <div class="flex items-center gap-2 self-start sm:self-center">
                        @if (r.status === ReservationStatus.Pending) {
                          <button (click)="acceptOffer(r)" [disabled]="isSubmittingReservation()"
                                  class="text-xs font-bold bg-emerald-500 hover:bg-emerald-400 text-slate-950 px-3 py-1.5 rounded-lg transition">
                            Aceptar Cupo
                          </button>
                          <button (click)="declineOffer(r)" [disabled]="isSubmittingReservation()"
                                  class="text-xs font-bold bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 px-3 py-1.5 rounded-lg transition">
                            Rechazar
                          </button>
                        } @else {
                          <button (click)="cancelReservation(r)" [disabled]="isSubmittingReservation()"
                                  class="text-xs font-bold bg-rose-950 hover:bg-rose-900 text-rose-300 border border-rose-800/60 px-3 py-1.5 rounded-lg transition">
                            {{ r.status === ReservationStatus.Waitlisted ? 'Salir de la Espera' : 'Cancelar Turno' }}
                          </button>
                        }
                      </div>
                    </div>
                  }
                </div>
              }
            </div>

            <!-- Available Sessions Schedule -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg space-y-4">
              <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                <div>
                  <h2 class="text-xl font-bold text-white flex items-center gap-2">
                    <span class="text-cyan-400">⚡</span> Clases Disponibles para Reservar
                  </h2>
                  <p class="text-xs text-slate-400 mt-0.5">Explorá y reservá clases abiertas de los próximos 7 días.</p>
                </div>
                <div class="flex items-center gap-3">
                  <span class="text-xs text-slate-400">
                    Mostrando <strong class="text-cyan-300">{{ filteredAvailableClasses().length }}</strong> de {{ availableClasses().length }} clases
                  </span>
                  <button (click)="loadAvailableSessions()" class="text-xs text-cyan-400 hover:text-cyan-300 font-semibold transition">
                    ↻ Actualizar
                  </button>
                </div>
              </div>

              <!-- Interactive Filters Bar -->
              <div class="bg-slate-950/70 border border-slate-800/80 rounded-xl p-4 space-y-3">
                <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                  <!-- Search input -->
                  <div>
                    <label class="block text-[11px] font-semibold text-slate-400 mb-1">Buscar actividad o profe</label>
                    <input type="text"
                           [ngModel]="filterSearchText()"
                           (ngModelChange)="filterSearchText.set($event)"
                           placeholder="Ej. Funcional, Laura..."
                           class="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-cyan-500" />
                  </div>

                  <!-- Activity Filter -->
                  <div>
                    <label class="block text-[11px] font-semibold text-slate-400 mb-1">Actividad</label>
                    <select [ngModel]="filterActivityId()"
                            (ngModelChange)="filterActivityId.set($event)"
                            class="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-cyan-500">
                      <option value="ALL">Todas las actividades</option>
                      @for (act of availableActivities(); track act.id) {
                        <option [value]="act.id">{{ act.name }}</option>
                      }
                    </select>
                  </div>

                  <!-- Date Filter -->
                  <div>
                    <label class="block text-[11px] font-semibold text-slate-400 mb-1">Fecha</label>
                    <select [ngModel]="filterDate()"
                            (ngModelChange)="filterDate.set($event)"
                            class="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-cyan-500">
                      <option value="ALL">Todas las fechas</option>
                      @for (d of availableDateOptions(); track d.date) {
                        <option [value]="d.date">{{ d.display }}</option>
                      }
                    </select>
                  </div>

                  <!-- Time Slot Filter -->
                  <div>
                    <label class="block text-[11px] font-semibold text-slate-400 mb-1">Franja Horaria</label>
                    <select [ngModel]="filterTimeSlot()"
                            (ngModelChange)="filterTimeSlot.set($event)"
                            class="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-cyan-500">
                      <option value="ALL">Todas las franjas</option>
                      <option value="MORNING">Mañana (antes de 12:00)</option>
                      <option value="AFTERNOON">Tarde (12:00 a 18:00)</option>
                      <option value="NIGHT">Noche (a partir de 18:00)</option>
                    </select>
                  </div>
                </div>

                <!-- Secondary filters and actions -->
                <div class="flex flex-wrap items-center justify-between gap-3 pt-2 border-t border-slate-800/60">
                  <div class="flex flex-wrap items-center gap-4">
                    <!-- Room filter -->
                    @if (availableRooms().length > 1) {
                      <div class="flex items-center gap-2">
                        <span class="text-[11px] text-slate-400">Sala:</span>
                        <select [ngModel]="filterRoomId()"
                                (ngModelChange)="filterRoomId.set($event)"
                                class="bg-slate-900 border border-slate-700 rounded-lg px-2 py-1 text-xs text-slate-200 focus:outline-none focus:border-cyan-500">
                          <option value="ALL">Todas las salas</option>
                          @for (r of availableRooms(); track r.id) {
                            <option [value]="r.id">{{ r.name }}</option>
                          }
                        </select>
                      </div>
                    }

                    <!-- Only available spots toggle -->
                    <label class="flex items-center gap-2 text-xs text-slate-300 cursor-pointer select-none">
                      <input type="checkbox"
                             [checked]="filterOnlyAvailableSpots()"
                             (change)="filterOnlyAvailableSpots.set($any($event.target).checked)"
                             class="rounded bg-slate-900 border-slate-700 text-cyan-500 focus:ring-0 focus:ring-offset-0" />
                      <span>Solo con lugares disponibles</span>
                    </label>
                  </div>

                  @if (hasActiveFilters()) {
                    <button (click)="resetFilters()"
                            class="text-xs text-rose-400 hover:text-rose-300 font-medium underline flex items-center gap-1 transition">
                      ✕ Limpiar filtros
                    </button>
                  }
                </div>
              </div>

              <!-- Classes Grid -->
              @if (availableClasses().length === 0) {
                <div class="text-center py-8 text-slate-500 text-sm">
                  No hay clases abiertas para reservar en los próximos 7 días.
                </div>
              } @else if (filteredAvailableClasses().length === 0) {
                <div class="text-center py-8 text-slate-400 text-sm bg-slate-950/40 rounded-xl border border-slate-800/60 space-y-2">
                  <p>No se encontraron clases con los filtros seleccionados.</p>
                  <button (click)="resetFilters()" class="text-xs bg-slate-800 hover:bg-slate-700 text-cyan-300 px-3 py-1.5 rounded-lg transition">
                    Restablecer filtros
                  </button>
                </div>
              } @else {
                <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                  @for (c of filteredAvailableClasses(); track c.classSessionId) {
                    <div class="bg-slate-950/70 border border-slate-800/80 rounded-xl p-4 flex flex-col justify-between hover:border-slate-700 transition">
                      <div>
                        <div class="flex items-center justify-between">
                          <span class="font-bold text-white">{{ c.activityName }}</span>
                          <span class="text-xs px-2 py-0.5 rounded font-bold"
                                [ngClass]="c.availableSpots > 0 ? 'bg-emerald-500/20 text-emerald-400' : 'bg-amber-500/20 text-amber-400'">
                            {{ c.availableSpots > 0 ? c.availableSpots + ' lugares' : 'Completa · ' + c.waitlistCount + ' en espera' }}
                          </span>
                        </div>
                        <div class="text-xs text-slate-400 mt-2 space-y-0.5">
                          <p>⏰ {{ c.startTime | appDate:'timeOnly' }} - {{ c.endTime | appDate:'timeOnly' }} | 📆 {{ c.classDate | appDate }}</p>
                          <p>📍 {{ c.roomName }}</p>
                          <p>👤 {{ c.instructorName }}</p>
                        </div>

                        @if (c.requiresBooking === false) {
                          <p class="mt-2 text-xs text-cyan-300 font-medium">
                            ℹ️ Acceso libre sin reserva previa. El ingreso se registra en recepción por orden de llegada.
                          </p>
                        } @else if (!c.hasActiveReservation && !c.canReserve && !c.canJoinWaitlist) {
                          <ul class="mt-2 text-xs text-rose-300 space-y-0.5">
                            @for (reason of c.ineligibilityReasons; track reason.code) {
                              <li>• {{ reason.message }}</li>
                            } @empty {
                              <li>• {{ c.isWithinBookingWindow ? 'No podés reservar esta clase.' : 'Fuera de la ventana de reserva.' }}</li>
                            }
                          </ul>
                        }
                      </div>

                      <div class="mt-4 pt-3 border-t border-slate-800/80 flex items-center justify-between">
                        <span class="text-xs text-slate-500">Cupo: {{ c.reservedCount }}/{{ c.capacity }}</span>
                        @if (c.requiresBooking === false) {
                          <span class="text-xs font-semibold text-cyan-400 bg-cyan-950/60 border border-cyan-800/80 px-2 py-0.5 rounded">
                            Acceso libre (ingreso en recepción)
                          </span>
                        } @else if (c.hasActiveReservation) {
                          <span class="text-xs font-semibold text-cyan-300">
                            {{ c.activeReservationStatus === ReservationStatus.Waitlisted
                                ? 'En espera #' + c.activeWaitlistPosition
                                : (c.activeReservationStatus === ReservationStatus.Pending ? 'Cupo ofrecido' : 'Ya reservada') }}
                          </span>
                        } @else {
                          <button (click)="reserveClass(c)"
                                  [disabled]="isSubmittingReservation() || (!c.canReserve && !c.canJoinWaitlist)"
                                  class="text-xs font-bold px-3 py-1.5 rounded-lg transition disabled:opacity-40 disabled:cursor-not-allowed"
                                  [ngClass]="c.canReserve ? 'bg-cyan-500 hover:bg-cyan-400 text-slate-950' : 'bg-amber-600 hover:bg-amber-500 text-slate-950'">
                            {{ c.canReserve ? 'Reservar' : (c.canJoinWaitlist ? 'Anotarme en Espera' : 'No disponible') }}
                          </button>
                        }
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
              <div class="flex items-center justify-between mb-3">
                <h3 class="text-lg font-bold text-white">Detalle de Membresía</h3>
                @if (activeMembership(); as mem) {
                  <div class="flex items-center gap-1.5">
                    <span class="text-xs px-2 py-0.5 rounded font-bold" [ngClass]="getMembershipStatusBadgeClass(mem)">
                      {{ getMembershipStatusText(mem) }}
                    </span>
                    @if (mem.isDepleted) {
                      <span class="text-xs px-2 py-0.5 rounded font-bold bg-amber-500/20 text-amber-300 border border-amber-500/30">
                        Sin créditos
                      </span>
                    }
                  </div>
                }
              </div>
              @if (activeMembership(); as mem) {
                <div class="space-y-3 text-sm">
                  <div class="flex justify-between py-1 border-b border-slate-800">
                    <span class="text-slate-400">Plan:</span>
                    <span class="font-semibold text-slate-200">{{ mem.membershipPlanName }}</span>
                  </div>
                  <div class="flex justify-between py-1 border-b border-slate-800">
                    <span class="text-slate-400">Vigencia:</span>
                    <span class="font-semibold text-slate-200">{{ mem.startDate | appDate }} - {{ mem.endDate | appDate }}</span>
                  </div>
                  <div class="flex justify-between py-1 border-b border-slate-800">
                    <span class="text-slate-400">Créditos totales:</span>
                    <span class="font-semibold text-slate-200">{{ mem.isUnlimited ? 'Ilimitado' : mem.totalCredits }}</span>
                  </div>
                  <div class="flex justify-between py-1" [ngClass]="mem.hasOutstandingBalance ? 'border-b border-slate-800' : ''">
                    <span class="text-slate-400">Créditos disponibles:</span>
                    <span class="font-bold text-base" [ngClass]="mem.isDepleted ? 'text-amber-400' : 'text-cyan-400'">
                      {{ mem.isUnlimited ? 'Ilimitado' : mem.availableCredits }}
                    </span>
                  </div>
                  @if (mem.hasOutstandingBalance || (mem.pendingBalance ?? 0) > 0) {
                    <div class="flex justify-between py-1 text-amber-300">
                      <span>Saldo pendiente:</span>
                      <span class="font-bold">\${{ mem.pendingBalance | moneyAmount }}</span>
                    </div>
                  }
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
                <div class="flex items-center gap-2">
                  <span class="text-xs bg-slate-800 px-2 py-0.5 rounded text-slate-400">{{ notifications().length }}</span>
                  <a routerLink="/portal/notificaciones" class="text-xs text-cyan-400 hover:underline">Ver todas</a>
                </div>
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
                        <span class="text-[10px] text-slate-500">{{ (n.sentAtUtc || n.createdAtUtc) | appDate:'time' }}</span>
                      </div>
                      <p class="mt-1 text-slate-300">{{ n.body || n.message }}</p>
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
                        <div class="text-[10px] text-slate-500">{{ m.createdAtUtc | appDate:'time' }}</div>
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
  private readonly notificationsService = inject(NotificationsService);

  activeMembership = signal<Membership | null>(null);
  myReservations = signal<Reservation[]>([]);
  availableClasses = signal<ReservableClassSession[]>([]);

  // Filter signals for available classes
  filterActivityId = signal<string>('ALL');
  filterDate = signal<string>('ALL');
  filterTimeSlot = signal<string>('ALL');
  filterOnlyAvailableSpots = signal<boolean>(false);
  filterRoomId = signal<string>('ALL');
  filterSearchText = signal<string>('');

  readonly ReservationStatus = ReservationStatus;
  /** Debe coincidir con Reservations:FreeCancellationHoursBeforeStart del backend; sólo se usa para avisar. */
  readonly freeCancellationHours = 2;
  medicalStatus = signal<MedicalCertificateStatus | null>(null);
  notifications = signal<NotificationItem[]>([]);
  creditMovements = signal<CreditMovement[]>([]);

  uploadExpiryDate = '';
  isUploadingCert = signal(false);
  isSubmittingReservation = signal(false);
  feedbackMessage = signal<string | null>(null);
  feedbackSuccess = signal(true);

  // Computeds for filter options
  readonly availableActivities = computed(() => {
    const map = new Map<string, string>();
    for (const c of this.availableClasses()) {
      map.set(c.activityId, c.activityName);
    }
    return Array.from(map.entries())
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly availableRooms = computed(() => {
    const map = new Map<string, string>();
    for (const c of this.availableClasses()) {
      map.set(c.roomId, c.roomName);
    }
    return Array.from(map.entries())
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  readonly availableDateOptions = computed(() => {
    const dates = new Set<string>();
    for (const c of this.availableClasses()) {
      if (c.classDate) dates.add(c.classDate);
    }
    return Array.from(dates)
      .sort()
      .map(d => ({ date: d, display: formatDateDisplay(d) }));
  });

  readonly hasActiveFilters = computed(() => {
    return (
      this.filterActivityId() !== 'ALL' ||
      this.filterDate() !== 'ALL' ||
      this.filterTimeSlot() !== 'ALL' ||
      this.filterOnlyAvailableSpots() ||
      this.filterRoomId() !== 'ALL' ||
      this.filterSearchText().trim().length > 0
    );
  });

  readonly filteredAvailableClasses = computed(() => {
    const all = this.availableClasses();
    const actId = this.filterActivityId();
    const date = this.filterDate();
    const slot = this.filterTimeSlot();
    const onlyAvailable = this.filterOnlyAvailableSpots();
    const roomId = this.filterRoomId();
    const search = this.filterSearchText().trim().toLowerCase();

    return all.filter(c => {
      // Activity
      if (actId !== 'ALL' && c.activityId !== actId) {
        return false;
      }

      // Room
      if (roomId !== 'ALL' && c.roomId !== roomId) {
        return false;
      }

      // Availability
      if (onlyAvailable && c.availableSpots <= 0) {
        return false;
      }

      // Date
      if (date !== 'ALL' && c.classDate !== date) {
        return false;
      }

      // Time slot
      if (slot !== 'ALL') {
        const time = c.startTime || '';
        // Comparar horas en formato HH:mm
        const hour = parseInt(time.split(':')[0], 10);
        if (!isNaN(hour)) {
          if (slot === 'MORNING' && hour >= 12) return false;
          if (slot === 'AFTERNOON' && (hour < 12 || hour >= 18)) return false;
          if (slot === 'NIGHT' && hour < 18) return false;
        }
      }

      // Search text
      if (search) {
        const actMatch = c.activityName?.toLowerCase().includes(search);
        const instMatch = c.instructorName?.toLowerCase().includes(search);
        if (!actMatch && !instMatch) {
          return false;
        }
      }

      return true;
    });
  });

  ngOnInit(): void {
    const studentId = this.authService.currentUser()?.personId;
    if (studentId) {
      this.loadStudentData(studentId);
    }
  }

  resetFilters(): void {
    this.filterActivityId.set('ALL');
    this.filterDate.set('ALL');
    this.filterTimeSlot.set('ALL');
    this.filterOnlyAvailableSpots.set(false);
    this.filterRoomId.set('ALL');
    this.filterSearchText.set('');
  }

  loadStudentData(studentId: string): void {
    this.gymApi.getActiveMembership(studentId).subscribe({
      next: (mem) => this.activeMembership.set(mem),
      error: () => {
        this.gymApi.getCurrentMemberships(studentId).subscribe({
          next: (list) => this.activeMembership.set(list.length > 0 ? list[0] : null),
          error: () => this.activeMembership.set(null)
        });
      }
    });

    this.gymApi.getMyUpcomingReservations().subscribe({
      next: (res) => this.myReservations.set(res),
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
    // El servidor evalúa cupos y elegibilidad del alumno autenticado para cada clase.
    this.gymApi.getAvailableClasses({ from: today, to: nextWeek }).subscribe({
      next: (classes) => this.availableClasses.set(classes),
      error: () => this.availableClasses.set([])
    });
  }

  reserveClass(classSession: ReservableClassSession): void {
    const studentId = this.authService.currentUser()?.personId;
    if (!studentId) return;

    this.isSubmittingReservation.set(true);
    this.gymApi.createReservation({ classSessionId: classSession.classSessionId }).subscribe({
      next: (res) => {
        this.isSubmittingReservation.set(false);
        this.showFeedback(
          res.status === ReservationStatus.Waitlisted
            ? `La clase está completa: quedaste en lista de espera, posición ${res.waitListPosition}.`
            : '¡Reserva confirmada con éxito!',
          true);
        this.loadStudentData(studentId);
      },
      error: (err) => {
        this.isSubmittingReservation.set(false);
        this.showFeedback(err.error?.message || 'Error al procesar la reserva.', false);
        this.loadAvailableSessions();
      }
    });
  }

  cancelReservation(reservation: Reservation): void {
    if (this.isLateCancellation(reservation) &&
        !confirm(`Faltan menos de ${this.freeCancellationHours} horas para la clase. Si cancelás ahora, el crédito no se devuelve. ¿Cancelar igual?`)) {
      return;
    }

    this.runReservationAction(
      this.gymApi.cancelReservation(reservation.id),
      (res) => res.isLateCancellation
        ? 'Reserva cancelada fuera de término: el crédito no se devuelve.'
        : 'Reserva cancelada correctamente.',
      'Error al cancelar la reserva.');
  }

  acceptOffer(reservation: Reservation): void {
    this.runReservationAction(
      this.gymApi.acceptWaitlistOffer(reservation.id),
      () => '¡Cupo aceptado! Tu reserva quedó confirmada.',
      'No se pudo aceptar el cupo.');
  }

  declineOffer(reservation: Reservation): void {
    this.runReservationAction(
      this.gymApi.declineWaitlistOffer(reservation.id),
      () => 'Rechazaste el cupo ofrecido.',
      'No se pudo rechazar el cupo.');
  }

  /** Cancelar una reserva confirmada a menos de `freeCancellationHours` del inicio no devuelve el crédito. */
  isLateCancellation(reservation: Reservation): boolean {
    if (reservation.status !== ReservationStatus.Confirmed || !reservation.startsAtUtc) return false;
    const hoursLeft = (new Date(reservation.startsAtUtc).getTime() - Date.now()) / 3_600_000;
    return hoursLeft < this.freeCancellationHours;
  }

  getReservationStatusText(reservation: Reservation): string {
    switch (reservation.status) {
      case ReservationStatus.Confirmed: return 'Confirmada';
      case ReservationStatus.Waitlisted: return `Lista de Espera #${reservation.waitListPosition}`;
      case ReservationStatus.Pending: return 'Cupo Ofrecido';
      default: return 'Finalizada';
    }
  }

  getReservationBadgeClass(reservation: Reservation): string {
    switch (reservation.status) {
      case ReservationStatus.Confirmed: return 'bg-cyan-500/20 text-cyan-300';
      case ReservationStatus.Waitlisted: return 'bg-amber-500/20 text-amber-300';
      case ReservationStatus.Pending: return 'bg-emerald-500/20 text-emerald-300';
      default: return 'bg-slate-700 text-slate-300';
    }
  }

  private runReservationAction(
    action: Observable<Reservation>,
    successMessage: (result: Reservation) => string,
    fallbackError: string
  ): void {
    const studentId = this.authService.currentUser()?.personId;
    if (!studentId) return;

    this.isSubmittingReservation.set(true);
    action.subscribe({
      next: (res) => {
        this.isSubmittingReservation.set(false);
        this.showFeedback(successMessage(res), true);
        this.loadStudentData(studentId);
      },
      error: (err) => {
        this.isSubmittingReservation.set(false);
        this.showFeedback(err.error?.message || fallbackError, false);
        this.loadStudentData(studentId);
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
        this.notificationsService.refreshUnreadCount().subscribe({ error: () => {} });
      }
    });
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

  getMembershipStatusBadgeClass(mem: Membership): string {
    const effective = mem.effectiveStatus ?? mem.status;
    switch (effective) {
      case 0: return 'bg-amber-500/20 text-amber-300 border border-amber-500/30';
      case 1: return 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/30';
      case 2: return 'bg-slate-700 text-slate-300';
      case 3: return 'bg-orange-500/20 text-orange-300 border border-orange-500/30';
      case 4: return 'bg-rose-500/20 text-rose-300 border border-rose-500/30';
      default: return 'bg-slate-800 text-slate-400';
    }
  }

  private showFeedback(msg: string, success: boolean): void {
    this.feedbackMessage.set(msg);
    this.feedbackSuccess.set(success);
    setTimeout(() => this.feedbackMessage.set(null), 5000);
  }
}
