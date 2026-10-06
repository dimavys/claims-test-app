import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SKIP_ERROR_TOAST, SKIP_LOADING_BAR } from '../api/api-context';
import { Auth } from '../auth/auth';
import { AppConfig } from '../config/app-config';
import { ApiException } from './api-error';
import { authInterceptor, errorInterceptor, loadingInterceptor } from './interceptors';
import { Loading } from './loading';
import { Notifier } from './notifier';
import { HttpContext } from '@angular/common/http';

const API = 'http://api.test';

describe('HTTP interceptors', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let notifier: { success: ReturnType<typeof vi.fn>; info: ReturnType<typeof vi.fn>; warn: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let token: string | null;
  let auth: { token: () => string | null; isAuthenticated: () => boolean; logout: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    token = 'jwt-token';
    notifier = { success: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() };
    auth = { token: () => token, isAuthenticated: () => token !== null, logout: vi.fn(() => { token = null; }) };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, loadingInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        { provide: AppConfig, useValue: { apiBaseUrl: API } },
        { provide: Notifier, useValue: notifier },
        { provide: Auth, useValue: auth },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  describe('authInterceptor', () => {
    it('adds the bearer token to calls to our API', () => {
      http.get(`${API}/api/claims`).subscribe();
      const req = controller.expectOne(`${API}/api/claims`);
      expect(req.request.headers.get('Authorization')).toBe('Bearer jwt-token');
      req.flush([]);
    });

    it('does not leak the token to other hosts', () => {
      http.get('https://other.example/x').subscribe();
      const req = controller.expectOne('https://other.example/x');
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush({});
    });

    it('sends no header when signed out', () => {
      token = null;
      http.get(`${API}/api/claims`).subscribe();
      const req = controller.expectOne(`${API}/api/claims`);
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush([]);
    });
  });

  describe('loadingInterceptor', () => {
    it('is active while a request is in flight and clears afterwards, even on failure', () => {
      const loading = TestBed.inject(Loading);
      http.get(`${API}/a`).subscribe({ error: () => undefined });
      http.get(`${API}/b`).subscribe();
      expect(loading.active()).toBe(true);

      controller.expectOne(`${API}/a`).flush({}, { status: 500, statusText: 'x' });
      expect(loading.active()).toBe(true);
      controller.expectOne(`${API}/b`).flush({});
      expect(loading.active()).toBe(false);
    });

    it('ignores background requests that opt out', () => {
      const loading = TestBed.inject(Loading);
      http.get(`${API}/poll`, { context: new HttpContext().set(SKIP_LOADING_BAR, true) }).subscribe();
      expect(loading.active()).toBe(false);
      controller.expectOne(`${API}/poll`).flush({});
    });
  });

  describe('errorInterceptor', () => {
    const validation = { type: 'ValidationError', title: 'One or more validation errors occurred.', status: 422, errors: { LossDate: ['Loss date cannot be in the future.'] } };

    it('turns failures into ApiException and shows a warning snackbar for client errors', () => {
      let caught: unknown;
      http.post(`${API}/api/claims`, {}).subscribe({ error: e => (caught = e) });
      controller.expectOne(`${API}/api/claims`).flush(validation, { status: 422, statusText: 'Unprocessable' });

      expect(caught).toBeInstanceOf(ApiException);
      expect((caught as ApiException).fieldErrors['LossDate']).toEqual(['Loss date cannot be in the future.']);
      expect(notifier.warn).toHaveBeenCalledOnce();
      expect(notifier.warn.mock.calls[0][0]).toContain('Loss date cannot be in the future.');
      expect(notifier.error).not.toHaveBeenCalled();
    });

    it('shows an error snackbar for server failures and network loss', () => {
      http.get(`${API}/a`).subscribe({ error: () => undefined });
      controller.expectOne(`${API}/a`).flush({ type: 'ServerError', title: 'An unexpected error occurred.', status: 500 }, { status: 500, statusText: 'x' });
      http.get(`${API}/b`).subscribe({ error: () => undefined });
      controller.expectOne(`${API}/b`).error(new ProgressEvent('error'), { status: 0 });

      expect(notifier.error).toHaveBeenCalledTimes(2);
      expect(notifier.error.mock.calls[1][0]).toMatch(/cannot reach the server/i);
    });

    it('stays quiet when the caller shows the error itself', () => {
      let caught: unknown;
      http.post(`${API}/x`, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) }).subscribe({ error: e => (caught = e) });
      controller.expectOne(`${API}/x`).flush(validation, { status: 422, statusText: 'x' });

      expect(caught).toBeInstanceOf(ApiException);
      expect(notifier.warn).not.toHaveBeenCalled();
      expect(notifier.error).not.toHaveBeenCalled();
    });

    it('signs out and redirects to login on a 401 from a protected call', () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      http.get(`${API}/api/claims`).subscribe({ error: () => undefined });
      controller.expectOne(`${API}/api/claims`).flush({ type: 'Unauthorized', title: 'Authentication is required.', status: 401 }, { status: 401, statusText: 'x' });

      expect(auth.logout).toHaveBeenCalledWith(false);
      expect(navigate).toHaveBeenCalledWith(['/login'], expect.anything());
      expect(notifier.warn).toHaveBeenCalledWith(expect.stringMatching(/session has expired/i));
    });

    it('does not treat a failed login as an expired session', () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      http.post(`${API}/api/auth/login`, {}).subscribe({ error: () => undefined });
      controller.expectOne(`${API}/api/auth/login`).flush({ type: 'Unauthorized', title: 'Invalid user name or password.', status: 401 }, { status: 401, statusText: 'x' });

      expect(auth.logout).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
    });
  });
});
