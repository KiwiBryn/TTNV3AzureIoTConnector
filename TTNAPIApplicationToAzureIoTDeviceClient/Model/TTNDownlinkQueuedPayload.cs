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
namespace devMobile.TheThingsNetwork.Models
{
   using System.Collections.Generic;
   using System.Runtime.Serialization;

   using Newtonsoft.Json;
   using Newtonsoft.Json.Linq;

   public class DownlinkQueued
   {
      [JsonProperty("f_port")]
      public int Port { get; set; }

      [JsonProperty("frm_payload")]
      public string PayloadRaw { get; set; }

      [JsonProperty("decoded_payload")]
      public JToken PayloadDecoded { get; set; }

      [JsonProperty("Priority")]
      public string priority { get; set; }

      [JsonProperty("confirmed")]
      public bool Confirmed { get; set; }

      [JsonProperty("correlation_ids")]
      public List<string> CorrelationIds { get; set; }
   }

   public class DownlinkQueuedPayload
   {
      [JsonProperty("end_device_ids")]
      public EndDeviceIds EndDeviceIds { get; set; }
      [JsonProperty("correlation_ids")]
      public List<string> CorrelationIds { get; set; }
      [JsonProperty("downlink_queued")]
      public DownlinkQueued DownlinkQueued { get; set; }
   }
}



