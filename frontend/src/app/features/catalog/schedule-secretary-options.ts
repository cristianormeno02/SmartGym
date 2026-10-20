import { Observable, forkJoin, map } from 'rxjs';
import { PeopleService } from '../../core/services/people.service';
import { PersonStatus, PersonSummary } from '../../core/models/person.model';

export interface PersonOption {
  id: string;
  label: string;
}

// El backend acepta como secretario de un horario a personas con rol Secretary o Administrator.
export function loadSecretaryCandidates(peopleService: PeopleService): Observable<PersonSummary[]> {
  return forkJoin([
    peopleService.search({ role: 'Secretary', status: PersonStatus.Active, pageSize: 100 }),
    peopleService.search({ role: 'Administrator', status: PersonStatus.Active, pageSize: 100 })
  ]).pipe(
    map(([secretaries, admins]) => {
      const byId = new Map<string, PersonSummary>();
      for (const p of [...(secretaries.items || []), ...(admins.items || [])]) {
        byId.set(p.id, p);
      }
      return [...byId.values()];
    })
  );
}

export function toPersonOptions(people: PersonSummary[]): PersonOption[] {
  return people.map(p => ({ id: p.id, label: `${p.lastName}, ${p.firstName}` }));
}
