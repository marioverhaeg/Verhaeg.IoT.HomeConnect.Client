# Introduction
Home Connect allows you to control your BSH (Bosch, Siemens, and others) devices over the internet. You can find more information on Home Connect here: https://www.home-connect.com/global.

I have developed this software using the information that is available on: https://developer.home-connect.com/.

Verhaeg.IoT.HomeConnect.Client allows you to communicate with your appliance. At this moment it provides authentication using 
the oAuth device authentication flow: https://oauth.net/2/device-flow/ and you are able to retrieve information on your washer appliance from the Home Connect cloud. 

I've only tested the client on the Bosch WAXH2K75NL/01, but it should work with other appliances as well. I will test its capabilities for a dishwasher as soon as I have this up and running (probably by the end of 2022).

# Usage
## Authenticate to the cloud service
Start the AuthorizationManager to authenticate to the HomeConnect cloud using the device-flow.
```c#
AuthorizationManager.Start(uri, authentication_uri, token_uri, client_id, client_secret, devicename, ha_id);
```
The AuthorizationManager will write the device-flow login URL into its log-file:
```
 [INF] Received response: {
  "device_code": "xxx",
  "expires_in": 600,
  "interval": 5,
  "user_code": "xxx-xxxx",
  "verification_uri": "https://api.home-connect.com/security/oauth/device_verify",
  "verification_uri_complete": "https://api.home-connect.com/security/oauth/device_verify?user_code=xxx-xxxx"
}
```
Use the verification_uri_complete to enable the AuthorizationManager to login.

## Get events
Start the EventManager and subscribe to its events:
```c#
EventManager.Start(uri, devicename, haId);
EventManager.Instance().applianceEvent += Worker_applianceEvent;
```

## Start Program
Start the currently selected program on a device:
```c#
KeyValuePair<string,string> kvp = new KeyValuePair<string,string>("StartSelectedProgram", haId);
CommandManager.Instance().Write(kvp);
```

