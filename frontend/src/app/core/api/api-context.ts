import { HttpContext, HttpContextToken } from '@angular/common/http';

/** Set on requests whose failures the caller shows itself (e.g. inline form errors), to suppress the global snackbar. */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/** Set on background refreshes that should not drive the global progress bar. */
export const SKIP_LOADING_BAR = new HttpContextToken<boolean>(() => false);

export const quietErrors = () => new HttpContext().set(SKIP_ERROR_TOAST, true);
export const silent = () => new HttpContext().set(SKIP_ERROR_TOAST, true).set(SKIP_LOADING_BAR, true);
