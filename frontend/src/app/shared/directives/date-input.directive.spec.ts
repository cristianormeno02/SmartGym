import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { describe, it, expect, beforeEach } from 'vitest';
import { DateInputDirective } from './date-input.directive';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, DateInputDirective],
  template: `
    <form [formGroup]="form">
      <input type="text" formControlName="dateField" appDateInput />
    </form>
  `
})
class TestHostComponent {
  form = new FormGroup({
    dateField: new FormControl<string | null>(null)
  });
}

describe('DateInputDirective', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let component: TestHostComponent;
  let inputEl: HTMLInputElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    inputEl = fixture.nativeElement.querySelector('input');
  });

  it('sets default attributes: placeholder, inputmode, maxlength', () => {
    expect(inputEl.placeholder).toBe('DD/MM/AAAA');
    expect(inputEl.getAttribute('inputmode')).toBe('numeric');
    expect(inputEl.maxLength).toBe(10);
  });

  it('formats model ISO value yyyy-MM-dd to DD/MM/AAAA in the input on writeValue', () => {
    component.form.controls.dateField.setValue('2026-10-25');
    fixture.detectChanges();

    expect(inputEl.value).toBe('25/10/2026');
  });

  it('clears input when model is set to null', () => {
    component.form.controls.dateField.setValue('2026-10-25');
    fixture.detectChanges();
    expect(inputEl.value).toBe('25/10/2026');

    component.form.controls.dateField.setValue(null);
    fixture.detectChanges();
    expect(inputEl.value).toBe('');
  });

  it('automatically inserts slashes when typing digits', () => {
    inputEl.value = '2510';
    inputEl.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(inputEl.value).toBe('25/10/');
  });

  it('emits ISO value yyyy-MM-dd when a full valid DD/MM/AAAA date is typed', () => {
    inputEl.value = '25/10/2026';
    inputEl.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(component.form.controls.dateField.value).toBe('2026-10-25');
    expect(component.form.controls.dateField.valid).toBe(true);
  });

  it('marks error invalidDate when an invalid calendar date is entered', () => {
    inputEl.value = '31/02/2026';
    inputEl.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(component.form.controls.dateField.value).toBeNull();
    expect(component.form.controls.dateField.errors?.['invalidDate']).toBe(true);
  });

  it('marks error invalidDate on blur if input is incomplete', () => {
    inputEl.value = '25/10';
    inputEl.dispatchEvent(new Event('input'));
    inputEl.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(component.form.controls.dateField.value).toBeNull();
    expect(component.form.controls.dateField.errors?.['invalidDate']).toBe(true);
  });
});
