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

export interface Reservation {
  id: string;
  classSessionId: string;
  activityName: string;
  classDate: string;
  startTime: string;
  endTime: string;
  roomName: string;
  studentId: string;
  studentName: string;
  studentDni?: string;
  documentType?: DocumentType;
  documentNumber?: string;
  membershipId?: string;
  status: number; // 0=Reserved, 1=Confirmed, 2=Cancelled, 3=Attended, 4=NoShow, 5=WaitList
  reservedAtUtc: string;
  confirmedAtUtc?: string;
  cancelledAtUtc?: string;
  cancellationReason?: string;
  isLateCancellation: boolean;
  attendedAtUtc?: string;
  attendanceSource?: number;
  waitListPosition?: number;
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

export interface Promotion {
  id: string;
  name: string;
  code?: string;
  conditionType: number;
  benefitType: number;
  discountPercentage?: number;
  fixedDiscountAmount?: number;
  extraCredits?: number;
  startDate: string;
  endDate?: string;
  maxRedemptionsTotal?: number;
  currentRedemptionsCount: number;
  isActive: boolean;
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
