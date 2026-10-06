import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ClaimsApi } from '../../../core/api/claims-api';
import { Notifier } from '../../../core/http/notifier';
import { ClaimParty, PartyInput, PartyRole, PartyType } from '../../../core/models/api.models';
import { splitCamelCase } from '../../../core/util/domain';
import { Badge } from '../../../shared/badge';
import { ConfirmDialog } from '../../../shared/confirm-dialog';
import { ClaimDetailStore } from '../claim-detail.store';

const ROLES: PartyRole[] = ['Claimant', 'Insured', 'ThirdParty', 'Witness', 'Attorney'];

@Component({
  selector: 'app-parties-tab',
  imports: [
    ReactiveFormsModule, MatTableModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatTooltipModule, Badge,
  ],
  template: `
    <div class="wrap">
      <div class="head">
        <h2>Parties</h2>
        <button mat-stroked-button (click)="open(true)" [disabled]="adding()" data-testid="add-party-button"><mat-icon>person_add</mat-icon> Add party</button>
      </div>

      @if (adding()) {
        <form class="inline-form" [formGroup]="form" (ngSubmit)="save()" data-testid="party-inline-form">
          <mat-form-field>
            <mat-label>Role</mat-label>
            <mat-select formControlName="partyRole" data-testid="new-party-role">
              @for (r of roles; track r) { <mat-option [value]="r">{{ splitCamelCase(r) }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Type</mat-label>
            <mat-select formControlName="partyType">
              <mat-option value="Person">Person</mat-option>
              <mat-option value="Company">Company</mat-option>
            </mat-select>
          </mat-form-field>
          @if (type() === 'Person') {
            <mat-form-field><mat-label>First name</mat-label><input matInput formControlName="firstName" data-testid="new-party-first" />
              @if (form.controls.firstName.hasError('required')) { <mat-error>First name is required.</mat-error> }</mat-form-field>
            <mat-form-field><mat-label>Last name</mat-label><input matInput formControlName="lastName" data-testid="new-party-last" />
              @if (form.controls.lastName.hasError('required')) { <mat-error>Last name is required.</mat-error> }</mat-form-field>
          } @else {
            <mat-form-field class="span-2"><mat-label>Company name</mat-label><input matInput formControlName="companyName" data-testid="new-party-company" />
              @if (form.controls.companyName.hasError('required')) { <mat-error>Company name is required.</mat-error> }</mat-form-field>
          }
          <mat-form-field><mat-label>Email</mat-label><input matInput type="email" formControlName="email" />
            @if (form.controls.email.hasError('email')) { <mat-error>Enter a valid email address.</mat-error> }</mat-form-field>
          <mat-form-field><mat-label>Phone</mat-label><input matInput formControlName="phone" /></mat-form-field>
          <div class="row-actions">
            <button mat-button type="button" (click)="open(false)">Cancel</button>
            <button mat-flat-button type="submit" [disabled]="busy()" data-testid="new-party-save">Add party</button>
          </div>
        </form>
      }

      <table mat-table [dataSource]="parties()" data-testid="parties-table">
        <ng-container matColumnDef="role">
          <th mat-header-cell *matHeaderCellDef>Role</th>
          <td mat-cell *matCellDef="let p"><app-badge [tone]="p.partyRole === 'Claimant' ? 'open' : 'neutral'">{{ splitCamelCase(p.partyRole) }}</app-badge></td>
        </ng-container>
        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>Name</th>
          <td mat-cell *matCellDef="let p"><strong>{{ p.displayName }}</strong> <span class="muted">· {{ p.partyType }}</span></td>
        </ng-container>
        <ng-container matColumnDef="contact">
          <th mat-header-cell *matHeaderCellDef>Contact</th>
          <td mat-cell *matCellDef="let p">{{ p.email || '' }}{{ p.email && p.phone ? ' · ' : '' }}{{ p.phone || '' }}@if (!p.email && !p.phone) { <span class="muted">—</span> }</td>
        </ng-container>
        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let p"><app-badge [tone]="p.isActive ? 'closed' : 'draft'">{{ p.isActive ? 'Active' : 'Removed' }}</app-badge></td>
        </ng-container>
        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let p" class="end">
            @if (p.isActive) {
              <span [matTooltip]="isLastClaimant(p) ? 'A claim must keep at least one Claimant' : ''">
                <button mat-icon-button [disabled]="isLastClaimant(p) || busy()" (click)="remove(p)" aria-label="Remove party" data-testid="remove-party"><mat-icon>person_remove</mat-icon></button>
              </span>
            }
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.inactive]="!row.isActive"></tr>
      </table>
    </div>
  `,
  styles: `
    .wrap { padding: 20px; }
    .head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
    h2 { margin: 0; font: var(--mat-sys-title-medium); }
    table { width: 100%; }
    .end { text-align: right; }
    .inactive { opacity: 0.55; }
    .inline-form { display: grid; grid-template-columns: repeat(4, 1fr); gap: 4px 12px; padding: 12px; border: 1px dashed var(--mat-sys-outline); border-radius: 10px; margin-bottom: 16px; }
    .span-2 { grid-column: span 2; }
    .row-actions { grid-column: 1 / -1; display: flex; justify-content: flex-end; gap: 8px; }
  `,
})
export class PartiesTab {
  protected readonly store = inject(ClaimDetailStore);
  private readonly api = inject(ClaimsApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly roles = ROLES;
  protected readonly splitCamelCase = splitCamelCase;
  protected readonly columns = ['role', 'name', 'contact', 'status', 'actions'];
  protected readonly parties = computed(() => this.store.claim()?.parties ?? []);
  protected readonly adding = signal(false);
  protected readonly busy = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    partyRole: ['Claimant' as PartyRole],
    partyType: ['Person' as PartyType],
    firstName: [''],
    lastName: [''],
    companyName: [''],
    email: ['', Validators.email],
    phone: [''],
  });
  protected readonly type = toSignal(this.form.controls.partyType.valueChanges, { initialValue: 'Person' as PartyType });

  protected isLastClaimant(party: ClaimParty): boolean {
    return party.partyRole === 'Claimant' && party.isActive && this.store.activeClaimants().length <= 1;
  }

  protected open(value: boolean): void {
    this.adding.set(value);
    if (value) this.form.reset({ partyRole: 'Witness', partyType: 'Person' });
  }

  protected save(): void {
    const v = this.form.getRawValue();
    const person = v.partyType === 'Person';
    this.form.controls.firstName.setErrors(person && !v.firstName.trim() ? { required: true } : null);
    this.form.controls.lastName.setErrors(person && !v.lastName.trim() ? { required: true } : null);
    this.form.controls.companyName.setErrors(!person && !v.companyName.trim() ? { required: true } : null);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }

    const party: PartyInput = {
      partyRole: v.partyRole, partyType: v.partyType,
      firstName: person ? v.firstName.trim() : null, lastName: person ? v.lastName.trim() : null,
      companyName: person ? null : v.companyName.trim(), email: v.email.trim() || null, phone: v.phone.trim() || null,
    };
    const claim = this.store.claim();
    if (!claim) return;

    this.busy.set(true);
    this.api.addParty(claim.id, party).subscribe({
      next: added => { this.busy.set(false); this.adding.set(false); this.notifier.success(`${added.displayName} added.`); this.store.reload(); },
      error: () => this.busy.set(false),
    });
  }

  protected remove(party: ClaimParty): void {
    this.dialog.open(ConfirmDialog, {
      data: { title: 'Remove party?', message: `${party.displayName} will be marked as removed from this claim.`, confirmLabel: 'Remove', destructive: true },
    }).afterClosed().subscribe(ok => {
      const claim = this.store.claim();
      if (!ok || !claim) return;
      this.busy.set(true);
      this.api.removeParty(claim.id, party.id).subscribe({
        next: () => { this.busy.set(false); this.notifier.success('Party removed.'); this.store.reload(); },
        error: () => this.busy.set(false),
      });
    });
  }
}
