import { Pipe, PipeTransform } from '@angular/core';
import { formatMonetaryAmount } from '../../core/utils/monetary-format';

@Pipe({
  name: 'moneyAmount',
  standalone: true,
  pure: true
})
export class MoneyAmountPipe implements PipeTransform {
  transform(value: number | string | null | undefined, defaultEmpty = '0,00'): string {
    const formatted = formatMonetaryAmount(value);
    return formatted !== '' ? formatted : defaultEmpty;
  }
}
