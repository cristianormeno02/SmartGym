import { DocumentType } from './person.model';
export interface Room {
  id: string;
  name: string;
  description?: string;
  capacity: number;
  isActive: boolean;
}

export interface PublicActivity {
  code: string;
  name: string;
  shortDescription?: string;
  description?: string;
  equipmentNotes?: string;
  colorHex?: string;
  minAge?: number;
  maxAge?: number;
  logoUrl?: string;
  primaryImageUrl?: string;
  galleryImageUrls: string[];
}

export enum ActivityStatus {
  Active = 1,
  Inactive = 2,
  Archived = 3
}

export interface CreateRoomRequest {
  name: string;
  description?: string;
  capacity: number;
}

export interface UpdateRoomRequest {
  name: string;
  description?: string;
  capacity: number;
  isActive: boolean;
}

export interface CreateActivityRequest {
  code: string;
  name: string;
  shortDescription?: string;
  description?: string;
  equipmentNotes?: string;
  colorHex?: string;
  defaultCapacity?: number;
  minAge?: number;
  maxAge?: number;
}

export interface UpdateActivityRequest {
  name: string;
  shortDescription?: string;
  description?: string;
  equipmentNotes?: string;
  colorHex?: string;
  defaultCapacity?: number;
  minAge?: number;
  maxAge?: number;
  version: number;
}

export interface ChangeActivityStatusRequest {
  status: ActivityStatus | number;
  version: number;
}

export interface CreateRecurringScheduleRequest {
  activityId: string;
  roomId: string;
  instructorId: string;
  secretaryId?: string | null;
  dayOfWeek: number;
  startTime: string; // "HH:mm:ss" or "HH:mm"
  endTime: string;
  validFrom: string; // "yyyy-MM-dd"
  validTo?: string | null;
  maxCapacity?: number | null;
}

export interface UpdateRecurringScheduleRequest {
  roomId: string;
  instructorId: string;
  secretaryId?: string | null;
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  validFrom: string;
  validTo?: string | null;
  maxCapacity?: number | null;
  isActive: boolean;
}

export enum ScheduleConflictKind {
  Room = 0,
  Instructor = 1,
  WithinBatch = 2
}

export interface ScheduleConflict {
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  kind: ScheduleConflictKind | number;
  conflictingScheduleId?: string | null;
  conflictingActivityName?: string | null;
  conflictingStartTime?: string | null;
  conflictingEndTime?: string | null;
}

export interface BatchScheduleSlot {
  daysOfWeek: number[];
  startTime: string;
  endTime: string;
}

export interface CreateBatchRecurringScheduleRequest {
  activityId: string;
  roomId: string;
  instructorId: string;
  secretaryId?: string | null;
  validFrom: string;
  validTo?: string | null;
  maxCapacity?: number | null;
  slots: BatchScheduleSlot[];
}

export interface CreateMembershipPlanRequest {
  name: string;
  description?: string;
  durationDays: number;
  price: number;
  credits: number;
  isUnlimited: boolean;
  appliesToAllActivities: boolean;
  allowedActivityIds?: string[];
}

export interface UpdateMembershipPlanRequest {
  name: string;
  description?: string;
  durationDays: number;
  price: number;
  credits: number;
  isUnlimited: boolean;
  appliesToAllActivities: boolean;
  allowedActivityIds?: string[];
  isActive: boolean;
}

export interface Activity {
  id: string;
  code: string;
  name: string;
  shortDescription?: string;
  description?: string;
  equipmentNotes?: string;
  colorHex?: string;
  defaultCapacity?: number;
  minAge?: number;
  maxAge?: number;
  status: number;
  version?: number;
  logoUrl?: string;
  primaryImageUrl?: string;
  createdAtUtc?: string;
  isActive: boolean;
}

export interface RecurringSchedule {
  id: string;
  activityId: string;
  activityName: string;
  roomId: string;
  roomName: string;
  instructorId: string;
  instructorName: string;
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  validFrom: string;
  validTo?: string;
  maxCapacity?: number;
  isActive: boolean;
}

