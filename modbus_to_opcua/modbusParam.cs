using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
//using NModbus;
using Modbus.Device;

namespace modbus_to_opcua
{
    public class ModbusParam
    {
        public IModbusMaster modbusClient { get; set; }
        public TcpClient modbusTCPClient { get; set; }
        public string ipAddress { get; set; }
        public int port { get; set; }
        public int id { get; set; }
        public int functionCode { get; set; }
        public int register { get; set; }
        public string type { get; set; }
        public int length { get; set; }
    }
}
