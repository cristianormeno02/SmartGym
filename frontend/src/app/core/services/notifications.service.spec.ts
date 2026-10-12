import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { NotificationsService } from './notifications.service';
import {
  NotificationCategory,
  NotificationChannel,
  NotificationPreferenceItem,
  NotificationType
} from '../models/gym.models';

describe('NotificationsService', () => {
  let service: NotificationsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        NotificationsService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(NotificationsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads initial unreadCount and updates signal', () => {
    service.refreshUnreadCount().subscribe((res) => {
      expect(res.unreadCount).toBe(4);
    });

    const req = httpMock.expectOne('http://localhost:5000/api/notifications/unread-count');
    expect(req.request.method).toBe('GET');
    req.flush({ unreadCount: 4 });

    expect(service.unreadCount()).toBe(4);
  });

  it('marks individual notification as read and unread updating signals', () => {
    service.loadRecentNotifications().subscribe();

    const listReq = httpMock.expectOne(
      (r) => r.url === 'http://localhost:5000/api/notifications' && r.params.get('pageSize') === '8'
    );
    listReq.flush({
      items: [
        {
          id: 'notif-1',
          recipientPersonId: 'person-1',
          recipientName: 'Clara',
          type: NotificationType.ReservationConfirmed,
          category: NotificationCategory.Reservations,
          channel: NotificationChannel.InApp,
          title: 'Reserva confirmada',
          body: 'Tu turno de Funcional quedó confirmado.',
          isRead: false,
          sentAtUtc: '2026-10-12T12:00:00Z',
          actionUrl: '/portal/alumno'
        }
      ],
      pageNumber: 1,
      pageSize: 8,
      totalCount: 1,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false
    });

    service.unreadCount.set(1);

    // Mark as read
    service.markAsRead('notif-1').subscribe();
    const readReq = httpMock.expectOne('http://localhost:5000/api/notifications/notif-1/read');
    expect(readReq.request.method).toBe('PATCH');
    readReq.flush({
      id: 'notif-1',
      recipientPersonId: 'person-1',
      recipientName: 'Clara',
      type: NotificationType.ReservationConfirmed,
      category: NotificationCategory.Reservations,
      channel: NotificationChannel.InApp,
      title: 'Reserva confirmada',
      body: 'Tu turno de Funcional quedó confirmado.',
      isRead: true,
      sentAtUtc: '2026-10-12T12:00:00Z',
      readAtUtc: '2026-10-12T12:05:00Z',
      actionUrl: '/portal/alumno'
    });

    expect(service.unreadCount()).toBe(0);
    expect(service.recentNotifications()[0].isRead).toBe(true);
    expect(service.recentNotifications()[0].message).toBe('Tu turno de Funcional quedó confirmado.');

    // Mark as unread
    service.markAsUnread('notif-1').subscribe();
    const unreadReq = httpMock.expectOne('http://localhost:5000/api/notifications/notif-1/unread');
    expect(unreadReq.request.method).toBe('PATCH');
    unreadReq.flush({
      id: 'notif-1',
      recipientPersonId: 'person-1',
      recipientName: 'Clara',
      type: NotificationType.ReservationConfirmed,
      category: NotificationCategory.Reservations,
      channel: NotificationChannel.InApp,
      title: 'Reserva confirmada',
      body: 'Tu turno de Funcional quedó confirmado.',
      isRead: false,
      sentAtUtc: '2026-10-12T12:00:00Z',
      actionUrl: '/portal/alumno'
    });

    expect(service.unreadCount()).toBe(1);
    expect(service.recentNotifications()[0].isRead).toBe(false);
  });

  it('marks all notifications as read and resets unreadCount signal to 0', () => {
    service.unreadCount.set(3);
    service.recentNotifications.set([
      {
        id: 'n1',
        title: 'A',
        body: 'B1',
        message: 'B1',
        isRead: false,
        sentAtUtc: '2026-10-12T12:00:00Z',
        createdAtUtc: '2026-10-12T12:00:00Z'
      },
      {
        id: 'n2',
        title: 'B',
        body: 'B2',
        message: 'B2',
        isRead: false,
        sentAtUtc: '2026-10-12T12:01:00Z',
        createdAtUtc: '2026-10-12T12:01:00Z'
      }
    ]);

    service.markAllAsRead().subscribe((res) => {
      expect(res.markedCount).toBe(2);
    });

    const req = httpMock.expectOne('http://localhost:5000/api/notifications/mark-all-read');
    expect(req.request.method).toBe('POST');
    req.flush({ markedCount: 2 });

    expect(service.unreadCount()).toBe(0);
    expect(service.recentNotifications().every((n) => n.isRead)).toBe(true);
  });

  it('validates safe internal actionUrl and rejects external or script URLs', () => {
    expect(service.isSafeInternalUrl('/portal/alumno')).toBe(true);
    expect(service.isSafeInternalUrl('/portal/notificaciones')).toBe(true);
    expect(service.isSafeInternalUrl('https://evil.example.com/phish')).toBe(false);
    expect(service.isSafeInternalUrl('http://evil.example.com')).toBe(false);
    expect(service.isSafeInternalUrl('//evil.example.com')).toBe(false);
    expect(service.isSafeInternalUrl('javascript:alert(1)')).toBe(false);
    expect(service.isSafeInternalUrl('')).toBe(false);
  });

  it('loads and updates preferences distinguishing locked transactional channels from promotional ones', () => {
    const mockPrefs: NotificationPreferenceItem[] = [
      {
        category: NotificationCategory.Reservations,
        channel: NotificationChannel.InApp,
        isEnabled: true,
        isLocked: true,
        isTransactional: true
      },
      {
        category: NotificationCategory.Promotions,
        channel: NotificationChannel.Email,
        isEnabled: false,
        isLocked: false,
        isTransactional: false
      }
    ];

    service.getPreferences().subscribe((prefs) => {
      expect(prefs.length).toBe(2);
      expect(prefs[0].isLocked).toBe(true);
      expect(prefs[0].isTransactional).toBe(true);
      expect(prefs[1].isLocked).toBe(false);
      expect(prefs[1].isTransactional).toBe(false);
    });

    const getReq = httpMock.expectOne('http://localhost:5000/api/notifications/preferences');
    expect(getReq.request.method).toBe('GET');
    getReq.flush(mockPrefs);

    service
      .updatePreferences({
        preferences: [
          {
            category: NotificationCategory.Promotions,
            channel: NotificationChannel.Email,
            isEnabled: true
          }
        ]
      })
      .subscribe((updated) => {
        expect(updated[1].isEnabled).toBe(true);
      });

    const putReq = httpMock.expectOne('http://localhost:5000/api/notifications/preferences');
    expect(putReq.request.method).toBe('PUT');
    putReq.flush([
      mockPrefs[0],
      { ...mockPrefs[1], isEnabled: true }
    ]);
  });

  it('hides provider-not-configured failures by default when listing failed deliveries', () => {
    const emptyPage = {
      items: [],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
      hasPreviousPage: false,
      hasNextPage: false
    };
    const isFailedList = (r: { url: string }) =>
      r.url === 'http://localhost:5000/api/admin/notifications/deliveries/failed';

    service.getFailedDeliveries().subscribe();
    const hiddenReq = httpMock.expectOne(isFailedList);
    expect(hiddenReq.request.params.get('excludeProviderNotConfigured')).toBe('true');
    hiddenReq.flush(emptyPage);

    service.getFailedDeliveries(1, 20, true).subscribe();
    const allReq = httpMock.expectOne(isFailedList);
    expect(allReq.request.params.get('excludeProviderNotConfigured')).toBe('false');
    allReq.flush(emptyPage);
  });

  it('reset clears the unread count and cached notifications of the previous session', () => {
    service.unreadCount.set(5);
    service.recentNotifications.set([
      { id: 'notif-1', title: 'Reserva confirmada', body: 'Body', message: 'Body', isRead: false, sentAtUtc: '', createdAtUtc: '' }
    ]);

    service.reset();

    expect(service.unreadCount()).toBe(0);
    expect(service.recentNotifications()).toEqual([]);
  });

  it('sends paging and date filters with the query parameter names the API binds', () => {
    const emptyPage = {
      items: [],
      pageNumber: 2,
      pageSize: 15,
      totalCount: 0,
      totalPages: 0,
      hasPreviousPage: true,
      hasNextPage: false
    };

    service
      .getNotifications({ pageNumber: 2, pageSize: 15, from: '2026-10-01T00:00:00Z', to: '2026-10-12T00:00:00Z' })
      .subscribe();

    const listReq = httpMock.expectOne((r) => r.url === 'http://localhost:5000/api/notifications');
    expect(listReq.request.params.get('page')).toBe('2');
    expect(listReq.request.params.get('pageSize')).toBe('15');
    expect(listReq.request.params.get('fromUtc')).toBe('2026-10-01T00:00:00Z');
    expect(listReq.request.params.get('toUtc')).toBe('2026-10-12T00:00:00Z');
    expect(listReq.request.params.has('pageNumber')).toBe(false);
    listReq.flush(emptyPage);

    service.getFailedDeliveries(3, 20).subscribe();

    const failedReq = httpMock.expectOne(
      (r) => r.url === 'http://localhost:5000/api/admin/notifications/deliveries/failed'
    );
    expect(failedReq.request.params.get('page')).toBe('3');
    expect(failedReq.request.params.has('pageNumber')).toBe(false);
    failedReq.flush(emptyPage);
  });
});
