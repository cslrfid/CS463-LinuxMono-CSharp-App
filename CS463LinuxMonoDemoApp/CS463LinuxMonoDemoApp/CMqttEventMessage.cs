using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CS463LinuxMonoDemoApp
{
    public enum EventType { TagPresence, TagAbsence, TagInventory }

    public class CMqttEventMessage
    {
        public string uuid;
        public string deviceIp;
        public string deviceName;
        public string epc;
        public string pc;
        public string timestamp;
        public string readPoint;
        public string rssi;
        [JsonConverter(typeof(StringEnumConverter))]
        public EventType type;
    }
}
