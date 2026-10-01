export interface Room {
  id: string;
  name: string;
  description?: string;
  capacity: number;
  isActive: boolean;
}

export interface Activity {
  id: string;
  name: string;
  summary?: string;
  description?: string;
  minCapacity: number;
  maxCapacity: number;
  logoUrl?: string;
  imageUrls: string[];
  status: number;
  minAge?: number;
  maxAge?: number;
  defaultRoomId?: string;
  defaultRoomName?: string;
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

export interface Membership {
  id: string;
  studentId: string;
  studentName: string;
  studentDni?: string;
  documentType?: number | string;
  documentNumber?: string;
  membershipPlanId: string;
  membershipPlanName: string;
  startDate: string;
  endDate: string;
  totalCredits: number;
  availableCredits: number;
  status: number;
  isValidToday: boolean;
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
  documentType?: number | string;
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
  membershipId: string;
  studentId: string;
  movementType: number; // 0=InitialCredit, 1=ReservationDebit, 2=EarlyCancellationRefund, 3=CompensatoryCredit, 4=SingleTicket
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
  documentType?: number | string;
  documentNumber?: string;
  dni?: string;
  isActive: boolean;
  roles: string[];
}
