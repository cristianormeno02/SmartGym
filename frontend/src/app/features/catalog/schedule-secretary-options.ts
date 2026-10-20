import { Observable, map } from 'rxjs';
import { PeopleService } from '../../core/services/people.service';
import { PersonStatus, PersonSummary } from '../../core/models/person.model';

export interface PersonOption {
  id: string;
  label: string;
}

// El selector de secretarios solo lista personas activas con usuario asignado y rol Secretary.
export function loadSecretaryCandidates(peopleService: PeopleService): Observable<PersonSummary[]> {
  return peopleService
    .search({ role: 'Secretary', status: PersonStatus.Active, pageSize: 100 })
    .pipe(map(res => (res.items || []).filter(p => p.hasUserAccount === true)));
}

export function toPersonOptions(people: PersonSummary[]): PersonOption[] {
  return people.map(p => ({ id: p.id, label: `${p.lastName}, ${p.firstName}` }));
}
