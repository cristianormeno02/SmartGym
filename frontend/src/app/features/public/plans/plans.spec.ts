import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlansComponent } from './plans';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { MembershipPlan } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('PlansComponent (Public with staff controls)', () => {
  let component: PlansComponent;
  let fixture: ComponentFixture<PlansComponent>;
  let gymApiSpy: any;
  let authServiceSpy: any;

  const mockPlans: MembershipPlan[] = [
    {
      id: 'p1',
      name: 'Plan Activo',
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
      id: 'p2',
      name: 'Plan Inactivo',
      durationDays: 60,
      price: 20000,
      credits: 10,
      isUnlimited: false,
      appliesToAllActivities: false,
      allowedActivityIds: ['act-1'],
      allowedActivityNames: ['Spinning'],
      isActive: false
    }
  ];

  beforeEach(() => {
    gymApiSpy = {
      getMembershipPlans: vi.fn().mockReturnValue(of(mockPlans)),
      updateMembershipPlan: vi.fn(),
      getActivities: vi.fn().mockReturnValue(of({ items: [] }))
    };

    authServiceSpy = {
      isStaff: vi.fn().mockReturnValue(false)
    };
  });

  const setupComponent = async (isStaffUser: boolean) => {
    authServiceSpy.isStaff.mockReturnValue(isStaffUser);

    await TestBed.configureTestingModule({
      imports: [PlansComponent],
      providers: [
        provideRouter([]),
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PlansComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  it('for Student or anonymous: calls getMembershipPlans(true) and hides staff controls', async () => {
    await setupComponent(false);

    expect(gymApiSpy.getMembershipPlans).toHaveBeenCalledWith(true);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-new-plan')).toBeNull();
    expect(compiled.querySelector('.btn-edit-plan')).toBeNull();
    expect(compiled.querySelector('.btn-toggle-plan')).toBeNull();
  });

  it('for Staff: calls getMembershipPlans(false) and shows + Nuevo Plan button and per-plan actions', async () => {
    await setupComponent(true);

    expect(gymApiSpy.getMembershipPlans).toHaveBeenCalledWith(false);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-new-plan')).not.toBeNull();
    expect(compiled.querySelector('.btn-edit-plan')).not.toBeNull();
    expect(compiled.querySelector('.btn-toggle-plan')).not.toBeNull();
  });

  it('for Staff: togglePlanStatus calls updateMembershipPlan and reloads plans', async () => {
    await setupComponent(true);

    const updated = { ...mockPlans[0], isActive: false };
    gymApiSpy.updateMembershipPlan.mockReturnValue(of(updated));
    gymApiSpy.getMembershipPlans.mockClear();
    gymApiSpy.getMembershipPlans.mockReturnValue(of([updated, mockPlans[1]]));

    component.togglePlanStatus(mockPlans[0]);

    expect(gymApiSpy.updateMembershipPlan).toHaveBeenCalledWith('p1', expect.objectContaining({
      isActive: false
    }));
    expect(gymApiSpy.getMembershipPlans).toHaveBeenCalledWith(false);
  });
});
