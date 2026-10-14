import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SchedulesComponent } from './schedules';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { of } from 'rxjs';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('SchedulesComponent (Public with staff controls)', () => {
  let component: SchedulesComponent;
  let fixture: ComponentFixture<SchedulesComponent>;
  let gymApiSpy: any;
  let authServiceSpy: any;

  const mockSchedules: RecurringSchedule[] = [
    {
      id: 'sched-1',
      activityId: 'act-1',
      activityName: 'Spinning',
      roomId: 'room-1',
      roomName: 'Sala 1',
      instructorId: 'inst-1',
      instructorName: 'Laura Gómez',
      dayOfWeek: 1,
      startTime: '10:00:00',
      endTime: '11:00:00',
      validFrom: '2026-03-01',
      maxCapacity: 20,
      isActive: true
    }
  ];

  beforeEach(() => {
    gymApiSpy = {
      getSchedules: vi.fn().mockReturnValue(of(mockSchedules)),
      deleteSchedule: vi.fn().mockReturnValue(of({ deleted: true })),
      getActivities: vi.fn().mockReturnValue(of({ items: [] })),
      getRooms: vi.fn().mockReturnValue(of([]))
    };

    authServiceSpy = {
      isStaff: vi.fn().mockReturnValue(false)
    };
  });

  const setupComponent = async (isStaffUser: boolean) => {
    authServiceSpy.isStaff.mockReturnValue(isStaffUser);

    await TestBed.configureTestingModule({
      imports: [SchedulesComponent],
      providers: [
        provideRouter([]),
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        {
          provide: ActivatedRoute,
          useValue: { queryParams: of({}) }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SchedulesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  it('for Student or anonymous: displays schedules without staff controls', async () => {
    await setupComponent(false);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-new-schedule')).toBeNull();
    expect(compiled.querySelector('.btn-edit-schedule')).toBeNull();
    expect(compiled.querySelector('.btn-delete-schedule')).toBeNull();
  });

  it('for Staff: displays + Nuevo Horario button and per-schedule edit/delete buttons', async () => {
    await setupComponent(true);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-new-schedule')).not.toBeNull();
    expect(compiled.querySelector('.btn-edit-schedule')).not.toBeNull();
    expect(compiled.querySelector('.btn-delete-schedule')).not.toBeNull();
  });

  it('for Staff: clicking delete opens confirmation warning and confirms removal', async () => {
    await setupComponent(true);

    component.requestDelete(mockSchedules[0]);
    fixture.detectChanges();

    expect(component.scheduleToDelete()).toEqual(mockSchedules[0]);

    component.confirmDelete();

    expect(gymApiSpy.deleteSchedule).toHaveBeenCalledWith('sched-1');
    expect(component.scheduleToDelete()).toBeNull();
  });
});
