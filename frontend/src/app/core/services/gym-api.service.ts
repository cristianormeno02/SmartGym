import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  Activity,
  PublicActivity,
  Room,
  RecurringSchedule,
  ClassSession,
  MembershipPlan,
  Membership,
  Reservation,
  DashboardMetrics,
  MedicalCertificateStatus,
  NotificationItem,
  CreditMovement,
  Promotion,
  AuditLog,
  AdminUserOverview
} from '../models/gym.models';

@Injectable({
  providedIn: 'root'
})
export class GymApiService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5000/api';

  // --- Actividades y Salas ---
  getPublicActivities(age?: number): Observable<PublicActivity[]> {
    let params = new HttpParams();
    if (age !== undefined && age !== null) {
      params = params.set('age', age.toString());
    }
    return this.http.get<PublicActivity[]>(`${this.apiUrl}/public/activities`, { params });
  }

  getPublicActivity(code: string): Observable<PublicActivity> {
    return this.http.get<PublicActivity>(`${this.apiUrl}/public/activities/${encodeURIComponent(code)}`);
  }

  getActivities(search?: string, status?: number, age?: number): Observable<any> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (status !== undefined) params = params.set('status', status.toString());
    if (age !== undefined) params = params.set('age', age.toString());
    return this.http.get<any>(`${this.apiUrl}/activities`, { params });
  }

  getActivity(id: string): Observable<Activity> {
    return this.http.get<Activity>(`${this.apiUrl}/activities/${id}`);
  }

  getRooms(): Observable<Room[]> {
    return this.http.get<Room[]>(`${this.apiUrl}/rooms`);
  }

  // --- Horarios y Clases ---
  getSchedules(activityId?: string, instructorId?: string): Observable<RecurringSchedule[]> {
    let params = new HttpParams();
    if (activityId) params = params.set('activityId', activityId);
    if (instructorId) params = params.set('instructorId', instructorId);
    return this.http.get<RecurringSchedule[]>(`${this.apiUrl}/schedules`, { params });
  }

  getClassSessions(filters?: { fromDate?: string; toDate?: string; activityId?: string; instructorId?: string; status?: number }): Observable<ClassSession[]> {
    let params = new HttpParams();
    if (filters?.fromDate) params = params.set('fromDate', filters.fromDate);
    if (filters?.toDate) params = params.set('toDate', filters.toDate);
    if (filters?.activityId) params = params.set('activityId', filters.activityId);
    if (filters?.instructorId) params = params.set('instructorId', filters.instructorId);
    if (filters?.status !== undefined) params = params.set('status', filters.status.toString());
    return this.http.get<ClassSession[]>(`${this.apiUrl}/classsessions`, { params });
  }

  suspendClassSession(id: string, reason: string): Observable<ClassSession> {
    return this.http.post<ClassSession>(`${this.apiUrl}/classsessions/${id}/suspend`, { reason });
  }

  finishClassSession(id: string): Observable<ClassSession> {
    return this.http.post<ClassSession>(`${this.apiUrl}/classsessions/${id}/finish`, {});
  }

  substituteInstructor(id: string, substituteInstructorId: string): Observable<ClassSession> {
    return this.http.post<ClassSession>(`${this.apiUrl}/classsessions/${id}/substitute`, { substituteInstructorId });
  }

  // --- Planes y Membresías ---
  getMembershipPlans(onlyActive = true): Observable<MembershipPlan[]> {
    const params = new HttpParams().set('onlyActive', onlyActive);
    return this.http.get<MembershipPlan[]>(`${this.apiUrl}/membershipplans`, { params });
  }

  getStudentMemberships(studentId: string): Observable<Membership[]> {
    return this.http.get<Membership[]>(`${this.apiUrl}/memberships/student/${studentId}`);
  }

  getActiveMembership(studentId: string): Observable<Membership> {
    return this.http.get<Membership>(`${this.apiUrl}/memberships/student/${studentId}/active`);
  }

  // --- Reservas y Asistencia ---
  createReservation(classSessionId: string, studentId: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.apiUrl}/reservations`, { classSessionId, studentId });
  }

  cancelReservation(reservationId: string, reason: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.apiUrl}/reservations/${reservationId}/cancel`, { reason });
  }

  getStudentReservations(studentId: string): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.apiUrl}/reservations/student/${studentId}`);
  }

  getSessionReservations(classSessionId: string): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.apiUrl}/reservations/session/${classSessionId}`);
  }

  getSessionAttendanceSummary(classSessionId: string): Observable<any> {
    return this.http.get(`${this.apiUrl}/reservations/session/${classSessionId}/attendance-summary`);
  }

  recordAttendance(reservationId: string, source: number = 0): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.apiUrl}/reservations/attendance`, { reservationId, source });
  }

  recordNoShow(reservationId: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.apiUrl}/reservations/${reservationId}/no-show`, {});
  }

  // --- Créditos Ledger ---
  getStudentCreditMovements(studentId: string): Observable<CreditMovement[]> {
    return this.http.get<CreditMovement[]>(`${this.apiUrl}/credits/student/${studentId}`);
  }

  issueCompensatoryCredit(request: { membershipId: string; amount: number; reason: string }): Observable<CreditMovement> {
    return this.http.post<CreditMovement>(`${this.apiUrl}/credits/compensatory`, request);
  }

  issueSingleClassTicket(request: { studentId: string; classSessionId: string; price: number; paymentMethod: number }): Observable<any> {
    return this.http.post(`${this.apiUrl}/credits/single-ticket`, request);
  }

  // --- Certificados Médicos ---
  getMedicalCertificateStatus(personId: string): Observable<MedicalCertificateStatus> {
    return this.http.get<MedicalCertificateStatus>(`${this.apiUrl}/medicalcertificates/status/${personId}`);
  }

  uploadMedicalCertificate(personId: string, file: File, expirationDate: string): Observable<MedicalCertificateStatus> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('expirationDate', expirationDate);
    return this.http.post<MedicalCertificateStatus>(`${this.apiUrl}/medicalcertificates/upload/${personId}`, formData);
  }

  // --- Notificaciones ---
  getMyNotifications(onlyUnread = false): Observable<NotificationItem[]> {
    const params = new HttpParams().set('onlyUnread', onlyUnread);
    return this.http.get<NotificationItem[]>(`${this.apiUrl}/notifications/my`, { params });
  }

  markNotificationAsRead(id: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/notifications/${id}/read`, {});
  }

  // --- Promociones ---
  getPromotions(onlyActive = true): Observable<Promotion[]> {
    const params = new HttpParams().set('onlyActive', onlyActive);
    return this.http.get<Promotion[]>(`${this.apiUrl}/promotions`, { params });
  }

  // --- Dashboard y Administración ---
  getDashboardMetrics(): Observable<DashboardMetrics> {
    return this.http.get<DashboardMetrics>(`${this.apiUrl}/dashboard/metrics`);
  }

  getAuditLogs(count = 50): Observable<AuditLog[]> {
    const params = new HttpParams().set('count', count.toString());
    return this.http.get<AuditLog[]>(`${this.apiUrl}/dashboard/audit-logs`, { params });
  }

  getAdminUsers(): Observable<AdminUserOverview[]> {
    return this.http.get<AdminUserOverview[]>(`${this.apiUrl}/users/admin-overview`);
  }
}
