import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SchedulesComponent } from './schedules';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { PeopleService } from '../../../core/services/people.service';
import { of } from 'rxjs';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('SchedulesComponent (Public with staff controls)', () => {
  let component: SchedulesComponent;
  let fixture: ComponentFixture<SchedulesComponent>;
  let gymApiSpy: any;
  let authServiceSpy: any;
  let peopleServiceSpy: any;

  const mockSchedules: RecurringSchedule[] = [
    {
      id: 'sched-1',
      activityId: 'act-1',
      activityName: 'Spinning',
      activityColorHex: '#EF4444',
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
    },
    {
      id: 'sched-2',
      activityId: 'act-2',
      activityName: 'Yoga',
      activityColorHex: '#10B981',
      roomId: 'room-2',
      roomName: 'Sala 2',
      instructorId: 'inst-2',
      instructorName: 'Carlos Ruiz',
      dayOfWeek: 2,
      startTime: '18:00:00',
      endTime: '19:00:00',
      validFrom: '2026-03-01',
      maxCapacity: 15,
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

    peopleServiceSpy = {
      search: vi.fn().mockReturnValue(of({ items: [] }))
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
        { provide: PeopleService, useValue: peopleServiceSpy },
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
    expect(compiled.querySelector('.btn-batch-schedule')).toBeNull();

    component.viewMode.set('cards');
    fixture.detectChanges();
    expect(compiled.querySelector('.btn-edit-schedule')).toBeNull();
    expect(compiled.querySelector('.btn-delete-schedule')).toBeNull();
  });

  it('for Staff: displays + Nuevo Horario button, Carga Masiva button, and per-schedule edit/delete buttons in cards view', async () => {
    await setupComponent(true);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-new-schedule')).not.toBeNull();
    expect(compiled.querySelector('.btn-batch-schedule')).not.toBeNull();

    component.viewMode.set('cards');
    fixture.detectChanges();
    expect(compiled.querySelector('.btn-edit-schedule')).not.toBeNull();
    expect(compiled.querySelector('.btn-delete-schedule')).not.toBeNull();
  });

  it('should toggle between calendar and cards view without new API requests', async () => {
    await setupComponent(false);
    expect(component.viewMode()).toBe('calendar');

    gymApiSpy.getSchedules.mockClear();

    component.viewMode.set('cards');
    fixture.detectChanges();
    expect(gymApiSpy.getSchedules).not.toHaveBeenCalled();

    component.viewMode.set('calendar');
    fixture.detectChanges();
    expect(gymApiSpy.getSchedules).not.toHaveBeenCalled();
  });

  it('should filter schedules by selected activities and restore all, exposing activity colorHex', async () => {
    await setupComponent(false);
    expect(component.isAllActivitiesSelected()).toBe(true);
    expect(component.filteredSchedules().length).toBe(2);
    expect(component.uniqueActivities()).toEqual([
      { id: 'act-1', name: 'Spinning', colorHex: '#EF4444' },
      { id: 'act-2', name: 'Yoga', colorHex: '#10B981' }
    ]);

    component.toggleActivityFilter('act-1');
    expect(component.isAllActivitiesSelected()).toBe(false);
    expect(component.filteredSchedules().map(s => s.id)).toEqual(['sched-1']);

    component.selectAllActivities();
    expect(component.isAllActivitiesSelected()).toBe(true);
    expect(component.filteredSchedules().length).toBe(2);
  });

  it('should filter schedules by selected rooms and combine with activity filter', async () => {
    await setupComponent(false);
    expect(component.isAllRoomsSelected()).toBe(true);
    expect(component.uniqueRooms()).toEqual([
      { id: 'room-1', name: 'Sala 1' },
      { id: 'room-2', name: 'Sala 2' }
    ]);

    component.toggleRoomFilter('room-1');
    expect(component.isAllRoomsSelected()).toBe(false);
    expect(component.isRoomSelected('room-1')).toBe(true);
    expect(component.filteredSchedules().map(s => s.id)).toEqual(['sched-1']);

    // Deselecting the last room returns to all rooms
    component.toggleRoomFilter('room-1');
    expect(component.isAllRoomsSelected()).toBe(true);
    expect(component.filteredSchedules().length).toBe(2);
  });

  it('should open detail modal when a schedule is selected from the calendar', async () => {
    await setupComponent(false);

    component.onScheduleSelectedFromCalendar(mockSchedules[0]);
    expect(component.selectedDetailSchedule()).toEqual(mockSchedules[0]);

    fixture.detectChanges();
    const detailModal = fixture.nativeElement.querySelector('app-schedule-detail-modal');
    expect(detailModal).not.toBeNull();
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

  it('for Staff: clicking batch opens batch modal and reloads schedules on save', async () => {
    await setupComponent(true);

    component.openBatchModal();
    expect(component.isBatchModalOpen()).toBe(true);

    gymApiSpy.getSchedules.mockClear();
    gymApiSpy.getSchedules.mockReturnValue(of(mockSchedules));

    component.onBatchSchedulesSaved([mockSchedules[0]]);
    expect(component.isBatchModalOpen()).toBe(false);
    expect(gymApiSpy.getSchedules).toHaveBeenCalled();
  });
});
