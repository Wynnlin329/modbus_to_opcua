using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace modbus_to_opcua
{
    public class insertOPC
    {
        public string opcuaTag { get; set; }
        public string modbusRegisters {get; set; }
        public string functionCode { get; set; }
        public string modbusValue { get; set; }
        public string type { get; set; }
    }

    public class insertPLC
    {
        public string modbusIP { get; set; }
        public string modbusRegisters { get; set; }
        public string functionCode { get; set; }
        public string modbusValue { get; set; }
        public string ID { get; set; }
    }
}
