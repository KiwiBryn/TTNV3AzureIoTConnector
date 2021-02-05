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
namespace devMobile.TheThingsNetwork.Models.DownlinkAckRaw
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

   public class DownlinkAck
   {
      public string session_key_id { get; set; }
      public int f_port { get; set; }
      public int f_cnt { get; set; }
      public string frm_payload { get; set; }
      public bool confirmed { get; set; }
      public string priority { get; set; }
      public List<string> correlation_ids { get; set; }
   }

   public class Root
   {
      public EndDeviceIds end_device_ids { get; set; }
      public List<string> correlation_ids { get; set; }
      public String received_at { get; set; }
      public DownlinkAck downlink_ack { get; set; }
   }
}