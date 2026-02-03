-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.

-- Drop views
BEGIN
    EXECUTE IMMEDIATE 'DROP VIEW books_view_all';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP VIEW books_view_with_mapping';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP VIEW stocks_view_selected';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP VIEW books_publishers_view_composite';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP VIEW books_publishers_view_composite_insertable';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

-- Drop procedures
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE get_books';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE get_book_by_id';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE get_publisher_by_id';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE insert_book';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE count_books';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE delete_last_inserted_book';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE update_book_title';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE get_authors_history_by_first_name';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP PROCEDURE insert_and_display_all_books_for_given_publisher';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

-- Drop tables
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE book_author_link CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE book_author_link_mm CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE reviews CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE reviews_mm CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE authors CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE authors_mm CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE book_website_placements CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE website_users CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE website_users_mm CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE books CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE books_mm CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE players CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE clubs CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE publishers CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE publishers_mm CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE magazines CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE stocks_price CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE stocks CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE comics CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE brokers CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE type_table CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE trees CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE fungi CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE empty_table CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE notebooks CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE journals CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE aow CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE series CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE sales CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE authors_history CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE revenues CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE graphql_incompatible CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE GQLmappings CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE bookmarks CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE mappedbookmarks CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE fte_data CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE intern_data CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE books_sold CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE default_with_function_table CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE DimAccount CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE date_only_table CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE users CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE user_profiles CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE default_books CASCADE CONSTRAINTS';
EXCEPTION
    WHEN OTHERS THEN NULL;
END;
/

-- Drop sequences
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE publishers_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE publishers_mm_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE books_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE books_mm_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE players_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE clubs_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE book_website_placements_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE authors_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE authors_mm_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE reviews_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE reviews_mm_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE comics_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE type_table_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE series_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE sales_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE authors_history_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE bookmarks_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE mappedbookmarks_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE fte_data_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE default_with_function_table_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE dimaccount_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE users_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE user_profiles_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/
BEGIN EXECUTE IMMEDIATE 'DROP SEQUENCE default_books_seq'; EXCEPTION WHEN OTHERS THEN NULL; END;
/

-- Create sequences (starting at 5001 for consistency)
CREATE SEQUENCE publishers_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE publishers_mm_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE books_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE books_mm_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE players_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE clubs_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE book_website_placements_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE authors_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE authors_mm_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE reviews_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE reviews_mm_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE comics_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE type_table_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE series_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE sales_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE authors_history_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE bookmarks_seq START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE mappedbookmarks_seq START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE fte_data_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE default_with_function_table_seq START WITH 5001 INCREMENT BY 1;
CREATE SEQUENCE dimaccount_seq START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE users_seq START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE user_profiles_seq START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE default_books_seq START WITH 5001 INCREMENT BY 1;

-- Create tables
CREATE TABLE publishers(
    id NUMBER PRIMARY KEY,
    name VARCHAR2(4000) NOT NULL
);

CREATE TABLE publishers_mm(
    id NUMBER PRIMARY KEY,
    name VARCHAR2(4000) NOT NULL
);

CREATE TABLE books(
    id NUMBER PRIMARY KEY,
    title VARCHAR2(4000) NOT NULL,
    publisher_id NUMBER NOT NULL
);

CREATE TABLE books_mm(
    id NUMBER PRIMARY KEY,
    title VARCHAR2(4000) NOT NULL,
    publisher_id NUMBER NOT NULL
);

CREATE TABLE default_books(
    id NUMBER PRIMARY KEY,
    title VARCHAR2(100) DEFAULT 'Placeholder'
);

CREATE TABLE players(
    id NUMBER PRIMARY KEY,
    name VARCHAR2(4000) NOT NULL,
    current_club_id NUMBER NOT NULL,
    new_club_id NUMBER NOT NULL
);

CREATE TABLE clubs(
    id NUMBER PRIMARY KEY,
    name VARCHAR2(4000) NOT NULL
);

CREATE TABLE book_website_placements(
    id NUMBER PRIMARY KEY,
    book_id NUMBER UNIQUE NOT NULL,
    price NUMBER NOT NULL
);

CREATE TABLE website_users(
    id NUMBER PRIMARY KEY,
    username VARCHAR2(4000) NULL
);

CREATE TABLE website_users_mm(
    id NUMBER PRIMARY KEY,
    username VARCHAR2(4000) NULL
);

CREATE TABLE authors(
    id NUMBER PRIMARY KEY,
    name VARCHAR2(4000) NOT NULL,
    birthdate VARCHAR2(4000) NOT NULL
);

CREATE TABLE authors_mm(
    id NUMBER PRIMARY KEY,
    name VARCHAR2(4000) NOT NULL,
    birthdate VARCHAR2(4000) NOT NULL
);

CREATE TABLE reviews(
    book_id NUMBER,
    id NUMBER,
    content VARCHAR2(4000) DEFAULT 'Its a classic' NOT NULL,
    websiteuser_id NUMBER DEFAULT 1,
    PRIMARY KEY(book_id, id)
);

CREATE TABLE reviews_mm(
    book_id NUMBER,
    id NUMBER,
    content VARCHAR2(4000) DEFAULT 'Its a classic' NOT NULL,
    websiteuser_id NUMBER DEFAULT 1,
    PRIMARY KEY(book_id, id)
);

CREATE TABLE book_author_link(
    book_id NUMBER NOT NULL,
    author_id NUMBER NOT NULL,
    royalty_percentage BINARY_DOUBLE DEFAULT 0 NULL,
    PRIMARY KEY(book_id, author_id)
);

