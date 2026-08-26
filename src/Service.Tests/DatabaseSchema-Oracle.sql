begin
    -- Copyright (c) Microsoft Corporation.
    -- Licensed under the MIT License.
    -- Drop views

   begin
      execute immediate q'[DROP VIEW books_view_all]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP VIEW books_view_with_mapping]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP VIEW stocks_view_selected]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP VIEW books_publishers_view_composite]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP VIEW books_publishers_view_composite_insertable]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;


    -- Drop procedures

   begin
      execute immediate q'[DROP PROCEDURE get_books]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE get_book_by_id]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE get_publisher_by_id]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE insert_book]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE count_books]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE delete_last_inserted_book]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE update_book_title]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE get_authors_history_by_first_name]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP PROCEDURE insert_and_display_all_books_for_given_publisher]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;


    -- Drop tables

   begin
      execute immediate q'[DROP TABLE book_author_link CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE book_author_link_mm CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE reviews CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE reviews_mm CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE authors CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE authors_mm CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE book_website_placements CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE website_users CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE website_users_mm CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE books CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE books_mm CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE players CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE clubs CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE publishers CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE publishers_mm CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE magazines CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE stocks_price CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE stocks CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE comics CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE brokers CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE type_table CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE trees CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE fungi CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE empty_table CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE notebooks CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE journals CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE aow CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE series CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE sales CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE authors_history CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE revenues CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE graphql_incompatible CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE GQLmappings CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE bookmarks CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE mappedbookmarks CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE fte_data CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE intern_data CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE books_sold CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE default_with_function_table CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE DimAccount CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE date_only_table CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE users CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE user_profiles CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE default_books CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;


    -- Drop tables in foo and bar schemas
   begin
      execute immediate q'[DROP TABLE foo.magazines CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP TABLE bar.magazines CASCADE CONSTRAINTS]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;

    -- Drop users (schemas) foo and bar
   begin
      execute immediate q'[DROP USER foo CASCADE]';
   exception
      when others then
         if sqlcode != -1918 then  -- ORA-01918: user 'FOO' does not exist
            dbms_output.put_line(SQLERRM);
         end if;
   end;
   begin
      execute immediate q'[DROP USER bar CASCADE]';
   exception
      when others then
         if sqlcode != -1918 then  -- ORA-01918: user 'BAR' does not exist
            dbms_output.put_line(SQLERRM);
         end if;
   end;


    -- Drop sequences
   begin
      execute immediate q'[DROP SEQUENCE publishers_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE publishers_mm_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE books_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE books_mm_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE players_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE clubs_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE book_website_placements_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE authors_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE authors_mm_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE reviews_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE reviews_mm_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE comics_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE type_table_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE series_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE sales_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE authors_history_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE bookmarks_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE mappedbookmarks_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE fte_data_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE default_with_function_table_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE dimaccount_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE users_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE user_profiles_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[DROP SEQUENCE default_books_seq]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   commit;


    -- Create sequences (starting at 5001 for consistency)

   execute immediate q'[CREATE SEQUENCE publishers_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE publishers_mm_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE books_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE books_mm_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE players_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE clubs_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE book_website_placements_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE authors_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE authors_mm_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE reviews_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE reviews_mm_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE comics_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE type_table_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE series_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE sales_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE authors_history_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE bookmarks_seq START WITH 1 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE mappedbookmarks_seq START WITH 1 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE fte_data_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE default_with_function_table_seq START WITH 5001 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE dimaccount_seq START WITH 1 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE users_seq START WITH 1 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE user_profiles_seq START WITH 1 INCREMENT BY 1]';
   execute immediate q'[CREATE SEQUENCE default_books_seq START WITH 5001 INCREMENT BY 1]';



    -- Create tables

   execute immediate q'[CREATE TABLE publishers(
        id NUMBER PRIMARY KEY,
        name VARCHAR2(4000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE publishers_mm(
        id NUMBER PRIMARY KEY,
        name VARCHAR2(4000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE books(
        id NUMBER PRIMARY KEY,
        title VARCHAR2(4000) NOT NULL,
        publisher_id NUMBER NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE books_mm(
        id NUMBER PRIMARY KEY,
        title VARCHAR2(4000) NOT NULL,
        publisher_id NUMBER NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE default_books(
        id NUMBER PRIMARY KEY,
        title VARCHAR2(100) DEFAULT 'Placeholder'
     ) ]';
   execute immediate q'[CREATE TABLE players(
        id NUMBER PRIMARY KEY,
        name VARCHAR2(4000) NOT NULL,
        current_club_id NUMBER NOT NULL,
        new_club_id NUMBER NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE clubs(
        id NUMBER PRIMARY KEY,
        name VARCHAR2(4000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE book_website_placements(
        id NUMBER PRIMARY KEY,
        book_id NUMBER UNIQUE NOT NULL,
        price NUMBER NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE website_users(
        id NUMBER PRIMARY KEY,
        username VARCHAR2(4000) NULL
     ) ]';
   execute immediate q'[CREATE TABLE website_users_mm(
        id NUMBER PRIMARY KEY,
        username VARCHAR2(4000) NULL
     ) ]';
   execute immediate q'[CREATE TABLE authors(
        id NUMBER PRIMARY KEY,
        name VARCHAR2(4000) NOT NULL,
        birthdate VARCHAR2(4000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE authors_mm(
        id NUMBER PRIMARY KEY,
        name VARCHAR2(4000) NOT NULL,
        birthdate VARCHAR2(4000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE reviews(
        book_id NUMBER,
        id NUMBER,
        content VARCHAR2(4000) DEFAULT 'Its a classic' NOT NULL,
        websiteuser_id NUMBER DEFAULT 1,
        PRIMARY KEY(book_id, id)
     ) ]';
   execute immediate q'[CREATE TABLE reviews_mm(
        book_id NUMBER,
        id NUMBER,
        content VARCHAR2(4000) DEFAULT 'Its a classic' NOT NULL,
        websiteuser_id NUMBER DEFAULT 1,
        PRIMARY KEY(book_id, id)
     ) ]';
   execute immediate q'[CREATE TABLE book_author_link(
        book_id NUMBER NOT NULL,
        author_id NUMBER NOT NULL,
        royalty_percentage BINARY_DOUBLE DEFAULT 0 NULL,
        PRIMARY KEY(book_id, author_id)
     ) ]';
   execute immediate q'[CREATE TABLE book_author_link_mm(
        book_id NUMBER NOT NULL,
        author_id NUMBER NOT NULL,
        royalty_percentage BINARY_DOUBLE DEFAULT 0 NULL,
        PRIMARY KEY(book_id, author_id)
     ) ]';


    -- Create schemas (users in Oracle) for multi-schema testing
    -- Note: In Oracle, schemas are tied to users. Creating separate users 'foo' and 'bar'
    -- to simulate SQL Server's schema concept.

   begin
      execute immediate q'[CREATE USER foo IDENTIFIED BY "TempPass123!" DEFAULT TABLESPACE USERS QUOTA UNLIMITED ON USERS]';
      execute immediate q'[GRANT CONNECT, RESOURCE TO foo]';
   exception
      when others then
         if sqlcode != -1920 then  -- ORA-01920: user name 'FOO' conflicts with another user or role name
            dbms_output.put_line(SQLERRM);
         end if;
   end;

   begin
      execute immediate q'[CREATE USER bar IDENTIFIED BY "TempPass123!" DEFAULT TABLESPACE USERS QUOTA UNLIMITED ON USERS]';
      execute immediate q'[GRANT CONNECT, RESOURCE TO bar]';
   exception
      when others then
         if sqlcode != -1920 then  -- ORA-01920: user name 'BAR' conflicts with another user or role name
            dbms_output.put_line(SQLERRM);
         end if;
   end;

    -- Create magazines table in foo schema
   begin
      execute immediate q'[CREATE TABLE foo.magazines(
        id NUMBER PRIMARY KEY,
        title VARCHAR2(4000) NOT NULL,
        issue_number NUMBER NULL
     ) ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;

    -- Create magazines table in bar schema  
   begin
      execute immediate q'[CREATE TABLE bar.magazines(
        upc NUMBER PRIMARY KEY,
        comic_name VARCHAR2(4000) NOT NULL,
        issue NUMBER NULL
     ) ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;

   execute immediate q'[CREATE TABLE comics(
        id NUMBER PRIMARY KEY,
        title VARCHAR2(4000) NOT NULL,
        volume NUMBER,
        categoryName VARCHAR2(100) NOT NULL UNIQUE,
        series_id NUMBER NULL
     ) ]';
   execute immediate q'[CREATE TABLE stocks(
        categoryid NUMBER NOT NULL,
        pieceid NUMBER NOT NULL,
        categoryName VARCHAR2(100) NOT NULL,
        piecesAvailable NUMBER DEFAULT 0,
        piecesRequired NUMBER DEFAULT 0 NOT NULL,
        PRIMARY KEY(categoryid, pieceid)
     ) ]';
   execute immediate q'[CREATE TABLE stocks_price(
        categoryid NUMBER NOT NULL,
        pieceid NUMBER NOT NULL,
        instant TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
        price BINARY_DOUBLE,
        is_wholesale_price NUMBER(1),
        PRIMARY KEY(categoryid, pieceid, instant)
     ) ]';
   execute immediate q'[CREATE TABLE brokers(
        "ID Number" NUMBER PRIMARY KEY,
        "First Name" VARCHAR2(4000) NOT NULL,
        "Last Name" VARCHAR2(4000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE type_table(
        id NUMBER PRIMARY KEY,
        byte_types NUMBER,
        short_types NUMBER,
        int_types NUMBER,
        long_types NUMBER,
        string_types VARCHAR2(4000),
        nvarchar_string_types VARCHAR2(2000),
        single_types BINARY_FLOAT,
        float_types BINARY_DOUBLE,
        decimal_types NUMBER,
        boolean_types NUMBER,
        date_types DATE,
        datetime_types TIMESTAMP,
        datetime2_types TIMESTAMP(7),
        datetimeoffset_types TIMESTAMP,
        smalldatetime_types TIMESTAMP,
        bytearray_types VARCHAR2(4000),
        uuid_types VARCHAR2(32)
     ) ]';
   execute immediate q'[CREATE TABLE trees (
        treeId NUMBER PRIMARY KEY,
        species VARCHAR2(4000),
        region VARCHAR2(4000),
        height VARCHAR2(4000)
     ) ]';
   execute immediate q'[CREATE TABLE fungi (
        speciesid NUMBER PRIMARY KEY,
        region VARCHAR2(4000),
        habitat VARCHAR2(6)
     ) ]';
   execute immediate q'[CREATE TABLE empty_table (
        id NUMBER PRIMARY KEY
     ) ]';
   execute immediate q'[CREATE TABLE notebooks (
        id NUMBER PRIMARY KEY,
        notebookname VARCHAR2(4000),
        color VARCHAR2(4000),
        ownername VARCHAR2(4000)
     ) ]';
   execute immediate q'[CREATE TABLE journals (
        id NUMBER PRIMARY KEY,
        journalname VARCHAR2(4000),
        color VARCHAR2(4000),
        ownername VARCHAR2(4000)
     ) ]';
   execute immediate q'[CREATE TABLE aow (
        NoteNum NUMBER PRIMARY KEY,
        DetailAssessmentAndPlanning CLOB,
        WagingWar CLOB,
        StrategicAttack CLOB
     ) ]';
   execute immediate q'[CREATE TABLE series (
        id NUMBER PRIMARY KEY,
        name NVARCHAR2(1000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE sales (
        id NUMBER PRIMARY KEY,
        item_name VARCHAR2(4000) NOT NULL,
        subtotal NUMBER(18,2) NOT NULL,
        tax NUMBER(18,2) NOT NULL,
        total NUMBER(18,2) GENERATED ALWAYS AS (subtotal + tax) VIRTUAL
     ) ]';
   execute immediate q'[CREATE TABLE authors_history (
        id NUMBER PRIMARY KEY,
        first_name VARCHAR2(100) NOT NULL,
        middle_name VARCHAR2(100),
        last_name VARCHAR2(100) NOT NULL,
        year_of_publish NUMBER,
        books_published NUMBER
     ) ]';
   execute immediate q'[CREATE TABLE revenues(
        id NUMBER PRIMARY KEY,
        category VARCHAR2(4000) NOT NULL,
        revenue NUMBER,
        accessible_role VARCHAR2(4000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE graphql_incompatible (
        "__typeName" NUMBER PRIMARY KEY,
        conformingName VARCHAR2(12)
     ) ]';
   execute immediate q'[CREATE TABLE GQLmappings (
        "__column1" NUMBER PRIMARY KEY,
        "__column2" VARCHAR2(4000),
        column3 VARCHAR2(4000)
     ) ]';
   execute immediate q'[CREATE TABLE bookmarks(
        id NUMBER PRIMARY KEY,
        bkname NVARCHAR2(1000) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE mappedbookmarks(
        id NUMBER PRIMARY KEY,
        bkname NVARCHAR2(50) NOT NULL
     ) ]';
   execute immediate q'[CREATE TABLE fte_data(
        id NUMBER,
        u_id NUMBER DEFAULT 2,
        name VARCHAR2(50),
        position VARCHAR2(20),
        salary NUMBER DEFAULT 20,
        PRIMARY KEY(id, u_id)
     ) ]';
   execute immediate q'[CREATE TABLE intern_data(
        id NUMBER,
        months NUMBER DEFAULT 2 NOT NULL,
        name VARCHAR2(50),
        salary NUMBER DEFAULT 15,
        PRIMARY KEY(id, months)
     ) ]';
   execute immediate q'[CREATE TABLE books_sold(
        id NUMBER PRIMARY KEY NOT NULL,
        book_name VARCHAR2(50),
        row_version RAW(8),
        copies_sold NUMBER DEFAULT 0,
        last_sold_on TIMESTAMP DEFAULT TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS')
     ) ]';
   execute immediate q'[CREATE TABLE default_with_function_table(
        id NUMBER PRIMARY KEY,
        user_value NUMBER,
        current_date TIMESTAMP DEFAULT SYSDATE NOT NULL,
        current_timestamp TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
        random_number NUMBER,
        next_date TIMESTAMP DEFAULT TRUNC(SYSDATE) + 1 NOT NULL,
        default_string_with_parenthesis VARCHAR2(100) DEFAULT '() ',
        default_function_string_with_parenthesis VARCHAR2(100) DEFAULT 'NOW() ',
        default_integer NUMBER DEFAULT 100,
        default_date_string TIMESTAMP DEFAULT TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS')
     ) ]';
   execute immediate q'[CREATE TABLE DimAccount (
        AccountKey NUMBER PRIMARY KEY,
        ParentAccountKey NUMBER NULL
     ) ]';
   execute immediate q'[CREATE TABLE users (
        userid NUMBER PRIMARY KEY,
        username NVARCHAR2(50) UNIQUE,
        email NVARCHAR2(100)
     ) ]';
   execute immediate q'[CREATE TABLE user_profiles (
        profileid NUMBER PRIMARY KEY,
        username NVARCHAR2(50) UNIQUE,
        profilepictureurl NVARCHAR2(255),
        userid NUMBER
     ) ]';
   execute immediate q'[CREATE TABLE date_only_table (
        event_date DATE NOT NULL,
        event_time INTERVAL DAY(0) TO SECOND(0) NOT NULL,
        event_timestamp TIMESTAMP NOT NULL
     ) ]'; 


    -- Add foreign key constraints

   execute immediate q'[ALTER TABLE books 
    ADD CONSTRAINT book_publisher_fk
    FOREIGN KEY (publisher_id)
    REFERENCES publishers (id ) ]';
   execute immediate q'[ALTER TABLE players 
    ADD CONSTRAINT player_club_fk
    FOREIGN KEY (current_club_id)
    REFERENCES clubs (id ) ]';
   execute immediate q'[ALTER TABLE book_website_placements 
    ADD CONSTRAINT book_website_placement_book_fk
    FOREIGN KEY (book_id)
    REFERENCES books (id ) ]';
   execute immediate q'[ALTER TABLE reviews 
    ADD CONSTRAINT review_book_fk
    FOREIGN KEY (book_id)
    REFERENCES books (id ) ]';
   execute immediate q'[ALTER TABLE book_author_link 
    ADD CONSTRAINT book_author_link_book_fk
    FOREIGN KEY (book_id)
    REFERENCES books (id ) ]';
   execute immediate q'[ALTER TABLE book_author_link 
    ADD CONSTRAINT book_author_link_author_fk
    FOREIGN KEY (author_id)
    REFERENCES authors (id ) ]';
   execute immediate q'[ALTER TABLE stocks 
    ADD CONSTRAINT stocks_comics_fk
    FOREIGN KEY (categoryName)
    REFERENCES comics (categoryName ) ]';
   execute immediate q'[ALTER TABLE stocks_price 
    ADD CONSTRAINT stocks_price_stocks_fk
    FOREIGN KEY (categoryid, pieceid)
    REFERENCES stocks (categoryid, pieceid ) ]';
   execute immediate q'[ALTER TABLE comics 
    ADD CONSTRAINT comics_series_fk
    FOREIGN KEY (series_id)
    REFERENCES series(id ) ]';
   execute immediate q'[ALTER TABLE DimAccount 
    ADD CONSTRAINT FK_DimAccount_DimAccount
    FOREIGN KEY (ParentAccountKey)
    REFERENCES DimAccount (AccountKey ) ]'; 




    -- Create triggers for auto-increment simulation
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER publishers_bir
    BEFORE INSERT ON publishers
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT publishers_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER publishers_mm_bir
    BEFORE INSERT ON publishers_mm
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT publishers_mm_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER books_bir
    BEFORE INSERT ON books
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT books_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER books_mm_bir
    BEFORE INSERT ON books_mm
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT books_mm_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER players_bir
    BEFORE INSERT ON players
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT players_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER clubs_bir
    BEFORE INSERT ON clubs
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT clubs_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER book_website_placements_bir
    BEFORE INSERT ON book_website_placements
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT book_website_placements_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER authors_bir
    BEFORE INSERT ON authors
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT authors_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER authors_mm_bir
    BEFORE INSERT ON authors_mm
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT authors_mm_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER reviews_bir
    BEFORE INSERT ON reviews
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT reviews_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER reviews_mm_bir
    BEFORE INSERT ON reviews_mm
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT reviews_mm_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER comics_bir
    BEFORE INSERT ON comics
    FOR EACH ROW
    BEGIN
        IF :new.volume IS NULL THEN
            SELECT comics_seq.NEXTVAL INTO :new.volume FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER type_table_bir
    BEFORE INSERT ON type_table
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT type_table_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER series_bir
    BEFORE INSERT ON series
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT series_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER sales_bir
    BEFORE INSERT ON sales
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT sales_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER authors_history_bir
    BEFORE INSERT ON authors_history
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT authors_history_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER bookmarks_bir
    BEFORE INSERT ON bookmarks
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT bookmarks_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER mappedbookmarks_bir
    BEFORE INSERT ON mappedbookmarks
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT mappedbookmarks_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER fte_data_bir
    BEFORE INSERT ON fte_data
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT fte_data_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER default_with_function_table_bir
    BEFORE INSERT ON default_with_function_table
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT default_with_function_table_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER dimaccount_bir
    BEFORE INSERT ON DimAccount
    FOR EACH ROW
    BEGIN
        IF :new.AccountKey IS NULL THEN
            SELECT dimaccount_seq.NEXTVAL INTO :new.AccountKey FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER users_bir
    BEFORE INSERT ON users
    FOR EACH ROW
    BEGIN
        IF :new.userid IS NULL THEN
            SELECT users_seq.NEXTVAL INTO :new.userid FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER user_profiles_bir
    BEFORE INSERT ON user_profiles
    FOR EACH ROW
    BEGIN
        IF :new.profileid IS NULL THEN
            SELECT user_profiles_seq.NEXTVAL INTO :new.profileid FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER default_books_bir
    BEFORE INSERT ON default_books
    FOR EACH ROW
    BEGIN
        IF :new.id IS NULL THEN
            SELECT default_books_seq.NEXTVAL INTO :new.id FROM dual ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;


    -- Insert data

   execute immediate q'[INSERT INTO publishers(id, name) VALUES (1234, 'Big Company')]';
   execute immediate q'[INSERT INTO publishers(id, name) VALUES (2345, 'Small Town Publisher')]';
   execute immediate q'[INSERT INTO publishers(id, name) VALUES (2323, 'TBD Publishing One')]';
   execute immediate q'[INSERT INTO publishers(id, name) VALUES (2324, 'TBD Publishing Two Ltd')]';
   execute immediate q'[INSERT INTO publishers(id, name) VALUES (1940, 'Policy Publisher 01')]';
   execute immediate q'[INSERT INTO publishers(id, name) VALUES (1941, 'Policy Publisher 02')]';
   execute immediate q'[INSERT INTO publishers(id, name) VALUES (1156, 'The First Publisher')]';
   execute immediate q'[INSERT INTO publishers_mm(id, name) VALUES (1234, 'Big Company')]';
   execute immediate q'[INSERT INTO publishers_mm(id, name) VALUES (2345, 'Small Town Publisher')]';
   execute immediate q'[INSERT INTO publishers_mm(id, name) VALUES (2323, 'TBD Publishing One')]';
   execute immediate q'[INSERT INTO publishers_mm(id, name) VALUES (2324, 'TBD Publishing Two Ltd')]';
   execute immediate q'[INSERT INTO publishers_mm(id, name) VALUES (1940, 'Policy Publisher 01')]';
   execute immediate q'[INSERT INTO publishers_mm(id, name) VALUES (1941, 'Policy Publisher 02')]';
   execute immediate q'[INSERT INTO publishers_mm(id, name) VALUES (1156, 'The First Publisher')]';
   execute immediate q'[INSERT INTO clubs(id, name) VALUES (1111, 'Manchester United')]';
   execute immediate q'[INSERT INTO clubs(id, name) VALUES (1112, 'FC Barcelona')]';
   execute immediate q'[INSERT INTO clubs(id, name) VALUES (1113, 'Real Madrid')]';
   execute immediate q'[INSERT INTO authors(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01')]';
   execute immediate q'[INSERT INTO authors(id, name, birthdate) VALUES (124, 'Aniruddh', '2002-02-02')]';
   execute immediate q'[INSERT INTO authors(id, name, birthdate) VALUES (125, 'Aniruddh', '2001-01-01')]';
   execute immediate q'[INSERT INTO authors(id, name, birthdate) VALUES (126, 'Aaron', '2001-01-01')]';
   execute immediate q'[INSERT INTO authors_mm(id, name, birthdate) VALUES (123, 'Jelte', '2001-01-01')]';
   execute immediate q'[INSERT INTO authors_mm(id, name, birthdate) VALUES (124, 'Aniruddh', '2002-02-02')]';
   execute immediate q'[INSERT INTO authors_mm(id, name, birthdate) VALUES (125, 'Aniruddh', '2001-01-01')]';
   execute immediate q'[INSERT INTO authors_mm(id, name, birthdate) VALUES (126, 'Aaron', '2001-01-01')]';
   execute immediate q'[INSERT INTO GQLmappings("__column1", "__column2", column3) VALUES (1, 'Incompatible GraphQL Name', 'Compatible GraphQL Name')]'
   ;
   execute immediate q'[INSERT INTO GQLmappings("__column1", "__column2", column3) VALUES (3, 'Old Value', 'Record to be Updated')]'
   ;
   execute immediate q'[INSERT INTO GQLmappings("__column1", "__column2", column3) VALUES (4, 'Lost Record', 'Record to be Deleted')]'
   ;
   execute immediate q'[INSERT INTO GQLmappings("__column1", "__column2", column3) VALUES (5, 'Filtered Record', 'Record to be Filtered on Find')]'
   ;
   commit;


    -- Insert bookmarks (1 to 10000)
   execute immediate q'[DECLARE
      v_counter number := 1 ;
      BEGIN
      WHILE v_counter <= 10000 LOOP
         insert into bookmarks (
            id,
            bkname
         ) values ( v_counter,
                    'Test Item #'
                    || lpad(
                       v_counter,
                       5,
                       '0'
                    ) ) ;



         v_counter := v_counter + 1 ;
      END LOOP ;


      COMMIT ;
   END ; ]';

    -- Insert mappedbookmarks (1 to 10000)
   execute immediate q'[DECLARE
      v_counter number := 1 ;
      BEGIN
      WHILE v_counter <= 10000 LOOP
         insert into mappedbookmarks (
            id,
            bkname
         ) values ( v_counter,
                    'Test Item #'
                    || lpad(
                       v_counter,
                       5,
                       '0'
                    ) ) ;


         v_counter := v_counter + 1 ;
      END LOOP ;
      COMMIT ;
   END ; ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 1,
              'Awesome book',
              1234  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 2,
              'Also Awesome book',
              1234  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 3,
              'Great wall of china explained',
              2345  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 4,
              'US history in a nutshell',
              2345  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 5,
              'Chernobyl Diaries',
              2323  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 6,
              'The Palace Door',
              2324  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 7,
              'The Groovy Bar',
              2324  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 8,
              'Time to Eat',
              2324  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 9,
              'Policy-Test-01',
              1940  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 10,
              'Policy-Test-02',
              1940  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 11,
              'Policy-Test-04',
              1941  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 12,
              'Time to Eat 2',
              1941  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 13,
              'Before Sunrise',
              1234  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 14,
              'Before Sunset',
              1234  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 15,
              'SQL_CONN',
              1234  ) ]';
   execute immediate q'[insert into books (
      id,
      title,
      publisher_id
   ) values ( 16,
              'SOME%CONN',
              1234  ) ]';
   execute immediate q'[
   insert into books (
      id,
      title,
      publisher_id
   ) values ( 17,
              'CONN%_CONN',
              1234  ) ]';
   execute immediate q'[
   insert into books (
      id,
      title,
      publisher_id
   ) values ( 18,
              'Special Book',
              1234  ) ]';
   execute immediate q'[
   insert into books (
      id,
      title,
      publisher_id
   ) values ( 19,
              'ME\YOU',
              1234  ) ]';
   execute immediate q'[
   insert into books (
      id,
      title,
      publisher_id
   ) values ( 20,
              'C:\LIFE',
              1234  ) ]';
   execute immediate q'[
   insert into books (
      id,
      title,
      publisher_id
   ) values ( 21,
              ',',
              1234 )
]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 1,
              'Awesome book',
              1234  ) ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 2,
              'Also Awesome book',
              1234  ) ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 3,
              'Great wall of china explained',
              2345 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 4,
              'US history in a nutshell',
              2345 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 5,
              'Chernobyl Diaries',
              2323 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 6,
              'The Palace Door',
              2324 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 7,
              'The Groovy Bar',
              2324 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 8,
              'Time to Eat',
              2324 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 9,
              'Policy-Test-01',
              1940 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 10,
              'Policy-Test-02',
              1940 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 11,
              'Policy-Test-04',
              1941 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 12,
              'Time to Eat 2',
              1941 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 13,
              'Before Sunrise',
              1234 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 14,
              'Before Sunset',
              1234 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 15,
              'SQL_CONN',
              1234 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 16,
              'SOME%CONN',
              1234 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 17,
              'CONN%_CONN',
              1234 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 18,
              'Special Book',
              1234 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 19,
              'ME\YOU',
              1234 )
  ]';
   execute immediate q'[
   insert into books_mm (
      id,
      title,
      publisher_id
   ) values ( 20,
              'C:\LIFE',
              1234 )
  ]';
   execute immediate q'[
   insert into players (
      id,
      name,
      current_club_id,
      new_club_id
   ) values ( 1,
              'Cristiano Ronaldo',
              1113,
              1111 )
  ]';
   execute immediate q'[
   insert into players (
      id,
      name,
      current_club_id,
      new_club_id
   ) values ( 2,
              'Leonel Messi',
              1112,
              1113 )
  ]';
   execute immediate q'[
   insert into book_website_placements (
      id,
      book_id,
      price
   ) values ( 1,
              1,
              100 )
  ]';
   execute immediate q'[
   insert into book_website_placements (
      id,
      book_id,
      price
   ) values ( 2,
              2,
              50 )
  ]';
   execute immediate q'[
   insert into book_website_placements (
      id,
      book_id,
      price
   ) values ( 3,
              3,
              23 )
  ]';
   execute immediate q'[
   insert into book_website_placements (
      id,
      book_id,
      price
   ) values ( 4,
              5,
              33 )
  ]';
   execute immediate q'[
   insert into book_author_link (
      book_id,
      author_id
   ) values ( 1,
              123 )
  ]';
   execute immediate q'[
   insert into book_author_link (
      book_id,
      author_id
   ) values ( 2,
              124 )
  ]';
   execute immediate q'[
   insert into book_author_link (
      book_id,
      author_id
   ) values ( 3,
              123 )
  ]';
   execute immediate q'[
   insert into book_author_link (
      book_id,
      author_id
   ) values ( 3,
              124 )
  ]';
   execute immediate q'[
   insert into book_author_link (
      book_id,
      author_id
   ) values ( 4,
              123 )
  ]';
   execute immediate q'[
   insert into book_author_link (
      book_id,
      author_id
   ) values ( 4,
              124 )
  ]';
   execute immediate q'[
   insert into book_author_link (
      book_id,
      author_id
   ) values ( 5,
              126 )
  ]';
   execute immediate q'[
   insert into book_author_link_mm (
      book_id,
      author_id
   ) values ( 1,
              123 )
  ]';
   execute immediate q'[
   insert into book_author_link_mm (
      book_id,
      author_id
   ) values ( 2,
              124 )
  ]';
   execute immediate q'[
   insert into book_author_link_mm (
      book_id,
      author_id
   ) values ( 3,
              123 )
  ]';
   execute immediate q'[
   insert into book_author_link_mm (
      book_id,
      author_id
   ) values ( 3,
              124 )
  ]';
   execute immediate q'[
   insert into book_author_link_mm (
      book_id,
      author_id
   ) values ( 4,
              123 )
  ]';
   execute immediate q'[
   insert into book_author_link_mm (
      book_id,
      author_id
   ) values ( 4,
              124 )
  ]';
   execute immediate q'[
   insert into book_author_link_mm (
      book_id,
      author_id
   ) values ( 5,
              126 )
  ]';
   execute immediate q'[
   insert into website_users (
      id,
      username
   ) values ( 1,
              'George' )
  ]';
   execute immediate q'[
   insert into website_users (
      id,
      username
   ) values ( 2,
              null )
  ]';
   execute immediate q'[
   insert into website_users (
      id,
      username
   ) values ( 3,
              '' )
  ]';
   execute immediate q'[
   insert into website_users (
      id,
      username
   ) values ( 4,
              'book_lover_95' )
  ]';
   execute immediate q'[
   insert into website_users (
      id,
      username
   ) values ( 5,
              'null' )
  ]';
   execute immediate q'[
   insert into website_users_mm (
      id,
      username
   ) values ( 1,
              'George' )
  ]';
   execute immediate q'[
   insert into website_users_mm (
      id,
      username
   ) values ( 2,
              null )
  ]';
   execute immediate q'[
   insert into website_users_mm (
      id,
      username
   ) values ( 3,
              '' )
  ]';
   execute immediate q'[
   insert into website_users_mm (
      id,
      username
   ) values ( 4,
              'book_lover_95' )
  ]';
   execute immediate q'[
   insert into website_users_mm (
      id,
      username
   ) values ( 5,
              'null' )
  ]';
   execute immediate q'[
   insert into reviews (
      id,
      book_id,
      content
   ) values ( 567,
              1,
              'Indeed a great book' )
  ]';
   execute immediate q'[
   insert into reviews (
      id,
      book_id,
      content
   ) values ( 568,
              1,
              'I loved it' )
  ]';
   execute immediate q'[
   insert into reviews (
      id,
      book_id,
      content
   ) values ( 569,
              1,
              'best book I read in years' )
  ]';
   execute immediate q'[
   insert into reviews_mm (
      id,
      book_id,
      content
   ) values ( 567,
              1,
              'Indeed a great book' )
  ]';
   execute immediate q'[
   insert into reviews_mm (
      id,
      book_id,
      content
   ) values ( 568,
              1,
              'I loved it' )
  ]';
   execute immediate q'[
   insert into reviews_mm (
      id,
      book_id,
      content
   ) values ( 569,
              1,
              'best book I read in years' )
  ]';
   execute immediate q'[INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
        single_types, float_types, decimal_types, boolean_types,
        date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
        bytearray_types)
    VALUES (1, 1, 1, 1, 1, '', '',
        0.33, 0.33, 0.333333, 1,
        TO_DATE('1999-01-08', 'YYYY-MM-DD'), TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS'),
        TO_TIMESTAMP('1999-01-08 10:23:54.9999999', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('1999-01-08 10:23:54.9999999', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('1999-01-08 10:23:54', 'YYYY-MM-DD HH24:MI:SS'),
        'ABCDEF0123' ) ]';
   execute immediate q'[INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
        single_types, float_types, decimal_types, boolean_types,
        date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
        bytearray_types)
    VALUES (2, 0, -1, -1, -1, 'lksa;jdflasdf;alsdflksdfkldj', 'lksa;jdflasdf;alsdflksdfkldj',
        -9.2, -9.2, -9.292929, 0,
        TO_DATE('1999-01-08', 'YYYY-MM-DD'), TO_TIMESTAMP('1999-01-08 10:23:00', 'YYYY-MM-DD HH24:MI:SS'),
        TO_TIMESTAMP('1999-01-08 10:23:00.9999999', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('1999-01-08 10:23:00.9999999', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('1999-01-08 10:23:00', 'YYYY-MM-DD HH24:MI:SS'),
        HEXTORAW('98AB7511AABB1234') ) ]';
   execute immediate q'[INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
        single_types, float_types, decimal_types, boolean_types,
        date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
        bytearray_types)
    VALUES (3, 0, -32768, -2147483648, -9223372036854775808, 'null', 'null',
        -3.4E30, -1.7E100, 0.000000001, 1,
        TO_DATE('1900-01-01', 'YYYY-MM-DD'), TO_TIMESTAMP('1900-01-01 00:00:00.000', 'YYYY-MM-DD HH24:MI:SS.FF3'),
        TO_TIMESTAMP('1900-01-01 00:00:00.0000000', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('1900-01-01 00:00:00.0000000', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('1900-01-01 00:00:00', 'YYYY-MM-DD HH24:MI:SS'),
        HEXTORAW('00000000') ) ]';
   execute immediate q'[INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
        single_types, float_types, decimal_types, boolean_types,
        date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
        bytearray_types)
    VALUES (4, 255, 32767, 2147483647, 9223372036854775807, 'null', 'null',
        3.4E30, 1.7E100, 99999999.999999, 1,
        TO_DATE('9999-12-31', 'YYYY-MM-DD'), TO_TIMESTAMP('9999-12-31 23:59:59', 'YYYY-MM-DD HH24:MI:SS'),
        TO_TIMESTAMP('9999-12-31 23:59:59.9999990', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('9999-12-31 23:59:59.9999990', 'YYYY-MM-DD HH24:MI:SS.FF7'),
        TO_TIMESTAMP('2079-06-06 00:00:00', 'YYYY-MM-DD HH24:MI:SS'),
        HEXTORAW('FFFFFFFF') ) ]';
   execute immediate q'[INSERT INTO type_table(id, byte_types, short_types, int_types, long_types, string_types, nvarchar_string_types,
        single_types, float_types, decimal_types, boolean_types,
        date_types, datetime_types, datetime2_types, datetimeoffset_types, smalldatetime_types,
        bytearray_types)
    VALUES (5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL ) ]';
   execute immediate q'[INSERT INTO type_table(id, uuid_types) VALUES (10, 'D1D021A847B44AE4B71898E89C41A161')]';
   execute immediate q'[INSERT INTO sales(id, item_name, subtotal, tax) VALUES (1, 'Watch', 249.00, 20.59)]';
   execute immediate q'[INSERT INTO sales(id, item_name, subtotal, tax) VALUES (2, 'Montior', 120.50, 11.12)]';
   execute immediate q'[INSERT INTO notebooks(id, notebookname, color, ownername) VALUES (1, 'Notebook1', 'red', 'Sean')]';
   
   
