import { Component, input } from '@angular/core';

/** Placeholder lines shown while data loads. */
@Component({
  selector: 'app-skeleton',
  template: `@for (w of widths(); track $index) { <span class="skeleton" [style.width]="w"></span> }`,
  styles: `:host { display: block; }`,
})
export class Skeleton {
  readonly lines = input(4);

  protected widths(): string[] {
    const pattern = ['92%', '78%', '85%', '60%', '88%', '70%'];
    return Array.from({ length: this.lines() }, (_, i) => pattern[i % pattern.length]);
  }
}
