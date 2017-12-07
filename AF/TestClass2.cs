using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueryGenerator.AF
{
    /** Sample of container for other class */
    public class TestClass2 : IQObject
    {
        List<TestClass1> _listValue = new List<TestClass1>();

        public List<TestClass1> ListValue { get { return _listValue; } }
        public TestClass1 SingleValue { get; set; }
        public string OwnValue { get; set; }

        public void StoreInfo(QData data)
        {
            //root table (one row for one object)
            var qt = new QTable { Name = "testclass2_table", Comment = "Stores TestClass2 content", Pk = "id", PkComment = "Identity" };

            //for list member need to create separate table
            var qt_child = new QTable { Name = "testclass2_table_sub", Comment = "Stores TestClass1 content", Pk = "id", PkComment = "Identity", Fk = "id1", FkComment = "Reference to parent row in testclass2_table" };
            TestClass1.StoreInfo(qt_child, new QHierarchy("ListValue", QHType.List, null), data);

            //single value can be stored in root table
            TestClass1.StoreInfo(qt, new QHierarchy("SingleValue", QHType.Member, null), data);

            //save own properties in root table
            data.AddInfo(qt, null,
                new QField { Name = "OwnValue", Comment = "Primitive type in TestClass2", Type = QType.String, Size = 250 });
        }
    }
}
