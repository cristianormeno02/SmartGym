export enum DocumentType {
  Dni = 0,
  Passport = 1,
  ForeignId = 2,
  Other = 3
}

export const DocumentTypeLabels: Record<DocumentType, string> = {
  [DocumentType.Dni]: 'DNI',
  [DocumentType.Passport]: 'Pasaporte',
  [DocumentType.ForeignId]: 'Cédula de Identidad',
  [DocumentType.Other]: 'Otro'
};

export enum PersonStatus {
  Active = 0,
  Inactive = 1,
  Blocked = 2,
  Deceased = 3
}

export const PersonStatusLabels: Record<PersonStatus, string> = {
  [PersonStatus.Active]: 'Activa',
  [PersonStatus.Inactive]: 'Inactiva',
  [PersonStatus.Blocked]: 'Bloqueada',
  [PersonStatus.Deceased]: 'Fallecida'
};

export enum Gender {
  Male = 0,
  Female = 1,
  Other = 2
}

export const GenderLabels: Record<Gender, string> = {
  [Gender.Male]: 'Masculino',
  [Gender.Female]: 'Femenino',
  [Gender.Other]: 'Otro'
};

export interface AddressDto {
  street?: string;
  number?: string;
  floor?: string;
  apartment?: string;
  city?: string;
  state?: string;
  postalCode?: string;
  country?: string;
}

export interface EmergencyContactDto {
  name: string;
  phone: string;
  relationship: string;
}

export interface IdentificationDocumentDto {
  type: DocumentType;
  number: string;
  issuingCountry: string;
  numberNormalized: string;
}

export interface PersonSummary {
  id: string;
  firstName: string;
  lastName: string;
  document?: IdentificationDocumentDto;
  email?: string;
  primaryPhone?: string;
  photoUrl?: string;
  status: PersonStatus;
  hasUserAccount: boolean;
}

export interface PersonDetail {
  id: string;
  firstName: string;
  lastName: string;
  document?: IdentificationDocumentDto;
  birthDate?: string;
  gender?: Gender;
  email?: string;
  primaryPhone?: string;
  secondaryPhone?: string;
  address?: AddressDto;
  emergencyContact?: EmergencyContactDto;
  photoUrl?: string;
  status: PersonStatus;
  statusReason?: string;
  statusChangedAtUtc?: string;
  statusChangedByUserId?: string;
  createdAtUtc: string;
  version: number;
  hasUserAccount: boolean;
}

export interface CreatePersonRequest {
  firstName: string;
  lastName: string;
  documentType?: DocumentType;
  documentNumber?: string;
  documentIssuingCountry?: string;
  birthDate?: string;
  gender?: Gender;
  email?: string;
  primaryPhone?: string;
  secondaryPhone?: string;
  address?: AddressDto;
  emergencyContact?: EmergencyContactDto;
}

export interface UpdatePersonRequest {
  firstName: string;
  lastName: string;
  version: number;
  documentType?: DocumentType;
  documentNumber?: string;
  documentIssuingCountry?: string;
  birthDate?: string;
  gender?: Gender;
  email?: string;
  primaryPhone?: string;
  secondaryPhone?: string;
  address?: AddressDto;
  emergencyContact?: EmergencyContactDto;
}

export interface UpdateOwnContactRequest {
  primaryPhone?: string;
  secondaryPhone?: string;
  address?: AddressDto;
  emergencyContact?: EmergencyContactDto;
}

export interface ChangePersonStatusRequest {
  targetStatus: PersonStatus;
  reason?: string;
}

export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
