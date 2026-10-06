import { Routes } from '@angular/router';

export const CLAIM_DETAIL_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./claim-detail').then(m => m.ClaimDetailPage), title: 'Claim' },
];
