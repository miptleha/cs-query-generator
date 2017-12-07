using Log;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Db
{
    /// <summary>
    /// Executes quieries (by names, not by sql).
    /// Fill objects with data for select commands (can load classes with IRow interface implementation).
    /// Logs all query execution, errors
    /// </summary>
    public class DbExecuter
    {
        #region public methods

        /// <summary>
        /// Connection string for each operation
        /// </summary>
        public static string ConnectionString { get; set; }

        /// <summary>
        /// Set true if log only errors, otherwise logs all actions.
        /// This option is also available for some methos (to silent execute only specific queries)
        /// </summary>
        public static bool SilentMode { get; set; }

        /// <summary>
        /// Show warning in log if request executed more than that time in seconds
        /// </summary>
        public static double LongRequestTime = 1;

        /// <summary>
        /// Load all sql-queries from sql folder.
        /// Call this method before all others methods.
        /// </summary>
        public static void Init(string path)
        {
            htFiles.Clear();
            htQueries.Clear();

            _path = path;
            var files = Directory.GetFiles(path, "*.xml");
            foreach (string file in files)
            {
                var dt = File.GetLastWriteTime(file);
                htFiles.Add(file, dt);
            }

            int cnt = 0;
            int n = 0;
            foreach (string file in files)
            {
                cnt += ReadFile(file);
                n++;
            }
            log.Debug(cnt + " sql queries loaded from " + n + " xml files");
        }

        /// <summary>
        /// Search for changes in sql-queries and (if was changes) reloads all sql-queries.
        /// This method can be called periodicaly.
        /// </summary>
        public static void ReloadSql()
        {
            if (string.IsNullOrEmpty(_path))
                throw new Exception("call Init() first");

            bool foundChanges = false;
            foreach (string file in htFiles.Keys)
            {
                var dtOld = htFiles[file];
                var dt = File.GetLastWriteTime(file);
                if (dt != dtOld)
                {
                    foundChanges = true;
                    break;
                }
            }
            if (foundChanges)
                Init(_path);
        }

        /// <summary>
        /// Executes DML block (insert, update, anonymouse block)
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static int Execute(string query, params DbParam[] dbParams)
        {
            return Execute(query, false, true, dbParams);
        }

        /// <summary>
        /// Executes DML block (insert, update, anonymouse block)
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="silentMode">if true, do not output debug information to log file</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static int Execute(string query, bool silentMode, bool showParams, params DbParam[] dbParams)
        {
            int res = 0;
            using (var con = new OracleConnection(ConnectionString))
            {
                con.Open();
                var sw = Stopwatch.StartNew();
                var cmd = con.CreateCommand();
                cmd.CommandText = GetSql(query);
                cmd.Connection = con;
                SetParamsInCmd(cmd, dbParams);

                if (!SilentMode && !silentMode)
                    log.Debug(string.Format("Execute query '{0}'{1}", query, showParams ? ParamText(cmd) : ""));
                try
                {
                    res = cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    log.Debug(string.Format("Error executing query '{0}': {1}\n{2}", query, ex.Message, CmdText(cmd)));
                    throw new Exception(string.Format("Error executing query '{0}': {1}", query, ex.Message));
                }
                GetParamsFromCmd(cmd, dbParams);
                sw.Stop();
                if (!SilentMode && sw.Elapsed.TotalSeconds > LongRequestTime)
                    log.Debug(string.Format("!Long request '{0}'. Rows affected: {1}. Executed in: {2}", query, res, sw.Elapsed.ToString()));
            }
            return res;
        }

        /// <summary>
        /// Executes DML block (insert, update, anonymouse block)
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static object SelectScalar(string query, params DbParam[] dbParams)
        {
            return SelectScalar(query, false, dbParams);
        }

        /// <summary>
        /// Executes DML block (insert, update, anonymouse block)
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="silentMode">if true, do not output debug information to log file</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static object SelectScalar(string query, bool silentMode, params DbParam[] dbParams)
        {
            object res = null;
            using (var con = new OracleConnection(ConnectionString))
            {
                con.Open();
                var sw = Stopwatch.StartNew();
                var cmd = con.CreateCommand();
                cmd.CommandText = GetSql(query);
                cmd.Connection = con;
                SetParamsInCmd(cmd, dbParams);

                if (!SilentMode && !silentMode)
                    log.Debug(string.Format("Execute query '{0}'{1}", query, ParamText(cmd)));
                try
                {
                    res = cmd.ExecuteScalar();
                }
                catch (Exception ex)
                {
                    log.Debug(string.Format("Error executing query '{0}': {1}\n{2}", query, ex.Message, CmdText(cmd)));
                    throw new Exception(string.Format("Error executing query '{0}': {1}", query, ex.Message));
                }
                GetParamsFromCmd(cmd, dbParams);
                sw.Stop();
                if (!SilentMode && sw.Elapsed.TotalSeconds > LongRequestTime)
                    log.Debug(string.Format("!Long request '{0}' for scalar. Read value: '{1}'. Executed in: {2}", query, res, sw.Elapsed.ToString()));
            }
            return res;
        }

        /// <summary>
        /// Selects object from db
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static List<T> Select<T>(string query, params DbParam[] dbParams) where T : IRow, new()
        {
            return Select<T>(query, false, dbParams);
        }

        /// <summary>
        /// Selects object from db
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="silentMode">if true, do not output debug information to log file</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static List<T> Select<T>(string query, bool silentMode, params DbParam[] dbParams) where T : IRow, new()
        {
            var res = new List<T>();
            using (var con = new OracleConnection(ConnectionString))
            {
                con.Open();
                var sw = Stopwatch.StartNew();
                var cmd = con.CreateCommand();
                cmd.CommandText = GetSql(query);
                cmd.Connection = con;
                SetParamsInCmd(cmd, dbParams);

                if (!SilentMode && !silentMode)
                    log.Debug(string.Format("Execute query '{0}'{1}", query, ParamText(cmd)));
                int cnt = 0;
                OracleDataReader r = null;
                try
                {
                    r = cmd.ExecuteReader();
                }
                catch (Exception ex)
                {
                    log.Debug(string.Format("Error executing query '{0}': {1}\n{2}", query, ex.Message, CmdText(cmd)));
                    throw new Exception(string.Format("Error executing query '{0}': {1}", query, ex.Message));
                }
                using (r)
                {
                    var columns = new Dictionary<string, int>();
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        string name = r.GetName(i);
                        if (columns.ContainsKey(name))
                            throw new Exception(string.Format("Duplicate field {0} in query", name));
                        columns.Add(name, i);
                    }

                    while (r.Read())
                    {
                        cnt++;
                        T t = new T();
                        t.Init(r, columns);
                        res.Add(t);
                    }
                }
                sw.Stop();
                if (!SilentMode && sw.Elapsed.TotalSeconds > LongRequestTime)
                    log.Debug(string.Format("!Long request '{0}' for reading {1}. Read {2} rows. Executed in: {3}", query, typeof(T), cnt, sw.Elapsed.ToString()));
            }
            return res;
        }

        /// <summary>
        /// Select only first row from query
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static T SelectRow<T>(string query, params DbParam[] dbParams) where T : IRow, new()
        {
            return SelectRow<T>(query, false, dbParams);
        }

        /// <summary>
        /// Select only first row from query
        /// </summary>
        /// <param name="query">name of the query (not sql text)</param>
        /// <param name="silentMode">if true, do not output debug information to log file</param>
        /// <param name="dbParams">parameters for query</param>
        /// <returns></returns>
        public static T SelectRow<T>(string query, bool silentMode, params DbParam[] dbParams) where T : IRow, new()
        {
            var list = Select<T>(query, silentMode, dbParams);
            return list.Count > 0 ? list[0] : default(T);
        }

        #endregion

        #region private methods

        static ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        static Dictionary<string, DateTime> htFiles = new Dictionary<string, DateTime>();
        static Dictionary<string, string> htQueries = new Dictionary<string, string>();
        static string _path;

        private static int ReadFile(string file)
        {
            var doc = XDocument.Load(file);
            var queries = doc.Root.Elements("sql");
            int cnt = 0;
            foreach (XElement q in queries)
            {
                string name = q.Attribute("name").Value;
                if (htQueries.ContainsKey(name))
                    throw new Exception(string.Format("Duplicate key: '{0}' for query", name));
                htQueries.Add(name, q.Value);
                cnt++;
            }
            return cnt;
        }

        private static string GetSql(string query)
        {
            if (!htQueries.ContainsKey(query))
                throw new Exception(string.Format("Query not found: '{0}'", query));

            return htQueries[query].Replace("\r", "");
        }

        private static string CmdText(OracleCommand cmd)
        {
            string str = cmd.CommandText;
            //foreach (OracleParameter p in cmd.Parameters)
            //{
            //    str += string.Format("\n{0}: {1}", p.ParameterName, p.Value == null || p.Value == DBNull.Value ? "null" : "'" + p.Value + "'");
            //}
            return str;
        }

        private static string ParamText(OracleCommand cmd)
        {
            string str = "";
            foreach (OracleParameter p in cmd.Parameters)
            {
                if (str.Length > 0)
                    str += ", ";

                var pVal = "";
                if (p.Direction == ParameterDirection.Output)
                    pVal = "[out]";
                else if (p.Value == null || p.Value == DBNull.Value)
                    pVal = "null";
                else if (p.Value.ToString().Length > 50)
                    pVal = "...";
                else
                    pVal = "'" + p.Value + "'";

                str += string.Format("{0}={1}", p.ParameterName, pVal);
            }
            return str.Length > 0 ? ": " + str : "";

        }

        private static void GetParamsFromCmd(OracleCommand cmd, params DbParam[] dbParams)
        {
            foreach (var p in dbParams)
            {
                var op = new OracleParameter(p.Name, p.Value);
                if (p.Output)
                {
                    bool found = false;
                    foreach (OracleParameter p1 in cmd.Parameters)
                    {
                        if (p1.ParameterName == p.Name)
                        {
                            p.Value = p1.Value;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        throw new Exception(string.Format("Parameter not found: {0}", p.Name));
                }
            }
        }

        private static void SetParamsInCmd(OracleCommand cmd, params DbParam[] dbParams)
        {
            cmd.BindByName = true;
            foreach (var p in dbParams)
            {
                OracleParameter op = null;
                try
                {
                    op = new OracleParameter(p.Name, p.Value);
                }
                catch (Exception ex)
                {
                    log.Error(string.Format("!!!Error in parameter: '{0}', value: '{1}', type: {2}", p.Name, p.Value, p.Value != null ? p.Value.GetType().ToString() : "null"), ex);
                    throw;
                }
                if (p.Output)
                {
                    op.Size = 1000;
                    op.Direction = ParameterDirection.Output;
                }
                cmd.Parameters.Add(op);
            }
        }

        #endregion
    }
}
