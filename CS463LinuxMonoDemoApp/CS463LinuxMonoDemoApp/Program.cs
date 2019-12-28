using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

using CSLibrary;
using CSLibrary.Constants;

using System.ComponentModel;
using System.Data;
using System.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Net.NetworkInformation;
using System.Linq;

using uPLibrary.Networking.M2Mqtt;
using Newtonsoft.Json;
using uPLibrary.Networking.M2Mqtt.Messages;



/*namespace CSL_Console_Mode_Demo*/
namespace CS463LinuxMonoDemoApp
{

    public struct program_config
    {
        public string str_saveTagData;
    };


    public struct reader_info
    {
        public string str_reader_type;
        public string str_ip_addr;
        public string str_epc_len;
        public string str_tid_len;
        public string str_user_data_len;
        public string str_toggleTarget;
        public string str_multiBanks;
        public string str_mqtt_broker_enable;
        public string str_mqtt_broker_port;
        public string str_tag_smoother;

        public int read_epc_len;               // EPC Length defined by user (by WORDS)
        public int read_tid_len;               // TID Length defined by user (by WORDS)
        public int read_user_data_len;        // User Data Length defined by user (by WORDS)

        public AntennaSequenceMode antSeqMode;           // Antenna Sequence Mode
        public byte[] antPort_seqTable;                 // Port Sequence Table
        public uint antPort_seqTableSize;               // Port Sequence Table Size

        public bool[] antPort_state;                  // Port Active / Inactive
        public uint[] antPort_power;                 // RF Power defined by user
        public uint[] antPort_dwell;                 // Dwell Time
        public uint[] antPort_Pofile;                // Profile
        public SingulationAlgorithm[] antPort_QAlg;            // Q Algorithm
        public uint[] antPort_startQValue;              // Dynamic Q defined by user
        public uint[] freq_channel;                 // Frequency Channel
        public bool IsNonHopping_usrDef;

        public uint init_toggleTarget;               // Toggle Target defined by user
        public uint init_multiBanks;                  // Multibank defined by user
        public RegionCode regionCode;               // Region Code
                                                    //        public bool freqHopping;                    // Frequency Hopping Enable flag

        public int epc_len_hex;              // number of digit display (defined by user) - EPC
        public int tid_len_hex;              // number of digit display (defined by user) - TID
        public int user_data_len_hex;   // number of digit display (defined by user) - User data

        public bool mqtt_broker_enable;
        public string mqtt_broker_ip;
        public uint mqtt_broker_port;
        public string mqtt_broker_clientid;
        public string[] mqtt_port_name;
        public string mqtt_topic;
        public uint tag_smoother;
    }

    public struct inventory_info
    {
        public string[] reader_ipAddress;
        public uint num_reader_running;
        public uint[] reader_tagCount;

        public string[] reader_ipAddress_hold;
        public uint num_reader_running_hold;
        public uint[] reader_tagCount_hold;

        public DateTime TagCountTimer;
    }

    class ReaderCtrlClass
    {
        public HighLevelInterface Reader;
        public void Reset()
        {

            Result rc = Result.OK;
            string t_str_readerinfo_1, t_str_readerinfo_2;
            int cnt_recon = 0;

            do
            {
                cnt_recon++;
                t_str_readerinfo_1 = Reader.DeviceNameOrIP + " : Reconnecting Reader #" + cnt_recon + " attempt .... \r\n";
                Console.WriteLine("");
                Console.WriteLine(t_str_readerinfo_1);

                t_str_readerinfo_2 = "";
                rc = Reader.Reconnect(1);
                if (rc == Result.OK)
                {
                    //Start inventory
                    t_str_readerinfo_2 = "Reader < " + Reader.DeviceNameOrIP + " > is reconnected";
                    Reader.SetOperationMode(CSLibrary.Constants.RadioOperationMode.CONTINUOUS);

                    int retry = 10;
                    while (Reader.StartOperation(Operation.TAG_RANGING, false) != Result.OK)
                    {
                        if (retry-- == 0)
                        {
                            t_str_readerinfo_2 += "Reader <" + Reader.DeviceNameOrIP + "> fails to restart";
                            break;
                        }
                    }
                }
                else
                {
                    t_str_readerinfo_2 = "Reader < " + Reader.DeviceNameOrIP + " > fails to reconnect";
                }


                t_str_readerinfo_2 += "\r\n";
                Console.WriteLine(t_str_readerinfo_2);

                TextWriter tw = new StreamWriter("ReaderResetLog_" + DateTime.Now.ToString("yyyyMMdd") + ".Txt", true);
                tw.WriteLine(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss UTC zzz") + "\r\n" + t_str_readerinfo_1 + t_str_readerinfo_2);
                tw.Close();

                Thread.Sleep(1000);      // Sleep for 1 seconds

            } while (rc != Result.OK);


        }
    }

    public enum INV_ALG
    {
        FIXEDQ = 0,
        DYNAMICQ = 3,
        UNKNOWN = 65535,
    };

    public enum RDR_PARAM
    {
        PORT = 0,
        ACT_STATE,
        POWER,
        DWELL_TM,
        PROFILE,
        Q_ALG,
        START_Q,
        FREQ_CH
    };


    class Program
    {
        public static List<HighLevelInterface> ReaderList = new List<HighLevelInterface>();

        public static List<MqttClient> MqttBrokerList = new List<MqttClient>();

        public static List<Dictionary<string, CTagResponse>[]> tagList = new List<Dictionary<string, CTagResponse>[]>();

        public static Queue<CMqttEventMessage> eventQueue = new Queue<CMqttEventMessage>();

        public static Thread tagInspectThread = new Thread(inspectTagBuffer);
        public static Thread mqttEventThread = new Thread(sendEventToBroker);

        public const int MAX_PORT_SEQ_NUM = 48;

        public static string[] RegionArray = { "UNKNOWN", "FCC", "ETSI", "CN", "CN1", "CN2", "CN3", "CN4", "CN5", "CN6", "CN7", "CN8", "CN9", "CN10", "CN11", "CN12",
                                                "TW", "KR", "HK", "JP", "AU", "MY", "SG", "IN", "G800", "ZA", "BR1", "BR2", "ID", "TH", "JP2012" };

