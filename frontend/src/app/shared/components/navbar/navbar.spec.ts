import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { signal } from '@angular/core';
import { NavbarComponent } from './navbar';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationsService } from '../../../core/services/notifications.service';
import { of } from 'rxjs';
import { NotificationItem } from '../../../core/models/gym.models';

describe('NavbarComponent — Notifications Bell & Dropdown', () => {
  let component: NavbarComponent;
  let fixture: ComponentFixture<NavbarComponent>;
  let router: Router;

  const isAuthenticatedSignal = signal(true);
  const unreadCountSignal = signal(3);
  const recentNotificationsSignal = signal<NotificationItem[]>([
    {
      id: 'notif-1',
      title: 'Reserva confirmada',
      body: 'Tu reserva de Funcional está confirmada.',
      message: 'Tu reserva de Funcional está confirmada.',
      isRead: false,
      sentAtUtc: '2026-04-12T10:00:00Z',
      createdAtUtc: '2026-04-12T10:00:00Z',
      actionUrl: '/portal/alumno'
    }
  ]);

  const mockAuthService = {
    isAuthenticated: isAuthenticatedSignal,
    isStaff: signal(false),
    isStudent: signal(true),
    isInstructor: signal(false),
    isSecretary: signal(false),
    isAdmin: signal(false),
    currentUser: signal({ fullName: 'Juan Pérez', photoUrl: null }),
    userRoles: signal(['Student']),
    logout: () => {}
  };

  let refreshCalls = 0;
  let resetCalls = 0;

  const mockNotificationsService = {
    unreadCount: unreadCountSignal,
    recentNotifications: recentNotificationsSignal,
    loading: signal(false),
    refreshUnreadCount: () => {
      refreshCalls++;
      return of(3);
    },
    reset: () => {
      resetCalls++;
    },
    loadRecentNotifications: () => of(recentNotificationsSignal()),
    markAsRead: (id: string) => {
      recentNotificationsSignal.update(items =>
        items.map(item => (item.id === id ? { ...item, isRead: true } : item))
      );
      unreadCountSignal.update(c => Math.max(0, c - 1));
      return of(void 0);
    },
    markAllAsRead: () => {
      recentNotificationsSignal.update(items => items.map(item => ({ ...item, isRead: true })));
      unreadCountSignal.set(0);
      return of(1);
    },
    isSafeInternalUrl: (url?: string | null) =>
      !!url && url.startsWith('/') && !url.startsWith('//') && !url.includes('://')
  };

  beforeEach(async () => {
    isAuthenticatedSignal.set(true);
    refreshCalls = 0;
    resetCalls = 0;
    unreadCountSignal.set(3);
    recentNotificationsSignal.set([
      {
        id: 'notif-1',
        title: 'Reserva confirmada',
        body: 'Tu reserva de Funcional está confirmada.',
        message: 'Tu reserva de Funcional está confirmada.',
        isRead: false,
        sentAtUtc: '2026-04-12T10:00:00Z',
        createdAtUtc: '2026-04-12T10:00:00Z',
        actionUrl: '/portal/alumno'
      }
    ]);

    await TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: mockAuthService },
        { provide: NotificationsService, useValue: mockNotificationsService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(NavbarComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  it('should display unread badge when unreadCount > 0', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const badge = compiled.querySelector('[data-testid="notifications-unread-badge"]');
    expect(badge).toBeTruthy();
    expect(badge?.textContent?.trim()).toBe('3');
  });

  it('should hide unread badge when unreadCount is 0', () => {
    unreadCountSignal.set(0);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const badge = compiled.querySelector('[data-testid="notifications-unread-badge"]');
    expect(badge).toBeNull();
  });

  it('should toggle notifications dropdown and allow marking all as read', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    const bellBtn = compiled.querySelector('[data-testid="notifications-bell-btn"]') as HTMLButtonElement;
    expect(bellBtn).toBeTruthy();

    bellBtn.click();
    fixture.detectChanges();

    const dropdown = compiled.querySelector('[data-testid="notifications-dropdown"]');
    expect(dropdown).toBeTruthy();
    expect(dropdown?.textContent).toContain('Reserva confirmada');

    const markAllBtn = compiled.querySelector('[data-testid="notifications-mark-all-btn"]') as HTMLButtonElement;
    expect(markAllBtn).toBeTruthy();
    markAllBtn.click();
    fixture.detectChanges();

    expect(unreadCountSignal()).toBe(0);
  });

  it('should navigate only when actionUrl is a safe internal route', () => {
    let navigatedUrl: string | null = null;
    const originalNavigateByUrl = router.navigateByUrl.bind(router);
    router.navigateByUrl = ((url: string) => {
      navigatedUrl = url;
      return Promise.resolve(true);
    }) as typeof router.navigateByUrl;

    component.openNotification({
      id: 'notif-1',
      title: 'Reserva confirmada',
      body: 'Body',
      message: 'Body',
      isRead: false,
      sentAtUtc: '2026-04-12T10:00:00Z',
      createdAtUtc: '2026-04-12T10:00:00Z',
      actionUrl: '/portal/alumno'
    });
    expect(navigatedUrl).toBe('/portal/alumno');

    navigatedUrl = null;
    component.openNotification({
      id: 'notif-2',
      title: 'Phishing attempt',
      body: 'Body',
      message: 'Body',
      isRead: false,
      sentAtUtc: '2026-04-12T10:00:00Z',
      createdAtUtc: '2026-04-12T10:00:00Z',
      actionUrl: 'https://evil.example.com'
    });
    expect(navigatedUrl).toBeNull();
    router.navigateByUrl = originalNavigateByUrl;
  });

  it('should clear notification state on logout and reload the unread count on the next login', () => {
    expect(refreshCalls).toBe(1);
    component.toggleDropdown();
    fixture.detectChanges();

    isAuthenticatedSignal.set(false);
    fixture.detectChanges();

    expect(resetCalls).toBe(2);
    expect(refreshCalls).toBe(1);
    expect(component.isDropdownOpen()).toBe(false);

    isAuthenticatedSignal.set(true);
    fixture.detectChanges();

    expect(refreshCalls).toBe(2);
  });
});
