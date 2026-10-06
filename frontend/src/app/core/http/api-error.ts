import { HttpErrorResponse } from '@angular/common/http';
import { ApiError } from '../models/api.models';

/** An API failure normalised to the server's error shape, so every caller handles one type. */
export class ApiException extends Error {
  constructor(readonly error: ApiError) {
    super(error.title);
  }

  get status(): number { return this.error.status; }
  get fieldErrors(): Record<string, string[]> { return this.error.errors ?? {}; }
  get blockingConditions(): string[] { return this.error.blockingConditions ?? []; }

  /** Every message the server attached, flattened (field errors first). */
  get allMessages(): string[] {
    const fields = Object.values(this.fieldErrors).flat();
    return [...fields, ...this.blockingConditions].filter((m, i, all) => m && all.indexOf(m) === i);
  }
}

export function toApiError(response: HttpErrorResponse): ApiError {
  const body = response.error;
  if (body && typeof body === 'object' && typeof body.title === 'string' && typeof body.status === 'number') {
    return body as ApiError;
  }

  if (response.status === 0) {
    return { type: 'Network', title: 'Cannot reach the server. Check your connection and try again.', status: 0 };
  }

  return { type: 'ServerError', title: response.statusText || 'The request failed.', status: response.status };
}

/** Text for the snackbar: the title, plus up to three specific messages. */
export function describeError(error: ApiError): string {
  const details = [
    ...Object.values(error.errors ?? {}).flat(),
    ...(error.blockingConditions ?? []),
  ].filter((m, i, all) => m && m !== error.title && all.indexOf(m) === i);

  if (details.length === 0) return error.title;
  const shown = details.slice(0, 3).join(' · ');
  return `${error.title} ${shown}${details.length > 3 ? ` (+${details.length - 3} more)` : ''}`;
}
