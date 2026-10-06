import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { silent } from '../../../core/api/api-context';
import { ReservesApi } from '../../../core/api/reserves-api';
import { Auth } from '../../../core/auth/auth';
import { Notifier } from '../../../core/http/notifier';
import { ReserveSubmission, ReserveTransaction, Reserves } from '../../../core/models/api.models';
import { canApproveAmount, requiredAuthority, splitCamelCase } from '../../../core/util/domain';
import { ApprovalBadge, PostingBadge } from '../../../shared/badge';
import { ConfirmDialog } from '../../../shared/confirm-dialog';
import { EmptyState } from '../../../shared/empty-state';
import { Skeleton } from '../../../shared/skeleton';
import { ClaimDetailStore } from '../claim-detail.store';
import { RejectDialog } from './reject-dialog';
import { ReservePanel, ReservePanelData } from './reserve-panel';

const POLL_MS = 3000;
const POLL_MAX = 10;

@Component({
  selector: 'app-reserves-tab',
  imports: [
    CurrencyPipe, DatePipe, MatTableModule, MatButtonModule, MatIconModule, MatTooltipModule,
    ApprovalBadge, PostingBadge, EmptyState, Skeleton,
  ],
  template: `
    <div class="wrap">
      <div class="head">
        <h2>Reserves</h2>
        <span [matTooltip]="addHint()">
          <button mat-flat-button (click)="openPanel()" [disabled]="!store.canChangeReserves()" data-testid="add-reserve"><mat-icon>add</mat-icon> Add reserve</button>
        </span>
      </div>

      @if (!store.canChangeReserves() && store.claim(); as c) {
        <div class="note" data-testid="reserve-blocked">
          <mat-icon>info</mat-icon>
          <span>{{ !c.policyId ? 'Reserves are blocked until a policy is linked to this claim.' : 'Reserves cannot be changed on a ' + c.status + ' claim.' }}</span>
        </div>
      }

      @if (loading()) {
        <app-skeleton [lines]="5" />
      } @else if (data(); as d) {
        <div class="cards" data-testid="reserve-cards">
          <div class="rcard total">
            <span class="label">Total reserves</span>
            <span class="value">{{ d.summary.totalReserves | currency: 'USD' }}</span>
            <span class="sub" [class.pending]="d.summary.totalPending !== 0">Pending {{ d.summary.totalPending | currency: 'USD' }}</span>
          </div>
          @for (c of d.summary.components; track c.id) {
            <div class="rcard" [attr.data-testid]="'card-' + c.component">
              <span class="label">{{ split(c.component) }}</span>
              <span class="value" [class.money-down]="c.currentAmount < 0">{{ c.currentAmount | currency: 'USD' }}</span>
              <span class="sub" [class.pending]="c.pendingAmount !== 0">Pending {{ c.pendingAmount | currency: 'USD' }}</span>
            </div>
          }
          @if (d.summary.managerOverride) { <div class="rcard override"><mat-icon>verified_user</mat-icon><span>Manager override active</span></div> }
        </div>

        @if (d.transactions.length === 0) {
          <app-empty-state icon="account_balance_wallet" title="No reserves yet" message="Add a reserve to record the expected cost of this claim." />
        } @else {
          <table mat-table [dataSource]="d.transactions" data-testid="reserve-table">
            <ng-container matColumnDef="date">
              <th mat-header-cell *matHeaderCellDef>Date</th>
              <td mat-cell *matCellDef="let t">{{ t.createdAt | date: 'medium' }}</td>
            </ng-container>
            <ng-container matColumnDef="type">
              <th mat-header-cell *matHeaderCellDef>Type</th>
              <td mat-cell *matCellDef="let t">{{ t.transactionType }}</td>
            </ng-container>
            <ng-container matColumnDef="component">
              <th mat-header-cell *matHeaderCellDef>Component</th>
              <td mat-cell *matCellDef="let t">{{ split(t.component) }}</td>
            </ng-container>
            <ng-container matColumnDef="amount">
              <th mat-header-cell *matHeaderCellDef class="num">Amount</th>
              <td mat-cell *matCellDef="let t" class="num" [class.money-up]="t.amount > 0" [class.money-down]="t.amount < 0">
                {{ t.amount > 0 ? '+' : '' }}{{ t.amount | currency: 'USD' }}
              </td>
            </ng-container>
            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef>Status</th>
              <td mat-cell *matCellDef="let t">
                <app-approval-badge [status]="t.approvalStatus" />
                @if (t.rejectionReason) { <div class="muted small" [matTooltip]="t.rejectionReason">“{{ t.rejectionReason }}”</div> }
              </td>
            </ng-container>
            <ng-container matColumnDef="submittedBy">
              <th mat-header-cell *matHeaderCellDef>Submitted by</th>
              <td mat-cell *matCellDef="let t">{{ t.submittedByName ?? '—' }}</td>
            </ng-container>
            <ng-container matColumnDef="approvedBy">
              <th mat-header-cell *matHeaderCellDef>Approved by</th>
              <td mat-cell *matCellDef="let t">{{ t.approvedByName ?? (t.approvalStatus === 'AutoApproved' ? 'Auto' : '—') }}</td>
            </ng-container>
            <ng-container matColumnDef="posting">
              <th mat-header-cell *matHeaderCellDef>GL posting</th>
              <td mat-cell *matCellDef="let t">
                @if (isApproved(t)) {
                  <app-posting-badge [status]="t.postingStatus" />
                  @if (t.postingStatus === 'Failed') {
                    <button mat-button class="retry" (click)="retry(t)" [disabled]="busyId() === t.id" data-testid="retry-posting"><mat-icon>refresh</mat-icon> Retry</button>
                  }
                } @else { <span class="muted">—</span> }
              </td>
            </ng-container>
            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef></th>
              <td mat-cell *matCellDef="let t" class="actions">
                @if (t.approvalStatus === 'PendingApproval') {
                  @if (auth.canApprove()) {
                    <span [matTooltip]="approveBlock(t)">
                      <button mat-flat-button class="approve" (click)="approve(t)" [disabled]="!!approveBlock(t) || busyId() === t.id" data-testid="approve">Approve</button>
                    </span>
                    <span [matTooltip]="rejectBlock(t)">
                      <button mat-stroked-button (click)="reject(t)" [disabled]="!!rejectBlock(t) || busyId() === t.id" data-testid="reject">Reject</button>
                    </span>
                  }
                  @if (isMine(t)) {
                    <button mat-button (click)="retract(t)" [disabled]="busyId() === t.id" data-testid="retract">Retract</button>
                  }
                }
              </td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns" [attr.data-testid]="'txn-' + row.approvalStatus"></tr>
          </table>
        }
      }
    </div>
  `,
  styles: `
    .wrap { padding: 20px; }
    .head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
    h2 { margin: 0; font: var(--mat-sys-title-medium); }
    .note { display: flex; gap: 8px; align-items: center; background: var(--status-reopened-bg); color: var(--status-reopened-fg); padding: 8px 12px; border-radius: 8px; margin-bottom: 12px; }
    .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 12px; margin-bottom: 20px; }
    .rcard { display: flex; flex-direction: column; gap: 2px; padding: 12px 16px; border: 1px solid var(--mat-sys-outline-variant); border-radius: 10px; background: var(--mat-sys-surface-container-lowest); }
    .rcard.total { background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); border-color: transparent; }
    .rcard.override { flex-direction: row; align-items: center; gap: 8px; color: var(--status-open-fg); background: var(--status-open-bg); }
    .label { font: var(--mat-sys-label-medium); opacity: 0.8; }
    .value { font: var(--mat-sys-headline-small); font-variant-numeric: tabular-nums; }
    .sub { font-size: 12px; color: var(--mat-sys-on-surface-variant); }
    .rcard.total .sub { color: inherit; opacity: 0.8; }
    .sub.pending { color: var(--status-reopened-fg); background: var(--status-reopened-bg); border-radius: 6px; padding: 1px 6px; align-self: flex-start; opacity: 1; font-weight: 500; }
    table { width: 100%; }
    th.num, td.num { text-align: right; font-variant-numeric: tabular-nums; }
    td.actions { white-space: nowrap; text-align: right; }
    td.actions > * { margin-left: 4px; }
    .approve { --mat-button-filled-container-color: var(--money-up); --mat-button-filled-label-text-color: #fff; }
    .small { font-size: 12px; max-width: 220px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .retry { margin-left: 4px; }
  `,
})
export class ReservesTab {
  protected readonly store = inject(ClaimDetailStore);
  protected readonly auth = inject(Auth);
  private readonly api = inject(ReservesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = ['date', 'type', 'component', 'amount', 'status', 'submittedBy', 'approvedBy', 'posting', 'actions'];
  protected readonly selfApproval = 'You cannot approve your own reserve.';
  protected readonly data = signal<Reserves | null>(null);
  protected readonly loading = signal(true);
  protected readonly busyId = signal<string | null>(null);
  protected readonly split = splitCamelCase;

  private readonly claimId = computed(() => this.store.claim()?.id);
  private pollTimer?: ReturnType<typeof setTimeout>;
  private pollCount = 0;

  protected readonly addHint = computed(() => {
    const c = this.store.claim();
    if (!c) return '';
    if (!c.policyId) return 'Link a policy first (reserves are blocked without one).';
    return this.store.canChangeReserves() ? '' : `Reserves cannot be changed on a ${c.status} claim.`;
  });

  constructor() {
    effect(() => {
      const id = this.claimId();
      if (id) untracked(() => this.load());
    });
    this.destroyRef.onDestroy(() => clearTimeout(this.pollTimer));
  }

  protected isApproved(t: ReserveTransaction): boolean {
    return t.approvalStatus === 'Approved' || t.approvalStatus === 'AutoApproved';
  }

  protected isMine(t: ReserveTransaction): boolean {
    return !!t.submittedByUserId && t.submittedByUserId === this.auth.user()?.id;
  }

  /** Why Approve/Reject is unavailable for this row ('' when allowed). The server re-checks all of it. */
  protected approveBlock(t: ReserveTransaction): string {
    if (this.isMine(t)) return this.selfApproval;
    if (!canApproveAmount(this.auth.role(), t.amount)) {
      return `${requiredAuthority(t.amount) === 'manager' ? 'Manager' : 'Supervisor'} approval is required for this amount.`;
    }
    return '';
  }

  /** Rejecting needs the same amount authority as approving, but your own reserve is rejected by retracting it. */
  protected rejectBlock(t: ReserveTransaction): string {
    return canApproveAmount(this.auth.role(), t.amount)
      ? ''
      : `${requiredAuthority(t.amount) === 'manager' ? 'Manager' : 'Supervisor'} authority is required for this amount.`;
  }

  protected openPanel(): void {
    const claim = this.store.claim();
    if (!claim) return;
    this.dialog.open<ReservePanel, ReservePanelData, ReserveSubmission | null>(ReservePanel, {
      data: { claimId: claim.id, components: this.data()?.summary.components ?? [] },
      position: { right: '0', top: '0' },
      height: '100vh', width: '440px', maxWidth: '100vw',
      panelClass: 'side-panel-dialog',
    }).afterClosed().subscribe(result => {
      if (!result) return;
      const t = result.transaction;
      if (result.requiresApproval) this.notifier.warn(`Reserve submitted — ${result.requiredAuthority.toLowerCase()}.`);
      else this.notifier.success(`Reserve of ${t.amount} auto-approved. GL posting queued.`);
      result.warnings.forEach(w => this.notifier.warn(w));
      this.refreshAll();
    });
  }

  protected approve(t: ReserveTransaction): void {
    this.act(t, this.api.approve(this.store.claim()!.id, t.id), 'Reserve approved. GL posting queued.');
  }

  protected reject(t: ReserveTransaction): void {
    this.dialog.open<RejectDialog, void, string>(RejectDialog).afterClosed().subscribe(reason => {
      if (reason) this.act(t, this.api.reject(this.store.claim()!.id, t.id, reason), 'Reserve rejected.');
    });
  }

  protected retract(t: ReserveTransaction): void {
    this.dialog.open(ConfirmDialog, {
      data: { title: 'Retract this reserve?', message: 'It will be cancelled. You can submit a new one with a revised amount.', confirmLabel: 'Retract' },
    }).afterClosed().subscribe(ok => ok && this.act(t, this.api.retract(this.store.claim()!.id, t.id), 'Reserve retracted.'));
  }

  protected retry(t: ReserveTransaction): void {
    this.act(t, this.api.retryPosting(this.store.claim()!.id, t.id), 'GL posting re-queued.');
  }

  private act(t: ReserveTransaction, request: ReturnType<ReservesApi['approve']>, success: string): void {
    this.busyId.set(t.id);
    request.subscribe({
      next: () => { this.busyId.set(null); this.notifier.success(success); this.refreshAll(); },
      error: () => { this.busyId.set(null); this.refreshAll(); }, // the toast explains; refresh in case someone else already acted
    });
  }

  private refreshAll(): void {
    this.load();
    this.store.reload();
  }

  private load(): void {
    const id = this.claimId();
    if (!id) return;
    this.api.get(id).subscribe({
      next: r => { this.data.set(r); this.loading.set(false); this.pollCount = 0; this.schedulePoll(); },
      error: () => this.loading.set(false),
    });
  }

  /** GL posting runs in a background job, so keep refreshing quietly until every approved row has posted. */
  private schedulePoll(): void {
    clearTimeout(this.pollTimer);
    const waiting = this.data()?.transactions.some(t => this.isApproved(t) && t.postingStatus === 'Pending');
    if (!waiting || this.pollCount >= POLL_MAX) return;

    this.pollTimer = setTimeout(() => {
      const id = this.claimId();
      if (!id) return;
      this.pollCount++;
      this.api.get(id, silent()).subscribe({
        next: r => { this.data.set(r); this.schedulePoll(); },
        error: () => undefined,
      });
    }, POLL_MS);
  }
}
