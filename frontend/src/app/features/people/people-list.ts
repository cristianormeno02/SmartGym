import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { PeopleService } from '../../core/services/people.service';
import {
  PersonSummary,
  PersonStatus,
  PersonStatusLabels,
  DocumentType,
  DocumentTypeLabels
} from '../../core/models/person.model';

@Component({
  selector: 'app-people-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-8 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto space-y-6">

        <!-- Top Header & New Button -->
        <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <div class="flex items-center gap-2">
              <span class="h-2.5 w-2.5 rounded-full bg-cyan-400"></span>
              <span class="text-xs uppercase tracking-widest font-semibold text-cyan-400">Directorio General</span>
            </div>
            <h1 class="text-3xl font-extrabold text-white mt-1">Gestión de Personas</h1>
            <p class="text-sm text-slate-400">Padrón maestro de clientes, docentes y personal del gimnasio.</p>
          </div>
          <div>
            <a routerLink="/portal/personas/nueva"
               class="inline-flex items-center gap-2 bg-cyan-500 hover:bg-cyan-400 text-slate-950 font-bold px-4 py-2.5 rounded-xl transition shadow-lg hover:shadow-cyan-500/20">
              <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4"/>
              </svg>
              Nueva Persona
            </a>
          </div>
        </div>

        <!-- Filter & Search Toolbar -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl p-4 shadow-lg flex flex-col md:flex-row gap-4 items-center justify-between">
          <div class="relative flex-1 w-full">
            <div class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-500">
              <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"/>
              </svg>
            </div>
            <input type="text"
                   [ngModel]="searchTerm()"
                   (ngModelChange)="onSearchInput($event)"
                   placeholder="Buscar por nombre, apellido o número de documento..."
                   class="w-full bg-slate-950 border border-slate-800 rounded-xl pl-10 pr-4 py-2.5 text-sm text-white placeholder-slate-500 focus:outline-none focus:border-cyan-500 transition" />
          </div>

          <!-- Status Filter -->
          <div class="flex items-center gap-2 w-full md:w-auto">
            <label class="text-xs font-semibold text-slate-400 uppercase whitespace-nowrap">Estado:</label>
            <select [ngModel]="selectedStatus()"
                    (ngModelChange)="onStatusChange($event)"
                    class="bg-slate-950 border border-slate-800 rounded-xl px-3 py-2.5 text-sm text-white focus:outline-none focus:border-cyan-500 transition w-full md:w-44">
              <option [ngValue]="undefined">Todos</option>
              <option [ngValue]="PersonStatus.Active">Activa</option>
              <option [ngValue]="PersonStatus.Inactive">Inactiva</option>
              <option [ngValue]="PersonStatus.Blocked">Bloqueada</option>
              <option [ngValue]="PersonStatus.Deceased">Fallecida</option>
            </select>
          </div>
        </div>

        <!-- People Table -->
        <div class="bg-slate-900 border border-slate-800 rounded-2xl shadow-xl overflow-hidden">
          <div class="overflow-x-auto">
            <table class="w-full text-left text-sm">
              <thead class="text-xs text-slate-400 bg-slate-950/70 border-b border-slate-800 uppercase tracking-wider">
                <tr>
                  <th class="py-3.5 px-4">Persona</th>
                  <th class="py-3.5 px-4">Documento</th>
                  <th class="py-3.5 px-4">Contacto</th>
                  <th class="py-3.5 px-4">Estado</th>
                  <th class="py-3.5 px-4 text-center">Usuario</th>
                  <th class="py-3.5 px-4 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-800/80">
                @if (peopleService.isBusy() && people().length === 0) {
                  <tr>
                    <td colspan="6" class="py-12 text-center text-slate-500">
                      <div class="inline-block animate-spin rounded-full h-8 w-8 border-t-2 border-b-2 border-cyan-400"></div>
                      <p class="mt-2 text-xs">Cargando personas...</p>
                    </td>
                  </tr>
                } @else if (people().length === 0) {
                  <tr>
                    <td colspan="6" class="py-12 text-center text-slate-500">
                      No se encontraron personas con los filtros ingresados.
                    </td>
                  </tr>
                } @else {
                  @for (person of people(); track person.id) {
                    <tr class="hover:bg-slate-800/40 transition"
                        [ngClass]="{'bg-rose-950/20 border-l-4 border-l-rose-500': person.status === PersonStatus.Blocked}">
                      
                      <!-- Persona Photo & Name -->
                      <td class="py-3.5 px-4">
                        <div class="flex items-center gap-3">
                          @if (person.photoUrl) {
                            <img [src]="person.photoUrl" alt="" class="w-10 h-10 rounded-full object-cover border border-slate-700" />
                          } @else {
                            <div class="w-10 h-10 rounded-full bg-slate-800 border border-slate-700 flex items-center justify-center font-bold text-slate-300 text-xs">
                              {{ person.firstName.charAt(0) }}{{ person.lastName.charAt(0) }}
                            </div>
                          }
                          <div>
                            <div class="font-bold text-slate-100 flex items-center gap-2">
                              {{ person.lastName }}, {{ person.firstName }}
                              @if (person.status === PersonStatus.Blocked) {
                                <span class="bg-rose-500/20 border border-rose-500/40 text-rose-300 text-[10px] font-black px-1.5 py-0.5 rounded tracking-wider uppercase">
                                  BLOQUEADA
                                </span>
                              }
                            </div>
                          </div>
                        </div>
                      </td>

                      <!-- Documento -->
                      <td class="py-3.5 px-4 text-slate-300 text-xs font-mono">
                        @if (person.document) {
                          <span class="text-slate-400 mr-1">{{ getDocumentTypeLabel(person.document.type) }}:</span>
                          {{ person.document.number }}
                        } @else {
                          <span class="text-slate-500 italic">Sin documento</span>
                        }
                      </td>

                      <!-- Contacto -->
                      <td class="py-3.5 px-4 text-xs">
                        <div class="text-slate-300">{{ person.email ?? '—' }}</div>
                        <div class="text-slate-500">{{ person.primaryPhone ?? '' }}</div>
                      </td>

                      <!-- Estado -->
                      <td class="py-3.5 px-4">
                        <span class="text-xs px-2.5 py-1 rounded-full font-bold border"
                              [ngClass]="getStatusBadgeClass(person.status)">
                          {{ getStatusLabel(person.status) }}
                        </span>
                      </td>

                      <!-- Usuario -->
                      <td class="py-3.5 px-4 text-center">
                        @if (person.hasUserAccount) {
                          <span class="text-emerald-400 text-xs font-semibold inline-flex items-center gap-1">
                            <span class="h-2 w-2 rounded-full bg-emerald-400"></span> Vinculado
                          </span>
                        } @else {
                          <span class="text-slate-500 text-xs">No</span>
                        }
                      </td>

                      <!-- Acciones -->
                      <td class="py-3.5 px-4 text-right">
                        <a [routerLink]="['/portal/personas', person.id]"
                           class="text-xs font-bold text-cyan-400 hover:text-cyan-300 bg-cyan-950/40 hover:bg-cyan-900/40 border border-cyan-800/50 px-3 py-1.5 rounded-lg transition">
                          Ver Ficha
                        </a>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>

          <!-- Pagination Bar -->
          <div class="bg-slate-950/60 border-t border-slate-800 px-4 py-3 flex items-center justify-between">
            <div class="text-xs text-slate-400">
              Mostrando <span class="font-bold text-slate-200">{{ people().length }}</span> de <span class="font-bold text-slate-200">{{ totalCount() }}</span> personas
            </div>
            <div class="flex items-center gap-2">
              <button (click)="goToPage(currentPage() - 1)"
                      [disabled]="currentPage() <= 1 || peopleService.isBusy()"
                      class="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 disabled:opacity-40 text-xs rounded-lg font-semibold text-slate-300 transition">
                Anterior
              </button>
              <span class="text-xs text-slate-400">Pág. {{ currentPage() }} de {{ totalPages() || 1 }}</span>
              <button (click)="goToPage(currentPage() + 1)"
                      [disabled]="currentPage() >= totalPages() || peopleService.isBusy()"
                      class="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 disabled:opacity-40 text-xs rounded-lg font-semibold text-slate-300 transition">
                Siguiente
              </button>
            </div>
          </div>
        </div>

      </div>
    </div>
  `
})
export class PeopleListComponent implements OnInit, OnDestroy {
  readonly peopleService = inject(PeopleService);
  private readonly router = inject(Router);

  readonly PersonStatus = PersonStatus;
  readonly DocumentType = DocumentType;

  readonly people = signal<PersonSummary[]>([]);
  readonly searchTerm = signal<string>('');
  readonly selectedStatus = signal<PersonStatus | undefined>(undefined);
  readonly currentPage = signal<number>(1);
  readonly totalPages = signal<number>(1);
  readonly totalCount = signal<number>(0);

  private readonly searchSubject = new Subject<string>();
  private searchSubscription?: Subscription;

  ngOnInit(): void {
    this.searchSubscription = this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged()
    ).subscribe((term) => {
      this.searchTerm.set(term);
      this.currentPage.set(1);
      this.loadPeople();
    });

    this.loadPeople();
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  onSearchInput(value: string): void {
    this.searchSubject.next(value);
  }

  onStatusChange(status?: PersonStatus): void {
    this.selectedStatus.set(status);
    this.currentPage.set(1);
    this.loadPeople();
  }

  goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
      this.loadPeople();
    }
  }

  loadPeople(): void {
    this.peopleService.search({
      search: this.searchTerm() || undefined,
      status: this.selectedStatus(),
      pageNumber: this.currentPage(),
      pageSize: 20
    }).subscribe({
      next: (result) => {
        this.people.set(result.items);
        this.currentPage.set(result.pageNumber);
        this.totalPages.set(result.totalPages);
        this.totalCount.set(result.totalCount);
      },
      error: (err) => {
        console.error('Error loading people:', err);
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
