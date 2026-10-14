export interface WeekDayOption {
  day: number;
  name: string;
}

export const WEEK_DAYS: readonly WeekDayOption[] = [
  { day: 1, name: 'Lunes' },
  { day: 2, name: 'Martes' },
  { day: 3, name: 'Miércoles' },
  { day: 4, name: 'Jueves' },
  { day: 5, name: 'Viernes' },
  { day: 6, name: 'Sábado' },
  { day: 0, name: 'Domingo' }
] as const;

export function getDayOfWeekName(day: number): string {
  const found = WEEK_DAYS.find((d) => d.day === day);
  return found ? found.name : 'Desconocido';
}

export function getDayOfWeekFromName(name: string): number {
  const normalized = name.trim().toLowerCase();
  const map: Record<string, number> = {
    lunes: 1,
    martes: 2,
    miercoles: 3,
    miércoles: 3,
    jueves: 4,
    viernes: 5,
    sabado: 6,
    sábado: 6,
    domingo: 0
  };
  return map[normalized] ?? -1;
}

export function formatTimeForApi(time: string): string {
  if (!time) return '';
  const trimmed = time.trim();
  if (/^\d{2}:\d{2}$/.test(trimmed)) {
    return `${trimmed}:00`;
  }
  return trimmed;
}

export function formatTimeForInput(time: string): string {
  if (!time) return '';
  const trimmed = time.trim();
  if (/^\d{2}:\d{2}:\d{2}$/.test(trimmed)) {
    return trimmed.substring(0, 5);
  }
  return trimmed;
}

/** Fecha local de hoy en formato "yyyy-MM-dd" (DateOnly del backend / input type="date"). */
export function todayIsoDate(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}
