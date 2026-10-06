import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { ReservesApi } from '../../../core/api/reserves-api';
import { ApiException } from '../../../core/http/api-error';
import {
  ReserveComponent, ReserveComponentType, ReserveSubmission, ReserveTransactionType,
} from '../../../core/models/api.models';
import {
  allowsNegative, authorityMessage, isValidReserveAmount, requiredAuthority, splitCamelCase,
} from '../../../core/util/domain';

export interface ReservePanelData {
  claimId: string;
  components: ReserveComponent[];
}

const COMPONENTS: ReserveComponentType[] = ['Indemnity', 'Expense', 'ALAE', 'SubrogationRecoverable'];

/** Slide-in panel for opening or changing a reserve, with the authority indicator updating as the amount is typed. */
@Component({
  selector: 'app-reserve-panel',
  imports: [
    ReactiveFormsModule, CurrencyPipe, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatIconModule, MatProgressSpinnerModule,
  ],
  template: `
    <header class="panel-head">
      <h2>Add reserve</h2>
      <button mat-icon-button mat-dialog-close aria-label="Close panel"><mat-icon>close</mat-icon></button>
    </header>

    <form [formGroup]="form" (ngSubmit)="submit()" class="panel-body" data-testid="reserve-panel">
      <mat-form-field>
        <mat-label>Component</mat-label>
        <mat-select formControlName="component" data-testid="panel-component">
          @for (c of components; track c) { <mat-option [value]="c">{{ splitCamelCase(c) }}</mat-option> }
        </mat-select>
        <mat-hint>Current balance: {{ balance() | currency: 'USD' }}</mat-hint>
      </mat-form-field>

      <mat-form-field>
        <mat-label>Change type</mat-label>
        <mat-select formControlName="type" data-testid="panel-type">
          <mat-option value="Add">Add — increase the reserve</mat-option>
          <mat-option value="Adjust">Adjust — revise upward</mat-option>
          <mat-option value="Reverse">Reverse — reduce the reserve</mat-option>
        </mat-select>
      </mat-form-field>

      <mat-form-field>
        <mat-label>Amount</mat-label>
        <span matTextPrefix>$&nbsp;</span>
        <input matInput type="number" step="0.01" formControlName="amount" data-testid="panel-amount" />
        @if (showAmountError()) {
          <mat-error>{{ amountError() }}</mat-error>
        } @else {
          <mat-hint>{{ allowsNegative() ? 'Recoveries may be negative.' : 'Enter a positive amount.' }}</mat-hint>
        }
      </mat-form-field>

      <mat-form-field>
        <mat-label>Reason</mat-label>
        <textarea matInput rows="3" formControlName="reason" data-testid="panel-reason"></textarea>
        @if (form.controls.reason.touched && !form.controls.reason.value.trim()) { <mat-error>A change reason is required.</mat-error> }
      </mat-form-field>

      @if (authority(); as a) {
        <div class="authority authority-{{ a.level }}" role="status" data-testid="panel-authority" [attr.data-level]="a.level">
          {{ a.message }}
          @if (newBalance() !== null) { <div class="small">New balance once approved: {{ newBalance() | currency: 'USD' }}</div> }
        </div>
      }

      @if (errors().length) {
        <div class="errors" role="alert" data-testid="panel-errors">
          <mat-icon>error</mat-icon>
          <ul>@for (e of errors(); track e) { <li>{{ e }}</li> }</ul>
        </div>
      }

      <div class="footer">
        <button mat-button type="button" mat-dialog-close [disabled]="busy()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="busy() || !valid()" data-testid="panel-submit">
          @if (busy()) { <mat-progress-spinner diameter="18" mode="indeterminate" /> } @else { Submit reserve }
        </button>
      </div>
    </form>
  `,
  styles: `
    :host { display: flex; flex-direction: column; height: 100%; }
    .panel-head { display: flex; align-items: center; justify-content: space-between; padding: 12px 8px 4px 24px; }
    .panel-head h2 { margin: 0; font: var(--mat-sys-title-large); }
    .panel-body { display: flex; flex-direction: column; gap: 8px; padding: 12px 24px 24px; overflow-y: auto; flex: 1; }
    .authority { padding: 10px 14px; border-radius: 8px; font-weight: 500; }
    .authority .small { font-weight: 400; font-size: 12px; margin-top: 2px; }
    .authority-auto { background: var(--status-closed-bg); color: var(--status-closed-fg); }
    .authority-supervisor, .authority-manager { background: var(--status-reopened-bg); color: var(--status-reopened-fg); }
    .errors { display: flex; gap: 8px; background: var(--status-danger-bg); color: var(--status-danger-fg); border-radius: 8px; padding: 10px 14px; }
    .errors ul { margin: 0; padding-left: 18px; }
    .footer { display: flex; justify-content: flex-end; gap: 8px; margin-top: auto; padding-top: 16px; }
  `,
})
export class ReservePanel {
  protected readonly data = inject<ReservePanelData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ReservePanel, ReserveSubmission | null>>(MatDialogRef);
  private readonly api = inject(ReservesApi);

