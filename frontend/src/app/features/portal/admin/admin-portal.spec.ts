import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { AdminPortalComponent } from './admin-portal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationsService } from '../../../core/services/notifications.service';
import { UserManagementService } from '../../../core/services/user-management.service';
import { of, throwError } from 'rxjs';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { AdminUserOverview } from '../../../core/models/gym.models';

describe('AdminPortalComponent', () => {
  let component: AdminPortalComponent;
  let fixture: ComponentFixture<AdminPortalComponent>;
  let gymApiSpy: any;
  let authServiceSpy: any;
  let notificationsServiceSpy: any;
  let userManagementSpy: any;

  const mockUsers: AdminUserOverview[] = [
    {
      userId: 'user-1',
      personId: 'person-1',
      fullName: 'Carlos Gómez',
      email: 'carlos@gym.com',
      isActive: true,
      roles: ['Administrator', 'Secretary']
    },
    {
      userId: 'user-2',
      personId: 'person-2',
      fullName: 'Laura Pausini',
      email: 'laura@gym.com',
      isActive: true,
      roles: ['Instructor']
    }
  ];

  beforeEach(() => {
    gymApiSpy = {
      getDashboardMetrics: vi.fn().mockReturnValue(of(null)),
      getAdminUsers: vi.fn().mockReturnValue(of(mockUsers)),
      getPromotions: vi.fn().mockReturnValue(of([])),
      getAuditLogs: vi.fn().mockReturnValue(of([])),
      getRooms: vi.fn().mockReturnValue(of([])),
      getActivities: vi.fn().mockReturnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 10, totalPages: 0, hasPreviousPage: false, hasNextPage: false }))
    };

    authServiceSpy = {
      isAdmin: vi.fn().mockReturnValue(true),
      isStaff: vi.fn().mockReturnValue(true),
      hasAnyRole: vi.fn().mockReturnValue(true),
      currentUser: signal({ userId: 'user-1', personId: 'person-1', email: 'carlos@gym.com', roles: ['Administrator'] })
    };

    notificationsServiceSpy = {
      getFailedDeliveries: vi.fn().mockReturnValue(of([])),
      getTemplates: vi.fn().mockReturnValue(of([]))
    };

    userManagementSpy = {
      updateRoles: vi.fn().mockReturnValue(of({}))
    };
  });

  const setupComponent = async (isAdmin: boolean) => {
    authServiceSpy.isAdmin.mockReturnValue(isAdmin);

    await TestBed.configureTestingModule({
      imports: [AdminPortalComponent],
      providers: [
        provideRouter([]),
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationsService, useValue: notificationsServiceSpy },
        { provide: UserManagementService, useValue: userManagementSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminPortalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  it('for Administrator: should display all tabs including users and audit, and load sensitive data', async () => {
    await setupComponent(true);

    const compiled = fixture.nativeElement as HTMLElement;
    const tabButtons = Array.from(compiled.querySelectorAll('.nav-tab-btn')).map(b => b.textContent?.trim());

    expect(tabButtons.some(t => t?.includes('Usuarios'))).toBe(true);
    expect(tabButtons.some(t => t?.includes('Auditoría'))).toBe(true);
    expect(tabButtons.some(t => t?.includes('Salas'))).toBe(true);
    expect(tabButtons.some(t => t?.includes('Catálogo'))).toBe(true);

    expect(gymApiSpy.getAdminUsers).toHaveBeenCalled();
    expect(gymApiSpy.getAuditLogs).toHaveBeenCalled();
  });

  it('for Secretary: should hide users and audit tabs, and NOT load sensitive data to avoid 403', async () => {
    await setupComponent(false);

    const compiled = fixture.nativeElement as HTMLElement;
    const tabButtons = Array.from(compiled.querySelectorAll('.nav-tab-btn')).map(b => b.textContent?.trim());

    expect(tabButtons.some(t => t?.includes('Usuarios'))).toBe(false);
    expect(tabButtons.some(t => t?.includes('Auditoría'))).toBe(false);
    expect(tabButtons.some(t => t?.includes('Salas'))).toBe(true);
    expect(tabButtons.some(t => t?.includes('Catálogo'))).toBe(true);

    expect(gymApiSpy.getAdminUsers).not.toHaveBeenCalled();
    expect(gymApiSpy.getAuditLogs).not.toHaveBeenCalled();
  });

  describe('Users tab actions', () => {
    beforeEach(async () => {
      await setupComponent(true);
      component.setActiveTab('users');
      fixture.detectChanges();
    });

    it('should render Ver Ficha and Roles buttons for each user row', () => {
      const viewFichaBtn1 = fixture.nativeElement.querySelector('[data-testid="btn-view-ficha-user-1"]');
      const manageRolesBtn1 = fixture.nativeElement.querySelector('[data-testid="btn-manage-roles-user-1"]');

      expect(viewFichaBtn1).not.toBeNull();
      expect(viewFichaBtn1.getAttribute('href')).toBe('/portal/personas/person-1');
      expect(manageRolesBtn1).not.toBeNull();

      const viewFichaBtn2 = fixture.nativeElement.querySelector('[data-testid="btn-view-ficha-user-2"]');
      expect(viewFichaBtn2.getAttribute('href')).toBe('/portal/personas/person-2');
    });

    it('should open Manage Roles modal when clicking Roles button', () => {
      const manageRolesBtn2 = fixture.nativeElement.querySelector('[data-testid="btn-manage-roles-user-2"]');
      manageRolesBtn2.click();
      fixture.detectChanges();

      expect(component.showRolesModal()).toBe(true);
      expect(component.selectedUserForRoles()).toEqual(mockUsers[1]);

      const modal = fixture.nativeElement.querySelector('[data-testid="admin-roles-modal"]');
      expect(modal).not.toBeNull();
      expect(modal.textContent).toContain('Laura Pausini');
    });

    it('should submit role updates and reload users on success', () => {
      component.openRolesModal(mockUsers[1]);
      component.userManageRoleIds[1] = true; // Add Administrator
      component.userManageRoleIds[3] = true; // Keep Instructor

      component.submitUserRoles();

      expect(userManagementSpy.updateRoles).toHaveBeenCalledWith('person-2', {
        roleIds: [1, 3]
      });
      expect(component.showRolesModal()).toBe(false);
      expect(gymApiSpy.getAdminUsers).toHaveBeenCalledTimes(2); // Initial + reload
    });

    it('should display conflict error (409) in modal when role update fails', () => {
      userManagementSpy.updateRoles.mockReturnValue(
        throwError(() => ({ error: { message: 'No se puede revocar el rol de Administrador porque no existen otros administradores operativos.' } }))
      );

      component.openRolesModal(mockUsers[0]);
      component.submitUserRoles();

      expect(component.showRolesModal()).toBe(true);
      expect(component.rolesModalError()).toContain('otros administradores');
    });

    it('should warn when administrator modifies own roles', () => {
      component.openRolesModal(mockUsers[0]); // user-1 is the current user
      fixture.detectChanges();

      expect(component.isSelfUser(mockUsers[0])).toBe(true);
      const warning = fixture.nativeElement.querySelector('[data-testid="admin-self-roles-warning"]');
      expect(warning).not.toBeNull();
    });
  });

  describe('notification template actions', () => {
    const templates = [
      { id: 't1', code: 'WELCOME', channel: 0, version: 1, locale: 'es', bodyTemplate: 'Hola', status: 1 }
    ];

    const templateActionButtons = (): string[] => {
      component.setActiveTab('notifications');
      fixture.detectChanges();
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button.template-action'))
        .map(b => b.textContent!.trim());
    };

    it('are visible for Administrator', async () => {
      notificationsServiceSpy.getTemplates.mockReturnValue(of(templates));
      await setupComponent(true);

      expect(templateActionButtons().length).toBe(1);
    });

    it('are hidden for Secretary, who can only read templates', async () => {
      notificationsServiceSpy.getTemplates.mockReturnValue(of(templates));
      await setupComponent(false);

      expect(templateActionButtons()).toEqual([]);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('WELCOME');
    });
  });

  it('for Secretary: if default or requested tab is admin-only, it falls back to first visible tab', async () => {
    authServiceSpy.isAdmin.mockReturnValue(false);

    await TestBed.configureTestingModule({
      imports: [AdminPortalComponent],
      providers: [
        provideRouter([]),
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationsService, useValue: notificationsServiceSpy },
        { provide: UserManagementService, useValue: userManagementSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminPortalComponent);
    component = fixture.componentInstance;
    component.activeTab.set('users' as any);
    fixture.detectChanges();

    expect(component.activeTab()).not.toBe('users');
    expect(component.activeTab()).not.toBe('audit');
  });

  it('should switch to rooms tab and render rooms management', async () => {
    await setupComponent(true);
    component.activeTab.set('rooms');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-rooms-management')).not.toBeNull();
  });

  it('should switch to catalog tab and render activities management by default', async () => {
    await setupComponent(true);
    component.activeTab.set('catalog');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-activities-management')).not.toBeNull();
  });
});
