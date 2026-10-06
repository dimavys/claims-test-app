import { Injectable, computed, signal } from '@angular/core';

/** Counts in-flight requests; the shell shows a progress bar while any are pending. */
@Injectable({ providedIn: 'root' })
export class Loading {
  private readonly pending = signal(0);
  readonly active = computed(() => this.pending() > 0);

  begin(): void { this.pending.update(n => n + 1); }
  end(): void { this.pending.update(n => Math.max(0, n - 1)); }
}
