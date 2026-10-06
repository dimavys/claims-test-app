import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { debounceTime, startWith, switchMap } from 'rxjs';
import { ClaimsApi } from '../../core/api/claims-api';
import { ApiException } from '../../core/http/api-error';
import { ClaimDetail, ClaimStatus, ClaimStatusChanged, ClosurePreflight } from '../../core/models/api.models';
import { STATUS_LABELS, transitionNeedsReason } from '../../core/util/domain';
import { StatusBadge } from '../../shared/badge';

export interface TransitionDialogData {
  claim: ClaimDetail;
  target: ClaimStatus;
}

/** Confirms a status change; for Closed it shows the live pre-flight checklist (FRS §11.3). */
@Component({
  selector: 'app-transition-dialog',
  imports: [
    ReactiveFormsModule, CurrencyPipe, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatCheckboxModule, MatIconModule, MatProgressSpinnerModule, StatusBadge,
  ],
  template: `
    <h2 mat-dialog-title>
      Move claim to <span class="target">{{ label }}</span>
    </h2>

    <mat-dialog-content>
      <p class="flow">
        <app-status-badge [status]="data.claim.status" /> <mat-icon>arrow_forward</mat-icon> <app-status-badge [status]="data.target" />
      </p>

      @if (isClosing) {
        <h3>Closure checklist</h3>
        @if (preflight(); as pf) {
          <ul class="checklist" data-testid="closure-checklist">
            @for (b of pf.blockers; track b) {
              <li class="bad"><mat-icon>cancel</mat-icon><span>{{ b }}</span></li>
            }
            @if (pf.canClose) {
              <li class="ok"><mat-icon>check_circle</mat-icon><span>All closure conditions are satisfied.</span></li>
            }
          </ul>
          @if (pf.openReserveBalance > 0) {
            <p class="note">Open reserve balance: <strong>{{ pf.openReserveBalance | currency: 'USD' }}</strong></p>
          }
        } @else {
          <mat-progress-spinner diameter="22" mode="indeterminate" />
        }
      }

      @if (needsAcknowledgement()) {
        <div class="ack" data-testid="warning-ack">
          <strong>Warnings to acknowledge</strong>
          <ul>@for (w of acknowledgeable(); track w.id) { <li>{{ w.message }}</li> }</ul>
          <mat-checkbox [formControl]="form.controls.acknowledge" data-testid="ack-checkbox">I have reviewed and acknowledge these warnings</mat-checkbox>
        </div>
      }

      @if (blockingCritical().length) {
        <div class="blocked" data-testid="critical-blockers">
          <mat-icon>block</mat-icon>
          <div><strong>Resolve these first</strong><ul>@for (c of blockingCritical(); track c.id) { <li>{{ c.message }}</li> }</ul></div>
        </div>
      }

      <form [formGroup]="form" class="fields">
        @if (isClosing && (preflight()?.openReserveBalance ?? 0) > 0) {
          <mat-form-field>
            <mat-label>Justification for closing with open reserves</mat-label>
            <textarea matInput rows="3" formControlName="justification" data-testid="closure-justification"></textarea>
            <mat-hint>Required: reserves still carry a balance.</mat-hint>
          </mat-form-field>
        }
        <mat-form-field>
          <mat-label>{{ reasonRequired ? 'Reason (required)' : 'Reason / note (optional)' }}</mat-label>
          <textarea matInput rows="2" formControlName="reason" data-testid="transition-reason"></textarea>
          @if (form.controls.reason.touched && reasonRequired && !form.controls.reason.value.trim()) {
            <mat-error>A reason is required for this change.</mat-error>
          }
        </mat-form-field>
      </form>

      @if (serverMessages().length) {
        <div class="blocked" role="alert" data-testid="transition-errors">
          <mat-icon>error</mat-icon>
          <ul>@for (m of serverMessages(); track m) { <li>{{ m }}</li> }</ul>
        </div>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close [disabled]="busy()" data-testid="transition-cancel">Cancel</button>
      <button mat-flat-button (click)="confirm()" [disabled]="!canConfirm()" data-testid="transition-confirm">
        @if (busy()) { <mat-progress-spinner diameter="18" mode="indeterminate" /> } @else { Move to {{ label }} }
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .target { font-weight: 600; }
    .flow { display: flex; align-items: center; gap: 8px; margin: 0 0 12px; }
    h3 { margin: 12px 0 4px; font: var(--mat-sys-title-small); }
    .checklist { list-style: none; padding: 0; margin: 0; }
    .checklist li { display: flex; gap: 8px; align-items: flex-start; padding: 4px 0; }
    .checklist .ok mat-icon { color: var(--money-up); }
    .checklist .bad mat-icon { color: var(--money-down); }
    .note { margin: 8px 0; }
    .fields { display: flex; flex-direction: column; gap: 4px; margin-top: 12px; min-width: 460px; }
    .ack { background: var(--status-reopened-bg); color: var(--status-reopened-fg); border-radius: 8px; padding: 10px 14px; margin-top: 8px; }
    .ack ul, .blocked ul { margin: 4px 0; padding-left: 18px; }
    .blocked { display: flex; gap: 8px; background: var(--status-danger-bg); color: var(--status-danger-fg); border-radius: 8px; padding: 10px 14px; margin-top: 8px; }
  `,
})
export class TransitionDialog {
  protected readonly data = inject<TransitionDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<TransitionDialog, ClaimStatusChanged | null>>(MatDialogRef);
  private readonly api = inject(ClaimsApi);

