using Db;
using Log;
using Microsoft.CSharp;
using Oracle.ManagedDataAccess.Client;
using QueryGenerator.AF;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace QueryGenerator
{
    public class Program
    {
        class FileDesc
        {
            public FileDesc(string name, string encoding)
            {
                this.Name = name;
                this.Encoding = encoding;
            }

            public string Name { get; set; }
            public string Encoding { get; set; }
        }

        class GeneratorData
        {
            public string Name { get; set; }
            public IQObject TestObj { get; set; }
        }

        static void Main(string[] args)
        {
            try
            {
                LogManager.Init();
                log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
                DbExecuter.ConnectionString = ConfigurationManager.ConnectionStrings["DbConnection"].ConnectionString;

                //--- set names, settings and objects here
                var dt = DateTime.Now;
                dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second); //milliseconds not stored in db
                var testObject1 = new TestClass1 { TextValue = "haha", NumberValue = 123, DateValue = dt };
                var testObject11 = new TestClass1 { TextValue = "haha1", NumberValue = 1231, DateValue = dt.AddDays(1) };
                var testObject12 = new TestClass1 { TextValue = "haha2", NumberValue = 1233, DateValue = dt.AddSeconds(1) };
                var testObject13 = new TestClass1 { TextValue = "haha3", NumberValue = 1234, DateValue = dt.AddYears(1) };
                var testObject2 = new TestClass2 { SingleValue = testObject13, OwnValue = "own" };
                testObject2.ListValue.AddRange(new TestClass1[] { testObject11, testObject12 });

                var srcData = new GeneratorData[]
                {
                    new GeneratorData { Name = "TestClass1", TestObj = testObject1 },
                    new GeneratorData { Name = "TestClass2", TestObj = testObject2 }
                };

                string nspace = "SampleProgram.Test";
                string classNameSuffix = "Manager";
                string methodNameInsert = "SaveObject";
                string methodNameSelect = "LoadObject";
                QField[] additionFields = new QField[] { new QField { Name = "EnvelopeId", Type = QType.String, Size = 36, Comment = "Some guid" } };
                string[] grantSchemas = null;
                bool ignoreCsError = true;


                //---

                log.Debug("Clear code folder");
                if (!Directory.Exists(CodePath))
                    Directory.CreateDirectory(CodePath);
                foreach (var f in Directory.GetFiles(CodePath))
                    File.Delete(f);

                var sw = Stopwatch.StartNew();
                foreach (var s in srcData)
                {
                    var opt = new QGeneratorOptions
                    {
                        Namespace = nspace,
                        ClassName = s.Name + classNameSuffix,
                        MethodNameSelect = methodNameSelect,
                        MethodNameInsert = methodNameInsert,
                        AdditionFields = additionFields,
                        GrantSchemas = grantSchemas
                    };

                    log.Debug("Generate code...");
                    var data = QGenerator.Generate(s.TestObj, opt);

                    var files = new FileDesc[]
                    {
                        new FileDesc(s.Name + ".sql", "windows-1251"), //set your database encoding here
                        new FileDesc(s.Name + " drop.sql", "windows-1251"),
                        new FileDesc(s.Name + ".xml", "utf-8"),
                        new FileDesc(s.Name + "Manager.cs", "utf-8")
                    };

                    SaveFile("table creation", files[0].Name, files[0].Encoding, data.CreateTablesSql);
                    SaveFile("table drop", files[1].Name, files[1].Encoding, data.DropTablesSql);
                    SaveFile("insert statements", files[2].Name, files[2].Encoding, data.InsertSql);
                    SaveFile("insert cs", files[3].Name, files[3].Encoding, data.InsertCs);

                    TestGeneratedFiles(opt, files, s.Name, s.TestObj, ignoreCsError, data.RootTableName);
                }
                sw.Stop();

                log.Debug("All generation done, time elapsed: " + sw.Elapsed.ToString());
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            Console.WriteLine("\nTo many text for console?\nFull log here: bin\\Debug\\QueryGenerator.log\n");
        }

        static ILog log;

        private static void TestGeneratedFiles(QGeneratorOptions opt, FileDesc[] files, string name, object obj, bool ignoreCsError, string rootTableName)
        {
            log.Debug("Drop tables...");
            string content = ReadFile(files[1].Name, files[1].Encoding, CodePath);
            var lines = content.Split(';');
            foreach (var l in lines)
            {
                using (var con = new OracleConnection(DbExecuter.ConnectionString))
                {
                    con.Open();
                    var cmd = con.CreateCommand();
                    cmd.CommandText = l.TrimEnd('\n', ';');

                    try
                    {
                        cmd.ExecuteNonQuery();
                    }
                    catch
                    {
                        //drop is optional, tables may not exists
                    }
                }
            }

            log.Debug("Create tables...");
            content = ReadFile(files[0].Name, files[0].Encoding, CodePath);
            content = Regex.Replace(content, @"/\*(.*?)*/", "", RegexOptions.Singleline);
            lines = content.Split(';');
            foreach (var l in lines)
            {
                using (var con = new OracleConnection(DbExecuter.ConnectionString))
                {
                    con.Open();
                    var cmd = con.CreateCommand();
                    var text = l.TrimEnd('\n', ';');
                    var text2 = Regex.Replace(text, "--.*?(\n|$)", "");
                    if (text2.Trim().Length == 0)
                        continue;
                    cmd.CommandText = text;

                    try
                    {
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception)
                    {
                        log.Debug("!!!Error execute command:\n" + cmd.CommandText);
                        throw;
                    }
                }
            }

            //load generated sql insert scripts (scan all xml files)
            log.Debug("Init DbExecuter...");
            DbExecuter.Init(CodePath);

            //execute generated cs code
            log.Debug("Load cs code...");
            content = ReadFile(files[3].Name, files[3].Encoding, CodePath);
            var o = LoadCode(content, (opt.Namespace ?? QGeneratorOptions.Default.Namespace) + "." + (opt.ClassName ?? QGeneratorOptions.Default.ClassName));
            MethodInfo miIns = o.GetType().GetMethod(opt.MethodNameInsert ?? QGeneratorOptions.Default.MethodNameInsert);
            MethodInfo miSel = o.GetType().GetMethod(opt.MethodNameSelect ?? QGeneratorOptions.Default.MethodNameSelect);
            log.Debug("Execute cs code...");
            var p = new List<object>();
            p.Add(obj);
            if (opt.AdditionFields != null && opt.AdditionFields.Length > 0)
            {
                for (int i = 0; i < opt.AdditionFields.Length; i++)
                {
                    var f = opt.AdditionFields[i];
                    object val = null;
                    if (f.Type == QType.Date)
                        val = new DateTime();
                    else if (f.Type == QType.Number)
                        val = i;
                    else if (f.Type == QType.String || f.Type == QType.StringList)
                        val = "z" + i.ToString();
                    else if (f.Type == QType.StringMulti)
                        val = new List<string> { "z1", "z2" };
                    else
                        throw new Exception("Type not supported: " + f.Type);
                    p.Add(val);
                }
            }

            try
            {
                log.Debug("insert...");
                miIns.Invoke(o, p.ToArray());

            }
            catch (Exception ex)
            {
                if (ignoreCsError)
                    log.Debug("Error execute insert code: " + ex.Message);
                else
                    throw;
            }

            object obj2 = null;
            try
            {
                log.Debug("select...");
                var t = obj.GetType();
                var pr = t.GetProperty("Id");
                decimal id = (decimal)pr.GetValue(obj, null);
                obj2 = miSel.Invoke(o, new object[] { id });
            }
            catch (Exception ex)
            {
                if (ignoreCsError)
                    log.Debug("Error execute insert code: " + ex.Message);
                else
                    throw;
            }

            log.Debug("diff: " + ObjCompare.Comparer.Compare(obj, obj2));

            //static compilation: add generated code (managers) to project root and uncomment code below
            //if (opt.ClassName == "TestClass1Manager")
            //{
            //    log.Debug("static test");
            //    var m = new SampleProgram.Test.TestClass1Manager();
            //    var test1 = (TestClass1)obj;
            //    m.SaveObject(test1, "qqqq");
            //    var test2 = m.LoadObject(test1.Id);
            //    log.Debug("diff: " + ObjCompare.Comparer.Compare(test1, test2));
            //}
            //else if (opt.ClassName == "TestClass2Manager")
            //{
            //    log.Debug("static test");
            //    var m = new SampleProgram.Test.TestClass2Manager();
            //    var test1 = (TestClass2)obj;
            //    m.SaveObject(test1, "qqqq");
            //    var test2 = m.LoadObject(test1.Id);
            //    log.Debug("diff: " + ObjCompare.Comparer.Compare(test1, test2));
            //}

            log.Debug(@"
++++++ Scripts and code tested for type: " + name + @"
");
        }

        private static object LoadCode(string content, string cls)
        {
            string source = content;

            Dictionary<string, string> providerOptions = new Dictionary<string, string>
                {
                    {"CompilerVersion", "v4.0"}
                };
            CSharpCodeProvider provider = new CSharpCodeProvider(providerOptions);


            string exePath = Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exePath);

            AssemblyName[] assemRefs = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
            List<string> references = new List<string>();

            foreach (AssemblyName assemblyName in assemRefs)
                references.Add(assemblyName.Name + ".dll");

            for (int i = 0; i < references.Count; i++)
            {
                string localName = Path.Combine(exeDir, references[i]);

                if (File.Exists(localName))
                    references[i] = localName;
            }

            references.Add(exePath);

            CompilerParameters compilerParams = new CompilerParameters(references.ToArray());

            CompilerResults results = provider.CompileAssemblyFromSource(compilerParams, source);

            if (results.Errors.Count != 0)
            {
                for (int i = 0; i < results.Errors.Count && i < 10; i++)
                {
                    CompilerError CompErr = results.Errors[i];
                    log.Debug("Line number " + CompErr.Line +
                        ", Error Number: " + CompErr.ErrorNumber +
                        ", '" + CompErr.ErrorText + ";" +
                        Environment.NewLine);
                }
                throw new Exception("Error executing generated cs file, found " + results.Errors.Count + " errors!");
            }

            object o = results.CompiledAssembly.CreateInstance(cls);
            return o;
        }

        private static string ReadFile(string file, string encoding, string path = null)
        {
            if (path == null)
                path = ExePath;

            string content;
            using (StreamReader sr = new StreamReader(Path.Combine(path, file), Encoding.GetEncoding(encoding)))
            {
                content = sr.ReadToEnd();
            }
            return content;
        }

        private static void SaveFile(string info, string file, string encoding, string statement)
        {
            string ctFile = Path.Combine(CodePath, file);
            using (StreamWriter sw = new StreamWriter(ctFile, false, Encoding.GetEncoding(encoding)))
            {
                sw.Write(statement);
            }
            log.Debug(info + " saved as: " + ctFile);
        }

        static string _exePath;
        private static string ExePath
        {
            get
            {
                if (_exePath == null)
                    _exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return _exePath;
            }
        }

        static string _codePath;
        private static string CodePath
        {
            get
            {
                if (_codePath == null)
                    _codePath = Path.Combine(ExePath, "code");
                return _codePath;
            }
        }
    }
}
