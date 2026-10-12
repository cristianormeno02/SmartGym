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

export interface MembershipPayment {
  id: string;
  studentId: string;
  studentName: string;
  membershipId?: string;
  amount: number;
  method: number;
  status: number;
  externalTransactionId?: string;
  notes?: string;
  paidAtUtc: string;
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

export interface NotificationItem {
  id: string;
  title: string;
  message: string;
  isRead: boolean;
  createdAtUtc: string;
  readAtUtc?: string;
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
