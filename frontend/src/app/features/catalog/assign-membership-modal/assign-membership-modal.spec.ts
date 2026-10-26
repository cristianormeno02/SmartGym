import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AssignMembershipModalComponent } from './assign-membership-modal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of, throwError } from 'rxjs';
import { MembershipDetail, MembershipPlan, MembershipStatus } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('AssignMembershipModalComponent', () => {
  let component: AssignMembershipModalComponent;
  let fixture: ComponentFixture<AssignMembershipModalComponent>;
  let gymApiSpy: any;

  const mockPlans: MembershipPlan[] = [
    {
      id: 'plan-1',
      name: 'Pase Libre Full',
      description: 'Acceso ilimitado a todas las actividades',
      durationDays: 30,
      price: 35000,
      credits: 0,
      isUnlimited: true,
      appliesToAllActivities: true,
      allowedActivityIds: [],
      allowedActivityNames: [],
      isActive: true
    },
    {
      id: 'plan-2',
      name: 'Pack 8 Clases Musculación',
      description: 'Ocho clases en el mes',
      durationDays: 30,
      price: 20000,
      credits: 8,
      isUnlimited: false,
      appliesToAllActivities: false,
      allowedActivityIds: ['act-1'],
      allowedActivityNames: ['Musculación'],
      isActive: true
    }
  ];

  const mockCreatedMembership: MembershipDetail = {
    id: 'mem-1',
    studentId: 'student-1',
    studentName: 'Martín López',
    membershipPlanId: 'plan-1',
    membershipPlanName: 'Pase Libre Full',
    startDate: '2026-10-25',
    endDate: '2026-11-23',
    totalCredits: 0,
    availableCredits: 0,
    isUnlimited: true,
    status: MembershipStatus.Active,
    isValidToday: true,
    allowedActivityIds: [],
    payments: [],
    suspensions: [],
    statusHistory: []
  };

  beforeEach(async () => {
    gymApiSpy = {
      getMembershipPlans: vi.fn().mockReturnValue(of(mockPlans)),
      assignMembership: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [AssignMembershipModalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AssignMembershipModalComponent);
    component = fixture.componentInstance;
    component.studentId = 'student-1';
    component.studentName = 'Martín López';
    fixture.detectChanges();
  });

  it('should load active membership plans on init and initialize startDate to today', () => {
    expect(component).toBeTruthy();
    expect(gymApiSpy.getMembershipPlans).toHaveBeenCalledWith(true);
    expect(component.plans().length).toBe(2);
    expect(component.startDate()).toBe(component.todayIso);

    const dateInput = fixture.nativeElement.querySelector('input[type="date"]') as HTMLInputElement;
    expect(dateInput).toBeTruthy();
    expect(dateInput.min).toBe(component.todayIso);
  });

  it('should reject past startDate as invalid', () => {
    component.selectedPlanId.set('plan-1');
    component.startDate.set('2020-01-01');
    fixture.detectChanges();

    expect(component.isFormValid()).toBe(false);
  });

  it('should display commercial preview with formatted price, projected end date, and credits/unlimited info when plan is selected', () => {
    component.selectedPlanId.set('plan-1');
    component.startDate.set('2026-10-25');
    fixture.detectChanges();

    expect(component.selectedPlan()?.name).toBe('Pase Libre Full');
    expect(component.projectedEndDate()).toBe('2026-11-23');

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('$35.000,00');
    expect(text).toContain('30 días');
    expect(text).toContain('Pase Libre Ilimitado');
    expect(text).toContain('Todas las actividades');

    // Switch to restricted plan with credits
    component.selectedPlanId.set('plan-2');
    fixture.detectChanges();

    const text2 = fixture.nativeElement.textContent as string;
    expect(text2).toContain('$20.000,00');
    expect(text2).toContain('8 créditos');
    expect(text2).toContain('Musculación');
  });

  it('should submit assignMembership request and emit assigned event on success', () => {
    gymApiSpy.assignMembership.mockReturnValue(of(mockCreatedMembership));
    const assignedSpy = vi.spyOn(component.assigned, 'emit');

    component.selectedPlanId.set('plan-1');
    component.startDate.set(component.todayIso);
    fixture.detectChanges();

    component.onSubmit();

    expect(gymApiSpy.assignMembership).toHaveBeenCalledWith({
      studentId: 'student-1',
      membershipPlanId: 'plan-1',
      startDate: component.todayIso
    });
    expect(assignedSpy).toHaveBeenCalledWith(mockCreatedMembership);
    expect(component.isSubmitting()).toBe(false);
  });

  it('should display error message and re-enable button when assignMembership fails', () => {
    gymApiSpy.assignMembership.mockReturnValue(
      throwError(() => ({ error: { message: 'La persona especificada no posee el rol de Alumno activo.' } }))
    );

    component.selectedPlanId.set('plan-1');
    component.startDate.set(component.todayIso);
    fixture.detectChanges();

    component.onSubmit();
    fixture.detectChanges();

    expect(component.isSubmitting()).toBe(false);
    expect(component.errorMessage()).toContain('La persona especificada no posee el rol de Alumno activo.');
    const errorBanner = fixture.nativeElement.querySelector('[data-testid="assign-error-msg"]');
    expect(errorBanner?.textContent).toContain('La persona especificada no posee el rol de Alumno activo.');
  });

  it('should emit cancelled when cancel or close button is clicked', () => {
    const cancelSpy = vi.spyOn(component.cancelled, 'emit');

    const cancelBtn = fixture.nativeElement.querySelector('[data-testid="btn-cancel-assign"]') as HTMLButtonElement;
    cancelBtn.click();

    expect(cancelSpy).toHaveBeenCalled();
  });
});
