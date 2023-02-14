using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using Opc.UaFx.Client;
//using Opc.UaFx;
using OpcUaHelper;

namespace modbus_to_opcua
{
    public class opcuaConnParam
    {
        public OpcUaClient opcClient { get; set; }
        public string account { get; set; }
        public string password { get; set; }
    }
    public class opcuaWriteParam
    {
        public List<string> nodeId { get; set; }
        public List<object> value { get; set; }
    }

}
