import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ClaimsApi } from '../../../core/api/claims-api';
import { Auth } from '../../../core/auth/auth';
import { Notifier } from '../../../core/http/notifier';
import { IssueBadge, SeverityBadge } from '../../../shared/badge';
import { ClaimDetailStore } from '../claim-detail.store';

@Component({
  selector: 'app-overview-tab',
  imports: [
    CurrencyPipe, DatePipe, ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule,
    MatIconModule, MatSlideToggleModule, SeverityBadge, IssueBadge,
  ],
  template: `
    @if (store.claim(); as c) {
      <div class="grid">
        <section class="block">
          <h2>Loss event</h2>
          <dl>
            <dt>Date of loss</dt><dd>{{ c.lossEvent.lossDate | date: 'medium' }}</dd>
            <dt>Reported</dt><dd>{{ c.lossEvent.reportDate | date: 'medium' }}</dd>
            <dt>Cause of loss</dt><dd>{{ c.lossEvent.causeOfLossName }} <span class="muted mono">{{ c.lossEvent.causeOfLossCode }}</span></dd>
            <dt>Location</dt><dd>{{ c.lossEvent.lossLocation || '—' }}</dd>
            <dt>Estimated loss</dt><dd>{{ c.lossEvent.estimatedLossAmount !== null ? (c.lossEvent.estimatedLossAmount | currency: 'USD') : '—' }}</dd>
            <dt>Police report</dt><dd>{{ c.lossEvent.policeReportNumber || '—' }}</dd>
            <dt>Severity</dt><dd><app-severity-badge [severity]="c.severity" /></dd>
            <dt>Claim type</dt><dd>{{ c.claimType || '—' }}</dd>
          </dl>
          <h3>Description</h3>
          <p class="description">{{ c.lossEvent.lossDescription }}</p>

          @if (c.status === 'Closed' || c.status === 'Withdrawn') {
            <h3>{{ c.status === 'Closed' ? 'Closure' : 'Withdrawal' }}</h3>
            <p>{{ c.closureReason || 'No reason recorded.' }} @if (c.closedAt) { <span class="muted">· {{ c.closedAt | date: 'medium' }}</span> }</p>
          }
        </section>

        <section class="block">
          <h2>Validation issues</h2>
          @for (i of c.validationIssues; track i.id) {
            <div class="issue" [class.done]="!i.isOutstanding" data-testid="issue">
              <app-issue-badge [severity]="i.severity" />
              <span class="msg">{{ i.message }}</span>
              <span class="muted state">{{ i.isResolved ? 'Resolved' : i.isAcknowledged ? 'Acknowledged' : 'Open' }}</span>
            </div>
          } @empty {
            <p class="muted">No validation issues were recorded.</p>
          }

          @if (auth.isManager()) {
            <h3>Reserve override</h3>
            <mat-slide-toggle [checked]="c.managerOverride" (change)="setOverride($event.checked)" data-testid="manager-override">
              Allow reserves above $10,000,000
            </mat-slide-toggle>
          } @else if (c.managerOverride) {
            <p class="muted"><mat-icon inline>verified_user</mat-icon> A manager has authorised reserves above $10,000,000 on this claim.</p>
          }
        </section>

        <section class="block wide">
          <h2>Notes</h2>
          <mat-form-field>
            <mat-label>Internal claim notes</mat-label>
            <textarea matInput rows="4" [formControl]="notes" data-testid="claim-notes"></textarea>
          </mat-form-field>
          <div class="save-row">
            <button mat-flat-button (click)="saveNotes()" [disabled]="notes.pristine || saving()" data-testid="save-notes">Save notes</button>
            @if (notes.pristine && savedAt()) { <span class="muted">Saved</span> }
          </div>
        </section>
      </div>
    }
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1.2fr 1fr; gap: 24px; padding: 20px; }
    .wide { grid-column: 1 / -1; }
    h2 { font: var(--mat-sys-title-medium); margin: 0 0 8px; }
    h3 { font: var(--mat-sys-title-small); margin: 16px 0 4px; }
    dl { display: grid; grid-template-columns: 140px 1fr; gap: 6px 12px; margin: 0; }
    dt { color: var(--mat-sys-on-surface-variant); }
    dd { margin: 0; }
    .description { white-space: pre-wrap; margin: 0; }
    .issue { display: flex; align-items: center; gap: 10px; padding: 8px 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .issue .msg { flex: 1; }
    .issue.done .msg { text-decoration: line-through; color: var(--mat-sys-on-surface-variant); }
    .state { font-size: 12px; }
    .save-row { display: flex; align-items: center; gap: 12px; }
  `,
})
export class OverviewTab {
  protected readonly store = inject(ClaimDetailStore);
  protected readonly auth = inject(Auth);
  private readonly api = inject(ClaimsApi);
  private readonly notifier = inject(Notifier);

  protected readonly notes = new FormControl('', { nonNullable: true });
  protected readonly saving = signal(false);
  protected readonly savedAt = signal<Date | null>(null);

  constructor() {
    // Load notes into the editor once per claim, but never overwrite the user's unsaved edits on a refresh.
    effect(() => {
      const claim = this.store.claim();
      if (claim && this.notes.pristine) this.notes.setValue(claim.notes ?? '', { emitEvent: false });
    });
  }

  protected saveNotes(): void {
    const claim = this.store.claim();
    if (!claim) return;
    this.saving.set(true);
    this.api.updateNotes(claim.id, this.notes.value.trim() || null).subscribe({
      next: () => { this.saving.set(false); this.notes.markAsPristine(); this.savedAt.set(new Date()); this.notifier.success('Notes saved.'); },
      error: () => this.saving.set(false),
    });
  }

  protected setOverride(value: boolean): void {
    const claim = this.store.claim();
    if (!claim) return;
    this.api.setManagerOverride(claim.id, value).subscribe({
      next: () => { this.notifier.success(value ? 'Override enabled.' : 'Override removed.'); this.store.reload(); },
      error: () => this.store.reload(),
    });
  }
}