CREATE TABLE book_author_link_mm(
    book_id NUMBER NOT NULL,
    author_id NUMBER NOT NULL,
    royalty_percentage BINARY_DOUBLE DEFAULT 0 NULL,
    PRIMARY KEY(book_id, author_id)
);

CREATE TABLE magazines(
    id NUMBER PRIMARY KEY,
    title VARCHAR2(4000) NOT NULL,
    issue_number NUMBER NULL
);

CREATE TABLE comics(
    id NUMBER PRIMARY KEY,
    title VARCHAR2(4000) NOT NULL,
    volume NUMBER,
    categoryName VARCHAR2(100) NOT NULL UNIQUE,
    series_id NUMBER NULL
);

CREATE TABLE stocks(
    categoryid NUMBER NOT NULL,
    pieceid NUMBER NOT NULL,
    categoryName VARCHAR2(100) NOT NULL,
    piecesAvailable NUMBER DEFAULT 0,
    piecesRequired NUMBER DEFAULT 0 NOT NULL,
    PRIMARY KEY(categoryid, pieceid)
);

CREATE TABLE stocks_price(
    categoryid NUMBER NOT NULL,
    pieceid NUMBER NOT NULL,
    instant TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
    price BINARY_DOUBLE,
    is_wholesale_price NUMBER(1),
    PRIMARY KEY(categoryid, pieceid, instant)
);

CREATE TABLE brokers(
    "ID Number" NUMBER PRIMARY KEY,
    "First Name" VARCHAR2(4000) NOT NULL,
    "Last Name" VARCHAR2(4000) NOT NULL
);

CREATE TABLE type_table(
    id NUMBER PRIMARY KEY,
    byte_types NUMBER(3),
    short_types NUMBER(5),
    int_types NUMBER(10),
    long_types NUMBER(19),
    string_types CLOB,
    nvarchar_string_types NCLOB,
    single_types BINARY_FLOAT,
    float_types BINARY_DOUBLE,
    decimal_types NUMBER(38, 19),
    boolean_types NUMBER(1),
    date_types DATE,
    datetime_types TIMESTAMP,
    datetime2_types TIMESTAMP(7),
    datetimeoffset_types TIMESTAMP WITH TIME ZONE,
    smalldatetime_types TIMESTAMP,
    time_types INTERVAL DAY(0) TO SECOND(7),
    bytearray_types RAW(2000),
    uuid_types RAW(16)
);

CREATE TABLE trees (
    treeId NUMBER PRIMARY KEY,
    species VARCHAR2(4000),
    region VARCHAR2(4000),
    height VARCHAR2(4000)
);

CREATE TABLE fungi (
    speciesid NUMBER PRIMARY KEY,
    region VARCHAR2(4000),
    habitat VARCHAR2(6)
);

CREATE TABLE empty_table (
    id NUMBER PRIMARY KEY
);

CREATE TABLE notebooks (
    id NUMBER PRIMARY KEY,
    notebookname VARCHAR2(4000),
    color VARCHAR2(4000),
    ownername VARCHAR2(4000)
);

CREATE TABLE journals (
    id NUMBER PRIMARY KEY,
    journalname VARCHAR2(4000),
    color VARCHAR2(4000),
    ownername VARCHAR2(4000)
);

CREATE TABLE aow (
    NoteNum NUMBER PRIMARY KEY,
    DetailAssessmentAndPlanning CLOB,
    WagingWar CLOB,
    StrategicAttack CLOB
);

CREATE TABLE series (
    id NUMBER PRIMARY KEY,
    name NVARCHAR2(1000) NOT NULL
);

CREATE TABLE sales (
    id NUMBER PRIMARY KEY,
    item_name VARCHAR2(4000) NOT NULL,
    subtotal NUMBER(18,2) NOT NULL,
    tax NUMBER(18,2) NOT NULL,
    total NUMBER(18,2) GENERATED ALWAYS AS (subtotal + tax) VIRTUAL
);

CREATE TABLE authors_history (
    id NUMBER PRIMARY KEY,
    first_name VARCHAR2(100) NOT NULL,
    middle_name VARCHAR2(100),
    last_name VARCHAR2(100) NOT NULL,
    year_of_publish NUMBER,
    books_published NUMBER
);

CREATE TABLE revenues(
    id NUMBER PRIMARY KEY,
    category VARCHAR2(4000) NOT NULL,
    revenue NUMBER,
    accessible_role VARCHAR2(4000) NOT NULL
);

CREATE TABLE graphql_incompatible (
    __typeName NUMBER PRIMARY KEY,
    conformingName VARCHAR2(12)
);

CREATE TABLE GQLmappings (
    __column1 NUMBER PRIMARY KEY,
    __column2 VARCHAR2(4000),
    column3 VARCHAR2(4000)
);

CREATE TABLE bookmarks(
    id NUMBER PRIMARY KEY,
    bkname NVARCHAR2(1000) NOT NULL
);

CREATE TABLE mappedbookmarks(
    id NUMBER PRIMARY KEY,
    bkname NVARCHAR2(50) NOT NULL
);

CREATE TABLE fte_data(
    id NUMBER,
    u_id NUMBER DEFAULT 2,
    name VARCHAR2(50),
    position VARCHAR2(20),
    salary NUMBER DEFAULT 20,
    PRIMARY KEY(id, u_id)
);

CREATE TABLE intern_data(
    id NUMBER,
    months NUMBER DEFAULT 2 NOT NULL,
    name VARCHAR2(50),
    salary NUMBER DEFAULT 15,
    PRIMARY KEY(id, months)
);