export interface ClassSession {
  id: string;
  recurringScheduleId?: string;
  activityId: string;
  activityName: string;
  roomId: string;
  roomName: string;
  instructorId: string;
  instructorName: string;
  originalInstructorId?: string;
  originalInstructorName?: string;
  substituteInstructorId?: string;
  substituteInstructorName?: string;
  date: string;
  startTime: string;
  endTime: string;
  maxCapacity: number;
  reservedCount: number;
  attendedCount: number;
  availableSpots: number;
  status: number; // 0=Scheduled, 1=InProgress, 2=Finished, 3=Suspended, 4=Cancelled
  cancellationReason?: string;
  suspendedAtUtc?: string;
}

export interface MembershipPlan {
  id: string;
  name: string;
  description?: string;
  durationDays: number;
  price: number;
  credits: number;
  isUnlimited: boolean;
  appliesToAllActivities: boolean;
  allowedActivityIds: string[];
  allowedActivityNames: string[];
  isActive: boolean;
}

export enum MembershipStatus {
  Pending = 0,
  Active = 1,
  Expired = 2,
  Suspended = 3,
  Cancelled = 4
}

export interface Membership {
  id: string;
  studentId: string;
  studentName: string;
  studentDni?: string;
  documentType?: DocumentType;
  documentNumber?: string;
  membershipPlanId: string;
  membershipPlanName: string;
  renewedFromMembershipId?: string;
  startDate: string;
  endDate: string;
  totalCredits: number;
  availableCredits: number;
  status: MembershipStatus; // 0=Pending, 1=Active, 2=Expired, 3=Suspended, 4=Cancelled
  effectiveStatus?: MembershipStatus;
  isValidToday: boolean;
  purchasedAtUtc?: string;
  activatedAtUtc?: string;
  durationDaysSnapshot?: number;
  initialCredits?: number;
  extraCreditsGranted?: number;
  isUnlimited?: boolean;
  appliesToAllActivities?: boolean;
  initialCreditsLoaded?: boolean;
  hasAvailableCredits?: boolean;
  isDepleted?: boolean;
  startsInFuture?: boolean;
  daysRemaining?: number;
  originalPrice?: number;
  discountAmount?: number;
  purchasePrice?: number;
  currency?: string;
  totalPaidAmount?: number;
  pendingBalance?: number;
  hasOutstandingBalance?: boolean;
  promotionId?: string;
  promotionCode?: string;
  notes?: string;
  version?: number;
}

export interface MembershipStatusChange {
  id: string;
  previousStatus?: MembershipStatus;
  newStatus: MembershipStatus;
  reason: string;
  notes?: string;
  changedByUserId?: string;
  changedAtUtc: string;
}

export interface MembershipSuspension {
  id: string;
  startDate: string;
  endDate?: string;
  reactivatedAtDate?: string;
  extendExpirationDate: boolean;
  extensionDaysApplied: number;
  reason: string;
  notes?: string;
  suspendedByUserId: string;
  suspendedAtUtc: string;
  reactivatedByUserId?: string;
  reactivatedAtUtc?: string;
  isActiveSuspension: boolean;
}

/** Cash = 1, BankTransfer = 2, CreditCard = 3, DebitCard = 4, DigitalWallet = 5, Other = 6. */
export type PaymentMethod = 1 | 2 | 3 | 4 | 5 | 6;

/** Pending = 1, Completed = 2, Failed = 3, Refunded = 4, Cancelled = 5, PartiallyRefunded = 6. */
export type PaymentStatus = 1 | 2 | 3 | 4 | 5 | 6;

/** Manual = 1, MercadoPago = 2. */
export type PaymentProvider = 1 | 2;

/** Pending = 1, Completed = 2, Failed = 3. */
export type PaymentRefundStatus = 1 | 2 | 3;

/**
 * AmountMismatch = 1, CurrencyMismatch = 2, ApprovedAfterTerminalState = 3, Overpayment = 4,
 * DuplicateCharge = 5, ExternalRefund = 6, UnknownPayment = 7.
 */
export type PaymentDiscrepancyType = 1 | 2 | 3 | 4 | 5 | 6 | 7;

