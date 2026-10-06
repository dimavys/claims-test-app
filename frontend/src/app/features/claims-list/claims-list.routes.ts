import { Routes } from '@angular/router';

export const CLAIMS_LIST_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./claims-list').then(m => m.ClaimsList), title: 'Claims' },
];
