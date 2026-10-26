/**
 * Utilidades de manipulación, parseo y formateo de fechas y horas
 * asegurando convención DD/MM/AAAA y supresión de segundos (HH:mm) en SmartGym.
 */

/**
 * Valida si día, mes y año componen una fecha de calendario real.
 */
export function isValidDateParts(day: number, month: number, year: number): boolean {
  if (year < 1900 || year > 2100) return false;
  if (month < 1 || month > 12) return false;
  if (day < 1 || day > 31) return false;

  const daysInMonth = [
    31,
    isLeapYear(year) ? 29 : 28,
    31, 30, 31, 30,
    31, 31, 30, 31, 30, 31
  ];

  return day <= daysInMonth[month - 1];
}

function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

/**
 * Formatea una fecha o cadena ISO en `DD/MM/AAAA`.
 * Maneja cadenas ISO de solo fecha (yyyy-MM-dd) sin desfasaje por zona horaria.
 */
export function formatDateDisplay(value: Date | string | null | undefined): string {
  if (!value) return '';

  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!trimmed) return '';

    // Si es formato solo fecha "YYYY-MM-DD"
    const dateOnlyMatch = /^(\d{4})-(\d{1,2})-(\d{1,2})/.exec(trimmed);
    if (dateOnlyMatch) {
      const year = parseInt(dateOnlyMatch[1], 10);
      const month = parseInt(dateOnlyMatch[2], 10);
      const day = parseInt(dateOnlyMatch[3], 10);
      return `${String(day).padStart(2, '0')}/${String(month).padStart(2, '0')}/${year}`;
    }

    const d = new Date(trimmed);
    if (isNaN(d.getTime())) return '';
    return formatDateDisplay(d);
  }

  if (value instanceof Date) {
    if (isNaN(value.getTime())) return '';
    const day = String(value.getDate()).padStart(2, '0');
    const month = String(value.getMonth() + 1).padStart(2, '0');
    const year = value.getFullYear();
    return `${day}/${month}/${year}`;
  }

  return '';
}

/**
 * Formatea una fecha o fecha/hora en `DD/MM/AAAA HH:mm`, suprimiendo los segundos.
 */
export function formatDateTimeDisplay(value: Date | string | null | undefined): string {
  if (!value) return '';

  const d = value instanceof Date ? value : new Date(value);
  if (isNaN(d.getTime())) return '';

  const datePart = formatDateDisplay(d);
  const hours = String(d.getHours()).padStart(2, '0');
  const minutes = String(d.getMinutes()).padStart(2, '0');
  return `${datePart} ${hours}:${minutes}`;
}

/**
 * Formatea un horario a `HH:mm`, suprimiendo estrictamente los segundos.
 * Admite cadenas TimeOnly ("18:00:00", "09:30"), cadenas completas ISO o Date.
 */
export function formatTimeDisplay(value: Date | string | null | undefined): string {
  if (!value) return '';

  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!trimmed) return '';

    // Si es formato HH:mm[:ss]
    const timeMatch = /^(\d{1,2}):(\d{2})(?::\d{2})?/.exec(trimmed);
    if (timeMatch) {
      const hours = String(parseInt(timeMatch[1], 10)).padStart(2, '0');
      const minutes = timeMatch[2];
      return `${hours}:${minutes}`;
    }

    const d = new Date(trimmed);
    if (!isNaN(d.getTime())) {
      return formatTimeDisplay(d);
    }
    return '';
  }

  if (value instanceof Date) {
    if (isNaN(value.getTime())) return '';
    const hours = String(value.getHours()).padStart(2, '0');
    const minutes = String(value.getMinutes()).padStart(2, '0');
    return `${hours}:${minutes}`;
  }

  return '';
}

/**
 * Convierte de ISO "yyyy-MM-dd" a "DD/MM/AAAA".
 */
export function toDisplayDateString(isoDate: string | null | undefined): string {
  return formatDateDisplay(isoDate);
}

/**
 * Parsea una entrada de usuario en formato DD/MM/AAAA (o DD-MM-AAAA o DDMMYYYY)
 * validando que sea una fecha real válida y retornando "yyyy-MM-dd", o null si es inválida.
 */
export function parseDateInput(value: string | null | undefined): string | null {
  if (!value) return null;
  const trimmed = value.trim();
  if (!trimmed) return null;

  let day: number;
  let month: number;
  let year: number;

  const sepMatch = /^(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{4})$/.exec(trimmed);
  if (sepMatch) {
    day = parseInt(sepMatch[1], 10);
    month = parseInt(sepMatch[2], 10);
    year = parseInt(sepMatch[3], 10);
  } else if (/^\d{8}$/.test(trimmed)) {
    day = parseInt(trimmed.substring(0, 2), 10);
    month = parseInt(trimmed.substring(2, 4), 10);
    year = parseInt(trimmed.substring(4, 8), 10);
  } else {
    return null;
  }

  if (!isValidDateParts(day, month, year)) {
    return null;
  }

  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}
