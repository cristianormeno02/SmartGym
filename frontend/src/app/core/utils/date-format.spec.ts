import { describe, it, expect } from 'vitest';
import {
  formatDateDisplay,
  formatDateTimeDisplay,
  formatTimeDisplay,
  parseDateInput,
  toDisplayDateString,
  isValidDateParts
} from './date-format';

describe('date-format utilities', () => {
  describe('isValidDateParts', () => {
    it('validates common dates correctly', () => {
      expect(isValidDateParts(25, 10, 2026)).toBe(true);
      expect(isValidDateParts(1, 1, 2026)).toBe(true);
      expect(isValidDateParts(31, 12, 2026)).toBe(true);
    });

    it('validates leap years for February', () => {
      expect(isValidDateParts(29, 2, 2024)).toBe(true); // bisiesto
      expect(isValidDateParts(29, 2, 2026)).toBe(false); // no bisiesto
      expect(isValidDateParts(28, 2, 2026)).toBe(true);
    });

    it('rejects invalid days and months', () => {
      expect(isValidDateParts(0, 5, 2026)).toBe(false);
      expect(isValidDateParts(32, 5, 2026)).toBe(false);
      expect(isValidDateParts(31, 4, 2026)).toBe(false); // abril tiene 30
      expect(isValidDateParts(15, 0, 2026)).toBe(false);
      expect(isValidDateParts(15, 13, 2026)).toBe(false);
    });

    it('rejects out of range years', () => {
      expect(isValidDateParts(10, 5, 1899)).toBe(false);
      expect(isValidDateParts(10, 5, 2101)).toBe(false);
    });
  });

  describe('formatDateDisplay', () => {
    it('returns empty string for null, undefined or empty values', () => {
      expect(formatDateDisplay(null)).toBe('');
      expect(formatDateDisplay(undefined)).toBe('');
      expect(formatDateDisplay('')).toBe('');
    });

    it('formats pure ISO date yyyy-MM-dd without timezone shifting', () => {
      expect(formatDateDisplay('2026-10-25')).toBe('25/10/2026');
      expect(formatDateDisplay('2026-01-05')).toBe('05/01/2026');
    });

    it('formats full ISO string or Date object', () => {
      const d = new Date(2026, 9, 25); // 25 Oct 2026 local
      expect(formatDateDisplay(d)).toBe('25/10/2026');
    });
  });

  describe('formatDateTimeDisplay', () => {
    it('returns empty string for null, undefined or empty values', () => {
      expect(formatDateTimeDisplay(null)).toBe('');
      expect(formatDateTimeDisplay(undefined)).toBe('');
    });

    it('formats ISO datetime without seconds in HH:mm', () => {
      const d = new Date(2026, 9, 25, 18, 30, 45);
      expect(formatDateTimeDisplay(d)).toBe('25/10/2026 18:30');
    });
  });

  describe('formatTimeDisplay', () => {
    it('returns empty string for null, undefined or empty values', () => {
      expect(formatTimeDisplay(null)).toBe('');
      expect(formatTimeDisplay(undefined)).toBe('');
      expect(formatTimeDisplay('')).toBe('');
    });

    it('strips seconds from TimeOnly strings like 18:00:00 or 09:30:15', () => {
      expect(formatTimeDisplay('18:00:00')).toBe('18:00');
      expect(formatTimeDisplay('09:30:15')).toBe('09:30');
      expect(formatTimeDisplay('18:00')).toBe('18:00');
    });

    it('formats Date or ISO datetime to HH:mm without seconds', () => {
      const d = new Date(2026, 9, 25, 8, 5, 59);
      expect(formatTimeDisplay(d)).toBe('08:05');
    });
  });

  describe('parseDateInput', () => {
    it('returns null for empty or invalid values', () => {
      expect(parseDateInput(null)).toBeNull();
      expect(parseDateInput('')).toBeNull();
      expect(parseDateInput('25/10')).toBeNull();
      expect(parseDateInput('32/10/2026')).toBeNull();
      expect(parseDateInput('29/02/2026')).toBeNull();
    });

    it('parses valid DD/MM/AAAA or DD-MM-AAAA into yyyy-MM-dd', () => {
      expect(parseDateInput('25/10/2026')).toBe('2026-10-25');
      expect(parseDateInput('05/01/2026')).toBe('2026-01-05');
      expect(parseDateInput('5/1/2026')).toBe('2026-01-05');
      expect(parseDateInput('25-10-2026')).toBe('2026-10-25');
    });

    it('supports pure 8-digit numbers DDMMYYYY', () => {
      expect(parseDateInput('25102026')).toBe('2026-10-25');
    });
  });

  describe('toDisplayDateString', () => {
    it('converts yyyy-MM-dd to DD/MM/AAAA', () => {
      expect(toDisplayDateString('2026-10-25')).toBe('25/10/2026');
      expect(toDisplayDateString(null)).toBe('');
    });
  });
});
