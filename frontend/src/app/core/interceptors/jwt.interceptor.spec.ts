import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { vi, describe, beforeEach, afterEach, it, expect } from 'vitest';
import { jwtInterceptor } from './jwt.interceptor';
import { AuthService } from '../services/auth.service';

describe('jwtInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let authServiceSpy: { token: ReturnType<typeof signal<string | null>>; logout: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    authServiceSpy = {
      token: signal<string | null>(null),
      logout: vi.fn()
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting()
      ]
    });

    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should not add Authorization header if token is null', () => {
    authServiceSpy.token.set(null);

    httpClient.get('/api/test').subscribe();

    const req = httpMock.expectOne('/api/test');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('should add Authorization header if token exists', () => {
    authServiceSpy.token.set('valid-token-123');

    httpClient.get('/api/test').subscribe();

    const req = httpMock.expectOne('/api/test');
    expect(req.request.headers.get('Authorization')).toBe('Bearer valid-token-123');
    req.flush({});
  });

  it('should call authService.logout() on 401 when an existing session was present', () => {
    authServiceSpy.token.set('existing-token');

    httpClient.get('/api/test').subscribe({
      error: (err) => {
        expect(err.status).toBe(401);
      }
    });

    const req = httpMock.expectOne('/api/test');
    req.flush({ message: 'Token invalid' }, { status: 401, statusText: 'Unauthorized' });

    expect(authServiceSpy.logout).toHaveBeenCalled();
  });

  it('should NOT call authService.logout() on 401 when no session was present', () => {
    authServiceSpy.token.set(null);

    httpClient.post('/api/auth/login', {}).subscribe({
      error: (err) => {
        expect(err.status).toBe(401);
      }
    });

    const req = httpMock.expectOne('/api/auth/login');
    req.flush({ message: 'Bad credentials' }, { status: 401, statusText: 'Unauthorized' });

    expect(authServiceSpy.logout).not.toHaveBeenCalled();
  });
});
