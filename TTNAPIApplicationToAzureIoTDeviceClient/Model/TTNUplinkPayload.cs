//---------------------------------------------------------------------------------
// Copyright (c) September 2020, devMobile Software
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
   using System;
   using System.Collections.Generic;
   using Newtonsoft.Json;
   using Newtonsoft.Json.Linq;

   // Production version of classes for unpacking HTTP payload https://json2csharp.com/
   // In inital version from https://www.thethingsindustries.com/docs/reference/data-formats/
   public class GatewayIds
   {
      [JsonProperty("gateway_id")]
      public string GatewayId { get; set; }

      [JsonProperty("eui")]
      public string GatewayEui { get; set; }
   }

   public class RxMetadata
   {
      [JsonProperty("gateway_ids")]
      public GatewayIds GatewayIds { get; set; }

      [JsonProperty("time")]
      public DateTime ReceivedAtUtc { get; set; }

      [JsonProperty("timestamp")]
      public ulong Timestamp { get; set; }

      [JsonProperty("rssi")]
      public int Rssi { get; set; }

      [JsonProperty("channel_rssi")]
      public int ChannelRssi { get; set; }

      [JsonProperty("snr")]
      public double Snr { get; set; }

      [JsonProperty("uplink_token")]
      public string UplinkToken { get; set; }

      [JsonProperty("channel_index")]
      public int ChannelIndex { get; set; }
   }

   public class Lora
   {
      [JsonProperty("bandwidth")]
      public int Bandwidth { get; set; }

      [JsonProperty("spreading_factor")]
      public int SpreadingFactor { get; set; }
   }

   public class DataRate
   {
      [JsonProperty("lora")]
      public Lora Lora { get; set; }
   }

   public class Settings
   {
      [JsonProperty("data_rate")]
      public DataRate DataRate { get; set; }

      [JsonProperty("data_rate_index")]
      public int DataRateIndex { get; set; }

      [JsonProperty("coding_rate")]
      public string CodingRate { get; set; }

      [JsonProperty("frequency")]
      public string Frequency { get; set; }

      [JsonProperty("gateway_channel_index")]
      public int GatewayChannelIndex { get; set; }

      [JsonProperty("device_channel_index")]
      public int DeviceChannelIndex { get; set; }
   }

   public class User
   {
      [JsonProperty("latitude")]
      public double Latitude { get; set; }

      [JsonProperty("longitude")]
      public double Longitude { get; set; }

      [JsonProperty("altitude")]
      public int Altitude { get; set; }

      [JsonProperty("source")]
      public string Source { get; set; }
   }

   public class Locations
   {
      public User user { get; set; }
   }

   public class UplinkMessage
   {
      [JsonProperty("session_key_id")]
      public string SessionKeyId { get; set; }

      [JsonProperty("f_cnt")]
      public int Counter { get; set; }

      [JsonProperty("f_port")]
      public int? Port { get; set; }

      [JsonProperty("decoded_payload")]
      public JToken PayloadDecoded { get; set; }

      [JsonProperty("frm_payload")]
      public string PayloadRaw { get; set; }

      [JsonProperty("rx_metadata")]
      public List<RxMetadata> RXMetadata { get; set; }

      [JsonProperty("settings")]
      public Settings Settings { get; set; }

      [JsonProperty("received_at")]
      public DateTime ReceivedAtUtc { get; set; }

      [JsonProperty("consumed_airtime")]
      public string ConsumedAirtime { get; set; }

      public Locations locations { get; set; }
   }

   public class PayloadUplink
   {
      [JsonProperty("end_device_ids")]
      public EndDeviceIds EndDeviceIds { get; set; }

      [JsonProperty("correlation_ids")]
      public List<string> CorrelationIds { get; set; }

      [JsonProperty("received_at")]
      public DateTime ReceivedAtUtc { get; set; }

      [JsonProperty("uplink_message")]
      public UplinkMessage UplinkMessage { get; set; }

      [JsonProperty("simulated")]
      public bool Simulated { get; set; }
   }
}
