import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { GymApiService } from '../../../core/services/gym-api.service';
import { RecurringSchedule, Activity } from '../../../core/models/gym.models';

@Component({
  selector: 'app-schedules',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 py-12 px-4 sm:px-6 lg:px-8">
      <div class="max-w-7xl mx-auto">
        <!-- Header -->
        <div class="text-center max-w-3xl mx-auto mb-10">
          <h1 class="text-4xl font-black text-white">Grilla Semanal de Clases</h1>
          <p class="text-slate-400 mt-2 text-lg">Consulta los días, turnos y docentes de cada actividad.</p>
        </div>

        <!-- Day Selector Tabs -->
        <div class="flex flex-wrap justify-center gap-2 mb-10">
          @for (day of daysOfWeek; track day.value) {
            <button
              (click)="selectedDay.set(day.value)"
              [class.bg-cyan-500]="selectedDay() === day.value"
              [class.text-slate-950]="selectedDay() === day.value"
              [class.font-bold]="selectedDay() === day.value"
              [class.bg-slate-900]="selectedDay() !== day.value"
              [class.text-slate-400]="selectedDay() !== day.value"
              class="px-4 py-2 rounded-xl text-sm border border-slate-800 transition hover:text-white"
            >
              {{ day.label }}
            </button>
          }
        </div>

        <!-- Schedules Grid -->
        <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          @for (item of filteredSchedules(); track item.id) {
            <div class="bg-slate-900 border border-slate-800 rounded-2xl p-6 hover:border-cyan-500/40 transition">
              <div class="flex justify-between items-start mb-4">
                <div>
                  <h3 class="text-xl font-bold text-white">{{ item.activityName }}</h3>
                  <span class="text-xs text-cyan-400 font-medium">{{ item.roomName }}</span>
                </div>
                <div class="text-right">
                  <span class="text-lg font-black text-white">{{ item.startTime.substring(0, 5) }}</span>
                  <span class="text-xs text-slate-500 block">a {{ item.endTime.substring(0, 5) }}</span>
                </div>
              </div>

              <div class="pt-4 border-t border-slate-800/80 flex justify-between items-center text-xs text-slate-400">
                <div class="flex items-center gap-1.5">
                  <span class="w-2 h-2 rounded-full bg-emerald-400"></span>
                  <span>Prof. {{ item.instructorName }}</span>
                </div>
                <span class="font-medium">Cupo: {{ item.maxCapacity || 30 }}</span>
              </div>
            </div>
          } @empty {
            <div class="col-span-full text-center py-12 bg-slate-900/30 rounded-2xl border border-slate-800/60 text-slate-500">
              No hay clases programadas para este día de la semana.
            </div>
          }
        </div>
      </div>
    </div>
  `
})
export class SchedulesComponent implements OnInit {
  private readonly gymApi = inject(GymApiService);
  private readonly route = inject(ActivatedRoute);

  readonly schedules = signal<RecurringSchedule[]>([]);
  readonly selectedDay = signal<number>(1); // Lunes por defecto

  readonly daysOfWeek = [
    { value: 1, label: 'Lunes' },
    { value: 2, label: 'Martes' },
    { value: 3, label: 'Miércoles' },
    { value: 4, label: 'Jueves' },
    { value: 5, label: 'Viernes' },
    { value: 6, label: 'Sábado' },
    { value: 0, label: 'Domingo' }
  ];

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const activityId = params['activityId'];
      this.gymApi.getSchedules(activityId).subscribe({
        next: (items) => this.schedules.set(items),
        error: () => this.schedules.set([])
      });
    });
  }

  filteredSchedules(): RecurringSchedule[] {
    return this.schedules().filter(s => s.dayOfWeek === this.selectedDay());
  }
}
