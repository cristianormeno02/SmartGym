import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { StudentPortalComponent } from './student-portal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationsService } from '../../../core/services/notifications.service';
import { of } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ReservableClassSession, ReservationStatus } from '../../../core/models/gym.models';

describe('StudentPortalComponent', () => {
  let component: StudentPortalComponent;
  let fixture: ComponentFixture<StudentPortalComponent>;
  let gymApiSpy: any;
  let authServiceSpy: any;
  let notificationsSpy: any;

  const mockSessions: ReservableClassSession[] = [
    {
      classSessionId: 'cs-1',
      activityId: 'act-1',
      activityName: 'Spinning',
      roomId: 'room-1',
      roomName: 'Sala Indoor',
      effectiveInstructorId: 'inst-1',
      instructorName: 'Prof. Laura',
      classDate: '2026-10-26',
      startTime: '08:30:00',
      endTime: '09:30:00',
      startsAtUtc: '2026-10-26T11:30:00Z',
      endsAtUtc: '2026-10-26T12:30:00Z',
      capacity: 20,
      reservedCount: 15,
      availableSpots: 5,
      waitlistCount: 0,
      isWithinBookingWindow: true,
      hasActiveReservation: false,
      canReserve: true,
      canJoinWaitlist: false,
      ineligibilityReasons: []
    },
    {
      classSessionId: 'cs-2',
      activityId: 'act-2',
      activityName: 'Funcional',
      roomId: 'room-2',
      roomName: 'Sala Outdoor',
      effectiveInstructorId: 'inst-2',
      instructorName: 'Prof. Juan',
      classDate: '2026-10-26',
      startTime: '15:00:00',
      endTime: '16:00:00',
      startsAtUtc: '2026-10-26T18:00:00Z',
      endsAtUtc: '2026-10-26T19:00:00Z',
      capacity: 15,
      reservedCount: 15,
      availableSpots: 0,
      waitlistCount: 2,
      isWithinBookingWindow: true,
      hasActiveReservation: false,
      canReserve: false,
      canJoinWaitlist: true,
      ineligibilityReasons: []
    },
    {
      classSessionId: 'cs-3',
      activityId: 'act-1',
      activityName: 'Spinning',
      roomId: 'room-1',
      roomName: 'Sala Indoor',
      effectiveInstructorId: 'inst-1',
      instructorName: 'Prof. Laura',
      classDate: '2026-10-27',
      startTime: '19:30:00',
      endTime: '20:30:00',
      startsAtUtc: '2026-10-27T22:30:00Z',
      endsAtUtc: '2026-10-27T23:30:00Z',
      capacity: 20,
      reservedCount: 8,
      availableSpots: 12,
      waitlistCount: 0,
      isWithinBookingWindow: true,
      hasActiveReservation: false,
      canReserve: true,
      canJoinWaitlist: false,
      ineligibilityReasons: []
    }
  ];

  beforeEach(async () => {
    gymApiSpy = {
      getActiveMembership: vi.fn().mockReturnValue(of(null)),
      getCurrentMemberships: vi.fn().mockReturnValue(of([])),
      getMyUpcomingReservations: vi.fn().mockReturnValue(of([])),
      getMedicalCertificateStatus: vi.fn().mockReturnValue(of({ isValid: true })),
      getMyNotifications: vi.fn().mockReturnValue(of([])),
      getStudentCreditMovements: vi.fn().mockReturnValue(of([])),
      getAvailableClasses: vi.fn().mockReturnValue(of(mockSessions)),
      createReservation: vi.fn().mockReturnValue(of({ status: ReservationStatus.Confirmed }))
    };

    authServiceSpy = {
      currentUser: signal({
        userId: 'u-1',
        personId: 'p-1',
        fullName: 'Juan Alumno',
        email: 'alumno@gym.com'
      })
    };

    notificationsSpy = {};

    await TestBed.configureTestingModule({
      imports: [StudentPortalComponent],
      providers: [
        provideRouter([]),
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationsService, useValue: notificationsSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(StudentPortalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads available classes and derives available activities and rooms', () => {
    expect(component.availableClasses().length).toBe(3);
    expect(component.filteredAvailableClasses().length).toBe(3);
    expect(component.availableActivities().length).toBe(2);
    expect(component.availableRooms().length).toBe(2);
  });

  it('filters by activity correctly', () => {
    component.filterActivityId.set('act-1');
    fixture.detectChanges();

    expect(component.filteredAvailableClasses().length).toBe(2);
    expect(component.filteredAvailableClasses().every(c => c.activityId === 'act-1')).toBe(true);
  });

  it('filters by time slot (morning < 12:00, afternoon 12:00-17:59, night >= 18:00)', () => {
    component.filterTimeSlot.set('MORNING');
    fixture.detectChanges();
    expect(component.filteredAvailableClasses().length).toBe(1);
    expect(component.filteredAvailableClasses()[0].classSessionId).toBe('cs-1');

    component.filterTimeSlot.set('AFTERNOON');
    fixture.detectChanges();
    expect(component.filteredAvailableClasses().length).toBe(1);
    expect(component.filteredAvailableClasses()[0].classSessionId).toBe('cs-2');

    component.filterTimeSlot.set('NIGHT');
    fixture.detectChanges();
    expect(component.filteredAvailableClasses().length).toBe(1);
    expect(component.filteredAvailableClasses()[0].classSessionId).toBe('cs-3');
  });

  it('filters by only available spots (availableSpots > 0)', () => {
    component.filterOnlyAvailableSpots.set(true);
    fixture.detectChanges();

    expect(component.filteredAvailableClasses().length).toBe(2);
    expect(component.filteredAvailableClasses().every(c => c.availableSpots > 0)).toBe(true);
  });

  it('filters by date correctly', () => {
    component.filterDate.set('2026-10-27');
    fixture.detectChanges();

    expect(component.filteredAvailableClasses().length).toBe(1);
    expect(component.filteredAvailableClasses()[0].classSessionId).toBe('cs-3');
  });

  it('filters by search text across activity and instructor', () => {
    component.filterSearchText.set('juan');
    fixture.detectChanges();

    expect(component.filteredAvailableClasses().length).toBe(1);
    expect(component.filteredAvailableClasses()[0].classSessionId).toBe('cs-2');

    component.filterSearchText.set('spinning');
    fixture.detectChanges();
    expect(component.filteredAvailableClasses().length).toBe(2);
  });

  it('resets all filters when resetFilters() is called', () => {
    component.filterActivityId.set('act-1');
    component.filterTimeSlot.set('MORNING');
    component.filterOnlyAvailableSpots.set(true);
    component.filterSearchText.set('Laura');
    fixture.detectChanges();

    expect(component.hasActiveFilters()).toBe(true);

    component.resetFilters();
    fixture.detectChanges();

    expect(component.hasActiveFilters()).toBe(false);
    expect(component.filteredAvailableClasses().length).toBe(3);
  });

  it('displays dates in DD/MM/AAAA and times in HH:mm in rendered template', () => {
    const textContent = fixture.nativeElement.textContent;
    expect(textContent).toContain('26/10/2026');
    expect(textContent).toContain('08:30');
    expect(textContent).not.toContain('08:30:00');
  });
});
