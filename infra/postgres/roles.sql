-- Eine Login-Rolle pro Dienst, die nur ihre eigene Datenbank nutzen darf. Damit gilt „kein Dienst greift auf die
-- Datenbank eines anderen zu“ nicht mehr nur per Konvention, sondern wird von Postgres durchgesetzt.
--
-- Läuft per psql als Server-Admin: in Azure im Container-Apps-Job db-roles (infra/modules/dbroles.bicep, gestartet von
-- deploy.sh), im Test gegen einen Postgres-Container (tests/Portal.Tests/Infra/DatabaseRolesTests.cs).
-- Idempotent: legt fehlende Rollen an und setzt die Passwörter jedes Mal neu (Rotation = neues Passwort, neu deployen).
-- Die Tabellen legen die Dienste selbst an (EF-Migration bzw. CREATE TABLE IF NOT EXISTS) – sie gehören dann der Rolle.
\set ON_ERROR_STOP on

-- Passwörter aus der Umgebung. Fehlt eine Variable, bleibt :'…' unersetzt und das Skript bricht mit Syntaxfehler ab.
\getenv instrument_pw INSTRUMENT_DB_PASSWORD
\getenv search_pw SEARCH_DB_PASSWORD
\getenv history_pw HISTORY_DB_PASSWORD
\getenv keycloak_pw KEYCLOAK_DB_PASSWORD

SELECT format('CREATE ROLE %I LOGIN', r) FROM unnest(ARRAY['instrument', 'search', 'history', 'keycloak']) AS r
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = r) \gexec

ALTER ROLE instrument PASSWORD :'instrument_pw';
ALTER ROLE search PASSWORD :'search_pw';
ALTER ROLE history PASSWORD :'history_pw';
ALTER ROLE keycloak PASSWORD :'keycloak_pw';

-- Standardmäßig darf PUBLIC (= jede Rolle) auf jede Datenbank verbinden. Das nehmen wir weg und geben CONNECT gezielt.
REVOKE ALL ON DATABASE instrumentdb, searchdb, historydb, keycloak FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE instrumentdb TO instrument;
GRANT CONNECT, TEMPORARY ON DATABASE searchdb TO search;
GRANT CONNECT, TEMPORARY ON DATABASE historydb TO history;
GRANT CONNECT, TEMPORARY ON DATABASE keycloak TO keycloak;

-- Seit Postgres 15 darf PUBLIC im Schema public keine Tabellen mehr anlegen – die eigene Rolle braucht das Recht explizit.
\c instrumentdb
GRANT USAGE, CREATE ON SCHEMA public TO instrument;
\c searchdb
GRANT USAGE, CREATE ON SCHEMA public TO search;
\c historydb
GRANT USAGE, CREATE ON SCHEMA public TO history;
\c keycloak
GRANT USAGE, CREATE ON SCHEMA public TO keycloak;