/** Open = 1, Resolved = 2. */
export type PaymentDiscrepancyStatus = 1 | 2;

export interface Payment {
  id: string;
  payerPersonId: string;
  payerName: string;
  membershipId?: string;
  classSessionId?: string;
  amount: number;
  method: PaymentMethod;
  status: PaymentStatus;
  externalTransactionId?: string;
  receiptUrl?: string;
  notes?: string;
  paidAtUtc: string;
  provider: PaymentProvider;
  currency: string;
  refundedAmount: number;
  /** Importe cobrado menos lo devuelto. */
  netAmount: number;
  confirmedAtUtc?: string;
  cancelledAtUtc?: string;
  /** Sólo en pagos en línea pendientes: adonde enviar al alumno para pagar. */
  checkoutUrl?: string;
  expiresAtUtc?: string;
}

/** Diferencia entre lo que informa la pasarela y el pago interno, pendiente de revisión. */
export interface PaymentDiscrepancy {
  id: string;
  paymentId?: string;
  provider: PaymentProvider;
  type: PaymentDiscrepancyType;
  status: PaymentDiscrepancyStatus;
  internalAmount?: number;
  internalCurrency?: string;
  internalStatus?: string;
  externalAmount?: number;
  externalCurrency?: string;
  externalStatus?: string;
  externalTransactionId?: string;
  detectedAtUtc: string;
  resolvedAtUtc?: string;
  resolvedByUserId?: string;
  resolutionNotes?: string;
}

/** Pago tal como llega en el detalle de una membresía: mismo contrato que /api/payments. */
export type MembershipPayment = Payment;

export interface PaymentRefund {
  id: string;
  paymentId: string;
  amount: number;
  currency: string;
  reason: string;
  notes?: string;
  requestedByUserId?: string;
  createdAtUtc: string;
  /** La devolución de un pago en línea queda pendiente hasta que la pasarela responde. */
  status: PaymentRefundStatus;
  externalRefundId?: string;
  completedAtUtc?: string;
  failureReason?: string;
}

export interface MembershipBalance {
  membershipId: string;
  studentId: string;
  purchasePrice: number;
  currency: string;
  /** Neto de los pagos confirmados. */
  totalPaidAmount: number;
  pendingPaymentsAmount: number;
  pendingBalance: number;
  payments: Payment[];
  refunds: PaymentRefund[];
}

export interface PaymentFilter {
  studentId?: string;
  membershipId?: string;
  classSessionId?: string;
  status?: PaymentStatus;
  method?: PaymentMethod;
  provider?: PaymentProvider;
  fromUtc?: string;
  toUtc?: string;
  page?: number;
  pageSize?: number;
}

export interface RegisterPaymentRequest {
  payerPersonId: string;
  membershipId?: string;
  classSessionId?: string;
  amount: number;
  method: PaymentMethod;
  externalTransactionId?: string;
  notes?: string;
  /** Sólo Pending (1) o Completed (2). Por defecto Completed. */
  status?: PaymentStatus;
  currency?: string;
}

export interface ApplyPaymentRequest {
  membershipId?: string;
  classSessionId?: string;
}

export interface CreatePaymentRefundRequest {
  amount: number;
  reason: string;
  notes?: string;
}

/** Cuerpo de error de /api/payments. */
export interface PaymentApiError {
  code: string;
  message: string;
  details?: unknown;
}

export interface MembershipDetail extends Membership {
  promotionName?: string;
  promotionRedemptionId?: string;
  createdByUserId?: string;
  cancelledAtUtc?: string;
  cancelledByUserId?: string;
  cancellationReason?: string;
  allowedActivityIds: string[];
  payments: MembershipPayment[];
  suspensions: MembershipSuspension[];
  statusHistory: MembershipStatusChange[];
}

export interface MembershipIneligibilityReason {
  code: string;
  message: string;
}

export interface MembershipEligibility {
  membershipId: string;
  studentId: string;
  evaluatedDate: string;
  evaluatedActivityId?: string;
  evaluatedClassSessionId?: string;
  isEligible: boolean;
  effectiveStatus: MembershipStatus;
  isUnlimited: boolean;
  availableCredits: number;
  hasAvailableCredits: boolean;
  isDepleted: boolean;
  ineligibilityReasons: MembershipIneligibilityReason[];
}

