import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { PersonFormComponent, isMinor, documentNumberError } from './person-form';
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

  describe('documentNumberError (mismas reglas que DocumentNormalizer del backend)', () => {
    it.each([
      ['12.345.678'],
      ['12-345-678'],
      ['01.234.567'],
      ['1234'],
      ['123 456 789']
    ])('acepta el DNI %s', (value) => {
      expect(documentNumberError(DocumentType.Dni, value, null)).toBeNull();
    });

    it.each([
      ['12345678A'],
      ['---'],
      ['000'],
      ['1234567890']
    ])('rechaza el DNI %s', (value) => {
      expect(documentNumberError(DocumentType.Dni, value, null)).not.toBeNull();
    });

    it('acepta el pasaporte con letras y separadores del escenario del spec', () => {
      expect(documentNumberError(DocumentType.Passport, ' a-123.456-x ', 'BR')).toBeNull();
    });

    it('rechaza un número que queda con menos de 3 caracteres tras normalizar', () => {
      expect(documentNumberError(DocumentType.IdentityCard, 'A-1', 'UY')).not.toBeNull();
    });

    it.each([[null], [''], ['BRA'], ['1A']])('exige país emisor de 2 letras para documentos no DNI (%s)', (country) => {
      expect(documentNumberError(DocumentType.Other, 'ABC123', country)).not.toBeNull();
    });
  });

  it('marca inválido el número de pasaporte si falta el país emisor', () => {
    const docNumberCtrl = component.form.get('documentNumber');
    component.form.patchValue({ documentType: DocumentType.Passport, documentIssuingCountry: '' });

    docNumberCtrl?.setValue('A1234567');
    expect(docNumberCtrl?.valid).toBe(false);

    component.form.patchValue({ documentIssuingCountry: 'BR' });
    docNumberCtrl?.updateValueAndValidity();
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
