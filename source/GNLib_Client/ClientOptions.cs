using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameNetClient
{ 
    public sealed class ClientOptions
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9000;

        public bool UseTls { get; set; } = true;
        public string TlsTargetHost { get; set; } = "localhost"; // CN/SNI for cert validation
        public bool AllowInvalidServerCertForTesting { get; set; } = false;
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    }
}
