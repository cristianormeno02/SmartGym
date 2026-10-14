import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import {
  Activity,
  PublicActivity,
  Room,
  RecurringSchedule,
  ClassSession,
  MembershipPlan,
  Membership,
  MembershipDetail,
  MembershipEligibility,
  MembershipCreditsSummary,
  Reservation,
  ReservationDetail,
  ReservableClassSession,
  WaitlistEntry,
  CreateReservationRequest,
  CancelReservationRequest,
  ReservationFilter,
  PagedResult,
  AttendanceSource,
  DashboardMetrics,
  MedicalCertificateStatus,
  NotificationItem,
  CreditMovement,
  Promotion,
  AuditLog,
  AdminUserOverview,
  Payment,
  PaymentRefund,
  PaymentFilter,
  MembershipBalance,
  RegisterPaymentRequest,
  ApplyPaymentRequest,
  CreatePaymentRefundRequest,
  PaymentDiscrepancy,
  PaymentDiscrepancyStatus,
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
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class GymApiService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = environment.apiUrl;

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

  getActivities(search?: string, status?: number, age?: number, page: number = 1, pageSize: number = 10): Observable<PagedResult<Activity>> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (status !== undefined) params = params.set('status', status.toString());
    if (age !== undefined) params = params.set('age', age.toString());
    if (page) params = params.set('page', page.toString());
    if (pageSize) params = params.set('pageSize', pageSize.toString());
    return this.http.get<PagedResult<Activity>>(`${this.apiUrl}/activities`, { params });
  }

  getActivity(id: string): Observable<Activity> {
    return this.http.get<Activity>(`${this.apiUrl}/activities/${id}`);
  }

  createActivity(request: CreateActivityRequest): Observable<Activity> {
    return this.http.post<Activity>(`${this.apiUrl}/activities`, request);
  }

  updateActivity(id: string, request: UpdateActivityRequest): Observable<Activity> {
    return this.http.put<Activity>(`${this.apiUrl}/activities/${id}`, request);
  }

  changeActivityStatus(id: string, request: ChangeActivityStatusRequest): Observable<{ changed: boolean }> {
    return this.http.patch<{ changed: boolean }>(`${this.apiUrl}/activities/${id}/status`, request);
  }

  getRooms(): Observable<Room[]> {
    return this.http.get<Room[]>(`${this.apiUrl}/rooms`);
  }

  createRoom(request: CreateRoomRequest): Observable<Room> {
    return this.http.post<Room>(`${this.apiUrl}/rooms`, request);
  }

  updateRoom(id: string, request: UpdateRoomRequest): Observable<Room> {
    return this.http.put<Room>(`${this.apiUrl}/rooms/${id}`, request);
  }

  // --- Horarios y Clases ---
  getSchedules(activityId?: string, instructorId?: string): Observable<RecurringSchedule[]> {
    let params = new HttpParams();
    if (activityId) params = params.set('activityId', activityId);
    if (instructorId) params = params.set('instructorId', instructorId);
    return this.http.get<RecurringSchedule[]>(`${this.apiUrl}/schedules`, { params });
  }

  createSchedule(request: CreateRecurringScheduleRequest): Observable<RecurringSchedule> {
    return this.http.post<RecurringSchedule>(`${this.apiUrl}/schedules`, request);
  }

  updateSchedule(id: string, request: UpdateRecurringScheduleRequest): Observable<RecurringSchedule> {
    return this.http.put<RecurringSchedule>(`${this.apiUrl}/schedules/${id}`, request);
  }

  deleteSchedule(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/schedules/${id}`);
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

  createMembershipPlan(request: CreateMembershipPlanRequest): Observable<MembershipPlan> {
    return this.http.post<MembershipPlan>(`${this.apiUrl}/membershipplans`, request);
  }

  updateMembershipPlan(id: string, request: UpdateMembershipPlanRequest): Observable<MembershipPlan> {
    return this.http.put<MembershipPlan>(`${this.apiUrl}/membershipplans/${id}`, request);
  }

  deleteMembershipPlan(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/membershipplans/${id}`);
  }

  getStudentMemberships(studentId: string): Observable<Membership[]> {
    return this.http.get<Membership[]>(`${this.apiUrl}/memberships/student/${studentId}`);
  }

  getCurrentMemberships(studentId: string): Observable<Membership[]> {
    return this.http.get<Membership[]>(`${this.apiUrl}/memberships/student/${studentId}/current`);
  }

  getActiveMembership(studentId: string): Observable<Membership> {
    return this.http.get<Membership>(`${this.apiUrl}/memberships/student/${studentId}/active`);
  }

  getMembershipById(membershipId: string): Observable<MembershipDetail> {
    return this.http.get<MembershipDetail>(`${this.apiUrl}/memberships/${membershipId}`);
  }

  getMembershipEligibility(
    membershipId: string,
    paramsObj?: { date?: string; activityId?: string; classSessionId?: string }
  ): Observable<MembershipEligibility> {
    let params = new HttpParams();
    if (paramsObj?.date) params = params.set('date', paramsObj.date);
    if (paramsObj?.activityId) params = params.set('activityId', paramsObj.activityId);
    if (paramsObj?.classSessionId) params = params.set('classSessionId', paramsObj.classSessionId);
    return this.http.get<MembershipEligibility>(`${this.apiUrl}/memberships/${membershipId}/eligibility`, { params });
  }

  getMembershipCredits(membershipId: string): Observable<MembershipCreditsSummary> {
    return this.http.get<MembershipCreditsSummary>(`${this.apiUrl}/memberships/${membershipId}/credits`);
  }

  // --- Reservas y lista de espera ---

  /**
   * Crea la reserva (o inscribe en lista de espera si la clase está completa y allowWaitlist no es false).
   * La clave de idempotencia identifica el intento: reutilizarla en un reintento devuelve la misma reserva.
   */
  createReservation(request: CreateReservationRequest, idempotencyKey: string = crypto.randomUUID()): Observable<Reservation> {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });
    return this.http.post<Reservation>(`${this.apiUrl}/reservations`, request, { headers });
  }

  cancelReservation(reservationId: string, request: CancelReservationRequest = {}): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.apiUrl}/reservations/${reservationId}/cancel`, request);
  }

  getReservations(filter: ReservationFilter = {}): Observable<PagedResult<Reservation>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<PagedResult<Reservation>>(`${this.apiUrl}/reservations`, { params });
  }

  getReservation(reservationId: string): Observable<ReservationDetail> {
    return this.http.get<ReservationDetail>(`${this.apiUrl}/reservations/${reservationId}`);
  }

  getStudentReservations(studentId: string): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.apiUrl}/reservations/student/${studentId}`);
  }

  getMyReservations(): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.apiUrl}/reservations/my`);
  }

  getMyUpcomingReservations(): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.apiUrl}/reservations/my/upcoming`);
  }

  /** Clases reservables con cupos y elegibilidad evaluada en el servidor. El personal indica studentId. */
  getAvailableClasses(filter: {
    from: string;
    to: string;
    studentId?: string;
    activityId?: string;
    roomId?: string;
    instructorId?: string;
  }): Observable<ReservableClassSession[]> {
    let params = new HttpParams().set('from', filter.from).set('to', filter.to);
    if (filter.studentId) params = params.set('studentId', filter.studentId);
    if (filter.activityId) params = params.set('activityId', filter.activityId);
    if (filter.roomId) params = params.set('roomId', filter.roomId);
    if (filter.instructorId) params = params.set('instructorId', filter.instructorId);
    return this.http.get<ReservableClassSession[]>(`${this.apiUrl}/reservations/available-classes`, { params });
  }

  getSessionReservations(classSessionId: string): Observable<Reservation[]> {
    return this.http.get<Reservation[]>(`${this.apiUrl}/reservations/class-session/${classSessionId}`);
  }

  getSessionWaitlist(classSessionId: string): Observable<WaitlistEntry[]> {
    return this.http.get<WaitlistEntry[]>(`${this.apiUrl}/reservations/class-session/${classSessionId}/waitlist`);
  }

  leaveWaitlist(classSessionId: string): Observable<Reservation> {
    return this.http.delete<Reservation>(`${this.apiUrl}/reservations/class-session/${classSessionId}/waitlist/me`);
  }

  acceptWaitlistOffer(reservationId: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.apiUrl}/reservations/${reservationId}/accept-waitlist-offer`, {});
  }

  declineWaitlistOffer(reservationId: string): Observable<Reservation> {
    return this.http.post<Reservation>(`${this.apiUrl}/reservations/${reservationId}/decline-waitlist-offer`, {});
  }

  // --- Asistencia ---
  getSessionAttendanceSummary(classSessionId: string): Observable<any> {
    return this.http.get(`${this.apiUrl}/reservations/class-session/${classSessionId}/attendance-summary`);
  }

  recordAttendance(reservationId: string, source: AttendanceSource = AttendanceSource.SecretaryDesk): Observable<Reservation> {
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

  // --- Pagos ---
  getPayments(filter: PaymentFilter = {}): Observable<PagedResult<Payment>> {
    return this.http.get<PagedResult<Payment>>(`${this.apiUrl}/payments`, { params: this.toParams(filter) });
  }

  getPendingPayments(page = 1, pageSize = 20): Observable<PagedResult<Payment>> {
    return this.http.get<PagedResult<Payment>>(`${this.apiUrl}/payments/pending`, {
      params: this.toParams({ page, pageSize })
    });
  }

  getPaymentById(paymentId: string): Observable<Payment> {
    return this.http.get<Payment>(`${this.apiUrl}/payments/${paymentId}`);
  }

  getStudentPayments(studentId: string): Observable<Payment[]> {
    return this.http.get<Payment[]>(`${this.apiUrl}/payments/student/${studentId}`);
  }

  getPaymentRefunds(paymentId: string): Observable<PaymentRefund[]> {
    return this.http.get<PaymentRefund[]>(`${this.apiUrl}/payments/${paymentId}/refunds`);
  }

  getMembershipBalance(membershipId: string): Observable<MembershipBalance> {
    return this.http.get<MembershipBalance>(`${this.apiUrl}/payments/obligations/membership/${membershipId}`);
  }

  /** La clave de idempotencia evita un cobro duplicado ante un doble envío o un reintento. */
  registerPayment(request: RegisterPaymentRequest, idempotencyKey?: string): Observable<Payment> {
    return this.http.post<Payment>(`${this.apiUrl}/payments`, request, {
      headers: this.idempotencyHeaders(idempotencyKey)
    });
  }

  confirmPayment(paymentId: string, externalTransactionId?: string, notes?: string): Observable<Payment> {
    return this.http.post<Payment>(`${this.apiUrl}/payments/${paymentId}/confirm`, { externalTransactionId, notes });
  }

  failPayment(paymentId: string, reason: string): Observable<Payment> {
    return this.http.post<Payment>(`${this.apiUrl}/payments/${paymentId}/fail`, { reason });
  }

  cancelPayment(paymentId: string, reason: string): Observable<Payment> {
    return this.http.post<Payment>(`${this.apiUrl}/payments/${paymentId}/cancel`, { reason });
  }

  applyPayment(paymentId: string, request: ApplyPaymentRequest): Observable<Payment> {
    return this.http.post<Payment>(`${this.apiUrl}/payments/${paymentId}/apply`, request);
  }

  refundPayment(paymentId: string, request: CreatePaymentRefundRequest, idempotencyKey?: string): Observable<PaymentRefund> {
    return this.http.post<PaymentRefund>(`${this.apiUrl}/payments/${paymentId}/refunds`, request, {
      headers: this.idempotencyHeaders(idempotencyKey)
    });
  }

  // --- Pagos en línea ---

  /** El importe lo calcula el servidor (saldo pendiente). Redirigir al alumno a `checkoutUrl`. */
  createPaymentCheckout(membershipId: string, idempotencyKey?: string): Observable<Payment> {
    return this.http.post<Payment>(`${this.apiUrl}/payments/checkout`, { membershipId }, {
      headers: this.idempotencyHeaders(idempotencyKey)
    });
  }

  reconcilePayment(paymentId: string): Observable<Payment> {
    return this.http.post<Payment>(`${this.apiUrl}/payments/${paymentId}/reconcile`, {});
  }

  getPaymentDiscrepancies(status?: PaymentDiscrepancyStatus, page = 1, pageSize = 20): Observable<PagedResult<PaymentDiscrepancy>> {
    return this.http.get<PagedResult<PaymentDiscrepancy>>(`${this.apiUrl}/payments/discrepancies`, {
      params: this.toParams({ status, page, pageSize })
    });
  }

  resolvePaymentDiscrepancy(discrepancyId: string, notes: string): Observable<PaymentDiscrepancy> {
    return this.http.post<PaymentDiscrepancy>(`${this.apiUrl}/payments/discrepancies/${discrepancyId}/resolve`, { notes });
  }

  private idempotencyHeaders(idempotencyKey?: string): HttpHeaders {
    return idempotencyKey ? new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) : new HttpHeaders();
  }

  private toParams(values: object): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(values)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return params;
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
    return this.http.get<NotificationItem[]>(`${this.apiUrl}/notifications/my`, { params }).pipe(
      map((items) =>
        (items ?? []).map((raw) => ({
          ...raw,
          body: raw.body ?? raw.message ?? '',
          message: raw.body ?? raw.message ?? '',
          sentAtUtc: raw.sentAtUtc ?? raw.createdAtUtc ?? '',
          createdAtUtc: raw.sentAtUtc ?? raw.createdAtUtc ?? ''
        }))
      )
    );
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
