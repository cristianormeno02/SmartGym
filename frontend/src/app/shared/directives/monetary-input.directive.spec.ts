import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { describe, it, expect, beforeEach } from 'vitest';
import { MonetaryInputDirective } from './monetary-input.directive';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, MonetaryInputDirective],
  template: `<input type="text" inputmode="decimal" appMonetaryInput [formControl]="priceControl" />`
})
class TestHostComponent {
  priceControl = new FormControl<number | null>(null);
}

describe('MonetaryInputDirective', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let host: TestHostComponent;
  let inputEl: HTMLInputElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    inputEl = fixture.nativeElement.querySelector('input') as HTMLInputElement;
  });

  it('displays formatted value with 2 decimals, comma decimal and dot thousands when FormControl is set programmatically', () => {
    host.priceControl.setValue(35000);
    fixture.detectChanges();
    expect(inputEl.value).toBe('35.000,00');

    host.priceControl.setValue(1234.5);
    fixture.detectChanges();
    expect(inputEl.value).toBe('1.234,50');
  });

  it('formats thousands with dots while typing integer digits and updates FormControl with numeric value', () => {
    inputEl.value = '35000';
    inputEl.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(inputEl.value).toBe('35.000');
    expect(host.priceControl.value).toBe(35000);
  });

  it('allows typing decimal part with comma up to 2 decimals and updates FormControl with decimal number', () => {
    inputEl.value = '35000,5';
    inputEl.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(inputEl.value).toBe('35.000,5');
    expect(host.priceControl.value).toBe(35000.5);

    inputEl.value = '1250,759';
    inputEl.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(inputEl.value).toBe('1.250,75');
    expect(host.priceControl.value).toBe(1250.75);
  });

  it('completes 2 decimal places on blur and marks control as touched', () => {
    inputEl.value = '35000';
    inputEl.dispatchEvent(new Event('input'));
    expect(host.priceControl.touched).toBe(false);

    inputEl.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(inputEl.value).toBe('35.000,00');
    expect(host.priceControl.value).toBe(35000);
    expect(host.priceControl.touched).toBe(true);

    inputEl.value = '35000,5';
    inputEl.dispatchEvent(new Event('input'));
    inputEl.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(inputEl.value).toBe('35.000,50');
    expect(host.priceControl.value).toBe(35000.5);
  });

  it('sets FormControl to null when input is cleared', () => {
    host.priceControl.setValue(35000);
    fixture.detectChanges();
    expect(inputEl.value).toBe('35.000,00');

    inputEl.value = '';
    inputEl.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(inputEl.value).toBe('');
    expect(host.priceControl.value).toBeNull();
  });
});
