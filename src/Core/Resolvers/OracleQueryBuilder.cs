// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Oracle
    /// </summary>
    public class OracleQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder, IRelationalReadPlanBuilder
    {
        public const string UPSERT_IDENTIFIER_COLUMN_NAME = "___upsert_op___";
        private const string INSERT_UPSERT = "inserted";
        private const string UPDATE_UPSERT = "updated";
        private const string ORACLE_ESCAPE_CHAR = "\\";
        /// <summary>Alias of the DISTINCT page-key set joined by direct child aggregates.</summary>
        private const string PageJoinAlias = "dab_page";

        /// <summary>
        /// Oracle rejects IN lists with more than 1000 expressions (each chunk is emitted as its
        /// own OR'd IN list) and a statement can carry at most 65535 binds. Child cursors bind one
        /// entry per page key per correlation column, so pages beyond this bound fall back to the
        /// single-query JSON path instead of emitting a pathological statement.
        /// </summary>
        private const int MaxPageKeysForBindList = 10000;

        /// <summary>Maximum expressions in one Oracle IN list (ORA-01795 beyond it).</summary>
        private const int MaxInListExpressions = 1000;
        /// <summary>
        /// Indicator emitted by the fallback-to-update branch when the target row does not exist
        /// (no row matched the primary key, and no update policy exists to explain a no-match).
        /// The executor maps it to 404 ItemNotFound, mirroring the other database engines.
        /// </summary>
        public const string MISSING_UPSERT = "missing";
        public const string COUNT_ROWS_WITH_GIVEN_PK = "cnt_rows_to_update";
        public const string IS_FALLBACK_TO_UPDATE = "is_fallback_to_update";

        // DML RETURNING INTO fills output binds, not the DbDataReader. Wrap DML in a PL/SQL block
        // that opens :dab_result so ExtractResultSetFromDbDataReaderAsync can consume one row.
        // Literals cannot appear in RETURNING (upsert indicators go on the REF CURSOR SELECT).
        internal const string RESULT_CURSOR_PARAM_NAME = "dab_result";

        // Exposed column labels are not guaranteed to be valid Oracle bind-variable names: a column
        // mapping can expose a label containing spaces, punctuation, or non-ASCII characters
        // (e.g. "Scientific Name", "United State's Region", "始計"). Oracle parses ':Scientific
        // Name' as bind ':Scientific' followed by identifier 'Name', which corrupts the PL/SQL block.
        // RETURNING ... INTO therefore uses generated safe bind names; the REF CURSOR still aliases
        // them back to the exposed labels so the mutation response is unchanged.
        private const string OUTPUT_BIND_PREFIX = "dab_out_";

        private static DbCommandBuilder _builder = new OracleCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <summary>
        /// Quotes a physical column reference for use in raw SQL fragments (OData filters,
        /// predicate operands). Callers pass names already resolved to the PHYSICAL backing
        /// casing preserved from Oracle metadata, so no case transformation is applied - a
        /// quoted lowercase/mixed-case column resolves exactly as Oracle stores it.
        /// </summary>
        /// <inheritdoc />
        public override string QuotePhysicalColumn(string columnName)
        {
            return QuoteIdentifier(columnName);
        }

        /// <summary>
        /// Unquoted catalog objects (schema, table, package, procedure) are stored UPPERCASE.
        /// Always quoted here, so they must be emitted UPPERCASE.
        /// </summary>
        private string QuoteCatalogObject(string objectName)
        {
            return QuoteIdentifier(objectName.ToUpperInvariant());
        }

        /// <summary>
        /// Quotes a schema-qualified catalog object. Empty schema yields a bare quoted object.
        /// </summary>
        internal string QuoteRelation(string? schemaName, string objectName)
        {
            string quotedObject = QuoteCatalogObject(objectName);
            if (string.IsNullOrWhiteSpace(schemaName))
            {
                return quotedObject;
            }

            return $"{QuoteCatalogObject(schemaName)}.{quotedObject}";
        }

        /// <summary>
        /// DAB-generated aliases start as table{N}. They are quoted, so FROM/JOIN and column
        /// prefixes must share one spelling. Uppercase is that spelling.
        /// </summary>
        public string QuoteTableAlias(string alias)
        {
            return QuoteCatalogObject(alias);
        }

        /// <summary>
        /// Helper method to add ESCAPE clause to the LIKE clauses in the query.
        /// </summary>
        private static string AddEscapeToLikeClauses(string predicate)
        {
            const string escapeClause = $" ESCAPE '{ORACLE_ESCAPE_CHAR}'";
            // Regex to find LIKE clauses and append ESCAPE
            return Regex.Replace(predicate, @"(LIKE\s+@[\w\d]+)", $"$1{escapeClause}", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Overrides the base EXISTS-subquery builder used for predicates that filter on a nested
        /// relationship (e.g. <c>characters(filter: { actor: { name: { eq: ... } } })</c>). The base
        /// emits <c>FROM schema.table AS "alias"</c> which Oracle rejects: the AS keyword is not
        /// accepted for table aliases (ORA-00907/ORA-02000) and quoted identifiers are
        /// case-sensitive, so the alias and table/schema names must be emitted UPPERCASE to match
        /// the references produced by <see cref="Build(Column)"/>.
        /// </summary>
        /// <inheritdoc />
        public override string Build(BaseSqlQueryStructure structure)
        {
            string predicates = new(JoinPredicateStrings(
                       structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                       Build(structure.Predicates)));

            string query = $"SELECT 1 " +
                   $"FROM {QuoteRelation(structure.DatabaseObject.SchemaName, structure.DatabaseObject.Name)} " +
                   $"{QuoteTableAlias(structure.SourceAlias)}{Build(structure.Joins)} " +
                   $"WHERE {predicates}";

            return query;
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            OracleBuildContext context = new() { RootStructure = structure };
            string query = BuildStructureWithJson(structure, context);
            return context.Ctes.Count == 0
                ? query
                : $"WITH {string.Join(", ", context.Ctes)} {query}";
        }

        /// <summary>
        /// Builds a SqlQueryStructure into its single JSON document (list or object) and, when a
        /// <paramref name="context"/> is supplied, renders nested relationships as keyed CTE
        /// aggregates joined on the relationship foreign keys instead of correlated LATERALs.
        /// Null context means "legacy/correlated" mode: nested relationships are rendered as
        /// <c>LEFT OUTER JOIN LATERAL</c>. That mode is used only where the keyed CTE form cannot
        /// be proven equivalent (e.g. a relationship whose correlation predicate is not a plain
        /// column equality), so semantics are preserved while the common shapes get the
        /// de-correlated plan.
        /// </summary>
        private string BuildStructureWithJson(SqlQueryStructure structure, OracleBuildContext? context)
        {
            // Schema/table and DAB aliases go through QuoteRelation / QuoteTableAlias (uppercase,
            // quoted). Column names are physical backing names and are emitted verbatim.
            string fromSql = BuildFromSql(structure, context);

            // When the page CTE already applied the root predicates (filters, policy, keyset
            // paging), the final query reads the page instead of re-scanning the root table.
            List<Predicate>? predicatesOverride = context is not null && ReferenceEquals(structure, context.RootStructure)
                ? context.RootPredicatesOverride
                : null;
            string predicates = PageFeedsRoot(structure, context)
                ? BASE_PREDICATE
                : BuildStructurePredicates(structure, predicatesOverride);

            string aggregations = BuildAggregationColumns(structure);

            string query = $"SELECT {MakeSelectColumns(structure, context)}{aggregations}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}"
                + BuildGroupBy(structure)
                + BuildHaving(structure)
                + BuildOrderBy(structure)
                + $" OFFSET 0 ROWS FETCH NEXT {structure.Limit()} ROWS ONLY";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");
            string jsonDocAlias = QuoteIdentifier("json_doc");
            string orderAlias = QuoteIdentifier("__dab_ord");

            StringBuilder result = new();
            if (structure.IsListQuery)
            {
                // JSON_ARRAYAGG is unordered unless ORDER BY is given. ROWNUM is captured after
                // the inner ORDER BY/FETCH so cursor pagination matches that sort. FORMAT JSON
                // keeps the already-built object from being escaped as a string. Oracle requires
                // ORDER BY before RETURNING: "FORMAT JSON RETURNING CLOB ORDER BY" raises ORA-02000.
                result.Append($"SELECT COALESCE(JSON_ARRAYAGG({jsonDocAlias} FORMAT JSON ORDER BY {orderAlias} RETURNING CLOB), TO_CLOB(JSON_ARRAY())) ");
                result.Append($"AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} FROM ( ");
                result.Append($"SELECT JSON_OBJECT(* RETURNING CLOB) AS {jsonDocAlias}, ROWNUM AS {orderAlias} FROM ( ");
                result.Append(query);
                result.Append($" ) ) {subqueryName}");
            }
            else
            {
                // RETURNING CLOB must sit inside JSON_OBJECT(...) (outside the parens is ORA-00923)
                // to lift the VARCHAR2 4000-byte cap. TO_CLOB(JSON_OBJECT(*)) does not.
                result.Append($"SELECT JSON_OBJECT(* RETURNING CLOB) ");
                result.Append($"AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} FROM ( ");
                result.Append(query);
                result.Append($" ) {subqueryName}");
            }

            return result.ToString();
        }

        /// <summary>
        /// Per-query state for the keyed-CTE strategy. Every nested relationship that can be
        /// de-correlated becomes one CTE aggregating its JSON fragment once per parent key; the
        /// parent joins it on the relationship columns. CTEs are appended leaf-first, so a CTE can
        /// reference the CTEs registered for its own nested relationships before itself.
        /// </summary>
        private sealed class OracleBuildContext
        {
            public SqlQueryStructure? RootStructure { get; set; }

            public List<string> Ctes { get; } = new();

            /// <summary>JoinQueries alias (e.g. "table1_subq") to the CTE carrying its JSON.</summary>
            public Dictionary<string, string> CteNameByJoinAlias { get; } = new(StringComparer.Ordinal);

            /// <summary>Name of the request-page CTE, when one was emitted.</summary>
            public string? PageCteName { get; set; }

            /// <summary>
            /// Whether the final query reads the page CTE instead of re-scanning the root table
            /// (and therefore must not re-apply the root predicates).
            /// </summary>
            public bool PageCteFeedsRoot { get; set; }

            /// <summary>Parent correlation expression to the page CTE column holding its value.</summary>
            public Dictionary<string, string> PageColumnAliasByExpression { get; } = new(StringComparer.Ordinal);

            /// <summary>
            /// JoinQueries alias of a flattened to-one relationship to its inline JSON object
            /// expression (built from the flattened join's columns).
            /// </summary>
            public Dictionary<string, string> InlineJsonByJoinAlias { get; } = new(StringComparer.Ordinal);

            /// <summary>Counter used to name set-based nested-filter key CTEs.</summary>
            public int NextFilterCteId { get; set; }

            /// <summary>
            /// Page predicates with nested-relationship filters rewritten as set-based key CTEs.
            /// Reused by the final query when it does not read the page CTE.
            /// </summary>
            public List<Predicate>? PagePredicatesOverride { get; set; }

            /// <summary>Key-set joins belonging to <see cref="PagePredicatesOverride"/>.</summary>
            public List<string> PageKeySetJoins { get; } = new();

            /// <summary>Predicate override the final (root) query must use, when any.</summary>
            public List<Predicate>? RootPredicatesOverride { get; set; }
        }

        /// <summary>
        /// Builds the FROM clause of a structure, rendering each nested relationship as either a
        /// keyed CTE join (correlation is a set of plain column equalities) or the legacy
        /// correlated LATERAL when the CTE form cannot be proven equivalent.
        /// </summary>
        private string BuildFromSql(SqlQueryStructure structure, OracleBuildContext? context)
        {
            bool isRoot = context is not null && ReferenceEquals(structure, context.RootStructure);
            if (isRoot)
            {
                EnsurePageCte(structure, context!);
            }

            string fromSql = PageFeedsRoot(structure, context)
                ? $"{QuoteIdentifier(context!.PageCteName!)} {QuoteTableAlias(structure.SourceAlias)}"
                : $"{QuoteRelation(structure.DatabaseObject.SchemaName, structure.DatabaseObject.Name)} " +
                  $"{QuoteTableAlias(structure.SourceAlias)}{Build(structure.Joins)}";

            if (isRoot && context is not null && !PageFeedsRoot(structure, context))
            {
                // The final query applies the root predicates itself (no page CTE, or the page
                // cannot feed the root). Reuse the page's rewritten predicates and key-set joins
                // when present, otherwise rewrite the nested-filter EXISTS predicates now so the
                // root query never probes the child table per candidate parent.
                if (context.PagePredicatesOverride is not null)
                {
                    fromSql += string.Concat(context.PageKeySetJoins);
                    context.RootPredicatesOverride = context.PagePredicatesOverride;
                }
                else
                {
                    List<string> keySetJoins = new();
                    List<Predicate> rewritten = new();
                    foreach (Predicate predicate in structure.Predicates)
                    {
                        rewritten.Add(RewritePredicateForPage(
                            predicate, structure.SourceAlias, context, keySetJoins, inConjunction: true));
                    }

                    if (keySetJoins.Count > 0)
                    {
                        fromSql += string.Concat(keySetJoins);
                        context.RootPredicatesOverride = rewritten;
                    }
                }
            }

            // Policy/filter predicates are emitted as unqualified column strings. Flattening adds
            // sibling tables to this row source, which would make those strings ambiguous, so
            // flattening is disabled anywhere in the chain that carries such a predicate.
            bool allowFlatten = context is not null && !HasUnqualifiedPredicates(structure);
            AddRelationshipJoins(structure, context, applyPageRestriction: isRoot, allowFlatten, ref fromSql);
            return fromSql;
        }

        /// <summary>
        /// Whether the structure carries a raw policy or filter predicate string whose column
        /// references are not table-qualified.
        /// </summary>
        private static bool HasUnqualifiedPredicates(SqlQueryStructure structure)
        {
            return !string.IsNullOrEmpty(structure.GetDbPolicyForOperation(EntityActionOperation.Read))
                || !string.IsNullOrEmpty(structure.FilterPredicates);
        }

        /// <summary>
        /// Appends a join for every nested relationship of <paramref name="parent"/>: to-one
        /// relationships whose correlated columns are the child's primary key are flattened into
        /// the parent row source (no CTE, no ranking, inline JSON); other relationships become
        /// keyed aggregate CTEs; anything that cannot be de-correlated keeps the correlated
        /// LATERAL fallback.
        /// </summary>
        private void AddRelationshipJoins(
            SqlQueryStructure parent,
            OracleBuildContext? context,
            bool applyPageRestriction,
            bool allowFlatten,
            ref string fromSql)
        {
            foreach (KeyValuePair<string, SqlQueryStructure> joinQuery in parent.JoinQueries)
            {
                SqlQueryStructure child = joinQuery.Value;
                if (context is not null
                    && TryGetCorrelationKeys(child, parent.SourceAlias,
                        out List<(Column Parent, Column Child)> correlationKeys,
                        out HashSet<Predicate> correlationPredicates))
                {
                    bool flattenChild = allowFlatten && !HasUnqualifiedPredicates(child);
                    if (flattenChild
                        && TryFlattenToOneJoin(child, correlationKeys, context, flattenChild, ref fromSql, out string inlineJson))
                    {
                        context.InlineJsonByJoinAlias[joinQuery.Key] = inlineJson;
                        continue;
                    }

                    if (IsCteEligible(child))
                    {
                        string? pageJoin = applyPageRestriction ? BuildPageJoin(correlationKeys, context) : null;
                        if (TryBuildChildCte(joinQuery.Key, child, correlationKeys, correlationPredicates,
                                context, pageJoin, out string cteName,
                                out List<(string KeyAlias, string ParentExpression)> joinKeys))
                        {
                            string onClause = string.Join(" AND ", joinKeys.Select(
                                joinKey => $"{QuoteIdentifier(cteName)}.{QuoteIdentifier(joinKey.KeyAlias)} = {joinKey.ParentExpression}"));
                            fromSql += $" LEFT OUTER JOIN {QuoteIdentifier(cteName)} ON {onClause}";
                            continue;
                        }
                    }
                }

                fromSql += $" LEFT OUTER JOIN LATERAL ({BuildStructureWithJson(joinQuery.Value, null)}) {QuoteTableAlias(joinQuery.Key)} ON (1=1)";
            }
        }

        /// <summary>
        /// Flattens a to-one relationship into the parent row source when the correlated child
        /// columns are the child's primary key and the child cannot fan out. Its columns become
        /// plain join expressions and its JSON object is built inline, so the relationship needs
        /// no CTE, no CLOB column, and no ROW_NUMBER. Returns false when the child must keep a
        /// keyed aggregate (non-unique correlation, associative joins, or a list relationship).
        /// </summary>
        private bool TryFlattenToOneJoin(
            SqlQueryStructure child,
            List<(Column Parent, Column Child)> correlationKeys,
            OracleBuildContext context,
            bool allowFlatten,
            ref string fromSql,
            out string jsonExpression)
        {
            jsonExpression = string.Empty;
            if (!CanFlattenToOne(child, correlationKeys))
            {
                return false;
            }

            // The child's policy/filter predicates (including the correlation equalities) become
            // the outer join condition, so parents without a matching child keep their row.
            string childFrom = $"{QuoteRelation(child.DatabaseObject.SchemaName, child.DatabaseObject.Name)} " +
                               $"{QuoteTableAlias(child.SourceAlias)}{Build(child.Joins)}";
            fromSql += $" LEFT OUTER JOIN {childFrom} ON ({BuildStructurePredicates(child)})";

            // The child's own relationships must be joined before its JSON object is built.
            AddRelationshipJoins(child, context, applyPageRestriction: false, allowFlatten, ref fromSql);

            // An outer join with no matching child leaves every child column NULL, and building
            // JSON_OBJECT unconditionally would emit an all-null object instead of JSON null.
            // The correlated columns are the child's primary key (never NULL when a row exists),
            // so they distinguish "no child" from "child with null fields".
            string existenceCheck = string.Join(" AND ", correlationKeys.Select(
                key => $"{Build(key.Child)} IS NOT NULL"));
            jsonExpression = $"CASE WHEN {existenceCheck} THEN {BuildJsonObjectExpression(child, context)} ELSE NULL END";
            return true;
        }

        /// <summary>
        /// A to-one relationship can be flattened when the correlated child columns are exactly
        /// the child's primary key (one row per parent value) and the child has no associative
        /// joins: both conditions rule out row multiplication in the parent row source.
        /// </summary>
        private static bool CanFlattenToOne(
            SqlQueryStructure child,
            List<(Column Parent, Column Child)> correlationKeys)
        {
            if (child.IsListQuery || child.PaginationMetadata.IsPaginated || child.Joins.Count > 0)
            {
                return false;
            }

            return IsPrimaryKeyCorrelation(child, correlationKeys);
        }

        /// <summary>
        /// Whether the child-side correlation columns form the child's primary key, which makes
        /// the correlation unique and per-parent ranking unnecessary.
        /// </summary>
        private static bool IsPrimaryKeyCorrelation(
            SqlQueryStructure child,
            List<(Column Parent, Column Child)> correlationKeys)
        {
            List<string> primaryKey = child.PrimaryKey();
            if (primaryKey.Count == 0 || primaryKey.Count != correlationKeys.Count)
            {
                return false;
            }

            HashSet<string> correlationColumns = new(
                correlationKeys.Select(key => key.Child.ColumnName), StringComparer.Ordinal);
            return correlationColumns.SetEquals(primaryKey);
        }

        /// <summary>
        /// Whether a nested relationship can be rendered as a keyed aggregate CTE. Nested groupBy
        /// has no keyed equivalent, and a deterministic order is required for the per-parent rank.
        /// </summary>
        private static bool IsCteEligible(SqlQueryStructure child)
        {
            return child.GroupByMetadata.Fields.Count == 0 && child.OrderByColumns.Count > 0;
        }

        /// <summary>
        /// Extracts the equality predicates that correlate a child to its parent. Returns false when
        /// the child references the parent alias through anything other than a plain column
        /// equality, because such a child cannot be pre-aggregated independently of the parent row.
        /// </summary>
        private static bool TryGetCorrelationKeys(
            BaseSqlQueryStructure child,
            string parentAlias,
            out List<(Column Parent, Column Child)> correlationKeys,
            out HashSet<Predicate> correlationPredicates)
        {
            correlationKeys = new();
            correlationPredicates = new();
            foreach (Predicate predicate in child.Predicates)
            {
                Column? left = predicate.Left?.AsColumn();
                Column? right = predicate.Right?.AsColumn();
                bool leftIsParent = left is not null && string.Equals(left.TableAlias, parentAlias, StringComparison.Ordinal);
                bool rightIsParent = right is not null && string.Equals(right.TableAlias, parentAlias, StringComparison.Ordinal);
                if (!leftIsParent && !rightIsParent)
                {
                    continue;
                }

                if (predicate.Op != PredicateOperation.Equal
                    || left is null || right is null
                    || leftIsParent == rightIsParent)
                {
                    return false;
                }

                correlationPredicates.Add(predicate);
                correlationKeys.Add(leftIsParent ? (left, right) : (right, left));
            }

            return correlationKeys.Count > 0;
        }

        /// <summary>
        /// Emits the request-page CTE when the root has at least one de-correlatable child: the
        /// root's page (filters, policy, keyset predicate, ordering and limit applied) with every
        /// column the final query needs. Child aggregates use it to restrict ranking/JSON to
        /// page-reachable rows, and when the root has no associative joins the final query reads
        /// this CTE instead of re-scanning the root table, so the root predicates run only once.
        /// </summary>
        private void EnsurePageCte(SqlQueryStructure root, OracleBuildContext context)
        {
            if (context.PageCteName is not null
                || root.IsMultipleCreateOperation
                || root.GroupByMetadata.Fields.Count > 0
                || root.OrderByColumns.Count == 0
                || root.Limit() is null)
            {
                return;
            }

            bool allowFlatten = !HasUnqualifiedPredicates(root);
            bool canFeedRoot = root.Joins.Count == 0;
            bool needsPageRestriction = false;
            List<(string Expression, string ColumnName)> parentColumns = new();
            foreach (SqlQueryStructure child in root.JoinQueries.Values)
            {
                if (!TryGetCorrelationKeys(child, root.SourceAlias,
                        out List<(Column Parent, Column Child)> correlationKeys,
                        out _))
                {
                    // Parent columns referenced by a non-equality correlation cannot be exported,
                    // so the final query keeps scanning the root table.
                    canFeedRoot = false;
                    continue;
                }

                bool flattened = allowFlatten
                    && !HasUnqualifiedPredicates(child)
                    && CanFlattenToOne(child, correlationKeys);

                // Only keyed child aggregates consult the page; flattened joins are already
                // bounded by the page rows they join to.
                if (!flattened && IsCteEligible(child))
                {
                    needsPageRestriction = true;
                }

                foreach ((Column parent, Column _) in correlationKeys)
                {
                    string parentExpression = Build(parent);
                    if (!parentColumns.Any(entry => string.Equals(entry.Expression, parentExpression, StringComparison.Ordinal)))
                    {
                        parentColumns.Add((parentExpression, parent.ColumnName));
                    }
                }
            }

            if (!needsPageRestriction || parentColumns.Count == 0)
            {
                return;
            }

            string pageCteName = "dab_page_cte";
            List<string> pageColumns = new();
            HashSet<string> exportedNames = new(StringComparer.Ordinal);

            // Columns keyed by their physical name so the final query and its joins can reference
            // them as if they were reading the root table directly.
            foreach (LabelledColumn column in root.Columns)
            {
                if (column.ColumnName != SqlQueryStructure.DATA_IDENT
                    && exportedNames.Add(column.ColumnName))
                {
                    pageColumns.Add($"{Build(column as Column)} AS {QuoteIdentifier(column.ColumnName)}");
                }
            }

            foreach (OrderByColumn orderColumn in root.OrderByColumns)
            {
                if (exportedNames.Add(orderColumn.ColumnName))
                {
                    pageColumns.Add($"{Build(orderColumn, printDirection: false)} AS {QuoteIdentifier(orderColumn.ColumnName)}");
                }
            }

            foreach ((string expression, string columnName) in parentColumns)
            {
                if (exportedNames.Add(columnName))
                {
                    pageColumns.Add($"{expression} AS {QuoteIdentifier(columnName)}");
                }
            }

            // Child aggregates match page membership through these stable aliases.
            for (int i = 0; i < parentColumns.Count; i++)
            {
                string columnAlias = $"c{i}";
                pageColumns.Add($"{parentColumns[i].Expression} AS {QuoteIdentifier(columnAlias)}");
                context.PageColumnAliasByExpression[parentColumns[i].Expression] = columnAlias;
            }

            // Nested-relationship filters render as correlated EXISTS subqueries, which probe the
            // child table once per candidate parent. On large child tables without a parent/date
            // composite index Oracle can turn that into an index join over the whole date range
            // (minutes for a page). For the page computation, ANDed nested filters are rewritten
            // into set-based DISTINCT key CTEs joined once; predicates under OR/NOT are left as
            // EXISTS to preserve semantics.
            List<string> filterKeySetJoins = new();
            List<Predicate> pagePredicates = new();
            foreach (Predicate predicate in root.Predicates)
            {
                pagePredicates.Add(RewritePredicateForPage(
                    predicate, root.SourceAlias, context, filterKeySetJoins, inConjunction: true));
            }

            string baseFrom = $"{QuoteRelation(root.DatabaseObject.SchemaName, root.DatabaseObject.Name)} " +
                              $"{QuoteTableAlias(root.SourceAlias)}{Build(root.Joins)}" +
                              string.Concat(filterKeySetJoins);
            // The page CTE is read by the final query and by every direct child aggregate, and
            // its definition may reference a set-based filter CTE. Some Oracle versions inline a
            // query name used more than once when its definition references another query name
            // and then raise ORA-32036. MATERIALIZE pins the documented workaround and also stops
            // the page computation (and its filter scan) from being evaluated once per reader.
            string pageSql = $"SELECT /*+ MATERIALIZE */ {string.Join(", ", pageColumns)} FROM {baseFrom} " +
                             $"WHERE {BuildStructurePredicates(root, pagePredicates)}{BuildOrderBy(root)} " +
                             $"OFFSET 0 ROWS FETCH NEXT {(root.Limit() ?? 1).ToString(CultureInfo.InvariantCulture)} ROWS ONLY";

            context.Ctes.Add($"{QuoteIdentifier(pageCteName)} AS ( {pageSql} )");
            context.PageCteName = pageCteName;
            context.PageCteFeedsRoot = canFeedRoot;
            context.PagePredicatesOverride = pagePredicates;
            context.PageKeySetJoins.AddRange(filterKeySetJoins);
        }

        /// <summary>
        /// Rewrites an ANDed predicate for the page computation: a nested-relationship filter
        /// (an EXISTS over a correlated child) becomes a set-based DISTINCT key CTE joined once.
        /// Predicates nested under OR/NOT are copied unchanged so boolean semantics are preserved.
        /// </summary>
        private Predicate RewritePredicateForPage(
            Predicate predicate,
            string parentAlias,
            OracleBuildContext context,
            List<string> keySetJoins,
            bool inConjunction)
        {
            if (inConjunction
                && predicate.Left is null
                && predicate.Op == PredicateOperation.EXISTS
                && predicate.Right.AsSqlQueryStructure() is BaseSqlQueryStructure exists
                && TryBuildFilterKeySetJoin(exists, parentAlias, context, out string join))
            {
                keySetJoins.Add(join);
                return TruePredicate();
            }

            bool operandsInConjunction = inConjunction && predicate.Op == PredicateOperation.AND;
            PredicateOperand? left = RewriteOperandForPage(predicate.Left, parentAlias, context, keySetJoins, operandsInConjunction);
            PredicateOperand right = RewriteOperandForPage(predicate.Right, parentAlias, context, keySetJoins, operandsInConjunction)
                                     ?? predicate.Right;
            return ReferenceEquals(left, predicate.Left) && ReferenceEquals(right, predicate.Right)
                ? predicate
                : new Predicate(left, predicate.Op, right, predicate.AddParenthesis);
        }

        private PredicateOperand? RewriteOperandForPage(
            PredicateOperand? operand,
            string parentAlias,
            OracleBuildContext context,
            List<string> keySetJoins,
            bool inConjunction)
        {
            if (operand is null)
            {
                return null;
            }

            if (operand.AsPredicate() is Predicate inner)
            {
                Predicate rewritten = RewritePredicateForPage(inner, parentAlias, context, keySetJoins, inConjunction);
                return ReferenceEquals(rewritten, inner) ? operand : new PredicateOperand(rewritten);
            }

            return operand;
        }

        private static Predicate TruePredicate()
        {
            return new Predicate(new PredicateOperand("1"), PredicateOperation.Equal, new PredicateOperand("1"));
        }

        /// <summary>
        /// Builds the DISTINCT key CTE for one nested-relationship filter and returns the join that
        /// attaches it to the page. The CTE reads the correlated child columns for the filter's
        /// conditions in a single set-based pass, replacing per-parent probes.
        /// </summary>
        private bool TryBuildFilterKeySetJoin(
            BaseSqlQueryStructure exists,
            string parentAlias,
            OracleBuildContext context,
            out string join)
        {
            join = string.Empty;
            if (!TryGetCorrelationKeys(exists, parentAlias,
                    out List<(Column Parent, Column Child)> correlationKeys,
                    out HashSet<Predicate> correlationPredicates))
            {
                return false;
            }

            string cteName = $"dab_filter_cte_{context.NextFilterCteId++}";
            string fromSql = $"{QuoteRelation(exists.DatabaseObject.SchemaName, exists.DatabaseObject.Name)} " +
                             $"{QuoteTableAlias(exists.SourceAlias)}{Build(exists.Joins)}";

            List<string> keyAliases = new();
            List<string> keySelect = new();
            for (int i = 0; i < correlationKeys.Count; i++)
            {
                string keyAlias = $"k{i}";
                keyAliases.Add(keyAlias);
                keySelect.Add($"{Build(correlationKeys[i].Child)} AS {QuoteIdentifier(keyAlias)}");
            }

            string predicates = JoinPredicateStrings(
                exists.GetDbPolicyForOperation(EntityActionOperation.Read),
                exists.FilterPredicates,
                Build(exists.Predicates.Where(predicate => !correlationPredicates.Contains(predicate)).ToList()));
            predicates = AddEscapeToLikeClauses(predicates);

            context.Ctes.Add($"{QuoteIdentifier(cteName)} AS ( SELECT DISTINCT {string.Join(", ", keySelect)} " +
                             $"FROM {fromSql} WHERE {predicates} )");

            List<string> joinConditions = new();
            for (int i = 0; i < correlationKeys.Count; i++)
            {
                joinConditions.Add($"{QuoteIdentifier(cteName)}.{QuoteIdentifier(keyAliases[i])} = {Build(correlationKeys[i].Parent)}");
            }

            join = $" INNER JOIN {QuoteIdentifier(cteName)} ON ({string.Join(" AND ", joinConditions)})";
            return true;
        }

        /// <summary>
        /// Whether the final query for the structure reads the page CTE instead of the root table.
        /// </summary>
        private static bool PageFeedsRoot(SqlQueryStructure structure, OracleBuildContext? context)
        {
            return context is not null
                && ReferenceEquals(structure, context.RootStructure)
                && context.PageCteName is not null
                && context.PageCteFeedsRoot;
        }

        /// <summary>
        /// Builds the inner join that restricts a direct child aggregate to the page keys. Joining
        /// the DISTINCT key set (rather than an EXISTS probe) lets the optimizer push the parent
        /// key into the child index access, and cannot duplicate child rows inside the aggregate.
        /// </summary>
        private string? BuildPageJoin(
            List<(Column Parent, Column Child)> correlationKeys,
            OracleBuildContext context)
        {
            if (context.PageCteName is null)
            {
                return null;
            }

            List<string> pageColumns = new();
            List<string> conditions = new();
            foreach ((Column parent, Column child) in correlationKeys)
            {
                if (!context.PageColumnAliasByExpression.TryGetValue(Build(parent), out string? pageColumnAlias))
                {
                    return null;
                }

                if (!pageColumns.Contains(pageColumnAlias))
                {
                    pageColumns.Add(pageColumnAlias);
                }

                conditions.Add($"{QuoteIdentifier(PageJoinAlias)}.{QuoteIdentifier(pageColumnAlias)} = {Build(child)}");
            }

            return $" INNER JOIN (SELECT DISTINCT {string.Join(", ", pageColumns.Select(QuoteIdentifier))} " +
                   $"FROM {QuoteIdentifier(context.PageCteName)}) {QuoteIdentifier(PageJoinAlias)} " +
                   $"ON ({string.Join(" AND ", conditions)})";
        }

        #region Relational read plan (C#-side JSON assembly)

        // The plan model is internal, so the shared interface is satisfied explicitly while the
        // concrete methods stay internal for tests and the engine-side call sites of this build.
        bool IRelationalReadPlanBuilder.TryBuildRelationalReadPlan(
            SqlQueryStructure root,
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> pageKeysByJoinAlias,
            out RelationalReadPlan? plan)
            => TryBuildRelationalReadPlan(root, pageKeysByJoinAlias, out plan);

        bool IRelationalReadPlanBuilder.TryBuildRelationalPageCursor(
            SqlQueryStructure root,
            out RelationalReadCursor? pageCursor)
            => TryBuildRelationalPageCursor(root, out pageCursor);

        bool IRelationalReadPlanBuilder.TryBuildRelationalChildCursors(
            RelationalReadCursor parentCursor,
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> pageKeysByJoinAlias,
            out IReadOnlyList<RelationalReadCursor>? childCursors)
            => TryBuildRelationalChildCursors(parentCursor, pageKeysByJoinAlias, out childCursors);

        /// <summary>
        /// Builds the full relational plan for a request: the page cursor plus one cursor per
        /// relationship, scoped by the parent key values already read from the page cursor rows
        /// (<paramref name="pageKeysByJoinAlias"/> maps a relationship alias to one array of
        /// key values per parent row, in <see cref="TryGetCorrelationKeys"/> order).
        /// </summary>
        internal bool TryBuildRelationalReadPlan(
            SqlQueryStructure root,
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> pageKeysByJoinAlias,
            out RelationalReadPlan? plan)
        {
            plan = null;
            if (!TryBuildRelationalPageCursor(root, out RelationalReadCursor? pageCursor)
                || !TryBuildRelationalChildCursors(pageCursor!, pageKeysByJoinAlias, out IReadOnlyList<RelationalReadCursor>? childCursors))
            {
                return false;
            }

            plan = new RelationalReadPlan(pageCursor!, childCursors!);
            return true;
        }

        /// <summary>
        /// Builds the request-page cursor of the relational read plan: the root's filters (with
        /// nested-relationship filters rewritten as set-based key CTEs), policy, keyset predicate,
        /// ordering and limit applied to one flat row set. No JSON functions are emitted; nested
        /// documents are assembled in C#. Returns false when the root cannot be represented as
        /// flat rows (multiple-create read-back, groupBy, no deterministic order) or when a
        /// relationship in the tree is not correlated by plain column equalities.
        /// </summary>
        internal bool TryBuildRelationalPageCursor(SqlQueryStructure root, out RelationalReadCursor? pageCursor)
        {
            pageCursor = null;
            if (root.IsMultipleCreateOperation
                // A groupBy root is rendered as flat aggregate rows; nested relationships on a
                // groupBy query have no relational form, so that shape keeps the JSON path.
                || (root.GroupByMetadata.Fields.Count > 0 && root.JoinQueries.Count > 0)
                || (root.Limit() ?? 1) > MaxPageKeysForBindList)
            {
                return false;
            }

            CursorProjectionBuilder projection = new(QuoteIdentifier);
            Dictionary<string, List<string>> correlationAliases = new(StringComparer.Ordinal);
            string fromSql = $"{QuoteRelation(root.DatabaseObject.SchemaName, root.DatabaseObject.Name)} " +
                             $"{QuoteTableAlias(root.SourceAlias)}{Build(root.Joins)}";
            if (!TryAddRelationalStructure(
                    root, !HasUnqualifiedPredicates(root), projection, ref fromSql, correlationAliases,
                    out List<RelationalReadField> fields))
            {
                return false;
            }

            // Aggregate columns are not part of SqlQueryStructure.Columns; they are rendered
            // separately by the JSON builder and must be projected here for the assembler.
            foreach (AggregationOperation aggregation in root.GroupByMetadata.Aggregations)
            {
                fields.Add(projection.AddScalar(Build(aggregation.Column), aggregation.Column.OperationAlias));
            }

            // Reuse the page predicate rewrite (currently applied to the keyed-CTE page) so the
            // root never probes a child table once per candidate parent; the nested-filter key
            // CTEs are emitted on this cursor's own WITH clause.
            OracleBuildContext context = new() { RootStructure = root };
            List<string> keySetJoins = new();
            List<Predicate> predicates = new();
            foreach (Predicate predicate in root.Predicates)
            {
                predicates.Add(RewritePredicateForPage(predicate, root.SourceAlias, context, keySetJoins, inConjunction: true));
            }

            fromSql += string.Concat(keySetJoins);
            string sql = $"SELECT {projection.SelectList} FROM {fromSql} " +
                         $"WHERE {BuildStructurePredicates(root, predicates)}" +
                         $"{BuildGroupBy(root)}{BuildHaving(root)}{BuildOrderBy(root)} " +
                         $"OFFSET 0 ROWS FETCH NEXT {(root.Limit() ?? 1).ToString(CultureInfo.InvariantCulture)} ROWS ONLY";
            if (context.Ctes.Count > 0)
            {
                sql = $"WITH {string.Join(", ", context.Ctes)} {sql}";
            }

            pageCursor = new RelationalReadCursor(
                Structure: root,
                Sql: sql,
                JoinAlias: null,
                IsList: root.IsListQuery,
                Limit: (int)(root.Limit() ?? 1),
                Keys: Array.Empty<RelationalReadKey>(),
                Fields: fields,
                Binds: Array.Empty<RelationalReadBind>(),
                CorrelationAliasesByJoinAlias: ToReadOnlyAliasMap(correlationAliases),
                AliasByExpression: projection.AliasByExpression);
            return true;
        }

        /// <summary>
        /// Builds one cursor per relationship nested under <paramref name="parentCursor"/>'s rows,
        /// including relationships reached through flattened to-one rows. Each cursor ranks its
        /// rows per parent key, keeps the per-parent top-N and is restricted to the parent key
        /// values supplied by the caller, so the rank never scans children of non-page parents.
        /// </summary>
        internal bool TryBuildRelationalChildCursors(
            RelationalReadCursor parentCursor,
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> pageKeysByJoinAlias,
            out IReadOnlyList<RelationalReadCursor>? childCursors)
        {
            childCursors = null;
            List<RelationalReadCursor> cursors = new();
            if (!TryCollectRelationalChildCursors(
                    parentCursor.Structure,
                    parentCursor,
                    !HasUnqualifiedPredicates(parentCursor.Structure),
                    pageKeysByJoinAlias,
                    cursors))
            {
                return false;
            }

            childCursors = cursors;
            return true;
        }

        private bool TryCollectRelationalChildCursors(
            SqlQueryStructure structure,
            RelationalReadCursor parentCursor,
            bool allowFlatten,
            IReadOnlyDictionary<string, IReadOnlyList<object?[]>> pageKeysByJoinAlias,
            List<RelationalReadCursor> cursors)
        {
            foreach (KeyValuePair<string, SqlQueryStructure> joinQuery in structure.JoinQueries)
            {
                SqlQueryStructure child = joinQuery.Value;
                if (!TryGetCorrelationKeys(child, structure.SourceAlias,
                        out List<(Column Parent, Column Child)> correlationKeys,
                        out HashSet<Predicate> correlationPredicates))
                {
                    return false;
                }

                if (allowFlatten
                    && !HasUnqualifiedPredicates(child)
                    && CanFlattenToOne(child, correlationKeys))
                {
                    // The rows are part of the parent cursor; descend into its relationships.
                    if (!TryCollectRelationalChildCursors(child, parentCursor, allowFlatten, pageKeysByJoinAlias, cursors))
                    {
                        return false;
                    }

                    continue;
                }

                if (child.GroupByMetadata.Fields.Count > 0 || child.OrderByColumns.Count == 0)
                {
                    return false;
                }

                if (!pageKeysByJoinAlias.TryGetValue(joinQuery.Key, out IReadOnlyList<object?[]>? pageKeys)
                    || pageKeys.Count == 0)
                {
                    // No parent row carries a key for this relationship; the executor renders
                    // an empty result without opening a cursor.
                    continue;
                }

                if (!TryBuildRelationalChildCursor(
                        child, correlationKeys, correlationPredicates, joinQuery.Key, parentCursor, pageKeys,
                        out RelationalReadCursor? cursor))
                {
                    return false;
                }

                cursors.Add(cursor!);
            }

            return true;
        }

        private bool TryBuildRelationalChildCursor(
            SqlQueryStructure child,
            List<(Column Parent, Column Child)> correlationKeys,
            HashSet<Predicate> correlationPredicates,
            string joinAlias,
            RelationalReadCursor parentCursor,
            IReadOnlyList<object?[]> pageKeys,
            out RelationalReadCursor? cursor)
        {
            cursor = null;

            CursorProjectionBuilder projection = new(QuoteIdentifier);
            Dictionary<string, List<string>> correlationAliases = new(StringComparer.Ordinal);
            string fromSql = $"{QuoteRelation(child.DatabaseObject.SchemaName, child.DatabaseObject.Name)} " +
                             $"{QuoteTableAlias(child.SourceAlias)}{Build(child.Joins)}";
            if (!TryAddRelationalStructure(
                    child, !HasUnqualifiedPredicates(child), projection, ref fromSql, correlationAliases,
                    out List<RelationalReadField> fields))
            {
                return false;
            }

            List<RelationalReadKey> keys = new();
            List<string> keyAliases = new();
            List<string> keyExpressions = new();
            for (int i = 0; i < correlationKeys.Count; i++)
            {
                string expression = Build(correlationKeys[i].Child);
                if (!parentCursor.AliasByExpression.TryGetValue(Build(correlationKeys[i].Parent), out string? parentAlias))
                {
                    return false;
                }

                string alias = $"k{i}";
                keyAliases.Add(alias);
                keyExpressions.Add(expression);
                keys.Add(new RelationalReadKey(alias, parentAlias));
            }

            // One bind per parent-side key value: the cursor returns rows only for the parent
            // rows already read, so ranking and the top-N run within the page, not the whole table.
            List<RelationalReadBind> binds = new();
            List<string> keyRows = new();
            for (int row = 0; row < pageKeys.Count; row++)
            {
                List<string> rowBinds = new();
                for (int i = 0; i < correlationKeys.Count; i++)
                {
                    object? value = i < pageKeys[row].Length ? pageKeys[row][i] : null;
                    string name = BaseQueryStructure.GetEncodedParamName(child.Counter.Next());
                    binds.Add(new RelationalReadBind(name, value, correlationKeys[i].Parent.ColumnName));
                    rowBinds.Add(name);
                }

                keyRows.Add(rowBinds.Count == 1 ? rowBinds[0] : $"({string.Join(", ", rowBinds)})");
            }

            string leftSide = keyExpressions.Count == 1
                ? keyExpressions[0]
                : $"({string.Join(", ", keyExpressions)})";

            // Oracle caps one IN list at 1000 expressions (ORA-01795); larger pages are split
            // into several OR'd lists over the same bind values.
            List<string> keyPredicates = new();
            for (int start = 0; start < keyRows.Count; start += MaxInListExpressions)
            {
                IEnumerable<string> chunk = keyRows.Skip(start).Take(MaxInListExpressions);
                keyPredicates.Add($"{leftSide} IN ({string.Join(", ", chunk)})");
            }

            string pageKeyPredicate = keyPredicates.Count == 1
                ? keyPredicates[0]
                : $"({string.Join(" OR ", keyPredicates)})";

            string predicateSql = JoinPredicateStrings(
                child.GetDbPolicyForOperation(EntityActionOperation.Read),
                child.FilterPredicates,
                Build(child.Predicates.Where(predicate => !correlationPredicates.Contains(predicate)).ToList()),
                Build(child.PaginationMetadata.PaginationPredicate));
            predicateSql = AddEscapeToLikeClauses(predicateSql);

            int limit = (int)(child.Limit() ?? 1);
            string orderSql = string.Join(", ", child.OrderByColumns.Select(orderColumn => Build(orderColumn)));
            string partitionBy = string.Join(", ", keyExpressions);
            string keySelect = string.Join(", ", keyExpressions.Zip(
                keyAliases, (expression, alias) => $"{expression} AS {QuoteIdentifier(alias)}"));
            string rankedColumns = projection.SelectList.Length > 0
                ? $"{projection.SelectList}, {keySelect}"
                : keySelect;
            string rnAlias = QuoteIdentifier("rn");

            string innerSql = $"SELECT {rankedColumns}, " +
                              $"ROW_NUMBER() OVER (PARTITION BY {partitionBy} ORDER BY {orderSql}) AS {rnAlias} " +
                              $"FROM {fromSql} WHERE {predicateSql} AND {pageKeyPredicate}";

            // Aliases are quoted everywhere: an unquoted reference would fold to uppercase and
            // miss the quoted alias projected by the inline view (ORA-00904).
            string childAlias = QuoteTableAlias(child.SourceAlias);
            List<string> outerColumns = new(projection.Aliases);
            outerColumns.AddRange(keyAliases);
            outerColumns.Add("rn");
            string outerSelect = string.Join(", ", outerColumns.Select(column => $"{childAlias}.{QuoteIdentifier(column)}"));
            string outerOrder = string.Join(", ", keyAliases
                .Select(alias => $"{childAlias}.{QuoteIdentifier(alias)}")
                .Append($"{childAlias}.{QuoteIdentifier("rn")}"));

            string sql = $"SELECT {outerSelect} FROM ( {innerSql} ) {childAlias} " +
                         $"WHERE {childAlias}.{rnAlias} <= {limit.ToString(CultureInfo.InvariantCulture)} " +
                         $"ORDER BY {outerOrder}";

            cursor = new RelationalReadCursor(
                Structure: child,
                Sql: sql,
                JoinAlias: joinAlias,
                IsList: child.IsListQuery,
                Limit: limit,
                Keys: keys,
                Fields: fields,
                Binds: binds,
                CorrelationAliasesByJoinAlias: ToReadOnlyAliasMap(correlationAliases),
                AliasByExpression: projection.AliasByExpression);
            return true;
        }

        /// <summary>
        /// Projects one structure into a relational cursor: selected scalar columns in selection
        /// order, flattened to-one selections as nested field trees, and (second pass) the
        /// parent-side correlation columns of every relationship that becomes its own cursor.
        /// </summary>
        private bool TryAddRelationalStructure(
            SqlQueryStructure structure,
            bool allowFlatten,
            CursorProjectionBuilder projection,
            ref string fromSql,
            Dictionary<string, List<string>> correlationAliases,
            out List<RelationalReadField> fields)
        {
            fields = new();

            foreach (LabelledColumn column in structure.Columns)
            {
                if (column.ColumnName != SqlQueryStructure.DATA_IDENT)
                {
                    fields.Add(projection.AddScalar(Build(column as Column), column.Label));
                    continue;
                }

                if (column.TableAlias is null
                    || !structure.JoinQueries.TryGetValue(column.TableAlias, out SqlQueryStructure? nested)
                    || !TryGetCorrelationKeys(nested, structure.SourceAlias,
                        out List<(Column Parent, Column Child)> nestedKeys, out _))
                {
                    return false;
                }

                if (!allowFlatten
                    || HasUnqualifiedPredicates(nested)
                    || !CanFlattenToOne(nested, nestedKeys))
                {
                    // Rendered as its own cursor (built by TryBuildRelationalChildCursors); the
                    // field is a placeholder the assembler fills from that cursor's result.
                    fields.Add(new RelationalReadField(
                        jsonName: column.Label,
                        relationJoinAlias: column.TableAlias,
                        relationIsList: nested.IsListQuery));
                    continue;
                }

                if (!TryAddFlattenedRelationalObject(
                        nested, nestedKeys, allowFlatten, projection, ref fromSql, correlationAliases, column.Label,
                        out RelationalReadField? objectField))
                {
                    return false;
                }

                fields.Add(objectField!);
            }

            foreach (KeyValuePair<string, SqlQueryStructure> joinQuery in structure.JoinQueries)
            {
                SqlQueryStructure nested = joinQuery.Value;
                if (!TryGetCorrelationKeys(nested, structure.SourceAlias,
                        out List<(Column Parent, Column Child)> nestedKeys, out _))
                {
                    return false;
                }

                if (allowFlatten
                    && !HasUnqualifiedPredicates(nested)
                    && CanFlattenToOne(nested, nestedKeys))
                {
                    // Rendered into this cursor by the first pass.
                    continue;
                }

                if (nested.GroupByMetadata.Fields.Count > 0 || nested.OrderByColumns.Count == 0)
                {
                    return false;
                }

                List<string> aliases = new();
                foreach ((Column parent, Column _) in nestedKeys)
                {
                    aliases.Add(projection.AddExpression(Build(parent), parent.ColumnName));
                }

                correlationAliases[joinQuery.Key] = aliases;
            }

            return true;
        }

        /// <summary>
        /// Joins a to-one child into the cursor's row source and renders its selection as a nested
        /// field tree. The outer join cannot multiply rows because the correlation columns are the
        /// child's primary key; those columns double as the object's null guard.
        /// </summary>
        private bool TryAddFlattenedRelationalObject(
            SqlQueryStructure child,
            List<(Column Parent, Column Child)> correlationKeys,
            bool allowFlatten,
            CursorProjectionBuilder projection,
            ref string fromSql,
            Dictionary<string, List<string>> correlationAliases,
            string jsonName,
            out RelationalReadField? objectField)
        {
            objectField = null;

            string childFrom = $"{QuoteRelation(child.DatabaseObject.SchemaName, child.DatabaseObject.Name)} " +
                               $"{QuoteTableAlias(child.SourceAlias)}";
            fromSql += $" LEFT OUTER JOIN {childFrom} ON ({BuildStructurePredicates(child)})";

            List<string> guardAliases = new();
            foreach ((Column _, Column childColumn) in correlationKeys)
            {
                guardAliases.Add(projection.AddExpression(Build(childColumn), childColumn.ColumnName));
            }

            if (!TryAddRelationalStructure(
                    child, allowFlatten, projection, ref fromSql, correlationAliases, out List<RelationalReadField> children))
            {
                return false;
            }

            objectField = new RelationalReadField(
                jsonName,
                isObject: true,
                nullGuardAliases: guardAliases,
                children: children);
            return true;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToReadOnlyAliasMap(
            Dictionary<string, List<string>> aliases)
        {
            return aliases.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Accumulates one cursor's projection: the SELECT list, the alias chosen for every built
        /// expression (so nested correlation columns resolve to already-projected aliases instead
        /// of duplicating them), and the JSON field tree.
        /// </summary>
        private sealed class CursorProjectionBuilder
        {
            private readonly Func<string, string> _quoteIdentifier;
            private readonly List<string> _select = new();
            private readonly List<string> _aliases = new();
            private readonly List<RelationalReadField> _fields = new();
            private readonly Dictionary<string, string> _aliasByExpression = new(StringComparer.Ordinal);
            private readonly HashSet<string> _takenAliases = new(StringComparer.Ordinal);

            public CursorProjectionBuilder(Func<string, string> quoteIdentifier)
            {
                _quoteIdentifier = quoteIdentifier;
            }

            public string SelectList => string.Join(", ", _select);

            public IReadOnlyList<string> Aliases => _aliases;

            public IReadOnlyDictionary<string, string> AliasByExpression => _aliasByExpression;

            /// <summary>Projects an expression once and returns the alias it is available under.</summary>
            public string AddExpression(string expression, string preferredAlias)
            {
                if (_aliasByExpression.TryGetValue(expression, out string? existing))
                {
                    return existing;
                }

                string alias = preferredAlias;
                for (int suffix = 1; !_takenAliases.Add(alias); suffix++)
                {
                    alias = $"{preferredAlias}_{suffix}";
                }

                _select.Add($"{expression} AS {_quoteIdentifier(alias)}");
                _aliases.Add(alias);
                _aliasByExpression[expression] = alias;
                return alias;
            }

            public RelationalReadField AddScalar(string expression, string jsonName)
            {
                RelationalReadField field = new(jsonName, alias: AddExpression(expression, jsonName));
                _fields.Add(field);
                return field;
            }
        }

        #endregion

        /// <summary>
        /// Builds the policy/filter/pagination predicate shared by the main query and the page CTE.
        /// <paramref name="predicatesOverride"/> supplies already-rewritten predicates for the page
        /// (see <see cref="RewritePredicateForPage"/>).
        /// </summary>
        private string BuildStructurePredicates(SqlQueryStructure structure, List<Predicate>? predicatesOverride = null)
        {
            List<Predicate> predicates = predicatesOverride ?? structure.Predicates;
            string predicateString;
            if (structure.IsMultipleCreateOperation)
            {
                predicateString = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(predicates, " OR ", isMultipleCreateOperation: true),
                                    Build(structure.PaginationMetadata.PaginationPredicate));
            }
            else
            {
                predicateString = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));
            }

            // Add ESCAPE clause to LIKE predicates so that % and _ wildcards in the
            // search pattern are properly escaped when used as literal characters.
            return AddEscapeToLikeClauses(predicateString);
        }

        /// <summary>
        /// Registers a keyed aggregate CTE for one nested relationship. <see cref="BuildFromSql"/>
        /// inside <see cref="BuildChildCteSql"/> registers this child's own nested CTEs first,
        /// which keeps the WITH clause leaf-first (a CTE may only reference earlier CTEs).
        /// </summary>
        private bool TryBuildChildCte(
            string joinAlias,
            SqlQueryStructure child,
            List<(Column Parent, Column Child)> correlationKeys,
            HashSet<Predicate> correlationPredicates,
            OracleBuildContext context,
            string? pageJoin,
            out string cteName,
            out List<(string KeyAlias, string ParentExpression)> joinKeys)
        {
            cteName = $"{joinAlias}_cte";
            joinKeys = new();
            List<string> keyAliases = new();
            List<string> keyExpressions = new();
            for (int i = 0; i < correlationKeys.Count; i++)
            {
                string keyAlias = $"k{i}";
                keyAliases.Add(keyAlias);
                keyExpressions.Add(Build(correlationKeys[i].Child));
                joinKeys.Add((keyAlias, Build(correlationKeys[i].Parent)));
            }

            // A to-one relationship correlated on the child's primary key has exactly one row per
            // key, so the per-parent rank (and its sort) can be skipped entirely.
            bool skipRanking = !child.IsListQuery
                && child.Joins.Count == 0
                && IsPrimaryKeyCorrelation(child, correlationKeys);

            string cteSql = BuildChildCteSql(
                child, correlationPredicates, keyAliases, keyExpressions, context, pageJoin, skipRanking);
            context.Ctes.Add($"{QuoteIdentifier(cteName)} AS ( {cteSql} )");
            context.CteNameByJoinAlias[joinAlias] = cteName;
            return true;
        }

        /// <summary>
        /// Builds one relationship aggregate CTE: rank the child rows per parent key, keep the
        /// per-parent top-N (N is the child's limit, which includes the extra row used for
        /// pagination), then aggregate the JSON fragment once per parent key. For a to-one
        /// relationship whose correlation is the child's primary key the rank and sort are
        /// skipped because exactly one row exists per key.
        /// </summary>
        private string BuildChildCteSql(
            SqlQueryStructure child,
            HashSet<Predicate> correlationPredicates,
            List<string> keyAliases,
            List<string> keyExpressions,
            OracleBuildContext context,
            string? pageJoin,
            bool skipRanking)
        {
            // Direct foreign-key relationships rank first and attach descendant relationships
            // only to the surviving rows, so flattened to-one joins and descendant aggregates run
            // for the per-parent top-N instead of the whole page child set. Associative (many-to-
            // many) children keep the single-block form because their link-table joins can expose
            // duplicate column names, which the ranked row source cannot carry.
            return child.Joins.Count == 0
                ? BuildRankedChildCteSql(child, correlationPredicates, keyAliases, keyExpressions, context, pageJoin, skipRanking)
                : BuildJoinedChildCteSql(child, correlationPredicates, keyAliases, keyExpressions, context, pageJoin, skipRanking);
        }

        /// <summary>
        /// Rank-then-join form: the child's own predicates (including the page restriction) apply
        /// in the ranking block; relationship joins and JSON construction happen afterwards, only
        /// for the rows that survive the per-parent top-N filter.
        /// </summary>
        private string BuildRankedChildCteSql(
            SqlQueryStructure child,
            HashSet<Predicate> correlationPredicates,
            List<string> keyAliases,
            List<string> keyExpressions,
            OracleBuildContext context,
            string? pageJoin,
            bool skipRanking)
        {
            string predicates = JoinPredicateStrings(
                child.GetDbPolicyForOperation(EntityActionOperation.Read),
                child.FilterPredicates,
                Build(child.Predicates.Where(predicate => !correlationPredicates.Contains(predicate)).ToList()),
                Build(child.PaginationMetadata.PaginationPredicate));
            predicates = AddEscapeToLikeClauses(predicates);

            string childAlias = QuoteTableAlias(child.SourceAlias);
            string keyList = string.Join(", ", keyAliases.Select(QuoteIdentifier));
            string keySelect = string.Join(", ", keyExpressions.Zip(
                keyAliases, (expression, alias) => $"{expression} AS {QuoteIdentifier(alias)}"));
            string baseFrom = $"{QuoteRelation(child.DatabaseObject.SchemaName, child.DatabaseObject.Name)} " +
                              $"{childAlias}{Build(child.Joins)}" +
                              (pageJoin ?? string.Empty);
            string exposedSelect = BuildRankedExposedColumns(child, childAlias, context);

            string coreSelect = $"SELECT {exposedSelect}, {keySelect}";
            if (!skipRanking)
            {
                string partitionBy = string.Join(", ", keyExpressions);
                string orderBy = string.Join(", ", child.OrderByColumns.Select(orderColumn => Build(orderColumn)));
                coreSelect += $", ROW_NUMBER() OVER (PARTITION BY {partitionBy} ORDER BY {orderBy}) AS \"rn\"";
            }

            // The ranked subquery keeps the child alias and its physical column names, so the
            // descendant joins and their predicates render exactly as they would against the base
            // table. The child's own predicates were already applied inside the ranking block.
            // Keys and the rank are qualified with the child alias because descendant CTE joins
            // share the FROM and expose key columns of their own.
            string rankedFrom = $"( {coreSelect} FROM {baseFrom} WHERE {predicates} ) {childAlias}";
            AddRelationshipJoins(child, context, applyPageRestriction: false, allowFlatten: true, ref rankedFrom);

            string jsonObject = BuildJsonObjectExpression(child, context);
            string rowLimit = (child.Limit() ?? 1).ToString(CultureInfo.InvariantCulture);
            string qualifiedKeys = string.Join(", ", keyAliases.Select(
                keyAlias => $"{childAlias}.{QuoteIdentifier(keyAlias)}"));

            if (skipRanking)
            {
                return $"SELECT {qualifiedKeys}, {jsonObject} AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} " +
                       $"FROM {rankedFrom}";
            }

            if (!child.IsListQuery)
            {
                return $"SELECT {qualifiedKeys}, {jsonObject} AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} " +
                       $"FROM {rankedFrom} WHERE {childAlias}.\"rn\" <= {rowLimit}";
            }

            // JSON is built only for rows that survived the per-parent top-N filter.
            return $"SELECT {keyList}, " +
                   $"COALESCE(JSON_ARRAYAGG(json_doc ORDER BY \"rn\" RETURNING CLOB), TO_CLOB(JSON_ARRAY())) " +
                   $"AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} " +
                   $"FROM ( SELECT {jsonObject} AS json_doc, {qualifiedKeys} AS {keyList}, " +
                   $"{childAlias}.\"rn\" AS \"rn\" FROM {rankedFrom} " +
                   $"WHERE {childAlias}.\"rn\" <= {rowLimit} ) ranked_rows GROUP BY {keyList}";
        }

        /// <summary>
        /// Columns the ranked row source must expose: the child's own projected columns plus the
        /// parent-side columns every descendant relationship joins on. Falls back to the whole row
        /// when a descendant correlation cannot be enumerated.
        /// </summary>
        private string BuildRankedExposedColumns(SqlQueryStructure child, string childAlias, OracleBuildContext context)
        {
            List<string> exposed = new();
            HashSet<string> exposedNames = new(StringComparer.Ordinal);

            foreach (LabelledColumn column in child.Columns)
            {
                if (column.ColumnName != SqlQueryStructure.DATA_IDENT
                    && exposedNames.Add(column.ColumnName))
                {
                    exposed.Add($"{Build(column as Column)} AS {QuoteIdentifier(column.ColumnName)}");
                }
            }

            foreach (SqlQueryStructure descendant in child.JoinQueries.Values)
            {
                if (!TryGetCorrelationKeys(descendant, child.SourceAlias,
                        out List<(Column Parent, Column Child)> correlationKeys,
                        out _))
                {
                    // A non-equality correlation may reference any child column, so expose all.
                    return $"{childAlias}.*";
                }

                foreach ((Column parent, Column _) in correlationKeys)
                {
                    if (exposedNames.Add(parent.ColumnName))
                    {
                        exposed.Add($"{Build(parent)} AS {QuoteIdentifier(parent.ColumnName)}");
                    }
                }
            }

            return exposed.Count == 0 ? "1" : string.Join(", ", exposed);
        }

        /// <summary>
        /// Single-block form used for associative children: all relationship joins participate in
        /// the ranking block, and JSON is aggregated over the top-N rows.
        /// </summary>
        private string BuildJoinedChildCteSql(
            SqlQueryStructure child,
            HashSet<Predicate> correlationPredicates,
            List<string> keyAliases,
            List<string> keyExpressions,
            OracleBuildContext context,
            string? pageJoin,
            bool skipRanking)
        {
            string fromSql = BuildFromSql(child, context) + (pageJoin ?? string.Empty);

            string predicates = JoinPredicateStrings(
                child.GetDbPolicyForOperation(EntityActionOperation.Read),
                child.FilterPredicates,
                Build(child.Predicates.Where(predicate => !correlationPredicates.Contains(predicate)).ToList()),
                Build(child.PaginationMetadata.PaginationPredicate));
            predicates = AddEscapeToLikeClauses(predicates);

            string visibleSelect = string.Join(", ", child.Columns.Select(
                column => $"{BuildColumnValue(child, column, context)} AS {QuoteIdentifier(column.Label)}"));
            string partitionBy = string.Join(", ", keyExpressions);
            string orderBy = string.Join(", ", child.OrderByColumns.Select(orderColumn => Build(orderColumn)));
            string keyList = string.Join(", ", keyAliases.Select(QuoteIdentifier));
            string keySelect = string.Join(", ", keyExpressions.Zip(
                keyAliases, (expression, alias) => $"{expression} AS {QuoteIdentifier(alias)}"));
            string rowLimit = (child.Limit() ?? 1).ToString(CultureInfo.InvariantCulture);
            string jsonObject = BuildJsonObjectFromAliases(child, context);
            string source = $"SELECT {visibleSelect}, {keySelect} FROM {fromSql} WHERE {predicates}";

            if (skipRanking)
            {
                return $"SELECT {keyList}, {jsonObject} AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} " +
                       $"FROM ( {source} )";
            }

            string ranked = $"SELECT {visibleSelect}, {keySelect}, " +
                            $"ROW_NUMBER() OVER (PARTITION BY {partitionBy} ORDER BY {orderBy}) AS \"rn\" " +
                            $"FROM {fromSql} WHERE {predicates}";

            if (child.IsListQuery)
            {
                return $"SELECT {keyList}, " +
                       $"COALESCE(JSON_ARRAYAGG({jsonObject} ORDER BY \"rn\" RETURNING CLOB), TO_CLOB(JSON_ARRAY())) " +
                       $"AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} " +
                       $"FROM ( {ranked} ) WHERE \"rn\" <= {rowLimit} GROUP BY {keyList}";
            }

            return $"SELECT {keyList}, {jsonObject} AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} " +
                   $"FROM ( {ranked} ) WHERE \"rn\" <= {rowLimit}";
        }

        /// <summary>
        /// Builds the explicit JSON object over the ranked row source's column aliases. Unlike
        /// JSON_OBJECT(*), the key/value list lets the keyed CTE strategy exclude ranking/key
        /// columns from the document. NULL ON NULL matches JSON_OBJECT(*) behavior for nulls.
        /// </summary>
        private string BuildJsonObjectFromAliases(SqlQueryStructure structure, OracleBuildContext context)
        {
            List<string> fields = new();
            foreach (LabelledColumn column in structure.Columns)
            {
                fields.Add($"{QuoteJsonKey(column.Label)} VALUE {QuoteIdentifier(column.Label)}");
            }

            return $"JSON_OBJECT({string.Join(", ", fields)} NULL ON NULL{BuildJsonReturningClause(structure, context)})";
        }

        /// <summary>
        /// Chooses the JSON object's return type: VARCHAR2 when a conservative upper bound on the
        /// serialized object stays well below the 4000-byte limit (avoiding a LOB per row), and
        /// CLOB otherwise (unbounded strings, list relationships, lateral fallbacks, cycles).
        /// </summary>
        private static string BuildJsonReturningClause(SqlQueryStructure structure, OracleBuildContext context)
        {
            return TryEstimateJsonObjectSize(structure, context, new HashSet<SqlQueryStructure>(), out _)
                ? " RETURNING VARCHAR2(4000)"
                : " RETURNING CLOB";
        }

        /// <summary>
        /// Estimates the serialized size of a structure's JSON object, recursing into flattened
        /// to-one objects and keyed to-one CTEs. Returns false when any part is unbounded.
        /// </summary>
        private static bool TryEstimateJsonObjectSize(
            SqlQueryStructure structure,
            OracleBuildContext context,
            HashSet<SqlQueryStructure> path,
            out int size)
        {
            const int varcharBudget = 3000;
            size = 16;
            if (!path.Add(structure))
            {
                // Relationship cycle: stay conservative.
                return false;
            }

            try
            {
                SourceDefinition sourceDefinition = structure.GetUnderlyingSourceDefinition();
                foreach (LabelledColumn column in structure.Columns)
                {
                    // Worst-case JSON escaping expands one character to six bytes.
                    size += (QuoteJsonKey(column.Label).Length + 4) * 6;
                    if (size > varcharBudget)
                    {
                        return false;
                    }

                    if (column.ColumnName == SqlQueryStructure.DATA_IDENT)
                    {
                        if (column.TableAlias is null
                            || !structure.JoinQueries.TryGetValue(column.TableAlias, out SqlQueryStructure? child)
                            || child.IsListQuery
                            || (!context.InlineJsonByJoinAlias.ContainsKey(column.TableAlias)
                                && !context.CteNameByJoinAlias.ContainsKey(column.TableAlias)))
                        {
                            return false;
                        }

                        if (!TryEstimateJsonObjectSize(child, context, path, out int childSize))
                        {
                            return false;
                        }

                        // CASE/CTE null handling and key overhead.
                        size += childSize + 64;
                        continue;
                    }

                    if (!sourceDefinition.Columns.TryGetValue(column.ColumnName, out ColumnDefinition? definition)
                        || !TryEstimateScalarSize(definition, ref size))
                    {
                        return false;
                    }
                }

                return size <= varcharBudget;
            }
            finally
            {
                path.Remove(structure);
            }
        }

        /// <summary>
        /// Adds a conservative upper bound for one scalar column's JSON representation. Returns
        /// false for strings/byte arrays without a known bounded length.
        /// </summary>
        private static bool TryEstimateScalarSize(ColumnDefinition definition, ref int size)
        {
            Type systemType = definition.SystemType;
            if (systemType == typeof(string))
            {
                if (definition.Length is not int length || length <= 0)
                {
                    return false;
                }

                size += length * 6;
            }
            else if (systemType == typeof(byte[]))
            {
                if (definition.Length is not int byteLength || byteLength <= 0)
                {
                    return false;
                }

                size += ((byteLength + 2) / 3) * 4;
            }
            else
            {
                // Numbers/dates/guids/times have small fixed representations.
                size += 64;
            }

            return size <= 3000;
        }

        /// <summary>
        /// Builds the JSON object for a flattened to-one row source directly from its column
        /// value expressions, so no intermediate CTE/CLOB column is needed for the relationship.
        /// </summary>
        private string BuildJsonObjectExpression(SqlQueryStructure structure, OracleBuildContext context)
        {
            List<string> fields = new();
            foreach (LabelledColumn column in structure.Columns)
            {
                fields.Add($"{QuoteJsonKey(column.Label)} VALUE {BuildColumnValue(structure, column, context)}");
            }

            return $"JSON_OBJECT({string.Join(", ", fields)} NULL ON NULL{BuildJsonReturningClause(structure, context)})";
        }

        private static string QuoteJsonKey(string label)
        {
            return $"'{label.Replace("'", "''")}'";
        }

        /// <summary>
        /// Builds the value expression for one projected column: nested JSON fragments come from
        /// the child CTE (or the lateral alias), byte[] columns are base64-encoded, and everything
        /// else is a plain column reference.
        /// </summary>
        private string BuildColumnValue(SqlQueryStructure structure, LabelledColumn column, OracleBuildContext? context)
        {
            if (column.ColumnName == SqlQueryStructure.DATA_IDENT)
            {
                if (context is not null && column.TableAlias is not null)
                {
                    // Flattened to-one relationship: its JSON object is built inline over the
                    // flattened join's columns.
                    if (context.InlineJsonByJoinAlias.TryGetValue(column.TableAlias, out string? inlineJson))
                    {
                        return inlineJson;
                    }

                    if (context.CteNameByJoinAlias.TryGetValue(column.TableAlias, out string? childCteName)
                        && structure.JoinQueries.TryGetValue(column.TableAlias, out SqlQueryStructure? childStructure))
                    {
                        string expression = $"{QuoteIdentifier(childCteName)}.{QuoteIdentifier(SqlQueryStructure.DATA_IDENT)}";
                        // A keyed CTE has no row for a parent with zero children; list relationships
                        // must still deserialize as an empty JSON array, matching the correlated shape.
                        return childStructure.IsListQuery
                            ? $"COALESCE({expression}, TO_CLOB(JSON_ARRAY()))"
                            : expression;
                    }
                }

                return Build(column as Column);
            }

            if (structure.GetColumnSystemType(column.ColumnName) == typeof(byte[]))
            {
                // Oracle RAW/BLOB is not stored as base64 so a conversion is made before
                // producing the json result since HotChocolate handles ByteArray as base64.
                // UTL_ENCODE.BASE64_ENCODE(NULL) throws ORA-29261 "bad argument", so NULL byte
                // columns must pass through unmodified (CASE WHEN ... IS NULL THEN NULL).
                string refColumn = Build(column as Column);
                return $"CASE WHEN {refColumn} IS NULL THEN NULL " +
                       $"ELSE UTL_RAW.CAST_TO_VARCHAR2(UTL_ENCODE.BASE64_ENCODE({refColumn})) END";
            }

            return Build(column as Column);
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            string dbPolicyPredicates = JoinPredicateStrings(structure.GetDbPolicyForOperation(EntityActionOperation.Create));
            SourceDefinition sourceDefinition = structure.GetUnderlyingSourceDefinition();

            string tableName = QuoteRelation(structure.DatabaseObject.SchemaName, structure.DatabaseObject.Name);
            string insertQuery = $"INSERT INTO {tableName} ";

            // Config + derived FK params can resolve to the same physical column. Oracle
            // rejects duplicate names in INSERT (ORA-00957) and in the policy DUAL subquery
            // (ORA-00918). Last write wins so an explicit FK is kept over a derived one.
            (List<string> insertCols, List<string> insertVals) = structure.InsertColumns.Count > 0
                ? DedupeInsertColumns(structure.InsertColumns, structure.Values)
                : ([], []);

            if (insertCols.Count > 0)
            {
                string insertColumns = BuildColumnList(insertCols);
                insertQuery += $"({insertColumns}) ";
                insertQuery += $"VALUES ({string.Join(", ", insertVals)}) ";
            }
            else
            {
                // Oracle does not support the SQL Server/PostgreSQL DEFAULT VALUES syntax.
                // Insert one defaulted or identity column explicitly instead.
                // (An insert with no columns and a create database policy is rejected earlier by
                // SqlInsertStructure, because there would be no row values for the policy to scope.)
                string? defaultColumn = sourceDefinition.Columns
                    .Where(pair => pair.Value.HasDefault || pair.Value.IsAutoGenerated)
                    .Select(pair => pair.Key)
                    .FirstOrDefault();

                if (defaultColumn is null)
                {
                    throw new DataApiBuilderException(
                        "INSERT requires at least one defaulted or identity column when no values are provided",
                        System.Net.HttpStatusCode.BadRequest,
                        DataApiBuilderException.SubStatusCodes.DatabaseInputError);
                }

                insertQuery += $"({QuoteIdentifier(defaultColumn)}) VALUES (DEFAULT) ";
            }

            // RETURNING rejects aliases (ORA-00925); emit bare physical column names.
            (string returningColumns, string bindNames, string selectFromBinds, string outputTypeHints) =
                BuildOutputBindings(structure.OutputColumns, sourceDefinition);

            bool hasCreatePolicy = !dbPolicyPredicates.Equals(BASE_PREDICATE);
            if (hasCreatePolicy)
            {
                // PL/SQL cannot host a scalar subquery in IF (PLS-00103). SELECT COUNT(*) INTO a
                // NUMBER, then gate the INSERT. Empty cursor (WHERE 1 = 0) surfaces 403, not ORA-01400.
                string namedValues = string.Join(", ", insertCols.Zip(insertVals,
                    (col, val) => $"{val} AS {QuoteIdentifier(col)}"));
                return $"{outputTypeHints}DECLARE v_dab_insert_count NUMBER; BEGIN " +
                    $"SELECT COUNT(*) INTO v_dab_insert_count FROM (SELECT {namedValues} FROM DUAL) WHERE {dbPolicyPredicates}; " +
                    $"IF v_dab_insert_count > 0 THEN " +
                    $"{insertQuery} RETURNING {returningColumns} INTO {bindNames}; " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; " +
                    $"ELSE " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL WHERE 1 = 0; " +
                    $"END IF; " +
                    $"END;";
            }

            return $"{outputTypeHints}BEGIN {insertQuery} RETURNING {returningColumns} INTO {bindNames}; " +
                $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; END;";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            string predicates = JoinPredicateStrings(
                                   structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                                   Build(structure.Predicates));

            // The RETURNING column list must be bare (no aliases - ORA-00925); column names carry the
            // exact physical casing preserved from Oracle metadata.
            (string returningColumns, string bindNames, string selectFromBinds, string outputTypeHints) =
                BuildOutputBindings(structure.OutputColumns, structure.GetUnderlyingSourceDefinition());
            string updateQuery = $"UPDATE {QuoteRelation(structure.DatabaseObject.SchemaName, structure.DatabaseObject.Name)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {predicates} " +
                    $"RETURNING {returningColumns} INTO {bindNames}";

            // When the UPDATE matches no row (record absent, or the update database policy blocks it),
            // RETURNING INTO never fires and the output binds stay NULL. Opening the cursor
            // unconditionally would fabricate a row of all-NULL columns, which the mutation engine
            // treats as a successful update. Mirror the upsert builder: open an EMPTY cursor when
            // SQL%ROWCOUNT is 0 so a no-match update surfaces as "item not found" (or a policy
            // failure), matching the behavior of the other database engines.
            return $"{outputTypeHints}BEGIN {updateQuery}; " +
                $"IF SQL%ROWCOUNT > 0 THEN " +
                $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL; " +
                $"ELSE " +
                $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {selectFromBinds} FROM DUAL WHERE 1 = 0; " +
                $"END IF; " +
                $"END;";
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            string predicates = JoinPredicateStrings(
                       structure.GetDbPolicyForOperation(EntityActionOperation.Delete),
                       Build(structure.Predicates));

            return $"DELETE FROM {QuoteRelation(structure.DatabaseObject.SchemaName, structure.DatabaseObject.Name)} " +
                    $"WHERE {predicates}";
        }

        /// <summary>
        /// Builds an Oracle-compatible stored procedure execution query.
        /// "EXEC" is SQL*Plus client syntax and is invalid for ODP.NET CommandType.Text
        /// (ORA-00900), so the subprogram is invoked from within a PL/SQL anonymous block.
        /// Oracle subprograms return result sets through a SYS_REFCURSOR (a procedure's OUT
        /// parameter, or a function's RETURN value), which ODP.NET does NOT surface in the
        /// DbDataReader on its own. We therefore bind ":dab_result" (registered as a RefCursor by
        /// <see cref="OracleBindRegistrar"/>) whose rows ODP.NET exposes as the reader result set.
        ///
        /// Arguments use named association so a REF CURSOR that is not the last parameter, and a
        /// skipped optional argument, cannot shift later binds. A function's RETURN is assigned,
        /// not named:
        ///  - Procedure: BEGIN "S"."P"("ID" => :param0, "P_CUR" => :dab_result); END;
        ///  - Function returning a cursor: BEGIN :dab_result := "S"."P"("ID" => :param0); END;
        ///  - Scalar function: SELECT "S"."P"("ID" => :param0) AS "value" FROM DUAL
        /// </summary>
        public string Build(SqlExecuteStructure structure)
        {
            DatabaseStoredProcedure sp = (DatabaseStoredProcedure)structure.DatabaseObject;

            // NOTE: sp.IsFunction is NOT parsed from config; it is populated during metadata
            // discovery (OracleMetadataProvider.FillSchemaForStoredProcedureAsync detects the
            // ALL_ARGUMENTS POSITION 0 row that marks a function's RETURN value). This Build method
            // therefore relies on that discovery having completed first - an ordering invariant of
            // the startup pipeline. If it were ever skipped, IsFunction would remain false and a
            // function would be emitted as a bare `BEGIN schema.func(:p0); END;` statement, which
            // is invalid PL/SQL for a function.
            string qualifiedName = string.IsNullOrEmpty(sp.PackageName)
                ? QuoteRelation(sp.SchemaName, sp.Name)
                : $"{QuoteRelation(sp.SchemaName, sp.PackageName)}.{QuoteCatalogObject(sp.Name)}";

            // ProcedureParameters maps each subprogram argument NAME (catalog spelling) to the
            // engine-generated bind reference (e.g. "@param0"). The call must reference the VALUES.
            // Emitting the keys (":id") would produce binds with no matching DbConnectionParam.
            // StoredProcedureDefinition.Columns holds OUT/RETURN metadata. An IDataReader column is
            // the REF CURSOR argument (procedures) or the function RETURN (functions).
            bool returnsCursor = structure.GetUnderlyingSourceDefinition().Columns.Values
                .Any(column => column.SystemType == typeof(IDataReader));
            List<string> callArgs = BuildNamedCallArguments(structure, includeRefCursor: !sp.IsFunction && returnsCursor);

            if (sp.IsFunction)
            {
                // A function's result (even if it is a REF CURSOR) is expressed as its RETURN
                // value, so it cannot be called as a bare statement.
                if (returnsCursor)
                {
                    return $"BEGIN :{RESULT_CURSOR_PARAM_NAME} := {qualifiedName}({JoinArgs(callArgs)}); END;";
                }

                string select = $"SELECT {qualifiedName}({JoinArgs(callArgs)}) AS {QuoteIdentifier("value")} FROM DUAL";
                return select;
            }

            // Procedures with no arguments are invoked without parentheses.
            string args = callArgs.Count > 0 ? $"({string.Join(", ", callArgs)})" : string.Empty;

            return $"BEGIN {qualifiedName}{args}; END;";
        }

        /// <summary>
        /// Named PL/SQL associations (<c>"ARG" => :paramN</c>). The REF CURSOR is bound to its
        /// actual argument name when this is a procedure; a function RETURN is not an argument.
        /// </summary>
        private List<string> BuildNamedCallArguments(SqlExecuteStructure structure, bool includeRefCursor)
        {
            List<string> callArgs = new();
            foreach ((string argumentName, object bind) in structure.ProcedureParameters)
            {
                string bindName = bind.ToString()!.TrimStart('@');
                callArgs.Add($"{QuoteIdentifier(argumentName)} => :{bindName}");
            }

            if (!includeRefCursor)
            {
                return callArgs;
            }

            string? cursorArgument = null;
            foreach ((string columnName, ColumnDefinition column) in structure.GetUnderlyingSourceDefinition().Columns)
            {
                if (column.SystemType == typeof(IDataReader))
                {
                    cursorArgument = columnName;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(cursorArgument))
            {
                callArgs.Add($"{QuoteIdentifier(cursorArgument)} => :{RESULT_CURSOR_PARAM_NAME}");
            }
            else
            {
                // Result metadata did not record the cursor argument name. Keep a bind so the
                // executor still opens a reader, rather than dropping the result set.
                callArgs.Add($":{RESULT_CURSOR_PARAM_NAME}");
            }

            return callArgs;
        }

        private static string JoinArgs(List<string> callArgs)
        {
            return callArgs.Count > 0 ? string.Join(", ", callArgs) : string.Empty;
        }

        public string Build(SqlUpsertQueryStructure structure)
        {
            // Oracle upserts are built as a SINGLE PL/SQL anonymous block because:
            //  1. ODP.NET does not accept multiple ';'-separated statements in one command
            //     (ORA-03405), so the Postgres-style "COUNT; UPDATE; INSERT;" batch is impossible.
            //  2. MERGE cannot return per-branch data (RETURNING only supports a single column
            //     expression list and cannot include literals).
            //  3. UPDATE/INSERT ... RETURNING delivers values through output binds, not the reader.
            //
            // The block performs the UPDATE first; if it matched no rows (SQL%ROWCOUNT = 0) it
            // performs a guarded INSERT. Each branch emits the resulting columns - plus an
            // ___upsert_op___ indicator literal - through a REF CURSOR result set that the executor
            // reads to distinguish update (200) from insert (201) and to surface policy failures.
            string tableName = QuoteRelation(structure.DatabaseObject.SchemaName, structure.DatabaseObject.Name);
            string pkPredicates = Build(structure.Predicates);

            string updatePredicates = JoinPredicateStrings(pkPredicates, structure.GetDbPolicyForOperation(EntityActionOperation.Update));
            // RETURNING column list must be bare (no aliases - ORA-00925); column names carry the
            // exact physical casing preserved from Oracle metadata.
            (string returningColumns, string bindNames, string selectFromBinds, string outputTypeHints) =
                BuildOutputBindings(structure.OutputColumns, structure.GetUnderlyingSourceDefinition());
            string updateQuery = $"UPDATE {tableName} " +
                $"SET {Build(structure.UpdateOperations, ", ")} " +
                $"WHERE {updatePredicates} " +
                $"RETURNING {returningColumns} " +
                $"INTO {bindNames}";

            if (structure.IsFallbackToUpdate)
            {
                // Update-only flow (e.g. autogenerated PK): no INSERT branch. When no row matched
                // the primary key + update policy, distinguish "row exists but the update policy
                // blocked it" (403, empty cursor - non-leaky) from "row absent" (404) by probing
                // row existence, mirroring PostgreSQL/MSSQL:
                //  - row exists: the UPDATE was blocked by the policy -> EMPTY cursor (403).
                //  - row absent: emit a 'missing' indicator row so the executor surfaces 404.
                // PL/SQL cannot evaluate a scalar subquery in an IF condition (PLS-00103), so the
                // existence probe uses SELECT COUNT(*) INTO a declared variable.
                string pkExistencePredicates = Build(structure.Predicates);
                string fallbackIndicator = $"{selectFromBinds}, '{UPDATE_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";
                string missingIndicator = $"{selectFromBinds}, '{MISSING_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";
                return $"{outputTypeHints}DECLARE v_dab_upsert_count NUMBER; BEGIN {updateQuery}; " +
                    $"IF SQL%ROWCOUNT > 0 THEN " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {fallbackIndicator} FROM DUAL; " +
                    $"ELSE " +
                    $"SELECT COUNT(*) INTO v_dab_upsert_count FROM {tableName} WHERE {pkExistencePredicates}; " +
                    $"IF v_dab_upsert_count > 0 THEN " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {fallbackIndicator} FROM DUAL WHERE 1 = 0; " +
                    $"ELSE " +
                    $"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {missingIndicator} FROM DUAL; " +
                    $"END IF; " +
                    $"END IF; " +
                    $"END;";
            }
            else
            {
                // INSERT only runs when the UPDATE matched no row. Two cases:
                //   1. The row EXISTS but the update policy blocked it → return 403 via an empty
                //      cursor, without leaking whether the row exists (matches Postgres/MSSQL).
                //   2. The row is ABSENT → attempt INSERT, gated by the create policy.
                // When the create policy blocks the insert, an empty cursor surfaces 403.
                //
                // Race safety: concurrent upserts for the same missing PK serialize on the primary
                // key; the loser hits ORA-00001 (unique constraint) which the exception parser maps
                // to HTTP 409, matching the behavior of the other engines' non-atomic upserts.
                string? createPolicy = structure.GetDbPolicyForOperation(EntityActionOperation.Create);
                bool hasCreatePolicy = !string.IsNullOrEmpty(createPolicy) && !createPolicy.Equals(BASE_PREDICATE);

                (List<string> insertCols, List<string> insertVals) =
                    DedupeInsertColumns(structure.InsertColumns, structure.Values);
                string insertColumns = BuildColumnList(insertCols);
                string insertQuery = $"INSERT INTO {tableName} ({insertColumns}) " +
                    $"VALUES ({string.Join(", ", insertVals)}) " +
                    $"RETURNING {returningColumns} " +
                    $"INTO {bindNames}";

                // Alias each value with its physical column name in a DUAL subquery so a create
                // policy that references column names (e.g. "OWNERID" = :paramN, emitted via
                // QuotePhysicalColumn) can resolve them - otherwise ORA-00904 invalid identifier.
                string namedValues = string.Join(", ", insertCols.Zip(insertVals,
                    (col, val) => $"{val} AS {QuoteIdentifier(col)}"));

                string insertIndicator = $"{selectFromBinds}, '{INSERT_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";
                string updateIndicator = $"{selectFromBinds}, '{UPDATE_UPSERT}' AS {QuoteIdentifier(UPSERT_IDENTIFIER_COLUMN_NAME)}";

                StringBuilder block = new();
                block.Append($"{outputTypeHints}DECLARE v_dab_upsert_count NUMBER; BEGIN ");
                block.Append($"{updateQuery}; ");
                block.Append($"IF SQL%ROWCOUNT > 0 THEN ");
                block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {updateIndicator} FROM DUAL; ");
                block.Append($"ELSE ");
                // Distinguish "row exists but update policy blocked" (403) from "row absent" (insert).
                // PL/SQL cannot evaluate a scalar subquery in an IF condition (PLS-00103), so the
                // existence probe uses SELECT COUNT(*) INTO a declared variable.
                block.Append($"SELECT COUNT(*) INTO v_dab_upsert_count FROM {tableName} WHERE {pkPredicates}; ");
                block.Append($"IF v_dab_upsert_count > 0 THEN ");
                block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {updateIndicator} FROM DUAL WHERE 1 = 0; ");
                block.Append($"ELSE ");
                if (hasCreatePolicy)
                {
                    // Gate the INSERT on the create policy: when blocked, open an empty cursor
                    // so the executor surfaces 403 rather than ORA-01400 (400).
                    block.Append($"SELECT COUNT(*) INTO v_dab_upsert_count FROM (SELECT {namedValues} FROM DUAL) WHERE {createPolicy}; ");
                    block.Append($"IF v_dab_upsert_count > 0 THEN ");
                    block.Append($"{insertQuery}; ");
                    block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL; ");
                    block.Append($"ELSE ");
                    block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL WHERE 1 = 0; ");
                    block.Append($"END IF; ");
                }
                else
                {
                    block.Append($"{insertQuery}; ");
                    block.Append($"OPEN :{RESULT_CURSOR_PARAM_NAME} FOR SELECT {insertIndicator} FROM DUAL; ");
                }

                block.Append($"END IF; ");
                block.Append($"END IF; ");
                block.Append($"END;");
                return block.ToString();
            }
        }

        /// <summary>
        /// Builds the four correlated fragments a RETURNING ... INTO DML block needs:
        /// the bare physical RETURNING column list, the generated safe INTO bind names,
        /// the REF CURSOR SELECT that aliases each bind back to its exposed label, and the
        /// type-hint comment consumed by <see cref="OracleQueryExecutor"/>.
        /// Exposed labels are used only as cursor aliases - never as bind names, because they
        /// may contain characters Oracle rejects in a bind variable.
        /// </summary>
        private (string ReturningColumns, string BindNames, string SelectFromBinds, string OutputTypeHints)
            BuildOutputBindings(
                IReadOnlyList<LabelledColumn> outputColumns,
                SourceDefinition sourceDefinition)
        {
            // RETURNING rejects aliases (ORA-00925); emit bare physical column names.
            string returningColumns = string.Join(", ", outputColumns.Select(c => QuoteIdentifier(c.ColumnName)));
            string bindNames = string.Join(", ", outputColumns.Select((_, index) => $":{OUTPUT_BIND_PREFIX}{index}"));
            string selectFromBinds = string.Join(", ", outputColumns.Select(
                (column, index) => $":{OUTPUT_BIND_PREFIX}{index} AS {QuoteIdentifier(column.Label)}"));
            string outputTypeHints = BuildOutputTypeHints(outputColumns, sourceDefinition);

            return (returningColumns, bindNames, selectFromBinds, outputTypeHints);
        }

        /// <summary>
        /// Prefixes the PL/SQL block with a comment listing RETURNING bind types so the
        /// executor can register output parameters with the correct OracleDbType. The keys are
        /// the generated safe bind names emitted by <see cref="BuildOutputBindings"/>.
        /// </summary>
        private static string BuildOutputTypeHints(
            IReadOnlyList<LabelledColumn> outputColumns,
            SourceDefinition sourceDefinition)
        {
            IEnumerable<string> hints = outputColumns
                .Select((column, index) =>
                {
                    sourceDefinition.Columns.TryGetValue(column.ColumnName, out ColumnDefinition? definition);
                    OracleDbType outputType = GetOracleOutputType(definition);
                    if (outputType == OracleDbType.Raw)
                    {
                        // SQL RAW is at most 2000 bytes; PL/SQL RAW is at most 32767.
                        // Size 0 makes ODP.NET RETURNING INTO fail or return empty bytes.
                        int length = definition?.Length is int columnLength and > 0 ? columnLength : 2000;
                        int size = Math.Min(Math.Max(length, 2000), 32767);
                        return $"{OUTPUT_BIND_PREFIX}{index}={outputType}:{size}";
                    }

                    return $"{OUTPUT_BIND_PREFIX}{index}={outputType}";
                });

            return $"/* DAB_ORACLE_OUTPUT_TYPES:{string.Join(",", hints)} */ ";
        }

        private static OracleDbType GetOracleOutputType(ColumnDefinition? definition)
        {
            Type? systemType = definition?.SystemType;
            DbType? dbType = definition?.DbType;

            if (systemType == typeof(byte[]))
            {
                // BLOB/LONG RAW do not fit in a RAW bind (PL/SQL RAW max is 32767 bytes).
                // NCLOB is a string flagged IsClob and must not take this binary path.
                return definition?.IsBlob == true ? OracleDbType.Blob : OracleDbType.Raw;
            }

            if (systemType == typeof(DateTimeOffset))
            {
                return OracleDbType.TimeStampTZ;
            }

            if (systemType == typeof(DateTime))
            {
                return OracleDbType.TimeStamp;
            }

            // CLOB/NCLOB columns must bind as Clob (not Varchar2, which truncates at 4000 bytes
            // with ORA-06502) in RETURNING ... INTO output parameters.
            if (definition?.IsClob is true)
            {
                return OracleDbType.Clob;
            }

            return dbType switch
            {
                DbType.Boolean => OracleDbType.Boolean,
                DbType.Byte => OracleDbType.Byte,
                DbType.Int16 => OracleDbType.Int16,
                DbType.Int32 => OracleDbType.Int32,
                DbType.Int64 => OracleDbType.Int64,
                DbType.Single => OracleDbType.Single,
                DbType.Double => OracleDbType.Double,
                DbType.Decimal => OracleDbType.Decimal,
                _ => OracleDbType.Varchar2,
            };
        }

        protected override string Build(Column column)
        {
            // OracleMetadataProvider.GetPhysicalDatabaseColumnName preserves the exact physical
            // casing stored in Oracle metadata: UPPERCASE for unquoted identifiers (e.g. ID,
            // TITLE) and original case for quoted ones (e.g. __column1, data). Emit the name
            // verbatim so quoted identifiers resolve correctly (Oracle is case-sensitive for
            // quoted identifiers: "DATA" != "data", "__column1" != "__COLUMN1").
            if (!string.IsNullOrEmpty(column.TableAlias))
            {
                return $"{QuoteTableAlias(column.TableAlias)}.{QuoteIdentifier(column.ColumnName)}";
            }
            // If there is no table alias we return [{Column}]
            else
            {
                return $"{QuoteIdentifier(column.ColumnName)}";
            }
        }

        /// <summary>
        /// Override to emit the INNER JOIN alias WITHOUT the AS keyword - Oracle does not accept
        /// "AS" for table aliases in the FROM/JOIN clause (unlike column aliases) and rejects it
        /// with ORA-02000 "missing ON or USING keyword". The alias is uppercased to match the
        /// uppercase alias emitted by <see cref="Build(Column)"/> (Oracle is case-sensitive for
        /// quoted identifiers).
        /// </summary>
        protected override string Build(SqlJoinStructure join)
        {
            if (join is null)
            {
                throw new ArgumentNullException(nameof(join));
            }

            return $" INNER JOIN {QuoteRelation(join.DbObject.SchemaName, join.DbObject.Name)} " +
                   $"{QuoteTableAlias(join.TableAlias)} " +
                   $"ON {Build(join.Predicates)}";
        }

        /// <summary>
        /// Builds an aggregation column (e.g. MAX("TABLE0"."ID")) for the SELECT list and
        /// HAVING clauses. Reuses <see cref="Build(Column)"/> so alias and column casing stay
        /// consistent. Aggregation functions (COUNT/SUM/AVG/MIN/MAX) are case-insensitive.
        /// </summary>
        protected override string Build(AggregationColumn column, bool useAlias = false)
        {
            string columnName = Build(column as Column);
            columnName = column.IsDistinct ? $"DISTINCT ({columnName})" : columnName;
            string appendAlias = useAlias ? $" AS {QuoteIdentifier(column.OperationAlias)}" : string.Empty;
            return $"{column.Type.ToString().ToUpperInvariant()}({columnName}) {appendAlias}";
        }

        /// <summary>
        /// Builds a comma-separated, individually-quoted column list for INSERT column lists.
        /// Column names carry the exact physical casing preserved from Oracle metadata (uppercase
        /// for unquoted identifiers, exact case for quoted ones), so they are emitted verbatim.
        /// </summary>
        private string BuildColumnList(IEnumerable<string> columnNames)
        {
            return string.Join(", ", columnNames.Select(c => QuoteIdentifier(c)));
        }

        /// <summary>
        /// Collapses insert column/value pairs that share a physical name (ignore-case).
        /// Later values replace earlier ones so an explicit FK is kept over a derived one.
        /// </summary>
        private static (List<string> Columns, List<string> Values) DedupeInsertColumns(
            IReadOnlyList<string> columns,
            IReadOnlyList<string> values)
        {
            Dictionary<string, (string Column, string Value)> unique = new(StringComparer.OrdinalIgnoreCase);
            int count = Math.Min(columns.Count, values.Count);
            for (int i = 0; i < count; i++)
            {
                unique[columns[i]] = (columns[i], values[i]);
            }

            return ([.. unique.Values.Select(pair => pair.Column)], [.. unique.Values.Select(pair => pair.Value)]);
        }

        /// <summary>
        /// Looks into the upsert result returned by Oracle and returns
        /// whether the upsert was executed as an insert.
        /// This function also removes the metadata column that Oracle
        /// returns to indicate how UPSERT is executed.
        /// </summary>
        public static bool IsInsert(IDictionary<string, object?> upsertResult)
        {
            if (!upsertResult.ContainsKey(UPSERT_IDENTIFIER_COLUMN_NAME))
            {
                throw new ArgumentException($"Upsert result must have a {UPSERT_IDENTIFIER_COLUMN_NAME} column.");
            }

            object? opType = upsertResult[UPSERT_IDENTIFIER_COLUMN_NAME];
            upsertResult.Remove(UPSERT_IDENTIFIER_COLUMN_NAME);

            if (opType != null && opType is string opTypeStr)
            {
                switch (opTypeStr)
                {
                    case INSERT_UPSERT:
                        return true;
                    case UPDATE_UPSERT:
                        return false;
                }
            }

            throw new ArgumentException($"Invalid {UPSERT_IDENTIFIER_COLUMN_NAME} column value.");
        }

        /// <summary>
        /// Encode byte array columns to base64 strings instead of hex strings
        /// when parsing the results into json
        /// </summary>
        private string MakeSelectColumns(SqlQueryStructure structure, OracleBuildContext? context)
        {
            List<string> builtColumns = new();

            // go through columns to find columns with type byte[]
            foreach (LabelledColumn column in structure.Columns)
            {
                // columns which contain the json of a nested type are called SqlQueryStructure.DATA_IDENT
                // and they are not actual columns of the underlying table so don't check for column type
                // in that scenario
                builtColumns.Add(
                    $"{BuildColumnValue(structure, column, context)} AS {QuoteIdentifier(column.Label)}");
            }

            return string.Join(", ", builtColumns);
        }

        /// <summary>
        /// Builds the metadata query for a STANDALONE Oracle subprogram only.
        /// Callers must bind <c>@param0</c> (schema) and <c>@param1</c> (subprogram name); the
        /// values are compared case-insensitively in the query.
        /// </summary>
        /// <inheritdoc/>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            // Oracle 19c implementation for retrieving stored procedure result set metadata.
            // Oracle doesn't have a direct equivalent to SQL Server's
            // dm_exec_describe_first_result_set_for_object. Instead we query ALL_ARGUMENTS to get
            // OUT / IN OUT arguments that represent the result.
            // databaseObjectName is unused: names are bound as @param0/@param1 (never interpolated)
            // so a hostile object name cannot alter the query. The signature is fixed by IQueryBuilder.
            string query =
                $"SELECT " +
                $"ARGUMENT_NAME AS {QuoteIdentifier(STOREDPROC_COLUMN_NAME)}, " +
                $"DATA_TYPE AS {QuoteIdentifier(STOREDPROC_COLUMN_SYSTEMTYPENAME)}, " +
                $"'false' AS {QuoteIdentifier(STOREDPROC_COLUMN_ISNULLABLE)} " +
                $"FROM ALL_ARGUMENTS " +
                $"WHERE UPPER(OWNER) = UPPER(@param0) " +
                $"AND UPPER(OBJECT_NAME) = UPPER(@param1) " +
                $"AND OVERLOAD IS NULL " +
                $"AND IN_OUT IN ('OUT', 'IN/OUT') " +
                $"AND ARGUMENT_NAME IS NOT NULL " +
                $"ORDER BY POSITION";

            return query;
        }

        /// <summary>
        /// Builds an Oracle query that retrieves result set metadata for a subprogram that lives
        /// inside a package. Unlike standalone subprograms (whose arguments appear in ALL_ARGUMENTS
        /// with an empty PACKAGE_NAME column), packaged subprogram arguments are keyed by the
        /// PACKAGE_NAME column and the bare subprogram name in OBJECT_NAME.
        /// Callers must bind <c>@param0</c> (schema), <c>@param1</c> (package, unused when the
        /// package is null/empty) and <c>@param2</c> (subprogram name).
        /// </summary>
        /// <param name="schemaName">Owning schema, e.g. "SYSTEM".</param>
        /// <param name="packageName">Package name, e.g. "PKG_TEST".</param>
        /// <param name="subprogramName">Bare subprogram name within the package, e.g. "GET_BOOKS".</param>
        /// <param name="isFunction">True when the subprogram is a function. A function's result set
        /// is its RETURN value, which appears in ALL_ARGUMENTS as an OUT argument at POSITION 0 with
        /// a NULL ARGUMENT_NAME.</param>
        public string BuildStoredProcedureResultDetailsQuery(
            string schemaName,
            string? packageName,
            string subprogramName,
            bool isFunction,
            string? overload = null)
        {
            // ALL_ARGUMENTS keys a standalone subprogram by PACKAGE_NAME IS NULL and a packaged one
            // by PACKAGE_NAME = <package>. (In Oracle an empty string IS NULL, so a plain equality
            // against an empty package name would match nothing.)
            // Names are bound (never interpolated) so a hostile object name cannot alter the query.
            string packageClause = string.IsNullOrEmpty(packageName)
                ? "PACKAGE_NAME IS NULL"
                : "UPPER(PACKAGE_NAME) = UPPER(@param1)";
            // A null overload means the subprogram is not overloaded. Filtering to one OVERLOAD
            // value keeps packaged overloads from mixing their parameters into one positional list.
            string overloadClause = string.IsNullOrEmpty(overload)
                ? "AND OVERLOAD IS NULL "
                : "AND OVERLOAD = @param3 ";

            string query =
                $"SELECT " +
                $"ARGUMENT_NAME AS {QuoteIdentifier(STOREDPROC_COLUMN_NAME)}, " +
                $"DATA_TYPE AS {QuoteIdentifier(STOREDPROC_COLUMN_SYSTEMTYPENAME)}, " +
                $"'false' AS {QuoteIdentifier(STOREDPROC_COLUMN_ISNULLABLE)} " +
                $"FROM ALL_ARGUMENTS " +
                $"WHERE UPPER(OWNER) = UPPER(@param0) " +
                $"AND {packageClause} " +
                $"AND UPPER(OBJECT_NAME) = UPPER(@param2) " +
                overloadClause +
                (isFunction
                    // A function's result set is its RETURN value (POSITION 0, ARGUMENT_NAME null).
                    ? $"AND POSITION = 0 "
                    // A procedure's result set is its cursor OUT parameter(s).
                    : $"AND IN_OUT IN ('OUT', 'IN/OUT') AND ARGUMENT_NAME IS NOT NULL ") +
                $"ORDER BY POSITION";

            return query;
        }

        /// <inheritdoc/>
        public override string BuildForeignKeyInfoQuery(int numberOfParameters)
        {
            string[] schemaNameParams = CreateParams(kindOfParam: SCHEMA_NAME_PARAM, numberOfParameters);
            string[] tableNameParams = CreateParams(kindOfParam: TABLE_NAME_PARAM, numberOfParameters);

            // Oracle uses :param syntax instead of @param
            string tableSchemaParamsForInClause = string.Join(", :", schemaNameParams);
            string tableNameParamsForInClause = string.Join(", :", tableNameParams);

            // Oracle uses its data dictionary views instead of INFORMATION_SCHEMA
            // ALL_CONSTRAINTS contains constraint information (CONSTRAINT_TYPE = 'R' for foreign keys)
            // ALL_CONS_COLUMNS contains column mappings for constraints
            // R_OWNER and R_CONSTRAINT_NAME reference the parent (unique/primary key) constraint
            string foreignKeyQuery = $@"
                SELECT
                    RefCons.CONSTRAINT_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition))},
                    RefCons.OWNER {QuoteIdentifier($"Referencing{nameof(DatabaseObject.SchemaName)}")},
                    RefCons.TABLE_NAME {QuoteIdentifier($"Referencing{nameof(SourceDefinition)}")},
                    RefConsCol.COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencingColumns))},
                    RefConsPk.OWNER {QuoteIdentifier($"Referenced{nameof(DatabaseObject.SchemaName)}")},
                    RefConsPk.TABLE_NAME {QuoteIdentifier($"Referenced{nameof(SourceDefinition)}")},
                    RefConsPkCol.COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencedColumns))}
                FROM
                    ALL_CONSTRAINTS RefCons
                    INNER JOIN
                    ALL_CONS_COLUMNS RefConsCol
                        ON RefCons.OWNER = RefConsCol.OWNER
                        AND RefCons.CONSTRAINT_NAME = RefConsCol.CONSTRAINT_NAME
                    INNER JOIN
                    ALL_CONSTRAINTS RefConsPk
                        ON RefCons.R_OWNER = RefConsPk.OWNER
                        AND RefCons.R_CONSTRAINT_NAME = RefConsPk.CONSTRAINT_NAME
                    INNER JOIN
                    ALL_CONS_COLUMNS RefConsPkCol
                        ON RefConsPk.OWNER = RefConsPkCol.OWNER
                        AND RefConsPk.CONSTRAINT_NAME = RefConsPkCol.CONSTRAINT_NAME
                        AND RefConsCol.POSITION = RefConsPkCol.POSITION
                WHERE
                    RefCons.CONSTRAINT_TYPE = 'R'
                    AND UPPER(RefCons.OWNER) IN (:{tableSchemaParamsForInClause})
                    AND UPPER(RefCons.TABLE_NAME) IN (:{tableNameParamsForInClause})";

            return foreignKeyQuery;
        }

        /// <inheritdoc/>
        public string BuildQueryToGetReadOnlyColumns(string schemaParamName, string tableParamName)
        {
            // Oracle uses :param instead of @param for bind parameters
            string query = $"SELECT COLUMN_NAME FROM ALL_TAB_COLS " +
                $"WHERE OWNER = :{schemaParamName.TrimStart('@')} AND TABLE_NAME = :{tableParamName.TrimStart('@')} AND VIRTUAL_COLUMN = 'YES'";
            return query;
        }

        /// <summary>
        /// Builds an Oracle query that discovers autoentity tables: user-accessible tables that
        /// have a primary key, filtered by the include/exclude patterns and named per the name
        /// pattern (both using {schema}/{object} placeholders). Include/exclude patterns are comma-
        /// separated SQL LIKE patterns (with ESCAPE '\') matched against "schema.object".
        /// Oracle-maintained system schemas (SYS dictionary base tables and friends) are excluded
        /// so a privileged connection does not materialize hundreds of system entities, mirroring
        /// the system-object filtering the MSSQL builder performs.
        /// Entity names are lowercased so generated REST paths and GraphQL type names stay
        /// stable regardless of catalog folding. Column exposed names are a separate layer
        /// (catalog spelling unless mapped). The returned "schema"/"object" values keep
        /// physical casing for source resolution.
        /// Returns rows aliased as "schema", "object", and "entity_name" (the JSON property names
        /// the metadata provider reads when materializing generated entities).
        /// </summary>
        public string BuildGetAutoentitiesQuery()
        {
            return @"
WITH exclude_patterns AS (
    SELECT TRIM(REGEXP_SUBSTR(:exclude_pattern, '[^,]+', 1, LEVEL)) AS pattern
    FROM dual
    CONNECT BY LEVEL <= REGEXP_COUNT(:exclude_pattern, ',') + 1
        AND TRIM(REGEXP_SUBSTR(:exclude_pattern, '[^,]+', 1, LEVEL)) IS NOT NULL
),
include_patterns AS (
    SELECT TRIM(REGEXP_SUBSTR(:include_pattern, '[^,]+', 1, LEVEL)) AS pattern
    FROM dual
    CONNECT BY LEVEL <= REGEXP_COUNT(:include_pattern, ',') + 1
        AND TRIM(REGEXP_SUBSTR(:include_pattern, '[^,]+', 1, LEVEL)) IS NOT NULL
),
candidate_tables AS (
    SELECT
        t.owner AS schema_name,
        t.table_name AS object_name,
        t.owner || '.' || t.table_name AS full_name
    FROM all_tables t
    WHERE t.owner NOT IN (
              'SYS', 'XDB', 'ORDSYS', 'ORDDATA', 'MDSYS', 'OLAPSYS', 'LBACSYS',
              'DVSYS', 'AUDSYS', 'OJVMSYS', 'CTXSYS', 'WMSYS', 'EXFSYS', 'OUTLN',
              'GSMADMIN_INTERNAL', 'DBSNMP', 'APPQOSSYS', 'FLOWS_FILES')
      AND NOT REGEXP_LIKE(t.owner, '^APEX_[0-9]+')
      AND EXISTS (
        SELECT 1
        FROM all_constraints c
        WHERE c.owner = t.owner
          AND c.table_name = t.table_name
          AND c.constraint_type = 'P'
    )
)
SELECT
    a.schema_name AS ""schema"",
    a.object_name AS ""object"",
    CASE
        WHEN NVL(LENGTH(TRIM(:name_pattern)), 0) = 0 THEN LOWER(a.object_name)
        ELSE REPLACE(REPLACE(:name_pattern, '{schema}', LOWER(a.schema_name)), '{object}', LOWER(a.object_name))
    END AS ""entity_name""
FROM candidate_tables a
WHERE
    (NOT EXISTS (SELECT 1 FROM exclude_patterns)
     OR NOT EXISTS (SELECT 1 FROM exclude_patterns WHERE UPPER(a.full_name) LIKE UPPER(exclude_patterns.pattern) ESCAPE '\'))
    AND
    (NOT EXISTS (SELECT 1 FROM include_patterns)
     OR EXISTS (SELECT 1 FROM include_patterns WHERE UPPER(a.full_name) LIKE UPPER(include_patterns.pattern) ESCAPE '\'))
ORDER BY a.schema_name, a.object_name";
        }

        public string QuoteTableNameAsDBConnectionParam(string param)
        {
            // This value is bound as a DbConnectionParam (e.g. against ALL_TAB_COLS.TABLE_NAME),
            // NOT embedded directly in SQL text. Oracle stores unquoted identifiers in uppercase
            // and ALL_TAB_COLS.TABLE_NAME stores the bare (unquoted, uppercased) name, so the
            // bind value must be bare and uppercased - quoting it here would never match.
            return param.ToUpperInvariant();
        }
    }
}
