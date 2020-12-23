//---------------------------------------------------------------------------------
// Copyright (c) December 2020, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsNetwork.Models
{
	using CommandLine;

	public class CommandLineOptions
	{
		[Option('u', "APIbaseURL", Required = false, HelpText = "TTN Restful API URL.")]
		public string ApiBaseUrl { get; set; }

		[Option('K', "APIKey", Required = true, HelpText = "TTN Restful API APIkey")]
		public string ApiKey { get; set; }

		[Option('P', "APIApplicationID", Required = true, HelpText = "TTN Restful API ApplicationID")]
		public string ApiApplicationID { get; set; }

		[Option('D', "DeviceListPageSize", Required = true, HelpText = "The size of the pages used to retrieve EndDevice configuration")]
		public int DevicePageSize { get; set; }

		[Option('S', "MQTTServerName", Required = true, HelpText = "TTN MQTT API server name")]
		public string MqttServerName { get; set; }

		[Option('A', "MQTTAccessKey", Required = true, HelpText = "TTN MQTT API access key")]
		public string MqttAccessKey { get; set; }

		[Option('Q', "MQTTApplicationID", Required = true, HelpText = "TTN MQTT API ApplicationID")]
		public string MqttApplicationID { get; set; }

		[Option('C', "MQTTClientName", Required = true, HelpText = "TTN MQTT API Client ID")]
		public string MqttClientID { get; set; }

		[Option('Z', "AzureIoTHubConnectionString", Required = true, HelpText = "Azure IoT Hub Connection string")]
		public string AzureIoTHubconnectionString { get; set; }
	}
}