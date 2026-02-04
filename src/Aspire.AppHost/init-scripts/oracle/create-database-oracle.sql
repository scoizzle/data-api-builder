-- create-database-oracle.sql
-- Oracle version of create-database.sql

-- Note: Oracle doesn't use explicit database creation in the same way as MSSQL/PostgreSQL
-- The database is created when you create a user/schema
-- This script assumes you're connected to an Oracle instance with appropriate privileges

-- Drop tables in reverse order of creation due to foreign key dependencies
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE "Character_Species" CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE "Series_Character" CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE "Character" CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE "Species" CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE "Actor" CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE "Series" CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

-- Create tables
CREATE TABLE "Series" (
    "Id" NUMBER PRIMARY KEY,
    "Name" VARCHAR2(255) NOT NULL
);

CREATE TABLE "Actor" (
    "Id" NUMBER PRIMARY KEY,
    "Name" VARCHAR2(255) NOT NULL,
    "BirthYear" NUMBER NOT NULL
);

CREATE TABLE "Species" (
    "Id" NUMBER PRIMARY KEY,
    "Name" VARCHAR2(255) NOT NULL
);

CREATE TABLE "Character" (
    "Id" NUMBER PRIMARY KEY,
    "Name" VARCHAR2(255) NOT NULL,
    "ActorId" NUMBER NOT NULL,
    "Stardate" NUMBER(10, 2),
    CONSTRAINT fk_character_actor FOREIGN KEY ("ActorId") REFERENCES "Actor"("Id")
);

CREATE TABLE "Series_Character" (
    "SeriesId" NUMBER,
    "CharacterId" NUMBER,
    "Role" VARCHAR2(500),
    CONSTRAINT fk_series_char_series FOREIGN KEY ("SeriesId") REFERENCES "Series"("Id"),
    CONSTRAINT fk_series_char_char FOREIGN KEY ("CharacterId") REFERENCES "Character"("Id"),
    CONSTRAINT pk_series_char PRIMARY KEY ("SeriesId", "CharacterId")
);

CREATE TABLE "Character_Species" (
    "CharacterId" NUMBER,
    "SpeciesId" NUMBER,
    CONSTRAINT fk_char_species_char FOREIGN KEY ("CharacterId") REFERENCES "Character"("Id"),
    CONSTRAINT fk_char_species_species FOREIGN KEY ("SpeciesId") REFERENCES "Species"("Id"),
    CONSTRAINT pk_char_species PRIMARY KEY ("CharacterId", "SpeciesId")
);

-- Insert data
INSERT INTO "Series" ("Id", "Name") VALUES (1, 'Star Trek');
INSERT INTO "Series" ("Id", "Name") VALUES (2, 'Star Trek: The Next Generation');
INSERT INTO "Series" ("Id", "Name") VALUES (3, 'Star Trek: Voyager');
INSERT INTO "Series" ("Id", "Name") VALUES (4, 'Star Trek: Deep Space Nine');
INSERT INTO "Series" ("Id", "Name") VALUES (5, 'Star Trek: Enterprise');

INSERT INTO "Species" ("Id", "Name") VALUES (1, 'Human');
INSERT INTO "Species" ("Id", "Name") VALUES (2, 'Vulcan');
INSERT INTO "Species" ("Id", "Name") VALUES (3, 'Android');
INSERT INTO "Species" ("Id", "Name") VALUES (4, 'Klingon');
INSERT INTO "Species" ("Id", "Name") VALUES (5, 'Betazoid');
INSERT INTO "Species" ("Id", "Name") VALUES (6, 'Hologram');
INSERT INTO "Species" ("Id", "Name") VALUES (7, 'Bajoran');
INSERT INTO "Species" ("Id", "Name") VALUES (8, 'Changeling');
INSERT INTO "Species" ("Id", "Name") VALUES (9, 'Trill');
INSERT INTO "Species" ("Id", "Name") VALUES (10, 'Ferengi');
INSERT INTO "Species" ("Id", "Name") VALUES (11, 'Denobulan');
INSERT INTO "Species" ("Id", "Name") VALUES (12, 'Borg');

INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (1, 'William Shatner', 1931);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (2, 'Leonard Nimoy', 1931);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (3, 'DeForest Kelley', 1920);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (4, 'James Doohan', 1920);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (5, 'Nichelle Nichols', 1932);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (6, 'George Takei', 1937);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (7, 'Walter Koenig', 1936);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (8, 'Patrick Stewart', 1940);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (9, 'Jonathan Frakes', 1952);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (10, 'Brent Spiner', 1949);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (11, 'Michael Dorn', 1952);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (12, 'Gates McFadden', 1949);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (13, 'Marina Sirtis', 1955);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (14, 'LeVar Burton', 1957);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (15, 'Kate Mulgrew', 1955);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (16, 'Robert Beltran', 1953);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (17, 'Tim Russ', 1956);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (18, 'Roxann Dawson', 1958);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (19, 'Robert Duncan McNeill', 1964);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (20, 'Garrett Wang', 1968);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (21, 'Robert Picardo', 1953);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (22, 'Jeri Ryan', 1968);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (23, 'Avery Brooks', 1948);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (24, 'Nana Visitor', 1957);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (25, 'Rene Auberjonois', 1940);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (26, 'Terry Farrell', 1963);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (27, 'Alexander Siddig', 1965);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (28, 'Armin Shimerman', 1949);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (29, 'Cirroc Lofton', 1978);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (30, 'Scott Bakula', 1954);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (31, 'Jolene Blalock', 1975);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (32, 'John Billingsley', 1960);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (33, 'Connor Trinneer', 1969);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (34, 'Dominic Keating', 1962);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (35, 'Linda Park', 1978);
INSERT INTO "Actor" ("Id", "Name", "BirthYear") VALUES (36, 'Anthony Montgomery', 1971);

INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (1, 'James T. Kirk', 1, 2233.04);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (2, 'Spock', 2, 2230.06);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (3, 'Leonard McCoy', 3, 2227.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (4, 'Montgomery Scott', 4, 2222.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (5, 'Uhura', 5, 2233.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (6, 'Hikaru Sulu', 6, 2237.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (7, 'Pavel Chekov', 7, 2245.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (8, 'Jean-Luc Picard', 8, 2305.07);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (9, 'William Riker', 9, 2335.08);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (10, 'Data', 10, 2336.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (11, 'Worf', 11, 2340.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (12, 'Beverly Crusher', 12, 2324.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (13, 'Deanna Troi', 13, 2336.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (14, 'Geordi La Forge', 14, 2335.02);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (15, 'Kathryn Janeway', 15, 2336.05);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (16, 'Chakotay', 16, 2329.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (17, 'Tuvok', 17, 2264.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (18, 'B''Elanna Torres', 18, 2349.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (19, 'Tom Paris', 19, 2346.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (20, 'Harry Kim', 20, 2349.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (21, 'The Doctor', 21, 2371.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (22, 'Seven of Nine', 22, 2348.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (23, 'Benjamin Sisko', 23, 2332.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (24, 'Kira Nerys', 24, 2343.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (25, 'Odo', 25, 2337.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (27, 'Jadzia Dax', 26, 2341.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (28, 'Julian Bashir', 27, 2341.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (29, 'Quark', 28, 2333.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (30, 'Jake Sisko', 29, 2355.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (31, 'Jonathan Archer', 30, 2112.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (32, 'T''Pol', 31, 2088.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (33, 'Phlox', 32, 2102.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (34, 'Charles "Trip" Tucker III', 33, 2121.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (35, 'Malcolm Reed', 34, 2117.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (36, 'Hoshi Sato', 35, 2129.00);
INSERT INTO "Character" ("Id", "Name", "ActorId", "Stardate") VALUES (37, 'Travis Mayweather', 36, 2126.00);

INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (1, 1, 'Captain');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (1, 2, 'Science Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (1, 3, 'Doctor');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (1, 4, 'Engineer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (1, 5, 'Communications Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (1, 6, 'Helmsman');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (1, 7, 'Navigator');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (2, 8, 'Captain');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (2, 9, 'First Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (2, 10, 'Operations Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (2, 11, 'Security Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (2, 12, 'Doctor');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (2, 13, 'Counselor');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (2, 14, 'Engineer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 15, 'Captain');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 16, 'First Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 17, 'Tactical Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 18, 'Engineer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 19, 'Helmsman');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 20, 'Operations Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 21, 'Doctor');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (3, 22, 'Astrometrics Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 23, 'Commanding Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 24, 'First Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 25, 'Security Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 11, 'Strategic Operations Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 27, 'Science Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 28, 'Doctor');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 29, 'Bar Owner');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (4, 30, 'Civilian');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (5, 31, 'Captain');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (5, 32, 'Science Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (5, 33, 'Doctor');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (5, 34, 'Chief Engineer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (5, 35, 'Armory Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (5, 36, 'Communications Officer');
INSERT INTO "Series_Character" ("SeriesId", "CharacterId", "Role") VALUES (5, 37, 'Helmsman');

INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (1, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (2, 2);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (2, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (3, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (4, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (5, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (6, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (7, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (8, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (9, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (10, 3);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (11, 4);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (12, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (13, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (13, 5);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (14, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (15, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (16, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (17, 2);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (18, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (18, 4);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (19, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (20, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (21, 6);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (22, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (22, 12);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (23, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (24, 7);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (25, 8);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (27, 9);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (28, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (29, 10);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (30, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (31, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (32, 2);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (33, 11);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (34, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (35, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (36, 1);
INSERT INTO "Character_Species" ("CharacterId", "SpeciesId") VALUES (37, 1);

COMMIT;
