import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RoomsManagementComponent } from './rooms-management';
import { GymApiService } from '../../../core/services/gym-api.service';
import { of } from 'rxjs';
import { Room } from '../../../core/models/gym.models';
import { vi, describe, it, expect, beforeEach } from 'vitest';

describe('RoomsManagementComponent', () => {
  let component: RoomsManagementComponent;
  let fixture: ComponentFixture<RoomsManagementComponent>;
  let gymApiSpy: {
    getRooms: ReturnType<typeof vi.fn>;
    updateRoom: ReturnType<typeof vi.fn>;
  };

  const mockRooms: Room[] = [
    { id: 'r1', name: 'Sala 1', description: 'Planta Baja', capacity: 20, isActive: true },
    { id: 'r2', name: 'Sala 2', description: 'Primer Piso', capacity: 15, isActive: false }
  ];

  beforeEach(async () => {
    gymApiSpy = {
      getRooms: vi.fn().mockReturnValue(of(mockRooms)),
      updateRoom: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [RoomsManagementComponent],
      providers: [
        { provide: GymApiService, useValue: gymApiSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(RoomsManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load and display both active and inactive rooms', () => {
    expect(component).toBeTruthy();
    expect(component.rooms().length).toBe(2);
    expect(gymApiSpy.getRooms).toHaveBeenCalled();
  });

  it('should open modal for new room', () => {
    component.openCreateModal();
    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedRoom()).toBeNull();
  });

  it('should open modal for editing room', () => {
    component.openEditModal(mockRooms[0]);
    expect(component.isModalOpen()).toBe(true);
    expect(component.selectedRoom()).toEqual(mockRooms[0]);
  });

  it('should refresh rooms on room saved', () => {
    gymApiSpy.getRooms.mockClear();
    gymApiSpy.getRooms.mockReturnValue(of(mockRooms));

    component.onRoomSaved();
    expect(component.isModalOpen()).toBe(false);
    expect(gymApiSpy.getRooms).toHaveBeenCalled();
  });

  it('toggleRoomStatus should call updateRoom with inverted status and refresh', () => {
    const updated: Room = { ...mockRooms[0], isActive: false };
    gymApiSpy.updateRoom.mockReturnValue(of(updated));
    gymApiSpy.getRooms.mockClear();
    gymApiSpy.getRooms.mockReturnValue(of(mockRooms));

    component.toggleRoomStatus(mockRooms[0]);

    expect(gymApiSpy.updateRoom).toHaveBeenCalledWith('r1', {
      name: 'Sala 1',
      description: 'Planta Baja',
      capacity: 20,
      isActive: false
    });
    expect(gymApiSpy.getRooms).toHaveBeenCalled();
  });
});