        public static string[,] Tab_OEM_Country_Code = {
                                                        { "0", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" },                         // Code0
                                                        { "3", "ETSI", "IN", "G800", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" },               // Code1
                                                        { "10", "AU", "BR1", "BR2", "FCC", "HK", "SG", "MY", "ZA", "TH", "ID", "", "", "", "", "", "", "", "", "" },  // Code2
                                                        { "0", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" },                         // Code3
                                                        { "19", "AU", "MY", "HK", "SG", "TW", "ID", "CN", "CN1", "CN2", "CN3", "CN4", "CN5", "CN6", "CN7", "CN8", "CN9", "CN10", "CN11", "CN12" },    // Code4
                                                        { "0", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" },                         // Code5
                                                        { "0", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" },                         // Code6
                                                        { "17", "AU", "HK", "TH", "SG", "CN", "CN1", "CN2", "CN3", "CN4", "CN5", "CN6", "CN7", "CN8", "CN9", "CN10", "CN11", "CN12", "", "" },        // Code7
                                                        { "1", "JP", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" }                        // Code8
                                                   };

        private static string str_sw_version = "Sample_CSL_RFID_MultiReader_Reconnect_Demo_v1.3.1";         // software version
        private static string fileName = "Config.txt";                                           // filename of User Configuration

        public static reader_info[] rdr_info_data = new reader_info[100];
        private static int num_readers;          // Number of Readers

        private static bool flag_display = false;
        private static bool flag_endProg = false;

        private static uint flag_saveTagData = 1;

        public static inventory_info cur_inv_info;
        // private static DateTime log_TagCountTimer = DateTime.Now;

        public static int TagLogFileCurrentNumber = 0;
        public static int TagLogFileSizeLimit = 4096 * 512;

        public static string str_dataLogEvt = "";
        public static string str_dataLogToFile = "";

        public static int flag_error = 0;

        static object StateChangedLock = new object();
        static void ReaderXP_StateChangedEvent(object sender, CSLibrary.Events.OnStateChangedEventArgs e)
        {

            lock (StateChangedLock)
            {
                HighLevelInterface t_Reader = (HighLevelInterface)sender;
                ReaderCtrlClass t_readerCtrl = new ReaderCtrlClass();
                string t_str_readerinfo;

                switch (e.state)
                {
                    case CSLibrary.Constants.RFState.IDLE:
                        break;
                    case CSLibrary.Constants.RFState.BUSY:
                        break;
                    case CSLibrary.Constants.RFState.RESET:
                        // Reconnect reader and restart inventory

                        t_str_readerinfo = t_Reader.IPAddress + " : Reader is disconnected";
                        Console.WriteLine(t_str_readerinfo);
                        str_dataLogToFile += "\r\n" + t_str_readerinfo + "\r\n";

                        TextWriter tw = new StreamWriter("ReaderResetLog_" + DateTime.Now.ToString("yyyyMMdd") + ".Txt", true);
                        tw.WriteLine(t_str_readerinfo + "          " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss UTC zzz"));
                        tw.Close();

                        //Use other thread to create progress
                        t_readerCtrl.Reader = t_Reader;
                        Thread reset = new Thread(t_readerCtrl.Reset);
                        reset.Start();

                        break;
                    case CSLibrary.Constants.RFState.ABORT:
                        break;
                }
            }
        }



        static bool LoadConfigFile()
        {
            char[] delimiterChars = { ' ', '\t' };
            string str_checkconfig = "";
            tagList = new List<Dictionary<string, CTagResponse>[]>();
            eventQueue.Clear();

            bool Success = false;
            try
            {
                if (File.Exists(fileName) == false)     // Check File Exist
                {
                    Console.WriteLine("Cannot find the Configuration file (" + fileName + ") !");
                    Success = false;
                    return Success;
                }

                using (FileStream fs = new FileStream(fileName, FileMode.Open))
                using (StreamReader sr = new StreamReader(fs))
                {

                    Success = false;

                    while (true)
                    {
                        string readline1 = sr.ReadLine();

                        if (readline1 == null)
                        {
                            break;
                        }


                        if ((readline1 != "") && (readline1.Substring(0, 1) != ";"))
                        {
                            string[] str_configHeader = readline1.Split(delimiterChars);
                            string str_chkHeader_saveTag = "[SAVE_TAG_DATA]";
                            if (str_configHeader[0].Length == str_chkHeader_saveTag.Length)
                            {
                                flag_saveTagData = Convert.ToUInt32(str_configHeader[2]);      // saveTagData
                                if ((flag_saveTagData != 0) && (flag_saveTagData != 1))
                                {
                                    Console.WriteLine("[SAVE_TAG_DATA] should be 0 or 1 !");
                                    Success = false;
                                }
                            }

                            str_checkconfig = "[MULTIREADER_DEMO]";
                            if ((readline1.Length == str_checkconfig.Length) && (readline1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                            {
                                
                                tagList.Add(new Dictionary<string, CTagResponse>[4]);
                                tagList[num_readers][0] = new Dictionary<string, CTagResponse>();
                                tagList[num_readers][1] = new Dictionary<string, CTagResponse>();
                                tagList[num_readers][2] = new Dictionary<string, CTagResponse>();
                                tagList[num_readers][3] = new Dictionary<string, CTagResponse>();
                                num_readers++;

                                rdr_info_data[num_readers - 1].mqtt_port_name = new string[4];
                                rdr_info_data[num_readers - 1].antPort_state = new bool[4];                       // initialize mem for port state

                                rdr_info_data[num_readers - 1].antPort_seqTable = new byte[MAX_PORT_SEQ_NUM];                         // initialize mem for Port Sequence Table
                                Array.Clear(rdr_info_data[num_readers - 1].antPort_seqTable, 0, MAX_PORT_SEQ_NUM);                    // Set to zero value
                                rdr_info_data[num_readers - 1].antPort_seqTableSize = 0;

                                rdr_info_data[num_readers - 1].antPort_power = new uint[4];                       // initialize mem for antenna Port - power
                                rdr_info_data[num_readers - 1].antPort_dwell = new uint[4];                       // initialize mem for Dwell Time
                                rdr_info_data[num_readers - 1].antPort_Pofile = new uint[4];                      // initialize mem for Port Profile
                                rdr_info_data[num_readers - 1].antPort_QAlg = new SingulationAlgorithm[4];                      // initialize mem for Port Q Algorithm
                                rdr_info_data[num_readers - 1].antPort_startQValue = new uint[4];                   // initialize mem for Start Q
                                rdr_info_data[num_readers - 1].freq_channel = new uint[4];                         // initialize mem for freqency channel

                                rdr_info_data[num_readers - 1].IsNonHopping_usrDef = false;

                                Success = true;
                            }


                            string[] readarr1 = readline1.Split(new char[] { '\n' });
                            foreach (string s1 in readarr1)
                            {

                                if (Success == false)
                                {
                                    break;
                                }

                                str_checkconfig = "IP =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].str_ip_addr = words[2];     // Read IP address
                                }
                                str_checkconfig = "EPC_LEN =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].str_epc_len = words[2];                                  // Read EPC Length
                                    rdr_info_data[num_readers - 1].read_epc_len = Convert.ToInt32(rdr_info_data[num_readers - 1].str_epc_len);
                                    rdr_info_data[num_readers - 1].epc_len_hex = rdr_info_data[num_readers - 1].read_epc_len * 4;
                                }

                                str_checkconfig = "TOGGLE_TARGET =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].str_toggleTarget = words[2];                                  // Read Toggle Target
                                    rdr_info_data[num_readers - 1].init_toggleTarget = Convert.ToUInt32(rdr_info_data[num_readers - 1].str_toggleTarget);
                                    if ((rdr_info_data[num_readers - 1].init_toggleTarget != 0) && (rdr_info_data[num_readers - 1].init_toggleTarget != 1))
                                    {
                                        Console.WriteLine("TOGGLE_TARGET should be 0 or 1 !");
                                        Success = false;
                                    }
                                }

                                str_checkconfig = "MULTIBANK =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].str_multiBanks = words[2];                                  // Read Multi-bank
                                    rdr_info_data[num_readers - 1].init_multiBanks = Convert.ToUInt32(rdr_info_data[num_readers - 1].str_multiBanks);
                                    if ((rdr_info_data[num_readers - 1].init_multiBanks < 0) || (rdr_info_data[num_readers - 1].init_multiBanks > 2))
                                    {
                                        Console.WriteLine("MULTIBANK should be 0, 1 or 2 !");
                                        Success = false;
                                    }
                                }

                                str_checkconfig = "MQTT_BROKER_ENABLE =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].str_mqtt_broker_enable = words[2];                                  // Read Multi-bank
                                    rdr_info_data[num_readers - 1].mqtt_broker_enable = rdr_info_data[num_readers - 1].str_mqtt_broker_enable == "1" ? true : false;
                                }
                                str_checkconfig = "MQTT_BROKER_IP =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].mqtt_broker_ip = words[2];
                                }
                                str_checkconfig = "MQTT_BROKER_PORT =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].str_mqtt_broker_port = words[2];                                  // Read Multi-bank
                                    rdr_info_data[num_readers - 1].mqtt_broker_port = Convert.ToUInt32(rdr_info_data[num_readers - 1].str_mqtt_broker_port);
                                    if ((rdr_info_data[num_readers - 1].mqtt_broker_port < 0) || (rdr_info_data[num_readers - 1].mqtt_broker_port > 9999))
                                    {
                                        Console.WriteLine("MQTT Port Number should be unsigned integer in valid range!");
                                        Success = false;
                                    }
                                }
                                str_checkconfig = "MQTT_BROKER_CLIENTID =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].mqtt_broker_clientid = words[2];
                                }
                                str_checkconfig = "MQTT_BROKER_DEVICE_PORT1 =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].mqtt_port_name[0] = words[2];
                                }
                                str_checkconfig = "MQTT_BROKER_DEVICE_PORT2 =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].mqtt_port_name[1] = words[2];
                                }
                                str_checkconfig = "MQTT_BROKER_DEVICE_PORT3 =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].mqtt_port_name[2] = words[2];
                                }
                                str_checkconfig = "MQTT_BROKER_DEVICE_PORT4 =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].mqtt_port_name[3] = words[2];
                                }

                                str_checkconfig = "TAG_SMOOTHER_TIME =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    rdr_info_data[num_readers - 1].str_tag_smoother = words[2];                                  // Read Multi-bank
                                    rdr_info_data[num_readers - 1].tag_smoother = Convert.ToUInt32(rdr_info_data[num_readers - 1].str_tag_smoother);
                                    if ((rdr_info_data[num_readers - 1].tag_smoother < 0) || (rdr_info_data[num_readers - 1].tag_smoother > 30000))
                                    {
                                        Console.WriteLine("tag smoother should be unsigned integer in valid range!");
                                        Success = false;
                                    }
                                }
                                str_checkconfig = "REGION_CODE =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);

                                    string t_region = words[2];
                                    int t_code = Array.IndexOf(RegionArray, t_region);                                         // Read Region Code
                                    rdr_info_data[num_readers - 1].regionCode = (RegionCode)t_code;

                                    // RegionCode t_code = rdr_info_data[num_readers - 1].regionCode;
                                }
                                /*
                                                                        str_checkconfig = "FREQ_HOPPING =";
                                                                        if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                                                        {
                                                                            string[] words = s1.Split(delimiterChars);

                                                                            if(words[2] == "ON")
                                                                                rdr_info_data[num_readers - 1].freqHopping = true;                      // Enable Frequency Hopping
                                                                            else
                                                                                rdr_info_data[num_readers - 1].freqHopping = false;                     // Disable Frequency Hopping
                                                                        }
                                */

                                str_checkconfig = "ANT_SEQUENCE_MODE =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);
                                    if (words[2] == "NORMAL")
                                        rdr_info_data[num_readers - 1].antSeqMode = AntennaSequenceMode.NORMAL;
                                    else if (words[2] == "SEQUENCE")
                                        rdr_info_data[num_readers - 1].antSeqMode = AntennaSequenceMode.SEQUENCE;
                                    else if (words[2] == "SMART_CHK")
                                        rdr_info_data[num_readers - 1].antSeqMode = AntennaSequenceMode.SMART_CHECK;
                                    else if (words[2] == "SEQUENCE_SMART_CHK")
                                        rdr_info_data[num_readers - 1].antSeqMode = AntennaSequenceMode.SEQUENCE_SMART_CHECK;
                                    else
                                    {
                                        Console.WriteLine("Antenna Mode Error ");
                                        Success = false;
                                    }
                                }

