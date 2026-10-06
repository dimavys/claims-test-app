import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppConfig } from '../config/app-config';
import { LoginResponse, RoleCode } from '../models/api.models';
import { Auth } from './auth';

const API = 'http://api.test';
const KEY = 'claims.session';

const response = (role: RoleCode, expiresInMinutes = 60): LoginResponse => ({
  accessToken: `token-${role}`,
  expiresAt: new Date(Date.now() + expiresInMinutes * 60_000).toISOString(),
  user: { id: `id-${role}`, userName: role, displayName: role.toUpperCase(), role },
});

function setup(stored?: LoginResponse) {
  sessionStorage.clear();
  if (stored) {
    sessionStorage.setItem(KEY, JSON.stringify({ token: stored.accessToken, expiresAt: stored.expiresAt, user: stored.user }));
  }
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: AppConfig, useValue: { apiBaseUrl: API } }],
  });
  return { auth: TestBed.inject(Auth), http: TestBed.inject(HttpTestingController), router: TestBed.inject(Router) };
}

describe('Auth', () => {
  beforeEach(() => sessionStorage.clear());

  it('starts signed out with no stored session', () => {
    const { auth } = setup();
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.user()).toBeNull();
    expect(auth.canApprove()).toBe(false);
  });

  it('login stores the session and exposes role-based flags', () => {
    const { auth, http } = setup();
    auth.login('supervisor', 'pw').subscribe();
    const req = http.expectOne(`${API}/api/auth/login`);
    expect(req.request.body).toEqual({ userName: 'supervisor', password: 'pw' });
    req.flush(response('supervisor'));

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.token()).toBe('token-supervisor');
    expect(auth.role()).toBe('supervisor');
    expect(auth.canApprove()).toBe(true);
    expect(auth.isManager()).toBe(false);
    expect(JSON.parse(sessionStorage.getItem(KEY)!).token).toBe('token-supervisor');
  });

  it('only supervisors and managers may approve; only managers are managers', () => {
    for (const [role, approve, manager] of [['handler', false, false], ['supervisor', true, false], ['manager', true, true]] as const) {
      const { auth, http } = setup();
      auth.switchTo(role).subscribe();
      http.expectOne(`${API}/api/auth/dev-token`).flush(response(role));
      expect([role, auth.canApprove(), auth.isManager()]).toEqual([role, approve, manager]);
    }
  });

  it('restores a valid session after a reload', () => {
    const { auth } = setup(response('handler'));
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.user()?.displayName).toBe('HANDLER');
  });

  it('ignores an expired stored session', () => {
    const { auth } = setup(response('handler', -5));
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('ignores a corrupted stored session', () => {
    sessionStorage.setItem(KEY, '{not json');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: AppConfig, useValue: { apiBaseUrl: API } }] });
    expect(TestBed.inject(Auth).isAuthenticated()).toBe(false);
  });

  it('switching role replaces the session', () => {
    const { auth, http } = setup(response('handler'));
    auth.switchTo('manager').subscribe();
    http.expectOne(`${API}/api/auth/dev-token`).flush(response('manager'));
    expect(auth.role()).toBe('manager');
    expect(auth.token()).toBe('token-manager');
  });

  it('logout clears the session and navigates to login', () => {
    const { auth, router } = setup(response('manager'));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    auth.logout();

    expect(auth.isAuthenticated()).toBe(false);
    expect(sessionStorage.getItem(KEY)).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('loads the role-switcher users, tolerating a disabled endpoint', () => {
    const { auth, http } = setup();
    auth.loadSwitchableUsers();
    http.expectOne(`${API}/api/auth/users`).flush('', { status: 404, statusText: 'Not Found' });
    expect(auth.switchableUsers()).toEqual([]);

    auth.loadSwitchableUsers();
    http.expectOne(`${API}/api/auth/users`).flush([response('handler').user]);
    expect(auth.switchableUsers()).toHaveLength(1);
  });
});
