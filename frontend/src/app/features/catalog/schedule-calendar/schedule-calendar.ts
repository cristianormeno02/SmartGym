import { Component, Input, Output, EventEmitter, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { WEEK_DAYS } from '../../../core/utils/catalog-mappings';

export interface CalendarLayoutItem {
  schedule: RecurringSchedule;
  startMinutes: number;
  endMinutes: number;
  durationMinutes: number;
  topPx: number;
  heightPx: number;
  colIndex: number;
  totalCols: number;
  leftPercent: number;
  widthPercent: number;
}

export interface HourRange {
  startHour: number;
  endHour: number;
}

const HOUR_HEIGHT_PX = 60;
const DEFAULT_ACTIVITY_COLOR_HEX = '#06B6D4';
const HEX_COLOR_REGEX = /^#[0-9A-Fa-f]{6}$/;

function timeToMinutes(time: string): number {
  if (!time) return 0;
  const [h, m] = time.split(':').map(Number);
  return (h || 0) * 60 + (m || 0);
}

function toIsoDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const clean = hex.replace('#', '');
  const r = parseInt(clean.substring(0, 2), 16) || 6;
  const g = parseInt(clean.substring(2, 4), 16) || 182;
  const b = parseInt(clean.substring(4, 6), 16) || 212;
  return { r, g, b };
}

