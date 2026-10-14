## MODIFIED Requirements

### Requirement: Búsqueda paginada de personas
El sistema SHALL permitir buscar personas con paginación (tamaño por defecto 20, máximo 100) y orden por apellido y nombre. Un único término de búsqueda SHALL coincidir:
- con nombre y apellido de forma parcial, sin distinguir mayúsculas ni acentos, exigiendo que cada palabra del término esté contenida;
- con el número de documento normalizado de forma exacta, cuando el término contenga dígitos y tenga al menos 5 caracteres alfanuméricos.

El sistema SHALL permitir además filtrar por estado, por tipo de documento y por rol. El filtro por rol SHALL retornar solo personas que tengan asignado ese rol con la asignación activa, y SHALL rechazar con `400` un nombre de rol inexistente.

#### Scenario: Búsqueda por documento con formato visual
- **WHEN** un operador busca con el término `12.345.678`
- **THEN** el sistema retorna la persona cuyo documento normalizado es `12345678`

#### Scenario: Búsqueda parcial sin acentos
- **WHEN** un operador busca con el término `gonza`
- **THEN** el sistema retorna una lista paginada que incluye personas con nombre o apellido González o Gonzalo

#### Scenario: Búsqueda por nombre completo
- **WHEN** un operador busca con el término `juan perez`
- **THEN** el sistema retorna las personas cuyo nombre y apellido contienen ambas palabras, por ejemplo "Juan Pérez"

#### Scenario: Filtro por estado
- **WHEN** un operador lista personas filtrando por el estado `BLOQUEADA`
- **THEN** el sistema retorna sólo personas en ese estado, con el total de resultados para paginar

#### Scenario: Filtro por rol
- **WHEN** un operador lista personas filtrando por el rol `Instructor` y el estado `ACTIVA`
- **THEN** el sistema retorna sólo personas activas con el rol `Instructor` asignado y activo, excluyendo a quienes tengan ese rol revocado

#### Scenario: Rol inexistente
- **WHEN** un operador lista personas filtrando por un rol que no existe
- **THEN** el sistema responde 400

#### Scenario: Persona inexistente
- **WHEN** se solicita la ficha de un identificador que no existe
- **THEN** el sistema responde 404
