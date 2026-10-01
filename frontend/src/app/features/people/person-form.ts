import { Component, OnInit, inject, signal, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators, AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { PeopleService } from '../../core/services/people.service';
import {
  DocumentType,
  DocumentTypeLabels,
  Gender,
  GenderLabels,
  PersonDetail,
  CreatePersonRequest,
  UpdatePersonRequest
} from '../../core/models/person.model';

export function documentValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const parent = control.parent;
    if (!parent) return null;
    const docType = parent.get('documentType')?.value;
    const docNumber = control.value;
    const country = parent.get('documentIssuingCountry')?.value || 'AR';

    if (!docNumber) return null;

    if (docType === DocumentType.Dni || docType === 0) {
      const clean = docNumber.toString().replace(/\D/g, '');
      if (country === 'AR' && (clean.length < 7 || clean.length > 8)) {
        return { invalidDni: 'El DNI argentino debe contener 7 u 8 dígitos numéricos.' };
      }
      if (clean.length < 5 || clean.length > 10) {
        return { invalidDni: 'El DNI debe contener entre 5 y 10 dígitos.' };
      }
    } else if (docType === DocumentType.Passport || docType === 1) {
      if (!/^[a-zA-Z0-9]{6,12}$/.test(docNumber)) {
        return { invalidPassport: 'El pasaporte debe contener entre 6 y 12 caracteres alfanuméricos.' };
      }
    } else if (docType === DocumentType.ForeignId || docType === 2) {
      if (!/^[a-zA-Z0-9]{6,15}$/.test(docNumber)) {
        return { invalidForeignId: 'La cédula debe contener entre 6 y 15 caracteres alfanuméricos.' };
      }
    }
    return null;
  };
}

export function isMinor(birthDateStr: string | null | undefined): boolean {
  if (!birthDateStr) return false;
  const birth = new Date(birthDateStr);
  const now = new Date();
  let age = now.getFullYear() - birth.getFullYear();
  const m = now.getMonth() - birth.getMonth();
  if (m < 0 || (m === 0 && now.getDate() < birth.getDate())) {
    age--;
  }
  return age < 18;
}

