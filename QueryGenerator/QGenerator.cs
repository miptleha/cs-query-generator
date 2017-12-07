using Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueryGenerator
{
    public class QTexts
    {
        public string InsertSql { get; set; }
        public string CreateTablesSql { get; set; }
        public string DropTablesSql { get; set; }
        public string InsertCs { get; set; }
        public string RootTableName { get; set; }
    }

    public class QGeneratorOptions
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public string[] GrantSchemas { get; set; }
        public QField[] AdditionFields { get; set; }

        public static QGeneratorOptions Default
        {
            get
            {
                return new QGeneratorOptions { Namespace = "Foo", ClassName = "Bar", MethodName = "CreateObject" };
            }
        }
    }

    public class QGenerator
    {
        public static QTexts Generate(IQObject obj, QGeneratorOptions opt)
        {
            //collect metadata from object
            var data = new QData();
            obj.StoreInfo(data);

            //generate sql and cs code
            var res = new QTexts();
            res.RootTableName = data.Root.Table.Name;

            //drop table script
            string dt = "";
            foreach (var tab in data.Tables.Values)
            {
                dt += "drop table " + tab.Name + ";\n";
                if (tab.Pk != null)
                    dt += "drop sequence " + tab.Name + "_Seq;\n";
            }
            res.DropTablesSql = dt;

            //create table script
            string ct = "";
            foreach (var tab in data.Tables.Values)
            {
                ct += "-- Create table\ncreate table " + tab.Name + "\n(\n";

                if (tab.Fields.Count == 0)
                    throw new Exception("Table " + tab.Name + " does not contains any fields (except pk and fk)");

                int namesLen = tab.Fields.Max(f => f.FullName.Length + (f.Type == QType.StringMulti ? f.Count.ToString().Length : 0));
                if (tab.Pk != null && tab.Pk.Length > namesLen)
                    namesLen = tab.Pk.Length;
                if (tab.Fk != null && tab.Fk.Length > namesLen)
                    namesLen = tab.Fk.Length;
                var checkUnique = new Dictionary<string, QHierarchy>();
                if (data.Root.Table.Name == tab.Name && opt.AdditionFields != null && opt.AdditionFields.Length > 0)
                {
                    int namesLen2 = opt.AdditionFields.Max(f => f.FullName.Length + (f.Type == QType.StringMulti ? f.Count.ToString().Length : 0));
                    if (namesLen2 > namesLen)
                        namesLen = namesLen2;

                    foreach (var f in opt.AdditionFields)
                    {
                        string ftype = GetType(f);
                        if (f.Type == QType.StringMulti)
                        {
                            for (int i = 0; i < f.Count; i++)
                            {
                                string fname = f.Name + (i + 1).ToString();
                                if (checkUnique.ContainsKey(fname.ToLower()))
                                    throw new Exception("Table " + tab.Name + " has multiple columns with name: '" + fname + " (in addition fields)");
                                checkUnique.Add(fname.ToLower(), new QHierarchy("Addition Field: " + fname, QHType.Member, null));

                                ct += "  " + fname + new string(' ', namesLen - fname.Length + 1) + ftype + ",\n";
                            }
                        }
                        else
                        {
                            string fname = f.Name;
                            if (checkUnique.ContainsKey(fname.ToLower()))
                                throw new Exception("Table " + tab.Name + " has multiple columns with name: '" + fname + " (in addition fields)");
                            checkUnique.Add(fname.ToLower(), new QHierarchy("Addition Field: " + fname, QHType.Member, null));

                            ct += "  " + fname + new string(' ', namesLen - fname.Length + 1) + ftype + ",\n";
                        }
                    }
                }

                if (tab.Pk != null)
                {
                    string fname = tab.Pk;
                    if (checkUnique.ContainsKey(fname.ToLower()))
                        throw new Exception("Table " + tab.Name + " has multiple columns with name: '" + fname + " (in PK field)");
                    checkUnique.Add(fname.ToLower(), new QHierarchy("PK: " + fname, QHType.Member, null));

                    ct += "  " + tab.Pk + new string(' ', namesLen - tab.Pk.Length + 1) + "NUMBER,\n";
                }
                if (tab.Fk != null)
                {
                    string fname = tab.Fk;
                    if (checkUnique.ContainsKey(fname.ToLower()))
                        throw new Exception("Table " + tab.Name + " has multiple columns with name: '" + fname + " (in FK field)");
                    checkUnique.Add(fname.ToLower(), new QHierarchy("FK: " + fname, QHType.Member, null));

                    ct += "  " + tab.Fk + new string(' ', namesLen - tab.Fk.Length + 1) + "NUMBER,\n";
                }
                foreach (var f in tab.Fields)
                {
                    string ftype = GetType(f);
                    if (f.Type == QType.StringMulti)
                    {
                        for (int i = 0; i < f.Count; i++)
                        {
                            string fname = f.FullName + (i + 1).ToString();
                            if (checkUnique.ContainsKey(fname.ToLower()))
                                throw new Exception("Table " + tab.Name + " has multiple columns with name: '" + fname + "' in " + (f.Hierarchy != null ? f.Hierarchy.Path : "parent") + " and " + checkUnique[fname.ToLower()].Path);
                            checkUnique.Add(fname.ToLower(), f.Hierarchy);

                            ct += "  " + fname + new string(' ', namesLen - fname.Length + 1) + ftype + ",\n";
                        }
                    }
                    else
                    {
                        string fname = f.FullName;
                        if (checkUnique.ContainsKey(fname.ToLower()))
                            throw new Exception("Table " + tab.Name + " has multiple columns with name: '" + fname + "' in " + (f.Hierarchy != null ? f.Hierarchy.Path : "parent") + " and " + checkUnique[fname.ToLower()].Path);
                        checkUnique.Add(fname.ToLower(), f.Hierarchy);

                        ct += "  " + fname + new string(' ', namesLen - fname.Length + 1) + ftype + ",\n";
                    }
                }
                ct = ct.TrimEnd('\n', ',');

                ct += "\n);\n-- Add comments to the table\ncomment on table " + tab.Name + " is '" + RemoveSemicolon(tab.Comment) + "';\n-- Add comments to the columns\n";
                if (data.Root.Table.Name == tab.Name && opt.AdditionFields != null && opt.AdditionFields.Length > 0)
                {
                    foreach (var f in opt.AdditionFields)
                    {
                        string ftype = GetType(f);
                        if (f.Type == QType.StringMulti)
                        {
                            for (int i = 0; i < f.Count; i++)
                            {
                                string fname = f.Name + (i + 1).ToString();
                                ct += "comment on column " + tab.Name + "." + fname + " is '" + RemoveSemicolon(f.Comment) + "';\n";
                            }
                        }
                        else
                        {
                            string fname = f.Name;
                            ct += "comment on column " + tab.Name + "." + fname + " is '" + RemoveSemicolon(f.Comment) + "';\n";
                        }
                    }
                }
                if (tab.Pk != null)
                    ct += "comment on column " + tab.Name + "." + tab.Pk + " is '" + RemoveSemicolon(tab.PkComment) + "';\n";
                if (tab.Fk != null)
                    ct += "comment on column " + tab.Name + "." + tab.Fk + " is '" + RemoveSemicolon(tab.FkComment) + "';\n";
                foreach (var f in tab.Fields)
                {
                    if (f.Type == QType.StringMulti)
                    {
                        for (int i = 0; i < f.Count; i++)
                            ct += "comment on column " + tab.Name + "." + f.FullName + (i + 1).ToString() + " is '" + RemoveSemicolon(f.Comment) + "';\n";
                    }
                    else
                    {
                        ct += "comment on column " + tab.Name + "." + f.FullName + " is '" + RemoveSemicolon(f.Comment) + "';\n";
                    }
                }

                if (tab.Pk != null)
                {
                    ct += "-- Create constraints\nalter table " + tab.Name + " add constraint " + tab.Name + "_Pk primary key (" + tab.Pk + ");\n";
                    ct += "-- Create sequence\ncreate sequence " + tab.Name + "_Seq;\n";
                }

                if (opt.GrantSchemas != null && opt.GrantSchemas.Length > 0)
                {
                    ct += "-- Grant priveleges\n";
                    foreach (string schema in opt.GrantSchemas)
                    {
                        ct += "grant select, insert, update, delete on " + tab.Name + " to " + schema + ";\n";
                        if (tab.Pk != null)
                            ct += "grant select, alter on " + tab.Name + "_Seq to " + schema + ";\n";
                    }
                }

                ct += "\n";

            }
            res.CreateTablesSql = ct;

            //insert statement as xml file
            string ins = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n<queries>\n";
            foreach (var tab in data.Tables.Values)
            {
                ins += "  <sql name=\"insert " + tab.Name + "\">insert into " + tab.Name + "(";

                if (data.Root.Table.Name == tab.Name && opt.AdditionFields != null && opt.AdditionFields.Length > 0)
                {
                    foreach (var f in opt.AdditionFields)
                    {
                        if (f.Type == QType.StringMulti)
                        {
                            for (int i = 0; i < f.Count; i++)
                                ins += f.Name + (i + 1).ToString() + ", ";
                        }
                        else
                        {
                            ins += f.Name + ", ";
                        }
                    }
                }
                if (tab.Pk != null)
                    ins += tab.Pk + ", ";
                if (tab.Fk != null)
                    ins += tab.Fk + ", ";
                foreach (var f in tab.Fields)
                {
                    if (f.Type == QType.StringMulti)
                    {
                        for (int i = 0; i < f.Count; i++)
                            ins += f.FullName + (i + 1).ToString() + ", ";
                    }
                    else
                    {
                        ins += f.FullName + ", ";
                    }
                }
                ins = ins.TrimEnd(' ', ',');

                ins += ") values(";
                if (data.Root.Table.Name == tab.Name && opt.AdditionFields != null && opt.AdditionFields.Length > 0)
                {
                    foreach (var f in opt.AdditionFields)
                    {
                        if (f.Type == QType.StringMulti)
                        {
                            for (int i = 0; i < f.Count; i++)
                                ins += ":" + f.Name + (i + 1).ToString() + ", ";
                        }
                        else
                        {
                            ins += ":" + f.Name + ", ";
                        }
                    }
                }
                if (tab.Pk != null)
                    ins += tab.Name + "_Seq.nextval, ";
                if (tab.Fk != null)
                    ins += ":" + tab.Fk + ", ";
                foreach (var f in tab.Fields)
                {
                    if (f.Type == QType.StringMulti)
                    {
                        for (int i = 0; i < f.Count; i++)
                            ins += ":" + f.FullName + (i + 1).ToString() + ", ";
                    }
                    else
                    {
                        ins += ":" + f.FullName + ", ";
                    }
                }
                ins = ins.TrimEnd(' ', ',');
                ins += ")";
                if (tab.Pk != null)
                    ins += " returning " + tab.Pk + " into :" + tab.Pk;
                ins += "</sql>\n";

            }
            ins += "</queries>";
            ins = CheckLineLen(ins);
            res.InsertSql = ins;


            //insert cs
            _fieldCount.Clear(); //check that all fields used ones
            foreach (var t in data.Tables.Values)
            {
                _fieldCount.Add(t.Name, new Dictionary<string, int>());
                var fs = _fieldCount[t.Name];
                foreach (var f in t.Fields)
                    fs.Add(f.FullName, 0);
            }

            string cs = "";
            if (data.Root.Table.Pk != null)
                cs += "var pId = new DbParam { Name = \"" + data.Root.Table.Pk + "\", Output = true };\n";
            AddList(ref cs, data.Root, 0, opt.AdditionFields);

            foreach (var t in data.Tables.Values)
            {
                var fs = _fieldCount[t.Name];
                foreach (var f in t.Fields)
                {
                    var cnt = fs[f.FullName];
                    if (cnt == 0)
                        throw new Exception("Field '" + f.FullName + "' in table '" + t.Name + "' was ignored in cs code building, hierarchy: " + GetPath(f, null));
                    if (cnt > 1)
                        throw new Exception("Field '" + f.FullName + "' in table '" + t.Name + "' was used " + cnt + " times in cs code building, hierarchy: " + GetPath(f, null));
                }
            }

            res.InsertCs =
@"using Db;
using System;
using System.Collections.Generic;

namespace " + (opt.Namespace ?? QGeneratorOptions.Default.Namespace) + @"
{
    public class " + (opt.ClassName ?? QGeneratorOptions.Default.ClassName) + @"
    {
        public void " + (opt.MethodName ?? QGeneratorOptions.Default.MethodName) + @"(" + obj.GetType().FullName + @" obj";

            if (opt.AdditionFields != null && opt.AdditionFields.Length > 0)
            {
                foreach (var f in opt.AdditionFields)
                {
                    string type = null;
                    if (f.Type == QType.Date)
                        type = "DateTime";
                    else if (f.Type == QType.Number)
                        type = "decimal";
                    else if (f.Type == QType.StringMulti)
                        type = "List<string>";
                    else if (f.Type == QType.String || f.Type == QType.StringList)
                        type = "string";
                    else
                        throw new Exception("Type not supported: " + type);

                    res.InsertCs += ", " + type + " " + f.Name;
                }
            }

            res.InsertCs += @")
        {
" + cs + @"
        }
    }
}";

            return res;
        }

        static Dictionary<string, Dictionary<string, int>> _fieldCount = new Dictionary<string, Dictionary<string, int>>();

        static ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static void AddChildrenMember(ref string cs, List<QExtHierarchy> children, int num, QHierarchy p)
        {
            foreach (var c in children)
            {
                if (c.Hierarchy.Type != QHType.Member)
                    continue;
                AddFields(ref cs, c.Fields, num == 0 ? "obj" : "i" + num, p, c.Table);
                AddChildrenMember(ref cs, c.Children, num, p);
            }
        }

        private static void AddChildrenList(ref string cs, List<QExtHierarchy> children, int num, QHierarchy p)
        {
            foreach (var c in children)
            {
                if (c.Hierarchy.Type != QHType.List)
                {
                    AddChildrenList(ref cs, c.Children, num, p);
                    continue;
                }

                cs += "if (" + GetPathList(num == 0 ? "obj" : "i" + num, c.Hierarchy, p) + ")\n";
                cs += "foreach (var i" + (num + 1) + " in " + (num == 0 ? "obj" : "i" + num) + "." + GetPath(c.Hierarchy, p) + ")\n{\n";
                if (c.Table.Pk != null)
                    cs += "    var pId" + (num + 1).ToString() + " = new DbParam { Name = \"" + c.Table.Pk + "\", Output = true };\n";
                AddList(ref cs, c, num + 1);
                cs += "\n}\n";
            }
        }

        private static void AddList(ref string cs, QExtHierarchy c, int num, params QField[] additionFields)
        {
            cs += "DbExecuter.Execute(\"insert " + c.Table.Name + "\", false, false, \n";

            if (additionFields != null)
            {
                foreach (var f in additionFields)
                {
                    if (f.Type == QType.StringMulti)
                    {
                        for (int i = 0; i < f.Count; i++)
                        {
                            cs += "    new DbParam { Name = \"" + f.Name + (i + 1).ToString() + "\", Value = " + f.Name + ".Count > " + i + " ? " + f.Name + "[" + i + "] : null },\n";
                        }
                    }
                    else
                    {
                        cs += "    new DbParam { Name = \"" + f.Name + "\", Value = " + f.Name + " },\n";
                    }
                }
            }

            if (c.Table.Pk != null)
                cs += "    pId" + (num == 0 ? "" : num.ToString()) + ",\n";
            if (c.Table.Fk != null)
                cs += "    new DbParam { Name = \"" + c.Table.Fk + "\", Value = pId" + (num == 1 ? "" : (num - 1).ToString()) + ".Value },\n";

            AddFields(ref cs, c.Fields, num == 0 ? "obj" : "i" + num, c.Hierarchy, c.Table);
            AddChildrenMember(ref cs, c.Children, num, c.Hierarchy);
            cs = cs.TrimEnd('\n', ',');
            cs += ");\n\n";
            AddChildrenList(ref cs, c.Children, num, c.Hierarchy);
        }

        private static void AddFields(ref string cs, List<QField> fields, string obj, QHierarchy p, QTable t)
        {
            foreach (var f in fields)
            {
                string val = obj + (f.NoNameCs ? "" : "." + GetPath(f, p));
                if (f.Type == QType.StringList)
                    val = "string.Join(\", \", " + val + ")";

                string check = GetPathList(obj, f.Hierarchy, p);

                if (f.Type == QType.StringMulti)
                {
                    for (int i = 0; i < f.Count; i++)
                    {
                        string fullVal = string.IsNullOrEmpty(check) ? (val + ".Count > " + i + " ? " + val + "[" + i + "] : null") : (check + " && " + val + ".Count > " + i + " ? (object)" + val + "[" + i + "] : null");
                        cs += "    new DbParam { Name = \"" + f.FullName + (i + 1) + "\", Value = " + fullVal + " },\n";
                    }
                }
                else
                {
                    string fullVal = string.IsNullOrEmpty(check) ? val : check + " ? (object)" + val + " : null";
                    cs += "    new DbParam { Name = \"" + f.FullName + "\", Value = " + fullVal + " },\n";
                }

                _fieldCount[t.Name][f.FullName]++;
            }
        }

        private static string GetPathList(string obj, QHierarchy h, QHierarchy p)
        {
            string path = "";
            while (h != null && h != p)
            {
                path = h.Name + (string.IsNullOrEmpty(path) ? "" : "." + path);
                h = h.Parent;
            }

            var list = path.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            string res = "";
            for (int i = 0; i < list.Length; i++)
            {
                string val = string.Join(".", list, 0, i + 1);
                if (!string.IsNullOrEmpty(res))
                    res += " && ";
                res += obj + "." + val + " != null";
            }
            return res;
        }

        private static string GetPath(QField f, QHierarchy p)
        {
            var prefix = (f.Hierarchy != null ? GetPath(f.Hierarchy, p) : "");
            var name = (string.IsNullOrEmpty(prefix) ? "" : prefix + ".") + (f.NameCs ?? f.Name);
            return name;
        }

        private static string GetPath(QHierarchy h, QHierarchy p)
        {
            string path = "";
            while (h != null && h != p)
            {
                path = h.Name + (string.IsNullOrEmpty(path) ? "" : "." + path);
                h = h.Parent;
            }
            return path;
        }

        private static string RemoveSemicolon(string comment)
        {
            if (comment == null)
                return null;

            return comment.Replace(';', ',').Replace('\'', '"');
        }

        private static string CheckLineLen(string src, int size = 100, string prefix = "    ")
        {
            var lines = src.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string str = lines[i];
                while (true)
                {
                    int p = str.LastIndexOf('\n');
                    if (p == -1)
                        p = 0;
                    if (str.Length - p > size)
                    {
                        int p1 = FindPosByChar(str, p, size, ',', ' ', '(');
                        str = str.Insert(p1, "\n" + prefix);
                    }
                    else
                    {
                        break;
                    }
                }
                lines[i] = str;
            }
            src = string.Join("\n", lines);
            return src;
        }

        private static int FindPosByChar(string str, int p, int size, params char[] cs)
        {
            string str1 = str.Substring(p, size);
            foreach (var c in cs)
            {
                int p1 = str1.LastIndexOf(',');
                if (p1 <= 0)
                    continue;
                return p + p1 + 1;
            }
            return p + size;
        }

        private static string GetType(QField f)
        {
            string ftype = null;
            if (f.Type == QType.Date)
            {
                ftype = "DATE";
            }
            else if (f.Type == QType.Number)
            {
                ftype = "NUMBER";
                if (f.Size != 0)
                    ftype += "(" + f.Size + ")";
            }
            else if (f.Type == QType.String || f.Type == QType.StringList || f.Type == QType.StringMulti)
            {
                if (f.Type == QType.StringMulti && f.Count <= 0)
                    throw new Exception("Count must be positive for StringMulti type");

                ftype = "VARCHAR2";
                if (f.Size != 0)
                    ftype += "(" + f.Size + ")";
                else
                    throw new Exception("Size not set for string column: '" + f.FullName + "', hierarchy: " + GetPath(f, null));
            }
            else
            {
                throw new Exception("Type not supported: " + f.Type);
            }

            if (!string.IsNullOrEmpty(f.Default))
                ftype += " default " + f.Default;
            if (f.NotNull)
                ftype += " not null";

            return ftype;
        }
    }
}
