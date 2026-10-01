## 1. Domain Model (TDD: tests first for every item)

- [x] 1.1 Implement `TextNormalizer` (NFD, strip diacritics, lowercase, collapse whitespace) with unit tests covering the full Spanish alphabet (á é í ó ú ü ñ, upper and lower case) and repeated spaces.
- [x] 1.2 Implement `DocumentNormalizer` and the `IdentificationDocument` value object with unit tests for:
  - DNI: separators, leading zeros, letters rejected, empty/zero rejected, length 1–9, issuing country forced to `AR`;
  - PASAPORTE/CI/OTRO: uppercase alphanumeric, length 3–30, mandatory issuing country;
  - all-or-nothing type/number.
- [x] 1.3 Add the `DocumentType`, `Gender` and `PersonStatus` enums, with `[JsonStringEnumMemberName]` Spanish API codes and a serialization round-trip test.
- [x] 1.4 Implement the `Address`, `ProfileImage` and `EmergencyContact` value objects.
- [x] 1.5 Refactor `Person` (`SmartGym.Domain/Entities/Identity/Person.cs`):
  - private setters plus domain methods (`Create`, `UpdatePersonalData`, `UpdateContact`, `SetDocument`, `ChangeStatus`, `SetProfileImage`, `ClearProfileImage`);
  - non-empty name invariants, email normalization, `SearchName` recalculation;
  - `IsActive` synchronized with `Status`;
  - minors require a complete emergency contact.

  Rename `PhoneNumber` → `PrimaryPhone` and `PhotoUrl` → `ExternalAvatarUrl`. Update `PersonTests`.
- [x] 1.6 Implement the status transition rules in `Person.ChangeStatus` with unit tests for the full transition matrix:
  - allowed and rejected transitions;
  - same-state rejection;
  - mandatory reason;
  - audit fields;
  - administrator-only transitions (`FALLECIDA` and its reversal);
  - read-only behavior while `FALLECIDA`.

## 2. Persistence and Migration

- [x] 2.1 Update `PersonConfiguration`:
  - explicit PascalCase owned columns and string enum conversions;
  - `xmin` concurrency token (`Version`);
  - filtered unique indexes `IX_People_Document_Unique` and `IX_People_Email_Unique`;
  - GIN trigram index on `SearchName`, B-tree on `(LastName, FirstName)`;
  - document consistency check constraint.

  Extend `PersonModelConfigurationTests`.
- [x] 2.2 Write the migration `EvolvePersonToPeopleModule`:
  - pre-check that aborts on invalid or duplicate DNI and duplicate emails;
  - `pg_trgm` and `unaccent` extensions;
  - backfill of status, document, email, `SearchName` and missing last names;
  - column renames, index replacement, drop of `Dni`;
  - `Down` restoring the previous schema.
- [x] 2.3 Verify the migration against a PostgreSQL database seeded with representative legacy rows:
  - formatted DNI, null DNI, mixed-case email, empty last name, inactive person, Google photo URL;
  - the pre-check aborting on a seeded collision;
  - the .NET `TextNormalizer` and the SQL `unaccent` backfill producing identical `SearchName` for Spanish names.

## 3. File Storage

- [x] 3.1 Add `GetAccessUrlAsync(key, expiresIn)` to `IFileStorageService` and implement it:
  - `S3StorageService`: presigned GET URL;
  - `LocalStorageService`: returns its `/storage/...` path.

  Add unit tests for both.
- [x] 3.2 Implement `ProfileImageProcessor` (SkiaSharp):
  - validation: ≤ 5 MB, MIME whitelist, magic-byte check, decodability;
  - processing: re-encode to WebP, resize to ≤ 1024×1024, strip metadata.

  Test with fixture files: valid JPEG/PNG/WebP, PDF renamed as `.jpg`, oversized file, and a JPEG with EXIF GPS data that must come out stripped.

## 4. Application Layer (`SmartGym.Application/Modules/People`)

- [x] 4.1 Create the DTOs:
  - `CreatePersonRequest`, `UpdatePersonRequest` (with `version`), `UpdateOwnContactRequest`, `ChangePersonStatusRequest`;
  - `PersonDetailDto`, `PersonSummaryDto`, `PagedResult<T>`.

  Add FluentValidation validators with unit tests.
