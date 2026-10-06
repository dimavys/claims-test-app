import { Component } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { inject } from '@angular/core';

/** Asks for the rejection reason; closes with the text, or undefined when cancelled. */
@Component({
  selector: 'app-reject-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Reject reserve</h2>
    <mat-dialog-content>
      <p>The submitter will see this reason and can submit a revised amount. The rejected entry stays in the history.</p>
      <mat-form-field>
        <mat-label>Rejection reason</mat-label>
        <textarea matInput rows="3" [formControl]="reason" data-testid="reject-reason"></textarea>
        @if (reason.touched && reason.hasError('required')) { <mat-error>A rejection reason is required.</mat-error> }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button (click)="confirm()" data-testid="reject-confirm">Reject</button>
    </mat-dialog-actions>
  `,
  styles: `mat-form-field { width: 100%; min-width: 420px; } p { margin-top: 0; }`,
})
export class RejectDialog {
  private readonly ref = inject<MatDialogRef<RejectDialog, string>>(MatDialogRef);
  protected readonly reason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] });

  protected confirm(): void {
    this.reason.markAsTouched();
    if (this.reason.valid) this.ref.close(this.reason.value.trim());
  }
}
