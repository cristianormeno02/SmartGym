import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { PeopleListComponent } from './people-list';
import { PeopleService } from '../../core/services/people.service';
import { PersonStatus, DocumentType, PagedResult, PersonSummary } from '../../core/models/person.model';

describe('PeopleListComponent', () => {
  let component: PeopleListComponent;
  let fixture: ComponentFixture<PeopleListComponent>;
  let searchSpy: any;

  const mockPagedResult: PagedResult<PersonSummary> = {
    items: [
      {
        id: '1',
        firstName: 'Juan',
        lastName: 'Pérez',
        document: { type: DocumentType.Dni, number: '12345678', issuingCountry: 'AR', numberNormalized: '12345678' },
        status: PersonStatus.Active,
        hasUserAccount: true
      },
      {
        id: '2',
        firstName: 'Carlos',
        lastName: 'García',
        document: { type: DocumentType.Dni, number: '87654321', issuingCountry: 'AR', numberNormalized: '87654321' },
        status: PersonStatus.Blocked,
        hasUserAccount: false
      }
    ],
    pageNumber: 1,
    pageSize: 20,
    totalCount: 2,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false
  };

  beforeEach(async () => {
    searchSpy = vi.fn().mockReturnValue(of(mockPagedResult));

    const mockPeopleService = {
      isBusy: () => false,
      search: searchSpy
    };

    await TestBed.configureTestingModule({
      imports: [PeopleListComponent],
      providers: [
        provideRouter([]),
        { provide: PeopleService, useValue: mockPeopleService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PeopleListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('should create the component and load people on init', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
    expect(searchSpy).toHaveBeenCalledWith(expect.objectContaining({
      pageNumber: 1,
      pageSize: 20
    }));
    expect(component.people().length).toBe(2);
  });

  it('should display BLOQUEADA badge for blocked persons', () => {
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('BLOQUEADA');
  });

  it('should debounce search input', () => {
    vi.useFakeTimers();
    fixture.detectChanges();
    searchSpy.mockClear();

    component.onSearchInput('gonzalez');
    expect(searchSpy).not.toHaveBeenCalled();

    vi.advanceTimersByTime(300);
    expect(searchSpy).toHaveBeenCalledWith(expect.objectContaining({
      search: 'gonzalez',
      pageNumber: 1
    }));
  });

  it('should reload on status filter change', () => {
    fixture.detectChanges();
    searchSpy.mockClear();

    component.onStatusChange(PersonStatus.Blocked);
    expect(searchSpy).toHaveBeenCalledWith(expect.objectContaining({
      status: PersonStatus.Blocked,
      pageNumber: 1
    }));
  });
});