export interface MembershipCreditsSummary {
  membershipId: string;
  studentId: string;
  isUnlimited: boolean;
  initialCredits: number;
  consumedCredits: number;
  refundedCredits: number;
  bonusCredits: number;
  compensatoryCredits: number;
  forfeitedCredits: number;
  expiredCredits: number;
  availableCredits: number;
  hasAvailableCredits: boolean;
  isDepleted: boolean;
  movements: CreditMovement[];
}

export enum ReservationStatus {
  Confirmed = 1,
  Cancelled = 3,
  Attended = 4,
  NoShow = 5,
  Waitlisted = 6,
  Pending = 7
}

export enum ReservationOrigin {
  StudentSelfService = 1,
  StaffDesk = 2,
  Legacy = 3
}

export enum ReservationAccessType {
  MembershipCredit = 1,
  UnlimitedMembership = 2,
  CompensatoryCredit = 3,
  SingleClassTicket = 4,
  PromotionalBenefit = 5
}

export enum ReservationCancellationSource {
  Student = 1,
  Staff = 2,
  ClassSessionChange = 3,
  MembershipChange = 4,
  WaitlistOfferExpired = 5,
  WaitlistDeclined = 6,
  WaitlistExpired = 7
}

export enum AttendanceSource {
  SecretaryDesk = 1,
  InstructorInRoom = 2,
  SelfCheckIn = 3
}

/** Estados que cuentan como reserva activa: ocupan cupo o esperan uno. */
export const ACTIVE_RESERVATION_STATUSES: readonly ReservationStatus[] = [
  ReservationStatus.Confirmed,
  ReservationStatus.Waitlisted,
  ReservationStatus.Pending
];

export interface ReservationStatusChange {
  id: string;
  reservationId: string;
  previousStatus?: ReservationStatus;
  newStatus: ReservationStatus;
  reason: string;
  changedByUserId?: string;
  trigger: number;
  changedAtUtc: string;
}

export interface Reservation {
  id: string;
  classSessionId: string;
  activityId?: string;
  activityName: string;
  classDate: string;
  startTime: string;
  endTime: string;
  startsAtUtc?: string;
  endsAtUtc?: string;
  roomName: string;
  studentId: string;
  studentName: string;
  studentDni?: string;
  documentType?: DocumentType;
  documentNumber?: string;
  membershipId?: string;
  status: ReservationStatus;
  origin: ReservationOrigin;
  accessType: ReservationAccessType;
  reservedAtUtc: string;
  confirmedAtUtc?: string;
  cancelledAtUtc?: string;
  cancellationReason?: string;
  cancellationSource?: ReservationCancellationSource;
  isLateCancellation: boolean;
  isPolicyOverride: boolean;
  policyOverrideReason?: string;
  affectedBySessionChange: boolean;
  rescheduledToSessionId?: string;
  attendedAtUtc?: string;
  attendanceSource?: AttendanceSource;
  waitListPosition?: number;
  waitlistEnteredAtUtc?: string;
  waitlistOfferExpiresAtUtc?: string;
  creditMovementId?: string;
  refundCreditMovementId?: string;
  singleClassTicketMovementId?: string;
  paymentId?: string;
  promotionRedemptionId?: string;
  notes?: string;
  wasIdempotentReplay?: boolean;
}

export interface ReservationDetail extends Reservation {
  activityId: string;
  startsAtUtc: string;
  endsAtUtc: string;
  createdByUserId?: string;
  cancelledByUserId?: string;
  checkedInByUserId?: string;
  statusHistory: ReservationStatusChange[];
}

