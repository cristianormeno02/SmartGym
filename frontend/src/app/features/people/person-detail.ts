import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PeopleService } from '../../core/services/people.service';
import { AuthService } from '../../core/services/auth.service';
import {
  PersonDetail,
  PersonStatus,
  PersonStatusLabels,
  DocumentType,
  DocumentTypeLabels,
  Gender,
  GenderLabels
} from '../../core/models/person.model';

@Component({
  selector: 'app-person-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
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
                  <dd class="text-slate-200 mt-0.5">{{ p.birthDate ?? '—' }}</dd>
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
                  <dd class="text-slate-200 mt-0.5">{{ p.createdAtUtc | date:'medium' }}</dd>
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
                    <dd class="text-slate-200 mt-0.5">{{ p.statusChangedAtUtc | date:'medium' }}</dd>
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

      </div>
    </div>
  `
})
export class PersonDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly peopleService = inject(PeopleService);
  readonly authService = inject(AuthService);

  readonly PersonStatus = PersonStatus;
  readonly DocumentType = DocumentType;

  readonly person = signal<PersonDetail | null>(null);
  readonly feedback = signal<string | null>(null);
  readonly feedbackSuccess = signal<boolean>(true);
  readonly isUploadingPhoto = signal<boolean>(false);

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
      },
      error: (err) => {
        this.feedback.set(err.error?.message || 'Error al cargar la ficha de la persona.');
        this.feedbackSuccess.set(false);
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
}
