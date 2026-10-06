import { Routes } from '@angular/router';

export const FNOL_INTAKE_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./fnol-intake').then(m => m.FnolIntake), title: 'Log new claim' },
];
