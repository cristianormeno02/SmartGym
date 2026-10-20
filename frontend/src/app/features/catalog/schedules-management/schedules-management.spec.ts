import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SchedulesManagementComponent } from './schedules-management';
import { GymApiService } from '../../../core/services/gym-api.service';
import { PeopleService } from '../../../core/services/people.service';
import { of } from 'rxjs';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('SchedulesManagementComponent', () => {
  let component: SchedulesManagementComponent;
  let fixture: ComponentFixture<SchedulesManagementComponent>;
  let gymApiSpy: any;
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
      dayOfWeek: 1, // Lunes
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
      dayOfWeek: 1, // Lunes
      startTime: '18:00:00',
      endTime: '19:00:00',
      validFrom: '2026-03-01',
      maxCapacity: 15,
      isActive: false
    }
  ];

  beforeEach(async () => {
    gymApiSpy = {
      getSchedules: vi.fn().mockReturnValue(of(mockSchedules)),
      deleteSchedule: vi.fn().mockReturnValue(of({ deleted: true })),
      getActivities: vi.fn().mockReturnValue(of({ items: [] })),
      getRooms: vi.fn().mockReturnValue(of([]))
    };

    peopleServiceSpy = {
      search: vi.fn().mockReturnValue(of({ items: [] }))
    };

    await TestBed.configureTestingModule({
      imports: [SchedulesManagementComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: PeopleService, useValue: peopleServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(SchedulesManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load schedules on init', () => {
    expect(component).toBeTruthy();
    expect(gymApiSpy.getSchedules).toHaveBeenCalled();
    expect(component.schedules().length).toBe(2);
  });

  it('should display the schedules of the selected day in cards view', () => {
    component.viewMode.set('cards');
    component.selectedDay.set(1);
    expect(component.displayedSchedules().map(s => s.id)).toEqual(['sched-1', 'sched-2']);
    expect((component as any).showInactive).toBeUndefined();
    expect(fixture.nativeElement.textContent).not.toContain('Mostrar turnos inactivos');
  });

  it('should toggle view between calendar and cards without making a new API query', () => {
    expect(component.viewMode()).toBe('calendar');
    gymApiSpy.getSchedules.mockClear();

    component.viewMode.set('cards');
    fixture.detectChanges();
    expect(gymApiSpy.getSchedules).not.toHaveBeenCalled();

    component.viewMode.set('calendar');
    fixture.detectChanges();
    expect(gymApiSpy.getSchedules).not.toHaveBeenCalled();
  });

  it('should filter by one or multiple activities and reset to all, exposing activity colorHex', () => {
    expect(component.isAllActivitiesSelected()).toBe(true);
    expect(component.filteredSchedules().length).toBe(2);
    expect(component.uniqueActivities()).toEqual([
      { id: 'act-1', name: 'Spinning', colorHex: '#EF4444' },
      { id: 'act-2', name: 'Yoga', colorHex: '#10B981' }
    ]);

    // Filter by act-1
    component.toggleActivityFilter('act-1');
    expect(component.isAllActivitiesSelected()).toBe(false);
    expect(component.isActivitySelected('act-1')).toBe(true);
    expect(component.filteredSchedules().map(s => s.id)).toEqual(['sched-1']);

    // Toggle act-2 also (multi-activity)
    component.toggleActivityFilter('act-2');
    expect(component.filteredSchedules().length).toBe(2);

    // Reset back to all
    component.selectAllActivities();
    expect(component.isAllActivitiesSelected()).toBe(true);
    expect(component.filteredSchedules().length).toBe(2);
  });

  it('should filter by one or multiple rooms and combine with activity filter across calendar and cards views', () => {
    expect(component.isAllRoomsSelected()).toBe(true);
    expect(component.uniqueRooms()).toEqual([
      { id: 'room-1', name: 'Sala 1' },
      { id: 'room-2', name: 'Sala 2' }
    ]);

    // Filter by room-2
    component.toggleRoomFilter('room-2');
    expect(component.isAllRoomsSelected()).toBe(false);
    expect(component.isRoomSelected('room-2')).toBe(true);
    expect(component.filteredSchedules().map(s => s.id)).toEqual(['sched-2']);

    // In cards view on Monday, only sched-2 is displayed
    component.viewMode.set('cards');
    component.selectedDay.set(1);
    expect(component.displayedSchedules().map(s => s.id)).toEqual(['sched-2']);

    // Combine with activity act-1 (disjoint with room-2) -> 0 results
    component.toggleActivityFilter('act-1');
    expect(component.filteredSchedules().length).toBe(0);

    // Reset rooms to all -> sched-1 (which matches act-1) is visible
    component.selectAllRooms();
    expect(component.isAllRoomsSelected()).toBe(true);
    expect(component.filteredSchedules().map(s => s.id)).toEqual(['sched-1']);
  });

  it('should open schedule detail modal when clicking a schedule in calendar and allow edit/delete from detail', () => {
    component.onScheduleSelectedFromCalendar(mockSchedules[0]);
    expect(component.selectedDetailSchedule()).toEqual(mockSchedules[0]);

    // Test edit from detail
    component.onEditFromDetail(mockSchedules[0]);
    expect(component.selectedDetailSchedule()).toBeNull();
    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedSchedule()).toEqual(mockSchedules[0]);

    // Test delete from detail
    component.onScheduleSelectedFromCalendar(mockSchedules[0]);
    component.onDeleteFromDetail(mockSchedules[0]);
    expect(component.selectedDetailSchedule()).toBeNull();
    expect(component.scheduleToDelete()).toEqual(mockSchedules[0]);
  });

  it('should open modal for create and edit', () => {
    component.openCreateModal();
    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedSchedule()).toBeNull();

    component.openEditModal(mockSchedules[0]);
    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedSchedule()?.id).toBe('sched-1');
  });

  it('should prompt warning dialog on requestDelete and call deleteSchedule on confirm', () => {
    component.requestDelete(mockSchedules[0]);
    expect(component.scheduleToDelete()).toEqual(mockSchedules[0]);

    gymApiSpy.getSchedules.mockClear();
    gymApiSpy.getSchedules.mockReturnValue(of([mockSchedules[1]]));

    component.confirmDelete();

    expect(gymApiSpy.deleteSchedule).toHaveBeenCalledWith('sched-1');
    expect(component.scheduleToDelete()).toBeNull();
    expect(gymApiSpy.getSchedules).toHaveBeenCalled();
  });

  it('should cancel delete request without calling deleteSchedule', () => {
    component.requestDelete(mockSchedules[0]);
    component.cancelDelete();

    expect(component.scheduleToDelete()).toBeNull();
    expect(gymApiSpy.deleteSchedule).not.toHaveBeenCalled();
  });

  it('should open batch modal and reload schedules on batch saved', () => {
    component.openBatchModal();
    expect(component.isBatchModalOpen()).toBe(true);

    gymApiSpy.getSchedules.mockClear();
    gymApiSpy.getSchedules.mockReturnValue(of(mockSchedules));

    component.onBatchSchedulesSaved([mockSchedules[0]]);
    expect(component.isBatchModalOpen()).toBe(false);
    expect(component.feedbackMessage()).toContain('1 horarios');
    expect(gymApiSpy.getSchedules).toHaveBeenCalled();
  });
});
