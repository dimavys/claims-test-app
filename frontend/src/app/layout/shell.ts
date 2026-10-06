import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../core/auth/auth';
import { Loading } from '../core/http/loading';
import { Notifier } from '../core/http/notifier';
import { UserInfo } from '../core/models/api.models';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive,
    MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, MatDividerModule, MatProgressBarModule,
  ],
  template: `
    <mat-toolbar class="bar">
      <a class="brand" routerLink="/claims">
        <mat-icon>shield</mat-icon>
        <span>Claims Module</span>
      </a>
      <nav>
        <a mat-button routerLink="/claims" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: false }">Claims</a>
      </nav>
      <span class="spacer"></span>

      @if (user(); as u) {
        <button mat-button class="user" [matMenuTriggerFor]="userMenu" data-testid="user-menu">
          <mat-icon>account_circle</mat-icon>
          <span class="who">
            <strong>{{ u.displayName }}</strong>
            <small class="role role-{{ u.role }}">{{ u.role }}</small>
          </span>
          <mat-icon>arrow_drop_down</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu" xPosition="before">
          @if (switchable().length > 0) {
            <div class="menu-title">Switch role (testing)</div>
            @for (other of switchable(); track other.id) {
              <button mat-menu-item [disabled]="other.id === u.id" (click)="switchTo(other)" [attr.data-testid]="'switch-' + other.userName">
                <mat-icon>{{ other.id === u.id ? 'check' : 'swap_horiz' }}</mat-icon>
                <span>{{ other.displayName }} · {{ other.role }}</span>
              </button>
            }
            <mat-divider />
          }
          <button mat-menu-item (click)="signOut()" data-testid="sign-out"><mat-icon>logout</mat-icon><span>Sign out</span></button>
        </mat-menu>
      }
    </mat-toolbar>

    <div class="progress">
      @if (loading.active()) { <mat-progress-bar mode="indeterminate" /> }
    </div>

    <main><router-outlet /></main>
  `,
  styles: `
    :host { display: block; min-height: 100vh; }
    .bar { position: sticky; top: 0; z-index: 10; gap: 8px; background: var(--mat-sys-primary); color: var(--mat-sys-on-primary);
      --mat-button-text-label-text-color: var(--mat-sys-on-primary); }
    .brand { display: flex; align-items: center; gap: 8px; font: var(--mat-sys-title-large); color: inherit; text-decoration: none; margin-right: 16px; }
    nav a.active { background: rgb(255 255 255 / 0.16); }
    .user { text-align: left; }
    .who { display: inline-flex; flex-direction: column; line-height: 1.15; margin: 0 4px; }
    .who small.role { text-transform: capitalize; opacity: 0.85; font-size: 11px; }
    .menu-title { padding: 8px 16px 4px; font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .progress { height: 4px; position: sticky; top: 64px; z-index: 9; }
  `,
})
export class Shell {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);
  protected readonly loading = inject(Loading);

  protected readonly user = this.auth.user;
  protected readonly switchable = computed(() => this.auth.switchableUsers());

  constructor() {
    this.auth.loadSwitchableUsers();
  }

  protected switchTo(user: UserInfo): void {
    this.auth.switchTo(user.userName).subscribe(() => {
      this.notifier.info(`Now acting as ${user.displayName} (${user.role}).`);
      // Re-run guards and data loads for the new identity (permissions differ per role).
      void this.router.navigateByUrl(this.router.url, { onSameUrlNavigation: 'reload' });
    });
  }

  protected signOut(): void {
    this.auth.logout();
  }
}
