import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { GymApiService } from '../../../core/services/gym-api.service';
import { Room } from '../../../core/models/gym.models';
import { RoomFormModalComponent } from '../room-form-modal/room-form-modal';

@Component({
  selector: 'app-rooms-management',
  standalone: true,
  imports: [CommonModule, RoomFormModalComponent],
  template: `
    <div class="space-y-6 animate-fadeIn">
      <!-- Section Header -->
      <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 shadow-xl flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h2 class="text-2xl font-bold text-white flex items-center gap-2">
            <span>🏢</span> Salas y Espacios Físicos
          </h2>
          <p class="text-sm text-slate-400 mt-1">
            Administra los salones, aforos máximos y disponibilidad para turnos de clases
          </p>
        </div>
        <button
          (click)="openCreateModal()"
          class="px-5 py-2.5 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold text-sm transition flex items-center gap-2 self-start sm:self-auto shadow-lg shadow-cyan-500/20"
        >
          <span>+</span> Nueva Sala
        </button>
      </div>

      <!-- Feedback banner -->
      @if (feedbackMessage()) {
        <div class="bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 px-4 py-3 rounded-xl text-sm flex justify-between items-center">
          <span>{{ feedbackMessage() }}</span>
          <button (click)="feedbackMessage.set(null)" class="text-emerald-400 hover:text-white">&times;</button>
        </div>
      }

      <!-- Rooms Table -->
      <div class="bg-slate-900 border border-slate-800 rounded-2xl shadow-xl overflow-hidden">
        <div class="overflow-x-auto">
          <table class="w-full text-left text-sm text-slate-300">
            <thead class="text-xs uppercase bg-slate-950/70 text-slate-400 border-b border-slate-800">
              <tr>
                <th class="py-3.5 px-6">Nombre de la Sala</th>
                <th class="py-3.5 px-6">Descripción / Ubicación</th>
                <th class="py-3.5 px-6 text-center">Aforo Máximo</th>
                <th class="py-3.5 px-6 text-center">Estado</th>
                <th class="py-3.5 px-6 text-right">Acciones</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-800/80">
              @for (room of rooms(); track room.id) {
                <tr class="hover:bg-slate-800/30 transition">
                  <td class="py-4 px-6 font-bold text-white flex items-center gap-2">
                    <span class="w-2.5 h-2.5 rounded-full" [ngClass]="room.isActive ? 'bg-emerald-400' : 'bg-slate-600'"></span>
                    {{ room.name }}
                  </td>
                  <td class="py-4 px-6 text-slate-400">{{ room.description || '-' }}</td>
                  <td class="py-4 px-6 text-center">
                    <span class="px-2.5 py-1 rounded-md bg-slate-800 text-cyan-400 font-bold text-xs border border-slate-700">
                      {{ room.capacity }} personas
                    </span>
                  </td>
                  <td class="py-4 px-6 text-center">
                    @if (room.isActive) {
                      <span class="px-2.5 py-1 rounded-full text-xs font-bold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                        Activa
                      </span>
                    } @else {
                      <span class="px-2.5 py-1 rounded-full text-xs font-bold bg-slate-700/50 text-slate-400 border border-slate-600">
                        Inactiva
                      </span>
                    }
                  </td>
                  <td class="py-4 px-6 text-right space-x-2 whitespace-nowrap">
                    <button
                      (click)="openEditModal(room)"
                      class="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-semibold text-white border border-slate-700 transition"
                    >
                      Editar
                    </button>
                    <button
                      (click)="toggleRoomStatus(room)"
                      class="px-3 py-1.5 rounded-lg text-xs font-semibold border transition"
                      [ngClass]="room.isActive ? 'border-amber-500/40 text-amber-400 hover:bg-amber-500/10' : 'border-emerald-500/40 text-emerald-400 hover:bg-emerald-500/10'"
                    >
                      {{ room.isActive ? 'Desactivar' : 'Reactivar' }}
                    </button>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5" class="py-8 text-center text-slate-500">
                    No se han registrado salas físicas todavía.
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>

      <!-- Modal Component -->
      @if (isModalOpen()) {
        <app-room-form-modal
          [room]="selectedRoom()"
          (saved)="onRoomSaved()"
          (cancelled)="isModalOpen.set(false)"
        ></app-room-form-modal>
      }
    </div>
  `
})
export class RoomsManagementComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);

  readonly rooms = signal<Room[]>([]);
  readonly isModalOpen = signal(false);
  readonly selectedRoom = signal<Room | null>(null);
  readonly feedbackMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.loadRooms();
  }

  loadRooms(): void {
    this.gymApi.getRooms().subscribe({
      next: (data) => this.rooms.set(data),
      error: () => this.rooms.set([])
    });
  }

  openCreateModal(): void {
    this.selectedRoom.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(room: Room): void {
    this.selectedRoom.set(room);
    this.isModalOpen.set(true);
  }

  onRoomSaved(): void {
    this.isModalOpen.set(false);
    this.feedbackMessage.set('Sala guardada exitosamente.');
    this.loadRooms();
  }

  toggleRoomStatus(room: Room): void {
    const nextStatus = !room.isActive;
    this.gymApi.updateRoom(room.id, {
      name: room.name,
      description: room.description,
      capacity: room.capacity,
      isActive: nextStatus
    }).subscribe({
      next: () => {
        this.feedbackMessage.set(`Sala "${room.name}" ${nextStatus ? 'reactivada' : 'desactivada'} exitosamente.`);
        this.loadRooms();
      },
      error: (err) => {
        this.feedbackMessage.set(err.error?.message || 'Error al cambiar el estado de la sala.');
      }
    });
  }
}
