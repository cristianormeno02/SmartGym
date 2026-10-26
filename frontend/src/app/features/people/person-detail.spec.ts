import { signal } from '@angular/core';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { PersonDetailComponent } from './person-detail';
import { PeopleService } from '../../core/services/people.service';
import { AuthService } from '../../core/services/auth.service';
import { UserManagementService } from '../../core/services/user-management.service';
import { GymApiService } from '../../core/services/gym-api.service';
import { PersonDetail, PersonStatus, DocumentType } from '../../core/models/person.model';
import { UserAccountDto } from '../../core/models/user-management.models';
import { Membership, MembershipDetail, MembershipStatus } from '../../core/models/gym.models';

describe('PersonDetailComponent', () => {
  let component: PersonDetailComponent;
  let fixture: ComponentFixture<PersonDetailComponent>;
  let mockPeopleService: any;
  let mockAuthService: any;
  let mockUserManagementService: any;
  let mockGymApiService: any;

  const mockPerson: PersonDetail = {
    id: 'test-person-id',
    firstName: 'Martín',
    lastName: 'López',
    document: { type: DocumentType.Dni, number: '28111222', issuingCountry: 'AR', numberNormalized: '28111222' },
    status: PersonStatus.Active,
    email: 'martin@example.com',
    primaryPhone: '11445566',
    createdAtUtc: '2026-01-01T00:00:00Z',
    version: 1,
    hasUserAccount: false
  };

  const mockUserAccount: UserAccountDto = {
    hasAccount: true,
    userId: 'user-123',
    username: 'martin@example.com',
    isActive: true,
    lastLoginUtc: '2026-10-14T10:00:00Z',
    roles: ['Administrator', 'Secretary', 'Student']
  };

  const mockMemberships: Membership[] = [
    {
      id: 'mem-1',
      studentId: 'test-person-id',
      studentName: 'Martín López',
      membershipPlanId: 'plan-1',
      membershipPlanName: 'Pase Libre Full',
      startDate: '2026-10-01',
      endDate: '2026-10-30',
      totalCredits: 0,
      availableCredits: 0,
      isUnlimited: true,
      status: MembershipStatus.Active,
      isValidToday: true,
      purchasePrice: 35000
    }
  ];

  beforeEach(async () => {
    mockPeopleService = {
      getById: vi.fn().mockReturnValue(of(mockPerson)),
      changeStatus: vi.fn(),
      uploadPhoto: vi.fn(),
      deletePhoto: vi.fn(),
      isBusy: () => false
    };

    mockAuthService = {
      isAdmin: signal(false),
      isStaff: signal(true),
      currentUser: signal({ userId: 'admin-user-id', personId: 'admin-person-id', email: 'admin@gym.com', roles: ['Administrator'] })
    };

    mockUserManagementService = {
      getAccount: vi.fn().mockReturnValue(of(mockUserAccount)),
      provision: vi.fn().mockReturnValue(of(mockUserAccount)),
      updateRoles: vi.fn().mockReturnValue(of(mockUserAccount)),
      updateStatus: vi.fn().mockReturnValue(of({ ...mockUserAccount, isActive: false })),
      resetPassword: vi.fn().mockReturnValue(of(undefined))
    };

    mockGymApiService = {
      getStudentMemberships: vi.fn().mockReturnValue(of(mockMemberships)),
      getMembershipPlans: vi.fn().mockReturnValue(of([])),
      assignMembership: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [PersonDetailComponent],
      providers: [
        provideRouter([]),
        { provide: PeopleService, useValue: mockPeopleService },
        { provide: AuthService, useValue: mockAuthService },
        { provide: UserManagementService, useValue: mockUserManagementService },
        { provide: GymApiService, useValue: mockGymApiService },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => 'test-person-id'
              }
            }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PersonDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load person details on init', () => {
    expect(component).toBeTruthy();
    expect(mockPeopleService.getById).toHaveBeenCalledWith('test-person-id');
    expect(component.person()).toEqual(mockPerson);
  });

  it('should NOT display user account card when user is not Administrator', () => {
    const card = fixture.nativeElement.querySelector('[data-testid="user-account-card"]');
    expect(card).toBeNull();
    expect(mockUserManagementService.getAccount).not.toHaveBeenCalled();
  });

  describe('when user is Administrator', () => {
    beforeEach(() => {
      mockAuthService.isAdmin.set(true);
      component.loadAccount('test-person-id');
      fixture.detectChanges();
    });

    it('should display user account card and load account data', () => {
      expect(mockUserManagementService.getAccount).toHaveBeenCalledWith('test-person-id');
      expect(component.userAccount()).toEqual(mockUserAccount);

      const card = fixture.nativeElement.querySelector('[data-testid="user-account-card"]');
      expect(card).not.toBeNull();
    });

    it('should show active status badge and role chips', () => {
      const statusBadge = fixture.nativeElement.querySelector('[data-testid="account-status-badge"]');
      expect(statusBadge.textContent).toContain('Activa');

      const rolesContainer = fixture.nativeElement.querySelector('[data-testid="account-roles-container"]');
      expect(rolesContainer.textContent).toContain('Administrador');
      expect(rolesContainer.textContent).toContain('Secretaria');
    });

    it('should show notice when person has no account and disable provision button if email is missing', () => {
      component.userAccount.set({
        hasAccount: false,
        userId: null,
        username: null,
        isActive: null,
        lastLoginUtc: null,
        roles: []
      });
      component.person.set({ ...mockPerson, email: undefined });
      fixture.detectChanges();

      const notice = fixture.nativeElement.querySelector('[data-testid="no-account-notice"]');
      expect(notice).not.toBeNull();

      const btn = fixture.nativeElement.querySelector('[data-testid="btn-provision-account"]');
      expect(btn.disabled).toBe(true);

      const warning = fixture.nativeElement.querySelector('[data-testid="no-email-warning"]');
      expect(warning).not.toBeNull();
    });

    it('should disable provision button if person is deceased', () => {
      component.userAccount.set({
        hasAccount: false,
        userId: null,
        username: null,
        isActive: null,
        lastLoginUtc: null,
        roles: []
      });
      component.person.set({ ...mockPerson, status: PersonStatus.Deceased });
      fixture.detectChanges();

      const btn = fixture.nativeElement.querySelector('[data-testid="btn-provision-account"]');
      expect(btn.disabled).toBe(true);

      const deceasedWarning = fixture.nativeElement.querySelector('[data-testid="deceased-warning"]');
      expect(deceasedWarning).not.toBeNull();
    });

    it('should open provision modal and call provision service', () => {
      component.openProvisionModal();
      expect(component.showProvisionModal()).toBe(true);

      component.provisionPassword = 'SecurePassword123!';
      component.provisionRoleIds[2] = true; // Secretary
      component.provisionRoleIds[3] = true; // Instructor

      component.submitProvision();

      expect(mockUserManagementService.provision).toHaveBeenCalledWith('test-person-id', {
        password: 'SecurePassword123!',
        roleIds: [2, 3]
      });
      expect(component.showProvisionModal()).toBe(false);
      expect(component.person()?.hasUserAccount).toBe(true);
      expect(component.feedbackSuccess()).toBe(true);
    });

    it('should display error message on provision failure (e.g. 409 conflict)', () => {
      mockUserManagementService.provision.mockReturnValue(
        throwError(() => ({ error: { message: 'El correo electrónico ya se encuentra registrado.' } }))
      );

      component.openProvisionModal();
      component.provisionPassword = 'Password123!';
      component.submitProvision();

      expect(component.showProvisionModal()).toBe(true);
      expect(component.provisionError()).toBe('El correo electrónico ya se encuentra registrado.');
    });

    it('should display the validation detail instead of the generic 400 message', () => {
      mockUserManagementService.provision.mockReturnValue(
        throwError(() => ({
          status: 400,
          error: {
            message: 'Se produjeron uno o más errores de validación.',
            errors: { Password: ['La contraseña debe tener al menos 8 caracteres, al menos una letra mayúscula, un número y un carácter especial.'] }
          }
        }))
      );

      component.openProvisionModal();
      component.provisionPassword = 'weak';
      component.submitProvision();

      expect(component.provisionError()).toBe(
        'La contraseña debe tener al menos 8 caracteres, al menos una letra mayúscula, un número y un carácter especial.'
      );
    });

    it('should open roles modal with current roles initialized', () => {
      component.openRolesModal();
      expect(component.showRolesModal()).toBe(true);
      expect(component.manageRoleIds[1]).toBe(true); // Administrator
      expect(component.manageRoleIds[2]).toBe(true); // Secretary
      expect(component.manageRoleIds[3]).toBe(false); // Instructor
      expect(component.manageRoleIds[4]).toBe(true); // Student
    });

    it('should submit role updates and close modal on success', () => {
      component.openRolesModal();
      component.manageRoleIds[2] = false;
      component.manageRoleIds[3] = true;
      component.manageRoleIds[4] = false;

      component.submitUpdateRoles();

      expect(mockUserManagementService.updateRoles).toHaveBeenCalledWith('test-person-id', {
        roleIds: [1, 3]
      });
      expect(component.showRolesModal()).toBe(false);
      expect(component.feedbackSuccess()).toBe(true);
    });

    it('should display conflict error (409) in roles modal when removing last admin', () => {
      mockUserManagementService.updateRoles.mockReturnValue(
        throwError(() => ({ error: { message: 'No se puede revocar el rol de Administrador porque no existen otros administradores operativos.' } }))
      );

      component.openRolesModal();
      component.submitUpdateRoles();

      expect(component.showRolesModal()).toBe(true);
      expect(component.rolesError()).toContain('otros administradores');
    });

    it('should warn when administrator is modifying own roles', () => {
      mockAuthService.currentUser.set({
        userId: 'user-123',
        personId: 'test-person-id',
        email: 'martin@example.com',
        roles: ['Administrator']
      });

      component.openRolesModal();
      fixture.detectChanges();

      expect(component.isSelfAccount()).toBe(true);
      const warning = fixture.nativeElement.querySelector('[data-testid="self-modification-warning"]');
      expect(warning).not.toBeNull();
    });

    it('should reset password on submit', () => {
      component.openResetPasswordModal();
      expect(component.showResetPasswordModal()).toBe(true);

      component.newAccountPassword = 'NewSecretPassword1!';
      component.submitResetPassword();

      expect(mockUserManagementService.resetPassword).toHaveBeenCalledWith('test-person-id', {
        newPassword: 'NewSecretPassword1!'
      });
      expect(component.showResetPasswordModal()).toBe(false);
      expect(component.feedbackSuccess()).toBe(true);
    });

    it('should toggle account status between active and suspended', () => {
      component.openStatusAccountModal();
      expect(component.showStatusAccountModal()).toBe(true);

      component.submitToggleStatusAccount();

      expect(mockUserManagementService.updateStatus).toHaveBeenCalledWith('test-person-id', {
        isActive: false
      });
      expect(component.showStatusAccountModal()).toBe(false);
      expect(component.userAccount()?.isActive).toBe(false);
    });

    it('should show error in status modal if suspending last admin fails with 409', () => {
      mockUserManagementService.updateStatus.mockReturnValue(
        throwError(() => ({ error: { message: 'No se puede suspender al último administrador operativo del sistema.' } }))
      );

      component.openStatusAccountModal();
      component.submitToggleStatusAccount();

      expect(component.showStatusAccountModal()).toBe(true);
      expect(component.statusAccountError()).toContain('último administrador');
    });
  });

  describe('Student Memberships section', () => {
    it('should load and display student memberships on init', () => {
      expect(mockGymApiService.getStudentMemberships).toHaveBeenCalledWith('test-person-id');
      expect(component.memberships().length).toBe(1);

      const card = fixture.nativeElement.querySelector('[data-testid="student-memberships-card"]');
      expect(card).not.toBeNull();
      expect(card.textContent).toContain('Membresías del Alumno');
      expect(card.textContent).toContain('Pase Libre Full');
      expect(card.textContent).toContain('Activa');
      expect(card.textContent).toContain('2026-10-01');
      expect(card.textContent).toContain('2026-10-30');

      const assignBtn = fixture.nativeElement.querySelector('[data-testid="btn-assign-membership"]');
      expect(assignBtn).not.toBeNull();
      expect(assignBtn.disabled).toBe(false);
    });

    it('should disable assign membership button and show warning when person is not ACTIVE', () => {
      component.person.set({ ...mockPerson, status: PersonStatus.Blocked });
      fixture.detectChanges();

      const assignBtn = fixture.nativeElement.querySelector('[data-testid="btn-assign-membership"]') as HTMLButtonElement;
      expect(assignBtn.disabled).toBe(true);

      const warning = fixture.nativeElement.querySelector('[data-testid="inactive-person-membership-warning"]');
      expect(warning).not.toBeNull();
    });

    it('should open AssignMembershipModalComponent when clicking + Asignar Membresía', () => {
      const assignBtn = fixture.nativeElement.querySelector('[data-testid="btn-assign-membership"]') as HTMLButtonElement;
      assignBtn.click();
      fixture.detectChanges();

      expect(component.showAssignMembershipModal()).toBe(true);
      const modal = fixture.nativeElement.querySelector('app-assign-membership-modal');
      expect(modal).not.toBeNull();
    });

    it('should close modal, display success feedback, and reload memberships when assignment completes', () => {
      component.openAssignMembershipModal();
      expect(component.showAssignMembershipModal()).toBe(true);

      const newMembershipDetail: MembershipDetail = {
        id: 'mem-2',
        studentId: 'test-person-id',
        studentName: 'Martín López',
        membershipPlanId: 'plan-1',
        membershipPlanName: 'Pase Libre Full',
        startDate: '2026-10-25',
        endDate: '2026-11-23',
        totalCredits: 0,
        availableCredits: 0,
        isUnlimited: true,
        status: MembershipStatus.Active,
        isValidToday: true,
        allowedActivityIds: [],
        payments: [],
        suspensions: [],
        statusHistory: []
      };

      mockGymApiService.getStudentMemberships.mockClear();
      component.onMembershipAssigned(newMembershipDetail);
      fixture.detectChanges();

      expect(component.showAssignMembershipModal()).toBe(false);
      expect(component.feedbackSuccess()).toBe(true);
      expect(component.feedback()).toContain('Membresía asignada exitosamente');
      expect(mockGymApiService.getStudentMemberships).toHaveBeenCalledWith('test-person-id');
    });
  });

  it('should open status modal and require reason', () => {
    component.openStatusModal();
    expect(component.showStatusModal()).toBe(true);

    component.statusReason = '';
    component.submitStatusChange();
    expect(component.modalError()).toContain('obligatorio');
    expect(mockPeopleService.changeStatus).not.toHaveBeenCalled();
  });

  it('should change status when reason is provided', () => {
    component.openStatusModal();
    component.newStatus = PersonStatus.Blocked;
    component.statusReason = 'Falta grave de conducta';

    const updated = { ...mockPerson, status: PersonStatus.Blocked, statusReason: 'Falta grave de conducta' };
    mockPeopleService.changeStatus.mockReturnValue(of(updated));

    component.submitStatusChange();

    expect(mockPeopleService.changeStatus).toHaveBeenCalledWith('test-person-id', {
      targetStatus: PersonStatus.Blocked,
      reason: 'Falta grave de conducta'
    });
    expect(component.showStatusModal()).toBe(false);
    expect(component.person()?.status).toBe(PersonStatus.Blocked);
  });

  it('should reject files exceeding 5MB on client side', () => {
    const largeFile = new File([new ArrayBuffer(6 * 1024 * 1024)], 'large.jpg', { type: 'image/jpeg' });
    const event = {
      target: {
        files: [largeFile]
      }
    } as any;

    component.onFileSelected(event);

    expect(component.feedback()).toContain('5 MB');
    expect(mockPeopleService.uploadPhoto).not.toHaveBeenCalled();
  });

  it('should upload valid image', () => {
    const validFile = new File(['abc'], 'avatar.png', { type: 'image/png' });
    const event = {
      target: {
        files: [validFile]
      }
    } as any;

    const updated = { ...mockPerson, photoUrl: 'https://r2.storage/photo.webp' };
    mockPeopleService.uploadPhoto.mockReturnValue(of(updated));

    component.onFileSelected(event);

    expect(mockPeopleService.uploadPhoto).toHaveBeenCalledWith('test-person-id', validFile);
    expect(component.person()?.photoUrl).toBe('https://r2.storage/photo.webp');
  });
});
