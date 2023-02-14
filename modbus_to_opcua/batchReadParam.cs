using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modbus_to_opcua
{
    public class batchReadParam
    {
        public List<int> id  = new List<int>();
        public List<int> functionCode = new List<int>();
        public List<int> startAddress = new List<int>();
        public List<int> dataLength = new List<int>();
    }
}
