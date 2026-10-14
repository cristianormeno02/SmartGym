import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminPortalComponent } from './admin-portal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationsService } from '../../../core/services/notifications.service';
import { of } from 'rxjs';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('AdminPortalComponent', () => {
  let component: AdminPortalComponent;
  let fixture: ComponentFixture<AdminPortalComponent>;
  let gymApiSpy: any;
  let authServiceSpy: any;
  let notificationsServiceSpy: any;

  beforeEach(() => {
    gymApiSpy = {
      getDashboardMetrics: vi.fn().mockReturnValue(of(null)),
      getAdminUsers: vi.fn().mockReturnValue(of([])),
      getPromotions: vi.fn().mockReturnValue(of([])),
      getAuditLogs: vi.fn().mockReturnValue(of([])),
      getRooms: vi.fn().mockReturnValue(of([])),
      getActivities: vi.fn().mockReturnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 10, totalPages: 0, hasPreviousPage: false, hasNextPage: false }))
    };

    authServiceSpy = {
      isAdmin: vi.fn().mockReturnValue(true),
      isStaff: vi.fn().mockReturnValue(true),
      hasAnyRole: vi.fn().mockReturnValue(true)
    };

    notificationsServiceSpy = {
      getFailedDeliveries: vi.fn().mockReturnValue(of([])),
      getTemplates: vi.fn().mockReturnValue(of([]))
    };
  });

  const setupComponent = async (isAdmin: boolean) => {
    authServiceSpy.isAdmin.mockReturnValue(isAdmin);

    await TestBed.configureTestingModule({
      imports: [AdminPortalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationsService, useValue: notificationsServiceSpy }
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
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationsService, useValue: notificationsServiceSpy }
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
