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
-- leak between requests on a pooled connection.
--
-- Package owner. The package is created in the schema of whoever runs this script. When that is not
-- the DAB login's schema, DAB's unqualified call fails with PLS-00201. Do one of:
--   * set the data-source option "session-context-package-owner" to the owning schema (for example
--     "SEC_ADMIN"); DAB then calls SEC_ADMIN.DAB_SESSION_CONTEXT_PKG, or
--   * create a private synonym in the DAB login's schema (see the end of this script).
--
-- Trust boundary. SET_CLAIM writes any attribute with definer rights and does not check its caller.
-- A session that holds EXECUTE on the package can set 'sub' or 'roles' to any value, so EXECUTE is
-- equivalent to "can assume any identity" to every SYS_CONTEXT-based policy. Grant it only to the
-- dedicated DAB login, never to PUBLIC, and do not let other applications share that login.
--
-- Reading claims. SYS_CONTEXT returns at most 256 bytes unless its third (length) argument is
-- given; DAB stores up to 4000 bytes per claim, so always pass 4000. Multi-valued claims are
-- forwarded as JSON array strings, which database-side policies can expand, for example:
--
--   SELECT ... WHERE region IN (
--       SELECT jt.value
--       FROM JSON_TABLE(SYS_CONTEXT('DAB_SESSION_CONTEXT', 'regions', 4000), '$[*]'
--            COLUMNS (value VARCHAR2(4000) PATH '$')) jt);
--
-- Inspect a forwarded value with:
--   SELECT SYS_CONTEXT('DAB_SESSION_CONTEXT', 'roles', 4000) FROM DUAL;

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

-- Grant execution to the DAB login only (replace DAB_USER with the connection-string User Id):
-- GRANT EXECUTE ON DAB_SESSION_CONTEXT_PKG TO DAB_USER;
--
-- If this script did not run as DAB_USER, either set "session-context-package-owner" to this
-- schema, or create a private synonym (requires CREATE ANY SYNONYM; replace OWNER with this schema):
-- CREATE OR REPLACE SYNONYM DAB_USER.DAB_SESSION_CONTEXT_PKG FOR OWNER.DAB_SESSION_CONTEXT_PKG;