  protected readonly components = COMPONENTS;
  protected readonly splitCamelCase = splitCamelCase;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    component: ['Indemnity' as ReserveComponentType],
    type: ['Add' as ReserveTransactionType],
    amount: new FormControl<number | null>(null),
    reason: [''],
  });

  protected readonly busy = signal(false);
  protected readonly errors = signal<string[]>([]);

  private readonly component = toSignal(this.form.controls.component.valueChanges, { initialValue: 'Indemnity' as ReserveComponentType });
  private readonly type = toSignal(this.form.controls.type.valueChanges, { initialValue: 'Add' as ReserveTransactionType });
  private readonly amount = toSignal(this.form.controls.amount.valueChanges, { initialValue: null as number | null });
  private readonly reason = toSignal(this.form.controls.reason.valueChanges, { initialValue: '' });

  protected readonly allowsNegative = computed(() => allowsNegative(this.component()));
  protected readonly balance = computed(() =>
    this.data.components.find(c => c.component === this.component())?.currentAmount ?? 0);

  private readonly amountOk = computed(() => isValidReserveAmount(this.component(), this.amount()));

  /** The signed change that will be applied to the balance once approved. */
  private readonly delta = computed(() => {
    const a = this.amount();
    return a === null || !this.amountOk() ? null : this.type() === 'Reverse' ? -a : a;
  });
  protected readonly newBalance = computed(() => this.delta() === null ? null : this.balance() + this.delta()!);

  protected readonly belowZero = computed(() =>
    !this.allowsNegative() && this.newBalance() !== null && this.newBalance()! < 0);

  protected readonly authority = computed(() => {
    const delta = this.delta();
    if (delta === null) return null;
    const level = requiredAuthority(delta);
    return { level, message: authorityMessage(level) };
  });

  protected readonly showAmountError = computed(() =>
    (this.amount() !== null && !this.amountOk()) || this.belowZero());
  protected readonly amountError = computed(() =>
    this.belowZero() ? 'The reserve balance cannot go below zero.' : 'Reserve amount must be greater than zero.');

  protected readonly valid = computed(() => this.amountOk() && !this.belowZero() && this.reason().trim().length > 0);

  protected submit(): void {
    this.form.markAllAsTouched();
    if (!this.valid()) return;

    const f = this.form.getRawValue();
    this.busy.set(true);
    this.errors.set([]);
    this.api.submit(this.data.claimId, {
      component: f.component, amount: f.amount!, changeReason: f.reason.trim(), transactionType: f.type,
    }).subscribe({
      next: result => this.ref.close(result),
      error: (e: unknown) => {
        this.busy.set(false);
        this.errors.set(e instanceof ApiException ? (e.allMessages.length ? e.allMessages : [e.message]) : ['The reserve could not be saved.']);
      },
    });
  }
}
