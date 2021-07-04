using System;
using System.Collections.Generic;
using System.Text;

namespace Verhaeg.IoT.HomeConnect.Client
{
    public class Device_Authentication
    {
        // oAuth device flow data
        public string device_code;
        public int expires_in;
        public int interval;
        public string user_code;
        public string verification_uri;
        public string verification_uri_complete;
    }
}