CREATE TABLE books_sold(
    id NUMBER PRIMARY KEY NOT NULL,
    book_name VARCHAR2(50),
    row_version RAW(8),
    copies_sold NUMBER DEFAULT 0,
    last_sold_on TIMESTAMP DEFAULT TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS'),
    last_sold_on_date TIMESTAMP GENERATED ALWAYS AS (last_sold_on) VIRTUAL
);

CREATE TABLE default_with_function_table(
    id NUMBER PRIMARY KEY,
    user_value NUMBER,
    current_date TIMESTAMP DEFAULT SYSDATE NOT NULL,
    current_timestamp TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    random_number NUMBER DEFAULT TRUNC(DBMS_RANDOM.VALUE(0, 1000)) NOT NULL,
    next_date TIMESTAMP DEFAULT TRUNC(SYSDATE) + 1 NOT NULL,
    default_string_with_parenthesis VARCHAR2(100) DEFAULT '()',
    default_function_string_with_parenthesis VARCHAR2(100) DEFAULT 'NOW()',
    default_integer NUMBER DEFAULT 100,
    default_date_string TIMESTAMP DEFAULT TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS')
);

CREATE TABLE DimAccount (
    AccountKey NUMBER PRIMARY KEY,
    ParentAccountKey NUMBER NULL
);

CREATE TABLE users (
    userid NUMBER PRIMARY KEY,
    username NVARCHAR2(50) UNIQUE,
    email NVARCHAR2(100)
);

CREATE TABLE user_profiles (
    profileid NUMBER PRIMARY KEY,
    username NVARCHAR2(50) UNIQUE,
    profilepictureurl NVARCHAR2(255),
    userid NUMBER
);

CREATE TABLE date_only_table (
    event_date DATE NOT NULL,
    event_time INTERVAL DAY(0) TO SECOND(0) NOT NULL,
    event_timestamp TIMESTAMP NOT NULL
);

-- Add foreign key constraints
ALTER TABLE books
ADD CONSTRAINT book_publisher_fk
FOREIGN KEY (publisher_id)
REFERENCES publishers (id)
ON DELETE CASCADE;

ALTER TABLE players
ADD CONSTRAINT player_club_fk
FOREIGN KEY (current_club_id)
REFERENCES clubs (id)
ON DELETE CASCADE;

ALTER TABLE book_website_placements
ADD CONSTRAINT book_website_placement_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE reviews
ADD CONSTRAINT review_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_book_fk
FOREIGN KEY (book_id)
REFERENCES books (id)
ON DELETE CASCADE;

ALTER TABLE book_author_link
ADD CONSTRAINT book_author_link_author_fk
FOREIGN KEY (author_id)
REFERENCES authors (id)
ON DELETE CASCADE;

ALTER TABLE stocks
ADD CONSTRAINT stocks_comics_fk
FOREIGN KEY (categoryName)
REFERENCES comics (categoryName)
ON DELETE CASCADE;

ALTER TABLE stocks_price
ADD CONSTRAINT stocks_price_stocks_fk
FOREIGN KEY (categoryid, pieceid)
REFERENCES stocks (categoryid, pieceid)
ON DELETE CASCADE;

ALTER TABLE comics
ADD CONSTRAINT comics_series_fk
FOREIGN KEY (series_id)
REFERENCES series(id)
ON DELETE CASCADE;

ALTER TABLE DimAccount
ADD CONSTRAINT FK_DimAccount_DimAccount
FOREIGN KEY (ParentAccountKey)
REFERENCES DimAccount (AccountKey);

-- Create triggers for auto-increment simulation
CREATE OR REPLACE TRIGGER publishers_bir
BEFORE INSERT ON publishers
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT publishers_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER publishers_mm_bir
BEFORE INSERT ON publishers_mm
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT publishers_mm_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER books_bir
BEFORE INSERT ON books
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT books_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER books_mm_bir
BEFORE INSERT ON books_mm
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT books_mm_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER players_bir
BEFORE INSERT ON players
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT players_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER clubs_bir
BEFORE INSERT ON clubs
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT clubs_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER book_website_placements_bir
BEFORE INSERT ON book_website_placements
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT book_website_placements_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER authors_bir
BEFORE INSERT ON authors
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT authors_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER authors_mm_bir
BEFORE INSERT ON authors_mm
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT authors_mm_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER reviews_bir
BEFORE INSERT ON reviews
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT reviews_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER reviews_mm_bir
BEFORE INSERT ON reviews_mm
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT reviews_mm_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER comics_bir
BEFORE INSERT ON comics
FOR EACH ROW
BEGIN
    IF :new.volume IS NULL THEN
        SELECT comics_seq.NEXTVAL INTO :new.volume FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER type_table_bir
BEFORE INSERT ON type_table
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT type_table_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER series_bir
BEFORE INSERT ON series
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT series_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER sales_bir
BEFORE INSERT ON sales
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT sales_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER authors_history_bir
BEFORE INSERT ON authors_history
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT authors_history_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER bookmarks_bir
BEFORE INSERT ON bookmarks
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT bookmarks_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER mappedbookmarks_bir
BEFORE INSERT ON mappedbookmarks
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT mappedbookmarks_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER fte_data_bir
BEFORE INSERT ON fte_data
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT fte_data_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER default_with_function_table_bir
BEFORE INSERT ON default_with_function_table
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT default_with_function_table_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER dimaccount_bir
BEFORE INSERT ON DimAccount
FOR EACH ROW
BEGIN
    IF :new.AccountKey IS NULL THEN
        SELECT dimaccount_seq.NEXTVAL INTO :new.AccountKey FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER users_bir
