-- Copyright (c) Microsoft Corporation.
-- Licensed under the MIT License.
--
-- Oracle session-context prerequisite for DAB's "set-session-context" data-source option.
--
-- Oracle only permits an application context to be set from the PL/SQL package associated with its
-- namespace (CREATE CONTEXT ... USING <package>), so this script creates:
--   1. the DAB_SESSION_CONTEXT namespace, and
--   2. the DAB_SESSION_CONTEXT_PKG package exposing CLEAR_CLAIMS / SET_CLAIM.
--
-- Run once as a DBA (requires CREATE ANY CONTEXT), then grant EXECUTE on the package to the DAB
-- login. DAB clears the namespace and rebinds every claim before each statement, so values never
-- leak between requests on a pooled connection. Multi-valued claims are forwarded as JSON array
-- strings, which database-side policies can expand, for example:
--
--   SELECT ... WHERE region IN (
--       SELECT jt.value
--       FROM JSON_TABLE(SYS_CONTEXT('DAB_SESSION_CONTEXT', 'regions'), '$[*]'
--            COLUMNS (value VARCHAR2(4000) PATH '$')) jt);
--
-- Inspect a forwarded value with:
--   SELECT SYS_CONTEXT('DAB_SESSION_CONTEXT', 'roles') FROM DUAL;

CREATE OR REPLACE CONTEXT DAB_SESSION_CONTEXT USING DAB_SESSION_CONTEXT_PKG;

CREATE OR REPLACE PACKAGE DAB_SESSION_CONTEXT_PKG AS
    PROCEDURE CLEAR_CLAIMS;
    PROCEDURE SET_CLAIM(p_name IN VARCHAR2, p_value IN VARCHAR2);
END DAB_SESSION_CONTEXT_PKG;
/

CREATE OR REPLACE PACKAGE BODY DAB_SESSION_CONTEXT_PKG AS
    PROCEDURE CLEAR_CLAIMS IS
    BEGIN
        DBMS_SESSION.CLEAR_ALL_CONTEXT('DAB_SESSION_CONTEXT');
    END CLEAR_CLAIMS;

    PROCEDURE SET_CLAIM(p_name IN VARCHAR2, p_value IN VARCHAR2) IS
    BEGIN
        DBMS_SESSION.SET_CONTEXT('DAB_SESSION_CONTEXT', p_name, p_value);
    END SET_CLAIM;
END DAB_SESSION_CONTEXT_PKG;
/

-- Grant execution to the DAB login (replace DAB_USER with the connection-string User Id):
-- GRANT EXECUTE ON DAB_SESSION_CONTEXT_PKG TO DAB_USER;
