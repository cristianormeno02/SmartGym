import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GymApiService } from './gym-api.service';
import { environment } from '../../../environments/environment';
import {
  ActivityStatus,
  CreateRoomRequest,
  UpdateRoomRequest,
  CreateActivityRequest,
  UpdateActivityRequest,
  ChangeActivityStatusRequest,
  CreateRecurringScheduleRequest,
  UpdateRecurringScheduleRequest,
  CreateMembershipPlanRequest,
  UpdateMembershipPlanRequest
} from '../models/gym.models';

describe('GymApiService - Catalog Management', () => {
  let service: GymApiService;
  let httpMock: HttpTestingController;
  const apiUrl = environment.apiUrl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        GymApiService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(GymApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('Rooms', () => {
    it('createRoom should send POST to /rooms with request body', () => {
      const request: CreateRoomRequest = {
        name: 'Sala Spinning',
        description: 'Piso 1',
        capacity: 25
      };

      service.createRoom(request).subscribe((res) => {
        expect(res.name).toBe('Sala Spinning');
      });

      const req = httpMock.expectOne(`${apiUrl}/rooms`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 'r1', ...request, isActive: true });
    });

    it('updateRoom should send PUT to /rooms/:id with request body', () => {
      const request: UpdateRoomRequest = {
        name: 'Sala Spinning Modificada',
        capacity: 30,
        isActive: true
      };

      service.updateRoom('r1', request).subscribe();

      const req = httpMock.expectOne(`${apiUrl}/rooms/r1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 'r1', ...request });
    });
  });

  describe('Activities', () => {
    it('createActivity should send POST to /activities with request body', () => {
      const request: CreateActivityRequest = {
        code: 'CROSSFIT',
        name: 'CrossFit WOD',
        description: 'Alta intensidad',
        colorHex: '#E11D48',
        defaultCapacity: 20,
        minAge: 16,
        maxAge: 65
      };

      service.createActivity(request).subscribe((res) => {
        expect(res.code).toBe('CROSSFIT');
      });

      const req = httpMock.expectOne(`${apiUrl}/activities`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 'a1', ...request, status: 1, isActive: true, version: 1 });
    });

    it('updateActivity should send PUT to /activities/:id with request body', () => {
      const request: UpdateActivityRequest = {
        name: 'CrossFit Avanzado',
        colorHex: '#E11D48',
        defaultCapacity: 25,
        version: 1
      };

      service.updateActivity('a1', request).subscribe();

      const req = httpMock.expectOne(`${apiUrl}/activities/a1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 'a1', code: 'CROSSFIT', ...request, status: 1, isActive: true, version: 2 });
    });

    it('changeActivityStatus should send PATCH to /activities/:id/status with request body', () => {
      const request: ChangeActivityStatusRequest = {
        status: ActivityStatus.Inactive,
        version: 1
      };

      service.changeActivityStatus('a1', request).subscribe((res) => {
        expect(res.changed).toBe(true);
      });

      const req = httpMock.expectOne(`${apiUrl}/activities/a1/status`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual(request);
      req.flush({ changed: true });
    });
  });

  describe('Schedules', () => {
    it('createSchedule should send POST to /schedules with request body', () => {
      const request: CreateRecurringScheduleRequest = {
        activityId: 'a1',
        roomId: 'r1',
        instructorId: 'i1',
        secretaryId: null,
        dayOfWeek: 1,
        startTime: '18:00',
        endTime: '19:00',
        validFrom: '2026-10-01',
        maxCapacity: 20
      };

      service.createSchedule(request).subscribe();

      const req = httpMock.expectOne(`${apiUrl}/schedules`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 's1', ...request, isActive: true });
    });

    it('updateSchedule should send PUT to /schedules/:id with request body', () => {
      const request: UpdateRecurringScheduleRequest = {
        roomId: 'r1',
        instructorId: 'i1',
        secretaryId: null,
        dayOfWeek: 1,
        startTime: '18:30',
        endTime: '19:30',
        validFrom: '2026-10-01',
        maxCapacity: 25,
        isActive: true
      };

      service.updateSchedule('s1', request).subscribe();

      const req = httpMock.expectOne(`${apiUrl}/schedules/s1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 's1', activityId: 'a1', ...request });
    });

    it('deleteSchedule should send DELETE to /schedules/:id', () => {
      service.deleteSchedule('s1').subscribe();

      const req = httpMock.expectOne(`${apiUrl}/schedules/s1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });
  });

  describe('MembershipPlans', () => {
    it('createMembershipPlan should send POST to /membershipplans with request body', () => {
      const request: CreateMembershipPlanRequest = {
        name: 'Pase Libre Full',
        description: 'Acceso total',
        durationDays: 30,
        price: 35000,
        credits: 0,
        isUnlimited: true,
        appliesToAllActivities: true,
        allowedActivityIds: []
      };

      service.createMembershipPlan(request).subscribe();

      const req = httpMock.expectOne(`${apiUrl}/membershipplans`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 'p1', ...request, isActive: true });
    });

    it('updateMembershipPlan should send PUT to /membershipplans/:id with request body', () => {
      const request: UpdateMembershipPlanRequest = {
        name: 'Pase Libre Full 2026',
        durationDays: 30,
        price: 40000,
        credits: 0,
        isUnlimited: true,
        appliesToAllActivities: true,
        allowedActivityIds: [],
        isActive: true
      };

      service.updateMembershipPlan('p1', request).subscribe();

      const req = httpMock.expectOne(`${apiUrl}/membershipplans/p1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ id: 'p1', ...request });
    });

    it('deleteMembershipPlan should send DELETE to /membershipplans/:id', () => {
      service.deleteMembershipPlan('p1').subscribe();

      const req = httpMock.expectOne(`${apiUrl}/membershipplans/p1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });
  });
});
