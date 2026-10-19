import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduleCalendarComponent } from './schedule-calendar';
import { RecurringSchedule } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ScheduleCalendarComponent', () => {
  let component: ScheduleCalendarComponent;
  let fixture: ComponentFixture<ScheduleCalendarComponent>;

  const mockSchedules: RecurringSchedule[] = [
    {
      id: 'sched-1',
      activityId: 'act-1',
      activityName: 'Spinning',
      roomId: 'room-1',
      roomName: 'Sala 1',
      instructorId: 'inst-1',
      instructorName: 'Laura Gómez',
      dayOfWeek: 1, // Lunes
      startTime: '09:00:00',
      endTime: '10:00:00',
      validFrom: '2026-01-01',
      validTo: '2026-12-31',
      maxCapacity: 20,
      isActive: true
    },
    {
      id: 'sched-2',
      activityId: 'act-2',
      activityName: 'Yoga',
      roomId: 'room-2',
      roomName: 'Sala 2',
      instructorId: 'inst-2',
      instructorName: 'Carlos Ruiz',
      dayOfWeek: 1, // Lunes (solapa con sched-1 de 09:30 a 10:00)
      startTime: '09:30:00',
      endTime: '10:30:00',
      validFrom: '2026-01-01',
      validTo: undefined,
      maxCapacity: 15,
      isActive: true
    },
    {
      id: 'sched-3',
      activityId: 'act-3',
      activityName: 'Funcional',
      roomId: 'room-1',
      roomName: 'Sala 1',
      instructorId: 'inst-1',
      instructorName: 'Laura Gómez',
      dayOfWeek: 2, // Martes
      startTime: '18:00:00',
      endTime: '19:30:00',
      validFrom: '2026-05-01', // Futuro
      validTo: undefined,
      maxCapacity: 20,
      isActive: true
    },
    {
      id: 'sched-4',
      activityId: 'act-4',
      activityName: 'Pilates',
      roomId: 'room-3',
      roomName: 'Sala 3',
      instructorId: 'inst-3',
      instructorName: 'Ana Torres',
      dayOfWeek: 5, // Viernes
      startTime: '08:00:00',
      endTime: '09:00:00',
      validFrom: '2026-01-01',
      validTo: '2026-02-01', // Pasado
      maxCapacity: 12,
      isActive: true
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ScheduleCalendarComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(ScheduleCalendarComponent);
    component = fixture.componentInstance;
    component.schedules = mockSchedules;
    // Fijar la semana de referencia: Lunes 16 de Marzo de 2026 (dentro de vigencia de sched-1 y sched-2)
    component.setReferenceDate(new Date('2026-03-16T12:00:00Z'));
    fixture.detectChanges();
  });

  it('should create the component and render week header and navigation controls', () => {
    expect(component).toBeTruthy();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-prev-week')).not.toBeNull();
    expect(compiled.querySelector('.btn-next-week')).not.toBeNull();
    expect(compiled.querySelector('.btn-current-week')).not.toBeNull();
  });

  it('should filter schedules by validity overlapping visible week and update on week change', () => {
    // Semana del 16 al 22 de Marzo 2026:
    // sched-1 (2026-01-01 a 2026-12-31) -> Visible
    // sched-2 (2026-01-01 en adelante) -> Visible
    // sched-3 (desde 2026-05-01) -> No visible
    // sched-4 (hasta 2026-02-01) -> No visible
    let visible = component.visibleSchedules();
    expect(visible.map(s => s.id)).toEqual(['sched-1', 'sched-2']);

    // Navegar a Mayo 2026 (por ejemplo semana del 4 al 10 de Mayo 2026)
    component.setReferenceDate(new Date('2026-05-04T12:00:00Z'));
    visible = component.visibleSchedules();
    expect(visible.some(s => s.id === 'sched-3')).toBe(true);

    // Navegar a Enero 2026 (semana del 12 al 18 de Enero 2026)
    component.setReferenceDate(new Date('2026-01-12T12:00:00Z'));
    visible = component.visibleSchedules();
    expect(visible.some(s => s.id === 'sched-4')).toBe(true);
  });

  it('should calculate dynamic visible hour range based on visible schedules', () => {
    // Para la semana del 16 de Marzo 2026:
    // Visible schedules: sched-1 (09:00 - 10:00) y sched-2 (09:30 - 10:30)
    // El rango calculado debe cubrir al menos desde las 9 hasta las 11
    const range = component.hourRange();
    expect(range.startHour).toBeLessThanOrEqual(9);
    expect(range.endHour).toBeGreaterThanOrEqual(11);
  });

  it('should default hour range to 7-22 when no schedules are visible', () => {
    component.schedules = [];
    const range = component.hourRange();
    expect(range.startHour).toBe(7);
    expect(range.endHour).toBe(22);
  });

  it('should compute position top and height in pixels based on startTime and duration', () => {
    const layoutItems = component.dayLayouts().get(1) || []; // Lunes
    expect(layoutItems.length).toBe(2);

    const item1 = layoutItems.find(i => i.schedule.id === 'sched-1')!;
    const item2 = layoutItems.find(i => i.schedule.id === 'sched-2')!;

    // sched-1: 60 minutos de duración
    // sched-2: 60 minutos de duración
    expect(item1.durationMinutes).toBe(60);
    expect(item2.durationMinutes).toBe(60);
    expect(item1.heightPx).toBe(item2.heightPx);

    // sched-2 inicia 30 minutos más tarde que sched-1, su topPx debe ser mayor
    expect(item2.topPx).toBeGreaterThan(item1.topPx);
  });

  it('should partition overlapping schedules in the same day into subcolumns', () => {
    const layoutItems = component.dayLayouts().get(1) || []; // Lunes
    const item1 = layoutItems.find(i => i.schedule.id === 'sched-1')!;
    const item2 = layoutItems.find(i => i.schedule.id === 'sched-2')!;

    // Al solaparse de 09:30 a 10:00, deben tener totalCols = 2 y colIndex distintos
    expect(item1.totalCols).toBe(2);
    expect(item2.totalCols).toBe(2);
    expect(item1.colIndex).not.toBe(item2.colIndex);
    expect(item1.widthPercent).toBe(50);
    expect(item2.widthPercent).toBe(50);
  });

  it('should assign a consistent color class based on activityId', () => {
    const color1A = component.getActivityColorClass('act-1');
    const color1B = component.getActivityColorClass('act-1');
    const color2 = component.getActivityColorClass('act-2');

    expect(color1A).toBe(color1B); // Idéntica para el mismo activityId
    expect(typeof color1A).toBe('string');
    expect(color1A.length).toBeGreaterThan(0);
  });

  it('should emit scheduleSelected when clicking on a schedule block', () => {
    const selectedSpy = vi.spyOn(component.scheduleSelected, 'emit');
    component.onSelectSchedule(mockSchedules[0]);
    expect(selectedSpy).toHaveBeenCalledWith(mockSchedules[0]);
  });

  it('should support day selection in compact mobile view', () => {
    expect(component.selectedMobileDay()).toBe(1); // Lunes por defecto
    component.selectMobileDay(3); // Miércoles
    expect(component.selectedMobileDay()).toBe(3);
  });
});
