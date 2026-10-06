import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, map, switchMap, tap } from 'rxjs';
import { silent } from '../../core/api/api-context';
import { ClaimsApi } from '../../core/api/claims-api';
import { ReferenceApi } from '../../core/api/reference-api';
import { ClaimListFilters, ClaimStatus, ClaimSummary } from '../../core/models/api.models';
import { ALL_STATUSES, STATUS_LABELS } from '../../core/util/domain';
import { EmptyState } from '../../shared/empty-state';
import { Skeleton } from '../../shared/skeleton';
import { StatusBadge } from '../../shared/badge';

const PAGE_SIZES = [10, 25, 50, 100];
const DEFAULT_PAGE_SIZE = 25;

/** yyyy-MM-dd in the user's local calendar (what the date inputs mean), kept in the URL. */
const toDay = (d: Date | null): string | null =>
  d ? `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}` : null;
const fromDay = (s: string | null): Date | null => (s ? new Date(`${s}T00:00:00`) : null);

@Component({
  selector: 'app-claims-list',
  imports: [
    ReactiveFormsModule, RouterLink, CurrencyPipe, DatePipe,
    MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatDatepickerModule,
    MatTableModule, MatPaginatorModule, StatusBadge, EmptyState, Skeleton,
  ],
  templateUrl: './claims-list.html',
  styleUrl: './claims-list.scss',
})
export class ClaimsList {
  private readonly api = inject(ClaimsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly statuses = ALL_STATUSES;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly columns = ['claimNumber', 'policyNumber', 'clientName', 'lossDate', 'cause', 'status', 'handler', 'total'];
  protected readonly causes = toSignal(inject(ReferenceApi).causeOfLossCodes(), { initialValue: [] });

  protected readonly rows = signal<ClaimSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly loading = signal(true);
  protected readonly firstLoad = signal(true);
  protected readonly failed = signal(false);

  protected readonly filters = inject(FormBuilder).group({
    search: [''],
    status: [[] as ClaimStatus[]],
    dateFrom: [null as Date | null],
    dateTo: [null as Date | null],
    handler: [''],
    cause: [''],
  });

  protected readonly hasFilters = signal(false);
  protected readonly isEmpty = computed(() => !this.loading() && !this.failed() && this.rows().length === 0);

  constructor() {
    // The URL is the single source of truth: it makes filters survive a refresh and work with the back button.
    this.route.queryParamMap.pipe(
      tap(params => this.applyParams(params)),
      map(params => this.toFilters(params)),
      distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
      tap(() => { this.loading.set(true); this.failed.set(false); }),
      switchMap(filters => this.api.list(filters).pipe(
        catchError(() => { this.failed.set(true); this.loading.set(false); this.firstLoad.set(false); return EMPTY; }),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(result => {
      this.rows.set(result.items);
      this.total.set(result.totalCount);
      this.loading.set(false);
      this.firstLoad.set(false);
    });

    // Text inputs wait for a pause in typing; selects and dates apply immediately.
    this.filters.valueChanges.pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.pushFilters());
  }

  protected open(claim: ClaimSummary): void {
    void this.router.navigate(['/claims', claim.id]);
  }

  protected onPage(event: PageEvent): void {
    void this.router.navigate([], {
      relativeTo: this.route, queryParamsHandling: 'merge',
      queryParams: { page: event.pageIndex || null, pageSize: event.pageSize === DEFAULT_PAGE_SIZE ? null : event.pageSize },
    });
  }

  protected clearFilters(): void {
    this.filters.reset({ search: '', status: [], dateFrom: null, dateTo: null, handler: '', cause: '' }, { emitEvent: false });
    void this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  protected reload(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.list(this.toFilters(this.route.snapshot.queryParamMap)).subscribe({
      next: r => { this.rows.set(r.items); this.total.set(r.totalCount); this.loading.set(false); },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
  }

  private pushFilters(): void {
    const f = this.filters.getRawValue();
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        q: f.search?.trim() || null,
        status: f.status?.length ? f.status.join(',') : null,
        from: toDay(f.dateFrom),
        to: toDay(f.dateTo),
        handler: f.handler?.trim() || null,
        cause: f.cause || null,
        pageSize: this.pageSize() === DEFAULT_PAGE_SIZE ? null : this.pageSize(),
      },
    });
  }

  /** Reflect the URL back into the controls (initial load, back/forward, shared links). */
  private applyParams(params: ParamMap): void {
    this.filters.patchValue({
      search: params.get('q') ?? '',
      status: (params.get('status')?.split(',').filter(s => ALL_STATUSES.includes(s as ClaimStatus)) ?? []) as ClaimStatus[],
      dateFrom: fromDay(params.get('from')),
      dateTo: fromDay(params.get('to')),
      handler: params.get('handler') ?? '',
      cause: params.get('cause') ?? '',
    }, { emitEvent: false });

    this.pageIndex.set(Math.max(0, Number(params.get('page') ?? 0) || 0));
    const size = Number(params.get('pageSize'));
    this.pageSize.set(PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE);
    this.hasFilters.set(['q', 'status', 'from', 'to', 'handler', 'cause'].some(k => params.has(k)));
  }

  private toFilters(params: ParamMap): ClaimListFilters {
    const from = fromDay(params.get('from'));
    const to = fromDay(params.get('to'));
    const endOfDay = to ? new Date(to.getFullYear(), to.getMonth(), to.getDate(), 23, 59, 59, 999) : null;
    const size = Number(params.get('pageSize'));

    return {
      search: params.get('q'),
      status: (params.get('status')?.split(',').filter(s => ALL_STATUSES.includes(s as ClaimStatus)) ?? []) as ClaimStatus[],
      dateFrom: from?.toISOString() ?? null,
      dateTo: endOfDay?.toISOString() ?? null,
      assignedHandler: params.get('handler'),
      causeOfLossCode: params.get('cause'),
      page: Math.max(0, Number(params.get('page') ?? 0) || 0) + 1,
      pageSize: PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE,
    };
  }
}