BEFORE INSERT ON users
FOR EACH ROW
BEGIN
    IF :new.userid IS NULL THEN
        SELECT users_seq.NEXTVAL INTO :new.userid FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER user_profiles_bir
BEFORE INSERT ON user_profiles
FOR EACH ROW
BEGIN
    IF :new.profileid IS NULL THEN
        SELECT user_profiles_seq.NEXTVAL INTO :new.profileid FROM dual;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER default_books_bir
BEFORE INSERT ON default_books
FOR EACH ROW
BEGIN
    IF :new.id IS NULL THEN
        SELECT default_books_seq.NEXTVAL INTO :new.id FROM dual;
    END IF;
END;
/

-- Insert data
INSERT INTO publishers(id, name) VALUES (1234, 'Big Company');
INSERT INTO publishers(id, name) VALUES (2345, 'Small Town Publisher');
INSERT INTO publishers(id, name) VALUES (2323, 'TBD Publishing One');
INSERT INTO publishers(id, name) VALUES (2324, 'TBD Publishing Two Ltd');
INSERT INTO publishers(id, name) VALUES (1940, 'Policy Publisher 01');
INSERT INTO publishers(id, name) VALUES (1941, 'Policy Publisher 02');
INSERT INTO publishers(id, name) VALUES (1156, 'The First Publisher');

INSERT INTO publishers_mm(id, name) VALUES (1234, 'Big Company');
INSERT INTO publishers_mm(id, name) VALUES (2345, 'Small Town Publisher');
INSERT INTO publishers_mm(id, name) VALUES (2323, 'TBD Publishing One');
INSERT INTO publishers_mm(id, name) VALUES (2324, 'TBD Publishing Two Ltd');
INSERT INTO publishers_mm(id, name) VALUES (1940, 'Policy Publisher 01');
INSERT INTO publishers_mm(id, name) VALUES (1941, 'Policy Publisher 02');
INSERT INTO publishers_mm(id, name) VALUES (1156, 'The First Publisher');

INSERT INTO clubs(id, name) VALUES (1111, 'Manchester United');
INSERT INTO clubs(id, name) VALUES (1112, 'FC Barcelona');
INSERT INTO clubs(id, name) VALUES (1113, 'Real Madrid');

INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01');
INSERT INTO authors(id, name, birthdate) VALUES (124, 'Aniruddh', '2002-02-02');
INSERT INTO authors(id, name, birthdate) VALUES (125, 'Aniruddh', '2001-01-01');
INSERT INTO authors(id, name, birthdate) VALUES (126, 'Aaron', '2001-01-01');

