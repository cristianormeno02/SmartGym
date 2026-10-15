import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UserManagementService } from './user-management.service';
import {
  UserAccountDto,
  ProvisionUserAccountRequest,
  UpdateUserRolesRequest,
  UpdateUserStatusRequest,
  AdminResetPasswordRequest
} from '../models/user-management.models';
import { environment } from '../../../environments/environment';

describe('UserManagementService', () => {
  let service: UserManagementService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiUrl}/users`;
  const personId = '3fa85f64-5717-4562-b3fc-2c963f66afa6';

  const mockAccount: UserAccountDto = {
    hasAccount: true,
    userId: '11111111-2222-3333-4444-555555555555',
    username: 'test@smartgym.com',
    isActive: true,
    lastLoginUtc: '2026-10-14T12:00:00Z',
    roles: ['Administrator', 'Secretary']
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        UserManagementService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(UserManagementService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getAccount should send GET to /api/users/{personId}/account', () => {
    let result: UserAccountDto | undefined;

    service.getAccount(personId).subscribe((res) => {
      result = res;
    });

    const req = httpMock.expectOne(`${baseUrl}/${personId}/account`);
    expect(req.request.method).toBe('GET');
    req.flush(mockAccount);

    expect(result).toEqual(mockAccount);
  });

  it('provision should send POST to /api/users/{personId}/account with payload', () => {
    const payload: ProvisionUserAccountRequest = {
      password: 'Password123!',
      roleIds: [2, 3]
    };
    let result: UserAccountDto | undefined;

    service.provision(personId, payload).subscribe((res) => {
      result = res;
    });

    const req = httpMock.expectOne(`${baseUrl}/${personId}/account`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(mockAccount);

    expect(result).toEqual(mockAccount);
  });

  it('updateRoles should send PUT to /api/users/{personId}/roles with payload', () => {
    const payload: UpdateUserRolesRequest = {
      roleIds: [1, 2]
    };
    let result: UserAccountDto | undefined;

    service.updateRoles(personId, payload).subscribe((res) => {
      result = res;
    });

    const req = httpMock.expectOne(`${baseUrl}/${personId}/roles`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush(mockAccount);

    expect(result).toEqual(mockAccount);
  });

  it('updateStatus should send PATCH to /api/users/{personId}/status with payload', () => {
    const payload: UpdateUserStatusRequest = {
      isActive: false
    };
    let result: UserAccountDto | undefined;

    service.updateStatus(personId, payload).subscribe((res) => {
      result = res;
    });

    const req = httpMock.expectOne(`${baseUrl}/${personId}/status`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual(payload);
    req.flush({ ...mockAccount, isActive: false });

    expect(result?.isActive).toBe(false);
  });

  it('resetPassword should send POST to /api/users/{personId}/reset-password with payload', () => {
    const payload: AdminResetPasswordRequest = {
      newPassword: 'NewPassword123!'
    };
    let completed = false;

    service.resetPassword(personId, payload).subscribe({
      next: () => {
        completed = true;
      }
    });

    const req = httpMock.expectOne(`${baseUrl}/${personId}/reset-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });
});
