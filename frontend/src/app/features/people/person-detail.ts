import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PeopleService } from '../../core/services/people.service';
import { AuthService } from '../../core/services/auth.service';
import { UserManagementService } from '../../core/services/user-management.service';
import { GymApiService } from '../../core/services/gym-api.service';
import { UserAccountDto, ROLE_DEFINITIONS } from '../../core/models/user-management.models';
import { Membership, MembershipDetail, MembershipStatus } from '../../core/models/gym.models';
import { AssignMembershipModalComponent } from '../catalog/assign-membership-modal/assign-membership-modal';
import { MoneyAmountPipe } from '../../shared/pipes/money-amount.pipe';
import { AppDatePipe } from '../../shared/pipes/app-date.pipe';
import {
  PersonDetail,
  PersonStatus,
  PersonStatusLabels,
  DocumentType,
  DocumentTypeLabels,
  Gender,
  GenderLabels
} from '../../core/models/person.model';
import { apiErrorMessage } from '../../core/utils/api-error';

@Component({
  selector: 'app-person-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AssignMembershipModalComponent, MoneyAmountPipe, AppDatePipe],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-5xl mx-auto space-y-6">

        <!-- Top Navigation -->
        <div class="flex items-center justify-between">
          <a routerLink="/portal/personas"
             class="inline-flex items-center gap-2 text-sm text-slate-400 hover:text-white transition">
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 19l-7-7m0 0l7-7m-7 7h18"/>
            </svg>
            Volver al listado
          </a>
          <div class="flex items-center gap-3">
            <a [routerLink]="['/portal/personas', person()?.id, 'editar']"
               class="bg-slate-800 hover:bg-slate-700 text-white font-bold text-xs px-4 py-2 rounded-xl transition border border-slate-700">
              ✏️ Editar Datos
            </a>
            <button (click)="openStatusModal()"
                    class="bg-cyan-600 hover:bg-cyan-500 text-white font-bold text-xs px-4 py-2 rounded-xl transition shadow">
              🔄 Cambiar Estado
            </button>
          </div>
        </div>

        <!-- Feedback Alert -->
        @if (feedback()) {
          <div class="p-4 rounded-xl text-sm font-semibold transition"
               [ngClass]="feedbackSuccess() ? 'bg-emerald-950/60 border border-emerald-800 text-emerald-300' : 'bg-rose-950/60 border border-rose-800 text-rose-300'">
            {{ feedback() }}
          </div>
        }

        @if (peopleService.isBusy() && !person()) {
          <div class="py-16 text-center text-slate-500">
            <div class="inline-block animate-spin rounded-full h-8 w-8 border-t-2 border-b-2 border-cyan-400"></div>
            <p class="mt-2 text-xs">Cargando ficha personal...</p>
          </div>
        } @else if (person(); as p) {

          <!-- Header Profile Card -->
          <div class="bg-slate-900 border border-slate-800 rounded-3xl p-6 sm:p-8 shadow-xl relative overflow-hidden"
               [ngClass]="{'border-rose-600/60': p.status === PersonStatus.Blocked}">

            <div class="flex flex-col sm:flex-row items-center sm:items-start gap-6">

              <!-- Avatar & Upload/Delete -->
              <div class="flex flex-col items-center gap-2">
                <div class="relative group">
                  @if (p.photoUrl) {
                    <img [src]="p.photoUrl" alt="" class="w-28 h-28 rounded-2xl object-cover border-2 border-slate-700 shadow-lg" />
                  } @else {
                    <div class="w-28 h-28 rounded-2xl bg-slate-800 border-2 border-slate-700 flex items-center justify-center font-black text-slate-400 text-3xl shadow-lg">
                      {{ p.firstName.charAt(0) }}{{ p.lastName.charAt(0) }}
                    </div>
                  }
                </div>

                <div class="flex items-center gap-1.5 mt-1">
                  <input #fileInput type="file" (change)="onFileSelected($event)" accept="image/jpeg,image/png,image/webp" class="hidden" />
                  <button (click)="fileInput.click()" [disabled]="isUploadingPhoto() || p.status === PersonStatus.Deceased"
                          class="text-[11px] bg-slate-800 hover:bg-slate-700 text-cyan-400 font-semibold px-2.5 py-1 rounded-lg border border-slate-700 transition disabled:opacity-40">
                    {{ isUploadingPhoto() ? 'Subiendo...' : (p.photoUrl ? 'Cambiar Foto' : 'Subir Foto') }}
                  </button>
                  @if (p.photoUrl) {
                    <button (click)="deletePhoto()" [disabled]="isUploadingPhoto() || p.status === PersonStatus.Deceased"
                            title="Eliminar foto"
                            class="text-[11px] bg-rose-950/40 hover:bg-rose-900/60 text-rose-300 font-semibold px-2 py-1 rounded-lg border border-rose-800/60 transition disabled:opacity-40">
                      🗑️
                    </button>
                  }
                </div>
              </div>

              <!-- Info & Badges -->
              <div class="flex-1 text-center sm:text-left space-y-2">
                <div class="flex flex-wrap items-center justify-center sm:justify-start gap-2">
                  <span class="text-xs px-3 py-1 rounded-full font-bold border"
                        [ngClass]="getStatusBadgeClass(p.status)">
                    {{ getStatusLabel(p.status) }}
                  </span>
                  @if (p.hasUserAccount) {
                    <span class="text-xs bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 px-3 py-1 rounded-full font-semibold">
                      Cuenta de Usuario Activa
                    </span>
                  } @else {
                    <span class="text-xs bg-slate-800 text-slate-400 border border-slate-700 px-3 py-1 rounded-full">
                      Sin cuenta de usuario
                    </span>
                  }
                </div>

                <h2 class="text-3xl font-black text-white">
                  {{ p.lastName }}, {{ p.firstName }}
                </h2>

                <div class="text-sm text-slate-400 flex flex-wrap items-center justify-center sm:justify-start gap-4">
                  @if (p.document) {
                    <div>
                      <span class="text-slate-500">{{ getDocumentTypeLabel(p.document.type) }}:</span>
                      <span class="font-mono text-slate-200 ml-1 font-semibold">{{ p.document.number }}</span>
                    </div>
                  }
                  @if (p.email) {
                    <div>
                      <span class="text-slate-500">Email:</span>
                      <span class="text-slate-200 ml-1">{{ p.email }}</span>
                    </div>
                  }
                  @if (p.primaryPhone) {
                    <div>
                      <span class="text-slate-500">Tel:</span>
                      <span class="text-slate-200 ml-1">{{ p.primaryPhone }}</span>
                    </div>
                  }
                </div>
              </div>

            </div>

            <!-- Blocked Warning Banner -->
            @if (p.status === PersonStatus.Blocked) {
              <div class="mt-6 bg-rose-950/60 border border-rose-800 rounded-xl p-4 flex items-start gap-3">
                <span class="text-xl">⛔</span>
                <div>
                  <h4 class="font-bold text-rose-200">Persona en estado BLOQUEADA</h4>
                  <p class="text-xs text-rose-300 mt-0.5">
                    No puede realizar nuevas reservas, contratar membresías ni registrar asistencia a clases.
                  </p>
                  @if (p.statusReason) {
                    <p class="text-xs text-rose-400 font-mono mt-1">Motivo: "{{ p.statusReason }}"</p>
                  }
                </div>
              </div>
            }
          </div>

          <!-- Card: Cuenta de Acceso y Roles (Solo Administrator) -->
          @if (authService.isAdmin()) {
            <div class="bg-slate-900 border border-slate-800 rounded-3xl p-6 shadow-xl space-y-4" data-testid="user-account-card">
              <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 border-b border-slate-800/80 pb-4">
                <div>
                  <h3 class="text-base font-bold text-white flex items-center gap-2">
                    <span class="text-xl">🔐</span> Cuenta de Acceso y Roles
                  </h3>
                  <p class="text-xs text-slate-400 mt-0.5">
                    Gestión de credenciales de inicio de sesión y asignación de roles operativos.
                  </p>
                </div>

                @if (isLoadingAccount()) {
                  <span class="text-xs text-slate-400 animate-pulse">Cargando cuenta...</span>
                } @else if (userAccount()?.hasAccount) {
                  <div class="flex flex-wrap items-center gap-2">
                    <button (click)="openRolesModal()" data-testid="btn-manage-roles"
                            class="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-cyan-400 border border-slate-700 rounded-xl text-xs font-bold transition">
                      ⚙️ Gestionar Roles
                    </button>
                    <button (click)="openResetPasswordModal()" data-testid="btn-reset-password"
                            class="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-amber-300 border border-slate-700 rounded-xl text-xs font-bold transition">
                      🔑 Restablecer Contraseña
                    </button>
                    <button (click)="openStatusAccountModal()" data-testid="btn-toggle-status"
                            class="px-3 py-1.5 rounded-xl text-xs font-bold transition border"
                            [ngClass]="userAccount()?.isActive ? 'bg-rose-950/40 hover:bg-rose-900/60 text-rose-300 border-rose-800/60' : 'bg-emerald-950/40 hover:bg-emerald-900/60 text-emerald-300 border-emerald-800/60'">
                      {{ userAccount()?.isActive ? '⛔ Suspender Cuenta' : '✅ Reactivar Cuenta' }}
                    </button>
                  </div>
                } @else {
                  <div>
                    <button (click)="openProvisionModal()" data-testid="btn-provision-account"
                            [disabled]="!p.email || p.status === PersonStatus.Deceased"
                            [title]="!p.email ? 'Debe registrar un correo electrónico en la ficha para crear una cuenta' : (p.status === PersonStatus.Deceased ? 'No se pueden crear cuentas para personas fallecidas' : '')"
                            class="px-4 py-2 bg-cyan-600 hover:bg-cyan-500 disabled:opacity-40 disabled:cursor-not-allowed text-white rounded-xl text-xs font-bold transition shadow">
                      + Crear Cuenta de Acceso
                    </button>
                  </div>
                }
              </div>

              <!-- Body of Card -->
              @if (userAccount(); as acc) {
                @if (acc.hasAccount) {
                  <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 text-xs pt-1">
                    <div>
                      <span class="text-slate-500 uppercase font-semibold block">Estado de la Cuenta</span>
                      <span class="inline-flex items-center gap-1.5 mt-1 font-bold px-2.5 py-0.5 rounded-full border text-xs"
                            [ngClass]="acc.isActive ? 'bg-emerald-950/50 text-emerald-400 border-emerald-800' : 'bg-rose-950/50 text-rose-400 border-rose-800'"
                            data-testid="account-status-badge">
                        {{ acc.isActive ? 'Activa' : 'Suspendida' }}
                      </span>
                    </div>

                    <div>
                      <span class="text-slate-500 uppercase font-semibold block">Nombre de Usuario / Email</span>
                      <span class="font-mono text-slate-200 mt-1 block font-semibold" data-testid="account-username">{{ acc.username }}</span>
                    </div>

                    <div>
                      <span class="text-slate-500 uppercase font-semibold block">Último Acceso</span>
                      <span class="text-slate-300 mt-1 block">
                        {{ acc.lastLoginUtc ? (acc.lastLoginUtc | appDate:'time') : 'Nunca ha iniciado sesión' }}
                      </span>
                    </div>
                  </div>

                  <div class="pt-2">
                    <span class="text-slate-500 uppercase font-semibold text-xs block mb-1.5">Roles Operativos Asignados</span>
                    <div class="flex flex-wrap gap-2 items-center" data-testid="account-roles-container">
                      @for (role of acc.roles; track role) {
                        <span class="text-xs px-3 py-1 rounded-full font-bold border" [ngClass]="getRoleBadgeClass(role)">
                          {{ getRoleLabel(role) }}
                        </span>
                      }
                      @if (acc.roles.length === 0) {
                        <span class="text-xs text-slate-500 italic">Sin roles operativos asignados</span>
                      }
                    </div>
                  </div>
                } @else {
                  <div class="py-2 text-xs text-slate-400 space-y-2">
                    <p class="font-semibold text-slate-300" data-testid="no-account-notice">
                      ℹ️ Sin cuenta de usuario creada
                    </p>
                    @if (!p.email) {
                      <p class="text-rose-400 font-medium" data-testid="no-email-warning">
                        ⚠️ Esta persona no tiene correo electrónico cargado. Para crear una cuenta de acceso, primero edita los datos de la ficha y asigna un email válido.
                      </p>
                    }
                    @if (p.status === PersonStatus.Deceased) {
                      <p class="text-rose-400 font-medium" data-testid="deceased-warning">
                        ⚠️ No se pueden aprovisionar credenciales de acceso a personas registradas como fallecidas.
                      </p>
                    }
                  </div>
                }
              }
            </div>
          }

          <!-- Card: Membresías del Alumno -->
          <div class="bg-slate-900 border border-slate-800 rounded-3xl p-6 shadow-xl space-y-4" data-testid="student-memberships-card">
            <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 border-b border-slate-800/80 pb-4">
              <div>
                <h3 class="text-base font-bold text-white flex items-center gap-2">
                  <span class="text-xl">💳</span> Membresías del Alumno
                </h3>
                <p class="text-xs text-slate-400 mt-0.5">
                  Historial de planes contratados, estado de vigencia y asignación directa de membresías.
                </p>
              </div>

              <div>
                <button (click)="openAssignMembershipModal()"
                        data-testid="btn-assign-membership"
                        [disabled]="p.status !== PersonStatus.Active"
                        [title]="p.status !== PersonStatus.Active ? 'Solo se pueden asignar membresías a personas en estado ACTIVA' : 'Asignar plan de membresía'"
                        class="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-40 disabled:cursor-not-allowed text-white rounded-xl text-xs font-bold transition shadow">
                  + Asignar Membresía
                </button>
              </div>
            </div>

            @if (p.status !== PersonStatus.Active) {
              <div class="p-3 bg-amber-950/40 border border-amber-800/60 text-amber-300 rounded-xl text-xs font-medium" data-testid="inactive-person-membership-warning">
                ⚠️ Solo se pueden asignar nuevas membresías a personas que se encuentren en estado <strong>ACTIVA</strong>.
              </div>
            }

            @if (isLoadingMemberships()) {
              <div class="py-4 text-xs text-slate-400 animate-pulse">
                Cargando membresías del alumno...
              </div>
            } @else if (memberships().length === 0) {
              <div class="py-4 text-xs text-slate-400 italic" data-testid="no-memberships-notice">
                No registra membresías contratadas actualmente.
              </div>
            } @else {
              <div class="space-y-3">
                @for (mem of memberships(); track mem.id) {
                  <div class="bg-slate-950/70 border border-slate-800 rounded-2xl p-4 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
                    <div class="space-y-1">
                      <div class="flex items-center gap-2 flex-wrap">
                        <span class="text-sm font-bold text-white">{{ mem.membershipPlanName }}</span>
                        <span class="text-[11px] px-2.5 py-0.5 rounded-full font-bold border"
                              [ngClass]="getMembershipStatusBadgeClass(mem)">
                          {{ getMembershipStatusLabel(mem) }}
                        </span>
                      </div>
                      <div class="text-xs text-slate-400 flex flex-wrap items-center gap-4">
                        <span>Vigencia: <strong class="text-slate-200 font-mono">{{ mem.startDate | appDate }}</strong> al <strong class="text-slate-200 font-mono">{{ mem.endDate | appDate }}</strong></span>
                        <span>
                          Cupo:
                          <strong class="text-cyan-400">
                            {{ (mem.isUnlimited || (mem.totalCredits === 0 && mem.availableCredits === 0)) ? 'Pase Libre Ilimitado (∞)' : (mem.availableCredits + ' / ' + mem.totalCredits + ' créditos') }}
                          </strong>
                        </span>
                        @if (mem.purchasePrice !== undefined && mem.purchasePrice !== null) {
                          <span>Importe: <strong class="text-emerald-400">\${{ mem.purchasePrice | moneyAmount }}</strong></span>
                        }
                      </div>
                    </div>
                  </div>
                }
              </div>
            }
          </div>

          <!-- Master Data Grid -->
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">

            <!-- Card: Identificación y Datos Personales -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg space-y-4">
              <h3 class="text-sm font-bold uppercase tracking-wider text-cyan-400 flex items-center gap-2">
                <span>📋</span> Datos Personales
              </h3>
              <dl class="grid grid-cols-2 gap-4 text-xs">
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Tipo Documento</dt>
                  <dd class="text-slate-200 font-bold mt-0.5">{{ p.document ? getDocumentTypeLabel(p.document.type) : '—' }}</dd>
                </div>
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Número Documento</dt>
                  <dd class="text-slate-200 font-mono font-bold mt-0.5">{{ p.document?.number ?? '—' }}</dd>
                </div>
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">País Emisor</dt>
                  <dd class="text-slate-200 font-bold mt-0.5">{{ p.document?.issuingCountry ?? '—' }}</dd>
                </div>
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Doc. Normalizado</dt>
                  <dd class="text-slate-400 font-mono mt-0.5">{{ p.document?.numberNormalized ?? '—' }}</dd>
                </div>
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Fecha de Nacimiento</dt>
                  <dd class="text-slate-200 mt-0.5">{{ (p.birthDate | appDate) || '—' }}</dd>
                </div>
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Género</dt>
                  <dd class="text-slate-200 mt-0.5">{{ p.gender !== undefined && p.gender !== null ? getGenderLabel(p.gender) : '—' }}</dd>
                </div>
              </dl>
            </div>

            <!-- Card: Domicilio y Contacto -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg space-y-4">
              <h3 class="text-sm font-bold uppercase tracking-wider text-cyan-400 flex items-center gap-2">
                <span>📍</span> Contacto y Domicilio
              </h3>
              <dl class="grid grid-cols-2 gap-4 text-xs">
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Teléfono Principal</dt>
                  <dd class="text-slate-200 mt-0.5">{{ p.primaryPhone ?? '—' }}</dd>
                </div>
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Teléfono Secundario</dt>
                  <dd class="text-slate-200 mt-0.5">{{ p.secondaryPhone ?? '—' }}</dd>
                </div>
                <div class="col-span-2">
                  <dt class="text-slate-500 uppercase font-semibold">Domicilio</dt>
                  <dd class="text-slate-200 mt-0.5">
                    @if (p.address?.street) {
                      {{ p.address?.street }} {{ p.address?.number }}
                      @if (p.address?.floor) { Piso {{ p.address?.floor }} }
                      @if (p.address?.apartment) { Dpto {{ p.address?.apartment }} }
                      <br />
                      {{ p.address?.city }}, {{ p.address?.state }} (CP {{ p.address?.postalCode }})
                    } @else {
                      <span class="text-slate-500 italic">Sin domicilio registrado</span>
                    }
                  </dd>
                </div>
              </dl>
            </div>

            <!-- Card: Contacto de Emergencia -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg space-y-4">
              <h3 class="text-sm font-bold uppercase tracking-wider text-cyan-400 flex items-center gap-2">
                <span>🚨</span> Contacto de Emergencia
              </h3>
              @if (p.emergencyContact?.name) {
                <dl class="grid grid-cols-2 gap-4 text-xs">
                  <div>
                    <dt class="text-slate-500 uppercase font-semibold">Nombre Completo</dt>
                    <dd class="text-slate-200 font-bold mt-0.5">{{ p.emergencyContact?.name }}</dd>
                  </div>
                  <div>
                    <dt class="text-slate-500 uppercase font-semibold">Teléfono</dt>
                    <dd class="text-slate-200 font-bold mt-0.5">{{ p.emergencyContact?.phone }}</dd>
                  </div>
                  <div class="col-span-2">
                    <dt class="text-slate-500 uppercase font-semibold">Vínculo / Parentesco</dt>
                    <dd class="text-slate-200 mt-0.5">{{ p.emergencyContact?.relationship }}</dd>
                  </div>
                </dl>
              } @else {
                <p class="text-xs text-slate-500 italic">No posee contacto de emergencia registrado.</p>
              }
            </div>

            <!-- Card: Auditoría de Estado y Versión -->
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-lg space-y-4">
              <h3 class="text-sm font-bold uppercase tracking-wider text-cyan-400 flex items-center gap-2">
                <span>🛡️</span> Auditoría y Registro
              </h3>
              <dl class="grid grid-cols-2 gap-4 text-xs">
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Versión de Ficha</dt>
                  <dd class="text-slate-200 font-mono mt-0.5">v{{ p.version }}</dd>
                </div>
                <div>
                  <dt class="text-slate-500 uppercase font-semibold">Fecha de Alta</dt>
                  <dd class="text-slate-200 mt-0.5">{{ p.createdAtUtc | appDate:'time' }}</dd>
                </div>
                @if (p.statusReason) {
                  <div class="col-span-2">
                    <dt class="text-slate-500 uppercase font-semibold">Último Motivo de Cambio de Estado</dt>
                    <dd class="text-slate-300 italic mt-0.5">"{{ p.statusReason }}"</dd>
                  </div>
                }
                @if (p.statusChangedAtUtc) {
                  <div>
                    <dt class="text-slate-500 uppercase font-semibold">Fecha Cambio de Estado</dt>
                    <dd class="text-slate-200 mt-0.5">{{ p.statusChangedAtUtc | appDate:'time' }}</dd>
                  </div>
                }
              </dl>
            </div>

          </div>

        }

        <!-- Status Change Modal -->
        @if (showStatusModal()) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl max-w-md w-full p-6 space-y-4 shadow-2xl">
              <h3 class="text-lg font-bold text-white flex items-center gap-2">
                <span>🔄</span> Cambiar Estado de Persona
              </h3>
              <p class="text-xs text-slate-400">
                Seleccione el nuevo estado para <span class="font-bold text-slate-200">{{ person()?.lastName }}, {{ person()?.firstName }}</span> e ingrese el motivo del cambio.
              </p>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Nuevo Estado <span class="text-rose-400">*</span></label>
                <select [(ngModel)]="newStatus" class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500">
                  <option [ngValue]="PersonStatus.Active">Activa</option>
                  <option [ngValue]="PersonStatus.Inactive">Inactiva</option>
                  <option [ngValue]="PersonStatus.Blocked">Bloqueada</option>
                  @if (authService.isAdmin()) {
                    <option [ngValue]="PersonStatus.Deceased">Fallecida (Solo Admin)</option>
                  }
                </select>
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Motivo del Cambio <span class="text-rose-400">*</span></label>
                <textarea [(ngModel)]="statusReason" rows="3"
                          placeholder="Indique el motivo administrativo o justificación obligatoria..."
                          class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"></textarea>
              </div>

              @if (modalError()) {
                <p class="text-xs text-rose-400 font-semibold">{{ modalError() }}</p>
              }

              <div class="flex items-center justify-end gap-2 pt-2">
                <button (click)="closeStatusModal()" class="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-xs font-semibold transition">
                  Cancelar
                </button>
                <button (click)="submitStatusChange()" [disabled]="isSubmittingStatus() || !statusReason.trim()"
                        class="px-5 py-2 bg-cyan-600 hover:bg-cyan-500 disabled:opacity-40 text-white rounded-xl text-xs font-bold transition">
                  {{ isSubmittingStatus() ? 'Cambiando...' : 'Confirmar Cambio' }}
                </button>
              </div>
            </div>
          </div>
        }

        <!-- Provision User Modal -->
        @if (showProvisionModal()) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4" data-testid="modal-provision">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl max-w-md w-full p-6 space-y-4 shadow-2xl">
              <h3 class="text-lg font-bold text-white flex items-center gap-2">
                <span>➕</span> Crear Cuenta de Acceso
              </h3>
              <p class="text-xs text-slate-400">
                Aprovisionar usuario para <span class="font-bold text-slate-200">{{ person()?.lastName }}, {{ person()?.firstName }}</span> ({{ person()?.email }}).
              </p>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Contraseña Inicial <span class="text-rose-400">*</span></label>
                <input type="password" [(ngModel)]="provisionPassword" placeholder="Contraseña segura" data-testid="input-provision-password"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 font-mono" />
                <p class="text-[11px] text-slate-500 mt-1">Mínimo 8 caracteres, al menos una mayúscula, un número y un símbolo.</p>
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-2">Roles Iniciales</label>
                <div class="grid grid-cols-2 gap-2 text-xs">
                  @for (role of ROLE_DEFINITIONS; track role.id) {
                    <label class="flex items-center gap-2 p-2 bg-slate-950/60 border border-slate-800 rounded-xl cursor-pointer hover:border-slate-700">
                      <input type="checkbox" [(ngModel)]="provisionRoleIds[role.id]" [attr.data-testid]="'checkbox-role-' + role.id"
                             class="rounded bg-slate-800 border-slate-700 text-cyan-500 focus:ring-0" />
                      <span class="text-slate-200 font-semibold">{{ role.label }}</span>
                    </label>
                  }
                </div>
              </div>

              @if (provisionError()) {
                <div class="p-3 bg-rose-950/60 border border-rose-800 text-rose-300 rounded-xl text-xs font-semibold" data-testid="provision-error-msg">
                  {{ provisionError() }}
                </div>
              }

              <div class="flex items-center justify-end gap-2 pt-2">
                <button (click)="closeProvisionModal()" class="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-xs font-semibold transition">
                  Cancelar
                </button>
                <button (click)="submitProvision()" [disabled]="isSubmittingProvision() || !provisionPassword" data-testid="btn-submit-provision"
                        class="px-5 py-2 bg-cyan-600 hover:bg-cyan-500 disabled:opacity-40 text-white rounded-xl text-xs font-bold transition">
                  {{ isSubmittingProvision() ? 'Creando...' : 'Crear Cuenta' }}
                </button>
              </div>
            </div>
          </div>
        }

        <!-- Manage Roles Modal -->
        @if (showRolesModal()) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4" data-testid="modal-roles">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl max-w-md w-full p-6 space-y-4 shadow-2xl">
              <h3 class="text-lg font-bold text-white flex items-center gap-2">
                <span>⚙️</span> Gestionar Roles Operativos
              </h3>
              <p class="text-xs text-slate-400">
                Seleccione los roles asignados a <span class="font-bold text-slate-200">{{ person()?.lastName }}, {{ person()?.firstName }}</span>.
              </p>

              @if (isSelfAccount()) {
                <div class="p-3 bg-amber-950/60 border border-amber-800 text-amber-300 rounded-xl text-xs font-semibold" data-testid="self-modification-warning">
                  ⚠️ Advertencia: Estás modificando tus propios roles. Si te revocas permisos o cambias roles, tu sesión actual se cerrará y deberás volver a iniciar sesión.
                </div>
              }

              <div class="space-y-2">
                @for (role of ROLE_DEFINITIONS; track role.id) {
                  <label class="flex items-center justify-between p-3 bg-slate-950/60 border border-slate-800 rounded-xl cursor-pointer hover:border-slate-700 transition">
                    <div>
                      <span class="text-sm text-slate-200 font-bold block">{{ role.label }}</span>
                      <span class="text-[11px] text-slate-500">{{ role.name }}</span>
                    </div>
                    <input type="checkbox" [(ngModel)]="manageRoleIds[role.id]" [attr.data-testid]="'checkbox-manage-role-' + role.id"
                           class="w-4 h-4 rounded bg-slate-800 border-slate-700 text-cyan-500 focus:ring-0" />
                  </label>
                }
              </div>

              @if (rolesError()) {
                <div class="p-3 bg-rose-950/60 border border-rose-800 text-rose-300 rounded-xl text-xs font-semibold" data-testid="roles-error-msg">
                  {{ rolesError() }}
                </div>
              }

              <div class="flex items-center justify-end gap-2 pt-2">
                <button (click)="closeRolesModal()" class="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-xs font-semibold transition">
                  Cancelar
                </button>
                <button (click)="submitUpdateRoles()" [disabled]="isSubmittingRoles()" data-testid="btn-submit-roles"
                        class="px-5 py-2 bg-cyan-600 hover:bg-cyan-500 disabled:opacity-40 text-white rounded-xl text-xs font-bold transition">
                  {{ isSubmittingRoles() ? 'Guardando...' : 'Guardar Roles' }}
                </button>
              </div>
            </div>
          </div>
        }

        <!-- Reset Password Modal -->
        @if (showResetPasswordModal()) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4" data-testid="modal-reset-password">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl max-w-md w-full p-6 space-y-4 shadow-2xl">
              <h3 class="text-lg font-bold text-white flex items-center gap-2">
                <span>🔑</span> Restablecer Contraseña
              </h3>
              <p class="text-xs text-slate-400">
                Defina una nueva contraseña provisoria para <span class="font-bold text-slate-200">{{ person()?.lastName }}, {{ person()?.firstName }}</span>.
              </p>

              @if (isSelfAccount()) {
                <div class="p-3 bg-amber-950/60 border border-amber-800 text-amber-300 rounded-xl text-xs font-semibold" data-testid="self-reset-password-warning">
                  ⚠️ Advertencia: Estás cambiando la contraseña de tu propia cuenta. Tu sesión actual se cerrará inmediatamente.
                </div>
              }

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Nueva Contraseña <span class="text-rose-400">*</span></label>
                <input type="password" [(ngModel)]="newAccountPassword" placeholder="Nueva contraseña provisoria" data-testid="input-reset-password"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 font-mono" />
                <p class="text-[11px] text-slate-500 mt-1">Mínimo 8 caracteres, al menos una mayúscula, un número y un símbolo.</p>
              </div>

              @if (resetPasswordError()) {
                <div class="p-3 bg-rose-950/60 border border-rose-800 text-rose-300 rounded-xl text-xs font-semibold" data-testid="reset-password-error-msg">
                  {{ resetPasswordError() }}
                </div>
              }

              <div class="flex items-center justify-end gap-2 pt-2">
                <button (click)="closeResetPasswordModal()" class="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-xs font-semibold transition">
                  Cancelar
                </button>
                <button (click)="submitResetPassword()" [disabled]="isSubmittingResetPassword() || !newAccountPassword" data-testid="btn-submit-reset-password"
                        class="px-5 py-2 bg-amber-600 hover:bg-amber-500 disabled:opacity-40 text-white rounded-xl text-xs font-bold transition">
                  {{ isSubmittingResetPassword() ? 'Restableciendo...' : 'Restablecer' }}
                </button>
              </div>
            </div>
          </div>
        }

        <!-- Suspend / Reactivate Status Modal -->
        @if (showStatusAccountModal()) {
          <div class="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4" data-testid="modal-status-account">
            <div class="bg-slate-900 border border-slate-800 rounded-2xl max-w-md w-full p-6 space-y-4 shadow-2xl">
              <h3 class="text-lg font-bold text-white flex items-center gap-2">
                <span>{{ userAccount()?.isActive ? '⛔' : '✅' }}</span>
                {{ userAccount()?.isActive ? 'Suspender Cuenta de Acceso' : 'Reactivar Cuenta de Acceso' }}
              </h3>

              @if (isSelfAccount() && userAccount()?.isActive) {
                <div class="p-3 bg-rose-950/60 border border-rose-800 text-rose-300 rounded-xl text-xs font-semibold" data-testid="self-suspension-warning">
                  ⚠️ Advertencia Crítica: Estás por suspender tu propia cuenta de acceso. No podrás volver a ingresar al sistema a menos que otro Administrador la reactive. Tu sesión se cerrará de inmediato.
                </div>
              }

              <p class="text-xs text-slate-300">
                @if (userAccount()?.isActive) {
                  ¿Está seguro de que desea <strong>suspender</strong> la cuenta de <strong>{{ person()?.lastName }}, {{ person()?.firstName }}</strong>? El usuario perderá acceso inmediato al sistema y sus sesiones activas serán invalidadas.
                } @else {
                  ¿Está seguro de que desea <strong>reactivar</strong> la cuenta de <strong>{{ person()?.lastName }}, {{ person()?.firstName }}</strong>? El usuario podrá volver a ingresar normalmente con sus credenciales y roles asignados.
                }
              </p>

              @if (statusAccountError()) {
                <div class="p-3 bg-rose-950/60 border border-rose-800 text-rose-300 rounded-xl text-xs font-semibold" data-testid="status-account-error-msg">
                  {{ statusAccountError() }}
                </div>
              }

              <div class="flex items-center justify-end gap-2 pt-2">
                <button (click)="closeStatusAccountModal()" class="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-xs font-semibold transition">
                  Cancelar
                </button>
                <button (click)="submitToggleStatusAccount()" [disabled]="isSubmittingStatusAccount()" data-testid="btn-submit-status-account"
                        class="px-5 py-2 text-white rounded-xl text-xs font-bold transition disabled:opacity-40"
                        [ngClass]="userAccount()?.isActive ? 'bg-rose-600 hover:bg-rose-500' : 'bg-emerald-600 hover:bg-emerald-500'">
                  {{ isSubmittingStatusAccount() ? 'Procesando...' : (userAccount()?.isActive ? 'Confirmar Suspensión' : 'Confirmar Reactivación') }}
                </button>
              </div>
            </div>
          </div>
        }

        <!-- Assign Membership Modal -->
        @if (showAssignMembershipModal() && person(); as p) {
          <app-assign-membership-modal
            [studentId]="p.id"
            [studentName]="p.lastName + ', ' + p.firstName"
            (assigned)="onMembershipAssigned($event)"
            (cancelled)="closeAssignMembershipModal()"
          />
        }

      </div>
    </div>
  `
})
export class PersonDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly peopleService = inject(PeopleService);
  readonly authService = inject(AuthService);
  readonly userManagementService = inject(UserManagementService);
  readonly gymApi = inject(GymApiService);

  readonly PersonStatus = PersonStatus;
  readonly DocumentType = DocumentType;
  readonly ROLE_DEFINITIONS = ROLE_DEFINITIONS;

  readonly person = signal<PersonDetail | null>(null);
  readonly feedback = signal<string | null>(null);
  readonly feedbackSuccess = signal<boolean>(true);
  readonly isUploadingPhoto = signal<boolean>(false);

  // Student Memberships
  readonly memberships = signal<Membership[]>([]);
  readonly isLoadingMemberships = signal<boolean>(false);
  readonly showAssignMembershipModal = signal<boolean>(false);

  // User Account & Roles
  readonly userAccount = signal<UserAccountDto | null>(null);
  readonly isLoadingAccount = signal<boolean>(false);

  readonly isSelfAccount = computed(() => {
    const current = this.authService.currentUser();
    const acc = this.userAccount();
    const p = this.person();
    if (!current) return false;
    if (acc?.userId && current.userId === acc.userId) return true;
    if (p?.id && current.personId === p.id) return true;
    return false;
  });

  // Provision modal
  readonly showProvisionModal = signal<boolean>(false);
  readonly isSubmittingProvision = signal<boolean>(false);
  readonly provisionError = signal<string | null>(null);
  provisionPassword = '';
  provisionRoleIds: { [key: number]: boolean } = { 1: false, 2: false, 3: false, 4: false };

  // Manage Roles modal
  readonly showRolesModal = signal<boolean>(false);
  readonly isSubmittingRoles = signal<boolean>(false);
  readonly rolesError = signal<string | null>(null);
  manageRoleIds: { [key: number]: boolean } = { 1: false, 2: false, 3: false, 4: false };

  // Reset Password modal
  readonly showResetPasswordModal = signal<boolean>(false);
  readonly isSubmittingResetPassword = signal<boolean>(false);
  readonly resetPasswordError = signal<string | null>(null);
  newAccountPassword = '';

  // Status Account modal
  readonly showStatusAccountModal = signal<boolean>(false);
  readonly isSubmittingStatusAccount = signal<boolean>(false);
  readonly statusAccountError = signal<string | null>(null);

  // Status modal
  readonly showStatusModal = signal<boolean>(false);
  readonly isSubmittingStatus = signal<boolean>(false);
  readonly modalError = signal<string | null>(null);
  newStatus: PersonStatus = PersonStatus.Active;
  statusReason: string = '';

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadPerson(id);
    }
  }

  loadPerson(id: string): void {
    this.peopleService.getById(id).subscribe({
      next: (p) => {
        this.person.set(p);
        this.newStatus = p.status;
        this.loadMemberships(id);
        if (this.authService.isAdmin()) {
          this.loadAccount(id);
        }
      },
      error: (err) => {
        this.feedback.set(err.error?.message || 'Error al cargar la ficha de la persona.');
        this.feedbackSuccess.set(false);
      }
    });
  }

  loadAccount(personId: string): void {
    if (!this.authService.isAdmin()) return;
    this.isLoadingAccount.set(true);
    this.userManagementService.getAccount(personId).subscribe({
      next: (acc) => {
        this.userAccount.set(acc);
        this.isLoadingAccount.set(false);
      },
      error: () => {
        this.isLoadingAccount.set(false);
      }
    });
  }

  openStatusModal(): void {
    const p = this.person();
    if (p) {
      this.newStatus = p.status;
      this.statusReason = '';
      this.modalError.set(null);
      this.showStatusModal.set(true);
    }
  }

  closeStatusModal(): void {
    this.showStatusModal.set(false);
  }

  submitStatusChange(): void {
    const p = this.person();
    if (!p) return;

    if (!this.statusReason.trim()) {
      this.modalError.set('El motivo del cambio de estado es obligatorio.');
      return;
    }

    this.isSubmittingStatus.set(true);
    this.modalError.set(null);

    this.peopleService.changeStatus(p.id, {
      targetStatus: this.newStatus,
      reason: this.statusReason.trim()
    }).subscribe({
      next: (updated) => {
        this.isSubmittingStatus.set(false);
        this.showStatusModal.set(false);
        this.person.set(updated);
        this.feedback.set(`Estado modificado exitosamente a: ${this.getStatusLabel(updated.status)}`);
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isSubmittingStatus.set(false);
        this.modalError.set(err.error?.message || 'No se pudo cambiar el estado.');
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];
    const allowedTypes = ['image/jpeg', 'image/png', 'image/webp'];

    if (!allowedTypes.includes(file.type)) {
      this.feedback.set('Formato no admitido. Seleccione un archivo JPEG, PNG o WebP.');
      this.feedbackSuccess.set(false);
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      this.feedback.set('El archivo excede el tamaño máximo permitido de 5 MB.');
      this.feedbackSuccess.set(false);
      return;
    }

    const p = this.person();
    if (!p) return;

    this.isUploadingPhoto.set(true);
    this.feedback.set(null);

    this.peopleService.uploadPhoto(p.id, file).subscribe({
      next: (updated) => {
        this.isUploadingPhoto.set(false);
        this.person.set(updated);
        this.feedback.set('Foto de perfil actualizada correctamente.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isUploadingPhoto.set(false);
        this.feedback.set(err.error?.message || 'Error al subir la foto de perfil.');
        this.feedbackSuccess.set(false);
      }
    });
  }

  deletePhoto(): void {
    const p = this.person();
    if (!p) return;

    if (!confirm('¿Desea eliminar la foto de perfil?')) return;

    this.isUploadingPhoto.set(true);
    this.feedback.set(null);

    this.peopleService.deletePhoto(p.id).subscribe({
      next: (updated) => {
        this.isUploadingPhoto.set(false);
        this.person.set(updated);
        this.feedback.set('Foto eliminada correctamente.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isUploadingPhoto.set(false);
        this.feedback.set(err.error?.message || 'Error al eliminar la foto.');
        this.feedbackSuccess.set(false);
      }
    });
  }

  getStatusLabel(status: PersonStatus): string {
    return PersonStatusLabels[status] ?? 'Desconocido';
  }

  getDocumentTypeLabel(type: DocumentType): string {
    return DocumentTypeLabels[type] ?? '';
  }

  getGenderLabel(gender: Gender): string {
    return GenderLabels[gender] ?? '';
  }

  getStatusBadgeClass(status: PersonStatus): string {
    switch (status) {
      case PersonStatus.Active:
        return 'bg-emerald-500/20 text-emerald-400 border-emerald-500/30';
      case PersonStatus.Inactive:
        return 'bg-slate-700/40 text-slate-300 border-slate-600/30';
      case PersonStatus.Blocked:
        return 'bg-rose-500/20 text-rose-300 border-rose-500/40 font-black';
      case PersonStatus.Deceased:
        return 'bg-purple-900/30 text-purple-300 border-purple-800/40';
      default:
        return 'bg-slate-800 text-slate-400 border-slate-700';
    }
  }

  // User Management Actions
  openProvisionModal(): void {
    this.provisionPassword = '';
    this.provisionRoleIds = { 1: false, 2: false, 3: false, 4: false };
    this.provisionError.set(null);
    this.showProvisionModal.set(true);
  }

  closeProvisionModal(): void {
    this.showProvisionModal.set(false);
  }

  submitProvision(): void {
    const p = this.person();
    if (!p) return;

    const roleIds = Object.entries(this.provisionRoleIds)
      .filter(([_, checked]) => checked)
      .map(([id]) => Number(id));

    this.isSubmittingProvision.set(true);
    this.provisionError.set(null);

    this.userManagementService.provision(p.id, {
      password: this.provisionPassword,
      roleIds
    }).subscribe({
      next: (acc) => {
        this.isSubmittingProvision.set(false);
        this.showProvisionModal.set(false);
        this.userAccount.set(acc);
        this.person.update(curr => curr ? { ...curr, hasUserAccount: true } : curr);
        this.feedback.set('Cuenta de acceso creada exitosamente.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isSubmittingProvision.set(false);
        this.provisionError.set(apiErrorMessage(err, 'Error al aprovisionar la cuenta de usuario.'));
      }
    });
  }

  openRolesModal(): void {
    const acc = this.userAccount();
    const currentRoles = acc?.roles ?? [];
    this.manageRoleIds = {
      1: currentRoles.includes('Administrator'),
      2: currentRoles.includes('Secretary'),
      3: currentRoles.includes('Instructor'),
      4: currentRoles.includes('Student')
    };
    this.rolesError.set(null);
    this.showRolesModal.set(true);
  }

  closeRolesModal(): void {
    this.showRolesModal.set(false);
  }

  submitUpdateRoles(): void {
    const p = this.person();
    if (!p) return;

    const roleIds = Object.entries(this.manageRoleIds)
      .filter(([_, checked]) => checked)
      .map(([id]) => Number(id));

    this.isSubmittingRoles.set(true);
    this.rolesError.set(null);

    this.userManagementService.updateRoles(p.id, { roleIds }).subscribe({
      next: (acc) => {
        this.isSubmittingRoles.set(false);
        this.showRolesModal.set(false);
        this.userAccount.set(acc);
        this.feedback.set('Roles operativos actualizados exitosamente.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isSubmittingRoles.set(false);
        this.rolesError.set(apiErrorMessage(err, 'Error al actualizar los roles.'));
      }
    });
  }

  openResetPasswordModal(): void {
    this.newAccountPassword = '';
    this.resetPasswordError.set(null);
    this.showResetPasswordModal.set(true);
  }

  closeResetPasswordModal(): void {
    this.showResetPasswordModal.set(false);
  }

  submitResetPassword(): void {
    const p = this.person();
    if (!p) return;

    this.isSubmittingResetPassword.set(true);
    this.resetPasswordError.set(null);

    this.userManagementService.resetPassword(p.id, { newPassword: this.newAccountPassword }).subscribe({
      next: () => {
        this.isSubmittingResetPassword.set(false);
        this.showResetPasswordModal.set(false);
        this.feedback.set('Contraseña restablecida exitosamente.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isSubmittingResetPassword.set(false);
        this.resetPasswordError.set(apiErrorMessage(err, 'Error al restablecer la contraseña.'));
      }
    });
  }

  openStatusAccountModal(): void {
    this.statusAccountError.set(null);
    this.showStatusAccountModal.set(true);
  }

  closeStatusAccountModal(): void {
    this.showStatusAccountModal.set(false);
  }

  submitToggleStatusAccount(): void {
    const p = this.person();
    const acc = this.userAccount();
    if (!p || !acc) return;

    const targetIsActive = !acc.isActive;
    this.isSubmittingStatusAccount.set(true);
    this.statusAccountError.set(null);

    this.userManagementService.updateStatus(p.id, { isActive: targetIsActive }).subscribe({
      next: (updatedAcc) => {
        this.isSubmittingStatusAccount.set(false);
        this.showStatusAccountModal.set(false);
        this.userAccount.set(updatedAcc);
        this.feedback.set(targetIsActive ? 'Cuenta reactivada exitosamente.' : 'Cuenta suspendida exitosamente.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isSubmittingStatusAccount.set(false);
        this.statusAccountError.set(apiErrorMessage(err, 'Error al cambiar el estado de la cuenta.'));
      }
    });
  }

  getRoleLabel(role: string): string {
    switch (role) {
      case 'Administrator': return 'Administrador';
      case 'Secretary': return 'Secretaria';
      case 'Instructor': return 'Profesor';
      case 'Student': return 'Alumno';
      default: return role;
    }
  }

  getRoleBadgeClass(role: string): string {
    switch (role) {
      case 'Administrator': return 'bg-purple-950/60 text-purple-300 border-purple-800';
      case 'Secretary': return 'bg-blue-950/60 text-blue-300 border-blue-800';
      case 'Instructor': return 'bg-emerald-950/60 text-emerald-300 border-emerald-800';
      case 'Student': return 'bg-amber-950/60 text-amber-300 border-amber-800';
      default: return 'bg-slate-800 text-slate-300 border-slate-700';
    }
  }

  // Student Memberships Actions
  loadMemberships(personId: string): void {
    this.isLoadingMemberships.set(true);
    this.gymApi.getStudentMemberships(personId).subscribe({
      next: (list) => {
        this.memberships.set(list ?? []);
        this.isLoadingMemberships.set(false);
      },
      error: () => {
        this.memberships.set([]);
        this.isLoadingMemberships.set(false);
      }
    });
  }

  openAssignMembershipModal(): void {
    const p = this.person();
    if (!p || p.status !== PersonStatus.Active) return;
    this.showAssignMembershipModal.set(true);
  }

  closeAssignMembershipModal(): void {
    this.showAssignMembershipModal.set(false);
  }

  onMembershipAssigned(_created: MembershipDetail): void {
    const p = this.person();
    this.showAssignMembershipModal.set(false);
    this.feedback.set('Membresía asignada exitosamente.');
    this.feedbackSuccess.set(true);
    if (p) {
      this.loadMemberships(p.id);
    }
  }

  getMembershipStatusLabel(mem: Membership): string {
    const status = mem.effectiveStatus ?? mem.status;
    switch (status) {
      case MembershipStatus.Active: return 'Activa';
      case MembershipStatus.Pending: return 'Pendiente';
      case MembershipStatus.Expired: return 'Vencida';
      case MembershipStatus.Suspended: return 'Suspendida';
      case MembershipStatus.Cancelled: return 'Cancelada';
      default: return 'Desconocido';
    }
  }

  getMembershipStatusBadgeClass(mem: Membership): string {
    const status = mem.effectiveStatus ?? mem.status;
    switch (status) {
      case MembershipStatus.Active:
        return 'bg-emerald-950/50 text-emerald-400 border-emerald-800';
      case MembershipStatus.Pending:
        return 'bg-amber-950/50 text-amber-400 border-amber-800';
      case MembershipStatus.Expired:
        return 'bg-slate-800 text-slate-400 border-slate-700';
      case MembershipStatus.Suspended:
        return 'bg-indigo-950/50 text-indigo-400 border-indigo-800';
      case MembershipStatus.Cancelled:
        return 'bg-rose-950/50 text-rose-400 border-rose-800';
      default:
        return 'bg-slate-800 text-slate-400 border-slate-700';
    }
  }
}
