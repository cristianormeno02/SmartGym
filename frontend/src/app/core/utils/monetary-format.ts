/**
 * Formatea un importe monetario con exactamente 2 decimales,
 * coma (`,`) como separador de decimales y punto (`.`) como separador de miles.
 * Ejemplo: 35000 -> "35.000,00"
 */
export function formatMonetaryAmount(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }

  const num = typeof value === 'number' ? value : parseMonetaryAmount(value);
  if (num === null || !Number.isFinite(num)) {
    return '';
  }

  const isNegative = num < 0;
  const absRounded = Math.round((Math.abs(num) + Number.EPSILON) * 100) / 100;
  const [integerPart, decimalPart] = absRounded.toFixed(2).split('.');
  const formattedInteger = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, '.');

  return `${isNegative ? '-' : ''}${formattedInteger},${decimalPart}`;
}

/**
 * Convierte un importe numérico o cadena con formato monetario (punto de miles y coma decimal)
 * en un número decimal redondeado a 2 decimales, o null si no es válido.
 */
export function parseMonetaryAmount(raw: string | number | null | undefined): number | null {
  if (raw === null || raw === undefined) {
    return null;
  }

  if (typeof raw === 'number') {
    if (!Number.isFinite(raw)) {
      return null;
    }
    return Math.round((raw + Number.EPSILON) * 100) / 100;
  }

  const trimmed = raw.trim();
  if (!trimmed) {
    return null;
  }

  // Elimina puntos de miles y reemplaza coma decimal por punto
  const normalized = trimmed.replace(/\./g, '').replace(',', '.');
  if (!/^-?\d+(\.\d+)?$/.test(normalized)) {
    return null;
  }

  const parsed = Number(normalized);
  if (!Number.isFinite(parsed)) {
    return null;
  }

  return Math.round((parsed + Number.EPSILON) * 100) / 100;
}
