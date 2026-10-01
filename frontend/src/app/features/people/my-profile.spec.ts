import { TestBed, ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { MyProfileComponent } from './my-profile';
import { PeopleService } from '../../core/services/people.service';
import { AuthService } from '../../core/services/auth.service';
import { PersonDetail, PersonStatus, DocumentType } from '../../core/models/person.model';

describe('MyProfileComponent', () => {
  let component: MyProfileComponent;
  let fixture: ComponentFixture<MyProfileComponent>;
  let mockPeopleService: any;
  let mockAuthService: any;

  const mockPerson: PersonDetail = {
    id: 'my-person-id',
    firstName: 'Laura',
    lastName: 'Fernández',
    document: { type: DocumentType.Dni, number: '33444555', issuingCountry: 'AR', numberNormalized: '33444555' },
    status: PersonStatus.Active,
    email: 'laura@example.com',
    primaryPhone: '11889900',
    createdAtUtc: '2026-01-01T00:00:00Z',
    version: 1,
    hasUserAccount: true
  };

  beforeEach(async () => {
    mockPeopleService = {
      getOwn: vi.fn().mockReturnValue(of(mockPerson)),
      updateOwnContact: vi.fn(),
      uploadOwnPhoto: vi.fn(),
      deleteOwnPhoto: vi.fn(),
      isBusy: () => false
    };

    mockAuthService = {
      currentUser: vi.fn().mockReturnValue({ fullName: 'Laura Fernández' })
    };

    await TestBed.configureTestingModule({
      imports: [MyProfileComponent],
      providers: [
        { provide: PeopleService, useValue: mockPeopleService },
        { provide: AuthService, useValue: mockAuthService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(MyProfileComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load own data on init', () => {
    expect(component).toBeTruthy();
    expect(mockPeopleService.getOwn).toHaveBeenCalled();
    expect(component.person()).toEqual(mockPerson);
    expect(component.form.get('primaryPhone')?.value).toBe('11889900');
  });

  it('should save updated contact information', () => {
    component.form.patchValue({
      primaryPhone: '11998877',
      secondaryPhone: '11223344'
    });

    const updated = { ...mockPerson, primaryPhone: '11998877', secondaryPhone: '11223344' };
    mockPeopleService.updateOwnContact.mockReturnValue(of(updated));

    component.onSaveContact();

    expect(mockPeopleService.updateOwnContact).toHaveBeenCalledWith(expect.objectContaining({
      primaryPhone: '11998877',
      secondaryPhone: '11223344'
    }));
    expect(component.feedbackSuccess()).toBe(true);
  });

  it('should reject non-image file on client side', () => {
    const pdfFile = new File(['pdf-content'], 'doc.pdf', { type: 'application/pdf' });
    const event = {
      target: {
        files: [pdfFile]
      }
    } as any;

    component.onFileSelected(event);

    expect(component.feedback()).toContain('Formato de imagen no permitido');
    expect(component.selectedFile()).toBeNull();
  });

  it('should reject file > 5MB on client side', () => {
    const bigFile = new File([new ArrayBuffer(6 * 1024 * 1024)], 'big.jpg', { type: 'image/jpeg' });
    const event = {
      target: {
        files: [bigFile]
      }
    } as any;

    component.onFileSelected(event);

    expect(component.feedback()).toContain('5 MB');
    expect(component.selectedFile()).toBeNull();
  });

  it('should upload own photo on confirmation', () => {
    const validFile = new File(['content'], 'avatar.webp', { type: 'image/webp' });
    component.selectedFile.set(validFile);

    const updated = { ...mockPerson, photoUrl: 'https://r2.storage/my-avatar.webp' };
    mockPeopleService.uploadOwnPhoto.mockReturnValue(of(updated));

    component.confirmUploadPhoto();

    expect(mockPeopleService.uploadOwnPhoto).toHaveBeenCalledWith(validFile);
    expect(component.person()?.photoUrl).toBe('https://r2.storage/my-avatar.webp');
  });
});