export interface ReservableClassSession {
  classSessionId: string;
  activityId: string;
  activityName: string;
  roomId: string;
  roomName: string;
  effectiveInstructorId: string;
  instructorName: string;
  classDate: string;
  startTime: string;
  endTime: string;
  startsAtUtc: string;
  endsAtUtc: string;
  capacity: number;
  reservedCount: number;
  availableSpots: number;
  waitlistCount: number;
  isWithinBookingWindow: boolean;
  hasActiveReservation: boolean;
  activeReservationId?: string;
  activeReservationStatus?: ReservationStatus;
  activeWaitlistPosition?: number;
  canReserve: boolean;
  canJoinWaitlist: boolean;
  suggestedAccessType?: ReservationAccessType;
  selectedMembershipId?: string;
  usableCredits?: number;
  ineligibilityReasons: MembershipIneligibilityReason[];
}

export interface WaitlistEntry {
  reservationId: string;
  classSessionId: string;
  studentId: string;
  studentName: string;
  studentDni: string;
  status: ReservationStatus;
  waitListPosition?: number;
  waitlistEnteredAtUtc?: string;
  waitlistOfferExpiresAtUtc?: string;
  accessType: ReservationAccessType;
  membershipId?: string;
}

export interface CreateReservationRequest {
  classSessionId: string;
  studentId?: string;
  membershipId?: string;
  singleClassTicketMovementId?: string;
  promotionCode?: string;
  allowWaitlist?: boolean;
  notes?: string;
  overrideBookingWindow?: boolean;
  policyOverrideReason?: string;
}

export interface CancelReservationRequest {
  reason?: string;
  overridePolicy?: boolean;
  policyOverrideReason?: string;
}

