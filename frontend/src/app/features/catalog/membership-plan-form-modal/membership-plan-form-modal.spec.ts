import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MembershipPlanFormModalComponent } from './membership-plan-form-modal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of, throwError } from 'rxjs';
import { Activity, ActivityStatus, MembershipPlan } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('MembershipPlanFormModalComponent', () => {
  let component: MembershipPlanFormModalComponent;
  let fixture: ComponentFixture<MembershipPlanFormModalComponent>;
  let gymApiSpy: any;

  const mockActivities: Activity[] = [
    { id: 'act-1', code: 'SPINNING', name: 'Spinning Power', status: ActivityStatus.Active, isActive: true },
    { id: 'act-2', code: 'MUSC', name: 'Musculación', status: ActivityStatus.Active, isActive: true }
  ];

  const mockPlan: MembershipPlan = {
    id: 'plan-1',
    name: 'Pase Libre Full',
    description: 'Acceso a todo',
    durationDays: 30,
    price: 35000,
    credits: 0,
    isUnlimited: true,
    appliesToAllActivities: true,
    allowedActivityIds: [],
    allowedActivityNames: [],
    isActive: true
  };

  beforeEach(async () => {
    gymApiSpy = {
      getActivities: vi.fn().mockReturnValue(of({ items: mockActivities })),
      createMembershipPlan: vi.fn(),
      updateMembershipPlan: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [MembershipPlanFormModalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(MembershipPlanFormModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should initialize create form with invalid status', () => {
    expect(component).toBeTruthy();
    expect(component.isEditMode).toBe(false);
    expect(component.form.valid).toBe(false);
  });

  it('should validate name required, price > 0, durationDays >= 1', () => {
    const nameCtrl = component.form.get('name');
    nameCtrl?.setValue('');
    expect(nameCtrl?.hasError('required')).toBe(true);

    const priceCtrl = component.form.get('price');
    priceCtrl?.setValue(0);
    expect(priceCtrl?.hasError('min')).toBe(true);
    priceCtrl?.setValue(100);
    expect(priceCtrl?.valid).toBe(true);

    const durCtrl = component.form.get('durationDays');
    durCtrl?.setValue(0);
    expect(durCtrl?.hasError('min')).toBe(true);
    durCtrl?.setValue(30);
    expect(durCtrl?.valid).toBe(true);
  });

  it('should require credits >= 1 when isUnlimited is false, and reset credits to 0 when isUnlimited is true', () => {
    component.form.patchValue({
      name: 'Plan Créditos',
      price: 20000,
      durationDays: 30,
      isUnlimited: false,
      credits: 0,
      appliesToAllActivities: true
    });
    component.onUnlimitedChange();

    expect(component.form.hasError('creditsRequired')).toBe(true);

    component.form.patchValue({ credits: 10 });
    expect(component.form.hasError('creditsRequired')).toBe(false);

    component.form.patchValue({ isUnlimited: true });
    component.onUnlimitedChange();
    expect(component.form.get('credits')?.value).toBe(0);
    expect(component.form.hasError('creditsRequired')).toBe(false);
  });

  it('should require at least one activity when appliesToAllActivities is false, and empty array when true', () => {
    component.form.patchValue({
      name: 'Plan Específico',
      price: 15000,
      durationDays: 30,
      isUnlimited: true,
      appliesToAllActivities: false,
      allowedActivityIds: []
    });
    component.onAppliesToAllChange();

    expect(component.form.hasError('activitiesRequired')).toBe(true);

    component.toggleActivity('act-1');
    expect(component.form.hasError('activitiesRequired')).toBe(false);
    expect(component.selectedActivityIds()).toEqual(['act-1']);

    component.form.patchValue({ appliesToAllActivities: true });
    component.onAppliesToAllChange();
    expect(component.selectedActivityIds()).toEqual([]);
    expect(component.form.hasError('activitiesRequired')).toBe(false);
  });

  it('should submit create request on alta and emit saved', () => {
    gymApiSpy.createMembershipPlan.mockReturnValue(of(mockPlan));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.form.patchValue({
      name: 'Pase Libre Full',
      description: 'Acceso a todo',
      durationDays: 30,
      price: 35000,
      isUnlimited: true,
      credits: 0,
      appliesToAllActivities: true
    });

    component.onSubmit();

    expect(gymApiSpy.createMembershipPlan).toHaveBeenCalledWith({
      name: 'Pase Libre Full',
      description: 'Acceso a todo',
      durationDays: 30,
      price: 35000,
      credits: 0,
      isUnlimited: true,
      appliesToAllActivities: true,
      allowedActivityIds: []
    });
    expect(emitSpy).toHaveBeenCalledWith(mockPlan);
  });

  it('should populate form in edit mode and submit updateMembershipPlan', () => {
    const restrictedPlan: MembershipPlan = {
      ...mockPlan,
      id: 'plan-2',
      name: 'Bono 10 Musculación',
      isUnlimited: false,
      credits: 10,
      appliesToAllActivities: false,
      allowedActivityIds: ['act-2'],
      isActive: false
    };
    gymApiSpy.updateMembershipPlan.mockReturnValue(of(restrictedPlan));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.plan = restrictedPlan;
    component.ngOnChanges();

    expect(component.isEditMode).toBe(true);
    expect(component.selectedActivityIds()).toEqual(['act-2']);

    component.onSubmit();

    expect(gymApiSpy.updateMembershipPlan).toHaveBeenCalledWith('plan-2', expect.objectContaining({
      name: 'Bono 10 Musculación',
      credits: 10,
      isUnlimited: false,
      appliesToAllActivities: false,
      allowedActivityIds: ['act-2'],
      isActive: false
    }));
    expect(emitSpy).toHaveBeenCalledWith(restrictedPlan);
  });

  it('should display error message on API failure', () => {
    gymApiSpy.createMembershipPlan.mockReturnValue(throwError(() => ({ error: { message: 'Nombre ya existente.' } })));

    component.form.patchValue({
      name: 'Plan Duplicado',
      price: 100,
      durationDays: 30,
      isUnlimited: true,
      appliesToAllActivities: true
    });

    component.onSubmit();

    expect(component.errorMessage()).toBe('Nombre ya existente.');
  });
});
