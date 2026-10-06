import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AppConfig } from '../config/app-config';
import { ClaimsApi, UploadProgress } from './claims-api';
import { ReservesApi } from './reserves-api';

const API = 'http://api.test';

function setup() {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AppConfig, useValue: { apiBaseUrl: API } }],
  });
  return { claims: TestBed.inject(ClaimsApi), reserves: TestBed.inject(ReservesApi), http: TestBed.inject(HttpTestingController) };
}

describe('ClaimsApi', () => {
  it('builds the list query from the filters, repeating status and skipping blanks', () => {
    const { claims, http } = setup();
    claims.list({
      status: ['Open', 'Draft'], dateFrom: '2026-01-01T00:00:00.000Z', dateTo: null, assignedHandler: ' Hannah ',
      causeOfLossCode: 'COL-FIRE', search: '  ', page: 2, pageSize: 50,
    }).subscribe();

    const req = http.expectOne(r => r.url === `${API}/api/claims`);
    expect(req.request.params.getAll('status')).toEqual(['Open', 'Draft']);
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('50');
    expect(req.request.params.get('dateFrom')).toBe('2026-01-01T00:00:00.000Z');
    expect(req.request.params.has('dateTo')).toBe(false);
    expect(req.request.params.get('assignedHandler')).toBe('Hannah');
    expect(req.request.params.get('causeOfLossCode')).toBe('COL-FIRE');
    expect(req.request.params.has('search')).toBe(false);
    req.flush({ items: [], totalCount: 0, page: 2, pageSize: 50, totalPages: 0 });
  });

  it('sends the idempotency key when creating a claim', () => {
    const { claims, http } = setup();
    claims.create({ policyId: null, lossDate: 'x', lossDescription: 'y', causeOfLossCode: 'z', parties: [], riskObjects: [] }, 'key-123').subscribe();

    const req = http.expectOne(`${API}/api/claims`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('key-123');
    req.flush({});
  });

  it('puts status transitions to the status endpoint', () => {
    const { claims, http } = setup();
    claims.transition('c1', { targetStatus: 'Closed', closureJustification: 'done' }).subscribe();

    const req = http.expectOne(`${API}/api/claims/c1/status`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ targetStatus: 'Closed', closureJustification: 'done' });
    req.flush({});
  });

  it('uploads multipart form data and reports progress then the saved document', () => {
    const { claims, http } = setup();
    const events: UploadProgress[] = [];
    claims.upload('c1', new File(['abc'], 'a.txt', { type: 'text/plain' }), 'Invoice', ' scan ').subscribe(e => events.push(e));

    const req = http.expectOne(`${API}/api/claims/c1/documents`);
    const body = req.request.body as FormData;
    expect((body.get('file') as File).name).toBe('a.txt');
    expect(body.get('documentType')).toBe('Invoice');
    expect(body.get('notes')).toBe('scan');

    req.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 200 });
    req.flush({ id: 'd1', documentName: 'a.txt' });

    expect(events.map(e => e.percent)).toEqual([25, 100]);
    expect(events[1].document?.id).toBe('d1');
  });
});

describe('ReservesApi', () => {
  it('uses the reserve action routes', () => {
    const { reserves, http } = setup();

    reserves.approve('c1', 't1').subscribe();
    expect(http.expectOne(`${API}/api/claims/c1/reserves/t1/approve`).request.method).toBe('POST');

    reserves.reject('c1', 't1', 'too high').subscribe();
    const reject = http.expectOne(`${API}/api/claims/c1/reserves/t1/reject`);
    expect(reject.request.body).toEqual({ rejectionReason: 'too high' });

    reserves.retract('c1', 't1').subscribe();
    http.expectOne(`${API}/api/claims/c1/reserves/t1/retract`);

    reserves.retryPosting('c1', 't1').subscribe();
    http.expectOne(`${API}/api/claims/c1/reserves/t1/retry-posting`);

    reserves.submit('c1', { component: 'Indemnity', amount: 10, changeReason: 'x', transactionType: 'Add' }).subscribe();
    expect(http.expectOne(`${API}/api/claims/c1/reserves`).request.method).toBe('POST');
  });
});