export interface ReservationFilter {
  studentId?: string;
  classSessionId?: string;
  activityId?: string;
  status?: ReservationStatus;
  accessType?: ReservationAccessType;
  origin?: ReservationOrigin;
  from?: string;
  to?: string;
  isLateCancellation?: boolean;
  affectedBySessionChange?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

/** Cuerpo de error de /api/reservations. */
export interface ReservationApiError {
  code: string;
  message: string;
  reasons?: MembershipIneligibilityReason[] | Record<string, string[]>;
}

export interface DashboardMetrics {
  totalActiveStudents: number;
  totalActiveInstructors: number;
  totalClassesToday: number;
  totalReservationsToday: number;
  totalAttendanceToday: number;
  attendanceRateTodayPercent: number;
  monthlyRevenue: number;
  expiringMembershipsNext7Days: number;
  pendingMedicalCertificatesCount: number;
}

export interface MedicalCertificateStatus {
  personId: string;
  fullName: string;
  hasCertificate: boolean;
  isValid: boolean;
  isExpired: boolean;
  expirationDate?: string;
  certificateUrl?: string;
  daysUntilExpiration?: number;
}

export enum NotificationCategory {
  Reservations = 1,
  Classes = 2,
  Memberships = 3,
  Payments = 4,
  Promotions = 5,
  Operational = 6,
  Security = 7
}

export enum NotificationChannel {
  Email = 1,
  InApp = 2,
  WhatsApp = 3,
  Push = 4
}

export enum NotificationType {
  ClassSuspended = 1,
  ClassReminder = 2,
  MembershipExpiring = 3,
  PromotionalAlert = 4,
  PaymentConfirmed = 5,
  MembershipActivated = 6,
  MembershipSuspended = 7,
  MembershipExpired = 8,
  ReservationConfirmed = 9,
  ReservationCancelled = 10,
  ReservationWaitlisted = 11,
  WaitlistPromoted = 12,
  WaitlistOfferAvailable = 13,
  WaitlistOfferExpired = 14,
  ClassCancelled = 15,
  ClassRescheduled = 16,
  ClassRoomChanged = 17,
  ClassInstructorChanged = 18,
  MembershipReactivated = 19,
  CreditsAdjusted = 20,
  PaymentRejected = 21,
  RefundConfirmed = 22,
  OperationalNotice = 23,
  SecurityAlert = 24
}

export enum NotificationDeliveryStatus {
  Pending = 1,
  Processing = 2,
  Sent = 3,
  Delivered = 4,
  Failed = 5,
  Cancelled = 6,
  Skipped = 7
}

export enum NotificationTemplateStatus {
  Draft = 1,
  Active = 2,
  Inactive = 3
}

export interface NotificationItem {
  id: string;
  recipientPersonId?: string;
  recipientName?: string;
  type?: NotificationType;
  category?: NotificationCategory;
  channel?: NotificationChannel;
  title: string;
  body?: string;
  message: string;
  isRead: boolean;
  sentAtUtc?: string;
  createdAtUtc: string;
  readAtUtc?: string;
  relatedEntityType?: string;
  relatedEntityId?: string;
  actionUrl?: string;
  expiresAtUtc?: string;
}

export interface UnreadCountResponse {
  unreadCount: number;
}

export interface MarkAllNotificationsReadResponse {
  markedCount: number;
}

export interface NotificationPreferenceItem {
  category: NotificationCategory;
  channel: NotificationChannel;
  isEnabled: boolean;
  isLocked: boolean;
  isTransactional: boolean;
}

export interface NotificationPreferenceUpdateItem {
  category: NotificationCategory;
  channel: NotificationChannel;
  isEnabled: boolean;
}

export interface UpdateNotificationPreferencesRequest {
  preferences: NotificationPreferenceUpdateItem[];
}

export interface NotificationDeliveryItem {
  id: string;
  notificationMessageId: string;
  channel: NotificationChannel;
  status: NotificationDeliveryStatus;
  destination?: string;
  providerName?: string;
  providerMessageId?: string;
  attemptCount: number;
  maxAttempts: number;
  lastAttemptAtUtc?: string;
  nextAttemptAtUtc?: string;
  deliveredAtUtc?: string;
  errorCode?: string;
  errorMessage?: string;
  isPermanentFailure: boolean;
  retriedByUserId?: string;
  createdAtUtc: string;
}

export interface NotificationTemplateItem {
  id: string;
  code: string;
  type: NotificationType;
  channel: NotificationChannel;
  locale: string;
  subjectTemplate?: string;
  bodyTemplate: string;
  allowedVariables: string[];
  version: number;
  status: NotificationTemplateStatus;
  createdAtUtc: string;
  updatedAtUtc?: string;
}

export interface CreateNotificationTemplateRequest {
  code: string;
  type: NotificationType;
  channel: NotificationChannel;
  locale?: string;
  subjectTemplate?: string;
  bodyTemplate: string;
  allowedVariables: string[];
  activateImmediately?: boolean;
}

export interface UpdateNotificationTemplateRequest {
  subjectTemplate?: string;
  bodyTemplate: string;
  allowedVariables: string[];
  expectedVersion?: number;
}

export interface CreditMovement {
  id: string;
  membershipId?: string;
  studentId: string;
  movementType: number; // 0=InitialCredit, 1=ReservationDebit, 2=EarlyCancellationRefund, 3=CompensatoryCredit, 4=SingleTicket, 5=PromotionBonus, 6=CancellationForfeit, 7=Expiration
  amount: number;
  balanceAfter: number;
  concept: string;
  referenceId?: string;
  createdAtUtc: string;
}

export enum PromotionStatus {
  Active = 1,
  Paused = 2,
  Archived = 3
}

export enum PromotionBenefitType {
  PercentageDiscount = 1,
  FixedAmountDiscount = 2,
  ExtraCredits = 3
}

export interface Promotion {
  id: string;
  code: string;
  name: string;
  description?: string;
  conditionType: number; // 1=NewStudent, 2=FamilyGroup, 3=Referral, 4=TwoForOne, 5=CouponCode
  benefitType: PromotionBenefitType;
  benefitValue: number;
  validFrom: string;
  validTo?: string;
  maxUsesTotal?: number;
  currentUsesTotal: number;
  maxUsesPerPerson: number;
  applicablePlanId?: string;
  applicablePlanName?: string;
  isActive: boolean;
  status: PromotionStatus;
  version: number; // concurrencia optimista: se reenvía al editar
}

export interface AuditLog {
  id: string;
  action: string;
  entityName: string;
  entityId?: string;
  performedByUserId?: string;
  performedByEmail?: string;
  timestampUtc: string;
  details?: string;
}

export interface AdminUserOverview {
  userId: string;
  personId: string;
  fullName: string;
  email: string;
  documentType?: DocumentType;
  documentNumber?: string;
  dni?: string;
  isActive: boolean;
  roles: string[];
}