                                str_checkconfig = "SEQUENCE =";
                                if ((s1.Length > str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    string[] words = s1.Split(delimiterChars);


                                    string[] t_str_seqTable = words[2].Split(new char[] { ',' });

                                    for (int t_seqidx = 0; t_seqidx < t_str_seqTable.Length; t_seqidx++)
                                    {
                                        if (rdr_info_data[num_readers - 1].antPort_seqTableSize < MAX_PORT_SEQ_NUM)
                                        {
                                            byte portnum = Convert.ToByte(t_str_seqTable[t_seqidx]);
                                            if (rdr_info_data[num_readers - 1].antPort_state[(uint)portnum] == false)
                                            {
                                                Console.WriteLine("Error: Port {0} is not in ACTIVE state, but define in antenna sequence", (uint)portnum);
                                                Success = false;
                                            }
                                            else
                                            {
                                                uint t_tabSize = rdr_info_data[num_readers - 1].antPort_seqTableSize;
                                                rdr_info_data[num_readers - 1].antPort_seqTable[t_tabSize] = portnum;                        // Store Port number to Squence Table
                                                rdr_info_data[num_readers - 1].antPort_seqTableSize++;
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("Error: Number of Antenna Squence exceed the maximum limit");
                                            Success = false;
                                        }
                                    }
                                    // Console.WriteLine("Test: Ant Seq");
                                }


                                str_checkconfig = "READER_PARAM =";
                                if ((s1.Length == str_checkconfig.Length) && (s1.Substring(0, str_checkconfig.Length) == str_checkconfig))
                                {
                                    uint t_cnt = 0;

                                    do
                                    {
                                        string t_portCfg_line = sr.ReadLine();
                                        string[] t_portCfg = t_portCfg_line.Split(new char[] { '\n', ',' });

                                        uint t_port = Convert.ToUInt32(t_portCfg[0]);

                                        // RDR_PARAM.ACT_STATE
                                        if (t_portCfg[(int)RDR_PARAM.ACT_STATE] == "OFF")
                                            rdr_info_data[num_readers - 1].antPort_state[t_port] = false;                  // Set port to Inactive state
                                        else
                                        {
                                            rdr_info_data[num_readers - 1].antPort_state[t_port] = true;                   // Set port to Active state

                                            // RDR_PARAM.POWER
                                            rdr_info_data[num_readers - 1].antPort_power[t_port] = Convert.ToUInt32(t_portCfg[(int)RDR_PARAM.POWER]);                 // Store Port RF Power value
                                                                                                                                                                      // RDR_PARAM.DWELL_TM
                                            rdr_info_data[num_readers - 1].antPort_dwell[t_port] = Convert.ToUInt32(t_portCfg[(int)RDR_PARAM.DWELL_TM]);              // Store Port Dwell Time
                                                                                                                                                                      // RDR_PARAM.PROFILE
                                            rdr_info_data[num_readers - 1].antPort_Pofile[t_port] = Convert.ToUInt32(t_portCfg[(int)RDR_PARAM.PROFILE]);              // Store Port Profile
                                                                                                                                                                      // RDR_PARAM.START_Q
                                            rdr_info_data[num_readers - 1].antPort_startQValue[t_port] = Convert.ToUInt32(t_portCfg[(int)RDR_PARAM.START_Q]);           // Store Start Q value

                                            // SingulationAlgorithm.FIXEDQ  /  SingulationAlgorithm.DYNAMICQ
                                            if (t_portCfg[(int)RDR_PARAM.Q_ALG].ToString() == "FIX")
                                                rdr_info_data[num_readers - 1].antPort_QAlg[t_port] = SingulationAlgorithm.FIXEDQ;                                 // Store Q Algorithm                      
                                            else
                                                rdr_info_data[num_readers - 1].antPort_QAlg[t_port] = SingulationAlgorithm.DYNAMICQ;                               // Store Q Algorithm


                                            // RDR_PARAM.FREQ_CH -  FHSS / CH-XX
                                            if (t_portCfg[(int)RDR_PARAM.FREQ_CH] == "HOP")                            // Frequency Hopping ?
                                            {
                                                rdr_info_data[num_readers - 1].IsNonHopping_usrDef = false;
                                                rdr_info_data[num_readers - 1].freq_channel[t_port] = 0;
                                            }
                                            else
                                            {
                                                rdr_info_data[num_readers - 1].IsNonHopping_usrDef = true;

                                                string str_input = t_portCfg[(int)RDR_PARAM.FREQ_CH];
                                                string str_freqCh = "CH-";
                                                if ((str_input.Length > str_freqCh.Length) && (str_input.Substring(0, str_freqCh.Length) == str_freqCh))
                                                {
                                                    string t_channel = str_input.Substring(str_freqCh.Length, str_input.Length - str_freqCh.Length);
                                                    rdr_info_data[num_readers - 1].freq_channel[t_port] = Convert.ToUInt32(t_channel);                               // Store Freq Channel
                                                }
                                                else
                                                {
                                                    Console.WriteLine("Error: Port {0} Freq Channel is not in correct format", t_port);
                                                    Success = false;
                                                }
                                            }

                                        }
                                    } while (t_cnt++ < 3);

                                    // t_cnt = 16;
                                }

                            }
                        }
                    }

                }
            }
            catch
            {
                return false;
            }
            return Success;
        }



        static void DataLog_InitMem()
        {
            cur_inv_info.reader_ipAddress = new string[100];
            cur_inv_info.num_reader_running = 0;
            cur_inv_info.reader_tagCount = new uint[100];
            cur_inv_info.reader_ipAddress_hold = new string[100];
            cur_inv_info.num_reader_running_hold = 0;
            cur_inv_info.reader_tagCount_hold = new uint[100];
            cur_inv_info.TagCountTimer = new DateTime();

            cur_inv_info.TagCountTimer = DateTime.Now;
        }


        static void DataLog_Write(int t_tagCount, string t_tagData)
        {

            //....................... Write data to Log file
            string TagLogDate = "_" + DateTime.Now.ToString("yyyyMMdd") + "_";
            string TagLogFileName = "TagLog" + TagLogDate + TagLogFileCurrentNumber + ".csv";
            FileInfo f = new FileInfo(TagLogFileName);

            if (f.Exists == false)
            {
                TextWriter tw = new StreamWriter(TagLogFileName, true);
                // tw.WriteLine("EPC, Serial Number, Date and Time, RSSI, Antenna Port");
                tw.WriteLine("Tag Count, Date and Time");
                tw.Close();
                f = new FileInfo(TagLogFileName);
            }
            if (f.Length < TagLogFileSizeLimit)
            {
                TextWriter tw = new StreamWriter(TagLogFileName, true);
                // tw.WriteLine(e.info.epc.ToString().Substring(0, epc_len_hex) + "," + SerialNumber + "," + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss UTC zzz") + "," + e.info.rssi + "," + e.info.antennaPort);
                tw.WriteLine("Tag Count :  " + t_tagCount + "," + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss UTC zzz"));
                tw.WriteLine(t_tagData);
                tw.Close();
            }

            f = new FileInfo(TagLogFileName);
            if (f.Length > TagLogFileSizeLimit)
            {
                do
                {
                    TagLogFileCurrentNumber++;
                    TagLogFileName = "TagLog" + TagLogDate + TagLogFileCurrentNumber + ".csv";
                    f = new FileInfo(TagLogFileName);
                } while (f.Exists);
                // TextWriter tw = new StreamWriter(TagLogFileName, true);
                // tw.WriteLine("EPC, Serial Number, Date and Time, RSSI, Antenna Port");
                // tw.Close();
            }

        }


        // Performance Counte
        public static PerformanceCounter cpuUsage = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        public static PerformanceCounter ramUsage = new PerformanceCounter("Memory", "Available MBytes");
        public static float cpu_usage_val = 0;


        public static void InitCpuUsage()
        {
            float _usage = 0;

            cpuUsage.NextValue();                   //ignore the 1 sample with value 0.
            for (int i = 0; i < 10; i++)
            {
                System.Threading.Thread.Sleep(30);         //sleep for 30ms take the average of value
                _usage += cpuUsage.NextValue();
            }

            cpu_usage_val = _usage / 10;     // take the average value
        }

        public static void CalculateCpuUsage()
        {
            float _usage = 0;
            float _usage_2 = 0;

            _usage_2 = cpuUsage.NextValue();                   //ignore the 1 sample with value 0.
            for (int i = 0; i < 4; i++)
            {
                do
                {
                    System.Threading.Thread.Sleep(40);         //sleep for 40ms take the average of value
                    _usage_2 = cpuUsage.NextValue();
                } while (_usage_2 == 0);

                // Console.WriteLine("_usage_2: " +  Convert.ToString(_usage_2) + "%");
                _usage += _usage_2;
            }
            _usage = _usage / 5;                    // take the average value
            // Console.WriteLine("");

            // cpu_usage_val = (cpu_usage_val + _usage) / 2;     // take the average value
            cpu_usage_val = _usage;
        }

        public static float getCpuUsage()
        {
            return cpu_usage_val;          //ignore the 1 sample with value 0.
        }

        public static float getRamUsage()
        {
            return ramUsage.NextValue();
        }


        static DateTime TagCountTimer = DateTime.Now;
        static void DataLog_Process()
        {
            int t_logTagCount;
            int hr;

            if (TagCountTimer > cur_inv_info.TagCountTimer)
            {
                // Console.WriteLine("Number of Readers Running : " + cur_inv_info.num_reader_running_hold);
                Console.WriteLine("Time : " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss UTC zzz"));

                string str_CurrentCpuUsage = Convert.ToString(getCpuUsage()) + "%";
                Console.WriteLine("Current Cpu Usage: " + str_CurrentCpuUsage);

                string str_AvailableRAM = Convert.ToString(getRamUsage()) + "MB";
                Console.WriteLine("AvailableRAM: " + str_AvailableRAM);


                // Tag Information
                t_logTagCount = 0;
                for (hr = 0; hr < cur_inv_info.num_reader_running_hold; hr++)
                {
                    Console.WriteLine(cur_inv_info.reader_ipAddress_hold[hr] + "  Tags per second : " + cur_inv_info.reader_tagCount_hold[hr]);
                    t_logTagCount += (int)cur_inv_info.reader_tagCount_hold[hr];
                }
                Console.WriteLine("\r\n");
                cur_inv_info.TagCountTimer = TagCountTimer;

                //tag buffer information
                for (int i = 0; i < num_readers; i++)
                {
                    if (tagList[i][0] != null)
                        Console.WriteLine("Reader IP={0}, Port 1 Tag Buffer={1}",rdr_info_data[i].str_ip_addr, tagList[i][0].Count);
                    if (tagList[i][1] != null)
                        Console.WriteLine("Reader IP={0}, Port 2 Tag Buffer={1}", rdr_info_data[i].str_ip_addr, tagList[i][1].Count);
                    if (tagList[i][2] != null)
                        Console.WriteLine("Reader IP={0}, Port 3 Tag Buffer={1}", rdr_info_data[i].str_ip_addr, tagList[i][2].Count);
                    if (tagList[i][3] != null)
                        Console.WriteLine("Reader IP={0}, Port 4 Tag Buffer={1}", rdr_info_data[i].str_ip_addr, tagList[i][3].Count);

                }

                Console.WriteLine("MQTT Event Message Buffer.  Count={0}", eventQueue.Count);

                if (flag_saveTagData == 1)
                {
                    DataLog_Write(t_logTagCount, str_dataLogToFile);      // Save Tag Data
                }
            }
        }


        static int DataLog_Sample(string t_ipAddress)
        {
            int kr, hr;

            for (kr = 0; kr < cur_inv_info.num_reader_running; kr++)
            {
                if (cur_inv_info.reader_ipAddress[kr] == t_ipAddress)
                {
                    cur_inv_info.reader_tagCount[kr]++;
                    break;
                }
            }
            if ((kr == cur_inv_info.num_reader_running) && (kr < 100))
            {
                cur_inv_info.reader_ipAddress[kr] = t_ipAddress;
                cur_inv_info.reader_tagCount[kr]++;
                cur_inv_info.num_reader_running++;
            }

            if (DateTime.Now > TagCountTimer)
            {
                hr = 0;
                for (hr = 0; hr < cur_inv_info.num_reader_running; hr++)
                {
                    cur_inv_info.reader_ipAddress_hold[hr] = cur_inv_info.reader_ipAddress[hr];
                    cur_inv_info.reader_tagCount_hold[hr] = cur_inv_info.reader_tagCount[hr];
                    cur_inv_info.reader_tagCount[hr] = 0;
                    cur_inv_info.reader_ipAddress[hr] = "";
                }
                cur_inv_info.num_reader_running_hold = cur_inv_info.num_reader_running;
                cur_inv_info.num_reader_running = 0;

                TagCountTimer = DateTime.Now.AddSeconds(1);
                return 1;
            }

            return 0;
        }


        //static object TagInventoryLock = new object();
        static void ReaderXP_TagInventoryEvent(object sender, CSLibrary.Events.OnAsyncCallbackEventArgs e)
        {
            string str_reader_info_0;

            lock (tagList)
            {
                HighLevelInterface Reader = (HighLevelInterface)sender;

                // Display Tag info in Console Window
                // str_reader_info_0 = "Reader ID: " +Reader.Name+ "  ( Port= " +e.info.antennaPort.ToString()+ "  Pwr=" +Reader.AntennaList[0].PowerLevel.ToString()+ "  RSSI=" +e.info.rssi.ToString()+ ")";
                // sb_datalog.Append(str_reader_info_0);

                str_reader_info_0 = Reader.IPAddress + " , " + e.info.epc.ToString();
                str_reader_info_0 += " , Port= " + e.info.antennaPort.ToString();
                // str_reader_info_0 += " , Time : " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss UTC zzz") + ", ( Port= " +e.info.antennaPort.ToString()+ "  Pwr=" +Reader.AntennaList[0].PowerLevel.ToString()+ "  RSSI=" +e.info.rssi.ToString()+ " RName : " +Reader.Name+ ")" + "\r\n";
                str_reader_info_0 += " , Time : " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss UTC zzz") + ", ( Pwr=" + Reader.AntennaList[0].PowerLevel.ToString() + "  RSSI=" + e.info.rssi.ToString() + " RName : " + Reader.Name + ")" + "\r\n";

                str_dataLogEvt += str_reader_info_0;


                if (DataLog_Sample(Reader.IPAddress) == 1)
                {
                    string test_name = Reader.Name;         // For Test only

                    str_dataLogToFile = str_dataLogEvt;
                    str_dataLogEvt = "";
                }

                //save to buffer if not exist
                for (int i=0;i<num_readers;i++)
                {
                    if (rdr_info_data[i].str_ip_addr == Reader.IPAddress)
                    {
                        CTagResponse tag = new CTagResponse();
                        tag.epc = e.info.epc;
                        tag.pc = e.info.pc;
                        tag.antennaPort = e.info.antennaPort;
                        tag.rssi = e.info.rssi;
                        tag.timestamp = DateTime.Now;
                        if (tagList[i][e.info.antennaPort].ContainsKey(e.info.epc.ToString().ToUpper()))
                        {
                            tagList[i][e.info.antennaPort].Remove(e.info.epc.ToString().ToUpper());
                        }
                        else
                        {
                            CMqttEventMessage evtMsg = new CMqttEventMessage();
                            evtMsg.uuid = System.Guid.NewGuid().ToString();
                            evtMsg.deviceIp = Reader.IPAddress;
                            evtMsg.deviceName = rdr_info_data[i].mqtt_broker_clientid;
                            evtMsg.epc = tag.epc.ToString();
                            evtMsg.pc = tag.pc.ToString();
                            evtMsg.timestamp = tag.timestamp.ToString("yyyy/MM/dd HH:mm:ss");
                            evtMsg.readPoint = rdr_info_data[i].mqtt_port_name[tag.antennaPort];
                            evtMsg.rssi = tag.rssi.ToString();
                            evtMsg.type = EventType.TagPresence;
                            eventQueue.Enqueue(evtMsg);
                        }
                        tagList[i][e.info.antennaPort].Add(e.info.epc.ToString().ToUpper(), tag);
                    }
                }

            }
        }

        static void inspectTagBuffer()
        {
            while (true)
            {
                for (int i = 0; i < num_readers; i++)
                {
                    for (int port = 0; port < 4; port++)
                    {
                        List<CTagResponse> tagsToRemove = new List<CTagResponse>();
                        lock (tagList[i][port])
                        {
                            DateTime currentTime = DateTime.Now;
                            tagsToRemove = tagList[i][port].Values.ToList().Where(value => (currentTime - value.timestamp).TotalMilliseconds > rdr_info_data[i].tag_smoother).ToList();
                            //remove tags
                            //tagList[i][port].ToList().Where(pair => (currentTime - pair.Value.timestamp).TotalMilliseconds > rdr_info_data[i].tag_smoother).ToList().ForEach(pair => tagList[i][port].Remove(pair.Key));
                           
                            //remove absense tags
                            foreach (CTagResponse tag in tagsToRemove)
                            {
                                tagList[i][port].Remove(tag.epc.ToString());
                            }
    
                        }

                        foreach (CTagResponse tag in tagsToRemove)
                        {
                                //generate event on tag absence
                                CMqttEventMessage evtMsg = new CMqttEventMessage();
                                evtMsg.uuid = System.Guid.NewGuid().ToString();
                                evtMsg.deviceIp = rdr_info_data[i].str_ip_addr;
                                evtMsg.deviceName = rdr_info_data[i].mqtt_broker_clientid;
                                evtMsg.epc = tag.epc.ToString();
                                evtMsg.pc = tag.pc.ToString();
                                evtMsg.timestamp = tag.timestamp.ToString("yyyy/MM/dd HH:mm:ss");
                                evtMsg.readPoint = rdr_info_data[i].mqtt_port_name[port];
                                evtMsg.rssi = tag.rssi.ToString();
                                evtMsg.type = EventType.TagAbsence;
                                eventQueue.Enqueue(evtMsg);
                        }

                        Thread.Sleep(100);
                    }
                }
            }
        }


        static void sendEventToBroker()
        {
            while (true)
            {
                CMqttEventMessage message;
                lock (eventQueue)
                {
                    if (eventQueue.Count > 0)
                        message = eventQueue.Dequeue();
                    else
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                }

                for (int i=0;i<num_readers;i++)
                {
                    if(rdr_info_data[i].str_ip_addr == message.deviceIp)
                    {
                        string topic = String.Format("devices/{0}/messages/events/", message.deviceName, message.type);
                        MqttBrokerList[i].Publish(topic, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, Formatting.Indented)), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
                        break;
                    }
                }

            }
        }


        static void Main(string[] args)
        {
            ConsoleKeyInfo mkeyinfo;
            CSLibrary.Constants.Result ret;

            //Console.SetWindowSize(Console.LargestWindowWidth * 80 / 100, Console.LargestWindowHeight * 80 / 100);       // Set Window Size
            Console.WriteLine(str_sw_version);
            Console.WriteLine(" ");


            DataLog_InitMem();
            InitCpuUsage();       // Initialize the value of CPU Usage

            Console.WriteLine("Reading " + fileName + " ....");
            Console.WriteLine(" ");
            if (LoadConfigFile() == false)
            {
                Console.WriteLine(" ");
                Console.WriteLine("Press [Enter] key to exit.");
                Console.Read();
                return;
            }

            Console.WriteLine("Press [p] to pause reading tag info.");
            Console.WriteLine("Press [Esc] to quit after reader is connected.");
            Console.WriteLine(" ");

            // Connect to mqtt broker
            for (int cnt = 0; cnt < num_readers; cnt++)
            {
                if (rdr_info_data[cnt].mqtt_broker_enable)
                {
                    //connect to mqtt broker
                    MqttClient client = new MqttClient(rdr_info_data[cnt].mqtt_broker_ip);
                    client.Connect(rdr_info_data[cnt].mqtt_broker_clientid);
                    MqttBrokerList.Add(client);
                }
            }

            //start thread that generates the tagAbsence event
            tagInspectThread.Start();
            mqttEventThread.Start();

            // Connect Multi Reader
            for (int cnt = 0; cnt < num_readers; cnt++)
            {
                HighLevelInterface Reader = new HighLevelInterface();

                Console.WriteLine("Connecting to reader with IP: {0} ...", rdr_info_data[cnt].str_ip_addr);
                Console.WriteLine(" ");
                Console.WriteLine(" ");
                if ((ret = Reader.Connect(rdr_info_data[cnt].str_ip_addr, 9000)) != CSLibrary.Constants.Result.OK)
                {
                    Reader.Disconnect();
                    Console.WriteLine(String.Format("Can not connect Reader,  IP <" + rdr_info_data[cnt].str_ip_addr + "> StartupReader Failed{0}", ret));
                }
                else
                {
                    Console.WriteLine(String.Format("Reader connect success,  IP <" + rdr_info_data[cnt].str_ip_addr + ">"));

                    Reader.OnStateChanged += new EventHandler<CSLibrary.Events.OnStateChangedEventArgs>(ReaderXP_StateChangedEvent);
                    Reader.OnAsyncCallback += new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(ReaderXP_TagInventoryEvent);

                    ReaderList.Add(Reader);
                }
            }

            // Set Reader Configuration
            foreach (HighLevelInterface Reader in ReaderList)
            {
                // Get the index from the reader List with reader IP
                int idxList = 0;
                while ((Reader.IPAddress != rdr_info_data[idxList].str_ip_addr) && (idxList < ReaderList.Count))
                    idxList++;

                CSLibrary.Structures.DynamicQParms QParms = new CSLibrary.Structures.DynamicQParms();
                QParms.maxQValue = 15;
                QParms.minQValue = 0;
                QParms.retryCount = 7;
                QParms.startQValue = 7;                                                  // Set Start Q
                QParms.thresholdMultiplier = 1;
                // QParms.toggleTarget = 1;
                QParms.toggleTarget = (ushort)rdr_info_data[idxList].init_toggleTarget;     // Set Toggle Target
                Reader.SetSingulationAlgorithmParms(CSLibrary.Constants.SingulationAlgorithm.DYNAMICQ, QParms);
                // CSLibrary.Structures.SingulationAlgorithmParms Params = new CSLibrary.Structures.SingulationAlgorithmParms();

                Reader.SetTagGroup(CSLibrary.Constants.Selected.ALL, CSLibrary.Constants.Session.S0, CSLibrary.Constants.SessionTarget.A);



                // ............................ Check Reader OEM Type
                uint t_oem_data1, t_oem_data2;
                bool t_oem_data3;
                t_oem_data1 = Reader.OEMCountryCode;
                t_oem_data2 = (uint)Reader.OEMDeviceType;
                t_oem_data3 = Reader.IsFixedChannelOnly;

                if (Reader.OEMCountryCode > 8)
                {
                    Console.Write("Reader OEM Country Code Error : " + Reader.OEMCountryCode);
                    return;
                }
                else
                {
                    Console.Write(Reader.IPAddress + " : Reader Country Code -{0} ", Reader.OEMCountryCode);
                    Console.Write("\r\n");
                }

                int t_csize = Convert.ToInt32(Tab_OEM_Country_Code[Reader.OEMCountryCode, 0]);
                int ct = 0;
                for (ct = 0; ct < t_csize; ct++)
                {
                    if (RegionArray[(int)rdr_info_data[idxList].regionCode] == Tab_OEM_Country_Code[Reader.OEMCountryCode, ct])
                    {
                        // -----------------------------Check OEM Fixed Frequency Channel / Frequency Hopping Channels
                        if (rdr_info_data[idxList].IsNonHopping_usrDef == true)
                        {
                            // Fixed Channel for reader with Country Code -1, -3, -8
                            if (Reader.IsFixedChannelOnly == true)
                            {
                                Reader.SetFixedChannel(rdr_info_data[idxList].regionCode, 1, LBT.OFF);              // Fixed Frequency Channel
                                Console.Write(Reader.IPAddress + " : Fixed Frequency Channel is applied\r\n");
                                Console.Write("\r\n");
                            }
                            else
                            {
                                Console.Write(Reader.IPAddress + " : Reader cannot be set to Fixed Frequency Mode");
                                Console.Write("\r\n");
                                flag_error = -30;
                            }
                        }
                        else
                        {
                            if (Reader.IsFixedChannelOnly == false)
                            {
                                Reader.SetHoppingChannels(rdr_info_data[idxList].regionCode);                   // Frequency Hopping Channel
                                Console.Write(Reader.IPAddress + " : Frequency Hopping is applied\r\n");
                                Console.Write("\r\n");
                            }
                            else
                            {
                                Console.Write(Reader.IPAddress + " : Reader cannot be set to Frequency Hopping Mode");
                                Console.Write("\r\n");
                                flag_error = -30;
                            }
                        }
                        break;
                    }
                }


                if (ct == t_csize)
                {
                    Console.Write(Reader.IPAddress + " : Reader with N = -" + Reader.OEMCountryCode + " does not support selected country!!");
                    flag_error = -31;
                }

                if (flag_error != 0)
                {
                    return;         // Exit Program
                }
                else
                {
                    // ------------------------------  Set Antenna Port State and Configuration
                    CSLibrary.Structures.AntennaPortStatus t_AntennaPortStatus = new CSLibrary.Structures.AntennaPortStatus();
                    CSLibrary.Structures.AntennaPortConfig t_AntennaPortConfig = new CSLibrary.Structures.AntennaPortConfig();
                    for (uint t_port = 0; t_port < 4; t_port++)
                    {
                        if (rdr_info_data[idxList].antPort_state[t_port] == false)
                        {
                            Reader.SetAntennaPortState(t_port, AntennaPortState.DISABLED);
                        }
                        else
                        {

                            Reader.GetAntennaPortConfiguration(t_port, ref t_AntennaPortConfig);       // Get Antenna Port Status
                            t_AntennaPortConfig.powerLevel = rdr_info_data[idxList].antPort_power[t_port];
                            t_AntennaPortConfig.dwellTime = rdr_info_data[idxList].antPort_dwell[t_port];       // Dwell Time
                            Reader.SetAntennaPortConfiguration(t_port, t_AntennaPortConfig);                // Set Antenna Configuration


                            Reader.GetAntennaPortStatus(t_port, t_AntennaPortStatus);
                            t_AntennaPortStatus.profile = rdr_info_data[idxList].antPort_Pofile[t_port];            // Set Current Link Profile
                            t_AntennaPortStatus.enableLocalProfile = true;                                          // Enable Current Link Profile
                            t_AntennaPortStatus.inv_algo = rdr_info_data[idxList].antPort_QAlg[t_port];             // Set Inventory Algorithm
                            t_AntennaPortStatus.enableLocalInv = true;                                              // Enable Local Inventory
                            t_AntennaPortStatus.startQ = (uint)rdr_info_data[idxList].antPort_startQValue[t_port];      // Set Start Q Value

                            // if (IsFreqHoppingRegion[(int)rdr_info_data[idxList].regionCode] == 0)
                            if (Reader.IsFixedChannelOnly)
                            {
                                t_AntennaPortStatus.enableLocalFreq = true;
                                t_AntennaPortStatus.freqChn = rdr_info_data[idxList].freq_channel[t_port];          // Set Fixed Frequency Channel for each port
                            }
                            else
                                t_AntennaPortStatus.enableLocalFreq = false;

                            Reader.SetAntennaPortStatus(t_port, t_AntennaPortStatus);

                            Reader.SetAntennaPortState(t_port, AntennaPortState.ENABLED);                               // Enable Antenna
                        }
                    }

                    if ((rdr_info_data[idxList].antSeqMode == AntennaSequenceMode.SEQUENCE) || (rdr_info_data[idxList].antSeqMode == AntennaSequenceMode.SEQUENCE_SMART_CHECK))
                    {
                        Reader.SetAntennaSequence(rdr_info_data[idxList].antPort_seqTable, rdr_info_data[idxList].antPort_seqTableSize, rdr_info_data[idxList].antSeqMode);
                    }
                    else
                        Reader.SetAntennaSequence(rdr_info_data[idxList].antPort_seqTable, 0, rdr_info_data[idxList].antSeqMode);


                    /*
                    //.......................... Read the Antenna Port Configuration and Status
                                        for (uint t_port = 0; t_port < 16; t_port++)
                                        {
                                            Reader.GetAntennaPortConfiguration(t_port, ref t_AntennaPortConfig);       // Get Antenna Port Status
                                            Reader.GetAntennaPortStatus(t_port, t_AntennaPortStatus);                   // Get Antenna Port Status
                                        }

                                        byte[] t_seqarray = new byte[MAX_PORT_SEQ_NUM];
                                        uint t_seqSize = new uint();
                                        AntennaSequenceMode t_antMode = new AntennaSequenceMode();
                                        Reader.GetAntennaSequence(t_seqarray, ref t_seqSize, ref t_antMode);
                    */

                    Reader.SetOperationMode(CSLibrary.Constants.RadioOperationMode.CONTINUOUS);

                    Reader.Options.TagRanging.flags = CSLibrary.Constants.SelectFlags.ZERO;

                    // Reader.Options.TagRanging.multibanks = 2;
                    Reader.Options.TagRanging.multibanks = rdr_info_data[idxList].init_multiBanks;
                    Reader.Options.TagRanging.bank1 = CSLibrary.Constants.MemoryBank.TID;
                    Reader.Options.TagRanging.offset1 = 0;
                    Reader.Options.TagRanging.count1 = 2;
                    Reader.Options.TagRanging.bank2 = CSLibrary.Constants.MemoryBank.USER;
                    Reader.Options.TagRanging.offset2 = 0;
                    Reader.Options.TagRanging.count2 = 2;
                }

            }

            // Start Inventory
            foreach (HighLevelInterface Reader in ReaderList)
            {
                Reader.StartOperation(CSLibrary.Constants.Operation.TAG_RANGING, false);
            }

            flag_display = true;
            flag_endProg = false;
            do
            {
                // Thread.Sleep(100);
                CalculateCpuUsage();

                //...................... Process Data Log
                DataLog_Process();

                //....................... Handle Key Event
                bool t_keyTrigger = Console.KeyAvailable;
                if (t_keyTrigger)
                {
                    mkeyinfo = Console.ReadKey();       // Program Loop here, press“Enter”to exit
                    switch (mkeyinfo.Key)
                    {
                        case ConsoleKey.P:
                            if (flag_display == true)
                            {
                                flag_display = false;

                                // Stop Inventory
                                foreach (HighLevelInterface Reader in ReaderList)
                                {
                                    Reader.StopOperation(true);
                                }

                                Thread.Sleep(200);
                                Console.WriteLine(" ");
                                Console.WriteLine("Pause.");
                                Console.WriteLine("Press [r] to resume reading tag info.");
                                Console.WriteLine("Press [Esc] to quit.");
                            }
                            break;

                        case ConsoleKey.R:
                            if (flag_display == false)
                            {
                                // Start Inventory
                                foreach (HighLevelInterface Reader in ReaderList)
                                {
                                    Reader.StartOperation(CSLibrary.Constants.Operation.TAG_RANGING, false);
                                }

                                Console.WriteLine(" ");
                                Console.WriteLine("Resume.");
                                Console.WriteLine("Press [p] to pause reading tag info.");
                                Console.WriteLine("Press [Esc] to quit.");
                                Thread.Sleep(1300);
                                flag_display = true;
                            }
                            break;

                        case ConsoleKey.Escape:
                            flag_endProg = true;
                            break;

                        default:
                            Thread.Sleep(100);
                            if (flag_display == false)
                                Console.WriteLine(" ");
                            break;
                    }
                }

            } while (flag_endProg == false);

            flag_display = false;
            Thread.Sleep(200);
            Console.WriteLine("Quit application.  Reader Stop reading");
            Console.WriteLine(" ");

            // Stop Inventory
            foreach (HighLevelInterface Reader in ReaderList)
            {
                Reader.StopOperation(true);
            }

            Console.WriteLine("Closing Window now ....");
            Thread.Sleep(5000);

            // Disconnect Reader
            foreach (HighLevelInterface Reader in ReaderList)
            {
                Reader.OnAsyncCallback -= new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(ReaderXP_TagInventoryEvent);
                Reader.OnStateChanged -= new EventHandler<CSLibrary.Events.OnStateChangedEventArgs>(ReaderXP_StateChangedEvent);

                Reader.Disconnect();
            }
        }
    }
}
