import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

/** One place for user feedback so every message looks and behaves the same. */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void { this.open(message, 'snack-success', 4000); }
  info(message: string): void { this.open(message, 'snack-info', 4000); }
  warn(message: string): void { this.open(message, 'snack-warn', 7000); }
  error(message: string): void { this.open(message, 'snack-error', 9000); }

  private open(message: string, panelClass: string, duration: number): void {
    this.snackBar.open(message, 'Dismiss', {
      duration,
      panelClass,
      horizontalPosition: 'center',
      verticalPosition: 'bottom',
    });
  }
}
