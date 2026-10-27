import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { StaffPortalComponent } from './staff-portal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { of } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  AttendanceRevocationReason,
  AttendanceSource,
  ClassSession,
  DeskEligibility,
  DeskEligibleMembership,
  PaymentMethodType,
  Reservation,
  ReservationAccessType,
  ReservationOrigin,
  ReservationStatus,
  SingleClassPassOption
} from '../../../core/models/gym.models';

describe('StaffPortalComponent - Recepción, Mostrador y Asistencia', () => {
  let component: StaffPortalComponent;
  let fixture: ComponentFixture<StaffPortalComponent>;
  let gymApiSpy: any;
  let authServiceSpy: any;

  const mockSession: ClassSession = {
    id: 'session-1',
    activityId: 'act-1',
    activityName: 'Funcional Drop-in',
    roomId: 'room-1',
    roomName: 'Sala 1',
    instructorId: 'inst-1',
    instructorName: 'Prof. Carlos',
    date: '2026-10-27',
    startTime: '10:00:00',
    endTime: '11:00:00',
    status: 1, // Open / Abierta
    maxCapacity: 20,
    reservedCount: 5,
    attendedCount: 2,
    availableSpots: 15,
    requiresBooking: false
  };

  const mockStudent = {
    id: 'student-123',
    fullName: 'Juan Pérez',
    email: 'juan.perez@test.com',
    documentNumber: '35123456',
    isActive: true,
    roles: ['Student']
  };

  const mockEligibleMembership: DeskEligibleMembership = {
    membershipId: 'mem-1',
    planName: 'Pase 12 Clases',
    isUnlimited: false,
    usableCredits: 8,
    validUntil: '2026-12-31'
  };

  const mockPassOption: SingleClassPassOption = {
    planId: 'plan-single-1',
    planName: 'Pase Individual Funcional',
    price: 3500
  };

  beforeEach(async () => {
    gymApiSpy = {
      getClassSessions: vi.fn().mockReturnValue(of([mockSession])),
      getSessionReservations: vi.fn().mockReturnValue(of([])),
      getSessionWaitlist: vi.fn().mockReturnValue(of([])),
      searchPeople: vi.fn().mockReturnValue(of({ items: [mockStudent], totalCount: 1 })),
      getDeskEligibility: vi.fn().mockReturnValue(of({
        classSessionId: mockSession.id,
        studentId: mockStudent.id,
        studentFullName: mockStudent.fullName,
        studentDni: mockStudent.documentNumber,
        requiresBooking: false,
        isWithinDropInWindow: true,
        availableSpots: 15,
        hasActiveReservation: false,
        canCheckInWithMembership: true,
        canPurchaseSingleClassPass: true,
        canEnrollReservation: true,
        eligibleMemberships: [mockEligibleMembership],
        singleClassPassOptions: [mockPassOption],
        ineligibilityReasons: []
      } as DeskEligibility)),
      deskDropInCheckIn: vi.fn().mockReturnValue(of({
        reservation: {
          id: 'res-new-1',
          classSessionId: mockSession.id,
          activityName: mockSession.activityName,
          classDate: mockSession.date,
          startTime: mockSession.startTime,
          endTime: mockSession.endTime,
          roomName: mockSession.roomName,
          studentId: mockStudent.id,
          studentName: mockStudent.fullName,
          status: ReservationStatus.Attended,
          origin: ReservationOrigin.StaffDesk,
          accessType: ReservationAccessType.MembershipCredit,
          reservedAtUtc: '2026-10-27T13:00:00Z',
          attendedAtUtc: '2026-10-27T13:00:00Z',
          isDropIn: true,
          isLateCancellation: false,
          isPolicyOverride: false,
          affectedBySessionChange: false
        }
      })),
      recordAttendance: vi.fn().mockReturnValue(of({
        id: 'res-existing-1',
        status: ReservationStatus.Attended
      })),
      revokeAttendance: vi.fn().mockReturnValue(of({
        reservation: {
          id: 'res-attended-1',
          status: ReservationStatus.Cancelled
        },
        actionTaken: 'Cancelled',
        rightsRestored: true
      })),
      createReservation: vi.fn().mockReturnValue(of({
        id: 'res-created-1',
        studentName: mockStudent.fullName,
        status: ReservationStatus.Confirmed
      })),
      getReservations: vi.fn().mockReturnValue(of({ items: [], totalCount: 0 })),
      generateClassSessions: vi.fn().mockReturnValue(of({ created: 0 })),
      suspendClassSession: vi.fn().mockReturnValue(of(mockSession)),
      finishClassSession: vi.fn().mockReturnValue(of(mockSession)),
      issueCompensatoryCredit: vi.fn().mockReturnValue(of({}))
    };

    authServiceSpy = {
      currentUser: signal({ id: 'staff-1', fullName: 'Recepcionista Ana', roles: ['Secretary'] }),
      userRoles: signal(['Secretary']),
      isStaff: signal(true),
      isInstructor: signal(false),
      isAdmin: signal(false),
      isStudent: signal(false)
    };

    await TestBed.configureTestingModule({
      imports: [StaffPortalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(StaffPortalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('no debe contener ningún input que solicite GUID del alumno ni de membresía', () => {
    component.openCompensatoryModal();
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const deskGuidInput = compiled.querySelector('input[placeholder*="ID del alumno"]');
    const anyGuidInput = compiled.querySelector('input[placeholder*="Guid"]');
    expect(deskGuidInput).toBeNull();
    expect(anyGuidInput).toBeNull();
  });

  it('debe buscar alumnos con debounce de 300ms al ingresar texto en el buscador de recepción', () => {
    vi.useFakeTimers();
    try {
      component.selectSession(mockSession);
      fixture.detectChanges();

      component.onDeskStudentSearch('Pérez');
      expect(gymApiSpy.searchPeople).not.toHaveBeenCalled();

      vi.advanceTimersByTime(300);
      expect(gymApiSpy.searchPeople).toHaveBeenCalledWith(expect.objectContaining({
        role: 'Student',
        search: 'Pérez',
        pageSize: 10
      }));
    } finally {
      vi.useRealTimers();
    }
  });

  it('al seleccionar un alumno, debe cargar su elegibilidad para la clase', () => {
    component.selectSession(mockSession);
    component.selectDeskStudent(mockStudent as any);

    expect(gymApiSpy.getDeskEligibility).toHaveBeenCalledWith(mockSession.id, mockStudent.id);
    expect(component.selectedDeskStudent()?.id).toBe(mockStudent.id);
    expect(component.deskEligibility()?.studentId).toBe(mockStudent.id);
  });

  it('si el alumno tiene membresía con crédito, debe permitir dar presente directo (Drop-in)', () => {
    component.selectSession(mockSession);
    component.selectDeskStudent(mockStudent as any);
    fixture.detectChanges();

    component.confirmDeskDropIn(mockEligibleMembership.membershipId);

    expect(gymApiSpy.deskDropInCheckIn).toHaveBeenCalledWith(expect.objectContaining({
      classSessionId: mockSession.id,
      studentId: mockStudent.id,
      membershipId: mockEligibleMembership.membershipId
    }));
  });

  it('si el alumno no tiene membresía, debe permitir cobrar pase individual in situ y dar presente', () => {
    component.selectSession(mockSession);
    component.selectDeskStudent(mockStudent as any);
    fixture.detectChanges();

    component.confirmSingleClassDeskDropIn(mockPassOption.planId, PaymentMethodType.Cash);

    expect(gymApiSpy.deskDropInCheckIn).toHaveBeenCalledWith(expect.objectContaining({
      classSessionId: mockSession.id,
      studentId: mockStudent.id,
      singleClassPlanId: mockPassOption.planId,
      paymentMethod: PaymentMethodType.Cash
    }));
  });

  it('si el alumno ya tiene reserva confirmada, debe dar presente directamente', () => {
    const eligibilityWithBooking: DeskEligibility = {
      classSessionId: mockSession.id,
      studentId: mockStudent.id,
      studentFullName: mockStudent.fullName,
      studentDni: mockStudent.documentNumber,
      requiresBooking: true,
      isWithinDropInWindow: true,
      availableSpots: 15,
      hasActiveReservation: true,
      existingReservationStatus: ReservationStatus.Confirmed,
      existingReservationId: 'res-confirmed-99',
      canCheckInWithMembership: false,
      canPurchaseSingleClassPass: false,
      canEnrollReservation: false,
      eligibleMemberships: [],
      singleClassPassOptions: [],
      ineligibilityReasons: []
    };
    gymApiSpy.getDeskEligibility.mockReturnValue(of(eligibilityWithBooking));

    component.selectSession(mockSession);
    component.selectDeskStudent(mockStudent as any);
    fixture.detectChanges();

    component.checkInConfirmedDeskStudent();

    expect(gymApiSpy.recordAttendance).toHaveBeenCalledWith('res-confirmed-99', AttendanceSource.SecretaryDesk);
  });

  it('debe permitir anular la asistencia de un alumno presente en la nómina (Revoke Attendance)', () => {
    const attendedReservation: Reservation = {
      id: 'res-att-1',
      studentId: mockStudent.id,
      studentName: mockStudent.fullName,
      documentNumber: '35123456',
      classSessionId: mockSession.id,
      activityName: mockSession.activityName,
      roomName: mockSession.roomName,
      classDate: mockSession.date,
      startTime: mockSession.startTime,
      endTime: mockSession.endTime,
      status: ReservationStatus.Attended,
      origin: ReservationOrigin.StaffDesk,
      accessType: ReservationAccessType.MembershipCredit,
      reservedAtUtc: '2026-10-27T10:00:00Z',
      isDropIn: true,
      isLateCancellation: false,
      isPolicyOverride: false,
      affectedBySessionChange: false
    };

    component.selectSession(mockSession);
    component.openRevokeAttendanceModal(attendedReservation);

    expect(component.reservationToRevoke()).toEqual(attendedReservation);

    component.confirmRevokeAttendance(AttendanceRevocationReason.StaffError, 'Carga duplicada por error');

    expect(gymApiSpy.revokeAttendance).toHaveBeenCalledWith('res-att-1', expect.objectContaining({
      reason: AttendanceRevocationReason.StaffError,
      notes: 'Carga duplicada por error'
    }));
  });

  it('no debe permitir confirmar la anulación de asistencia si la observación está vacía', () => {
    const attendedReservation: Reservation = {
      id: 'res-att-2',
      studentId: mockStudent.id,
      studentName: mockStudent.fullName,
      documentNumber: '35123456',
      classSessionId: mockSession.id,
      activityName: mockSession.activityName,
      roomName: mockSession.roomName,
      classDate: mockSession.date,
      startTime: mockSession.startTime,
      endTime: mockSession.endTime,
      status: ReservationStatus.Attended,
      origin: ReservationOrigin.StaffDesk,
      accessType: ReservationAccessType.MembershipCredit,
      reservedAtUtc: '2026-10-27T10:00:00Z',
      isDropIn: true,
      isLateCancellation: false,
      isPolicyOverride: false,
      affectedBySessionChange: false
    };

    component.openRevokeAttendanceModal(attendedReservation);
    component.revokeNotes = '   ';
    component.confirmRevokeAttendance();

    expect(gymApiSpy.revokeAttendance).not.toHaveBeenCalled();
  });

  it('debe ocultar los controles de recepción de mostrador para usuarios que solo son Instructor', () => {
    authServiceSpy.isStaff.set(false);
    authServiceSpy.isInstructor.set(true);
    authServiceSpy.userRoles.set(['Instructor']);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const deskSection = compiled.querySelector('#desk-checkin-section');
    expect(deskSection).toBeNull();
  });
});
