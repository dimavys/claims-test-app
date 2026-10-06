import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { ReserveSubmission, ReserveTransaction, Reserves, SubmitReserveRequest } from '../models/api.models';
import { quietErrors } from './api-context';

@Injectable({ providedIn: 'root' })
export class ReservesApi {
  private readonly http = inject(HttpClient);
  private readonly root = `${inject(AppConfig).apiBaseUrl}/api/claims`;

  private url(claimId: string, suffix = ''): string {
    return `${this.root}/${claimId}/reserves${suffix}`;
  }

  get(claimId: string, context?: HttpContext): Observable<Reserves> {
    return this.http.get<Reserves>(this.url(claimId), { context });
  }

  /** Form errors for the slide-in panel are shown inline, so no toast here. */
  submit(claimId: string, request: SubmitReserveRequest): Observable<ReserveSubmission> {
    return this.http.post<ReserveSubmission>(this.url(claimId), request, { context: quietErrors() });
  }

  approve(claimId: string, txnId: string): Observable<ReserveTransaction> {
    return this.http.post<ReserveTransaction>(this.url(claimId, `/${txnId}/approve`), {});
  }

  reject(claimId: string, txnId: string, rejectionReason: string): Observable<ReserveTransaction> {
    return this.http.post<ReserveTransaction>(this.url(claimId, `/${txnId}/reject`), { rejectionReason });
  }

  retract(claimId: string, txnId: string): Observable<ReserveTransaction> {
    return this.http.post<ReserveTransaction>(this.url(claimId, `/${txnId}/retract`), {});
  }

  retryPosting(claimId: string, txnId: string): Observable<ReserveTransaction> {
    return this.http.post<ReserveTransaction>(this.url(claimId, `/${txnId}/retry-posting`), {});
  }
}
