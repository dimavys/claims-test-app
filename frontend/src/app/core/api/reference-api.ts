import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { CauseOfLossCode, ClaimStatusInfo, PerilCategory, Policy } from '../models/api.models';
import { silent } from './api-context';

@Injectable({ providedIn: 'root' })
export class ReferenceApi {
  private readonly http = inject(HttpClient);
  private readonly root = inject(AppConfig).apiBaseUrl;

  // Reference data rarely changes: fetch once per session and share the result.
  private causes$?: Observable<CauseOfLossCode[]>;
  private statuses$?: Observable<ClaimStatusInfo[]>;

  causeOfLossCodes(perilCategory?: PerilCategory): Observable<CauseOfLossCode[]> {
    if (perilCategory) {
      return this.http.get<CauseOfLossCode[]>(`${this.root}/api/reference/cause-of-loss-codes`, {
        params: new HttpParams().set('perilCategory', perilCategory),
      });
    }
    return (this.causes$ ??= this.http.get<CauseOfLossCode[]>(`${this.root}/api/reference/cause-of-loss-codes`).pipe(shareReplay(1)));
  }

  claimStatuses(): Observable<ClaimStatusInfo[]> {
    return (this.statuses$ ??= this.http.get<ClaimStatusInfo[]>(`${this.root}/api/reference/claim-statuses`).pipe(shareReplay(1)));
  }

  /** Typeahead: fired on every pause in typing, so it must not drive the progress bar or toast on failure. */
  searchPolicies(q: string): Observable<Policy[]> {
    return this.http.get<Policy[]>(`${this.root}/api/policies/search`, { params: { q, take: 8 }, context: silent() });
  }
}
