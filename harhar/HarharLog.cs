using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Harhar
{
    public class HarharLog
    {
        public string FilePath { get; set; }

        public HarharLog(string filePath)
        {
            FilePath = filePath;
        }

        public void AppendLine(string value, HarharLogMessageTypes messageType)
        {
            var file = File.AppendText(FilePath);
            file.WriteLine("{0} [{1}] : {2}", DateTime.Now.ToString(), messageType.ToString(), value);
            file.Close();
        }
    }
    public enum HarharLogMessageTypes
    {
        Info,
        Warning,
        Error,
        Fatal
    }
}
