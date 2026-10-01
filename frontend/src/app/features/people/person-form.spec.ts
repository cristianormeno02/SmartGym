import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { PersonFormComponent, isMinor } from './person-form';
import { PeopleService } from '../../core/services/people.service';
import { DocumentType, Gender, PersonStatus } from '../../core/models/person.model';

describe('PersonFormComponent', () => {
  let component: PersonFormComponent;
  let fixture: ComponentFixture<PersonFormComponent>;
  let mockPeopleService: any;
  let router: Router;

  beforeEach(async () => {
    mockPeopleService = {
      getById: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      isBusy: () => false
    };

    await TestBed.configureTestingModule({
      imports: [PersonFormComponent],
      providers: [
        provideRouter([]),
        { provide: PeopleService, useValue: mockPeopleService },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => null
              }
            }
          }
        }
      ]
    }).compileComponents();

    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(PersonFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create the component with default create form', () => {
    expect(component).toBeTruthy();
    expect(component.isEditMode()).toBe(false);
    expect(component.form.valid).toBe(false); // firstName and lastName are required
  });

  it('should validate Argentine DNI correctly', () => {
    const docNumberCtrl = component.form.get('documentNumber');
    component.form.patchValue({
      documentType: DocumentType.Dni,
      documentIssuingCountry: 'AR'
    });

    // Invalid: only 4 digits
    docNumberCtrl?.setValue('1234');
    expect(docNumberCtrl?.valid).toBe(false);

    // Invalid: letters
    docNumberCtrl?.setValue('ABCDEF');
    expect(docNumberCtrl?.valid).toBe(false);

    // Valid: 8 digits
    docNumberCtrl?.setValue('35123456');
    expect(docNumberCtrl?.valid).toBe(true);

    // Valid: formatted with dots
    docNumberCtrl?.setValue('35.123.456');
    expect(docNumberCtrl?.valid).toBe(true);
  });

  it('should validate passport correctly', () => {
    const docNumberCtrl = component.form.get('documentNumber');
    component.form.patchValue({
      documentType: DocumentType.Passport
    });

    // Too short
    docNumberCtrl?.setValue('AB12');
    expect(docNumberCtrl?.valid).toBe(false);

    // Valid 8 chars
    docNumberCtrl?.setValue('A1234567');
    expect(docNumberCtrl?.valid).toBe(true);
  });

  it('should calculate isMinor correctly', () => {
    const today = new Date();
    const minorYear = today.getFullYear() - 15;
    const adultYear = today.getFullYear() - 25;

    expect(isMinor(`${minorYear}-05-10`)).toBe(true);
    expect(isMinor(`${adultYear}-05-10`)).toBe(false);
    expect(isMinor(null)).toBe(false);
  });

  it('should require emergency contact when birthDate indicates minor', () => {
    const today = new Date();
    const minorDate = `${today.getFullYear() - 10}-01-01`;

    const ecNameCtrl = component.form.get('emergencyContact.name');
    expect(ecNameCtrl?.hasError('required')).toBe(false);

    component.form.patchValue({ birthDate: minorDate });
    expect(component.isMinorSelected()).toBe(true);
    expect(ecNameCtrl?.hasError('required')).toBe(true);
  });

  it('should submit valid create request', () => {
    component.form.patchValue({
      firstName: 'María',
      lastName: 'Gómez',
      documentType: DocumentType.Dni,
      documentNumber: '30111222',
      documentIssuingCountry: 'AR',
      email: 'maria@example.com'
    });

    expect(component.form.valid).toBe(true);

    mockPeopleService.create.mockReturnValue(of({
      id: 'guid-123',
      firstName: 'María',
      lastName: 'Gómez'
    }));

    component.onSubmit();

    expect(mockPeopleService.create).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/portal/personas', 'guid-123']);
  });

  it('should handle 409 conflict error properly on submit', () => {
    component.form.patchValue({
      firstName: 'María',
      lastName: 'Gómez',
      documentType: DocumentType.Dni,
      documentNumber: '30111222',
      documentIssuingCountry: 'AR'
    });

    mockPeopleService.create.mockReturnValue(throwError(() => ({
      status: 409,
      error: { message: 'Ya existe una persona registrada con ese documento.' }
    })));

    component.onSubmit();

    expect(component.errorMessage()).toContain('Ya existe una persona');
  });
});