INSERT INTO authors_mm(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01');
INSERT INTO authors_mm(id, name, birthdate) VALUES (124, 'Aniruddh', '2002-02-02');
INSERT INTO authors_mm(id, name, birthdate) VALUES (125, 'Aniruddh', '2001-01-01');
INSERT INTO authors_mm(id, name, birthdate) VALUES (126, 'Aaron', '2001-01-01');

INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (1, 'Incompatible GraphQL Name', 'Compatible GraphQL Name');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (3, 'Old Value', 'Record to be Updated');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (4, 'Lost Record', 'Record to be Deleted');
INSERT INTO GQLmappings(__column1, __column2, column3) VALUES (5, 'Filtered Record', 'Record to be Filtered on Find');

-- Insert bookmarks (1 to 10000)
DECLARE
    v_counter NUMBER := 1;
BEGIN
    WHILE v_counter <= 10000 LOOP
        INSERT INTO bookmarks (id, bkname)
        VALUES (v_counter, 'Test Item #' || LPAD(v_counter, 5, '0'));
        v_counter := v_counter + 1;
    END LOOP;
    COMMIT;
END;
/

-- Insert mappedbookmarks (1 to 10000)
DECLARE
    v_counter NUMBER := 1;
BEGIN
    WHILE v_counter <= 10000 LOOP
        INSERT INTO mappedbookmarks (id, bkname)
        VALUES (v_counter, 'Test Item #' || LPAD(v_counter, 5, '0'));
        v_counter := v_counter + 1;
    END LOOP;
    COMMIT;
END;
/

INSERT INTO books(id, title, publisher_id) VALUES (1, 'Awesome book', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (2, 'Also Awesome book', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (3, 'Great wall of china explained', 2345);
INSERT INTO books(id, title, publisher_id) VALUES (4, 'US history in a nutshell', 2345);
INSERT INTO books(id, title, publisher_id) VALUES (5, 'Chernobyl Diaries', 2323);
INSERT INTO books(id, title, publisher_id) VALUES (6, 'The Palace Door', 2324);
INSERT INTO books(id, title, publisher_id) VALUES (7, 'The Groovy Bar', 2324);
INSERT INTO books(id, title, publisher_id) VALUES (8, 'Time to Eat', 2324);
INSERT INTO books(id, title, publisher_id) VALUES (9, 'Policy-Test-01', 1940);
INSERT INTO books(id, title, publisher_id) VALUES (10, 'Policy-Test-02', 1940);
INSERT INTO books(id, title, publisher_id) VALUES (11, 'Policy-Test-04', 1941);
INSERT INTO books(id, title, publisher_id) VALUES (12, 'Time to Eat 2', 1941);
INSERT INTO books(id, title, publisher_id) VALUES (13, 'Before Sunrise', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (14, 'Before Sunset', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (15, 'SQL_CONN', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (16, 'SOME%CONN', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (17, 'CONN%_CONN', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (18, '[Special Book]', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (19, 'ME\YOU', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (20, 'C:\LIFE', 1234);
INSERT INTO books(id, title, publisher_id) VALUES (21, '', 1234);

INSERT INTO books_mm(id, title, publisher_id) VALUES (1, 'Awesome book', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (2, 'Also Awesome book', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (3, 'Great wall of china explained', 2345);
INSERT INTO books_mm(id, title, publisher_id) VALUES (4, 'US history in a nutshell', 2345);
INSERT INTO books_mm(id, title, publisher_id) VALUES (5, 'Chernobyl Diaries', 2323);
INSERT INTO books_mm(id, title, publisher_id) VALUES (6, 'The Palace Door', 2324);
INSERT INTO books_mm(id, title, publisher_id) VALUES (7, 'The Groovy Bar', 2324);
INSERT INTO books_mm(id, title, publisher_id) VALUES (8, 'Time to Eat', 2324);
INSERT INTO books_mm(id, title, publisher_id) VALUES (9, 'Policy-Test-01', 1940);
INSERT INTO books_mm(id, title, publisher_id) VALUES (10, 'Policy-Test-02', 1940);
INSERT INTO books_mm(id, title, publisher_id) VALUES (11, 'Policy-Test-04', 1941);
INSERT INTO books_mm(id, title, publisher_id) VALUES (12, 'Time to Eat 2', 1941);
INSERT INTO books_mm(id, title, publisher_id) VALUES (13, 'Before Sunrise', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (14, 'Before Sunset', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (15, 'SQL_CONN', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (16, 'SOME%CONN', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (17, 'CONN%_CONN', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (18, '[Special Book]', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (19, 'ME\YOU', 1234);
INSERT INTO books_mm(id, title, publisher_id) VALUES (20, 'C:\LIFE', 1234);

INSERT INTO players(id, name, current_club_id, new_club_id) VALUES (1, 'Cristiano Ronaldo', 1113, 1111);
INSERT INTO players(id, name, current_club_id, new_club_id) VALUES (2, 'Leonel Messi', 1112, 1113);

INSERT INTO book_website_placements(id, book_id, price) VALUES (1, 1, 100);
INSERT INTO book_website_placements(id, book_id, price) VALUES (2, 2, 50);
INSERT INTO book_website_placements(id, book_id, price) VALUES (3, 3, 23);
INSERT INTO book_website_placements(id, book_id, price) VALUES (4, 5, 33);

INSERT INTO book_author_link(book_id, author_id) VALUES (1, 123);
INSERT INTO book_author_link(book_id, author_id) VALUES (2, 124);
INSERT INTO book_author_link(book_id, author_id) VALUES (3, 123);
INSERT INTO book_author_link(book_id, author_id) VALUES (3, 124);
INSERT INTO book_author_link(book_id, author_id) VALUES (4, 123);
INSERT INTO book_author_link(book_id, author_id) VALUES (4, 124);
INSERT INTO book_author_link(book_id, author_id) VALUES (5, 126);

INSERT INTO book_author_link_mm(book_id, author_id) VALUES (1, 123);
INSERT INTO book_author_link_mm(book_id, author_id) VALUES (2, 124);
INSERT INTO book_author_link_mm(book_id, author_id) VALUES (3, 123);
INSERT INTO book_author_link_mm(book_id, author_id) VALUES (3, 124);
INSERT INTO book_author_link_mm(book_id, author_id) VALUES (4, 123);
INSERT INTO book_author_link_mm(book_id, author_id) VALUES (4, 124);
INSERT INTO book_author_link_mm(book_id, author_id) VALUES (5, 126);

INSERT INTO website_users(id, username) VALUES (1, 'George');
INSERT INTO website_users(id, username) VALUES (2, NULL);
INSERT INTO website_users(id, username) VALUES (3, '');
INSERT INTO website_users(id, username) VALUES (4, 'book_lover_95');
INSERT INTO website_users(id, username) VALUES (5, 'null');

INSERT INTO website_users_mm(id, username) VALUES (1, 'George');
INSERT INTO website_users_mm(id, username) VALUES (2, NULL);
INSERT INTO website_users_mm(id, username) VALUES (3, '');
INSERT INTO website_users_mm(id, username) VALUES (4, 'book_lover_95');
INSERT INTO website_users_mm(id, username) VALUES (5, 'null');

INSERT INTO reviews(id, book_id, content) VALUES (567, 1, 'Indeed a great book');
INSERT INTO reviews(id, book_id, content) VALUES (568, 1, 'I loved it');
INSERT INTO reviews(id, book_id, content) VALUES (569, 1, 'best book I read in years');

INSERT INTO reviews_mm(id, book_id, content) VALUES (567, 1, 'Indeed a great book');
INSERT INTO reviews_mm(id, book_id, content) VALUES (568, 1, 'I loved it');
INSERT INTO reviews_mm(id, book_id, content) VALUES (569, 1, 'best book I read in years');

INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
    single_types, float_types, decimal_types, boolean_types,
    date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
    bytearray_types)
VALUES (1, 1, 1, 1, 1, '', '',
    0.33, 0.33, 0.333333, 1,
    TO_DATE('1999-01-08', 'YYYY-MM-DD'), TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS'),
    TO_TIMESTAMP('1999-01-08 10:23:54.9999999', 'YYYY-MM-DD HH24:MI:SS.FF7'),
    TO_TIMESTAMP_TZ('1999-01-08 10:23:54.9999999 -14:00', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM'),
    TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS'),
    HEXTORAW('ABCDEF0123'));

INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
    single_types, float_types, decimal_types, boolean_types,
    date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
    bytearray_types)
VALUES (2, 0, -1, -1, -1, 'lksa;jdflasdf;alsdflksdfkldj', 'lksa;jdflasdf;alsdflksdfkldj',
    -9.2, -9.2, -9.292929, 0,
    TO_DATE('1999-01-08', 'YYYY-MM-DD'), TO_TIMESTAMP('1999-01-08 10:23:00', 'YYYY-MM-DD HH24:MI:SS'),
    TO_TIMESTAMP('1999-01-08 10:23:00.9999999', 'YYYY-MM-DD HH24:MI:SS.FF7'),
    TO_TIMESTAMP_TZ('1999-01-08 10:23:00.9999999 +13:00', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM'),
    TO_TIMESTAMP('1999-01-08 10:23:00', 'YYYY-MM-DD HH24:MI:SS'),
    HEXTORAW('98AB7511AABB1234'));

INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
    single_types, float_types, decimal_types, boolean_types,
    date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
    bytearray_types)
VALUES (3, 0, -32768, -2147483648, -9223372036854775808, 'null', 'null',
    -3.4E38, -1.7E308, 2.929292E-19, 1,
    TO_DATE('0001-01-01', 'YYYY-MM-DD'), TO_TIMESTAMP('1753-01-01 00:00:00.000', 'YYYY-MM-DD HH24:MI:SS.FF3'),
    TO_TIMESTAMP('0001-01-01 00:00:00.0000000', 'YYYY-MM-DD HH24:MI:SS.FF7'),
    TO_TIMESTAMP_TZ('0001-01-01 00:00:00.0000000 +0:00', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM'),
    TO_TIMESTAMP('1900-01-01 00:00:00', 'YYYY-MM-DD HH24:MI:SS'),
    HEXTORAW('00000000'));

INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
    single_types, float_types, decimal_types, boolean_types,
    date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
    bytearray_types)
VALUES (4, 255, 32767, 2147483647, 9223372036854775807, 'null', 'null',
    3.4E38, 1.7E308, 2.929292E-14, 1,
    TO_DATE('9999-12-31', 'YYYY-MM-DD'), TO_TIMESTAMP('9999-12-31 23:59:59', 'YYYY-MM-DD HH24:MI:SS'),
    TO_TIMESTAMP('9999-12-31 23:59:59.9999999', 'YYYY-MM-DD HH24:MI:SS.FF7'),
    TO_TIMESTAMP_TZ('9999-12-31 23:59:59.9999999 +14:00', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM'),
    TO_TIMESTAMP('2079-06-06 00:00:00', 'YYYY-MM-DD HH24:MI:SS'),
    HEXTORAW('FFFFFFFF'));

INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
    single_types, float_types, decimal_types, boolean_types,
    date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
    bytearray_types)
VALUES (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);

INSERT INTO type_table(id, uuid_types) VALUES (10, HEXTORAW('D1D021A847B44AE4B71898E89C41A161'));

INSERT INTO sales(id, item_name, subtotal, tax) VALUES (1, 'Watch', 249.00, 20.59);
INSERT INTO sales(id, item_name, subtotal, tax) VALUES (2, 'Montior', 120.50, 11.12);

INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (1, 'Notebook1', 'red', 'Sean');
INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (2, 'Notebook2', 'green', 'Ani');
INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (3, 'Notebook3', 'blue', 'Jarupat');
INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (4, 'Notebook4', 'yellow', 'Aaron');

INSERT INTO journals(id, journalname, color, ownername) VALUES (1, 'Journal1', 'red', 'Sean');
INSERT INTO journals(id, journalname, color, ownername) VALUES (2, 'Journal2', 'green', 'Ani');
INSERT INTO journals(id, journalname, color, ownername) VALUES (3, 'Journal3', 'blue', 'Jarupat');
INSERT INTO journals(id, journalname, color, ownername) VALUES (4, 'Journal4', 'yellow', 'Aaron');
INSERT INTO journals(id, journalname, color, ownername) VALUES (5, 'Journal5', null, 'Abhishek');
INSERT INTO journals(id, journalname, color, ownername) VALUES (6, 'Journal6', 'green', null);
INSERT INTO journals(id, journalname, color, ownername) VALUES (7, 'Journal7', null, null);

INSERT INTO magazines(id, title, issue_number) VALUES (1, 'Vogue', 1234);
INSERT INTO magazines(id, title, issue_number) VALUES (11, 'Sports Illustrated', NULL);
INSERT INTO magazines(id, title, issue_number) VALUES (3, 'Fitness', NULL);

INSERT INTO brokers("ID Number", "First Name", "Last Name") VALUES (1, 'Michael', 'Burry');
INSERT INTO brokers("ID Number", "First Name", "Last Name") VALUES (2, 'Jordan', 'Belfort');

INSERT INTO series(id, name) VALUES (3001, 'Foundation');
INSERT INTO series(id, name) VALUES (3002, 'Hyperion Cantos');

INSERT INTO comics(id, title, categoryName, series_id) VALUES (1, 'Star Trek', 'SciFi', NULL);
INSERT INTO comics(id, title, categoryName, series_id) VALUES (2, 'Cinderella', 'Tales', 3001);
INSERT INTO comics(id, title, categoryName, series_id) VALUES (3, 'Únknown', '', 3002);
INSERT INTO comics(id, title, categoryName, series_id) VALUES (4, 'Alexander the Great', 'Historical', NULL);
INSERT INTO comics(id, title, categoryName, series_id) VALUES (5, 'Snow White', 'AnotherTales', 3001);

INSERT INTO stocks(categoryid, pieceid, categoryName) VALUES (1, 1, 'SciFi');
INSERT INTO stocks(categoryid, pieceid, categoryName) VALUES (2, 1, 'Tales');
INSERT INTO stocks(categoryid, pieceid, categoryName) VALUES (0, 1, '');
INSERT INTO stocks(categoryid, pieceid, categoryName) VALUES (100, 99, 'Historical');

INSERT INTO stocks_price(categoryid, pieceid, price, is_wholesale_price) VALUES (2, 1, 100.57, 1);
INSERT INTO stocks_price(categoryid, pieceid, price, is_wholesale_price) VALUES (1, 1, 42.75, 0);
INSERT INTO stocks_price(categoryid, pieceid, price, is_wholesale_price) VALUES (100, 99, NULL, NULL);
INSERT INTO stocks_price(categoryid, pieceid, instant, price, is_wholesale_price)
VALUES (2, 1, TO_TIMESTAMP('2023-08-21 15:11:04', 'YYYY-MM-DD HH24:MI:SS'), 100.57, 1);

INSERT INTO trees(treeId, species, region, height) VALUES (1, 'Tsuga terophylla', 'Pacific Northwest', '30m');
INSERT INTO trees(treeId, species, region, height) VALUES (2, 'Pseudotsuga menziesii', 'Pacific Northwest', '40m');
INSERT INTO trees(treeId, species, region, height) VALUES (4, 'test', 'Pacific Northwest', '0m');

INSERT INTO aow(NoteNum, DetailAssessmentAndPlanning, WagingWar, StrategicAttack)
VALUES (1, 'chapter one notes: ', 'chapter two notes: ', 'chapter three notes: ');

INSERT INTO fungi(speciesid, region, habitat) VALUES (1, 'northeast', 'forest');
INSERT INTO fungi(speciesid, region, habitat) VALUES (2, 'southwest', 'sand');
INSERT INTO fungi(speciesid, region, habitat) VALUES (3, 'northeast', 'test');

INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (1, 'Isaac', null, 'Asimov', 1993, 6);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (2, 'Robert', 'A.', 'Heinlein', 1886, null);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (3, 'Robert', null, 'Silvenberg', null, null);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (4, 'Dan', null, 'Simmons', 1759, 3);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (5, 'Isaac', null, 'Asimov', 2000, null);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (6, 'Robert', 'A.', 'Heinlein', 1899, 2);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (7, 'Isaac', null, 'Silvenberg', 1664, null);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (8, 'Dan', null, 'Simmons', 1799, 3);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (9, 'Aaron', null, 'Mitchells', 2001, 1);
INSERT INTO authors_history(id, first_name, middle_name, last_name, year_of_publish, books_published)
VALUES (10, 'Aaron', 'F.', 'Burtle', null, null);

INSERT INTO fte_data(id, name, position, salary) VALUES (1, 'Ellie', 'Junior Dev', 20);
INSERT INTO fte_data(id, name, position, salary) VALUES (2, 'Chris', 'Senior Dev', 40);

INSERT INTO intern_data(id, months, name) VALUES (1, 3, 'Tess');
INSERT INTO intern_data(id, months, name) VALUES (2, 4, 'Frank');

INSERT INTO revenues(id, category, revenue, accessible_role) VALUES (1, 'Book', 5000, 'Anonymous');
INSERT INTO revenues(id, category, revenue, accessible_role) VALUES (2, 'Comics', 10000, 'Anonymous');
INSERT INTO revenues(id, category, revenue, accessible_role) VALUES (3, 'Journals', 20000, 'Authenticated');
INSERT INTO revenues(id, category, revenue, accessible_role) VALUES (4, 'Series', 40000, 'Authenticated');

INSERT INTO books_sold(id, book_name, last_sold_on) VALUES (1, 'Awesome Book', SYSTIMESTAMP);

INSERT INTO users (username, email) VALUES ('john_doe', 'john.doe@example.com');
INSERT INTO users (username, email) VALUES ('jane_smith', 'jane.smith@example.com');

INSERT INTO user_profiles (username, profilepictureurl, userid)
VALUES ('john_doe', 'https://example.com/profiles/john_doe.jpg', 1);
INSERT INTO user_profiles (username, profilepictureurl, userid)
VALUES ('jane_smith', 'https://example.com/profiles/jane_smith.jpg', 2);

INSERT INTO DimAccount(AccountKey, ParentAccountKey) VALUES (1, null);
INSERT INTO DimAccount(AccountKey, ParentAccountKey) VALUES (2, 1);
INSERT INTO DimAccount(AccountKey, ParentAccountKey) VALUES (3, 2);
INSERT INTO DimAccount(AccountKey, ParentAccountKey) VALUES (4, 2);

INSERT INTO date_only_table(event_date, event_time, event_timestamp)
VALUES (TO_DATE('2023-01-01', 'YYYY-MM-DD'), INTERVAL '08:30:00' HOUR TO SECOND,
    TO_TIMESTAMP('2023-01-01 08:30:00', 'YYYY-MM-DD HH24:MI:SS'));
INSERT INTO date_only_table(event_date, event_time, event_timestamp)
VALUES (TO_DATE('2023-02-15', 'YYYY-MM-DD'), INTERVAL '12:45:00' HOUR TO SECOND,
    TO_TIMESTAMP('2023-02-15 12:45:00', 'YYYY-MM-DD HH24:MI:SS'));
INSERT INTO date_only_table(event_date, event_time, event_timestamp)
VALUES (TO_DATE('2023-03-30', 'YYYY-MM-DD'), INTERVAL '17:15:00' HOUR TO SECOND,
    TO_TIMESTAMP('2023-03-30 17:15:00', 'YYYY-MM-DD HH24:MI:SS'));

COMMIT;

-- Create views
CREATE VIEW books_view_all AS SELECT * FROM books;

CREATE VIEW books_view_with_mapping AS SELECT * FROM books;

CREATE VIEW stocks_view_selected AS
    SELECT categoryid, pieceid, categoryName, piecesAvailable
    FROM stocks;

CREATE VIEW books_publishers_view_composite AS SELECT
    publishers.name, books.id, books.title, publishers.id as pub_id
    FROM books, publishers
    WHERE publishers.id = books.publisher_id;

CREATE VIEW books_publishers_view_composite_insertable AS SELECT
    books.id, books.title, publishers.name, books.publisher_id
    FROM books, publishers
    WHERE publishers.id = books.publisher_id;

-- Create stored procedures
CREATE OR REPLACE PROCEDURE get_book_by_id(p_id IN NUMBER, p_cursor OUT SYS_REFCURSOR) AS
BEGIN
    OPEN p_cursor FOR
    SELECT * FROM books WHERE id = p_id;
END;
/

CREATE OR REPLACE PROCEDURE get_publisher_by_id(p_id IN NUMBER, p_cursor OUT SYS_REFCURSOR) AS
BEGIN
    OPEN p_cursor FOR
    SELECT * FROM publishers WHERE id = p_id;
END;
/

CREATE OR REPLACE PROCEDURE get_books(p_cursor OUT SYS_REFCURSOR) AS
BEGIN
    OPEN p_cursor FOR
    SELECT * FROM books;
END;
/

CREATE OR REPLACE PROCEDURE insert_book(p_title IN VARCHAR2, p_publisher_id IN NUMBER) AS
BEGIN
    INSERT INTO books(title, publisher_id) VALUES (p_title, p_publisher_id);
    COMMIT;
END;
/

CREATE OR REPLACE PROCEDURE count_books(p_cursor OUT SYS_REFCURSOR) AS
BEGIN
    OPEN p_cursor FOR
    SELECT COUNT(*) AS total_books FROM books;
END;
/

CREATE OR REPLACE PROCEDURE delete_last_inserted_book AS
    v_max_id NUMBER;
BEGIN
    SELECT MAX(id) INTO v_max_id FROM books;
    DELETE FROM books WHERE id = v_max_id;
    COMMIT;
END;
/

CREATE OR REPLACE PROCEDURE update_book_title(p_id IN NUMBER, p_title IN VARCHAR2, p_cursor OUT SYS_REFCURSOR) AS
BEGIN
    UPDATE books SET title = p_title WHERE id = p_id;
    OPEN p_cursor FOR
    SELECT * FROM books WHERE id = p_id;
    COMMIT;
END;
/

CREATE OR REPLACE PROCEDURE get_authors_history_by_first_name(p_firstName IN VARCHAR2, p_cursor OUT SYS_REFCURSOR) AS
BEGIN
    OPEN p_cursor FOR
    SELECT
        first_name || ' ' || NVL(middle_name || ' ', '') || last_name as author_name,
        MIN(year_of_publish) as first_publish_year,
        SUM(books_published) as total_books_published
    FROM authors_history
    WHERE first_name = p_firstName
    GROUP BY first_name || ' ' || NVL(middle_name || ' ', '') || last_name;
END;
/

CREATE OR REPLACE PROCEDURE insert_and_display_all_books_for_given_publisher(
    p_title IN VARCHAR2,
    p_publisher_name IN VARCHAR2,
    p_cursor OUT SYS_REFCURSOR
) AS
    v_publisher_id NUMBER;
BEGIN
    SELECT id INTO v_publisher_id FROM publishers WHERE name = p_publisher_name;
    INSERT INTO books(title, publisher_id) VALUES (p_title, v_publisher_id);
    OPEN p_cursor FOR
    SELECT * FROM books WHERE publisher_id = v_publisher_id;
    COMMIT;
END;
/

-- Create triggers for salary constraints (similar to SQL Server triggers)
CREATE OR REPLACE TRIGGER fte_data_after_insert_trigger
AFTER INSERT ON fte_data
FOR EACH ROW
BEGIN
    IF :new.salary > 100 THEN
        UPDATE fte_data SET salary = 100 WHERE id = :new.id AND u_id = :new.u_id;
    ELSIF :new.salary < 0 THEN
        UPDATE fte_data SET salary = 0 WHERE id = :new.id AND u_id = :new.u_id;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER fte_data_after_update_trigger
AFTER UPDATE ON fte_data
FOR EACH ROW
BEGIN
    IF :new.salary > 150 THEN
        UPDATE fte_data SET salary = 150 WHERE id = :new.id AND u_id = :new.u_id;
    ELSIF :new.salary < 0 THEN
        UPDATE fte_data SET salary = 0 WHERE id = :new.id AND u_id = :new.u_id;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER intern_data_after_insert_trigger
AFTER INSERT ON intern_data
FOR EACH ROW
BEGIN
    IF :new.salary > 30 THEN
        UPDATE intern_data SET salary = 30 WHERE id = :new.id AND months = :new.months;
    ELSIF :new.salary < 0 THEN
        UPDATE intern_data SET salary = 0 WHERE id = :new.id AND months = :new.months;
    END IF;
END;
/

CREATE OR REPLACE TRIGGER intern_data_after_update_trigger
AFTER UPDATE ON intern_data
FOR EACH ROW
BEGIN
    IF :new.salary > 50 THEN
        UPDATE intern_data SET salary = 50 WHERE id = :new.id AND months = :new.months;
    ELSIF :new.salary < 0 THEN
        UPDATE intern_data SET salary = 0 WHERE id = :new.id AND months = :new.months;
    END IF;
END;
/
