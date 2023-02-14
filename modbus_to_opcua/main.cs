//using EasyModbus;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Timers;
using Modbus.Device;
using Modbus.IO;
using System.Diagnostics;
using OpcUaHelper;
using Opc.Ua.Client;
using System.Globalization;
using Opc.Ua;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace modbus_to_opcua
{
    public class main
    {
        //string logAddress = @"D:\log\";
        //string opcuaUrl = "opc.tcp://AA2201503-NB.corpnet.asus:53530/OPCUA/SimulationServer";
        string opcuaUrl = "opc.tcp://10.95.129.2:53530/OPCUA/SimulationServer";
        bool modbusReconnectFlag = true;
        int readModbusIntervalTime = 1000;
        int modbusConnInterval = 1000;
        JObject json = new JObject();
        JObject insertOPC = new JObject();
        JObject insertOPCTemp = new JObject();
        JObject insertPLC = new JObject();
        List<JToken> modbusMappingList = new List<JToken>();
        List<JToken> backControlMappingList = new List<JToken>();
        Dictionary<string, IModbusMaster> ipAndModbusClientPair = new Dictionary<string, IModbusMaster>();
        modbusConnParam modbusConnParam = new modbusConnParam();
        opcuaConnParam opcuaConnection = new opcuaConnParam();
        Logger logger = LogManager.GetCurrentClassLogger();
        System.Timers.Timer ProcessTimer = new System.Timers.Timer();
        System.Timers.Timer modbusConnTimer = new System.Timers.Timer();
        Stopwatch stopWatch = new Stopwatch();
        OpcUaClient m_OpcUaClient = new OpcUaClient();
        private object locker = new Object();
        public void ProgramStart()
        {
            mappingDictionary mappingDictionary = new mappingDictionary();
            Dictionary<string, string> ipAndPortPair = new Dictionary<string, string>();
            Dictionary<int, List<JToken>> sortedMappingData = new Dictionary<int, List<JToken>>();
            Dictionary<string, string> readModbusAddressValueTable = new Dictionary<string, string>();
            
            CreateInsertOpcJson();
            
            OpcuaServerConnect();
            ipAndPortPair = DeviceConnectInfoCollection(this.modbusMappingList);
            this.modbusConnParam =  MappingDataClassification(ipAndPortPair, this.modbusMappingList);

            //Data Preprocessing-------------------------------------------------
            SortMappingDataToDict(this.modbusConnParam);
            //再把Dict<functionCode,mappingData>帶入BatchReadPreprocess
            BatchReadPreprocess(this.modbusConnParam);
            //建立Dict<addr,null>
            CreateMappingValueCompareDict(this.modbusConnParam, mappingDictionary);
            //-------------------------------------------------------------------
            SetProcessTimer(this.modbusConnParam, mappingDictionary);
            OpcuaWriteToPLCSystem();
            //OpcuaTagSubscribe();

            
            //Modbus_Test();


        }

        
        public void CreateInsertOpcJson()
        {
            string nodeID = string.Empty;
            string previousNodeID = string.Empty;
            string modbusRegisters = string.Empty;
            string previousModbusRegisters = string.Empty;
            //this.json = ExcelHelper.ExcelToJson("mappingTable.xlsx");
            this.json = JObject.Parse(File.ReadAllText("mappingTable.json"));
            foreach (var mappingList in json)
            {
                if(mappingList.Key == "MappingTable")
                {
                    foreach (var mapping in mappingList.Value)
                    {


                        //建立對應表list
                        this.modbusMappingList.Add(mapping);
                        nodeID = mapping["NodeID"].ToString();
                        if (nodeID == previousNodeID)//避免json有重複的key
                        {
                            this.insertOPC[previousNodeID]["modbusRegisters"] = this.insertOPC[previousNodeID]["modbusRegisters"] + "," + mapping["Registers"].ToString();

                        }
                        else
                        {
                            JProperty insertOPCTemp = new JProperty(mapping["NodeID"].ToString(),
                            JProperty.FromObject(
                                new insertOPC
                                {
                                    opcuaTag = mapping["NodeID"].ToString(),
                                    modbusRegisters = mapping["Registers"].ToString(),
                                    functionCode = mapping["FunctionCode"].ToString(),
                                    modbusValue = null,
                                    type = mapping["OPCUAType"].ToString()
                                }));
                            this.insertOPC.Add(insertOPCTemp);
                            this.insertOPCTemp.Add(insertOPCTemp);
                        }
                        previousNodeID = nodeID;
                        //建立要存入opcua的Json
                    }
                }
            }
            //this.insertOPC["ns=3;s=A"]["modbusValue"] = 50.ToString();
            //this.insertOPC["ns=3;s=B"]["modbusValue"] = 49.ToString();
            //this.insertOPCTemp = this.insertOPC;
        }
        public void OpcuaServerConnect()
        {
            if (this.m_OpcUaClient.Connected != true)
            {
                try
                {
                    this.m_OpcUaClient.Disconnect();
                    Thread.Sleep(100);
                    //this.m_OpcUaClient.ConnectServer(this.opcuaUrl);
                    this.m_OpcUaClient.ConnectServer(this.modbusMappingList[0].SelectToken("OpcuaTcp").ToString());
                    Thread.Sleep(100);
                    ModbusToOpcuaTagSubscribe();
                    OpcuaWriteToPLCSystemOpcuaTagSubscribe();
                    Console.WriteLine("OPCUA Server is Connected");
                }
                catch (Exception e)
                {
                    Console.WriteLine("OPCUA Server Connection Fail");
                }
            }













            //if (this.opcClient.State != OpcClientState.Connected)
            //{
            //    try
            //    {
            //        this.opcClient.SessionTimeout = 333333333;
            //        this.opcClient.OperationTimeout = 3000;
            //        this.opcClient.ReconnectTimeout = 3000;
            //        this.opcClient.Disconnect();
            //        Thread.Sleep(100);
            //        this.opcClient.Connect();
            //        Thread.Sleep(100);
            //        //OpcuaTagSubscribe();
            //        Console.WriteLine("OPCUA Server is Connected");
            //    }
            //    catch (Exception e)
            //    {
            //        Console.WriteLine("OPCUA Server Connection Fail");
            //    }
            //}
        }
        public Dictionary<string, string> DeviceConnectInfoCollection(List<JToken> modbusMappingList)
        {
            //之後可以用appSetting查表，來過濾ip跟port的組數
            Dictionary<string, string> ipAndPortPair = new Dictionary<string, string>();
            int count = 0;
            string ipAndPortConcat = string.Empty;
            string previousIP = string.Empty;
            string previousPort = string.Empty;
            foreach (var mappingData in modbusMappingList)
            {
                if (mappingData["ModbusIP"].ToString() != previousIP || mappingData["ModbusPort"].ToString() != previousPort)
                {
                    ipAndPortConcat = string.Format("{0},{1}", mappingData["ModbusIP"].ToString(), mappingData["ModbusPort"]).ToString();
                    ipAndPortPair[count.ToString()] = ipAndPortConcat;
                    previousIP = mappingData["ModbusIP"].ToString();
                    previousPort = mappingData["ModbusPort"].ToString();
                    count++;
                }
            }
            return ipAndPortPair;
        }
        public modbusConnParam MappingDataClassification(Dictionary<string, string> ipAndPortPair, List<JToken> modbusMappingList)
        {
            //多IP連線問題要解決
            modbusConnParam modbusConnParam = new modbusConnParam();
            foreach (var modbusConnItem in ipAndPortPair)
            {
                List<JToken> mappingDataTMP = new List<JToken>();
                ModbusParam modbusParam = new ModbusParam();
                ////var factory = new ModbusFactory();
                //TcpClient masterTcpClient = new TcpClient(modbusConnItem.Value.Split(",")[0].ToString(), Convert.ToInt16(modbusConnItem.Value.Split(",")[1]));
                //IModbusMaster modbusClient = ModbusIpMaster.CreateIp(masterTcpClient);
                //modbusParam.modbusClient = modbusClient;
                //modbusParam.modbusTCPClient = masterTcpClient;
                modbusParam.ipAddress = modbusConnItem.Value.Split(",")[0];
                modbusParam.port = Convert.ToInt16(modbusConnItem.Value.Split(",")[1]);
                modbusParam = modbusReConnect("MappingDataClassification", modbusParam);
                modbusConnParam.modbusParamList.Add(modbusParam);
                foreach (var mappingData in modbusMappingList)
                {
                    //將對應IP跟Port的資料分別存入list
                    if (mappingData["ModbusIP"].ToString() == modbusParam.ipAddress && mappingData["ModbusPort"].ToString() == modbusParam.port.ToString())
                    {
                        mappingDataTMP.Add(mappingData);
                    }
                }
                modbusConnParam.modbusMappingData.Add(mappingDataTMP);
            }
            //ushort[] readHoldingRegisters = modbusConnParam.modbusClientList[0].ReadHoldingRegisters(1, 0, 125);
            //ushort[] readHoldingRegisterss = modbusConnParam.modbusClientList[1].ReadHoldingRegisters(1, 0, 5);
            return modbusConnParam;

        }
        public void SortMappingDataToDict(modbusConnParam modbusConnParam)
        {
            int count = 0;


            //建立Dict<functionCode,mappingData>
            //把mappingData Sort 存進Dict<functionCode,mappingData>

            foreach (var mappingDataList in modbusConnParam.modbusMappingData)
            {
                Dictionary<int, List<JToken>> sortedMappingData = new Dictionary<int, List<JToken>>();
                List<JToken> coilData = new List<JToken>();
                List<JToken> inputData = new List<JToken>();
                List<JToken> holddingRegisterData = new List<JToken>();
                List<JToken> InputRegisterData = new List<JToken>();
                for (int i = 0; i < mappingDataList.Count; i++)
                {
                    string functionCode = mappingDataList[i]["FunctionCode"].ToString();
                    switch (functionCode)
                    {
                        case "1":
                            coilData.Add(mappingDataList[i]);
                            break;
                        case "2":
                            inputData.Add(mappingDataList[i]);
                            break;
                        case "3":
                            holddingRegisterData.Add(mappingDataList[i]);
                            break;
                        case "4":
                            InputRegisterData.Add(mappingDataList[i]);
                            break;
                        default:
                            break;
                    }
                }
                modbusConnParam.sortedMappingData.Add(sortedMappingData);
                //sort 全部 List<JToken>
                if (holddingRegisterData.Count() != 0)
                {
                    modbusConnParam.sortedMappingData[count][3] = holddingRegisterData.OrderBy(x => (int)x.SelectToken("Registers")).ToList();
                }
                if (coilData.Count() != 0)
                {
                    modbusConnParam.sortedMappingData[count][1] = coilData.OrderBy(x => (int)x.SelectToken("Registers")).ToList();
                }
                if (inputData.Count() != 0)
                {
                    modbusConnParam.sortedMappingData[count][2] = inputData.OrderBy(x => (int)x.SelectToken("Registers")).ToList();
                }
                if (InputRegisterData.Count() != 0)
                {
                    modbusConnParam.sortedMappingData[count][4] = InputRegisterData.OrderBy(x => (int)x.SelectToken("Registers")).ToList();
                }
                count++;
            }
        }
        public void BatchReadPreprocess(modbusConnParam modbusConnParam)
        {
            int maxSize = 125;
            int batchReadIntervalLimit = 50;
            for (int modbusMasterCount = 0; modbusMasterCount < modbusConnParam.modbusParamList.Count(); modbusMasterCount++)
            {
                int previousAddress = 0;
                int afterAddress = 0;
                int maxValue;
                int minValue;
                int registerAddress = 0;
                batchReadParam batchReadParam = new batchReadParam();
                Dictionary<int, List<JToken>> modbusDataDict = modbusConnParam.sortedMappingData[modbusMasterCount];
                foreach (var modbusData in modbusDataDict)
                {
                    int startAddress = 0;
                    int endAddress = 0;
                    int dataLength = 0;
                    int countNumber = 1;
                    int otherLength = 0;
                    batchReadParam batchReadParamTMP = new batchReadParam();
                    //endAddress = Convert.ToInt16(modbusData.Value.Last().SelectToken("Registers"));
                    foreach (var modbusDataJtoken in modbusData.Value)
                    {
                        registerAddress = (int)modbusDataJtoken.SelectToken("Registers");

                        if ((int)modbusData.Value.First().SelectToken("Registers") == registerAddress)
                        {
                            startAddress = registerAddress;
                        }
                        else
                        {
                            if (registerAddress - startAddress >= maxSize || registerAddress - endAddress >= batchReadIntervalLimit)
                            {
                                dataLength = endAddress - startAddress + countNumber;
                                batchReadParam = GetBatchReadParam(batchReadParam, (int)modbusData.Value[0]["ID"], modbusData.Key, startAddress, dataLength);
                                startAddress = registerAddress;
                            }
                        }
                        endAddress = registerAddress;
                    }
                    dataLength = endAddress - startAddress + countNumber;
                    batchReadParam = GetBatchReadParam(batchReadParam, (int)modbusData.Value[0]["ID"], modbusData.Key, startAddress, dataLength);
                }
                modbusConnParam.batchReadParam.Add(batchReadParam);
            }
        }
        public void CreateMappingValueCompareDict(modbusConnParam modbusConnParam, mappingDictionary mappingDictionary)
        {

            foreach (var item in modbusConnParam.batchReadParam)
            {
                Dictionary<string, string> readModbusAddressValueTable = new Dictionary<string, string>();
                for (int i = 0; i < item.functionCode.Count(); i++)
                {
                    for (int g = 0; g < item.dataLength[i]; g++)
                    {
                        readModbusAddressValueTable[item.functionCode[i] + "," + (item.startAddress[i] + g).ToString()] = string.Empty;
                    }
                }
                mappingDictionary.readModbusAddressValueTableList.Add(readModbusAddressValueTable);
            }

            foreach (var item in modbusConnParam.modbusMappingData)
            {
                Dictionary<string, string> mappingModbusAddressValueTable = new Dictionary<string, string>();
                Dictionary<string, string> opcuaAndModbusMappingTable = new Dictionary<string, string>();
                for (int i = 0; i < item.Count(); i++)
                {
                    foreach (var items in item)
                    {
                        mappingModbusAddressValueTable[items["FunctionCode"].ToString() + "," + items["Registers"].ToString()] = string.Empty;
                    }
                }
                mappingDictionary.mappingModbusAddressValueTableList.Add(mappingModbusAddressValueTable);

                for (int i = 0; i < item.Count(); i++)
                {
                    foreach (var items in item)
                    {
                        opcuaAndModbusMappingTable[items["FunctionCode"].ToString() + "," + items["Registers"].ToString()] = items["NodeID"].ToString();
                    }
                }
                mappingDictionary.opcuaAndModbusMappingTableList.Add(opcuaAndModbusMappingTable);
            }
        }
        public void SetProcessTimer(modbusConnParam modbusConnParam, mappingDictionary mappingDictionary)
        {
            this.ProcessTimer.Interval = 1000;
            this.ProcessTimer.AutoReset = false;
            this.ProcessTimer.Enabled = false;
            this.ProcessTimer.Elapsed += new ElapsedEventHandler((x, y) =>
            {
                while (true)
                {
                    Parallel.For(0, modbusConnParam.modbusParamList.Count(), modbusConnCount =>
                    {
                        ReadModbusAndInsertOpcuaProcess(modbusConnCount, modbusConnParam.modbusParamList[modbusConnCount], modbusConnParam.batchReadParam[modbusConnCount], mappingDictionary.readModbusAddressValueTableList[modbusConnCount],
                                                                                                                                       mappingDictionary.mappingModbusAddressValueTableList[modbusConnCount],
                                                                                                                                       mappingDictionary.opcuaAndModbusMappingTableList[modbusConnCount]);
                    });
                    //for (int modbusConnCount = 0; modbusConnCount < modbusConnParam.modbusClientList.Count(); modbusConnCount++)
                    //{
                    //    ReadModbus(modbusConnParam.modbusClientList[modbusConnCount], modbusConnParam.batchReadParam[modbusConnCount], mappingDictionary.readModbusAddressValueTableList[modbusConnCount],
                    //                                                                                                                   mappingDictionary.mappingModbusAddressValueTableList[modbusConnCount],
                    //                                                                                                                   mappingDictionary.opcuaAndModbusMappingTableList[modbusConnCount]);
                    //    //CombineModbusAddressValueAndOpcuaTagToDict(mappingDictionary.readModbusAddressValueTableList[modbusConnCount], mappingDictionary.mappingModbusAddressValueTableList[modbusConnCount], mappingDictionary.opcuaAndModbusMappingTableList[modbusConnCount]);


                    //}
                    //Console.WriteLine("---------------------------------");
                    Thread.Sleep(readModbusIntervalTime);
                }
            });
            this.ProcessTimer.Start();
        }
        public void ReadModbusAndInsertOpcuaProcess(int modbusConnCount, ModbusParam modbusParam, batchReadParam batchReadParam, Dictionary<string, string> readModbusAddressValueTable,
                                                                                    Dictionary<string, string> mappingModbusAddressValueTable,
                                                                                    Dictionary<string, string> opcuaAndModbusMappingTable)
        {
            //Dictionary<string, string> OpcuaNodeIdAndModbusValuePare = new Dictionary<string, string>();
            //之後可做判斷斷線重連
            List<string> changeNodeIDList = new List<string>();
            //List<OpcWriteNode> commands = new List<OpcWriteNode>();
            opcuaWriteParam opcuaWriteParam = new opcuaWriteParam();
            Dictionary<string, string> opcuaTagAndModbusValuePair = new Dictionary<string, string>();
            TcpClient modbusTcpClient = modbusParam.modbusTCPClient;
            try
            {
                if (modbusTcpClient != null && modbusTcpClient.Connected == true)
                {
                    readModbusAddressValueTable = ReadModbusValueToDict(modbusParam, batchReadParam, readModbusAddressValueTable);
                    opcuaTagAndModbusValuePair = CombineModbusAddressValueAndOpcuaTagToDict(readModbusAddressValueTable, mappingModbusAddressValueTable, opcuaAndModbusMappingTable);
                    changeNodeIDList = ModifyJsonData(opcuaTagAndModbusValuePair);
                    //commands = OPCUAWriteNode(changeNodeIDList);
                    opcuaWriteParam = OPCUAWriteNode(changeNodeIDList);
                    WtiteToOpcua(opcuaWriteParam, changeNodeIDList, modbusConnCount);
                }
                else
                {
                    modbusParam = modbusReConnect("ReadModbusAndInsertOpcuaProcess", modbusParam);
                    this.modbusConnParam.modbusParamList[modbusConnCount] = modbusParam;
                }
            }
            catch (Exception e)
            {
                if (e.Message.ToString() == "The operation is not allowed on non-connected sockets.")
                {
                    modbusReconnectFlag = true;
                }
                Console.WriteLine("ReadModbusAndInsertOpcuaProcess: " + e.Message.ToString());
            }
            //判斷modbusValue是否有改變，再將有改變的值寫入
        }
        public Dictionary<string, string> ReadModbusValueToDict(ModbusParam modbusParam, batchReadParam batchReadParam, Dictionary<string, string> readModbusAddressValueTable)
        {
            IModbusMaster modbusClient = modbusParam.modbusClient;
            for (int i = 0; i < batchReadParam.id.Count(); i++)
            {
                switch (batchReadParam.functionCode[i].ToString())
                {
                    case "1":
                        bool[] coilsDataTMP = modbusClient.ReadCoils((byte)batchReadParam.id[i], (ushort)batchReadParam.startAddress[i], (ushort)batchReadParam.dataLength[i]);
                        for (int g = 0; g < coilsDataTMP.Count(); g++)
                        {
                            readModbusAddressValueTable[batchReadParam.functionCode[i] + "," + (batchReadParam.startAddress[i] + g).ToString()] = coilsDataTMP[g].ToString();
                        }
                        break;
                    case "2":
                        bool[] inputsData = modbusClient.ReadInputs((byte)batchReadParam.id[i], (ushort)batchReadParam.startAddress[i], (ushort)batchReadParam.dataLength[i]);
                        for (int g = 0; g < inputsData.Count(); g++)
                        {
                            readModbusAddressValueTable[batchReadParam.functionCode[i] + "," + (batchReadParam.startAddress[i] + g).ToString()] = inputsData[g].ToString();
                        }
                        break;
                    case "3":
                        ushort[] holdingRegisterDataTMP = modbusClient.ReadHoldingRegisters((byte)batchReadParam.id[i], (ushort)batchReadParam.startAddress[i], (ushort)batchReadParam.dataLength[i]);
                        for (int g = 0; g < holdingRegisterDataTMP.Count(); g++)
                        {
                            readModbusAddressValueTable[batchReadParam.functionCode[i] + "," + (batchReadParam.startAddress[i] + g).ToString()] = holdingRegisterDataTMP[g].ToString();
                        }
                        break;
                    case "4":
                        ushort[] inputRegisterDataTMP = modbusClient.ReadInputRegisters((byte)batchReadParam.id[i], (ushort)batchReadParam.startAddress[i], (ushort)batchReadParam.dataLength[i]);
                        for (int g = 0; g < inputRegisterDataTMP.Count(); g++)
                        {
                            readModbusAddressValueTable[batchReadParam.functionCode[i] + "," + (batchReadParam.startAddress[i] + g).ToString()] = inputRegisterDataTMP[g].ToString();
                        }
                        break;
                    default:
                        break;
                }
            }
            return readModbusAddressValueTable;
        }
        public Dictionary<string, string> CombineModbusAddressValueAndOpcuaTagToDict(Dictionary<string, string> readModbusAddressValueTable,
                                                                                    Dictionary<string, string> mappingModbusAddressValueTable,
                                                                                    Dictionary<string, string> opcuaAndModbusMappingTable)
        {
            string opcuaTag = string.Empty;
            string previousOpcuaTag = string.Empty;
            string modbusValue = string.Empty;
            Dictionary<string, string> opcuaTagAndModbusValuePair = new Dictionary<string, string>();
            //先看opcuaAndModbusMappingTable裡nodeID的型別(查Json)
            //依型別與
            foreach (var keyValuePair in mappingModbusAddressValueTable)
            {
                mappingModbusAddressValueTable[keyValuePair.Key] = readModbusAddressValueTable[keyValuePair.Key];
            }

            foreach (var modbusRegAndOpcuaTag in opcuaAndModbusMappingTable)
            {
                opcuaTag = modbusRegAndOpcuaTag.Value;
                modbusValue = modbusRegAndOpcuaTag.Key;
                if (opcuaTag == previousOpcuaTag)
                {
                    opcuaTagAndModbusValuePair[opcuaTag] = opcuaTagAndModbusValuePair[opcuaTag] + "," + mappingModbusAddressValueTable[modbusValue];
                }
                else
                {
                    opcuaTagAndModbusValuePair[opcuaTag] = mappingModbusAddressValueTable[modbusValue];
                }
                previousOpcuaTag = opcuaTag;
            }
            return opcuaTagAndModbusValuePair;
        }
        public List<string> ModifyJsonData(Dictionary<string, string> opcuaTagAndModbusValuePair)
        {
            List<string> changeNodeIDList = new List<string>();
            foreach (var opcuaTagAndModbusValue in opcuaTagAndModbusValuePair)
            {
                int count = 0;
                ushort[] values = new ushort[2];
                string opcuaType = this.insertOPC[opcuaTagAndModbusValue.Key]["type"].ToString();

                switch (opcuaType)
                {
                    case "int16":
                        this.insertOPC[opcuaTagAndModbusValue.Key]["modbusValue"] = opcuaTagAndModbusValue.Value.ToString();
                        break;
                    case "int32":
                        foreach (var item in opcuaTagAndModbusValue.Value.Split(","))
                        {
                            //values.Append(ushort.Parse(item));
                            values[count] = (ushort.Parse(item));
                            count++;
                        }
                        int int32Value = ArrayToInt32(values);
                        this.insertOPC[opcuaTagAndModbusValue.Key]["modbusValue"] = int32Value;
                        break;
                    case "float":
                        foreach (var item in opcuaTagAndModbusValue.Value.Split(","))
                        {
                            values[count] = (ushort.Parse(item));
                            count++;
                        }
                        float floatValue = ArrayToFloat(values);
                        this.insertOPC[opcuaTagAndModbusValue.Key]["modbusValue"] = floatValue;
                        break;
                    case "bool":
                        this.insertOPC[opcuaTagAndModbusValue.Key]["modbusValue"] = opcuaTagAndModbusValue.Value.ToString();
                        break;
                    case "string":
                        break;
                    default:
                        break;
                }
                if (this.insertOPC[opcuaTagAndModbusValue.Key]["modbusValue"].ToString() != this.insertOPCTemp[opcuaTagAndModbusValue.Key]["modbusValue"].ToString())
                {
                    changeNodeIDList.Add(opcuaTagAndModbusValue.Key);
                    this.insertOPCTemp[opcuaTagAndModbusValue.Key]["modbusValue"] = this.insertOPC[opcuaTagAndModbusValue.Key]["modbusValue"];
                }
            }
            return changeNodeIDList;
        }
        public opcuaWriteParam OPCUAWriteNode(List<string> changeNodeIDList)
        {
            opcuaWriteParam opcuaWriteParam = new opcuaWriteParam();
            opcuaWriteParam.nodeId = new List<string>();
            opcuaWriteParam.value = new List<object>();
            foreach (var changeNodeID in changeNodeIDList)
            {
                opcuaWriteParam.nodeId.Add(changeNodeID.ToString());
                switch (this.insertOPC[changeNodeID]["type"].ToString())
                {
                    case "string":
                        opcuaWriteParam.value.Add(this.insertOPC[changeNodeID]["modbusValue"].ToString());
                        break;
                    case "int32":
                        opcuaWriteParam.value.Add(Convert.ToInt32(this.insertOPC[changeNodeID]["modbusValue"]));
                        break;
                    case "float":
                        opcuaWriteParam.value.Add(Convert.ToSingle(this.insertOPC[changeNodeID]["modbusValue"]));
                        break;
                    case "bool":
                        opcuaWriteParam.value.Add(Convert.ToBoolean(this.insertOPC[changeNodeID]["modbusValue"]));
                        break;
                    case "int16":
                        opcuaWriteParam.value.Add(Convert.ToInt16(this.insertOPC[changeNodeID]["modbusValue"]));
                        break;
                    default:
                        break;
                }
            }
            return opcuaWriteParam;



            //==========================================Opc.UaFx;===================================================
            //List<OpcWriteNode> commands = new List<OpcWriteNode>();
            //Dictionary<string, string> commandsDict = new Dictionary<string, string>();
            //foreach (var changeNodeID in changeNodeIDList)
            //{
            //    switch (this.insertOPC[changeNodeID]["type"].ToString())
            //    {
            //        case "string":
            //            commands.Add(new OpcWriteNode(changeNodeID, this.insertOPC[changeNodeID]["modbusValue"].ToString()));
            //            //commands.Add(new OpcWriteNode(changeNodeID, this.insertOPC[changeNodeID]["modbusValue"].ToString()));
            //            //commandsDict[changeNodeID] = "string";
            //            break;
            //        case "int32":
            //            commands.Add(new OpcWriteNode(changeNodeID, Convert.ToInt32(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commands.Add(new OpcWriteNode(changeNodeID, Convert.ToInt32(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commandsDict[changeNodeID] = "int32";
            //            break;
            //        case "float":
            //            commands.Add(new OpcWriteNode(changeNodeID, Convert.ToSingle(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commands.Add(new OpcWriteNode(changeNodeID, Convert.ToSingle(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commandsDict[changeNodeID] = "float";
            //            break;
            //        case "bool":
            //            commands.Add(new OpcWriteNode(changeNodeID, Convert.ToBoolean(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commands.Add(new OpcWriteNode(changeNodeID, Convert.ToBoolean(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commandsDict[changeNodeID] = "bool";
            //            break;
            //        case "int16":
            //            commands.Add(new OpcWriteNode(changeNodeID, Convert.ToInt16(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commands.Add(new OpcWriteNode(changeNodeID, Convert.ToInt16(this.insertOPC[changeNodeID]["modbusValue"])));
            //            //commandsDict[changeNodeID] = "int16";
            //            break;
            //        default:
            //            break;
            //    }
            //}
            //WtiteToOpcua(commandsDict);
            //return commands;
        }
        public void WtiteToOpcua(opcuaWriteParam opcuaWriteParam, List<string> changeNodeIDList, int modbusConnCount)
        {
            lock (locker)
            {
                if (opcuaWriteParam.nodeId.Count() != 0)
                {
                    if (this.m_OpcUaClient.Connected != true)
                    {
                        OpcuaServerConnect();
                    }
                    if (this.m_OpcUaClient.Connected == true)
                    {
                        try
                        {
                            bool results = this.m_OpcUaClient.WriteNodes(opcuaWriteParam.nodeId.ToArray(), opcuaWriteParam.value.ToArray());
                            //for (int i = 0; i < results.Count(); i++)
                            //{
                            //    if (results[i].IsGood)
                            //    {
                            //        Console.WriteLine("NodeID : " + changeNodeIDList[i] + " Value Change To : " + this.insertOPC[changeNodeIDList[i]]["modbusValue"].ToString());
                            //    }
                            //    else
                            //    {
                            //        Console.WriteLine("NodeID : " + changeNodeIDList[i] + " Write Value Fail ");
                            //    }
                            //}

                            //if (modbusConnCount == 0 && results == true)
                            //{
                            //    Console.WriteLine("10.95.129.2 =>  WtiteToOpcua Success");
                            //}
                            //else if (modbusConnCount == 1 && results == true)
                            //{
                            //    Console.WriteLine("192.168.56.1 =>  WtiteToOpcua Success");
                            //}
                            //Console.WriteLine("---------------------------------");
                        }
                        catch (Exception e)
                        {
                            if (modbusConnCount == 0)
                            {
                                Console.WriteLine("10.95.129.2 =>  WtiteToOpcua Fail : " + e.Message);
                            }
                            else if (modbusConnCount == 1)
                            {
                                Console.WriteLine("192.168.56.1 =>  WtiteToOpcua Fail : " + e.Message);
                            }
                        }
                    }
                }
            }
        }

        public void ModbusToOpcuaTagSubscribe()
        {
            List<string> modbusToOpcuaTagSubscribe = new List<string>();
            foreach (var nodeIDAndDataPair in this.insertOPC)
            {
                modbusToOpcuaTagSubscribe.Add(nodeIDAndDataPair.Key.ToString());
                //commands.Add(new OpcSubscribeDataChange(nodeIDAndDataPair.Key.ToString(), SubscribeNodes));
            }
            m_OpcUaClient.AddSubscription("ModbusToOpcuaTagSubscribe", modbusToOpcuaTagSubscribe.ToArray(), ModbusToOpcuaSubCallback);
        }
        public void ModbusToOpcuaSubCallback(string key, MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs args)
        {

            MonitoredItemNotification notification = args.NotificationValue as MonitoredItemNotification;
            string value = notification.Value.ToString();
            Console.WriteLine(monitoredItem.DisplayName.ToString() + " Change To : " + value);

        }





        public void OpcuaWriteToPLCSystem()
        {
            Dictionary<string, string> ipAndPortPair = new Dictionary<string, string>();
            ModbusParam plcModbusServerParam = new ModbusParam();
            CreateInsertPLCJson();
            OpcuaWriteToPLCSystemOpcuaTagSubscribe();
            ipAndPortPair = DeviceConnectInfoCollection(this.backControlMappingList);
            this.ipAndModbusClientPair = CreateIPAndModbusClientPairDict(ipAndPortPair);
            modbusConnTimerSet(ipAndPortPair);
        }
        public void CreateInsertPLCJson()
        {
            foreach (var mappingList in json)
            {
                if (mappingList.Key == "BackControlMappingTable")
                {
                    foreach (var plcMapping in mappingList.Value)
                    {
                        this.backControlMappingList.Add(plcMapping);
                        JProperty insertPLCTemp = new JProperty(plcMapping["NodeID"].ToString(),
                            JProperty.FromObject(
                                new insertPLC
                                {
                                    modbusIP = plcMapping["ModbusIP"].ToString(),
                                    modbusRegisters = plcMapping["Registers"].ToString(),
                                    functionCode = plcMapping["FunctionCode"].ToString(),
                                    ID = plcMapping["ID"].ToString()
                                }));
                        this.insertPLC.Add(insertPLCTemp);
                    }
                }
            }
        }
        public void OpcuaWriteToPLCSystemOpcuaTagSubscribe()
        {
            List<string> OpcuaWriteToPLCSubscribe = new List<string>();
            foreach (var nodeIDAndDataPair in this.insertPLC)
            {
                OpcuaWriteToPLCSubscribe.Add(nodeIDAndDataPair.Key.ToString());
                //commands.Add(new OpcSubscribeDataChange(nodeIDAndDataPair.Key.ToString(), SubscribeNodes));
            }
            m_OpcUaClient.AddSubscription("OpcuaWriteToPLCSystem", OpcuaWriteToPLCSubscribe.ToArray(), PLCSystemSubCallback);





            //List<OpcSubscribeDataChange> commands = new List<OpcSubscribeDataChange>();
            //foreach (var nodeIDAndDataPair in this.insertPLC)
            //{
            //    commands.Add(new OpcSubscribeDataChange(nodeIDAndDataPair.Key.ToString(), SubCallback));
            //}

            //OpcSubscription subscriptions = this.opcClient.SubscribeNodes(commands);
            //Console.WriteLine("OpcuaTagSubscribe OK!!!!!!!!");
        }
        public void PLCSystemSubCallback(string key, MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs args)
        {
            MonitoredItemNotification notification = args.NotificationValue as MonitoredItemNotification;
            string modbusIP = this.insertPLC[monitoredItem.DisplayName.ToString()]["modbusIP"].ToString();
            string functionCode = this.insertPLC[monitoredItem.DisplayName.ToString()]["functionCode"].ToString();
            ushort modbusRegister = Convert.ToUInt16(this.insertPLC[monitoredItem.DisplayName.ToString()]["modbusRegisters"]);
            byte id = Convert.ToByte(this.insertPLC[monitoredItem.DisplayName.ToString()]["ID"].ToString());
            string value = notification.Value.ToString();


            try
            {
                if (this.ipAndModbusClientPair[modbusIP] != null)
                {
                    switch (functionCode)
                    {
                        case "5":
                            this.ipAndModbusClientPair[modbusIP].WriteSingleCoil(id, modbusRegister, Convert.ToBoolean(value));
                            bool[] test2 = this.ipAndModbusClientPair[modbusIP].ReadCoils(id, modbusRegister, 1);
                            Console.WriteLine(monitoredItem.DisplayName.ToString() + " Change To : " + test2[0]);
                            break;
                        case "6":
                            this.ipAndModbusClientPair[modbusIP].WriteSingleRegister(id, modbusRegister, Convert.ToUInt16(value));
                            ushort[] test = this.ipAndModbusClientPair[modbusIP].ReadHoldingRegisters(id, modbusRegister, 1);
                            Console.WriteLine(monitoredItem.DisplayName.ToString() + " Change To : " + test[0]);
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (Exception ee)
            {
                if (ee.Message.ToString() == "The operation is not allowed on non-connected sockets.")
                {
                    modbusReconnectFlag = true;
                }
                Console.WriteLine("SubscribeNodes : " + ee.Message.ToString()); ;
            }
        }
        public Dictionary<string, IModbusMaster> CreateIPAndModbusClientPairDict(Dictionary<string, string> ipAndPortPair)
        {
            Dictionary<string, IModbusMaster> ipAndModbusClientPair = new Dictionary<string, IModbusMaster>();
            foreach (var ipAndPort in ipAndPortPair)
            {
                ModbusParam plcModbusServerParams = new ModbusParam();
                plcModbusServerParams.ipAddress = ipAndPort.Value.Split(",")[0].ToString();
                plcModbusServerParams.port = Convert.ToInt16(ipAndPort.Value.Split(",")[1]);
                try
                {
                    plcModbusServerParams = plcModbusReConnect(plcModbusServerParams);
                    Console.WriteLine("OpcuaWriteToPLCSystem :" + plcModbusServerParams.ipAddress + " connect success");
                    this.modbusReconnectFlag = false;
                }
                catch (Exception e)
                {
                    Console.WriteLine("OpcuaWriteToPLCSystem :" + plcModbusServerParams.ipAddress + " connect fail");
                    this.modbusReconnectFlag = true;
                }
                //TcpClient masterTcpClient = new TcpClient(ipAndPort.Value.Split(",")[0].ToString(), Convert.ToInt16(ipAndPort.Value.Split(",")[1]));
                //IModbusMaster modbusClient = ModbusIpMaster.CreateIp(masterTcpClient);

                ipAndModbusClientPair[ipAndPort.Value.Split(",")[0].ToString()] = plcModbusServerParams.modbusClient;
            }
            return ipAndModbusClientPair;
        }
        public void modbusConnTimerSet(Dictionary<string, string> ipAndPortPair)
        {
            this.modbusConnTimer.Interval = 1000;
            this.modbusConnTimer.AutoReset = false;
            this.modbusConnTimer.Enabled = false;
            this.modbusConnTimer.Elapsed += new ElapsedEventHandler((x, y) =>
            {
                while (true)
                {
                    if (this.modbusReconnectFlag == true || this.ipAndModbusClientPair.ContainsValue(null))
                    {
                        this.ipAndModbusClientPair = CreateIPAndModbusClientPairDict(ipAndPortPair);
                    }
                    Thread.Sleep(modbusConnInterval);
                }
            });
            modbusConnTimer.Start();
        }

        //public void SubscribeNodes(object sender, OpcDataChangeReceivedEventArgs e)
        //{
        //    //訂閱後資料改變後可寫入
        //    OpcMonitoredItem item = (OpcMonitoredItem)sender;

        //    string modbusIP = this.insertPLC[item.NodeId.ToString()]["modbusIP"].ToString();
        //    string functionCode = this.insertPLC[item.NodeId.ToString()]["functionCode"].ToString();
        //    ushort modbusRegister = Convert.ToUInt16(this.insertPLC[item.NodeId.ToString()]["modbusRegisters"]);
        //    byte id = Convert.ToByte(this.insertPLC[item.NodeId.ToString()]["ID"].ToString());
        //    string value = e.Item.Value.ToString();

        //    //Console.WriteLine("Data Change from NodeId '{0}': {1}", item.NodeId, e.Item.Value);
        //    try
        //    {
        //        if (this.ipAndModbusClientPair[modbusIP] != null)
        //        {
        //            switch (functionCode)
        //            {
        //                case "5":
        //                    this.ipAndModbusClientPair[modbusIP].WriteSingleCoil(id, modbusRegister, Convert.ToBoolean(value));
        //                    Console.WriteLine(this.ipAndModbusClientPair[modbusIP].ReadCoils(id, modbusRegister, 1));
        //                    break;
        //                case "6":
        //                    this.ipAndModbusClientPair[modbusIP].WriteSingleRegister(id, modbusRegister, Convert.ToUInt16(value));
        //                    ushort[] test = this.ipAndModbusClientPair[modbusIP].ReadHoldingRegisters(id, modbusRegister, 1);
        //                    Console.WriteLine(item.NodeId.ToString() + " Change To : " + test[0]);
        //                    break;
        //                default:
        //                    break;
        //            }
        //        }
        //    }
        //    catch (Exception ee)
        //    {
        //        if (ee.Message.ToString() == "The operation is not allowed on non-connected sockets.")
        //        {
        //            modbusReconnectFlag = true;
        //        }
        //        Console.WriteLine("SubscribeNodes : " + ee.Message.ToString()); ;
        //    }

        //}




        public ModbusParam modbusReConnect(string systemName, ModbusParam modbusParam)
        {
            try
            {
                if (modbusParam.modbusTCPClient != null)
                {
                    modbusParam.modbusTCPClient.Dispose();
                }
                modbusParam.modbusTCPClient = new TcpClient(modbusParam.ipAddress, modbusParam.port);
                modbusParam.modbusClient = ModbusIpMaster.CreateIp(modbusParam.modbusTCPClient);
                this.modbusReconnectFlag = false;
                Console.WriteLine(systemName + " : " + modbusParam.ipAddress + " connect success");
            }
            catch (Exception e)
            {
                Console.WriteLine(systemName + " : " + modbusParam.ipAddress + " connect fail");
            }
            return modbusParam;
        }
        public ModbusParam plcModbusReConnect(ModbusParam modbusParam)
        {
            if (modbusParam.modbusTCPClient != null)
            {
                modbusParam.modbusTCPClient.Dispose();
            }
            modbusParam.modbusTCPClient = new TcpClient(modbusParam.ipAddress, modbusParam.port);
            modbusParam.modbusClient = ModbusIpMaster.CreateIp(modbusParam.modbusTCPClient);
            return modbusParam;
        }
        public batchReadParam GetBatchReadParam(batchReadParam batchReadParam, int id, int functionCode, int startAddress, int dataLength)
        {
            batchReadParam.functionCode.Add(functionCode);
            batchReadParam.startAddress.Add(startAddress);
            batchReadParam.dataLength.Add(dataLength);
            batchReadParam.id.Add(id);

            return batchReadParam;
        }
        public int ArrayToInt32(ushort[] arrayData)
        {
            int data = 0;
            try
            {
                data = (ushort)arrayData[1] << 16 | (ushort)arrayData[0];
            }
            catch (Exception e)
            {
                //WriteLog("ArrayToInt32 exception : " + e.Message);
            }
            return data;
        }
        public int ArrayToNegative(ushort[] arrayData)
        {
            int data = 0;
            try
            {
                short[] array = new short[1];
                Buffer.BlockCopy(arrayData.ToArray(), 0, array, 0, 2);
                data = array[0];
            }
            catch (Exception e)
            {
                //WriteLog("ArrayToNegative exception : " + e.Message);
            }
            return data;
        }
        public float ArrayToFloat(ushort[] arrayData)
        {
            float data = 0;
            try
            {
                float[] array = new float[arrayData.Length/2];
                Buffer.BlockCopy(arrayData.ToArray(), 0, array, 0, arrayData.Length*2);
                data = array[0];
            }
            catch (Exception e)
            {
                //WriteLog("ArrayToInt32 exception : " + e.Message);
            }
            return data;
        }
        //public void WriteLog(string logMessage)
        //{
        //    DateTime value = DateTime.Now;
        //    string timeYMD = value.ToString("yyyy-MM-dd");
        //    //string timeYMD = value.ToString("yyyy-MM-ddmm");
        //    string timeHMS = value.ToString("HH:mm:ss");
        //    try
        //    {
        //        if (!Directory.Exists(logAddress))
        //        {
        //            Directory.CreateDirectory(logAddress);
        //        }
        //        StreamWriter sw = new StreamWriter(logAddress + "Log_" + timeYMD + ".txt", true);
        //        sw.WriteLine("[" + timeHMS + "]" + " : " + logMessage);
        //        sw.Close();
        //    }
        //    catch (Exception ee)
        //    {
        //        Console.WriteLine(ee.Message);
        //    }
        //}
    }
}
