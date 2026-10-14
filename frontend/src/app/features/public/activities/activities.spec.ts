import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivitiesComponent } from './activities';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { of, throwError } from 'rxjs';
import { provideRouter } from '@angular/router';
import { ActivityStatus, PagedResult } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ActivitiesComponent', () => {
  let component: ActivitiesComponent;
  let fixture: ComponentFixture<ActivitiesComponent>;
  let gymApiSpy: {
    getPublicActivities: ReturnType<typeof vi.fn>;
    getActivities: ReturnType<typeof vi.fn>;
    getActivity: ReturnType<typeof vi.fn>;
    changeActivityStatus: ReturnType<typeof vi.fn>;
  };
  let authServiceSpy: {
    isStaff: ReturnType<typeof vi.fn>;
    userRoles: ReturnType<typeof vi.fn>;
  };

  const mockPublic = [
    { code: 'ACT1', name: 'Actividad 1', description: 'Desc 1', galleryImageUrls: [] }
  ];

  const mockAdminActivities = [
    {
      id: 'a1',
      code: 'ACT1',
      name: 'Actividad 1',
      status: ActivityStatus.Active,
      isActive: true,
      galleryImageUrls: []
    }
  ];

  const pagedResult: PagedResult<any> = {
    items: mockAdminActivities,
    pageNumber: 1,
    pageSize: 10,
    totalCount: 1,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false
  };

  beforeEach(async () => {
    gymApiSpy = {
      getPublicActivities: vi.fn().mockReturnValue(of(mockPublic as any)),
      getActivities: vi.fn().mockReturnValue(of(pagedResult as any)),
      getActivity: vi.fn(),
      changeActivityStatus: vi.fn()
    };

    authServiceSpy = {
      isStaff: vi.fn().mockReturnValue(false),
      userRoles: vi.fn().mockReturnValue(['Student'])
    };

    await TestBed.configureTestingModule({
      imports: [ActivitiesComponent],
      providers: [
        provideRouter([]),
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy }
      ]
    }).compileComponents();
  });

  it('for student/anonymous, should call getPublicActivities and hide staff actions', () => {
    fixture = TestBed.createComponent(ActivitiesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(gymApiSpy.getPublicActivities).toHaveBeenCalled();
    expect(gymApiSpy.getActivities).not.toHaveBeenCalled();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-new-activity')).toBeNull();
  });

  it('for staff, should call getActivities and show + Nueva Actividad button', () => {
    authServiceSpy.isStaff.mockReturnValue(true);

    fixture = TestBed.createComponent(ActivitiesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(gymApiSpy.getActivities).toHaveBeenCalled();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-new-activity')).not.toBeNull();
  });

  it('for staff, should render status actions on each activity and reload after a change', () => {
    authServiceSpy.isStaff.mockReturnValue(true);
    fixture = TestBed.createComponent(ActivitiesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('app-activity-status-actions').length).toBe(1);

    gymApiSpy.getActivities.mockClear();
    component.onStatusChanged(mockAdminActivities[0] as any);
    expect(gymApiSpy.getActivities).toHaveBeenCalled();
  });

  it('for anonymous, should not render status actions', () => {
    fixture = TestBed.createComponent(ActivitiesComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-activity-status-actions')).toBeNull();
  });

  it('for staff, should not open the edit modal when the detail cannot be loaded', () => {
    authServiceSpy.isStaff.mockReturnValue(true);
    gymApiSpy.getActivity.mockReturnValue(throwError(() => ({ status: 500, error: { message: 'Fallo' } })));
    fixture = TestBed.createComponent(ActivitiesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.openEditModal(mockAdminActivities[0] as any);

    expect(component.isModalOpen()).toBe(false);
    expect(component.errorMessage()).toContain('Fallo');
  });

  it('for staff, the status filter should include archived activities', () => {
    authServiceSpy.isStaff.mockReturnValue(true);
    fixture = TestBed.createComponent(ActivitiesComponent);
    fixture.detectChanges();

    const options = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('select option')).map(o => o.textContent!.trim());
    expect(options).toContain('Archivadas');
  });
});
