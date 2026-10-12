import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap, map } from 'rxjs';
import {
  CreateNotificationTemplateRequest,
  MarkAllNotificationsReadResponse,
  NotificationCategory,
  NotificationChannel,
  NotificationDeliveryItem,
  NotificationItem,
  NotificationPreferenceItem,
  NotificationTemplateItem,
  NotificationTemplateStatus,
  NotificationType,
  PagedResult,
  UnreadCountResponse,
  UpdateNotificationPreferencesRequest,
  UpdateNotificationTemplateRequest
} from '../models/gym.models';

export interface NotificationFilter {
  isRead?: boolean;
  category?: NotificationCategory;
  from?: string;
  to?: string;
  pageNumber?: number;
  pageSize?: number;
}

@Injectable({
  providedIn: 'root'
})
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5000/api';

  readonly unreadCount = signal<number>(0);
  readonly recentNotifications = signal<NotificationItem[]>([]);
  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  static normalizeNotification(raw: Partial<NotificationItem> & { id: string; title: string; isRead: boolean }): NotificationItem {
    const bodyText = raw.body ?? raw.message ?? '';
    const sentAt = raw.sentAtUtc ?? raw.createdAtUtc ?? new Date().toISOString();
    return {
      ...raw,
      body: bodyText,
      message: bodyText,
      sentAtUtc: sentAt,
      createdAtUtc: sentAt
    };
  }

  reset(): void {
    this.unreadCount.set(0);
    this.recentNotifications.set([]);
    this.error.set(null);
  }

  refreshUnreadCount(): Observable<UnreadCountResponse> {
    return this.http.get<UnreadCountResponse>(`${this.apiUrl}/notifications/unread-count`).pipe(
      tap((res) => this.unreadCount.set(res.unreadCount ?? 0))
    );
  }

  loadRecentNotifications(pageSize = 8): Observable<NotificationItem[]> {
    const params = new HttpParams()
      .set('page', '1')
      .set('pageSize', pageSize.toString());

    return this.http.get<PagedResult<NotificationItem>>(`${this.apiUrl}/notifications`, { params }).pipe(
      map((res) => (res.items ?? []).map((item) => NotificationsService.normalizeNotification(item))),
      tap((items) => this.recentNotifications.set(items))
    );
  }

  getNotifications(filter: NotificationFilter = {}): Observable<PagedResult<NotificationItem>> {
    let params = new HttpParams();
    if (filter.isRead !== undefined) params = params.set('isRead', String(filter.isRead));
    if (filter.category !== undefined) params = params.set('category', String(filter.category));
    if (filter.from) params = params.set('fromUtc', filter.from);
    if (filter.to) params = params.set('toUtc', filter.to);
    if (filter.pageNumber !== undefined) params = params.set('page', String(filter.pageNumber));
    if (filter.pageSize !== undefined) params = params.set('pageSize', String(filter.pageSize));

    return this.http.get<PagedResult<NotificationItem>>(`${this.apiUrl}/notifications`, { params }).pipe(
      map((res) => ({
        ...res,
        items: (res.items ?? []).map((item) => NotificationsService.normalizeNotification(item))
      }))
    );
  }

  markAsRead(id: string): Observable<NotificationItem> {
    return this.http.patch<NotificationItem>(`${this.apiUrl}/notifications/${id}/read`, {}).pipe(
      map((item) => NotificationsService.normalizeNotification(item)),
      tap((updated) => {
        const current = this.recentNotifications();
        const existing = current.find((n) => n.id === id);
        if (!existing || !existing.isRead) {
          this.unreadCount.update((count) => Math.max(0, count - 1));
        }
        this.recentNotifications.update((items) =>
          items.map((n) => (n.id === id ? updated : n))
        );
      })
    );
  }

  markAsUnread(id: string): Observable<NotificationItem> {
    return this.http.patch<NotificationItem>(`${this.apiUrl}/notifications/${id}/unread`, {}).pipe(
      map((item) => NotificationsService.normalizeNotification(item)),
      tap((updated) => {
        const current = this.recentNotifications();
        const existing = current.find((n) => n.id === id);
        if (!existing || existing.isRead) {
          this.unreadCount.update((count) => count + 1);
        }
        this.recentNotifications.update((items) =>
          items.map((n) => (n.id === id ? updated : n))
        );
      })
    );
  }

  markAllAsRead(): Observable<MarkAllNotificationsReadResponse> {
    return this.http.post<MarkAllNotificationsReadResponse>(`${this.apiUrl}/notifications/mark-all-read`, {}).pipe(
      tap(() => {
        this.unreadCount.set(0);
        this.recentNotifications.update((items) =>
          items.map((n) => ({ ...n, isRead: true }))
        );
      })
    );
  }

  getPreferences(): Observable<NotificationPreferenceItem[]> {
    return this.http.get<NotificationPreferenceItem[]>(`${this.apiUrl}/notifications/preferences`);
  }

  updatePreferences(request: UpdateNotificationPreferencesRequest): Observable<NotificationPreferenceItem[]> {
    return this.http.put<NotificationPreferenceItem[]>(`${this.apiUrl}/notifications/preferences`, request);
  }

  isSafeInternalUrl(url?: string | null): boolean {
    if (!url || typeof url !== 'string') {
      return false;
    }
    const trimmed = url.trim();
    if (!trimmed.startsWith('/') || trimmed.startsWith('//')) {
      return false;
    }
    if (trimmed.includes('://') || trimmed.toLowerCase().startsWith('javascript:')) {
      return false;
    }
    return true;
  }

  // --- Administración de Entregas y Plantillas ---

  getFailedDeliveries(
    pageNumber = 1,
    pageSize = 20,
    includeProviderNotConfigured = false
  ): Observable<PagedResult<NotificationDeliveryItem>> {
    const params = new HttpParams()
      .set('page', String(pageNumber))
      .set('pageSize', String(pageSize))
      .set('excludeProviderNotConfigured', String(!includeProviderNotConfigured));
    return this.http.get<PagedResult<NotificationDeliveryItem>>(`${this.apiUrl}/admin/notifications/deliveries/failed`, { params });
  }

  retryDelivery(deliveryId: string, reason: string): Observable<NotificationDeliveryItem> {
    return this.http.post<NotificationDeliveryItem>(
      `${this.apiUrl}/admin/notifications/deliveries/${deliveryId}/retry`,
      { reason }
    );
  }

  getTemplates(filters?: {
    type?: NotificationType;
    channel?: NotificationChannel;
    status?: NotificationTemplateStatus;
  }): Observable<NotificationTemplateItem[]> {
    let params = new HttpParams();
    if (filters?.type !== undefined) params = params.set('type', String(filters.type));
    if (filters?.channel !== undefined) params = params.set('channel', String(filters.channel));
    if (filters?.status !== undefined) params = params.set('status', String(filters.status));
    return this.http.get<NotificationTemplateItem[]>(`${this.apiUrl}/admin/notification-templates`, { params });
  }

  createTemplate(request: CreateNotificationTemplateRequest): Observable<NotificationTemplateItem> {
    return this.http.post<NotificationTemplateItem>(`${this.apiUrl}/admin/notification-templates`, request);
  }

  updateTemplate(id: string, request: UpdateNotificationTemplateRequest): Observable<NotificationTemplateItem> {
    return this.http.put<NotificationTemplateItem>(`${this.apiUrl}/admin/notification-templates/${id}`, request);
  }

  activateTemplate(id: string): Observable<NotificationTemplateItem> {
    return this.http.post<NotificationTemplateItem>(`${this.apiUrl}/admin/notification-templates/${id}/activate`, {});
  }

  deactivateTemplate(id: string): Observable<NotificationTemplateItem> {
    return this.http.post<NotificationTemplateItem>(`${this.apiUrl}/admin/notification-templates/${id}/deactivate`, {});
  }
}
