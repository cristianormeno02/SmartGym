import { Pipe, PipeTransform } from '@angular/core';
import {
  formatDateDisplay,
  formatDateTimeDisplay,
  formatTimeDisplay
} from '../../core/utils/date-format';

export type AppDatePipeMode = 'date' | 'time' | 'timeOnly';

@Pipe({
  name: 'appDate',
  standalone: true,
  pure: true
})
export class AppDatePipe implements PipeTransform {
  transform(
    value: Date | string | number | null | undefined,
    mode: AppDatePipeMode = 'date'
  ): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    const normalizedValue = typeof value === 'number' ? new Date(value) : value;

    switch (mode) {
      case 'time':
        return formatDateTimeDisplay(normalizedValue);
      case 'timeOnly':
        return formatTimeDisplay(normalizedValue);
      case 'date':
      default:
        return formatDateDisplay(normalizedValue);
    }
  }
}
