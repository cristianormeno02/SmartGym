import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RoomFormModalComponent } from './room-form-modal';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of, throwError } from 'rxjs';
import { Room } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('RoomFormModalComponent', () => {
  let component: RoomFormModalComponent;
  let fixture: ComponentFixture<RoomFormModalComponent>;
  let gymApiSpy: {
    createRoom: ReturnType<typeof vi.fn>;
    updateRoom: ReturnType<typeof vi.fn>;
  };

  const mockRoom: Room = {
    id: 'room-1',
    name: 'Sala Musculación',
    description: 'Piso 2',
    capacity: 20,
    isActive: true
  };

  beforeEach(async () => {
    gymApiSpy = {
      createRoom: vi.fn(),
      updateRoom: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [RoomFormModalComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(RoomFormModalComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and have invalid form by default in create mode', () => {
    expect(component).toBeTruthy();
    expect(component.form.valid).toBe(false);
    expect(component.form.get('name')?.hasError('required')).toBe(true);
  });

  it('should validate capacity >= 1', () => {
    const capacityCtrl = component.form.get('capacity');
    capacityCtrl?.setValue(0);
    expect(capacityCtrl?.hasError('min')).toBe(true);

    capacityCtrl?.setValue(-5);
    expect(capacityCtrl?.hasError('min')).toBe(true);

    capacityCtrl?.setValue(10);
    expect(capacityCtrl?.valid).toBe(true);
  });

  it('should submit create request on alta and emit saved', () => {
    gymApiSpy.createRoom.mockReturnValue(of(mockRoom));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.form.setValue({
      name: 'Sala Musculación',
      description: 'Piso 2',
      capacity: 20,
      isActive: true
    });

    component.onSubmit();

    expect(gymApiSpy.createRoom).toHaveBeenCalledWith({
      name: 'Sala Musculación',
      description: 'Piso 2',
      capacity: 20
    });
    expect(emitSpy).toHaveBeenCalledWith(mockRoom);
  });

  it('should populate form in edit mode and submit updateRoom', () => {
    const updatedRoom: Room = { ...mockRoom, capacity: 25, isActive: false };
    gymApiSpy.updateRoom.mockReturnValue(of(updatedRoom));
    const emitSpy = vi.spyOn(component.saved, 'emit');

    component.room = mockRoom;
    component.ngOnChanges();

    expect(component.isEditMode).toBe(true);
    expect(component.form.value.name).toBe('Sala Musculación');
    expect(component.form.value.capacity).toBe(20);

    component.form.patchValue({ capacity: 25, isActive: false });
    component.onSubmit();

    expect(gymApiSpy.updateRoom).toHaveBeenCalledWith('room-1', {
      name: 'Sala Musculación',
      description: 'Piso 2',
      capacity: 25,
      isActive: false
    });
    expect(emitSpy).toHaveBeenCalledWith(updatedRoom);
  });

  it('should display server error message on failure', () => {
    gymApiSpy.createRoom.mockReturnValue(throwError(() => ({ error: { message: 'El nombre ya existe.' } })));

    component.form.setValue({
      name: 'Sala Existente',
      description: '',
      capacity: 10,
      isActive: true
    });

    component.onSubmit();

    expect(component.errorMessage()).toBe('El nombre ya existe.');
  });
});