@Component({
  selector: 'app-schedule-calendar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="space-y-4 select-none">
      <!-- Calendar Toolbar -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 bg-slate-900 border border-slate-800 rounded-2xl p-4">
        <!-- Week Navigation -->
        <div class="flex items-center gap-2">
          <button
            type="button"
            (click)="prevWeek()"
            class="btn-prev-week p-2 rounded-xl text-slate-400 hover:text-white hover:bg-slate-800 transition"
            title="Semana anterior"
          >
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
            </svg>
          </button>
          <button
            type="button"
            (click)="currentWeek()"
            class="btn-current-week px-3 py-1.5 rounded-xl text-xs font-semibold text-slate-300 bg-slate-800 hover:bg-slate-700 transition"
          >
            Esta semana
          </button>
          <button
            type="button"
            (click)="nextWeek()"
            class="btn-next-week p-2 rounded-xl text-slate-400 hover:text-white hover:bg-slate-800 transition"
            title="Semana siguiente"
          >
            <svg class="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
            </svg>
          </button>
          <span class="text-sm font-bold text-white ml-2">
            {{ weekLabel() }}
          </span>
        </div>

        <!-- Info & Hour Range Mode Toggle -->
        <div class="flex flex-wrap items-center justify-between sm:justify-end gap-3 text-xs text-slate-400">
          <button
            type="button"
            (click)="toggleHourRangeMode()"
            [class.bg-cyan-950/60]="!showFullHourRange()"
            [class.text-cyan-300]="!showFullHourRange()"
            [class.border-cyan-800/80]="!showFullHourRange()"
            [class.bg-slate-800]="showFullHourRange()"
            [class.text-slate-300]="showFullHourRange()"
            [class.border-slate-700]="showFullHourRange()"
            class="btn-toggle-hour-range px-3 py-1.5 rounded-xl text-xs font-semibold border transition hover:text-white"
          >
            {{ showFullHourRange() ? 'Mostrar solo horarios con actividad' : 'Ver todo el rango horario' }}
          </button>
          <span>{{ visibleSchedules().length }} turnos vigentes esta semana</span>
        </div>
      </div>

      <!-- Mobile Day Selector (Pantallas < md) -->
      <div class="block md:hidden">
        <div class="flex overflow-x-auto gap-1.5 pb-2">
          @for (day of weekDays; track day.day) {
            <button
              type="button"
              (click)="selectMobileDay(day.day)"
              [class.bg-cyan-500]="selectedMobileDay() === day.day"
              [class.text-slate-950]="selectedMobileDay() === day.day"
              [class.font-bold]="selectedMobileDay() === day.day"
              [class.bg-slate-900]="selectedMobileDay() !== day.day"
              [class.text-slate-400]="selectedMobileDay() !== day.day"
              class="px-3 py-1.5 rounded-xl text-xs border border-slate-800 whitespace-nowrap transition"
            >
              {{ day.name }}
              <span class="ml-1 text-[10px] opacity-75">({{ countForDay(day.day) }})</span>
            </button>
          }
        </div>

        <!-- Mobile Day Schedule List -->
        <div class="space-y-2.5 mt-2">
          @for (item of mobileDayItems(); track item.schedule.id) {
            <div
              (click)="onSelectSchedule(item.schedule)"
              class="p-4 rounded-xl border cursor-pointer transition shadow-md flex items-center justify-between text-slate-100"
              [ngStyle]="getActivityBlockStyle(item.schedule)"
            >
              <div>
                <div class="font-bold text-sm text-white">{{ item.schedule.activityName }}</div>
                <div class="text-xs opacity-90 mt-0.5">
                  {{ item.schedule.roomName }} • Prof. {{ item.schedule.instructorName }}
                </div>
              </div>
              <div class="text-right">
                <div class="font-mono font-bold text-sm text-white">
                  {{ item.schedule.startTime.substring(0, 5) }} - {{ item.schedule.endTime.substring(0, 5) }}
                </div>
                <div class="text-[11px] opacity-75 mt-0.5">
                  Cupo: {{ item.schedule.maxCapacity || 'Actividad' }}
                </div>
              </div>
            </div>
          } @empty {
            <div class="text-center py-8 bg-slate-900/40 rounded-xl border border-slate-800 text-xs text-slate-500">
              No hay turnos para este día en la semana visible.
            </div>
          }
        </div>
      </div>

      <!-- Desktop Calendar Grid (Pantallas >= md) -->
      <div class="hidden md:block bg-slate-900/60 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
        <!-- Week Days Column Headers -->
        <div class="grid grid-cols-8 border-b border-slate-800 bg-slate-900/90 text-center text-xs font-bold uppercase tracking-wider text-slate-400">
          <div class="py-3 px-2 border-r border-slate-800/80 text-[11px] text-slate-500">
            Hora
          </div>
          @for (day of weekDays; track day.day) {
            <div class="py-3 px-2 border-r border-slate-800/80 last:border-r-0">
              <span class="text-white">{{ day.name }}</span>
              <span class="block text-[10px] text-slate-500 font-normal mt-0.5">
                {{ countForDay(day.day) }} clases
              </span>
            </div>
          }
        </div>

        @if (hoursList().length === 0) {
          <div class="text-center py-12 text-xs text-slate-500">
            No hay horarios con actividades programadas en esta semana.
          </div>
        } @else {
          <!-- Grid Body with Fixed Hour Height -->
          <div class="relative overflow-y-auto max-h-[700px] select-text">
            <div class="grid grid-cols-8 relative" [style.height.px]="totalGridHeightPx()">
              <!-- Column 1: Time Labels Axis -->
              <div class="border-r border-slate-800/80 relative bg-slate-950/40">
                @for (hour of hoursList(); track hour) {
                  <div
                    class="absolute w-full text-right pr-2 text-[11px] font-mono text-slate-400 top-1 pointer-events-none"
                    [style.top.px]="getHourTopPx(hour)"
                  >
                    {{ formatHourLabel(hour) }}
                  </div>
                }
              </div>

              <!-- Columns 2-8: Monday to Sunday -->
              @for (day of weekDays; track day.day) {
                <div class="border-r border-slate-800/80 last:border-r-0 relative bg-slate-900/20">
                  <!-- Hour Horizontal Grid Lines -->
                  @for (hour of hoursList(); track hour) {
                    <div
                      class="absolute w-full border-t border-slate-800/40 pointer-events-none"
                      [style.top.px]="getHourTopPx(hour)"
                    ></div>
                  }

                  <!-- Scheduled Blocks -->
                  @for (item of dayLayouts().get(day.day) || []; track item.schedule.id) {
                    <div
                      (click)="onSelectSchedule(item.schedule)"
                      class="calendar-schedule-block absolute rounded-lg border p-1.5 overflow-hidden cursor-pointer transition shadow hover:shadow-lg hover:z-20 text-[11px] leading-tight flex flex-col justify-between text-slate-100"
                      [ngStyle]="getActivityBlockStyle(item.schedule)"
                      [style.top.px]="item.topPx"
                      [style.height.px]="item.heightPx"
                      [style.left.%]="item.leftPercent"
                      [style.width.%]="item.widthPercent"
                      [title]="item.schedule.activityName + ' (' + item.schedule.roomName + ')'"
                    >
                      <div>
                        <div class="font-bold truncate text-white">
                          {{ item.schedule.activityName }}
                        </div>
                        <div class="text-[10px] opacity-90 truncate">
                          {{ item.schedule.roomName }}
                        </div>
                      </div>
                      <div class="font-mono text-[10px] opacity-95 truncate mt-auto">
                        {{ item.schedule.startTime.substring(0, 5) }} - {{ item.schedule.endTime.substring(0, 5) }}
                      </div>
                    </div>
                  }
                </div>
              }
            </div>
          </div>
        }
      </div>
    </div>
  `
})
export class ScheduleCalendarComponent {
  private readonly _schedules = signal<RecurringSchedule[]>([]);

  @Input()
  set schedules(val: RecurringSchedule[]) {
    this._schedules.set(val || []);
  }
  get schedules(): RecurringSchedule[] {
    return this._schedules();
  }

  @Input() canManage: boolean = false;
  @Output() scheduleSelected = new EventEmitter<RecurringSchedule>();

  readonly weekDays = WEEK_DAYS;
  readonly referenceDate = signal<Date>(new Date());
  readonly selectedMobileDay = signal<number>(1); // Lunes por defecto
  readonly showFullHourRange = signal<boolean>(false);

  readonly weekRange = computed(() => {
    const ref = new Date(this.referenceDate());
    const day = ref.getDay(); // 0 is Sunday, 1 is Monday
    const diffToMonday = ref.getDate() - day + (day === 0 ? -6 : 1);

    const monday = new Date(ref);
    monday.setDate(diffToMonday);
    monday.setHours(0, 0, 0, 0);

    const sunday = new Date(monday);
    sunday.setDate(monday.getDate() + 6);
    sunday.setHours(23, 59, 59, 999);

    return {
      monday,
      sunday,
      mondayIso: toIsoDate(monday),
      sundayIso: toIsoDate(sunday)
    };
  });

  readonly weekLabel = computed(() => {
    const { monday, sunday } = this.weekRange();
    const options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short' };
    const monStr = monday.toLocaleDateString('es-AR', options);
    const sunStr = sunday.toLocaleDateString('es-AR', options);
    return `${monStr} - ${sunStr}, ${sunday.getFullYear()}`;
  });

  readonly visibleSchedules = computed(() => {
    const { mondayIso, sundayIso } = this.weekRange();
    return this._schedules().filter(s => {
      if (!s.isActive) return false;
      const from = s.validFrom ? s.validFrom.substring(0, 10) : '';
      const to = s.validTo ? s.validTo.substring(0, 10) : null;

      const startsBeforeWeekEnd = from <= sundayIso;
      const endsAfterWeekStart = !to || to >= mondayIso;

      return startsBeforeWeekEnd && endsAfterWeekStart;
    });
  });

  readonly hourRange = computed<HourRange>(() => {
    const visible = this.visibleSchedules();
    if (visible.length === 0) {
      return { startHour: 7, endHour: 22 };
    }

    let minH = 23;
    let maxH = 0;

    for (const s of visible) {
      const [sh] = s.startTime.split(':').map(Number);
      const [eh, em] = s.endTime.split(':').map(Number);
      const endCeil = (em || 0) > 0 ? eh + 1 : eh;
      if (sh < minH) minH = sh;
      if (endCeil > maxH) maxH = endCeil;
    }

    const startHour = Math.min(minH, 7);
    const endHour = Math.max(maxH, 22);

    return { startHour, endHour };
  });

  readonly hoursList = computed<number[]>(() => {
    const visible = this.visibleSchedules();
    if (this.showFullHourRange()) {
      const { startHour, endHour } = this.hourRange();
      const list: number[] = [];
      for (let h = startHour; h <= endHour; h++) {
        list.push(h);
      }
      return list;
    }

    const activeHours = new Set<number>();
    for (const s of visible) {
      const startMin = timeToMinutes(s.startTime);
      const endMin = timeToMinutes(s.endTime);
      const firstHour = Math.floor(startMin / 60);
      const lastHour = Math.max(firstHour, Math.ceil(endMin / 60) - 1);
      for (let h = firstHour; h <= lastHour; h++) {
        if (h >= 0 && h <= 23) {
          activeHours.add(h);
        }
      }
    }

    return Array.from(activeHours).sort((a, b) => a - b);
  });

  readonly totalGridHeightPx = computed(() => {
    const hours = this.hoursList();
    return Math.max(1, hours.length) * HOUR_HEIGHT_PX;
  });

  readonly dayLayouts = computed(() => {
    const layouts = new Map<number, CalendarLayoutItem[]>();
    const hours = this.hoursList();
    const visible = this.visibleSchedules();

    for (const wd of this.weekDays) {
      const daySchedules = visible.filter(s => s.dayOfWeek === wd.day);

      const items: {
        schedule: RecurringSchedule;
        startMin: number;
        endMin: number;
        duration: number;
      }[] = daySchedules.map(s => {
        const startMin = timeToMinutes(s.startTime);
        const endMin = timeToMinutes(s.endTime);
        const duration = Math.max(15, endMin - startMin);
        return { schedule: s, startMin, endMin, duration };
      });

      // Ordenar por hora de inicio y luego duración descendente
      items.sort((a, b) => a.startMin - b.startMin || b.duration - a.duration);

      // Algoritmo de particionado en subcolumnas (greedy graph coloring para intervalos)
      const dayResult: CalendarLayoutItem[] = [];

      // Agrupar en clusters continuos de solape
      let cluster: typeof items = [];
      let clusterEnd = -1;

      const processCluster = (cItems: typeof items) => {
        if (cItems.length === 0) return;

        // Asignar columnas
        const colEnds: number[] = [];
        const itemCols: number[] = [];

        for (const item of cItems) {
          let placed = false;
          for (let c = 0; c < colEnds.length; c++) {
            if (colEnds[c] <= item.startMin) {
              colEnds[c] = item.endMin;
              itemCols.push(c);
              placed = true;
              break;
            }
          }
          if (!placed) {
            itemCols.push(colEnds.length);
            colEnds.push(item.endMin);
          }
        }

        const totalCols = colEnds.length;

        for (let i = 0; i < cItems.length; i++) {
          const item = cItems[i];
          const colIndex = itemCols[i];
          const startHourOfItem = Math.floor(item.startMin / 60);
          const hourIdx = Math.max(0, hours.indexOf(startHourOfItem));
          const topPx = (hourIdx + (item.startMin % 60) / 60) * HOUR_HEIGHT_PX;
          const heightPx = Math.max(26, (item.duration / 60) * HOUR_HEIGHT_PX);
          const widthPercent = 100 / totalCols;
          const leftPercent = (colIndex / totalCols) * 100;

          dayResult.push({
            schedule: item.schedule,
            startMinutes: item.startMin,
            endMinutes: item.endMin,
            durationMinutes: item.duration,
            topPx,
            heightPx,
            colIndex,
            totalCols,
            leftPercent,
            widthPercent
          });
        }
      };

      for (const item of items) {
        if (cluster.length === 0 || item.startMin < clusterEnd) {
          cluster.push(item);
          clusterEnd = Math.max(clusterEnd, item.endMin);
        } else {
          processCluster(cluster);
          cluster = [item];
          clusterEnd = item.endMin;
        }
      }
      processCluster(cluster);

      layouts.set(wd.day, dayResult);
    }

    return layouts;
  });

  readonly mobileDayItems = computed(() => {
    return this.dayLayouts().get(this.selectedMobileDay()) || [];
  });

  countForDay(day: number): number {
    return (this.dayLayouts().get(day) || []).length;
  }

  getHourTopPx(hour: number): number {
    const idx = this.hoursList().indexOf(hour);
    return Math.max(0, idx) * HOUR_HEIGHT_PX;
  }

  formatHourLabel(hour: number): string {
    return `${String(hour).padStart(2, '0')}:00`;
  }

  getActivityColorHex(schedule: RecurringSchedule): string {
    const raw = schedule.activityColorHex?.trim();
    if (raw && HEX_COLOR_REGEX.test(raw)) {
      return raw.toUpperCase();
    }
    return DEFAULT_ACTIVITY_COLOR_HEX;
  }

  getActivityBlockStyle(schedule: RecurringSchedule): Record<string, string> {
    const hex = this.getActivityColorHex(schedule);
    const { r, g, b } = hexToRgb(hex);
    return {
      backgroundColor: `rgba(${r}, ${g}, ${b}, 0.22)`,
      borderColor: `rgba(${r}, ${g}, ${b}, 0.55)`,
      borderLeftColor: hex,
      borderLeftWidth: '4px'
    };
  }

  toggleHourRangeMode(): void {
    this.showFullHourRange.update(v => !v);
  }

  setReferenceDate(date: Date): void {
    this.referenceDate.set(new Date(date));
  }

  prevWeek(): void {
    const cur = new Date(this.referenceDate());
    cur.setDate(cur.getDate() - 7);
    this.referenceDate.set(cur);
  }

  nextWeek(): void {
    const cur = new Date(this.referenceDate());
    cur.setDate(cur.getDate() + 7);
    this.referenceDate.set(cur);
  }

  currentWeek(): void {
    this.referenceDate.set(new Date());
  }

  selectMobileDay(day: number): void {
    this.selectedMobileDay.set(day);
  }

  onSelectSchedule(schedule: RecurringSchedule): void {
    this.scheduleSelected.emit(schedule);
  }
}
