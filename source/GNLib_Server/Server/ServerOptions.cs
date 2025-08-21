using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace GameNetServer
{
    public sealed class ServerOptions
    {
        public IPAddress BindAddress { get; set; } = IPAddress.Any;
        public int Port { get; set; } = 9000;

        public bool UseTls { get; set; } = true;
        public X509Certificate2 ServerCertificate { get; set; }  // REQUIRED if UseTls
        public bool AllowInsecureForTesting { get; set; } = false;

        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(15);
    }
}
