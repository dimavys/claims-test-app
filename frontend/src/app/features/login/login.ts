import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { Auth } from '../../core/auth/auth';
import { ApiException } from '../../core/http/api-error';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="wrap">
      <section class="card login">
        <header>
          <mat-icon class="logo">shield</mat-icon>
          <h1>Claims Module</h1>
          <p class="muted">FNOL intake &amp; reserve management</p>
        </header>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field>
            <mat-label>User name</mat-label>
            <input matInput formControlName="userName" autocomplete="username" data-testid="login-user" />
            @if (form.controls.userName.hasError('required')) { <mat-error>User name is required</mat-error> }
          </mat-form-field>
          <mat-form-field>
            <mat-label>Password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="current-password" data-testid="login-password" />
            @if (form.controls.password.hasError('required')) { <mat-error>Password is required</mat-error> }
          </mat-form-field>

          @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

          <button mat-flat-button type="submit" [disabled]="busy()" data-testid="login-submit">
            @if (busy()) { <mat-progress-spinner diameter="18" mode="indeterminate" /> } @else { Sign in }
          </button>
        </form>

        @if (auth.switchableUsers().length > 0) {
          <div class="quick">
            <p class="muted">Test accounts — sign in with one click</p>
            <div class="chips">
              @for (u of auth.switchableUsers(); track u.id) {
                <button mat-stroked-button type="button" [disabled]="busy()" (click)="quickLogin(u.userName)" [attr.data-testid]="'quick-' + u.userName">
                  {{ u.displayName }} <small>· {{ u.role }}</small>
                </button>
              }
            </div>
          </div>
        }
      </section>
    </div>
  `,
  styles: `
    .wrap { min-height: 100vh; display: grid; place-items: center; padding: 24px; background: linear-gradient(160deg, var(--mat-sys-primary-container), var(--mat-sys-surface-container-low)); }
    .login { width: min(440px, 100%); display: flex; flex-direction: column; gap: 16px; box-shadow: var(--mat-sys-level2); }
    header { text-align: center; }
    header h1 { margin: 4px 0 0; font: var(--mat-sys-headline-small); }
    header p { margin: 4px 0 0; }
    .logo { font-size: 44px; width: 44px; height: 44px; color: var(--mat-sys-primary); }
    form { display: flex; flex-direction: column; gap: 12px; }
    form button { height: 44px; }
    .error { color: var(--mat-sys-error); margin: 0; }
    .quick p { margin: 0 0 8px; text-align: center; font-size: 12px; }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; justify-content: center; }
    small { opacity: 0.7; }
  `,
})
export class Login {
  protected readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  constructor() {
    this.auth.loadSwitchableUsers();
    if (this.auth.isAuthenticated()) void this.router.navigateByUrl(this.returnUrl());
  }

  protected submit(): void {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    const { userName, password } = this.form.getRawValue();
    this.run(this.auth.login(userName, password));
  }

  protected quickLogin(userName: string): void {
    this.run(this.auth.switchTo(userName));
  }

  private run(request: ReturnType<Auth['login']>): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => void this.router.navigateByUrl(this.returnUrl()),
      error: (e: unknown) => {
        this.busy.set(false);
        this.error.set(e instanceof ApiException && e.status === 401 ? 'Invalid user name or password.' : 'Sign in failed. Please try again.');
      },
    });
  }

  private returnUrl(): string {
    const target = this.route.snapshot.queryParamMap.get('returnUrl');
    return target && target.startsWith('/') && !target.startsWith('/login') ? target : '/claims';
  }
}
