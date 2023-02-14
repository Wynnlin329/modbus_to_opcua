using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modbus_to_opcua
{
    public class mappingDictionary
    {
        public List<Dictionary<string, string>> readModbusAddressValueTableList = new List<Dictionary<string, string>>();
        public List<Dictionary<string, string>> mappingModbusAddressValueTableList = new List<Dictionary<string, string>>();
        public List<Dictionary<string, string>> opcuaAndModbusMappingTableList = new List<Dictionary<string, string>>();
    }
}
