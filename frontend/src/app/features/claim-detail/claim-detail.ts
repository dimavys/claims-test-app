import { CurrencyPipe, DatePipe } from '@angular/common';
import { Clipboard } from '@angular/cdk/clipboard';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTabChangeEvent, MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Notifier } from '../../core/http/notifier';
import { ClaimStatus, ClaimStatusChanged, NextStatus } from '../../core/models/api.models';
import { STATUS_LABELS } from '../../core/util/domain';
import { EmptyState } from '../../shared/empty-state';
import { SeverityBadge, StatusBadge } from '../../shared/badge';
import { Skeleton } from '../../shared/skeleton';
import { ClaimDetailStore } from './claim-detail.store';
import { TransitionDialog, TransitionDialogData } from './transition-dialog';
import { AuditTab } from './tabs/audit-tab';
import { DocumentsTab } from './tabs/documents-tab';
import { OverviewTab } from './tabs/overview-tab';
import { PartiesTab } from './tabs/parties-tab';
import { ReservesTab } from './tabs/reserves-tab';

export const TAB_KEYS = ['overview', 'parties', 'reserves', 'documents', 'audit'] as const;
export type TabKey = (typeof TAB_KEYS)[number];

@Component({
  selector: 'app-claim-detail',
  imports: [
    CurrencyPipe, DatePipe, RouterLink,
    MatButtonModule, MatIconModule, MatMenuModule, MatTabsModule, MatTooltipModule,
    StatusBadge, SeverityBadge, EmptyState, Skeleton,
    OverviewTab, PartiesTab, ReservesTab, DocumentsTab, AuditTab,
  ],
  providers: [ClaimDetailStore],
  templateUrl: './claim-detail.html',
  styleUrl: './claim-detail.scss',
})
export class ClaimDetailPage {
  /** Bound from the route (`:id`) via withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly store = inject(ClaimDetailStore);
  private readonly dialog = inject(MatDialog);
  private readonly clipboard = inject(Clipboard);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly statusLabels = STATUS_LABELS;
  protected readonly createdNumber = signal<string | null>(this.route.snapshot.queryParamMap.get('created'));
  protected readonly selectedTab = signal(Math.max(0, TAB_KEYS.indexOf((this.route.snapshot.queryParamMap.get('tab') ?? 'overview') as TabKey)));
  protected readonly claim = this.store.claim;
  protected readonly menuHint = computed(() =>
    this.store.availableTransitions().length === 0 ? 'No status changes are available to your role.' : '');

  constructor() {
    effect(() => this.store.load(this.id()));
  }

  protected copyNumber(): void {
    const number = this.claim()?.claimNumber;
    if (number && this.clipboard.copy(number)) this.notifier.info(`Copied ${number}`);
  }

  protected dismissBanner(): void {
    this.createdNumber.set(null);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { created: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected openTransition(next: NextStatus): void {
    const claim = this.claim();
    if (!claim) return;

    this.dialog.open<TransitionDialog, TransitionDialogData, ClaimStatusChanged | null>(TransitionDialog, {
      data: { claim, target: next.status as ClaimStatus },
      width: '560px',
      autoFocus: 'dialog',
    }).afterClosed().subscribe(result => {
      if (result) {
        this.notifier.success(`Status changed to ${STATUS_LABELS[result.status]}.`);
        this.store.reload();
      }
    });
  }

  protected onTabChange(event: MatTabChangeEvent): void {
    this.selectedTab.set(event.index);
    void this.router.navigate([], {
      relativeTo: this.route, replaceUrl: true, queryParamsHandling: 'merge',
      queryParams: { tab: TAB_KEYS[event.index] === 'overview' ? null : TAB_KEYS[event.index] },
    });
  }

  /** Lets child tabs (e.g. audit links) switch tabs. */
  protected goToTab(key: TabKey): void {
    this.selectedTab.set(TAB_KEYS.indexOf(key));
    void this.router.navigate([], { relativeTo: this.route, replaceUrl: true, queryParamsHandling: 'merge', queryParams: { tab: key } });
  }
}
