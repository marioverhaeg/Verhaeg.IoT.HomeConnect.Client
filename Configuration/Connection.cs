using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Verhaeg.IoT.HomeConnect.Client.Configuration
{
    public class Connection
    {
        public string uri { get; set; }
        public string authentication_uri { get; set; }
        public string token_uri { get; set; }
        public string client_id { get; set; }
        public string client_secret { get; set; }
        public string device_name { get; set; }
        public string ha_id { get; set; }

        public Connection(string uri, string authentication_uri, string token_uri, string client_id,
            string client_secret, string device_name, string ha_id)
        {
            this.uri = uri;
            this.authentication_uri = authentication_uri;
            this.token_uri = token_uri;
            this.client_id = client_id;
            this.client_secret = client_secret;
            this.device_name = device_name;
            this.ha_id = ha_id;
        }
    }
}
