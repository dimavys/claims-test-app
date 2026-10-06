import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-empty-state',
  imports: [MatIconModule],
  template: `
    <div class="empty">
      <mat-icon>{{ icon() }}</mat-icon>
      <h3>{{ title() }}</h3>
      @if (message()) { <p class="muted">{{ message() }}</p> }
      <ng-content />
    </div>
  `,
  styles: `
    .empty { display: flex; flex-direction: column; align-items: center; gap: 4px; padding: 40px 16px; text-align: center; }
    mat-icon { font-size: 48px; width: 48px; height: 48px; color: var(--mat-sys-outline); }
    h3 { margin: 8px 0 0; font: var(--mat-sys-title-medium); }
    p { margin: 0 0 12px; }
  `,
})
export class EmptyState {
  readonly icon = input('inbox');
  readonly title = input.required<string>();
  readonly message = input<string>();
}
