import { describe, it, expect } from 'vitest';
import { formatMonetaryAmount, parseMonetaryAmount } from './monetary-format';

describe('monetary-format', () => {
  describe('formatMonetaryAmount', () => {
    it('formats integer amounts with 2 decimals, comma decimal separator and dot thousands separator', () => {
      expect(formatMonetaryAmount(35000)).toBe('35.000,00');
      expect(formatMonetaryAmount(20000)).toBe('20.000,00');
      expect(formatMonetaryAmount(500)).toBe('500,00');
    });

    it('formats decimal amounts with 1 or 2 decimals accurately', () => {
      expect(formatMonetaryAmount(1234.5)).toBe('1.234,50');
      expect(formatMonetaryAmount(1234.56)).toBe('1.234,56');
      expect(formatMonetaryAmount(0.05)).toBe('0,05');
    });

    it('rounds to 2 decimal places', () => {
      expect(formatMonetaryAmount(10.556)).toBe('10,56');
      expect(formatMonetaryAmount(10.554)).toBe('10,55');
    });

    it('formats zero and millions properly', () => {
      expect(formatMonetaryAmount(0)).toBe('0,00');
      expect(formatMonetaryAmount(1250000)).toBe('1.250.000,00');
    });

    it('formats negative amounts properly', () => {
      expect(formatMonetaryAmount(-1234.5)).toBe('-1.234,50');
    });

    it('returns empty string for null, undefined, empty string or NaN', () => {
      expect(formatMonetaryAmount(null)).toBe('');
      expect(formatMonetaryAmount(undefined)).toBe('');
      expect(formatMonetaryAmount('')).toBe('');
      expect(formatMonetaryAmount(NaN)).toBe('');
    });
  });

  describe('parseMonetaryAmount', () => {
    it('parses formatted strings with dot thousands and comma decimals into numbers', () => {
      expect(parseMonetaryAmount('35.000,00')).toBe(35000);
      expect(parseMonetaryAmount('1.234,50')).toBe(1234.5);
      expect(parseMonetaryAmount('1.250.000,75')).toBe(1250000.75);
      expect(parseMonetaryAmount('0,00')).toBe(0);
    });

    it('parses strings with only integer and thousands separators', () => {
      expect(parseMonetaryAmount('35.000')).toBe(35000);
      expect(parseMonetaryAmount('35000')).toBe(35000);
      expect(parseMonetaryAmount('35000,5')).toBe(35000.5);
    });

    it('returns finite numbers directly rounded to 2 decimals', () => {
      expect(parseMonetaryAmount(35000)).toBe(35000);
      expect(parseMonetaryAmount(1234.567)).toBe(1234.57);
    });

    it('returns null for empty, null, undefined or invalid strings', () => {
      expect(parseMonetaryAmount(null)).toBeNull();
      expect(parseMonetaryAmount(undefined)).toBeNull();
      expect(parseMonetaryAmount('')).toBeNull();
      expect(parseMonetaryAmount('   ')).toBeNull();
      expect(parseMonetaryAmount('abc')).toBeNull();
    });
  });
});
