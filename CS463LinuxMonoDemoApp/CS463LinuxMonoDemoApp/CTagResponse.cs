using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CSLibrary.Structures;

namespace CS463LinuxMonoDemoApp
{
    public class CTagResponse
    {
        public DateTime timestamp;
        public S_EPC epc;
        public S_PC pc;
        public uint antennaPort;
        public float rssi;
    }
}
