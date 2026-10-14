import {
  WEEK_DAYS,
  getDayOfWeekName,
  getDayOfWeekFromName,
  formatTimeForApi,
  todayIsoDate,
  formatTimeForInput
} from './catalog-mappings';

describe('catalog-mappings utilities', () => {
  describe('DayOfWeek mappings', () => {
    it('WEEK_DAYS should define 7 days starting from Monday to Sunday (0=Domingo)', () => {
      expect(WEEK_DAYS.length).toBe(7);
      expect(WEEK_DAYS[0]).toEqual({ day: 1, name: 'Lunes' });
      expect(WEEK_DAYS[6]).toEqual({ day: 0, name: 'Domingo' });
    });

    it('getDayOfWeekName should return Spanish day name', () => {
      expect(getDayOfWeekName(0)).toBe('Domingo');
      expect(getDayOfWeekName(1)).toBe('Lunes');
      expect(getDayOfWeekName(5)).toBe('Viernes');
      expect(getDayOfWeekName(6)).toBe('Sábado');
      expect(getDayOfWeekName(99)).toBe('Desconocido');
    });

    it('getDayOfWeekFromName should return numeric DayOfWeek', () => {
      expect(getDayOfWeekFromName('Lunes')).toBe(1);
      expect(getDayOfWeekFromName('miércoles')).toBe(3);
      expect(getDayOfWeekFromName('miercoles')).toBe(3);
      expect(getDayOfWeekFromName('Domingo')).toBe(0);
      expect(getDayOfWeekFromName('Inexistente')).toBe(-1);
    });
  });

  describe('Time format conversions', () => {
    it('formatTimeForApi should append :00 to HH:mm string', () => {
      expect(formatTimeForApi('18:30')).toBe('18:30:00');
      expect(formatTimeForApi('08:00')).toBe('08:00:00');
      expect(formatTimeForApi('18:30:00')).toBe('18:30:00');
      expect(formatTimeForApi('')).toBe('');
    });

    it('formatTimeForInput should trim seconds to produce HH:mm', () => {
      expect(formatTimeForInput('18:30:00')).toBe('18:30');
      expect(formatTimeForInput('09:15:45')).toBe('09:15');
      expect(formatTimeForInput('18:30')).toBe('18:30');
      expect(formatTimeForInput('')).toBe('');
    });
  });

  it('todayIsoDate should format the local date as yyyy-MM-dd with zero padding', () => {
    expect(todayIsoDate(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
    expect(todayIsoDate(new Date(2026, 11, 31))).toBe('2026-12-31');
  });
});
