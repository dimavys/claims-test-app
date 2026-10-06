import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

export interface ConfirmDialogData {
  title: string;
  message?: string;
  /** Bullet list shown under the message (e.g. warnings the user is accepting). */
  items?: string[];
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
}

/** Result is `true` on confirm and `false` otherwise. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      @if (data.message) { <p>{{ data.message }}</p> }
      @if (data.items?.length) {
        <ul>@for (item of data.items; track item) { <li>{{ item }}</li> }</ul>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false" data-testid="confirm-cancel">{{ data.cancelLabel ?? 'Cancel' }}</button>
      <button mat-flat-button [mat-dialog-close]="true" [class.destructive]="data.destructive" data-testid="confirm-ok">
        {{ data.confirmLabel ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    p { margin: 0 0 8px; }
    ul { margin: 8px 0 0; padding-left: 20px; }
    li { margin: 4px 0; }
    .destructive { --mat-button-filled-container-color: var(--mat-sys-error); --mat-button-filled-label-text-color: var(--mat-sys-on-error); }
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
}
