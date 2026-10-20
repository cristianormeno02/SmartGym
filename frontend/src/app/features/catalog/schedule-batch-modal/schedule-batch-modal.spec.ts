import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduleBatchModalComponent } from './schedule-batch-modal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { PeopleService } from '../../../core/services/people.service';
import { of, throwError } from 'rxjs';
import { Activity, ActivityStatus, RecurringSchedule, Room, ScheduleConflict, ScheduleConflictKind } from '../../../core/models/gym.models';
import { DocumentType, PersonStatus, PagedResult as PersonPagedResult, PersonSummary } from '../../../core/models/person.model';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('ScheduleBatchModalComponent', () => {
  let component: ScheduleBatchModalComponent;
  let fixture: ComponentFixture<ScheduleBatchModalComponent>;
  let gymApiSpy: any;
  let peopleServiceSpy: any;

  const mockActivities: Activity[] = [
    {
      id: 'act-1',
      code: 'FUNCIONAL',
      name: 'Entrenamiento Funcional',
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

  beforeEach(async () => {
    gymApiSpy = {
      getActivities: vi.fn().mockReturnValue(of({ items: mockActivities })),
      getRooms: vi.fn().mockReturnValue(of(mockRooms)),
      createBatchSchedules: vi.fn()
    };

    peopleServiceSpy = {
      search: vi.fn().mockReturnValue(of(mockInstructors))
    };

    await TestBed.configureTestingModule({
      imports: [ScheduleBatchModalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy },
        { provide: PeopleService, useValue: peopleServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ScheduleBatchModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create the component and initialize with one slot group', () => {
    expect(component).toBeTruthy();
    expect(component.slotGroups.length).toBe(1);
    expect(component.activities().length).toBe(2);
    expect(component.roomOptions().length).toBe(1); // Only active rooms
    expect(component.instructorOptions().length).toBe(1);
  });

  it('should allow adding and removing slot groups with a minimum of 1 group', () => {
    expect(component.slotGroups.length).toBe(1);
    component.addSlotGroup();
    expect(component.slotGroups.length).toBe(2);
    component.removeSlotGroup(1);
    expect(component.slotGroups.length).toBe(1);
    // Attempting to remove the last group should do nothing
    component.removeSlotGroup(0);
    expect(component.slotGroups.length).toBe(1);
  });

  it('should autocomplete endTime with 60 min for first group and previous duration for subsequent groups', () => {
    const group0 = component.slotGroups.at(0);
    group0.get('startTime')?.setValue('09:30');
    component.onStartTimeChanged(0);
    expect(group0.get('endTime')?.value).toBe('10:30');

    // Change duration to 45 mins
    group0.get('endTime')?.setValue('10:15');

    // Add second group
    component.addSlotGroup();
    const group1 = component.slotGroups.at(1);
    group1.get('startTime')?.setValue('16:00');
    component.onStartTimeChanged(1);
    // Should inherit 45 min duration from previous group: 16:45
    expect(group1.get('endTime')?.value).toBe('16:45');
  });

  it('should compute previews and total count correctly', () => {
    // Group 0: Lun (1), Mié (3), Vie (5) from 09:30 to 10:30
    const group0 = component.slotGroups.at(0);
    group0.patchValue({
      days: [1, 3, 5],
      startTime: '09:30',
      endTime: '10:30'
    });

    // Group 1: Mar (2), Jue (4) from 16:00 to 17:00
    component.addSlotGroup();
    const group1 = component.slotGroups.at(1);
    group1.patchValue({
      days: [2, 4],
      startTime: '16:00',
      endTime: '17:00'
    });

    const preview = component.previewSlots();
    expect(preview.length).toBe(5);
    expect(component.totalSlotsCount()).toBe(5);

    // Verify ordering: Lun 9:30, Mar 16:00, Mié 9:30, Jue 16:00, Vie 9:30
    expect(preview[0].dayOfWeek).toBe(1);
    expect(preview[1].dayOfWeek).toBe(2);
    expect(preview[2].dayOfWeek).toBe(3);
    expect(preview[3].dayOfWeek).toBe(4);
    expect(preview[4].dayOfWeek).toBe(5);
  });

  it('should validate that slot groups require at least one day', () => {
    const group0 = component.slotGroups.at(0);
    group0.patchValue({
      days: [],
      startTime: '09:30',
      endTime: '10:30'
    });

    expect(group0.valid).toBe(false);
    expect(group0.hasError('noDaysSelected')).toBe(true);
  });

  it('should validate that endTime must be greater than startTime', () => {
    const group0 = component.slotGroups.at(0);
    group0.patchValue({
      days: [1],
      startTime: '10:30',
      endTime: '09:30'
    });

    expect(group0.valid).toBe(false);
    expect(group0.hasError('timeRangeInvalid')).toBe(true);
  });

  it('should detect overlap between groups that share the same day', () => {
    const group0 = component.slotGroups.at(0);
    group0.patchValue({
      days: [1, 3],
      startTime: '09:30',
      endTime: '11:00'
    });

    component.addSlotGroup();
    const group1 = component.slotGroups.at(1);
    group1.patchValue({
      days: [1, 5],
      startTime: '10:00',
      endTime: '11:30'
    });

    // Lun (1) has 09:30-11:00 and 10:00-11:30 (overlap!)
    expect(component.form.hasError('slotOverlap')).toBe(true);
  });

  it('should validate maximum 50 total slots', () => {
    // Fill with many days/groups exceeding 50
    const group0 = component.slotGroups.at(0);
    group0.patchValue({
      days: [1, 2, 3, 4, 5, 6, 0],
      startTime: '08:00',
      endTime: '09:00'
    });

    // Add 7 more groups of 7 days -> 8 * 7 = 56 slots
    for (let i = 1; i < 8; i++) {
      component.addSlotGroup();
      component.slotGroups.at(i).patchValue({
        days: [1, 2, 3, 4, 5, 6, 0],
        startTime: `${String(8 + i).padStart(2, '0')}:00`,
        endTime: `${String(9 + i).padStart(2, '0')}:00`
      });
    }

    expect(component.totalSlotsCount()).toBe(56);
    expect(component.form.hasError('maxSlotsExceeded')).toBe(true);
  });

  it('should require maxCapacity when selected activity has no defaultCapacity', () => {
    component.form.patchValue({
      activityId: 'act-2' // Yoga has no defaultCapacity
    });
    component.onActivityChange();

    expect(component.isCapacityRequired()).toBe(true);
    const capacityCtrl = component.form.get('maxCapacity');
    capacityCtrl?.setValue(null);
    expect(capacityCtrl?.valid).toBe(false);
    expect(capacityCtrl?.hasError('required')).toBe(true);
  });

  it('should call gymApi.createBatchSchedules and emit saved on 201', () => {
    const savedSpy = vi.spyOn(component.saved, 'emit');
    const mockCreatedSchedules: RecurringSchedule[] = [
      {
        id: 'sched-1',
        activityId: 'act-1',
        activityName: 'Entrenamiento Funcional',
        roomId: 'room-1',
        roomName: 'Sala 1',
        instructorId: 'inst-1',
        instructorName: 'Laura Gómez',
        dayOfWeek: 1,
        startTime: '09:30:00',
        endTime: '10:30:00',
        validFrom: '2026-03-01',
        validTo: undefined,
        maxCapacity: 20,
        isActive: true
      }
    ];

    gymApiSpy.createBatchSchedules.mockReturnValue(of(mockCreatedSchedules));

    component.form.patchValue({
      activityId: 'act-1',
      roomId: 'room-1',
      instructorId: 'inst-1',
      validFrom: '2026-03-01',
      validTo: '',
      maxCapacity: 20
    });

    const group0 = component.slotGroups.at(0);
    group0.patchValue({
      days: [1],
      startTime: '09:30',
      endTime: '10:30'
    });

    component.onSubmit();

    expect(gymApiSpy.createBatchSchedules).toHaveBeenCalledWith({
      activityId: 'act-1',
      roomId: 'room-1',
      instructorId: 'inst-1',
      secretaryId: null,
      validFrom: '2026-03-01',
      validTo: null,
      maxCapacity: 20,
      slots: [
        {
          daysOfWeek: [1],
          startTime: '09:30:00',
          endTime: '10:30:00'
        }
      ]
    });

    expect(savedSpy).toHaveBeenCalledWith(mockCreatedSchedules);
  });

  it('should handle 409 conflict, keep modal open, retain inputs, and mark conflicting slots in preview', () => {
    const conflicts: ScheduleConflict[] = [
      {
        dayOfWeek: 1,
        startTime: '09:30:00',
        endTime: '10:30:00',
        kind: ScheduleConflictKind.Room,
        conflictingScheduleId: 'other-sched',
        conflictingActivityName: 'Pilates',
        conflictingStartTime: '09:00:00',
        conflictingEndTime: '10:00:00'
      }
    ];

    gymApiSpy.createBatchSchedules.mockReturnValue(
      throwError(() => ({
        status: 409,
        error: {
          message: 'Se detectaron conflictos en la sala seleccionada.',
          conflicts
        }
      }))
    );

    component.form.patchValue({
      activityId: 'act-1',
      roomId: 'room-1',
      instructorId: 'inst-1',
      validFrom: '2026-03-01'
    });

    const group0 = component.slotGroups.at(0);
    group0.patchValue({
      days: [1],
      startTime: '09:30',
      endTime: '10:30'
    });

    component.onSubmit();

    expect(component.isSubmitting()).toBe(false);
    expect(component.errorMessage()).toBe('Se detectaron conflictos en la sala seleccionada.');
    expect(component.conflicts().length).toBe(1);

    // Form inputs must still be preserved
    expect(component.form.get('activityId')?.value).toBe('act-1');
    expect(group0.get('startTime')?.value).toBe('09:30');

    // The preview slot should identify the conflict
    const preview = component.previewSlots();
    expect(preview.length).toBe(1);
    expect(preview[0].conflict).toBeDefined();
    expect(preview[0].conflict?.kind).toBe(ScheduleConflictKind.Room);
    expect(preview[0].conflict?.conflictingActivityName).toBe('Pilates');
  });

  it('should emit cancelled when cancel button is triggered', () => {
    const cancelSpy = vi.spyOn(component.cancelled, 'emit');
    component.onCancel();
    expect(cancelSpy).toHaveBeenCalled();
  });

  describe('secretary', () => {
    it('should load secretary options from people with Secretary and Administrator roles', () => {
      expect(peopleServiceSpy.search).toHaveBeenCalledWith(expect.objectContaining({ role: 'Secretary', status: PersonStatus.Active }));
      expect(peopleServiceSpy.search).toHaveBeenCalledWith(expect.objectContaining({ role: 'Administrator', status: PersonStatus.Active }));
    });

    it('should send the selected secretary in the batch request', () => {
      gymApiSpy.createBatchSchedules.mockReturnValue(of([]));

      component.form.patchValue({
        activityId: 'act-1',
        roomId: 'room-1',
        instructorId: 'inst-1',
        secretaryId: 'inst-1',
        validFrom: '2026-03-01',
        maxCapacity: 20
      });
      component.slotGroups.at(0).patchValue({ days: [1], startTime: '09:30', endTime: '10:30' });

      component.onSubmit();

      expect(gymApiSpy.createBatchSchedules).toHaveBeenCalledWith(expect.objectContaining({ secretaryId: 'inst-1' }));
    });
  });
});