execute immediate q'[
    insert into notebooks (
      id,
      notebookname,
      color,
      ownername
   ) values ( 2,
              'Notebook2',
              'green',
              'Ani' )
  ]';
   execute immediate q'[
   insert into notebooks (
      id,
      notebookname,
      color,
      ownername
   ) values ( 3,
              'Notebook3',
              'blue',
              'Jarupat' )
  ]';
   execute immediate q'[
   insert into notebooks (
      id,
      notebookname,
      color,
      ownername
   ) values ( 4,
              'Notebook4',
              'yellow',
              'Aaron' )
  ]';
   execute immediate q'[
   insert into journals (
      id,
      journalname,
      color,
      ownername
   ) values ( 1,
              'Journal1',
              'red',
              'Sean' )
  ]';
   execute immediate q'[
   insert into journals (
      id,
      journalname,
      color,
      ownername
   ) values ( 2,
              'Journal2',
              'green',
              'Ani' )
  ]';
   execute immediate q'[
   insert into journals (
      id,
      journalname,
      color,
      ownername
   ) values ( 3,
              'Journal3',
              'blue',
              'Jarupat' )
  ]';
   execute immediate q'[
   insert into journals (
      id,
      journalname,
      color,
      ownername
   ) values ( 4,
              'Journal4',
              'yellow',
              'Aaron' )
  ]';
   execute immediate q'[
   insert into journals (
      id,
      journalname,
      color,
      ownername
   ) values ( 5,
              'Journal5',
              null,
              'Abhishek' )
  ]';
   execute immediate q'[
   insert into journals (
      id,
      journalname,
      color,
      ownername
   ) values ( 6,
              'Journal6',
              'green',
              null )
  ]';
   execute immediate q'[
   insert into journals (
      id,
      journalname,
      color,
      ownername
   ) values ( 7,
              'Journal7',
              null,
              null )
  ]';
   execute immediate q'[
   insert into foo.magazines (
      id,
      title,
      issue_number
   ) values ( 1,
              'Vogue',
              1234 )
  ]';
   execute immediate q'[
   insert into foo.magazines (
      id,
      title,
      issue_number
   ) values ( 11,
              'Sports Illustrated',
              null )
  ]';
   execute immediate q'[
   insert into foo.magazines (
      id,
      title,
      issue_number
   ) values ( 3,
              'Fitness',
              null )
  ]';execute immediate q'[
   insert into bar.magazines (
      upc,
      comic_name,
      issue
   ) values ( 0,
              'Not Vogue',
              null )
  ]';
   execute immediate q'[
   insert into brokers (
      "ID Number",
      "First Name",
      "Last Name"
   ) values ( 1,
              'Michael',
              'Burry' )
  ]';
   execute immediate q'[
   insert into brokers (
      "ID Number",
      "First Name",
      "Last Name"
   ) values ( 2,
              'Jordan',
              'Belfort' )
  ]';
   execute immediate q'[
   insert into series (
      id,
      name
   ) values ( 3001,
              'Foundation' )
  ]';
   execute immediate q'[
   insert into series (
      id,
      name
   ) values ( 3002,
              'Hyperion Cantos' )
  ]';
   execute immediate q'[
   insert into comics (
      id,
      title,
      categoryname,
      series_id
   ) values ( 1,
              'Star Trek',
              'SciFi',
              null )
  ]';
   execute immediate q'[
   insert into comics (
      id,
      title,
      categoryname,
      series_id
   ) values ( 2,
              'Cinderella',
              'Tales',
              3001 )
  ]';
   execute immediate q'[
   insert into comics (
      id,
      title,
      categoryname,
      series_id
   ) values ( 3,
              'Únknown',
              'Mystery',
              3002 )
  ]';
   execute immediate q'[
   insert into comics (
      id,
      title,
      categoryname,
      series_id
   ) values ( 4,
              'Alexander the Great',
              'Historical',
              null )
  ]';
   execute immediate q'[
   insert into comics (
      id,
      title,
      categoryname,
      series_id
   ) values ( 5,
              'Snow White',
              'AnotherTales',
              3001 )
  ]';
   execute immediate q'[
   insert into stocks (
      categoryid,
      pieceid,
      categoryname
   ) values ( 1,
              1,
              'SciFi' )
  ]';
   execute immediate q'[
   insert into stocks (
      categoryid,
      pieceid,
      categoryname
   ) values ( 2,
              1,
              'Tales' )
  ]';
   execute immediate q'[
   insert into stocks (
      categoryid,
      pieceid,
      categoryname
   ) values ( 0,
              1,
              'SciFi' )
  ]';
   execute immediate q'[
   insert into stocks (
      categoryid,
      pieceid,
      categoryname
   ) values ( 100,
              99,
              'Historical' )
  ]';
   execute immediate q'[
   insert into stocks_price (
      categoryid,
      pieceid,
      price,
      is_wholesale_price
   ) values ( 2,
              1,
              100.57,
              1 )
  ]';
   execute immediate q'[
   insert into stocks_price (
      categoryid,
      pieceid,
      price,
      is_wholesale_price
   ) values ( 1,
              1,
              42.75,
              0 )
  ]';
   execute immediate q'[
   insert into stocks_price (
      categoryid,
      pieceid,
      price,
      is_wholesale_price
   ) values ( 100,
              99,
              null,
              null )
  ]';
   execute immediate q'[
   insert into stocks_price (
      categoryid,
      pieceid,
      instant,
      price,
      is_wholesale_price
   ) values ( 2,
              1,
              to_timestamp('2023-08-21 15:11:04',
                           'YYYY-MM-DD HH24:MI:SS'),
              100.57,
              1 )
  ]';
   execute immediate q'[
   insert into trees (
      treeid,
      species,
      region,
      height
   ) values ( 1,
              'Tsuga terophylla',
              'Pacific Northwest',
              '30m' )
  ]';
   execute immediate q'[
   insert into trees (
      treeid,
      species,
      region,
      height
   ) values ( 2,
              'Pseudotsuga menziesii',
              'Pacific Northwest',
              '40m' )
  ]';
   execute immediate q'[
   insert into trees (
      treeid,
      species,
      region,
      height
   ) values ( 4,
              'test',
              'Pacific Northwest',
              '0m' )
  ]';
   execute immediate q'[
   insert into aow (
      notenum,
      detailassessmentandplanning,
      wagingwar,
      strategicattack
   ) values ( 1,
              'chapter one notes: ',
              'chapter two notes: ',
              'chapter three notes: ' )
  ]';
   execute immediate q'[
   insert into fungi (
      speciesid,
      region,
      habitat
   ) values ( 1,
              'northeast',
              'forest' )
  ]';
   execute immediate q'[
   insert into fungi (
      speciesid,
      region,
      habitat
   ) values ( 2,
              'southwest',
              'sand' )
  ]';
   execute immediate q'[
   insert into fungi (
      speciesid,
      region,
      habitat
   ) values ( 3,
              'northeast',
              'test' )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 1,
              'Isaac',
              null,
              'Asimov',
              1993,
              6 )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 2,
              'Robert',
              'A.',
              'Heinlein',
              1886,
              null )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 3,
              'Robert',
              null,
              'Silvenberg',
              null,
              null )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 4,
              'Dan',
              null,
              'Simmons',
              1759,
              3 )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 5,
              'Isaac',
              null,
              'Asimov',
              2000,
              null )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 6,
              'Robert',
              'A.',
              'Heinlein',
              1899,
              2 )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 7,
              'Isaac',
              null,
              'Silvenberg',
              1664,
              null )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 8,
              'Dan',
              null,
              'Simmons',
              1799,
              3 )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 9,
              'Aaron',
              null,
              'Mitchells',
              2001,
              1 )
  ]';
   execute immediate q'[
   insert into authors_history (
      id,
      first_name,
      middle_name,
      last_name,
      year_of_publish,
      books_published
   ) values ( 10,
              'Aaron',
              'F.',
              'Burtle',
              null,
              null )
  ]';
   execute immediate q'[
   insert into fte_data (
      id,
      name,
      position,
      salary
   ) values ( 1,
              'Ellie',
              'Junior Dev',
              20 )
  ]';
   execute immediate q'[
   insert into fte_data (
      id,
      name,
      position,
      salary
   ) values ( 2,
              'Chris',
              'Senior Dev',
              40 )
  ]';
   execute immediate q'[
   insert into intern_data (
      id,
      months,
      name
   ) values ( 1,
              3,
              'Tess' )
  ]';
   execute immediate q'[
   insert into intern_data (
      id,
      months,
      name
   ) values ( 2,
              4,
              'Frank' )
  ]';
   execute immediate q'[
   insert into revenues (
      id,
      category,
      revenue,
      accessible_role
   ) values ( 1,
              'Book',
              5000,
              'Anonymous' )
  ]';
   execute immediate q'[
   insert into revenues (
      id,
      category,
      revenue,
      accessible_role
   ) values ( 2,
              'Comics',
              10000,
              'Anonymous' )
  ]';
   execute immediate q'[
   insert into revenues (
      id,
      category,
      revenue,
      accessible_role
   ) values ( 3,
              'Journals',
              20000,
              'Authenticated' )
  ]';
   execute immediate q'[
   insert into revenues (
      id,
      category,
      revenue,
      accessible_role
   ) values ( 4,
              'Series',
              40000,
              'Authenticated' )
  ]';
   execute immediate q'[
   insert into books_sold (
      id,
      book_name,
      last_sold_on
   ) values ( 1,
              'Awesome Book',
              systimestamp )
  ]';
   execute immediate q'[
   insert into users (
      username,
      email
   ) values ( 'john_doe',
              'john.doe@example.com' )
  ]';
   execute immediate q'[
   insert into users (
      username,
      email
   ) values ( 'jane_smith',
              'jane.smith@example.com' )
  ]';
   execute immediate q'[
   insert into user_profiles (
      username,
      profilepictureurl,
      userid
   ) values ( 'john_doe',
              'https://example.com/profiles/john_doe.jpg',
              1 )
  ]';
   execute immediate q'[
   insert into user_profiles (
      username,
      profilepictureurl,
      userid
   ) values ( 'jane_smith',
              'https://example.com/profiles/jane_smith.jpg',
              2 )
  ]';
   execute immediate q'[
   insert into dimaccount (
      accountkey,
      parentaccountkey
   ) values ( 1,
              null )
  ]';
   execute immediate q'[
   insert into dimaccount (
      accountkey,
      parentaccountkey
   ) values ( 2,
              1 )
  ]';
   execute immediate q'[
   insert into dimaccount (
      accountkey,
      parentaccountkey
   ) values ( 3,
              2 )
  ]';
   execute immediate q'[
   insert into dimaccount (
      accountkey,
      parentaccountkey
   ) values ( 4,
              2 )
  ]';
   execute immediate q'[
   insert into date_only_table (
      event_date,
      event_time,
      event_timestamp
   ) values ( to_date('2023-01-01','YYYY-MM-DD'),
              interval '08:30:00' hour to second,
              to_timestamp('2023-01-01 08:30:00',
                           'YYYY-MM-DD HH24:MI:SS') )
  ]';
   execute immediate q'[
   insert into date_only_table (
      event_date,
      event_time,
      event_timestamp
   ) values ( to_date('2023-02-15','YYYY-MM-DD'),
              interval '12:45:00' hour to second,
              to_timestamp('2023-02-15 12:45:00',
                           'YYYY-MM-DD HH24:MI:SS') )
  ]';
   execute immediate q'[
   insert into date_only_table (
      event_date,
      event_time,
      event_timestamp
   ) values ( to_date('2023-03-30','YYYY-MM-DD'),
              interval '17:15:00' hour to second,
              to_timestamp('2023-03-30 17:15:00',
                           'YYYY-MM-DD HH24:MI:SS') )
]';
   commit;

    -- Create views

   execute immediate q'[CREATE VIEW books_view_all AS SELECT * FROM books]';
   execute immediate q'[CREATE VIEW books_view_with_mapping AS SELECT * FROM books]';
   execute immediate q'[CREATE VIEW stocks_view_selected AS
        SELECT categoryid, pieceid, categoryName, piecesAvailable
        FROM stocks  ]';
   execute immediate q'[CREATE VIEW books_publishers_view_composite AS
        SELECT publishers.name, books.id, books.title, publishers.id as pub_id
        FROM books, publishers
        WHERE publishers.id = books.publisher_id  ]';
   execute immediate q'[CREATE VIEW books_publishers_view_composite_insertable AS
        SELECT books.id, books.title, publishers.name, books.publisher_id
        FROM books, publishers
        WHERE publishers.id = books.publisher_id]';


    -- Create stored procedures

   execute immediate q'[CREATE OR REPLACE PROCEDURE get_book_by_id(id IN NUMBER, cursor OUT SYS_REFCURSOR) AS
    BEGIN
        OPEN cursor FOR
        SELECT * FROM books WHERE id = id ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE get_publisher_by_id(id IN NUMBER, cursor OUT SYS_REFCURSOR) AS
    BEGIN
        OPEN cursor FOR
        SELECT * FROM publishers WHERE id = id ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE get_books(cursor OUT SYS_REFCURSOR) AS
    BEGIN
        OPEN cursor FOR
        SELECT * FROM books ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE insert_book(title IN VARCHAR2, publisher_id IN NUMBER) AS
    BEGIN
        INSERT INTO books(title, publisher_id) VALUES (title, publisher_id) ;
        COMMIT ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE count_books(cursor OUT SYS_REFCURSOR) AS
    BEGIN
        OPEN cursor FOR
        SELECT COUNT(*) AS total_books FROM books ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE delete_last_inserted_book AS
        v_max_id NUMBER ;
    BEGIN
        SELECT MAX(id) INTO v_max_id FROM books ;
        DELETE FROM books WHERE id = v_max_id ;
        COMMIT ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE update_book_title(id IN NUMBER, title IN VARCHAR2, cursor OUT SYS_REFCURSOR) AS
    BEGIN
        UPDATE books SET title = title WHERE id = id ;
        OPEN cursor FOR
        SELECT * FROM books WHERE id = id ;
        COMMIT ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE get_authors_history_by_first_name(firstName IN VARCHAR2, cursor OUT SYS_REFCURSOR) AS
    BEGIN
        OPEN cursor FOR
        SELECT
            first_name || ' ' || NVL(middle_name || ' ', '') || last_name as author_name,
            MIN(year_of_publish) as first_publish_year,
            SUM(books_published) as total_books_published
        FROM authors_history
        WHERE first_name = firstName
        GROUP BY first_name || ' ' || NVL(middle_name || ' ', '') || last_name ;
    END ; ]';
   execute immediate q'[CREATE OR REPLACE PROCEDURE insert_and_display_all_books_for_given_publisher(
        title IN VARCHAR2,
        publisher_name IN VARCHAR2,
        cursor OUT SYS_REFCURSOR
    ) AS
        v_publisher_id NUMBER ;
    BEGIN
        SELECT id INTO v_publisher_id FROM publishers WHERE name = publisher_name ;
        INSERT INTO books(title, publisher_id) VALUES (title, v_publisher_id) ;
        OPEN cursor FOR
        SELECT * FROM books WHERE publisher_id = v_publisher_id ;
        COMMIT ;
    END ; ]';


    -- Create triggers for salary constraints (similar to SQL Server triggers)

   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER fte_data_after_insert_trigger
    AFTER INSERT ON fte_data
    FOR EACH ROW
    BEGIN
        IF :new.salary > 100 THEN
            UPDATE fte_data SET salary = 100 WHERE id = :new.id AND u_id = :new.u_id ;
        ELSIF :new.salary < 0 THEN
            UPDATE fte_data SET salary = 0 WHERE id = :new.id AND u_id = :new.u_id ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER fte_data_after_update_trigger
    AFTER UPDATE ON fte_data
    FOR EACH ROW
    BEGIN
        IF :new.salary > 150 THEN
            UPDATE fte_data SET salary = 150 WHERE id = :new.id AND u_id = :new.u_id ;
        ELSIF :new.salary < 0 THEN
            UPDATE fte_data SET salary = 0 WHERE id = :new.id AND u_id = :new.u_id ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER intern_data_after_insert_trigger
    AFTER INSERT ON intern_data
    FOR EACH ROW
    BEGIN
        IF :new.salary > 30 THEN
            UPDATE intern_data SET salary = 30 WHERE id = :new.id AND months = :new.months ;
        ELSIF :new.salary < 0 THEN
            UPDATE intern_data SET salary = 0 WHERE id = :new.id AND months = :new.months ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
   begin
      execute immediate q'[CREATE OR REPLACE TRIGGER intern_data_after_update_trigger
    AFTER UPDATE ON intern_data
    FOR EACH ROW
    BEGIN
        IF :new.salary > 50 THEN
            UPDATE intern_data SET salary = 50 WHERE id = :new.id AND months = :new.months ;
        ELSIF :new.salary < 0 THEN
            UPDATE intern_data SET salary = 0 WHERE id = :new.id AND months = :new.months ;
        END IF ;
    END ; ]';
   exception
      when others then
         dbms_output.put_line(SQLERRM);
   end;
end;