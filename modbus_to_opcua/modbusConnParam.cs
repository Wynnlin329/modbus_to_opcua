using EasyModbus;
using Newtonsoft.Json.Linq;
//using NModbus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Modbus.Device;

namespace modbus_to_opcua
{
    public class modbusConnParam
    {
        public List<ModbusParam> modbusParamList = new List<ModbusParam>();

        public List<List<JToken>> modbusMappingData = new List<List<JToken>>();

        public List<batchReadParam> batchReadParam = new List<batchReadParam>();

        public List<Dictionary<int, List<JToken>>> sortedMappingData = new List<Dictionary<int, List<JToken>>>();

    }
}
