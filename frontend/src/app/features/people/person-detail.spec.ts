import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { PersonDetailComponent } from './person-detail';
import { PeopleService } from '../../core/services/people.service';
import { AuthService } from '../../core/services/auth.service';
import { PersonDetail, PersonStatus, DocumentType } from '../../core/models/person.model';

describe('PersonDetailComponent', () => {
  let component: PersonDetailComponent;
  let fixture: ComponentFixture<PersonDetailComponent>;
  let mockPeopleService: any;
  let mockAuthService: any;

  const mockPerson: PersonDetail = {
    id: 'test-person-id',
    firstName: 'Martín',
    lastName: 'López',
    document: { type: DocumentType.Dni, number: '28111222', issuingCountry: 'AR', numberNormalized: '28111222' },
    status: PersonStatus.Active,
    email: 'martin@example.com',
    primaryPhone: '11445566',
    createdAtUtc: '2026-01-01T00:00:00Z',
    version: 1,
    hasUserAccount: false
  };

  beforeEach(async () => {
    mockPeopleService = {
      getById: vi.fn().mockReturnValue(of(mockPerson)),
      changeStatus: vi.fn(),
      uploadPhoto: vi.fn(),
      deletePhoto: vi.fn(),
      isBusy: () => false
    };

    mockAuthService = {
      isAdmin: vi.fn().mockReturnValue(false)
    };

    await TestBed.configureTestingModule({
      imports: [PersonDetailComponent],
      providers: [
        provideRouter([]),
        { provide: PeopleService, useValue: mockPeopleService },
        { provide: AuthService, useValue: mockAuthService },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => 'test-person-id'
              }
            }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PersonDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load person details on init', () => {
    expect(component).toBeTruthy();
    expect(mockPeopleService.getById).toHaveBeenCalledWith('test-person-id');
    expect(component.person()).toEqual(mockPerson);
  });

  it('should open status modal and require reason', () => {
    component.openStatusModal();
    expect(component.showStatusModal()).toBe(true);

    component.statusReason = '';
    component.submitStatusChange();
    expect(component.modalError()).toContain('obligatorio');
    expect(mockPeopleService.changeStatus).not.toHaveBeenCalled();
  });

  it('should change status when reason is provided', () => {
    component.openStatusModal();
    component.newStatus = PersonStatus.Blocked;
    component.statusReason = 'Falta grave de conducta';

    const updated = { ...mockPerson, status: PersonStatus.Blocked, statusReason: 'Falta grave de conducta' };
    mockPeopleService.changeStatus.mockReturnValue(of(updated));

    component.submitStatusChange();

    expect(mockPeopleService.changeStatus).toHaveBeenCalledWith('test-person-id', {
      targetStatus: PersonStatus.Blocked,
      reason: 'Falta grave de conducta'
    });
    expect(component.showStatusModal()).toBe(false);
    expect(component.person()?.status).toBe(PersonStatus.Blocked);
  });

  it('should reject files exceeding 5MB on client side', () => {
    const largeFile = new File([new ArrayBuffer(6 * 1024 * 1024)], 'large.jpg', { type: 'image/jpeg' });
    const event = {
      target: {
        files: [largeFile]
      }
    } as any;

    component.onFileSelected(event);

    expect(component.feedback()).toContain('5 MB');
    expect(mockPeopleService.uploadPhoto).not.toHaveBeenCalled();
  });

  it('should upload valid image', () => {
    const validFile = new File(['abc'], 'avatar.png', { type: 'image/png' });
    const event = {
      target: {
        files: [validFile]
      }
    } as any;

    const updated = { ...mockPerson, photoUrl: 'https://r2.storage/photo.webp' };
    mockPeopleService.uploadPhoto.mockReturnValue(of(updated));

    component.onFileSelected(event);

    expect(mockPeopleService.uploadPhoto).toHaveBeenCalledWith('test-person-id', validFile);
    expect(component.person()?.photoUrl).toBe('https://r2.storage/photo.webp');
  });
});