- [x] 4.2 Implement `IPeopleService` create/update/get, with tests covering:
  - duplicate document and email detection (409), including updating a person with their own document;
  - mapping of unique-violation `23505` to 409;
  - email change rejected when a `User` is linked;
  - concurrency conflict (409);
  - 404 for unknown ids.
- [x] 4.3 Implement the status change use case, with tests for:
  - the audit user taken from `ICurrentUserService`;
  - the administrator-only checks;
  - preservation of memberships and reservations.
- [x] 4.4 Implement the search query:
  - multi-word AND over `SearchName`;
  - document detection with DNI and generic normalization;
  - status and document type filters;
  - paging (default 20, max 100) and stable ordering.

  Use repository tests against PostgreSQL, since trigram and `ILIKE` behavior is not reproducible in-memory.
- [x] 4.5 Implement the photo upload, replace and delete workflow:
  - compensation delete when the DB save fails;
  - best-effort delete of the previous object, with an orphan log;
  - rejection while `FALLECIDA`;
  - presigned URL resolution with fallback to `ExternalAvatarUrl`.

  Cover it with storage interaction unit tests.
- [x] 4.6 Implement the self-service use cases (`GetOwn`, `UpdateOwnContact`, own photo) with tests that protected fields cannot be modified.

## 5. Integration with Existing Modules

- [x] 5.1 Adapt `AuthService`, with tests:
  - local registration uses `DocumentNormalizer` and normalized duplicate checks;
  - Google sign-up derives the last name or uses the `"Sin apellido"` placeholder;
  - the Google photo is written to `ExternalAvatarUrl`;
  - login (local and Google) is denied for `FALLECIDA`.
- [x] 5.2 Replace `Dni`/`PhotoUrl` usages in:
  - `AuthDtos` and `UsersController`;
  - `MembershipService`/`MembershipDtos`, `ReservationService`/`ReservationDtos`, `FamilyGroupService`/`FamilyGroupDtos`.

  Expose `documentType` + `documentNumber` and the resolved photo URL. Update the affected tests.
- [x] 5.3 Audit the reservation creation and check-in entry points: they must require `Person.IsActive`, as Memberships and Activities already do. Add a test proving that a `BLOQUEADA` student cannot reserve.

## 6. Web API

- [x] 6.1 Create `PeopleController` (`api/people`) with:
  - staff endpoints (`RequireStaff`) and the administrator check for `FALLECIDA` transitions;
  - self-service `me` endpoints;
  - multipart photo endpoints with a request size limit;
  - error mapping (400/403/404/409).

  Write integration tests covering authorization per role (Alumno/Instructor get 403) and each error code.
- [x] 6.2 Add OpenAPI annotations and response types for all People endpoints, and verify the generated document.

## 7. Frontend (Angular 22)

- [x] 7.1 Update the existing models and views using `dni`/`photoUrl`:
  - `auth.models.ts`, `gym.models.ts`;
  - login and the student, staff and admin portals.

  Update their tests.
- [x] 7.2 Create the People TypeScript interfaces and `PeopleService` in `frontend/src/app/core` (HttpClient + Signals), tested with `HttpTestingController`.
- [x] 7.3 Build `PeopleListComponent` in `frontend/src/app/features/people`:
  - debounced single search box (name or document);
  - status filter;
  - paginated table;
  - visual alert for `BLOQUEADA`.

  Add component tests.
- [x] 7.4 Build `PersonDetailComponent` and `PersonFormComponent`:
  - Reactive Forms with a per-type document validator mirroring the backend rules;
  - conditional emergency contact for minors;
  - 409 handling (duplicate, stale version);
  - status change dialog with mandatory reason, hiding `FALLECIDA` for non-admins.

  Add component tests.
- [x] 7.5 Build the avatar upload component (client-side type/size pre-check and preview) and the self-service "Mis datos" view. Add component tests.

## 8. End-to-End Verification

- [x] 8.1 Run an end-to-end integration test with `LocalStorageService` covering:
  - registration without email or user;
  - duplicate document rejection (formatted vs plain DNI);
  - subsequent Google account linkage by email;
  - status transitions with audit, including a `BLOQUEADA` person being unable to reserve;
  - photo upload, replace and delete.
- [x] 8.2 Run `openspec validate people-management` and the full backend and frontend test suites, all green.
