import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlansManagementComponent } from './plans-management';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of } from 'rxjs';
import { MembershipPlan } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('PlansManagementComponent', () => {
  let component: PlansManagementComponent;
  let fixture: ComponentFixture<PlansManagementComponent>;
  let gymApiSpy: any;

  const mockPlans: MembershipPlan[] = [
    {
      id: 'p1',
      name: 'Plan Full',
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
      name: 'Plan 10 Clases',
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

  beforeEach(async () => {
    gymApiSpy = {
      getMembershipPlans: vi.fn().mockReturnValue(of(mockPlans)),
      updateMembershipPlan: vi.fn(),
      getActivities: vi.fn().mockReturnValue(of({ items: [] }))
    };

    await TestBed.configureTestingModule({
      imports: [PlansManagementComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PlansManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load all plans (including inactive) on init', () => {
    expect(component).toBeTruthy();
    expect(gymApiSpy.getMembershipPlans).toHaveBeenCalledWith(false);
    expect(component.plans().length).toBe(2);
  });

  it('should toggle plan status from active to inactive and reload', () => {
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

  it('should open modal for create and edit', () => {
    component.openCreateModal();
    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedPlan()).toBeNull();

    component.openEditModal(mockPlans[0]);
    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedPlan()?.id).toBe('p1');
  });
});
