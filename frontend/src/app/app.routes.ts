import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { Shell } from './layout/shell';

// Each feature is lazy-loaded as its own route tree (ClaimsList, FnolIntake, ClaimDetail).
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/login/login').then(m => m.Login), title: 'Sign in · Claims' },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'claims' },
      { path: 'claims', loadChildren: () => import('./features/claims-list/claims-list.routes').then(m => m.CLAIMS_LIST_ROUTES) },
      // 'new' must be declared before ':id' so it is not treated as a claim id.
      { path: 'claims/new', loadChildren: () => import('./features/fnol-intake/fnol-intake.routes').then(m => m.FNOL_INTAKE_ROUTES) },
      { path: 'claims/:id', loadChildren: () => import('./features/claim-detail/claim-detail.routes').then(m => m.CLAIM_DETAIL_ROUTES) },
    ],
  },
  { path: '**', redirectTo: '' },
];
