import { Component, computed, input } from '@angular/core';
import {
  ClaimStatus, ReserveApprovalStatus, ReservePostingStatus, ValidationSeverity,
} from '../core/models/api.models';
import { STATUS_LABELS, splitCamelCase } from '../core/util/domain';

export type Tone = 'draft' | 'open' | 'investigation' | 'pending' | 'closed' | 'reopened' | 'withdrawn' | 'danger' | 'neutral';

/** A small coloured pill. Colours come from the status palette tokens defined in styles.scss. */
@Component({
  selector: 'app-badge',
  template: `<span class="badge tone-{{ tone() }}"><ng-content /></span>`,
  styles: `
    .badge { display: inline-block; padding: 2px 10px; border-radius: 999px; font: var(--mat-sys-label-medium); font-weight: 500;
      white-space: nowrap; line-height: 20px; }
    .tone-draft { background: var(--status-draft-bg); color: var(--status-draft-fg); }
    .tone-open { background: var(--status-open-bg); color: var(--status-open-fg); }
    .tone-investigation { background: var(--status-investigation-bg); color: var(--status-investigation-fg); }
    .tone-pending { background: var(--status-pending-bg); color: var(--status-pending-fg); }
    .tone-closed { background: var(--status-closed-bg); color: var(--status-closed-fg); }
    .tone-reopened { background: var(--status-reopened-bg); color: var(--status-reopened-fg); }
    .tone-withdrawn { background: var(--status-withdrawn-bg); color: var(--status-withdrawn-fg); }
    .tone-danger { background: var(--status-danger-bg); color: var(--status-danger-fg); }
    .tone-neutral { background: var(--mat-sys-surface-container-high); color: var(--mat-sys-on-surface-variant); }
  `,
})
export class Badge {
  readonly tone = input<Tone>('neutral');
}

const STATUS_TONES: Record<ClaimStatus, Tone> = {
  Draft: 'draft',
  Open: 'open',
  UnderInvestigation: 'investigation',
  PendingPayment: 'pending',
  Closed: 'closed',
  Reopened: 'reopened',
  Withdrawn: 'withdrawn',
};

export const statusTone = (status: ClaimStatus): Tone => STATUS_TONES[status];

@Component({
  selector: 'app-status-badge',
  imports: [Badge],
  template: `<app-badge [tone]="tone()" [attr.data-status]="status()">{{ label() }}</app-badge>`,
})
export class StatusBadge {
  readonly status = input.required<ClaimStatus>();
  protected readonly tone = computed(() => statusTone(this.status()));
  protected readonly label = computed(() => STATUS_LABELS[this.status()]);
}

const APPROVAL_TONES: Record<ReserveApprovalStatus, Tone> = {
  AutoApproved: 'closed',
  Approved: 'closed',
  PendingApproval: 'reopened',
  Rejected: 'danger',
  Cancelled: 'draft',
};

@Component({
  selector: 'app-approval-badge',
  imports: [Badge],
  template: `<app-badge [tone]="tone()">{{ label() }}</app-badge>`,
})
export class ApprovalBadge {
  readonly status = input.required<ReserveApprovalStatus>();
  protected readonly tone = computed(() => APPROVAL_TONES[this.status()]);
  protected readonly label = computed(() => splitCamelCase(this.status()));
}

const POSTING_TONES: Record<ReservePostingStatus, Tone> = {
  Pending: 'reopened',
  Posted: 'closed',
  Failed: 'danger',
  Cancelled: 'draft',
};

@Component({
  selector: 'app-posting-badge',
  imports: [Badge],
  template: `<app-badge [tone]="tone()">GL {{ status() }}</app-badge>`,
})
export class PostingBadge {
  readonly status = input.required<ReservePostingStatus>();
  protected readonly tone = computed(() => POSTING_TONES[this.status()]);
}

@Component({
  selector: 'app-severity-badge',
  imports: [Badge],
  template: `<app-badge [tone]="tone()">{{ severity() }}</app-badge>`,
})
export class SeverityBadge {
  readonly severity = input.required<string>();
  protected readonly tone = computed<Tone>(() => ({
    Catastrophic: 'danger', Critical: 'investigation', Standard: 'open', Minor: 'draft',
  } as Record<string, Tone>)[this.severity()] ?? 'neutral');
}

@Component({
  selector: 'app-issue-badge',
  imports: [Badge],
  template: `<app-badge [tone]="severity() === 'Critical' ? 'danger' : 'reopened'">{{ severity() }}</app-badge>`,
})
export class IssueBadge {
  readonly severity = input.required<ValidationSeverity>();
}
