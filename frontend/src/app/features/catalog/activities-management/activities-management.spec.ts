import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivitiesManagementComponent } from './activities-management';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of, throwError } from 'rxjs';
import { Activity, ActivityStatus, PagedResult } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ActivitiesManagementComponent', () => {
  let component: ActivitiesManagementComponent;
  let fixture: ComponentFixture<ActivitiesManagementComponent>;
  let gymApiSpy: {
    getActivities: ReturnType<typeof vi.fn>;
    getActivity: ReturnType<typeof vi.fn>;
    changeActivityStatus: ReturnType<typeof vi.fn>;
  };

  const mockActivities: Activity[] = [
    {
      id: 'a1',
      code: 'SPINNING',
      name: 'Spinning Power',
      status: ActivityStatus.Active,
      colorHex: '#06B6D4',
      defaultCapacity: 20,
      version: 1,
      isActive: true
    },
    {
      id: 'a2',
      code: 'YOGA',
      name: 'Hatha Yoga',
      status: ActivityStatus.Inactive,
      colorHex: '#10B981',
      defaultCapacity: 15,
      version: 2,
      isActive: false
    }
  ];

  const pagedResult: PagedResult<Activity> = {
    items: mockActivities,
    pageNumber: 1,
    pageSize: 10,
    totalCount: 2,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false
  };

  beforeEach(async () => {
    gymApiSpy = {
      getActivities: vi.fn().mockReturnValue(of(pagedResult)),
      getActivity: vi.fn().mockReturnValue(of(mockActivities[0])),
      changeActivityStatus: vi.fn().mockReturnValue(of({ changed: true }))
    };

    await TestBed.configureTestingModule({
      imports: [ActivitiesManagementComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ActivitiesManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load paged activities on init', () => {
    expect(component).toBeTruthy();
    expect(component.activities().length).toBe(2);
    expect(gymApiSpy.getActivities).toHaveBeenCalled();
  });

  it('should filter by search query and status', () => {
    gymApiSpy.getActivities.mockClear();
    component.searchTerm = 'Spinning';
    component.selectedStatus = ActivityStatus.Active;
    component.applyFilters();

    expect(gymApiSpy.getActivities).toHaveBeenCalledWith('Spinning', ActivityStatus.Active, undefined, 1, 10);
  });

  it('should render the shared status actions for each activity', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('app-activity-status-actions').length).toBe(2);
  });

  it('should reload the list and show feedback after a status change', () => {
    gymApiSpy.getActivities.mockClear();

    component.onStatusChanged(mockActivities[0]);

    expect(gymApiSpy.getActivities).toHaveBeenCalled();
    expect(component.feedbackMessage()).toContain('Spinning Power');
  });

  it('should open the edit modal with the fresh detail', () => {
    gymApiSpy.getActivity.mockReturnValue(of({ ...mockActivities[0], version: 7 }));

    component.openEditModal(mockActivities[0]);

    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedActivity()?.version).toBe(7);
  });

  it('should not open the edit modal when the detail cannot be loaded', () => {
    gymApiSpy.getActivity.mockReturnValue(throwError(() => ({ status: 500, error: { message: 'Fallo' } })));

    component.openEditModal(mockActivities[0]);

    expect(component.isModalOpen()).toBe(false);
    expect(component.errorMessage()).toContain('Fallo');
  });
});
