import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, output, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { ClaimsApi } from '../../../core/api/claims-api';
import { AuditEntry } from '../../../core/models/api.models';
import { splitCamelCase } from '../../../core/util/domain';
import { Badge, Tone } from '../../../shared/badge';
import { EmptyState } from '../../../shared/empty-state';
import { Skeleton } from '../../../shared/skeleton';
import { ClaimDetailStore } from '../claim-detail.store';

export type AuditTarget = 'parties' | 'reserves' | 'documents';

/** Colour by what kind of thing happened. */
export function eventTone(eventType: string): Tone {
  if (eventType.startsWith('RESERVE') || eventType.startsWith('GL_POSTING_FAILED')) return eventType.endsWith('REJECTED') || eventType.endsWith('FAILED') ? 'danger' : 'pending';
  if (eventType.startsWith('GL_')) return 'closed';
  if (eventType.startsWith('STATUS') || eventType.startsWith('CLAIM_')) return 'open';
  if (eventType.startsWith('SLA') || eventType.startsWith('VALIDATION')) return 'reopened';
  if (eventType.startsWith('DOCUMENT') || eventType.startsWith('PARTY')) return 'investigation';
  return 'neutral';
}

/** Where a related entity can be inspected in the UI. */
export function relatedTab(type: string | null): AuditTarget | null {
  switch (type) {
    case 'ReserveHistory': return 'reserves';
    case 'ClaimDocument': return 'documents';
    case 'ClaimParty': return 'parties';
    default: return null;
  }
}

@Component({
  selector: 'app-audit-tab',
  imports: [DatePipe, MatPaginatorModule, MatButtonModule, MatIconModule, Badge, EmptyState, Skeleton],
  template: `
    <div class="wrap">
      <div class="head">
        <h2>Audit log</h2>
        <span class="muted"><mat-icon inline>lock</mat-icon> Read-only, append-only record of everything that happened on this claim.</span>
      </div>

      @if (loading()) {
        <app-skeleton [lines]="6" />
      } @else if (entries().length === 0) {
        <app-empty-state icon="history" title="No audit entries" />
      } @else {
        <ol class="timeline" data-testid="audit-list">
          @for (e of entries(); track e.id) {
            <li class="entry" data-testid="audit-entry">
              <div class="when">
                <div>{{ e.createdAt | date: 'mediumDate' }}</div>
                <div class="muted">{{ e.createdAt | date: 'mediumTime' }}</div>
              </div>
              <div class="body">
                <div class="line">
                  <app-badge [tone]="tone(e.eventType)">{{ label(e.eventType) }}</app-badge>
                  <span class="desc">{{ e.description }}</span>
                </div>
                <div class="meta muted">
                  <mat-icon inline>{{ e.createdByUserId ? 'person' : 'smart_toy' }}</mat-icon> {{ e.createdByName ?? 'System' }}
                  @if (target(e); as t) { · <a href="javascript:void(0)" (click)="openTab.emit(t)" [attr.data-testid]="'audit-link-' + t">View in {{ t }}</a> }
                  @if (e.oldValue || e.newValue) { · <button class="link" (click)="toggle(e.id)">{{ expanded().has(e.id) ? 'Hide' : 'Show' }} values</button> }
                </div>
                @if (expanded().has(e.id)) {
                  <div class="values">
                    @if (e.oldValue) { <div><span class="muted">Before</span><pre>{{ pretty(e.oldValue) }}</pre></div> }
                    @if (e.newValue) { <div><span class="muted">After</span><pre>{{ pretty(e.newValue) }}</pre></div> }
                  </div>
                }
              </div>
            </li>
          }
        </ol>
        <mat-paginator [length]="total()" [pageIndex]="page() - 1" [pageSize]="pageSize()" [pageSizeOptions]="[10, 25, 50]" (page)="onPage($event)" aria-label="Audit log pages" />
      }
    </div>
  `,
  styles: `
    .wrap { padding: 20px; }
    .head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
    h2 { margin: 0; font: var(--mat-sys-title-medium); }
    .timeline { list-style: none; margin: 0; padding: 0; }
    .entry { display: grid; grid-template-columns: 120px 1fr; gap: 16px; padding: 12px 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .when { font-size: 13px; }
    .line { display: flex; gap: 10px; align-items: baseline; flex-wrap: wrap; }
    .desc { overflow-wrap: anywhere; }
    .meta { font-size: 12px; margin-top: 4px; display: flex; align-items: center; gap: 4px; flex-wrap: wrap; }
    .meta a, .link { color: var(--mat-sys-primary); cursor: pointer; background: none; border: 0; padding: 0; font: inherit; text-decoration: underline; }
    .values { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 8px; }
    pre { margin: 2px 0 0; padding: 8px; background: var(--mat-sys-surface-container); border-radius: 6px; font-size: 12px; white-space: pre-wrap; overflow-wrap: anywhere; }
  `,
})
export class AuditTab {
  private readonly store = inject(ClaimDetailStore);
  private readonly api = inject(ClaimsApi);

  readonly openTab = output<AuditTarget>();

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly loading = signal(true);
  protected readonly expanded = signal<Set<string>>(new Set());

  private readonly claimId = computed(() => this.store.claim()?.id);
  /** Re-read when the claim changes (e.g. after a status change or reserve action elsewhere on the page). */
  private readonly version = computed(() => this.store.claim()?.recentAudit[0]?.id);

  protected readonly tone = eventTone;
  protected readonly label = (type: string) => splitCamelCase(type.replaceAll('_', ' ').toLowerCase().replace(/^./, c => c.toUpperCase()));
  protected readonly target = (e: AuditEntry) => relatedTab(e.relatedEntityType);

  constructor() {
    effect(() => {
      this.claimId();
      this.version();
      untracked(() => this.load());
    });
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  protected toggle(id: string): void {
    this.expanded.update(set => {
      const next = new Set(set);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  protected pretty(value: string): string {
    try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
  }

  private load(): void {
    const id = this.claimId();
    if (!id) return;
    this.api.audit(id, this.page(), this.pageSize()).subscribe({
      next: result => { this.entries.set(result.items); this.total.set(result.totalCount); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
