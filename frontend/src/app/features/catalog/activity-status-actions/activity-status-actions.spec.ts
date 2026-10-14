import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivityStatusActionsComponent } from './activity-status-actions';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of, throwError } from 'rxjs';
import { Activity, ActivityStatus } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ActivityStatusActionsComponent', () => {
  let component: ActivityStatusActionsComponent;
  let fixture: ComponentFixture<ActivityStatusActionsComponent>;
  let gymApiSpy: {
    getActivity: ReturnType<typeof vi.fn>;
    changeActivityStatus: ReturnType<typeof vi.fn>;
  };

  const activeActivity: Activity = {
    id: 'a1',
    code: 'SPINNING',
    name: 'Spinning',
    status: ActivityStatus.Active,
    isActive: true
  };

  function render(activity: Activity): HTMLElement {
    fixture.componentRef.setInput('activity', activity);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function buttonLabels(el: HTMLElement): string[] {
    return Array.from(el.querySelectorAll('button.status-action')).map(b => b.textContent!.trim());
  }

  beforeEach(async () => {
    gymApiSpy = {
      getActivity: vi.fn(),
      changeActivityStatus: vi.fn().mockReturnValue(of({ changed: true }))
    };

    await TestBed.configureTestingModule({
      imports: [ActivityStatusActionsComponent],
      providers: [{ provide: GymApiService, useValue: gymApiSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(ActivityStatusActionsComponent);
    component = fixture.componentInstance;
  });

  it('offers only valid transitions for each status', () => {
    expect(buttonLabels(render(activeActivity))).toEqual(['Inactivar']);
    expect(buttonLabels(render({ ...activeActivity, status: ActivityStatus.Inactive }))).toEqual(['Activar', 'Archivar']);
    expect(buttonLabels(render({ ...activeActivity, status: ActivityStatus.Archived }))).toEqual(['Restaurar']);
  });

  it('fetches the current version, patches the status and emits changed', () => {
    gymApiSpy.getActivity.mockReturnValue(of({ ...activeActivity, version: 5 }));
    const changedSpy = vi.spyOn(component.changed, 'emit');
    render(activeActivity);

    component.changeStatus(ActivityStatus.Inactive);

    expect(gymApiSpy.getActivity).toHaveBeenCalledWith('a1');
    expect(gymApiSpy.changeActivityStatus).toHaveBeenCalledWith('a1', { status: ActivityStatus.Inactive, version: 5 });
    expect(changedSpy).toHaveBeenCalled();
  });

  it('does not patch when the detail has no version', () => {
    gymApiSpy.getActivity.mockReturnValue(of({ ...activeActivity, version: undefined }));
    render(activeActivity);

    component.changeStatus(ActivityStatus.Inactive);

    expect(gymApiSpy.changeActivityStatus).not.toHaveBeenCalled();
    expect(component.dialog()?.message).toBeTruthy();
  });

  it('does not patch when the detail cannot be loaded', () => {
    gymApiSpy.getActivity.mockReturnValue(throwError(() => ({ status: 500, error: { message: 'Fallo' } })));
    render(activeActivity);

    component.changeStatus(ActivityStatus.Inactive);

    expect(gymApiSpy.changeActivityStatus).not.toHaveBeenCalled();
    expect(component.dialog()?.message).toContain('Fallo');
  });

  it('shows the dependency details when the API answers 409', () => {
    gymApiSpy.getActivity.mockReturnValue(of({ ...activeActivity, version: 1 }));
    gymApiSpy.changeActivityStatus.mockReturnValue(throwError(() => ({
      status: 409,
      error: { message: 'Tiene compromisos futuros.', details: { activeSchedules: 2, futureSessions: 5 } }
    })));
    const changedSpy = vi.spyOn(component.changed, 'emit');
    const el = render(activeActivity);

    component.changeStatus(ActivityStatus.Inactive);
    fixture.detectChanges();

    expect(component.dialog()?.message).toContain('Tiene compromisos futuros.');
    expect(el.textContent).toContain('Turnos programados activos: 2');
    expect(el.textContent).toContain('Clases futuras: 5');
    expect(changedSpy).not.toHaveBeenCalled();
  });
});
