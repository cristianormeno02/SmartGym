import { describe, it, expect } from 'vitest';
import { AppDatePipe } from './app-date.pipe';

describe('AppDatePipe', () => {
  const pipe = new AppDatePipe();

  it('transforms null, undefined and empty to empty string', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform('')).toBe('');
  });

  it('transforms pure ISO date yyyy-MM-dd to DD/MM/AAAA', () => {
    expect(pipe.transform('2026-10-25')).toBe('25/10/2026');
    expect(pipe.transform('2026-03-09')).toBe('09/03/2026');
  });

  it('transforms Date object to DD/MM/AAAA', () => {
    const d = new Date(2026, 9, 25);
    expect(pipe.transform(d)).toBe('25/10/2026');
  });

  it('transforms with mode "time" to DD/MM/AAAA HH:mm without seconds', () => {
    const d = new Date(2026, 9, 25, 14, 30, 45);
    expect(pipe.transform(d, 'time')).toBe('25/10/2026 14:30');
  });

  it('transforms with mode "timeOnly" to HH:mm without seconds', () => {
    expect(pipe.transform('18:00:00', 'timeOnly')).toBe('18:00');
    expect(pipe.transform('09:15:30', 'timeOnly')).toBe('09:15');
    const d = new Date(2026, 9, 25, 8, 5, 0);
    expect(pipe.transform(d, 'timeOnly')).toBe('08:05');
  });
});
