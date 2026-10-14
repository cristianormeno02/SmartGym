import { Component, Input, Output, EventEmitter, OnInit, OnChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { GymApiService } from '../../../core/services/gym-api.service';
import { Room, CreateRoomRequest, UpdateRoomRequest } from '../../../core/models/gym.models';

@Component({
  selector: 'app-room-form-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm animate-fadeIn">
      <div class="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg shadow-2xl overflow-hidden flex flex-col">
        <!-- Modal Header -->
        <div class="p-6 border-b border-slate-800 flex justify-between items-center">
          <div>
            <h3 class="text-xl font-bold text-white">
              {{ isEditMode ? 'Editar Sala o Espacio' : 'Nueva Sala' }}
            </h3>
            <p class="text-xs text-slate-400 mt-1">Configura las salas físicas y aforo disponible del gimnasio</p>
          </div>
          <button (click)="cancelled.emit()" class="text-slate-400 hover:text-white text-2xl leading-none">&times;</button>
        </div>

        <!-- Form Body -->
        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="p-6 space-y-5">
          @if (errorMessage()) {
            <div class="bg-rose-500/10 border border-rose-500/30 text-rose-400 px-4 py-3 rounded-xl text-sm">
              {{ errorMessage() }}
            </div>
          }

          <!-- Name -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Nombre de la Sala *
            </label>
            <input
              type="text"
              formControlName="name"
              placeholder="ej. Sala de Spinning, Musculación, Salón A"
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
            />
            @if (form.get('name')?.touched && form.get('name')?.hasError('required')) {
              <span class="text-xs text-rose-400 mt-1 block">El nombre es obligatorio.</span>
            }
          </div>

          <!-- Description -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Descripción / Ubicación
            </label>
            <input
              type="text"
              formControlName="description"
              placeholder="ej. Piso 1 al fondo, equipada con bicicletas fijas"
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
            />
          </div>

          <!-- Capacity -->
          <div>
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-300 mb-1.5">
              Capacidad Máxima (Aforo) *
            </label>
            <input
              type="number"
              formControlName="capacity"
              min="1"
              class="w-full bg-slate-950 border border-slate-800 rounded-xl px-4 py-2.5 text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition"
            />
            @if (form.get('capacity')?.touched && (form.get('capacity')?.hasError('required') || form.get('capacity')?.hasError('min'))) {
              <span class="text-xs text-rose-400 mt-1 block">La capacidad debe ser de al menos 1 persona.</span>
            }
          </div>

          <!-- Active Toggle (only in edit mode) -->
          @if (isEditMode) {
            <div class="flex items-center justify-between pt-2">
              <div>
                <span class="text-sm font-semibold text-white block">Estado de la sala</span>
                <span class="text-xs text-slate-400">Si está inactiva no podrá recibir nuevos turnos</span>
              </div>
              <label class="relative inline-flex items-center cursor-pointer">
                <input type="checkbox" formControlName="isActive" class="sr-only peer" />
                <div class="w-11 h-6 bg-slate-800 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-emerald-500"></div>
              </label>
            </div>
          }

          <!-- Actions -->
          <div class="flex justify-end gap-3 pt-4 border-t border-slate-800">
            <button
              type="button"
              (click)="cancelled.emit()"
              class="px-5 py-2.5 rounded-xl border border-slate-700 text-slate-300 hover:text-white hover:bg-slate-800 text-sm font-semibold transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              [disabled]="form.invalid || isSubmitting()"
              class="px-5 py-2.5 rounded-xl bg-cyan-500 hover:bg-cyan-400 disabled:opacity-50 text-slate-950 font-bold text-sm transition flex items-center gap-2"
            >
              @if (isSubmitting()) {
                <span class="w-4 h-4 border-2 border-slate-950 border-t-transparent rounded-full animate-spin"></span>
              }
              {{ isEditMode ? 'Guardar Cambios' : 'Crear Sala' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class RoomFormModalComponent implements OnInit, OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly gymApi = inject(GymApiService);

  @Input() room: Room | null = null;
  @Output() saved = new EventEmitter<Room>();
  @Output() cancelled = new EventEmitter<void>();

  readonly isSubmitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  form: FormGroup = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', [Validators.maxLength(250)]],
    capacity: [20, [Validators.required, Validators.min(1)]],
    isActive: [true]
  });

  get isEditMode(): boolean {
    return !!this.room?.id;
  }

  ngOnInit(): void {
    this.initForm();
  }

  ngOnChanges(): void {
    this.initForm();
  }

  private initForm(): void {
    if (this.room) {
      this.form.patchValue({
        name: this.room.name,
        description: this.room.description || '',
        capacity: this.room.capacity,
        isActive: this.room.isActive
      });
    } else {
      this.form.reset({
        name: '',
        description: '',
        capacity: 20,
        isActive: true
      });
    }
    this.errorMessage.set(null);
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const formValue = this.form.value;

    if (this.isEditMode && this.room?.id) {
      const updateReq: UpdateRoomRequest = {
        name: formValue.name.trim(),
        description: formValue.description?.trim() || undefined,
        capacity: Number(formValue.capacity),
        isActive: !!formValue.isActive
      };

      this.gymApi.updateRoom(this.room.id, updateReq).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Error al actualizar la sala.');
        }
      });
    } else {
      const createReq: CreateRoomRequest = {
        name: formValue.name.trim(),
        description: formValue.description?.trim() || undefined,
        capacity: Number(formValue.capacity)
      };

      this.gymApi.createRoom(createReq).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || 'Error al crear la sala.');
        }
      });
    }
  }
}
