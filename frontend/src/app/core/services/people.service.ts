import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap, finalize } from 'rxjs';
import {
  PersonSummary,
  PersonDetail,
  CreatePersonRequest,
  UpdatePersonRequest,
  ChangePersonStatusRequest,
  UpdateOwnContactRequest,
  PagedResult,
  PersonStatus,
  DocumentType
} from '../models/person.model';

@Injectable({
  providedIn: 'root'
})
export class PeopleService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5000/api/people';

  readonly isBusy = signal<boolean>(false);
  readonly selectedPerson = signal<PersonDetail | null>(null);

  search(filters?: {
    search?: string;
    status?: PersonStatus;
    documentType?: DocumentType;
    pageNumber?: number;
    pageSize?: number;
  }): Observable<PagedResult<PersonSummary>> {
    let params = new HttpParams();
    if (filters?.search) params = params.set('search', filters.search);
    if (filters?.status !== undefined) params = params.set('status', filters.status.toString());
    if (filters?.documentType !== undefined) params = params.set('documentType', filters.documentType.toString());
    if (filters?.pageNumber !== undefined) params = params.set('pageNumber', filters.pageNumber.toString());
    if (filters?.pageSize !== undefined) params = params.set('pageSize', filters.pageSize.toString());

    this.isBusy.set(true);
    return this.http.get<PagedResult<PersonSummary>>(this.apiUrl, { params }).pipe(
      finalize(() => this.isBusy.set(false))
    );
  }

  getById(id: string): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.get<PersonDetail>(`${this.apiUrl}/${id}`).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  create(request: CreatePersonRequest): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.post<PersonDetail>(this.apiUrl, request).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  update(id: string, request: UpdatePersonRequest): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.put<PersonDetail>(`${this.apiUrl}/${id}`, request).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  changeStatus(id: string, request: ChangePersonStatusRequest): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.patch<PersonDetail>(`${this.apiUrl}/${id}/status`, request).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  uploadPhoto(id: string, file: File): Observable<PersonDetail> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    this.isBusy.set(true);
    return this.http.put<PersonDetail>(`${this.apiUrl}/${id}/photo`, formData).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  deletePhoto(id: string): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.delete<PersonDetail>(`${this.apiUrl}/${id}/photo`).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  getOwn(): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.get<PersonDetail>(`${this.apiUrl}/me`).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  updateOwnContact(request: UpdateOwnContactRequest): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.put<PersonDetail>(`${this.apiUrl}/me/contact`, request).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  uploadOwnPhoto(file: File): Observable<PersonDetail> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    this.isBusy.set(true);
    return this.http.put<PersonDetail>(`${this.apiUrl}/me/photo`, formData).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }

  deleteOwnPhoto(): Observable<PersonDetail> {
    this.isBusy.set(true);
    return this.http.delete<PersonDetail>(`${this.apiUrl}/me/photo`).pipe(
      tap((person) => this.selectedPerson.set(person)),
      finalize(() => this.isBusy.set(false))
    );
  }
}
