import { describe, it, expect } from 'vitest';
import { MoneyAmountPipe } from './money-amount.pipe';

describe('MoneyAmountPipe', () => {
  const pipe = new MoneyAmountPipe();

  it('transforms numbers into formatted monetary string with 2 decimals, comma decimal and dot thousands', () => {
    expect(pipe.transform(35000)).toBe('35.000,00');
    expect(pipe.transform(15000.5)).toBe('15.000,50');
    expect(pipe.transform(1250000)).toBe('1.250.000,00');
    expect(pipe.transform(0)).toBe('0,00');
  });

  it('returns default "0,00" for null or undefined values', () => {
    expect(pipe.transform(null)).toBe('0,00');
    expect(pipe.transform(undefined)).toBe('0,00');
  });
});
