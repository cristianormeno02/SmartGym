import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduleFormModalComponent } from './schedule-form-modal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { PeopleService } from '../../../core/services/people.service';
import { of, throwError } from 'rxjs';
import { Activity, ActivityStatus, RecurringSchedule, Room } from '../../../core/models/gym.models';
import { DocumentType, PersonStatus, PagedResult as PersonPagedResult, PersonSummary } from '../../../core/models/person.model';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ScheduleFormModalComponent', () => {
  let component: ScheduleFormModalComponent;
  let fixture: ComponentFixture<ScheduleFormModalComponent>;
  let gymApiSpy: any;
  let peopleServiceSpy: any;

  const mockActivities: Activity[] = [
    {
      id: 'act-1',
      code: 'SPINNING',
      name: 'Spinning Power',
      status: ActivityStatus.Active,
      defaultCapacity: 20,
      isActive: true
    },
    {
      id: 'act-2',
      code: 'YOGA',
      name: 'Yoga Dinámico',
      status: ActivityStatus.Active,
      defaultCapacity: undefined,
      isActive: true
    }
  ];

  const mockRooms: Room[] = [
    { id: 'room-1', name: 'Sala 1', capacity: 25, isActive: true },
    { id: 'room-2', name: 'Sala 2', capacity: 15, isActive: false }
  ];

  const mockInstructors: PersonPagedResult<PersonSummary> = {
    items: [
      {
        id: 'inst-1',
        firstName: 'Laura',
        lastName: 'Gómez',
        document: { type: DocumentType.Dni, number: '20111222', issuingCountry: 'AR', numberNormalized: '20111222' },
        status: PersonStatus.Active,
        hasUserAccount: true
      }
    ],
    pageNumber: 1,
    pageSize: 100,
    totalCount: 1,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false
  };

  const mockSchedule: RecurringSchedule = {
    id: 'sched-1',
    activityId: 'act-1',
    activityName: 'Spinning Power',
    roomId: 'room-1',
    roomName: 'Sala 1',
    instructorId: 'inst-1',
    instructorName: 'Laura Gómez',
    dayOfWeek: 1,
    startTime: '10:00:00',
    endTime: '11:00:00',
    validFrom: '2026-03-01',
    validTo: '2026-12-31',
    maxCapacity: 20,
    isActive: true
  };

  beforeEach(async () => {
    gymApiSpy = {
      getActivities: vi.fn().mockReturnValue(of({ items: mockActivities })),
      getRooms: vi.fn().mockReturnValue(of(mockRooms)),
      createSchedule: vi.fn(),
      updateSchedule: vi.fn()
    };

    peopleServiceSpy = {
      search: vi.fn().mockReturnValue(of(mockInstructors))
    };

    await TestBed.configureTestingModule({
      imports: [ScheduleFormModalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: PeopleService, useValue: peopleServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ScheduleFormModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load only active activities, active rooms, and active instructors on init', () => {
    expect(component).toBeTruthy();
    expect(gymApiSpy.getActivities).toHaveBeenCalledWith(undefined, ActivityStatus.Active, undefined, 1, 100);
    expect(gymApiSpy.getRooms).toHaveBeenCalled();
    expect(peopleServiceSpy.search).toHaveBeenCalledWith(expect.objectContaining({
      role: 'Instructor',
      status: PersonStatus.Active
    }));
  });

  it('should validate startTime < endTime', () => {
    component.form.patchValue({
      startTime: '18:00',
      endTime: '17:00'
    });
    expect(component.form.hasError('timeRangeInvalid')).toBe(true);

    component.form.patchValue({
      startTime: '18:00',
      endTime: '18:00'
    });
    expect(component.form.hasError('timeRangeInvalid')).toBe(true);

    component.form.patchValue({
      startTime: '18:00',
      endTime: '19:00'
    });
    expect(component.form.hasError('timeRangeInvalid')).toBe(false);
  });

  it('should validate validFrom is required and validTo >= validFrom', () => {
    const validFromCtrl = component.form.get('validFrom');
    validFromCtrl?.setValue('');
    expect(validFromCtrl?.hasError('required')).toBe(true);

    component.form.patchValue({
      validFrom: '2026-06-01',
      validTo: '2026-05-01'
    });
    expect(component.form.hasError('dateRangeInvalid')).toBe(true);

    component.form.patchValue({
      validFrom: '2026-06-01',
      validTo: '2026-06-01'
    });
    expect(component.form.hasError('dateRangeInvalid')).toBe(false);

    component.form.patchValue({
      validFrom: '2026-06-01',
      validTo: '2026-07-01'
    });
    expect(component.form.hasError('dateRangeInvalid')).toBe(false);
  });

  it('should require maxCapacity when selected activity does NOT have defaultCapacity', () => {
    component.form.patchValue({
      activityId: 'act-2', // has no defaultCapacity
      maxCapacity: null
    });
    component.onActivityChange();

    expect(component.isCapacityRequired()).toBe(true);
    expect(component.form.get('maxCapacity')?.hasError('required')).toBe(true);

    component.form.patchValue({
      activityId: 'act-1', // has defaultCapacity 20
      maxCapacity: null
    });
    component.onActivityChange();

    expect(component.isCapacityRequired()).toBe(false);
  });

  it('should submit create request on alta and emit saved', () => {
    gymApiSpy.createSchedule.mockReturnValue(of(mockSchedule));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.form.patchValue({
      activityId: 'act-1',
      roomId: 'room-1',
      instructorId: 'inst-1',
      dayOfWeek: 1,
      startTime: '10:00',
      endTime: '11:00',
      validFrom: '2026-03-01',
      validTo: '2026-12-31',
      maxCapacity: 20
    });

    component.onSubmit();

    expect(gymApiSpy.createSchedule).toHaveBeenCalledWith(expect.objectContaining({
      activityId: 'act-1',
      roomId: 'room-1',
      instructorId: 'inst-1',
      secretaryId: null,
      dayOfWeek: 1,
      startTime: '10:00:00',
      endTime: '11:00:00',
      validFrom: '2026-03-01',
      validTo: '2026-12-31',
      maxCapacity: 20
    }));
    expect(emitSpy).toHaveBeenCalledWith(mockSchedule);
  });

  it('in edit mode: should disable activityId field and submit updateSchedule without activityId', () => {
    gymApiSpy.updateSchedule.mockReturnValue(of(mockSchedule));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.schedule = mockSchedule;
    component.ngOnChanges();

    expect(component.isEditMode).toBe(true);
    expect(component.form.get('activityId')?.disabled).toBe(true);

    component.form.patchValue({
      roomId: 'room-1',
      instructorId: 'inst-1',
      dayOfWeek: 2,
      startTime: '11:00',
      endTime: '12:00'
    });

    component.onSubmit();

    expect(gymApiSpy.updateSchedule).toHaveBeenCalledWith('sched-1', expect.objectContaining({
      roomId: 'room-1',
      instructorId: 'inst-1',
      secretaryId: null,
      dayOfWeek: 2,
      startTime: '11:00:00',
      endTime: '12:00:00'
    }));
    expect(emitSpy).toHaveBeenCalledWith(mockSchedule);
  });

  it('should default validFrom to today (local date) on create', () => {
    const now = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    const today = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;

    expect(component.form.get('validFrom')?.value).toBe(today);
  });

  it('should not expose an isActive control and always send isActive true on update', () => {
    gymApiSpy.updateSchedule.mockReturnValue(of(mockSchedule));
    expect(component.form.get('isActive')).toBeNull();

    component.schedule = mockSchedule;
    component.ngOnChanges();
    component.onSubmit();

    expect(gymApiSpy.updateSchedule).toHaveBeenCalledWith('sched-1', expect.objectContaining({ isActive: true }));
  });

  it('in edit mode: should keep the current room and instructor selectable even if no longer active', () => {
    component.schedule = {
      ...mockSchedule,
      roomId: 'room-2',
      roomName: 'Sala 2',
      instructorId: 'inst-gone',
      instructorName: 'Pedro Ruiz'
    };
    component.ngOnChanges();

    expect(component.roomOptions().map(r => r.id)).toEqual(['room-1', 'room-2']);
    expect(component.instructorOptions().map(i => i.id)).toEqual(['inst-1', 'inst-gone']);
    expect(component.instructorOptions().find(i => i.id === 'inst-gone')?.label).toBe('Pedro Ruiz');
  });

  it('in create mode: room and instructor options only include active ones', () => {
    expect(component.roomOptions().map(r => r.id)).toEqual(['room-1']);
    expect(component.instructorOptions().map(i => i.id)).toEqual(['inst-1']);
  });

  it('should display error message without closing modal on 409 collision', () => {
    gymApiSpy.createSchedule.mockReturnValue(throwError(() => ({
      status: 409,
      error: { message: 'Superposición de horario en la sala.' }
    })));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.form.patchValue({
      activityId: 'act-1',
      roomId: 'room-1',
      instructorId: 'inst-1',
      dayOfWeek: 1,
      startTime: '10:00',
      endTime: '11:00',
      validFrom: '2026-03-01'
    });

    component.onSubmit();

    expect(component.errorMessage()).toContain('Superposición de horario en la sala.');
    expect(emitSpy).not.toHaveBeenCalled();
  });

  describe('secretary', () => {
    it('should load secretary options from people with Secretary and Administrator roles', () => {
      expect(peopleServiceSpy.search).toHaveBeenCalledWith(expect.objectContaining({ role: 'Secretary', status: PersonStatus.Active }));
      expect(peopleServiceSpy.search).toHaveBeenCalledWith(expect.objectContaining({ role: 'Administrator', status: PersonStatus.Active }));
      expect(component.secretaryOptions().some(o => o.id === 'inst-1')).toBe(true);
    });

    it('should send the selected secretary on create', () => {
      gymApiSpy.createSchedule.mockReturnValue(of(mockSchedule));

      component.form.patchValue({
        activityId: 'act-1',
        roomId: 'room-1',
        instructorId: 'inst-1',
        secretaryId: 'inst-1',
        dayOfWeek: 1,
        startTime: '10:00',
        endTime: '11:00',
        validFrom: '2026-03-01'
      });

      component.onSubmit();

      expect(gymApiSpy.createSchedule).toHaveBeenCalledWith(expect.objectContaining({ secretaryId: 'inst-1' }));
    });

    it('in edit mode: should preserve the assigned secretary and keep it as an option even if not listed', () => {
      gymApiSpy.updateSchedule.mockReturnValue(of(mockSchedule));
      component.schedule = { ...mockSchedule, secretaryId: 'sec-legacy', secretaryName: 'Ana Pérez' };
      component.ngOnChanges();

      expect(component.form.get('secretaryId')?.value).toBe('sec-legacy');
      expect(component.secretaryOptions()).toContainEqual({ id: 'sec-legacy', label: 'Ana Pérez' });

      component.onSubmit();

      expect(gymApiSpy.updateSchedule).toHaveBeenCalledWith('sched-1', expect.objectContaining({ secretaryId: 'sec-legacy' }));
    });

    it('in edit mode: should send null when the secretary is cleared', () => {
      gymApiSpy.updateSchedule.mockReturnValue(of(mockSchedule));
      component.schedule = { ...mockSchedule, secretaryId: 'sec-legacy', secretaryName: 'Ana Pérez' };
      component.ngOnChanges();

      component.form.patchValue({ secretaryId: '' });
      component.onSubmit();

      expect(gymApiSpy.updateSchedule).toHaveBeenCalledWith('sched-1', expect.objectContaining({ secretaryId: null }));
    });
  });
});