@Component({
  selector: 'app-person-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-4xl mx-auto space-y-6">

        <!-- Top Navigation & Header -->
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-3">
            <a [routerLink]="isEditMode() ? ['/portal/personas', personId()] : '/portal/personas'"
               class="p-2 bg-slate-900 hover:bg-slate-800 border border-slate-800 rounded-xl text-slate-400 hover:text-white transition">
              <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 19l-7-7m0 0l7-7m-7 7h18"/>
              </svg>
            </a>
            <div>
              <h1 class="text-2xl font-black text-white">
                {{ isEditMode() ? 'Editar Ficha Personal' : 'Alta de Persona' }}
              </h1>
              <p class="text-xs text-slate-400">
                {{ isEditMode() ? 'Actualice los datos personales y de contacto' : 'Cree un nuevo registro maestro en el sistema' }}
              </p>
            </div>
          </div>
        </div>

        <!-- Error / Conflict Alert -->
        @if (errorMessage()) {
          <div class="bg-rose-950/50 border border-rose-800/80 rounded-2xl p-4 flex items-start justify-between gap-3 text-rose-200 text-sm">
            <div class="flex items-center gap-2">
              <svg class="w-5 h-5 text-rose-400 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>
              </svg>
              <span>{{ errorMessage() }}</span>
            </div>
            @if (isVersionConflict()) {
              <button (click)="reloadPerson()" class="text-xs bg-rose-800 hover:bg-rose-700 text-white font-bold px-3 py-1.5 rounded-lg transition shrink-0">
                Recargar datos
              </button>
            }
          </div>
        }

        <!-- Main Form Card -->
        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="bg-slate-900 border border-slate-800 rounded-2xl p-6 sm:p-8 shadow-xl space-y-8">

          <!-- Section 1: Identificación -->
          <div>
            <h2 class="text-base font-bold text-cyan-400 uppercase tracking-wider mb-4 flex items-center gap-2">
              <span>👤</span> Datos de Identificación
            </h2>
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Nombre <span class="text-rose-400">*</span></label>
                <input type="text" formControlName="firstName"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                @if (form.get('firstName')?.touched && form.get('firstName')?.hasError('required')) {
                  <p class="text-xs text-rose-400 mt-1">El nombre es obligatorio.</p>
                }
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Apellido <span class="text-rose-400">*</span></label>
                <input type="text" formControlName="lastName"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                @if (form.get('lastName')?.touched && form.get('lastName')?.hasError('required')) {
                  <p class="text-xs text-rose-400 mt-1">El apellido es obligatorio.</p>
                }
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Tipo de Documento</label>
                <select formControlName="documentType"
                        class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500">
                  <option [ngValue]="null">Sin documento</option>
                  <option [ngValue]="DocumentType.Dni">DNI</option>
                  <option [ngValue]="DocumentType.Passport">Pasaporte</option>
                  <option [ngValue]="DocumentType.ForeignId">Cédula de Identidad</option>
                  <option [ngValue]="DocumentType.Other">Otro</option>
                </select>
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Número de Documento</label>
                <input type="text" formControlName="documentNumber"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 font-mono" />
                @if (form.get('documentNumber')?.touched && form.get('documentNumber')?.errors) {
                  <p class="text-xs text-rose-400 mt-1">
                    {{ form.get('documentNumber')?.errors?.['invalidDni'] ||
                       form.get('documentNumber')?.errors?.['invalidPassport'] ||
                       form.get('documentNumber')?.errors?.['invalidForeignId'] ||
                       'Documento no válido' }}
                  </p>
                }
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">País Emisor</label>
                <input type="text" formControlName="documentIssuingCountry" maxlength="2"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 uppercase font-mono"
                       placeholder="AR" />
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Género</label>
                <select formControlName="gender"
                        class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500">
                  <option [ngValue]="null">No especificado</option>
                  <option [ngValue]="Gender.Male">Masculino</option>
                  <option [ngValue]="Gender.Female">Femenino</option>
                  <option [ngValue]="Gender.Other">Otro</option>
                </select>
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Fecha de Nacimiento</label>
                <input type="date" formControlName="birthDate"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
                @if (isMinorSelected()) {
                  <p class="text-xs text-amber-400 mt-1 font-semibold">
                    ⚠️ Menor de 18 años: el contacto de emergencia es obligatorio.
                  </p>
                }
              </div>
            </div>
          </div>

          <div class="h-px bg-slate-800"></div>

          <!-- Section 2: Contacto -->
          <div>
            <h2 class="text-base font-bold text-cyan-400 uppercase tracking-wider mb-4 flex items-center gap-2">
              <span>📞</span> Información de Contacto
            </h2>
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div class="sm:col-span-2">
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">Correo Electrónico</label>
                <input type="email" formControlName="email"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500"
                       [class.opacity-50]="hasUserAccount()" />
                @if (hasUserAccount()) {
                  <p class="text-xs text-slate-500 mt-1">El email no puede modificarse porque está vinculado a una cuenta de usuario.</p>
                }
                @if (form.get('email')?.touched && form.get('email')?.hasError('email')) {
                  <p class="text-xs text-rose-400 mt-1">Formato de correo electrónico inválido.</p>
                }
              </div>

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
          </div>

          <div class="h-px bg-slate-800"></div>

          <!-- Section 3: Domicilio -->
          <div formGroupName="address">
            <h2 class="text-base font-bold text-cyan-400 uppercase tracking-wider mb-4 flex items-center gap-2">
              <span>🏠</span> Domicilio
            </h2>
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
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500 uppercase font-mono"
                       placeholder="AR" />
              </div>
            </div>
          </div>

          <div class="h-px bg-slate-800"></div>

          <!-- Section 4: Contacto de Emergencia -->
          <div formGroupName="emergencyContact">
            <h2 class="text-base font-bold text-cyan-400 uppercase tracking-wider mb-4 flex items-center gap-2">
              <span>🚨</span> Contacto de Emergencia
              @if (isMinorSelected()) {
                <span class="text-xs bg-amber-500/20 text-amber-400 px-2 py-0.5 rounded font-bold border border-amber-500/30">OBLIGATORIO</span>
              }
            </h2>
            <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">
                  Nombre Completo @if (isMinorSelected()) { <span class="text-rose-400">*</span> }
                </label>
                <input type="text" formControlName="name"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">
                  Teléfono @if (isMinorSelected()) { <span class="text-rose-400">*</span> }
                </label>
                <input type="tel" formControlName="phone"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
              </div>

              <div>
                <label class="block text-xs font-semibold uppercase text-slate-400 mb-1">
                  Vínculo / Parentesco @if (isMinorSelected()) { <span class="text-rose-400">*</span> }
                </label>
                <input type="text" formControlName="relationship"
                       placeholder="Ej: Madre, Padre, Tutor"
                       class="w-full bg-slate-950 border border-slate-800 rounded-xl px-3 py-2 text-sm text-white focus:outline-none focus:border-cyan-500" />
              </div>
            </div>
          </div>

          <!-- Action Buttons -->
          <div class="flex items-center justify-end gap-3 pt-4 border-t border-slate-800">
            <a [routerLink]="isEditMode() ? ['/portal/personas', personId()] : '/portal/personas'"
               class="px-4 py-2.5 bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white rounded-xl text-sm font-semibold transition">
              Cancelar
            </a>
            <button type="submit"
                    [disabled]="form.invalid || isSubmitting()"
                    class="px-6 py-2.5 bg-cyan-500 hover:bg-cyan-400 disabled:opacity-40 text-slate-950 font-bold rounded-xl text-sm transition shadow-lg hover:shadow-cyan-500/20">
              {{ isSubmitting() ? 'Guardando...' : (isEditMode() ? 'Actualizar Ficha' : 'Crear Persona') }}
            </button>
          </div>

        </form>

      </div>
    </div>
  `
})
export class PersonFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly peopleService = inject(PeopleService);

  readonly DocumentType = DocumentType;
  readonly Gender = Gender;

  readonly isEditMode = signal<boolean>(false);
  readonly personId = signal<string | null>(null);
  readonly personVersion = signal<number>(1);
  readonly hasUserAccount = signal<boolean>(false);
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly isVersionConflict = signal<boolean>(false);
  readonly isMinorSelected = signal<boolean>(false);

  form!: FormGroup;

  ngOnInit(): void {
    this.initForm();

    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'nueva') {
      this.isEditMode.set(true);
      this.personId.set(id);
      this.loadPerson(id);
    }
  }

  private initForm(): void {
    this.form = this.fb.group({
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      documentType: [DocumentType.Dni],
      documentNumber: ['', [documentValidator()]],
      documentIssuingCountry: ['AR', [Validators.maxLength(2)]],
      gender: [null],
      birthDate: [null],
      email: ['', [Validators.email]],
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

    this.form.get('birthDate')?.valueChanges.subscribe((val) => {
      const minor = isMinor(val);
      this.isMinorSelected.set(minor);
      this.updateEmergencyContactValidators(minor);
    });

    this.form.get('documentType')?.valueChanges.subscribe(() => {
      this.form.get('documentNumber')?.updateValueAndValidity();
    });
  }

  private updateEmergencyContactValidators(minor: boolean): void {
    const ecGroup = this.form.get('emergencyContact') as FormGroup;
    const controls = [ecGroup.get('name'), ecGroup.get('phone'), ecGroup.get('relationship')];

    for (const ctrl of controls) {
      if (minor) {
        ctrl?.setValidators([Validators.required]);
      } else {
        ctrl?.clearValidators();
      }
      ctrl?.updateValueAndValidity();
    }
  }

  loadPerson(id: string): void {
    this.peopleService.getById(id).subscribe({
      next: (person) => {
        this.personVersion.set(person.version);
        this.hasUserAccount.set(person.hasUserAccount);

        this.form.patchValue({
          firstName: person.firstName,
          lastName: person.lastName,
          documentType: person.document?.type ?? null,
          documentNumber: person.document?.number ?? '',
          documentIssuingCountry: person.document?.issuingCountry ?? 'AR',
          gender: person.gender ?? null,
          birthDate: person.birthDate ?? null,
          email: person.email ?? '',
          primaryPhone: person.primaryPhone ?? '',
          secondaryPhone: person.secondaryPhone ?? '',
          address: {
            street: person.address?.street ?? '',
            number: person.address?.number ?? '',
            floor: person.address?.floor ?? '',
            apartment: person.address?.apartment ?? '',
            city: person.address?.city ?? '',
            state: person.address?.state ?? '',
            postalCode: person.address?.postalCode ?? '',
            country: person.address?.country ?? 'AR'
          },
          emergencyContact: {
            name: person.emergencyContact?.name ?? '',
            phone: person.emergencyContact?.phone ?? '',
            relationship: person.emergencyContact?.relationship ?? ''
          }
        });

        if (person.hasUserAccount) {
          this.form.get('email')?.disable();
        }
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Error al cargar los datos de la persona.');
      }
    });
  }

  reloadPerson(): void {
    const id = this.personId();
    if (id) {
      this.errorMessage.set(null);
      this.isVersionConflict.set(false);
      this.loadPerson(id);
    }
  }

  onSubmit(): void {
    if (this.form.invalid) return;

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.isVersionConflict.set(false);

    const val = this.form.getRawValue();

    if (this.isEditMode()) {
      const updateRequest: UpdatePersonRequest = {
        firstName: val.firstName,
        lastName: val.lastName,
        version: this.personVersion(),
        documentType: val.documentType !== null ? val.documentType : undefined,
        documentNumber: val.documentNumber ? val.documentNumber.trim() : undefined,
        documentIssuingCountry: val.documentIssuingCountry ? val.documentIssuingCountry.trim() : undefined,
        birthDate: val.birthDate || undefined,
        gender: val.gender !== null ? val.gender : undefined,
        email: val.email ? val.email.trim() : undefined,
        primaryPhone: val.primaryPhone ? val.primaryPhone.trim() : undefined,
        secondaryPhone: val.secondaryPhone ? val.secondaryPhone.trim() : undefined,
        address: val.address?.street || val.address?.city ? val.address : undefined,
        emergencyContact: val.emergencyContact?.name ? val.emergencyContact : undefined
      };

      this.peopleService.update(this.personId()!, updateRequest).subscribe({
        next: (saved) => {
          this.isSubmitting.set(false);
          this.router.navigate(['/portal/personas', saved.id]);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          if (err.status === 409) {
            this.isVersionConflict.set(err.error?.message?.includes('concurrentemente'));
            this.errorMessage.set(err.error?.message || 'Conflicto: la ficha fue modificada concurrentemente o los datos ya existen.');
          } else {
            this.errorMessage.set(err.error?.message || 'Error al actualizar la persona.');
          }
        }
      });
    } else {
      const createRequest: CreatePersonRequest = {
        firstName: val.firstName,
        lastName: val.lastName,
        documentType: val.documentType !== null ? val.documentType : undefined,
        documentNumber: val.documentNumber ? val.documentNumber.trim() : undefined,
        documentIssuingCountry: val.documentIssuingCountry ? val.documentIssuingCountry.trim() : undefined,
        birthDate: val.birthDate || undefined,
        gender: val.gender !== null ? val.gender : undefined,
        email: val.email ? val.email.trim() : undefined,
        primaryPhone: val.primaryPhone ? val.primaryPhone.trim() : undefined,
        secondaryPhone: val.secondaryPhone ? val.secondaryPhone.trim() : undefined,
        address: val.address?.street || val.address?.city ? val.address : undefined,
        emergencyContact: val.emergencyContact?.name ? val.emergencyContact : undefined
      };

      this.peopleService.create(createRequest).subscribe({
        next: (saved) => {
          this.isSubmitting.set(false);
          this.router.navigate(['/portal/personas', saved.id]);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          if (err.status === 409) {
            this.errorMessage.set(err.error?.message || 'Ya existe una persona registrada con ese documento o correo.');
          } else {
            this.errorMessage.set(err.error?.message || 'Error al crear la persona.');
          }
        }
      });
    }
  }
}
