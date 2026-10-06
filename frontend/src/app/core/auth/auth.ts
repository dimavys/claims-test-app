import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, of, tap } from 'rxjs';
import { quietErrors } from '../api/api-context';
import { AppConfig } from '../config/app-config';
import { LoginResponse, RoleCode, UserInfo } from '../models/api.models';
import { roleRank } from '../util/domain';

interface Session {
  token: string;
  expiresAt: string;
  user: UserInfo;
}

const STORAGE_KEY = 'claims.session';

/**
 * Mock authentication: the API issues real JWTs for a few hard-coded users. The session lives in sessionStorage
 * (cleared when the tab closes) and the signal-based state drives role-aware UI everywhere.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly root = `${inject(AppConfig).apiBaseUrl}/api/auth`;

  private readonly session = signal<Session | null>(this.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly role = computed<RoleCode | null>(() => this.user()?.role ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null && !this.isExpired(this.session()!));

  /** Supervisors and managers may approve or reject reserves (FRS §3). */
  readonly canApprove = computed(() => roleRank(this.role()) >= roleRank('supervisor'));
  readonly isManager = computed(() => this.role() === 'manager');

  /** Users offered by the role switcher; empty when the API has it disabled. */
  readonly switchableUsers = signal<UserInfo[]>([]);

  login(userName: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.root}/login`, { userName, password }, { context: quietErrors() })
      .pipe(tap(r => this.start(r)));
  }

  /** Role switcher (testing only): act as another configured user without a password. */
  switchTo(userName: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.root}/dev-token`, { userName }).pipe(tap(r => this.start(r)));
  }

  loadSwitchableUsers(): void {
    this.http.get<UserInfo[]>(`${this.root}/users`, { context: quietErrors() })
      .pipe(catchError(() => of([] as UserInfo[])))
      .subscribe(users => this.switchableUsers.set(users));
  }

  logout(redirect = true): void {
    this.session.set(null);
    this.storage()?.removeItem(STORAGE_KEY);
    if (redirect) void this.router.navigate(['/login']);
  }

  private start(response: LoginResponse): void {
    const session: Session = { token: response.accessToken, expiresAt: response.expiresAt, user: response.user };
    this.session.set(session);
    this.storage()?.setItem(STORAGE_KEY, JSON.stringify(session));
  }

  private restore(): Session | null {
    try {
      const raw = this.storage()?.getItem(STORAGE_KEY);
      const session = raw ? (JSON.parse(raw) as Session) : null;
      return session && !this.isExpired(session) ? session : null;
    } catch {
      return null;
    }
  }

  private isExpired(session: Session): boolean {
    return new Date(session.expiresAt).getTime() <= Date.now();
  }

  private storage(): Storage | null {
    try { return typeof sessionStorage === 'undefined' ? null : sessionStorage; } catch { return null; }
  }
}