  protected readonly label = STATUS_LABELS[this.data.target];
  protected readonly isClosing = this.data.target === 'Closed';
  protected readonly reasonRequired = transitionNeedsReason(this.data.target);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    reason: [''],
    justification: [''],
    acknowledge: [false],
  });

  protected readonly busy = signal(false);
  protected readonly serverMessages = signal<string[]>([]);
  protected readonly preflight = signal<ClosurePreflight | null>(null);

  private readonly justification = toSignal(
    this.form.controls.justification.valueChanges.pipe(startWith('')), { initialValue: '' });
  private readonly reason = toSignal(this.form.controls.reason.valueChanges.pipe(startWith('')), { initialValue: '' });
  private readonly acknowledged = toSignal(this.form.controls.acknowledge.valueChanges.pipe(startWith(false)), { initialValue: false });

  /** Draft → Open: warnings that must be cleared or acknowledged (BR-C-02). */
  protected readonly acknowledgeable = computed(() => this.data.target === 'Open' && this.data.claim.status === 'Draft'
    ? this.data.claim.validationIssues.filter(i => i.isOutstanding && i.severity === 'Warning' && i.requiresAcknowledgement) : []);
  protected readonly needsAcknowledgement = computed(() => this.acknowledgeable().length > 0);
  protected readonly blockingCritical = computed(() => this.data.target === 'Open' && this.data.claim.status === 'Draft'
    ? this.data.claim.validationIssues.filter(i => i.isOutstanding && i.severity === 'Critical') : []);

  protected readonly canConfirm = computed(() => {
    if (this.busy()) return false;
    if (this.reasonRequired && !this.reason().trim()) return false;
    if (this.needsAcknowledgement() && !this.acknowledged()) return false;
    if (this.blockingCritical().length > 0) return false;
    if (this.isClosing) return this.preflight()?.canClose === true;
    return true;
  });

  constructor() {
    if (this.isClosing) {
      // Re-run the pre-flight as the justification is typed, so the checklist reflects what confirm would do.
      this.form.controls.justification.valueChanges.pipe(
        startWith(''), debounceTime(250),
        switchMap(text => this.api.closurePreflight(this.data.claim.id, text)),
        takeUntilDestroyed(),
      ).subscribe({ next: pf => this.preflight.set(pf), error: () => this.preflight.set({ canClose: false, blockers: ['Could not check closure conditions.'], openReserveBalance: 0, requiresJustification: false }) });
    }
  }

  protected confirm(): void {
    const f = this.form.getRawValue();
    this.busy.set(true);
    this.serverMessages.set([]);

    this.api.transition(this.data.claim.id, {
      targetStatus: this.data.target,
      reason: f.reason.trim() || null,
      acknowledgeWarnings: this.needsAcknowledgement() ? f.acknowledge : false,
      closureJustification: f.justification.trim() || null,
    }).subscribe({
      next: result => this.ref.close(result),
      error: (e: unknown) => {
        this.busy.set(false);
        this.serverMessages.set(e instanceof ApiException ? (e.allMessages.length ? e.allMessages : [e.message]) : ['The status change failed.']);
      },
    });
  }
}
