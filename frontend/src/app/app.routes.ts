import { Routes } from '@angular/router';
import { HomeComponent } from './features/public/home/home';
import { ActivitiesComponent } from './features/public/activities/activities';
import { SchedulesComponent } from './features/public/schedules/schedules';
import { PlansComponent } from './features/public/plans/plans';
import { LoginComponent } from './features/auth/login/login';
import { authGuard, roleGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'actividades', component: ActivitiesComponent },
  { path: 'horarios', component: SchedulesComponent },
  { path: 'planes', component: PlansComponent },
  { path: 'login', component: LoginComponent },
  {
    path: 'portal/alumno',
    canActivate: [authGuard, roleGuard(['Student'])],
    loadComponent: () => import('./features/portal/student/student-portal').then(m => m.StudentPortalComponent)
  },
  {
    path: 'portal/docente',
    canActivate: [authGuard, roleGuard(['Instructor', 'Secretary', 'Administrator'])],
    loadComponent: () => import('./features/portal/staff/staff-portal').then(m => m.StaffPortalComponent)
  },
  {
    path: 'portal/admin',
    canActivate: [authGuard, roleGuard(['Administrator'])],
    loadComponent: () => import('./features/portal/admin/admin-portal').then(m => m.AdminPortalComponent)
  },
  { path: '**', redirectTo: '' }
];
