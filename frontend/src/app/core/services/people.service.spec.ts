import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PeopleService } from './people.service';
import { DocumentType, PersonStatus, Gender, PersonDetail, PagedResult, PersonSummary } from '../models/person.model';

describe('PeopleService', () => {
  let service: PeopleService;
  let httpMock: HttpTestingController;
  const apiUrl = 'http://localhost:5000/api/people';

  const mockPersonDetail: PersonDetail = {
    id: '11111111-1111-1111-1111-111111111111',
    firstName: 'Juan',
    lastName: 'Pérez',
    document: {
      type: DocumentType.Dni,
      number: '12345678',
      issuingCountry: 'AR',
      numberNormalized: '12345678'
    },
    birthDate: '1990-01-01',
    gender: Gender.Male,
    email: 'juan@example.com',
    primaryPhone: '11223344',
    status: PersonStatus.Active,
    createdAtUtc: '2026-01-01T00:00:00Z',
    version: 1,
    hasUserAccount: false
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PeopleService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(PeopleService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('search should request GET /api/people with query parameters', () => {
    const mockPagedResult: PagedResult<PersonSummary> = {
      items: [{
        id: mockPersonDetail.id,
        firstName: mockPersonDetail.firstName,
        lastName: mockPersonDetail.lastName,
        document: mockPersonDetail.document,
        email: mockPersonDetail.email,
        primaryPhone: mockPersonDetail.primaryPhone,
        status: mockPersonDetail.status,
        hasUserAccount: false
      }],
      pageNumber: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false
    };

    service.search({ search: 'juan', status: PersonStatus.Active, documentType: DocumentType.Dni, pageNumber: 1, pageSize: 20 }).subscribe(res => {
      expect(res).toEqual(mockPagedResult);
    });

    const req = httpMock.expectOne(r =>
      r.url === apiUrl &&
      r.params.get('search') === 'juan' &&
      r.params.get('status') === '0' &&
      r.params.get('documentType') === '0' &&
      r.params.get('pageNumber') === '1' &&
      r.params.get('pageSize') === '20'
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockPagedResult);
  });

  it('getById should request GET /api/people/:id and update selectedPerson signal', () => {
    service.getById(mockPersonDetail.id).subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
      expect(service.selectedPerson()).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(`${apiUrl}/${mockPersonDetail.id}`);
    expect(req.request.method).toBe('GET');
    req.flush(mockPersonDetail);
  });

  it('create should request POST /api/people and update selectedPerson signal', () => {
    const request = {
      firstName: 'Juan',
      lastName: 'Pérez',
      documentType: DocumentType.Dni,
      documentNumber: '12345678',
      documentIssuingCountry: 'AR'
    };

    service.create(request).subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
      expect(service.selectedPerson()).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(apiUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(mockPersonDetail);
  });

  it('update should request PUT /api/people/:id', () => {
    const request = {
      firstName: 'Juan',
      lastName: 'Pérez',
      version: 1
    };

    service.update(mockPersonDetail.id, request).subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(`${apiUrl}/${mockPersonDetail.id}`);
    expect(req.request.method).toBe('PUT');
    req.flush(mockPersonDetail);
  });

  it('changeStatus should request PATCH /api/people/:id/status', () => {
    const request = {
      targetStatus: PersonStatus.Blocked,
      reason: 'Sanción disciplinaria'
    };

    service.changeStatus(mockPersonDetail.id, request).subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(`${apiUrl}/${mockPersonDetail.id}/status`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual(request);
    req.flush(mockPersonDetail);
  });

  it('uploadPhoto should request PUT /api/people/:id/photo with FormData', () => {
    const file = new File(['dummy content'], 'avatar.jpg', { type: 'image/jpeg' });

    service.uploadPhoto(mockPersonDetail.id, file).subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(`${apiUrl}/${mockPersonDetail.id}/photo`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush(mockPersonDetail);
  });

  it('deletePhoto should request DELETE /api/people/:id/photo', () => {
    service.deletePhoto(mockPersonDetail.id).subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(`${apiUrl}/${mockPersonDetail.id}/photo`);
    expect(req.request.method).toBe('DELETE');
    req.flush(mockPersonDetail);
  });

  it('getOwn should request GET /api/people/me', () => {
    service.getOwn().subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(`${apiUrl}/me`);
    expect(req.request.method).toBe('GET');
    req.flush(mockPersonDetail);
  });

  it('updateOwnContact should request PUT /api/people/me/contact', () => {
    const request = { primaryPhone: '99887766' };

    service.updateOwnContact(request).subscribe(res => {
      expect(res).toEqual(mockPersonDetail);
    });

    const req = httpMock.expectOne(`${apiUrl}/me/contact`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush(mockPersonDetail);
  });
});
