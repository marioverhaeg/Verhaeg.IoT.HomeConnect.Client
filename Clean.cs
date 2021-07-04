using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Verhaeg.IoT.HomeConnect.Client
{
    public class Clean
    {
        public static string DottedString(string str)
        {
            // Remove all characters before the last "." in the string
            while (str.Contains("."))
            {
                str = str.Remove(0, str.IndexOf(".") + 1);
            }
            return str;
        }
    }
}
