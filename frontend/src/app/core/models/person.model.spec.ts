import { describe, it, expect } from 'vitest';
import { DocumentType, Gender, PersonStatus } from './person.model';

// Los valores deben coincidir con los códigos que serializa la API (JsonStringEnumMemberName en el backend).
describe('People API enum contract', () => {
  it('PersonStatus usa los códigos de la API', () => {
    expect(Object.values(PersonStatus)).toEqual(['ACTIVA', 'INACTIVA', 'BLOQUEADA', 'FALLECIDA']);
  });

  it('DocumentType usa los códigos de la API', () => {
    expect(Object.values(DocumentType)).toEqual(['DNI', 'PASAPORTE', 'CI', 'OTRO']);
  });

  it('Gender usa los códigos de la API', () => {
    expect(Object.values(Gender)).toEqual(['MASCULINO', 'FEMENINO', 'X', 'NO_INFORMA']);
  });
});
