import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap, catchError, of } from 'rxjs';
import { AuthResponse, LoginRequest, RegisterRequest, GoogleAuthRequest, User } from '../models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiUrl = 'http://localhost:5000/api';

  readonly token = signal<string | null>(this.getStoredToken());
  readonly currentUser = signal<User | null>(null);

  readonly isAuthenticated = computed(() => !!this.token());
  readonly userRoles = computed(() => this.currentUser()?.roles ?? []);

  readonly isStudent = computed(() => this.hasRole('Student'));
  readonly isInstructor = computed(() => this.hasRole('Instructor'));
  readonly isSecretary = computed(() => this.hasRole('Secretary'));
  readonly isAdmin = computed(() => this.hasRole('Administrator'));
  readonly isStaff = computed(() => this.isAdmin() || this.isSecretary());

  constructor() {
    if (this.token()) {
      this.loadCurrentUser().subscribe();
    }
  }

  login(credentials: { email: string; password: string }): Observable<AuthResponse> {
    const request: LoginRequest = {
      email: credentials.email,
      passwordHash: credentials.password
    };

    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/login`, request).pipe(
      tap((res) => this.handleAuthSuccess(res))
    );
  }

  register(data: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/register`, data).pipe(
      tap((res) => this.handleAuthSuccess(res))
    );
  }

  loginWithGoogle(idToken: string): Observable<AuthResponse> {
    const request: GoogleAuthRequest = { idToken };
    return this.http.post<AuthResponse>(`${this.apiUrl}/auth/google`, request).pipe(
      tap((res) => this.handleAuthSuccess(res))
    );
  }

  logout(): void {
    if (typeof window !== 'undefined') {
      localStorage.removeItem('smartgym_token');
    }
    this.token.set(null);
    this.currentUser.set(null);
    this.router.navigate(['/login']);
  }

  loadCurrentUser(): Observable<User | null> {
    if (!this.token()) {
      return of(null);
    }

    return this.http.get<User>(`${this.apiUrl}/users/me`).pipe(
      tap((user) => this.currentUser.set(user)),
      catchError(() => {
        this.logout();
        return of(null);
      })
    );
  }

  hasRole(role: string): boolean {
    return this.userRoles().includes(role);
  }

  hasAnyRole(roles: string[]): boolean {
    return roles.some((r) => this.hasRole(r));
  }

  private handleAuthSuccess(res: AuthResponse): void {
    if (typeof window !== 'undefined') {
      localStorage.setItem('smartgym_token', res.token);
    }
    this.token.set(res.token);
    this.currentUser.set({
      userId: res.userId,
      personId: res.personId,
      fullName: res.fullName,
      email: res.email,
      roles: res.roles
    });
  }

  private getStoredToken(): string | null {
    if (typeof window !== 'undefined') {
      return localStorage.getItem('smartgym_token');
    }
    return null;
  }
}
