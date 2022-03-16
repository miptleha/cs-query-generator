using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Misc
{
    class Util
    {
        public static string ToStr(object val)
        {
            if (val == null || val == DBNull.Value)
                return null;
            return val.ToString();
        }

        public static DateTime ToDate(object val)
        {
            if (val == null || val == DBNull.Value)
                return DateTime.MinValue;
            return (DateTime)val;
        }

        public static DateTime? ToDateNull(object val)
        {
            if (val == null || val == DBNull.Value)
                return null;
            return (DateTime)val;
        }

        internal static void CheckName(XElement r, string name)
        {
            if (r.Name.LocalName != name)
                throw new Exception(string.Format("Неверный корневой элемент в теле сообщения: '{0}'. Ожидается: '{1}'.", r.Name.LocalName, name));
        }

        public static int? ToIntNull(object val)
        {
            if (val == null || val == DBNull.Value)
                return null;
            return ToInt(val);
        }

        public static int ToInt(object val)
        {
            return int.Parse(val.ToString());
        }

        public static long? ToLongNull(object val)
        {
            if (val == null || val == DBNull.Value)
                return null;
            return ToLong(val);
        }

        public static long ToLong(object val)
        {
            return long.Parse(val.ToString());
        }

        public static decimal? ToDecimalNull(object val)
        {
            if (val == null || val == DBNull.Value)
                return null;
            return ToDecimal(val);
        }

        public static decimal ToDecimal(object val)
        {
            if (val is decimal)
                return (decimal)val;
            else if (val is int)
                return (decimal)(int)val;
            else if (val is double)
                return Convert.ToDecimal((double)val);
            else
                throw new Exception("Попытка приведения типа " + val.GetType() + " к decimal");
        }

        public static string ToBase64(object content)
        {
            if (content == null || content == DBNull.Value)
                return null;

            var data = (byte[])content;
            if (data.Length == 0)
                return null;

            return Convert.ToBase64String(data);
        }

        public static byte[] FromBase64(string str)
        {
            if (string.IsNullOrEmpty(str))
                return null;

            byte[] res = null;
            try
            {
                res = Convert.FromBase64String(str);
            }
            catch (Exception ex)
            {
                Log.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType).Debug("Ошибка чтения BASE64: " + ex.Message);
            }

            return res;
        }
    }
}
