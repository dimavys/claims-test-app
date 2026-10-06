import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, throwError } from 'rxjs';
import { SKIP_ERROR_TOAST, SKIP_LOADING_BAR } from '../api/api-context';
import { Auth } from '../auth/auth';
import { AppConfig } from '../config/app-config';
import { ApiException, describeError, toApiError } from './api-error';
import { Loading } from './loading';
import { Notifier } from './notifier';

/** Adds the Bearer token to every call to our API (and only to our API). */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(Auth).token();
  const isApi = req.url.startsWith(inject(AppConfig).apiBaseUrl);
  return next(token && isApi ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req);
};

/** Drives the global progress bar from the number of requests in flight. */
export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(SKIP_LOADING_BAR)) return next(req);
  const loading = inject(Loading);
  loading.begin();
  return next(req).pipe(finalize(() => loading.end()));
};

/**
 * Normalises every failure into an ApiException and shows it once as a snackbar, unless the caller handles it inline.
 * A 401 on a protected call means the session is gone: sign out and go to the login screen.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notifier = inject(Notifier);
  const auth = inject(Auth);
  const router = inject(Router);

  return next(req).pipe(
    catchError((response: HttpErrorResponse) => {
      const error = toApiError(response);
      const isLogin = req.url.endsWith('/api/auth/login');

      if (error.status === 401 && !isLogin) {
        const wasSignedIn = auth.isAuthenticated();
        auth.logout(false);
        if (wasSignedIn || !router.url.startsWith('/login')) {
          notifier.warn('Your session has expired. Please sign in again.');
          void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
        }
      } else if (!req.context.get(SKIP_ERROR_TOAST)) {
        const message = describeError(error);
        if (error.status >= 500 || error.status === 0) notifier.error(message);
        else notifier.warn(message);
      }

      return throwError(() => new ApiException(error));
    }),
  );
};
