//---------------------------------------------------------------------------------
// Copyright (c) February 2021, devMobile Software
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
namespace devMobile.TheThingsNetwork.Models.UplinkRaw
{
   using System;
   using System.Collections.Generic;

   // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
   public class ApplicationIds
   {
      public string application_id { get; set; }
   }

   public class EndDeviceIds
   {
      public string device_id { get; set; }
      public ApplicationIds application_ids { get; set; }
      public string dev_eui { get; set; }
      public string join_eui { get; set; }
      public string dev_addr { get; set; }
   }

   public class DecodedPayload
   {
      public double temperature { get; set; }
      public double luminosity { get; set; }
   }

   public class GatewayIds
   {
      public string gateway_id { get; set; }
      public string eui { get; set; }
   }

   public class RxMetadata
   {
      public GatewayIds gateway_ids { get; set; }
      public DateTime time { get; set; }
      public long timestamp { get; set; }
      public int rssi { get; set; }
      public int channel_rssi { get; set; }
      public int snr { get; set; }
      public string uplink_token { get; set; }
      public int channel_index { get; set; }
   }

   public class Lora
   {
      public int bandwidth { get; set; }
      public int spreading_factor { get; set; }
   }

   public class DataRate
   {
      public Lora lora { get; set; }
   }

   public class Settings
   {
      public DataRate data_rate { get; set; }
      public int data_rate_index { get; set; }
      public string coding_rate { get; set; }
      public string frequency { get; set; }
      public long timestamp { get; set; }
      public DateTime time { get; set; }
   }

   public class User
   {
      public double latitude { get; set; }
      public double longitude { get; set; }
      public int altitude { get; set; }
      public string source { get; set; }
   }

   public class Locations
   {
      public User user { get; set; }
   }

   public class UplinkMessage
   {
      public string session_key_id { get; set; }
      public int f_cnt { get; set; }
      public string frm_payload { get; set; }
      public DecodedPayload decoded_payload { get; set; }
      public List<RxMetadata> rx_metadata { get; set; }
      public Settings settings { get; set; }
      public string consumed_airtime { get; set; }
      public Locations locations { get; set; }
      public string received_at { get; set; }
   }

   public class Root
   {
      public EndDeviceIds end_device_ids { get; set; }
      public List<string> correlation_ids { get; set; }
      public string received_at { get; set; }
      public UplinkMessage uplink_message { get; set; }
   }
}