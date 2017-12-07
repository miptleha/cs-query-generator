using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueryGenerator.AF
{
    /** Simple class for generator with different types of fields */
    public class TestClass1 : IQObject
    {
        public string TextValue { get; set; }
        public decimal? NumberValue { get; set; }
        public DateTime? DateValue { get; set; }

        public void StoreInfo(QData data)
        {
            //description of sql table
            var qt = new QTable { Name = "testclass1_table", Comment = "Stores TestClass1 content", Pk = "id", PkComment = "Identity" };

            //description in what sql fields store class properties
            StoreInfo(qt, null, data);
        }

        //create static method if class used as data in other class (for example TestClass2 contains member of TestClass1)
        public static void StoreInfo(QTable qt, QHierarchy h, QData data)
        {
            data.AddInfo(qt, h,
                new QField { Name = "txt", NameCs = "TextValue", Comment = "Text field", Type = QType.String, Size = 100 },
                new QField { Name = "num", NameCs = "NumberValue", Comment = "Number field", Type = QType.Number },
                new QField { Name = "dt", NameCs = "DateValue", Comment = "Date field", Type = QType.Date });
        }

    }
}
