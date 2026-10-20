import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduleDetailModalComponent } from './schedule-detail-modal';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ScheduleDetailModalComponent', () => {
  let component: ScheduleDetailModalComponent;
  let fixture: ComponentFixture<ScheduleDetailModalComponent>;

  const mockSchedule: RecurringSchedule = {
    id: 'sched-1',
    activityId: 'act-1',
    activityName: 'Entrenamiento Funcional',
    roomId: 'room-1',
    roomName: 'Sala de Musculación',
    instructorId: 'inst-1',
    instructorName: 'Laura Gómez',
    dayOfWeek: 1, // Lunes
    startTime: '09:30:00',
    endTime: '10:30:00',
    validFrom: '2026-03-01',
    validTo: '2026-12-31',
    maxCapacity: 25,
    isActive: true
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ScheduleDetailModalComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ScheduleDetailModalComponent);
    component = fixture.componentInstance;
    component.schedule = mockSchedule;
    fixture.detectChanges();
  });

  it('should display complete schedule information', () => {
    expect(component).toBeTruthy();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Entrenamiento Funcional');
    expect(text).toContain('Sala de Musculación');
    expect(text).toContain('Laura Gómez');
    expect(text).toContain('09:30');
    expect(text).toContain('10:30');
    expect(text).toContain('Lunes');
    expect(text).toContain('25');
  });

  it('should hide Edit and Delete action buttons when canManage is false', () => {
    component.canManage = false;
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-edit-detail')).toBeNull();
    expect(compiled.querySelector('.btn-delete-detail')).toBeNull();
  });

  it('should show Edit and Delete action buttons when canManage is true', () => {
    component.canManage = true;
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-edit-detail')).not.toBeNull();
    expect(compiled.querySelector('.btn-delete-detail')).not.toBeNull();
  });

  it('should emit edit when Edit button is clicked', () => {
    component.canManage = true;
    fixture.detectChanges();

    const editSpy = vi.spyOn(component.edit, 'emit');
    const editBtn = fixture.nativeElement.querySelector('.btn-edit-detail') as HTMLButtonElement;
    editBtn.click();

    expect(editSpy).toHaveBeenCalledWith(mockSchedule);
  });

  it('should emit delete when Delete button is clicked', () => {
    component.canManage = true;
    fixture.detectChanges();

    const deleteSpy = vi.spyOn(component.delete, 'emit');
    const deleteBtn = fixture.nativeElement.querySelector('.btn-delete-detail') as HTMLButtonElement;
    deleteBtn.click();

    expect(deleteSpy).toHaveBeenCalledWith(mockSchedule);
  });

  it('should emit closed when Close button is clicked', () => {
    const closedSpy = vi.spyOn(component.closed, 'emit');
    const closeBtn = fixture.nativeElement.querySelector('.btn-close-modal') as HTMLButtonElement;
    closeBtn.click();

    expect(closedSpy).toHaveBeenCalled();
  });

  it('should display the secretary when the schedule has one', () => {
    component.schedule = { ...mockSchedule, secretaryId: 'sec-1', secretaryName: 'Ana Pérez' };
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Ana Pérez');
  });

  it('should not render the secretary block when the schedule has none', () => {
    expect(fixture.nativeElement.querySelector('.detail-secretary')).toBeNull();
  });
});
