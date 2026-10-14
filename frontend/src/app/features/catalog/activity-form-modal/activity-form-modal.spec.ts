import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivityFormModalComponent } from './activity-form-modal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of, throwError } from 'rxjs';
import { Activity, ActivityStatus } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ActivityFormModalComponent', () => {
  let component: ActivityFormModalComponent;
  let fixture: ComponentFixture<ActivityFormModalComponent>;
  let gymApiSpy: {
    createActivity: ReturnType<typeof vi.fn>;
    updateActivity: ReturnType<typeof vi.fn>;
    getActivity: ReturnType<typeof vi.fn>;
  };

  const mockActivity: Activity = {
    id: 'act-1',
    code: 'SPINNING',
    name: 'Spinning Power',
    shortDescription: 'Ciclismo indoor',
    description: 'Entrenamiento cardiovascular intenso',
    equipmentNotes: 'Toalla y zapatillas con calas',
    colorHex: '#E11D48',
    defaultCapacity: 20,
    minAge: 16,
    maxAge: 65,
    status: ActivityStatus.Active,
    version: 3,
    isActive: true
  };

  beforeEach(async () => {
    gymApiSpy = {
      createActivity: vi.fn(),
      updateActivity: vi.fn(),
      getActivity: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [ActivityFormModalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ActivityFormModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should initialize create form with disabled/invalid submit initially', () => {
    expect(component).toBeTruthy();
    expect(component.isEditMode).toBe(false);
    expect(component.form.valid).toBe(false);
  });

  it('should normalize code to uppercase on input and validate pattern', () => {
    const codeCtrl = component.form.get('code');
    codeCtrl?.setValue('  crossfit  ');
    component.onCodeInput();
    expect(codeCtrl?.value).toBe('CROSSFIT');
    expect(codeCtrl?.valid).toBe(true);

    codeCtrl?.setValue('ZU'); // too short (min 3)
    expect(codeCtrl?.hasError('minlength')).toBe(true);
  });

  it('should disable code field in edit mode', () => {
    component.activity = mockActivity;
    component.ngOnChanges();

    expect(component.isEditMode).toBe(true);
    expect(component.form.get('code')?.disabled).toBe(true);
    expect(component.form.get('code')?.value).toBe('SPINNING');
    expect(component.form.get('name')?.value).toBe('Spinning Power');
  });

  it('should validate hex color pattern #RRGGBB', () => {
    const colorCtrl = component.form.get('colorHex');
    colorCtrl?.setValue('invalid');
    expect(colorCtrl?.hasError('pattern')).toBe(true);

    colorCtrl?.setValue('#E11D48');
    expect(colorCtrl?.valid).toBe(true);
  });

  it('should validate minAge <= maxAge', () => {
    component.form.patchValue({
      minAge: 30,
      maxAge: 20
    });
    expect(component.form.hasError('ageRangeInvalid')).toBe(true);

    component.form.patchValue({
      minAge: 16,
      maxAge: 60
    });
    expect(component.form.hasError('ageRangeInvalid')).toBe(false);
  });

  it('should validate defaultCapacity >= 1 when provided', () => {
    const capCtrl = component.form.get('defaultCapacity');
    capCtrl?.setValue(0);
    expect(capCtrl?.hasError('min')).toBe(true);

    capCtrl?.setValue(25);
    expect(capCtrl?.valid).toBe(true);
  });

  it('should submit create request on alta and emit saved', () => {
    gymApiSpy.createActivity.mockReturnValue(of(mockActivity));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.form.patchValue({
      code: 'SPINNING',
      name: 'Spinning Power',
      colorHex: '#E11D48',
      defaultCapacity: 20
    });

    component.onSubmit();

    expect(gymApiSpy.createActivity).toHaveBeenCalled();
    expect(emitSpy).toHaveBeenCalledWith(mockActivity);
  });

  it('should submit updateActivity with version in edit mode', () => {
    const updated = { ...mockActivity, name: 'Spinning Extreme', version: 4 };
    gymApiSpy.updateActivity.mockReturnValue(of(updated));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.activity = mockActivity;
    component.ngOnChanges();
    component.form.patchValue({ name: 'Spinning Extreme' });

    component.onSubmit();

    expect(gymApiSpy.updateActivity).toHaveBeenCalledWith('act-1', expect.objectContaining({
      name: 'Spinning Extreme',
      version: 3
    }));
    expect(emitSpy).toHaveBeenCalledWith(updated);
  });

  it('should show the server message when create answers 409', () => {
    gymApiSpy.createActivity.mockReturnValue(throwError(() => ({
      status: 409,
      error: { message: "El código 'SPINNING' ya está en uso." }
    })));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.form.patchValue({ code: 'SPINNING', name: 'Spinning Power' });
    component.onSubmit();

    expect(component.errorMessage()).toBe("El código 'SPINNING' ya está en uso.");
    expect(emitSpy).not.toHaveBeenCalled();
  });

  it('should not submit an update when the activity has no version', () => {
    component.activity = { ...mockActivity, version: undefined };
    component.ngOnChanges();

    component.onSubmit();

    expect(gymApiSpy.updateActivity).not.toHaveBeenCalled();
    expect(component.errorMessage()).toBeTruthy();
  });

  it('should handle 409 concurrency error by reloading detail and keeping modal open', () => {
    const freshDetail: Activity = { ...mockActivity, name: 'Spinning Modificado Por Otro', version: 4 };
    gymApiSpy.updateActivity.mockReturnValue(throwError(() => ({ status: 409, error: { message: 'Versión desactualizada.' } })));
    gymApiSpy.getActivity.mockReturnValue(of(freshDetail));

    component.activity = mockActivity;
    component.ngOnChanges();

    component.onSubmit();

    expect(gymApiSpy.getActivity).toHaveBeenCalledWith('act-1');
    expect(component.errorMessage()).toContain('modificada por otro usuario');
    expect(component.activity?.version).toBe(4);
    expect(component.form.get('name')?.value).toBe('Spinning Modificado Por Otro');
  });
});
