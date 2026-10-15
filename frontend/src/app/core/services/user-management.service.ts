import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  UserAccountDto,
  ProvisionUserAccountRequest,
  UpdateUserRolesRequest,
  UpdateUserStatusRequest,
  AdminResetPasswordRequest
} from '../models/user-management.models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class UserManagementService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/users`;

  getAccount(personId: string): Observable<UserAccountDto> {
    return this.http.get<UserAccountDto>(`${this.baseUrl}/${personId}/account`);
  }

  provision(personId: string, request: ProvisionUserAccountRequest): Observable<UserAccountDto> {
    return this.http.post<UserAccountDto>(`${this.baseUrl}/${personId}/account`, request);
  }

  updateRoles(personId: string, request: UpdateUserRolesRequest): Observable<UserAccountDto> {
    return this.http.put<UserAccountDto>(`${this.baseUrl}/${personId}/roles`, request);
  }

  updateStatus(personId: string, request: UpdateUserStatusRequest): Observable<UserAccountDto> {
    return this.http.patch<UserAccountDto>(`${this.baseUrl}/${personId}/status`, request);
  }

  resetPassword(personId: string, request: AdminResetPasswordRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${personId}/reset-password`, request);
  }
}
