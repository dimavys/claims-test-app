import { HttpClient, HttpContext, HttpEvent, HttpEventType, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';
import { AppConfig } from '../config/app-config';
import {
  AuditEntry, ClaimCreated, ClaimDetail, ClaimDocument, ClaimListFilters, ClaimParty, ClaimStatusChanged,
  ClaimSummary, ClosurePreflight, CreateClaimRequest, Paged, PartyInput, TransitionRequest, ValidationReport,
} from '../models/api.models';
import { quietErrors } from './api-context';

export interface UploadProgress {
  /** 0–100 while uploading. */
  percent: number;
  /** Set once the server has answered. */
  document?: ClaimDocument;
}

/** The only place components reach claims endpoints; components never inject HttpClient. */
@Injectable({ providedIn: 'root' })
export class ClaimsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(AppConfig).apiBaseUrl}/api/claims`;

  list(filters: ClaimListFilters): Observable<Paged<ClaimSummary>> {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);
    for (const status of filters.status ?? []) params = params.append('status', status);
    if (filters.dateFrom) params = params.set('dateFrom', filters.dateFrom);
    if (filters.dateTo) params = params.set('dateTo', filters.dateTo);
    if (filters.assignedHandler?.trim()) params = params.set('assignedHandler', filters.assignedHandler.trim());
    if (filters.causeOfLossCode) params = params.set('causeOfLossCode', filters.causeOfLossCode);
    if (filters.search?.trim()) params = params.set('search', filters.search.trim());
    return this.http.get<Paged<ClaimSummary>>(this.base, { params });
  }

  get(id: string, context?: HttpContext): Observable<ClaimDetail> {
    return this.http.get<ClaimDetail>(`${this.base}/${id}`, { context });
  }

  /** The Idempotency-Key makes a retried submit (double click, flaky network) create the claim only once. */
  create(request: CreateClaimRequest, idempotencyKey: string): Observable<ClaimCreated> {
    return this.http.post<ClaimCreated>(this.base, request, { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) });
  }

  /** Dry-run used by the review step; failures are shown inline, not as a toast. */
  validate(request: CreateClaimRequest): Observable<ValidationReport> {
    return this.http.post<ValidationReport>(`${this.base}/validate`, request, { context: quietErrors() });
  }

  transition(id: string, request: TransitionRequest): Observable<ClaimStatusChanged> {
    return this.http.put<ClaimStatusChanged>(`${this.base}/${id}/status`, request, { context: quietErrors() });
  }

  closurePreflight(id: string, closureJustification?: string): Observable<ClosurePreflight> {
    let params = new HttpParams();
    if (closureJustification?.trim()) params = params.set('closureJustification', closureJustification.trim());
    return this.http.get<ClosurePreflight>(`${this.base}/${id}/closure-preflight`, { params });
  }

  updateNotes(id: string, notes: string | null): Observable<void> {
    return this.http.put<void>(`${this.base}/${id}/notes`, { notes });
  }

  setManagerOverride(id: string, value: boolean): Observable<void> {
    return this.http.put<void>(`${this.base}/${id}/manager-override`, { value });
  }

  addParty(id: string, party: PartyInput): Observable<ClaimParty> {
    return this.http.post<ClaimParty>(`${this.base}/${id}/parties`, party);
  }

  removeParty(id: string, partyId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}/parties/${partyId}`);
  }

  audit(id: string, page: number, pageSize: number): Observable<Paged<AuditEntry>> {
    return this.http.get<Paged<AuditEntry>>(`${this.base}/${id}/audit`, { params: { page, pageSize } });
  }

  documents(id: string): Observable<ClaimDocument[]> {
    return this.http.get<ClaimDocument[]>(`${this.base}/${id}/documents`);
  }

  /** Multipart upload that reports progress; the last emission carries the saved document. */
  upload(id: string, file: File, documentType: string, notes?: string): Observable<UploadProgress> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('documentType', documentType);
    if (notes?.trim()) form.append('notes', notes.trim());

    return this.http.post<ClaimDocument>(`${this.base}/${id}/documents`, form, { reportProgress: true, observe: 'events' }).pipe(
      map((event: HttpEvent<ClaimDocument>): UploadProgress | null => {
        if (event.type === HttpEventType.UploadProgress) {
          return { percent: event.total ? Math.round((100 * event.loaded) / event.total) : 0 };
        }
        if (event.type === HttpEventType.Response) {
          return { percent: 100, document: event.body! };
        }
        return null;
      }),
      filter((p): p is UploadProgress => p !== null),
    );
  }
}
