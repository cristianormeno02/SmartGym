import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { PeopleService } from '../../core/services/people.service';
import { AuthService } from '../../core/services/auth.service';
import {
  PersonDetail,
  PersonStatus,
  PersonStatusLabels,
  DocumentType,
  DocumentTypeLabels,
  UpdateOwnContactRequest
} from '../../core/models/person.model';

@Component({
  selector: 'app-my-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-4xl mx-auto space-y-6">

        <!-- Header -->
        <div>
          <div class="flex items-center gap-2">
            <span class="h-2.5 w-2.5 rounded-full bg-cyan-400"></span>
            <span class="text-xs uppercase tracking-widest font-semibold text-cyan-400">Autoservicio</span>
          </div>
          <h1 class="text-3xl font-extrabold text-white mt-1">Mis Datos Personales</h1>
          <p class="text-sm text-slate-400">
            Consulte su ficha personal y mantenga actualizada su información de contacto y domicilio.
          </p>
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
            <p class="mt-2 text-xs">Cargando tus datos...</p>
          </div>
        } @else if (person(); as p) {

          <!-- Avatar & Readonly Protected Fields Card -->
          <div class="bg-slate-900 border border-slate-800 rounded-3xl p-6 sm:p-8 shadow-xl">
            <div class="flex flex-col sm:flex-row items-center sm:items-start gap-6">

              <!-- Photo Upload / Delete -->
              <div class="flex flex-col items-center gap-2">
                <div class="relative group">
                  @if (previewUrl()) {
                    <img [src]="previewUrl()" alt="Vista previa" class="w-28 h-28 rounded-2xl object-cover border-2 border-cyan-500 shadow-lg" />
                  } @else if (p.photoUrl) {
                    <img [src]="p.photoUrl" alt="" class="w-28 h-28 rounded-2xl object-cover border-2 border-slate-700 shadow-lg" />
                  } @else {
                    <div class="w-28 h-28 rounded-2xl bg-slate-800 border-2 border-slate-700 flex items-center justify-center font-black text-slate-400 text-3xl shadow-lg">
                      {{ p.firstName.charAt(0) }}{{ p.lastName.charAt(0) }}
                    </div>
                  }
                </div>

                <!-- Photo Actions -->
                <div class="flex items-center gap-1.5 mt-1">
                  @if (selectedFile()) {
                    <button (click)="confirmUploadPhoto()" [disabled]="isUploadingPhoto()"
                            class="text-[11px] bg-cyan-600 hover:bg-cyan-500 text-white font-bold px-2.5 py-1 rounded-lg transition">
                      Confirmar
                    </button>
                    <button (click)="cancelPhotoPreview()" [disabled]="isUploadingPhoto()"
                            class="text-[11px] bg-slate-800 hover:bg-slate-700 text-slate-300 px-2 py-1 rounded-lg transition">
                      ✕
                    </button>
                  } @else {
                    <input #fileInput type="file" (change)="onFileSelected($event)" accept="image/jpeg,image/png,image/webp" class="hidden" />
                    <button (click)="fileInput.click()" [disabled]="isUploadingPhoto()"
                            class="text-[11px] bg-slate-800 hover:bg-slate-700 text-cyan-400 font-semibold px-2.5 py-1 rounded-lg border border-slate-700 transition">
                      {{ isUploadingPhoto() ? 'Subiendo...' : (p.photoUrl ? 'Cambiar Foto' : 'Subir Foto') }}
                    </button>
                    @if (p.photoUrl) {
                      <button (click)="deleteOwnPhoto()" [disabled]="isUploadingPhoto()"
                              title="Eliminar foto"
                              class="text-[11px] bg-rose-950/40 hover:bg-rose-900/60 text-rose-300 font-semibold px-2 py-1 rounded-lg border border-rose-800/60 transition">
                        🗑️
                      </button>
                    }
                  }
                </div>
              </div>

              <!-- Protected Fields -->
              <div class="flex-1 space-y-3 text-center sm:text-left">
                <div class="flex flex-wrap items-center justify-center sm:justify-start gap-2">
                  <span class="text-xs px-3 py-1 rounded-full font-bold border"
                        [ngClass]="getStatusBadgeClass(p.status)">
                    {{ getStatusLabel(p.status) }}
                  </span>
                </div>
                <h2 class="text-2xl font-black text-white">
                  {{ p.firstName }} {{ p.lastName }}
                </h2>
                <div class="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs pt-1">
                  <div>
                    <span class="text-slate-500 uppercase font-semibold">Documento:</span>
                    <span class="text-slate-200 font-mono font-bold ml-1">
                      {{ p.document ? getDocumentTypeLabel(p.document.type) + ' ' + p.document.number : 'Sin documento' }}
                    </span>
                  </div>
                  <div>
                    <span class="text-slate-500 uppercase font-semibold">Fecha de Nac.:</span>
                    <span class="text-slate-200 ml-1">{{ p.birthDate ?? 'No informada' }}</span>
                  </div>
                  <div class="sm:col-span-2">
                    <span class="text-slate-500 uppercase font-semibold">Email:</span>
                    <span class="text-slate-200 ml-1 font-mono">{{ p.email ?? '—' }}</span>
                  </div>
                </div>
                <p class="text-[11px] text-slate-500 italic pt-1">
                  🔒 Por seguridad, para modificar su nombre, documento, email o fecha de nacimiento, solicite asistencia en recepción.
                </p>
              </div>

            </div>
          </div>

          <!-- Editable Contact Form -->
          <form [formGroup]="form" (ngSubmit)="onSaveContact()" class="bg-slate-900 border border-slate-800 rounded-3xl p-6 sm:p-8 shadow-xl space-y-6">
            <h3 class="text-base font-bold text-cyan-400 uppercase tracking-wider flex items-center gap-2">
              <span>✏️</span> Datos de Contacto y Domicilio (Editables)
            </h3>

            <!-- Phones -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Teléfono Principal</label>
                <input type="tel" formControlName="primaryPhone"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
              </div>
              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Teléfono Secundario</label>
                <input type="tel" formControlName="secondaryPhone"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
              </div>
            </div>

            <!-- Address -->
            <div formGroupName="address" class="space-y-4 pt-2">
              <h4 class="text-xs font-bold uppercase text-slate-300">Domicilio</h4>
              <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div class="sm:col-span-2">
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Calle</label>
                  <input type="text" formControlName="street"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Número</label>
                  <input type="text" formControlName="number"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Piso</label>
                  <input type="text" formControlName="floor"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Departamento</label>
                  <input type="text" formControlName="apartment"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Ciudad</label>
                  <input type="text" formControlName="city"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Provincia</label>
                  <input type="text" formControlName="state"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Código Postal</label>
                  <input type="text" formControlName="postalCode"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">País</label>
                  <input type="text" formControlName="country" maxlength="2"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 uppercase font-mono" />
                </div>
              </div>
            </div>

            <!-- Emergency Contact -->
            <div formGroupName="emergencyContact" class="space-y-4 pt-2">
              <h4 class="text-xs font-bold uppercase text-slate-300">Contacto de Emergencia</h4>
              <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Nombre Completo</label>
                  <input type="text" formControlName="name"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Teléfono</label>
                  <input type="tel" formControlName="phone"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
                <div>
                  <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Vínculo / Parentesco</label>
                  <input type="text" formControlName="relationship"
                         class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                </div>
              </div>
            </div>

            <!-- Submit Button -->
            <div class="flex items-center justify-end pt-4 border-t border-slate-800">
              <button type="submit" [disabled]="isSavingContact()"
                      class="px-6 py-2.5 bg-cyan-500 hover:bg-cyan-400 disabled:opacity-40 text-slate-950 font-bold rounded-xl text-sm transition shadow-lg hover:shadow-cyan-500/20">
                {{ isSavingContact() ? 'Guardando...' : 'Guardar Cambios' }}
              </button>
            </div>
          </form>

        }

      </div>
    </div>
  `
})
export class MyProfileComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  readonly peopleService = inject(PeopleService);
  readonly authService = inject(AuthService);

  readonly person = signal<PersonDetail | null>(null);
  readonly feedback = signal<string | null>(null);
  readonly feedbackSuccess = signal<boolean>(true);
  readonly isSavingContact = signal<boolean>(false);
  readonly isUploadingPhoto = signal<boolean>(false);

  // Avatar pre-check and preview
  readonly selectedFile = signal<File | null>(null);
  readonly previewUrl = signal<string | null>(null);

  form!: FormGroup;

  ngOnInit(): void {
    this.initForm();
    this.loadMyData();
  }

  private initForm(): void {
    this.form = this.fb.group({
      primaryPhone: [''],
      secondaryPhone: [''],
      address: this.fb.group({
        street: [''],
        number: [''],
        floor: [''],
        apartment: [''],
        city: [''],
        state: [''],
        postalCode: [''],
        country: ['AR']
      }),
      emergencyContact: this.fb.group({
        name: [''],
        phone: [''],
        relationship: ['']
      })
    });
  }

  loadMyData(): void {
    this.peopleService.getOwn().subscribe({
      next: (p) => {
        this.person.set(p);
        this.form.patchValue({
          primaryPhone: p.primaryPhone ?? '',
          secondaryPhone: p.secondaryPhone ?? '',
          address: {
            street: p.address?.street ?? '',
            number: p.address?.number ?? '',
            floor: p.address?.floor ?? '',
            apartment: p.address?.apartment ?? '',
            city: p.address?.city ?? '',
            state: p.address?.state ?? '',
            postalCode: p.address?.postalCode ?? '',
            country: p.address?.country ?? 'AR'
          },
          emergencyContact: {
            name: p.emergencyContact?.name ?? '',
            phone: p.emergencyContact?.phone ?? '',
            relationship: p.emergencyContact?.relationship ?? ''
          }
        });
      },
      error: (err) => {
        this.feedback.set(err.error?.message || 'Error al cargar los datos personales.');
        this.feedbackSuccess.set(false);
      }
    });
  }

  onSaveContact(): void {
    this.isSavingContact.set(true);
    this.feedback.set(null);

    const val = this.form.getRawValue();
    const req: UpdateOwnContactRequest = {
      primaryPhone: val.primaryPhone?.trim() || undefined,
      secondaryPhone: val.secondaryPhone?.trim() || undefined,
      address: val.address?.street || val.address?.city ? val.address : undefined,
      emergencyContact: val.emergencyContact?.name ? val.emergencyContact : undefined
    };

    this.peopleService.updateOwnContact(req).subscribe({
      next: (updated) => {
        this.isSavingContact.set(false);
        this.person.set(updated);
        this.feedback.set('Tus datos de contacto han sido actualizados exitosamente.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isSavingContact.set(false);
        this.feedback.set(err.error?.message || 'Error al guardar los datos de contacto.');
        this.feedbackSuccess.set(false);
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];
    const allowedTypes = ['image/jpeg', 'image/png', 'image/webp'];

    if (!allowedTypes.includes(file.type)) {
      this.feedback.set('Formato de imagen no permitido. Seleccione un archivo JPEG, PNG o WebP.');
      this.feedbackSuccess.set(false);
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      this.feedback.set('La imagen supera el límite de 5 MB.');
      this.feedbackSuccess.set(false);
      return;
    }

    this.selectedFile.set(file);
    const reader = new FileReader();
    reader.onload = () => {
      this.previewUrl.set(reader.result as string);
    };
    reader.readAsDataURL(file);
  }

  cancelPhotoPreview(): void {
    this.selectedFile.set(null);
    this.previewUrl.set(null);
  }

  confirmUploadPhoto(): void {
    const file = this.selectedFile();
    if (!file) return;

    this.isUploadingPhoto.set(true);
    this.feedback.set(null);

    this.peopleService.uploadOwnPhoto(file).subscribe({
      next: (updated) => {
        this.isUploadingPhoto.set(false);
        this.person.set(updated);
        this.cancelPhotoPreview();
        this.feedback.set('Foto de perfil actualizada con éxito.');
        this.feedbackSuccess.set(true);
      },
      error: (err) => {
        this.isUploadingPhoto.set(false);
        this.feedback.set(err.error?.message || 'Error al actualizar la foto de perfil.');
        this.feedbackSuccess.set(false);
      }
    });
  }

  deleteOwnPhoto(): void {
    if (!confirm('¿Deseas eliminar tu foto de perfil?')) return;

    this.isUploadingPhoto.set(true);
    this.feedback.set(null);

    this.peopleService.deleteOwnPhoto().subscribe({
      next: (updated) => {
        this.isUploadingPhoto.set(false);
        this.person.set(updated);
        this.feedback.set('Foto de perfil eliminada.');
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
